#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace UnityPackageImporter.Models;

internal sealed class PrefabCompositionDiagnostics
{
    internal HashSet<long> MaterializedLocalObjects { get; } = new();
    internal HashSet<long> DeferredMissingAliases { get; } = new();
}

internal static class UnityPrefabComposer
{
    private sealed record Document(long Id, int ClassId, YamlMappingNode Root, bool IsStripped = false)
    {
        internal YamlMappingNode Body => (YamlMappingNode)Root.Children.Values.Single();
        internal bool IsInstance => ClassId == 1001 && Root.Children.ContainsKey(new YamlScalarNode("PrefabInstance"));
    }

    private static readonly Regex Header = new(@"^--- !u!(?<class>\d+) &(?<id>-?\d+)(?<stripped> stripped)?\s*$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    internal static string Compose(string yaml, IReadOnlyDictionary<string, string> prefabPaths,
        IReadOnlyDictionary<string, IReadOnlyDictionary<long, int>>? modelObjects = null,
        PrefabCompositionDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(prefabPaths);
        var sources = new Dictionary<string, List<Document>>(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<long>();
        List<Document> Read(string text)
        {
            var result = new List<Document>();
            var headers = Header.Matches(text);
            var ids = new HashSet<long>();
            for (int index = 0; index < headers.Count; index++)
            {
                var header = headers[index];
                long id = long.Parse(header.Groups["id"].Value, CultureInfo.InvariantCulture);
                if (id == 0 || !ids.Add(id)) throw new InvalidDataException("Invalid or duplicate prefab document identity.");
                int start = header.Index + header.Length;
                int end = index + 1 < headers.Count ? headers[index + 1].Index : text.Length;
                var stream = new YamlStream();
                stream.Load(new StringReader(text.Substring(start, end - start)));
                if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode root || root.Children.Count != 1)
                    throw new InvalidDataException("Invalid Unity prefab document.");
                int classId = int.Parse(header.Groups["class"].Value, CultureInfo.InvariantCulture);
                UnityPrefabSchema.NormalizeDocument(root, classId);
                result.Add(new Document(id, classId, root, header.Groups["stripped"].Success));
                used.Add(id);
            }
            if (headers.Count == 0 && !string.IsNullOrWhiteSpace(text))
                throw new InvalidDataException("Unity prefab has no document headers.");
            return result;
        }

        var input = Read(yaml);
        foreach (var entry in prefabPaths.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            sources.Add(entry.Key, Read(File.ReadAllText(entry.Value)));
        long nextId = long.MaxValue;
        int expandedDocuments = 0;
        long Allocate()
        {
            while (!used.Add(nextId)) nextId--;
            return nextId--;
        }

        List<Document> Expand(List<Document> originals, HashSet<string> stack, int depth, IReadOnlyList<(long Id, int ClassId)>? requests = null)
        {
            if (depth > 64) throw new InvalidDataException("Prefab nesting exceeds the supported depth.");
            expandedDocuments = checked(expandedDocuments + originals.Count);
            if (expandedDocuments > 200000) throw new InvalidDataException("Prefab expansion exceeds the supported object count.");
            var documents = originals.Select(document => new Document(document.Id, document.ClassId,
                (YamlMappingNode)Clone(document.Root), document.IsStripped)).ToList();
            foreach (var request in requests ?? Array.Empty<(long Id, int ClassId)>())
            {
                if (documents.Any(document => document.Id == request.Id)) continue;
                var candidates = documents.Where(document => document.IsInstance)
                    .SelectMany(instance => sources.TryGetValue(Scalar(Mapping(instance.Body, "m_SourcePrefab"), "guid"), out var native)
                        ? native.Where(document => (request.ClassId == 0 || document.ClassId == request.ClassId) &&
                            PrefabVariantResolver.ComputeDerivedFileId(instance.Id, document.Id) == request.Id)
                            .Select(document => (Instance: instance, Source: document))
                        : Enumerable.Empty<(Document Instance, Document Source)>()).ToArray();
                if (candidates.Length > 1) throw new InvalidDataException("Ambiguous implicit native prefab identity.");
                if (candidates.Length == 1)
                {
                    var candidate = candidates[0];
                    var reference = Ref(candidate.Source.Id);
                    Set(reference, "guid", new YamlScalarNode(Scalar(Mapping(candidate.Instance.Body, "m_SourcePrefab"), "guid")));
                    Set(reference, "type", new YamlScalarNode("3"));
                    string type = candidate.Source.Root.Children.Keys.OfType<YamlScalarNode>().Single().Value!;
                    documents.Add(new Document(request.Id, candidate.Source.ClassId, new YamlMappingNode
                    {
                        { type, new YamlMappingNode { { "m_CorrespondingSourceObject", reference }, { "m_PrefabInstance", Ref(candidate.Instance.Id) }, { "m_PrefabAsset", Ref(0) } } }
                    }, true));
                }
                else EnsureDerivedObject(documents, request.Id, request.ClassId, modelObjects);
            }
            foreach (var instance in documents.Where(document => document.IsInstance).ToArray())
            {
                string guid = Scalar(Mapping(instance.Body, "m_SourcePrefab"), "guid");
                if (!sources.TryGetValue(guid, out var source)) continue;
                if (!stack.Add(guid)) throw new InvalidDataException("Cyclic native prefab reference: " + guid);
                var aliases = documents.Where(document => Reference(document.Body, "m_PrefabInstance") == instance.Id &&
                    Reference(document.Body, "m_CorrespondingSourceObject") != 0).ToArray();
                if (aliases.Any(alias => !string.Equals(Scalar(Mapping(alias.Body, "m_CorrespondingSourceObject"), "guid"), guid, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Native prefab alias refers to a different source asset.");
                var required = aliases.Select(alias => (Reference(alias.Body, "m_CorrespondingSourceObject"), alias.ClassId))
                    .Concat(Sequence(Mapping(instance.Body, "m_Modification"), "m_Modifications").OfType<YamlMappingNode>()
                        .Select(change => (Reference(change, "target"), ClassForProperty(Scalar(change, "propertyPath"))))).Distinct().ToArray();
                var expanded = Expand(source, stack, depth + 1, required);
                stack.Remove(guid);
                foreach (var alias in aliases)
                    EnsureDerivedObject(expanded, Reference(alias.Body, "m_CorrespondingSourceObject"), alias.ClassId, modelObjects);
                foreach (var change in Sequence(Mapping(instance.Body, "m_Modification"), "m_Modifications").OfType<YamlMappingNode>())
                    EnsureDerivedObject(expanded, Reference(change, "target"), ClassForProperty(Scalar(change, "propertyPath")), modelObjects);
                var map = new Dictionary<long, long>();
                foreach (var document in expanded)
                {
                    var matches = aliases.Where(alias => Reference(alias.Body, "m_CorrespondingSourceObject") == document.Id &&
                        string.Equals(Scalar(Mapping(alias.Body, "m_CorrespondingSourceObject"), "guid"), guid, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (matches.Length > 1) throw new InvalidDataException("Multiple stripped aliases claim one native prefab object.");
                    if (matches.Length == 1 && matches[0].ClassId != document.ClassId)
                        throw new InvalidDataException("Stripped prefab alias has a different Unity class than its source.");
                    map.Add(document.Id, matches.Length == 1 ? matches[0].Id : Allocate());
                }
                foreach (var alias in aliases)
                    if (!map.ContainsKey(Reference(alias.Body, "m_CorrespondingSourceObject")))
                        throw new InvalidDataException("Native prefab stripped object cannot resolve its source identity: " + alias.Id);
                var copies = expanded.Select(document => new Document(map[document.Id], document.ClassId,
                    (YamlMappingNode)Remap(document.Root, map), document.IsStripped)).ToList();
                var bySource = expanded.Select((document, index) => (document.Id, Copy: copies[index])).ToDictionary(pair => pair.Id, pair => pair.Copy);
                var modification = Mapping(instance.Body, "m_Modification");
                long parent = Reference(modification, "m_TransformParent");
                foreach (var document in copies)
                {
                    if (document.ClassId == 4 && Get(document.Body, "m_Father") != null && Reference(document.Body, "m_Father") == 0)
                        Set(document.Body, "m_Father", Ref(parent));
                    if (document.IsInstance && Mapping(document.Body, "m_Modification") is { } nested && Reference(nested, "m_TransformParent") == 0)
                        Set(nested, "m_TransformParent", Ref(parent));
                }
                foreach (var node in Sequence(modification, "m_Modifications"))
                {
                    if (node is not YamlMappingNode change) throw new InvalidDataException("Invalid prefab modification.");
                    var target = Mapping(change, "target");
                    if (!string.Equals(Scalar(target, "guid"), guid, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Native prefab override target is unavailable: " + Reference(change, "target"));
                    string path = Scalar(change, "propertyPath");
                    if (path == "m_RootOrder") continue;
                    if (!bySource.TryGetValue(Reference(change, "target"), out var document))
                    {
                        var derived = FindDerivedTarget(expanded, Reference(change, "target"), modelObjects);
                        if (derived == null) throw new InvalidDataException("Native prefab override target is unavailable: " + Reference(change, "target"));
                        var forwarded = (YamlMappingNode)Clone(change);
                        var reference = Ref(derived.Value.Target);
                        Set(reference, "guid", new YamlScalarNode(derived.Value.Guid));
                        Set(reference, "type", new YamlScalarNode("3"));
                        Set(forwarded, "target", reference);
                        EnsureSequence(Mapping(bySource[derived.Value.Instance.Id].Body, "m_Modification")!, "m_Modifications").Add(forwarded);
                        continue;
                    }
                    if (Reference(document.Body, "m_PrefabInstance") != 0 && Reference(document.Body, "m_CorrespondingSourceObject") != 0)
                    {
                        long owner = Reference(document.Body, "m_PrefabInstance");
                        var nested = copies.SingleOrDefault(candidate => candidate.Id == owner && candidate.IsInstance)
                            ?? throw new InvalidDataException("Stripped prefab override owner is unavailable.");
                        var forwarded = (YamlMappingNode)Clone(change);
                        Set(forwarded, "target", Clone(Get(document.Body, "m_CorrespondingSourceObject")!));
                        var nestedChanges = Mapping(nested.Body, "m_Modification") ?? throw new InvalidDataException("Nested prefab modification is unavailable.");
                        EnsureSequence(nestedChanges, "m_Modifications").Add(forwarded);
                    }
                    else
                    {
                        var reference = Mapping(change, "objectReference");
                        YamlNode value = reference != null && (Reference(change, "objectReference") != 0 || !string.IsNullOrEmpty(Scalar(reference, "guid")))
                            ? Clone(reference) : new YamlScalarNode(Scalar(change, "value"));
                        ApplyProperty(document.Body, path, value);
                    }
                }
                ApplyStructuralChanges(modification, documents, copies, bySource, map, guid);
                documents.Remove(instance);
                foreach (var alias in aliases) documents.Remove(alias);
                documents.AddRange(copies);
            }
            return documents;
        }

        var composed = Expand(input, new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0);
        MaterializeLocalSnapshots(composed, prefabPaths, modelObjects, diagnostics);
        var output = new StringBuilder("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
        foreach (var document in composed)
        {
            using var writer = new StringWriter(CultureInfo.InvariantCulture);
            new YamlStream(new YamlDocument(document.Root)).Save(writer, false);
            string body = Regex.Replace(writer.ToString(), @"(?m)^(?:---|\.\.\.)\s*$", "").Trim();
            output.Append("--- !u!").Append(document.ClassId).Append(" &").Append(document.Id)
                .Append(document.IsStripped ? " stripped\n" : "\n").Append(body).Append('\n');
        }
        return output.ToString();
    }
    private static void MaterializeLocalSnapshots(List<Document> documents,
        IReadOnlyDictionary<string, string> prefabs,
        IReadOnlyDictionary<string, IReadOnlyDictionary<long, int>>? models,
        PrefabCompositionDiagnostics? diagnostics)
    {
        var indexed = documents.ToDictionary(document => document.Id);
        var missing = documents.Where(document => document.IsInstance &&
            !string.IsNullOrEmpty(Scalar(Mapping(document.Body, "m_SourcePrefab"), "guid")) &&
            !prefabs.ContainsKey(Scalar(Mapping(document.Body, "m_SourcePrefab"), "guid")) &&
            models?.ContainsKey(Scalar(Mapping(document.Body, "m_SourcePrefab"), "guid")) != true).ToArray();
        foreach (var instance in missing)
        {
            string guid = Scalar(Mapping(instance.Body, "m_SourcePrefab"), "guid");
            var owned = documents.Where(document => Reference(document.Body, "m_PrefabInstance") == instance.Id &&
                Reference(document.Body, "m_CorrespondingSourceObject") != 0).ToArray();
            var modification = Mapping(instance.Body, "m_Modification");
            if (owned.Length == 0 || owned.Any(document => document.IsStripped ||
                    !string.Equals(Scalar(Mapping(document.Body, "m_CorrespondingSourceObject"), "guid"), guid, StringComparison.OrdinalIgnoreCase) ||
                    !CompleteLocalDocument(document, indexed)) ||
                new[] { "m_AddedGameObjects", "m_AddedComponents", "m_RemovedGameObjects", "m_RemovedComponents" }
                    .Any(key => Sequence(modification, key).Any())) continue;
            var sourceGroups = owned.GroupBy(document => Reference(document.Body, "m_CorrespondingSourceObject")).ToArray();
            if (sourceGroups.Any(group => group.Count() != 1)) continue;
            var bySource = sourceGroups.ToDictionary(group => group.Key, group => group.Single());
            var changes = Sequence(modification, "m_Modifications").OfType<YamlMappingNode>().ToArray();
            if (changes.Any(change => !bySource.ContainsKey(Reference(change, "target")) ||
                !string.Equals(Scalar(Mapping(change, "target"), "guid"), guid, StringComparison.OrdinalIgnoreCase))) continue;
            foreach (var change in changes)
            {
                string path = Scalar(change, "propertyPath");
                if (path == "m_RootOrder") continue;
                var reference = Mapping(change, "objectReference");
                YamlNode value = reference != null && (Reference(change, "objectReference") != 0 || !string.IsNullOrEmpty(Scalar(reference, "guid")))
                    ? Clone(reference) : new YamlScalarNode(Scalar(change, "value"));
                if (value is YamlMappingNode objectReference && string.Equals(Scalar(objectReference, "guid"), guid, StringComparison.OrdinalIgnoreCase))
                {
                    if (!bySource.TryGetValue(Number(Scalar(objectReference, "fileID")), out var target))
                        throw new InvalidDataException("A local prefab snapshot override refers to an unavailable source object.");
                    value = Ref(target.Id);
                }
                ApplyProperty(bySource[Reference(change, "target")].Body, path, value);
            }
            if (owned.Any(document => !CompleteLocalDocument(document, indexed)))
                throw new InvalidDataException("Prefab snapshot overrides invalidate the local object graph.");
            foreach (var document in owned)
            {
                Set(document.Body, "m_CorrespondingSourceObject", Ref(0));
                Set(document.Body, "m_PrefabInstance", Ref(0));
                diagnostics?.MaterializedLocalObjects.Add(document.Id);
            }
        }
        if (diagnostics == null) return;
        var missingIds = missing.Select(instance => instance.Id).ToHashSet();
        foreach (var alias in documents.Where(document => document.IsStripped && document.ClassId == 4 &&
            Get(document.Body, "m_GameObject") == null && missingIds.Contains(Reference(document.Body, "m_PrefabInstance"))))
        {
            bool required = documents.Where(document => document.Id != alias.Id).Any(document =>
                document.Body.Children.Any(pair => !(document.ClassId == 4 && (pair.Key as YamlScalarNode)?.Value == "m_Children") &&
                    HasLocalReference(pair.Value, alias.Id)));
            if (!required) diagnostics.DeferredMissingAliases.Add(alias.Id);
        }
    }

    private static bool CompleteLocalDocument(Document document, IReadOnlyDictionary<long, Document> indexed)
    {
        bool HasOwner() => indexed.TryGetValue(Reference(document.Body, "m_GameObject"), out var owner) &&
            owner.ClassId == 1 && !owner.IsStripped && Sequence(owner.Body, "m_Component")
                .OfType<YamlMappingNode>().Count(component => Reference(component, "component") == document.Id) == 1;
        bool LocalReferencesExist(YamlNode node)
        {
            if (node is YamlMappingNode mapping)
            {
                if (Get(mapping, "fileID") != null)
                    return !string.IsNullOrEmpty(Scalar(mapping, "guid")) || Number(Scalar(mapping, "fileID")) == 0 || indexed.ContainsKey(Number(Scalar(mapping, "fileID")));
                return mapping.Children.All(pair => LocalReferencesExist(pair.Value));
            }
            return node is not YamlSequenceNode sequence || sequence.Children.All(LocalReferencesExist);
        }
        if (document.Body.Children.Any(pair => (pair.Key as YamlScalarNode)?.Value is not
                ("m_CorrespondingSourceObject" or "m_PrefabInstance" or "m_PrefabAsset") && !LocalReferencesExist(pair.Value))) return false;
        switch (document.ClassId)
        {
            case 1:
                var components = Sequence(document.Body, "m_Component").OfType<YamlMappingNode>().Select(component => Reference(component, "component")).ToArray();
                return Get(document.Body, "m_Name") is YamlScalarNode && Scalar(document.Body, "m_IsActive") is "0" or "1" &&
                    components.Length > 0 && components.Distinct().Count() == components.Length &&
                    components.All(id => indexed.TryGetValue(id, out var component) && !component.IsStripped &&
                        Reference(component.Body, "m_GameObject") == document.Id) &&
                    components.Count(id => indexed[id].ClassId == 4) == 1;
            case 4:
                if (!HasOwner() || Mapping(document.Body, "m_Father") == null || Get(document.Body, "m_Children") is not YamlSequenceNode ||
                    !CompleteVector(Mapping(document.Body, "m_LocalPosition"), "x", "y", "z") ||
                    !CompleteVector(Mapping(document.Body, "m_LocalRotation"), "x", "y", "z", "w") ||
                    !CompleteVector(Mapping(document.Body, "m_LocalScale"), "x", "y", "z")) return false;
                var visited = new HashSet<long> { document.Id };
                long parent = Reference(document.Body, "m_Father");
                while (parent != 0)
                {
                    if (!visited.Add(parent) || !indexed.TryGetValue(parent, out var ancestor) || ancestor.ClassId != 4) return false;
                    parent = Reference(ancestor.Body, "m_Father");
                }
                return true;
            case 23:
                return HasOwner() && Get(document.Body, "m_Enabled") is YamlScalarNode && Get(document.Body, "m_Materials") is YamlSequenceNode;
            case 33:
                return HasOwner() && Reference(document.Body, "m_Mesh") != 0;
            case 114:
                return HasOwner() && !string.IsNullOrEmpty(Scalar(Mapping(document.Body, "m_Script"), "guid"));
            case 137:
                return HasOwner() && Reference(document.Body, "m_Mesh") != 0 && Get(document.Body, "m_Bones") is YamlSequenceNode &&
                    Get(document.Body, "m_Materials") is YamlSequenceNode;
            default:
                return false;
        }
    }

    private static bool CompleteVector(YamlMappingNode? value, params string[] axes) => axes.All(axis =>
        double.TryParse(Scalar(value, axis), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number));

    private static bool HasLocalReference(YamlNode node, long id) => node switch
    {
        YamlMappingNode mapping when Get(mapping, "fileID") != null => string.IsNullOrEmpty(Scalar(mapping, "guid")) && Number(Scalar(mapping, "fileID")) == id,
        YamlMappingNode mapping => mapping.Children.Values.Any(child => HasLocalReference(child, id)),
        YamlSequenceNode sequence => sequence.Children.Any(child => HasLocalReference(child, id)),
        _ => false
    };

    private static void ApplyStructuralChanges(YamlMappingNode? modification, List<Document> documents,
        List<Document> copies, Dictionary<long, Document> bySource, Dictionary<long, long> map, string sourceGuid)
    {
        var removed = new HashSet<long>();
        foreach (var node in Sequence(modification, "m_RemovedComponents").Concat(Sequence(modification, "m_RemovedGameObjects")))
        {
            if (node is not YamlMappingNode reference ||
                (!string.IsNullOrEmpty(Scalar(reference, "guid")) && !string.Equals(Scalar(reference, "guid"), sourceGuid, StringComparison.OrdinalIgnoreCase)) ||
                !map.TryGetValue(Number(Scalar(reference, "fileID")), out var removedId))
                throw new InvalidDataException("Removed prefab object cannot resolve its source identity.");
            removed.Add(removedId);
        }
        bool changed;
        do
        {
            changed = false;
            foreach (var document in copies)
            {
                if (removed.Contains(Reference(document.Body, "m_GameObject")) || removed.Contains(Reference(document.Body, "m_Father")) ||
                    (document.IsInstance && removed.Contains(Reference(Mapping(document.Body, "m_Modification"), "m_TransformParent"))))
                {
                    changed |= removed.Add(document.Id);
                    if (document.ClassId == 4 && Reference(document.Body, "m_GameObject") != 0) changed |= removed.Add(Reference(document.Body, "m_GameObject"));
                }
            }
        } while (changed);
        foreach (var document in copies)
        {
            RemoveReferences(document.Body, "m_Children", removed, null);
            RemoveReferences(document.Body, "m_Component", removed, "component");
        }
        copies.RemoveAll(document => removed.Contains(document.Id));
        foreach (var node in Sequence(modification, "m_AddedGameObjects").Concat(Sequence(modification, "m_AddedComponents")))
        {
            if (node is not YamlMappingNode added ||
                !string.Equals(Scalar(Mapping(added, "targetCorrespondingSourceObject"), "guid"), sourceGuid, StringComparison.OrdinalIgnoreCase) ||
                !bySource.TryGetValue(Reference(added, "targetCorrespondingSourceObject"), out var target) || removed.Contains(target.Id))
                throw new InvalidDataException("Added prefab object cannot resolve its target.");
            long addedId = Reference(added, "addedObject");
            var addedDocument = documents.SingleOrDefault(document => document.Id == addedId)
                ?? throw new InvalidDataException("Added prefab object is unavailable.");
            if (target.ClassId == 4)
            {
                var transform = addedDocument.ClassId == 4 ? addedDocument : documents.SingleOrDefault(document =>
                    document.ClassId == 4 && Reference(document.Body, "m_GameObject") == addedId);
                if (transform == null) throw new InvalidDataException("Added prefab GameObject has no Transform.");
                Set(transform.Body, "m_Father", Ref(target.Id));
                if (Get(target.Body, "m_Children") != null) Insert(EnsureSequence(target.Body, "m_Children"), Ref(transform.Id), added);
            }
            else if (target.ClassId == 1)
            {
                Set(addedDocument.Body, "m_GameObject", Ref(target.Id));
                Insert(EnsureSequence(target.Body, "m_Component"), new YamlMappingNode { { "component", Ref(addedId) } }, added);
            }
            else throw new InvalidDataException("Unsupported added prefab object target type.");
        }
    }

    private static int ClassForProperty(string path) => path switch
    {
        "m_Name" or "m_IsActive" or "m_Layer" or "m_TagString" => 1,
        _ when path.StartsWith("m_Local", StringComparison.Ordinal) || path == "m_RootOrder" => 4,
        _ => 0
    };

    private static void EnsureDerivedObject(List<Document> documents, long id, int classId,
        IReadOnlyDictionary<string, IReadOnlyDictionary<long, int>>? modelObjects)
    {
        if (documents.Any(document => document.Id == id) || classId == 0) return;
        var candidate = FindDerivedTarget(documents, id, modelObjects, classId);
        if (candidate == null) return;
        var selected = candidate.Value;
        var reference = Ref(selected.Target);
        Set(reference, "guid", new YamlScalarNode(selected.Guid));
        Set(reference, "type", new YamlScalarNode("3"));
        var body = new YamlMappingNode
        {
            { "m_CorrespondingSourceObject", reference },
            { "m_PrefabInstance", Ref(selected.Instance.Id) },
            { "m_PrefabAsset", Ref(0) }
        };
        string type = classId switch
        {
            1 => "GameObject",
            4 => "Transform",
            23 => "MeshRenderer",
            137 => "SkinnedMeshRenderer",
            _ => throw new InvalidDataException("Unsupported inherited model object class: " + classId)
        };
        documents.Add(new Document(id, classId, new YamlMappingNode { { type, body } }, true));
    }

    private static (Document Instance, long Target, string Guid)? FindDerivedTarget(List<Document> documents, long id,
        IReadOnlyDictionary<string, IReadOnlyDictionary<long, int>>? modelObjects, int classId = 0)
    {
        var candidates = new List<(Document Instance, long Target, string Guid)>();
        foreach (var instance in documents.Where(document => document.IsInstance))
        {
            string guid = Scalar(Mapping(instance.Body, "m_SourcePrefab"), "guid");
            var targets = Sequence(Mapping(instance.Body, "m_Modification"), "m_Modifications").OfType<YamlMappingNode>()
                .Where(change => Scalar(Mapping(change, "target"), "guid") == guid &&
                    (classId == 0 || ClassForProperty(Scalar(change, "propertyPath")) == classId)).Select(change => Reference(change, "target"));
            if (modelObjects?.TryGetValue(guid, out var identities) == true)
                targets = targets.Concat(identities.Where(pair => classId == 0 || pair.Value == classId).Select(pair => pair.Key));
            targets = targets.Distinct();
            foreach (long target in targets)
                if (PrefabVariantResolver.ComputeDerivedFileId(instance.Id, target) == id) candidates.Add((instance, target, guid));
        }
        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static YamlNode? Get(YamlMappingNode? mapping, string key) => mapping != null && mapping.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;
    private static YamlMappingNode? Mapping(YamlMappingNode? mapping, string key) => Get(mapping, key) as YamlMappingNode;
    private static string Scalar(YamlMappingNode? mapping, string key) => (Get(mapping, key) as YamlScalarNode)?.Value ?? "";
    private static long Number(string value) => string.IsNullOrEmpty(value) ? 0 : long.Parse(value, CultureInfo.InvariantCulture);
    private static long Reference(YamlMappingNode? mapping, string key) => Number(Scalar(Mapping(mapping, key), "fileID"));
    private static IEnumerable<YamlNode> Sequence(YamlMappingNode? mapping, string key) => (Get(mapping, key) as YamlSequenceNode)?.Children ?? Enumerable.Empty<YamlNode>();
    private static void Set(YamlMappingNode mapping, string key, YamlNode value) => mapping.Children[new YamlScalarNode(key)] = value;
    private static YamlMappingNode Ref(long id) => new() { { "fileID", new YamlScalarNode(id.ToString(CultureInfo.InvariantCulture)) } };

    private static YamlSequenceNode EnsureSequence(YamlMappingNode mapping, string key)
    {
        if (Get(mapping, key) is YamlSequenceNode existing) return existing;
        var sequence = new YamlSequenceNode();
        Set(mapping, key, sequence);
        return sequence;
    }

    private static YamlNode Clone(YamlNode node) => node switch
    {
        YamlMappingNode mapping => new YamlMappingNode(mapping.Children.Select(pair => new KeyValuePair<YamlNode, YamlNode>(Clone(pair.Key), Clone(pair.Value)))) { Style = mapping.Style },
        YamlSequenceNode sequence => new YamlSequenceNode(sequence.Children.Select(Clone)) { Style = sequence.Style },
        YamlScalarNode scalar => new YamlScalarNode(scalar.Value) { Style = scalar.Style },
        _ => throw new InvalidDataException("Unsupported YAML node in prefab.")
    };

    private static YamlNode Remap(YamlNode node, IReadOnlyDictionary<long, long> map)
    {
        if (node is YamlMappingNode mapping)
        {
            var result = (YamlMappingNode)Clone(mapping);
            if (Get(mapping, "fileID") is YamlScalarNode fileId && string.IsNullOrEmpty(Scalar(mapping, "guid")))
            {
                if (map.TryGetValue(Number(fileId.Value ?? ""), out long target)) Set(result, "fileID", new YamlScalarNode(target.ToString(CultureInfo.InvariantCulture)));
                return result;
            }
            foreach (var key in result.Children.Keys.ToArray()) result.Children[key] = Remap(result.Children[key], map);
            return result;
        }
        if (node is YamlSequenceNode sequence) return new YamlSequenceNode(sequence.Children.Select(child => Remap(child, map))) { Style = sequence.Style };
        return Clone(node);
    }

    private static void Insert(YamlSequenceNode sequence, YamlNode node, YamlMappingNode added)
    {
        int index = Get(added, "insertIndex") == null ? -1 : checked((int)Number(Scalar(added, "insertIndex")));
        if (index < -1 || index > sequence.Children.Count) throw new InvalidDataException("Invalid prefab insertion index.");
        sequence.Children.Insert(index < 0 ? sequence.Children.Count : index, node);
    }

    private static void RemoveReferences(YamlMappingNode body, string key, HashSet<long> removed, string? nested)
    {
        if (Get(body, key) is not YamlSequenceNode sequence) return;
        foreach (var node in sequence.Children.OfType<YamlMappingNode>().ToArray())
            if (removed.Contains(nested == null ? Number(Scalar(node, "fileID")) : Reference(node, nested))) sequence.Children.Remove(node);
    }

    private static void ApplyProperty(YamlMappingNode body, string path, YamlNode value)
    {
        string[] parts = path.Split('.');
        YamlNode current = body;
        for (int index = 0; index < parts.Length; index++)
        {
            string part = parts[index];
            if (part == "Array" && current is YamlSequenceNode) continue;
            if (current is YamlSequenceNode sequence)
            {
                if (part == "size" && index == parts.Length - 1)
                {
                    int size = checked((int)Number((value as YamlScalarNode)?.Value ?? ""));
                    if (size < 0 || size > 100000) throw new InvalidDataException("Invalid prefab array size.");
                    while (sequence.Children.Count > size) sequence.Children.RemoveAt(sequence.Children.Count - 1);
                    while (sequence.Children.Count < size) sequence.Add(sequence.Children.Count == 0 ? new YamlScalarNode("0") : Clone(sequence.Children[^1]));
                    return;
                }
                var match = Regex.Match(part, @"^data\[(\d+)\]$");
                if (!match.Success || !int.TryParse(match.Groups[1].Value, out int element) || element >= sequence.Children.Count)
                    throw new InvalidDataException("Unavailable prefab array property: " + path);
                if (index == parts.Length - 1) { sequence.Children[element] = value; return; }
                current = sequence.Children[element];
                continue;
            }
            if (current is not YamlMappingNode mapping || Get(mapping, part) is not { } child)
                throw new InvalidDataException("Unavailable prefab property: " + path);
            if (index == parts.Length - 1)
            {
                if (child is YamlMappingNode reference && Get(reference, "fileID") != null && value is YamlScalarNode { Value: "" }) value = Ref(0);
                Set(mapping, part, value);
                return;
            }
            current = child;
        }
        throw new InvalidDataException("Empty prefab property path.");
    }
}
