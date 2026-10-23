#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using YamlDotNet.RepresentationModel;

namespace UnityPackageImporter.Models;

internal sealed record BlendshapeAppearanceObject(long FileId, long AvatarRootId, string Name, string RelativePath);
internal sealed record BlendshapeAppearanceRenderer(long FileId, long GameObjectFileId, IReadOnlyDictionary<string, float> Weights);
internal sealed record BlendshapeAppearanceWrite(long RendererId, string Shape, float UnityWeight, long ComponentId);
internal sealed class BlendshapeAppearanceResult
{
    internal List<BlendshapeAppearanceWrite> Writes { get; } = new();
    internal List<AvatarOperationDiagnostic> Diagnostics { get; } = new();
    internal HashSet<long> Components { get; } = new();
    internal bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == AvatarOperationSeverity.Error);
}

internal static class BlendshapeInitialAppearance
{
    private sealed record Rule(long ComponentId, long SourceRenderer, string SourceShape, long TargetRenderer, string TargetShape, IReadOnlyList<(float Time, float Value)> Curve);

    internal static BlendshapeAppearanceResult Evaluate(string yaml,
        IReadOnlyDictionary<long, BlendshapeAppearanceObject> objects,
        IReadOnlyDictionary<long, BlendshapeAppearanceRenderer> renderers,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new BlendshapeAppearanceResult();
        var rules = new List<Rule>();
        var documents = ReadDocuments(yaml);
        void Diagnostic(long id, string message, string code, AvatarOperationSeverity severity = AvatarOperationSeverity.Error) =>
            result.Diagnostics.Add(new AvatarOperationDiagnostic { Severity = severity, Code = code, Message = message, SourceContext = "Blendshape component " + id.ToString(CultureInfo.InvariantCulture) });
        BlendshapeAppearanceRenderer? Skin(long id, long scope, bool rendererReference)
        {
            var candidates = renderers.Values.Where(renderer => rendererReference ? renderer.FileId == id : renderer.GameObjectFileId == id)
                .Where(renderer => objects.TryGetValue(renderer.GameObjectFileId, out var owner) && owner.AvatarRootId == scope).ToArray();
            return candidates.Length == 1 ? candidates[0] : null;
        }
        foreach (var document in documents.OrderBy(pair => pair.Key))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Get(document.Value, "MonoBehaviour") is not YamlMappingNode body) continue;
            string script = Text(Map(body, "m_Script"), "guid");
            bool ma = script.Equals(AvatarPackageIndex.BlendshapeSyncScriptGuid, StringComparison.OrdinalIgnoreCase);
            bool fury = AvatarPackageIndex.RecognizedVrcFuryScriptGuids.Contains(script);
            if (!ma && !fury) continue;
            var features = fury ? ReachableFeatures(body, message => Diagnostic(document.Key, message, "BLENDSHAPE_MANAGED_REFERENCES_INVALID")).Where(feature => Text(Map(feature, "type"), "class") == "BlendShapeLink" &&
                Text(Map(feature, "type"), "ns") == "VF.Model.Feature").ToArray() : Array.Empty<YamlMappingNode>();
            if (!ma && features.Length == 0) continue;
            long componentId = document.Key;
            result.Components.Add(componentId);
            long ownerId = Id(body, "m_GameObject");
            if (!objects.TryGetValue(ownerId, out var owner))
            {
                Diagnostic(componentId, "The blendshape component has no reconstructed owning avatar object.", "APPEARANCE_OWNER_UNRESOLVED");
                continue;
            }
            if (ma)
            {
                if (Get(body, "Bindings") is YamlSequenceNode empty && empty.Children.Count == 0) continue;
                var target = Skin(ownerId, owner.AvatarRootId, false);
                if (target == null)
                {
                    Diagnostic(componentId, "Blendshape Sync requires one local skinned renderer.", "BLENDSHAPE_TARGET_UNRESOLVED");
                    continue;
                }
                if (Get(body, "Bindings") is not YamlSequenceNode bindings)
                {
                    Diagnostic(componentId, "Blendshape Sync bindings are not a sequence.", "BLENDSHAPE_BINDINGS_INVALID");
                    continue;
                }
                foreach (var bindingNode in bindings)
                {
                    if (bindingNode is not YamlMappingNode binding)
                    {
                        Diagnostic(componentId, "A blendshape binding is not a mapping.", "BLENDSHAPE_BINDINGS_INVALID");
                        continue;
                    }
                    var reference = Map(binding, "ReferenceMesh");
                    long sourceObject = Id(reference, "targetObject");
                    string path = Text(reference, "referencePath");
                    BlendshapeAppearanceObject? sourceOwner = null;
                    if (!string.IsNullOrEmpty(path) && objects.TryGetValue(sourceObject, out var direct) && direct.AvatarRootId == owner.AvatarRootId)
                        sourceOwner = direct;
                    else if (!string.IsNullOrEmpty(path))
                    {
                        var candidates = objects.Values.Where(candidate => candidate.AvatarRootId == owner.AvatarRootId &&
                            (path == "$$$AVATAR_ROOT$$$" ? candidate.FileId == owner.AvatarRootId : !string.IsNullOrEmpty(path) && candidate.RelativePath == path)).ToArray();
                        if (candidates.Length == 1) sourceOwner = candidates[0];
                    }
                    var source = sourceOwner == null ? null : Skin(sourceOwner.FileId, owner.AvatarRootId, false);
                    if (source == null)
                    {
                        Diagnostic(componentId, "The declared Blendshape Sync source is missing, ambiguous, or outside the owning avatar.", "BLENDSHAPE_SOURCE_UNRESOLVED");
                        continue;
                    }
                    string from = Text(binding, "Blendshape");
                    string to = Text(binding, "LocalBlendshape");
                    if (string.IsNullOrWhiteSpace(to)) to = from;
                    var curve = new List<(float Time, float Value)>();
                    if (Bool(binding, "RemapCurveIsValid", false))
                    {
                        foreach (var key in Sequence(Map(binding, "RemapCurve"), "m_Curve"))
                        {
                            if (key is YamlMappingNode mapping) curve.Add((Float(mapping, "time"), Float(mapping, "value")));
                            else Diagnostic(componentId, "A remap curve key is not a mapping.", "BLENDSHAPE_REMAP_INVALID");
                        }
                    }
                    rules.Add(new Rule(componentId, source.FileId, from, target.FileId, to, curve));
                }
            }
            else foreach (var feature in features)
            {
                var data = Map(feature, "data");
                int version = 0;
                if (Get(data, "version") != null && !int.TryParse(Text(data, "version"), NumberStyles.Integer, CultureInfo.InvariantCulture, out version))
                {
                    Diagnostic(componentId, "BlendShapeLink serialized version is not an integer.", "BLENDSHAPE_VERSION_UNSUPPORTED");
                    continue;
                }
                if (version < 0 || version > 1)
                {
                    Diagnostic(componentId, "Unsupported BlendShapeLink serialized version.", "BLENDSHAPE_VERSION_UNSUPPORTED");
                    continue;
                }
                string baseName = Text(data, "baseObj");
                var baseSkins = renderers.Values.Where(renderer => objects.TryGetValue(renderer.GameObjectFileId, out var candidate) &&
                    candidate.AvatarRootId == owner.AvatarRootId && candidate.Name == baseName).ToArray();
                if (baseSkins.Length > 1)
                {
                    int shortest = baseSkins.Min(renderer => objects[renderer.GameObjectFileId].RelativePath.Length);
                    baseSkins = baseSkins.Where(renderer => objects[renderer.GameObjectFileId].RelativePath.Length == shortest).ToArray();
                }
                if (baseSkins.Length != 1)
                {
                    Diagnostic(componentId, "BlendShapeLink base skin is missing or ambiguous within the owning avatar.", "BLENDSHAPE_SOURCE_UNRESOLVED");
                    continue;
                }
                var source = baseSkins[0];
                var linked = version < 1 ? Sequence(data, "objs").OfType<YamlMappingNode>().Select(reference => Skin(Id(reference), owner.AvatarRootId, false)) :
                    Sequence(data, "linkSkins").OfType<YamlMappingNode>().Select(reference => Skin(Id(reference, "renderer"), owner.AvatarRootId, true));
                foreach (var target in linked)
                {
                    if (target == null)
                    {
                        Diagnostic(componentId, "A linked renderer is missing, ambiguous, or outside the owning avatar.", "BLENDSHAPE_TARGET_UNRESOLVED");
                        continue;
                    }
                    bool exact = Bool(data, "exactMatch", false);
                    var pairs = new HashSet<(string From, string To)>();
                    foreach (var include in Sequence(data, "includes").OfType<YamlMappingNode>())
                    {
                        string from = Text(include, "nameOnBase"), to = Text(include, "nameOnLinked");
                        if (string.IsNullOrWhiteSpace(from) && string.IsNullOrWhiteSpace(to)) continue;
                        if (string.IsNullOrWhiteSpace(from)) from = to;
                        if (string.IsNullOrWhiteSpace(to)) to = from;
                        AddMapping(from, to, true);
                    }
                    if (Bool(data, "includeAll", true))
                    {
                        var excludes = Sequence(data, "excludes").OfType<YamlMappingNode>().Select(entry => Text(entry, "name")).ToHashSet(StringComparer.Ordinal);
                        foreach (string name in source.Weights.Keys)
                            if (!excludes.Contains(name)) AddMapping(name, name, false);
                    }
                    foreach (var pair in pairs) rules.Add(new Rule(componentId, source.FileId, pair.From, target.FileId, pair.To, Array.Empty<(float, float)>()));
                    void AddMapping(string from, string to, bool explicitMapping)
                    {
                        string? sourceShape = Match(from, source.Weights.Keys, exact);
                        string? targetShape = Match(to, target.Weights.Keys, exact);
                        if (sourceShape == null || targetShape == null)
                        {
                            if (explicitMapping) Diagnostic(componentId, "An explicit BlendShapeLink mapping is missing or ambiguous.", "BLENDSHAPE_MAPPING_UNRESOLVED");
                            return;
                        }
                        if (!explicitMapping && pairs.Any(pair => pair.From == sourceShape)) return;
                        pairs.Add((sourceShape, targetShape));
                    }
                }
            }
            Diagnostic(componentId, "Initial blendshape weights are compiled; animator-curve propagation and runtime parameter behavior are not converted.", "INITIAL_APPEARANCE_ONLY", AvatarOperationSeverity.Warning);
        }
        var targets = rules.Select(rule => (rule.TargetRenderer, rule.TargetShape)).ToHashSet();
        foreach (var rule in rules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (targets.Contains((rule.SourceRenderer, rule.SourceShape)))
            {
                Diagnostic(rule.ComponentId, "Chained or cyclic blendshape synchronization is not supported; no transitive mapping was invented.", "BLENDSHAPE_CHAIN_UNSUPPORTED");
                continue;
            }
            if (!renderers[rule.SourceRenderer].Weights.TryGetValue(rule.SourceShape, out float weight) ||
                !renderers[rule.TargetRenderer].Weights.ContainsKey(rule.TargetShape))
            {
                Diagnostic(rule.ComponentId, "A declared source or destination blendshape is unavailable.", "BLENDSHAPE_MAPPING_UNRESOLVED");
                continue;
            }
            if (!float.IsFinite(weight) || rule.Curve.Any(key => !float.IsFinite(key.Time) || !float.IsFinite(key.Value)) ||
                rule.Curve.Zip(rule.Curve.Skip(1), (left, right) => left.Time >= right.Time).Any(invalid => invalid))
            {
                Diagnostic(rule.ComponentId, "Blendshape weights and remap keys must be finite, with strictly increasing key times.", "BLENDSHAPE_REMAP_INVALID");
                continue;
            }
            float mapped = Remap(weight, rule.Curve);
            if (!float.IsFinite(mapped))
            {
                Diagnostic(rule.ComponentId, "Blendshape remapping produced a non-finite weight.", "BLENDSHAPE_REMAP_INVALID");
                continue;
            }
            result.Writes.Add(new BlendshapeAppearanceWrite(rule.TargetRenderer, rule.TargetShape, mapped, rule.ComponentId));
        }
        foreach (var group in result.Writes.GroupBy(write => (write.RendererId, write.Shape)))
            if (group.Select(write => write.UnityWeight).Distinct().Count() > 1)
                Diagnostic(group.First().ComponentId, "Multiple blendshape bindings produce conflicting destination weights.", "BLENDSHAPE_WRITE_CONFLICT");
        if (result.HasErrors) result.Writes.Clear();
        return result;
    }

    internal static float Remap(float weight, IReadOnlyList<(float Time, float Value)> curve)
    {
        if (curve.Count < 2) return weight;
        int range = 0;
        while (range < curve.Count - 2 && weight > curve[range + 1].Time) range++;
        var left = curve[range]; var right = curve[range + 1];
        return (float)(left.Value + ((double)weight - left.Time) * (right.Value - left.Value) / (right.Time - left.Time));
    }

    internal static IReadOnlyDictionary<string, float> CaptureSourceWeights(IReadOnlyList<string> names,
        IReadOnlyList<float>? serializedWeights, IReadOnlyDictionary<string, float> nativeWeights)
    {
        var result = new Dictionary<string, float>(StringComparer.Ordinal);
        for (int index = 0; index < names.Count; index++)
        {
            string name = names[index];
            float weight = nativeWeights.TryGetValue(name, out float native) ? native :
                serializedWeights != null && index < serializedWeights.Count ? serializedWeights[index] : 0f;
            if (string.IsNullOrEmpty(name) || !result.TryAdd(name, weight))
                throw new InvalidDataException("Source blendshape names are empty or duplicated.");
        }
        return result;
    }

    private static string? Match(string name, IEnumerable<string> names, bool exact)
    {
        var values = names.ToArray();
        if (values.Contains(name, StringComparer.Ordinal)) return name;
        if (exact || string.IsNullOrWhiteSpace(name)) return null;
        string Normalize(string text) => Regex.Replace(text.ToLowerInvariant(), @"\s", "");
        var matches = values.Where(candidate => Normalize(candidate) == Normalize(name)).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    internal static IReadOnlyDictionary<long, YamlMappingNode> ReadDocuments(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(Regex.Replace(AvatarPackageIndex.NormalizeDocumentHeaders(yaml), @"(?m)^(---[ \t]+)!u!(\d+)", "$1!<tag:unity3d.com,2011:$2>")));
        return stream.Documents.ToDictionary(document => long.Parse(document.RootNode.Anchor.Value, CultureInfo.InvariantCulture), document => (YamlMappingNode)document.RootNode);
    }

    private static IEnumerable<YamlMappingNode> ReachableFeatures(YamlMappingNode body, Action<string> invalid)
    {
        var table = new Dictionary<long, YamlMappingNode>();
        bool malformed = false;
        foreach (var node in Sequence(Map(body, "references"), "RefIds"))
        {
            if (node is not YamlMappingNode entry || !long.TryParse(Text(entry, "rid"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long rid) ||
                rid is 0 or -1 or -2 || !table.TryAdd(rid, entry))
            {
                invalid("Managed-reference IDs must be valid, non-null, and unique, including orphan records.");
                malformed = true;
            }
        }
        if (malformed) yield break;
        var seen = new HashSet<long>();
        var queue = new Queue<long>();
        void Find(YamlNode? node)
        {
            if (node is YamlMappingNode mapping)
            {
                if (Get(mapping, "rid") is YamlScalarNode) queue.Enqueue(Id(mapping, "rid"));
                foreach (var entry in mapping.Children.Values) Find(entry);
            }
            else if (node is YamlSequenceNode sequence) foreach (var entry in sequence) Find(entry);
        }
        Find(Get(body, "content"));
        Find(Get(Map(body, "config"), "features"));
        while (queue.TryDequeue(out long id))
        {
            if (!seen.Add(id) || !table.TryGetValue(id, out var entry)) continue;
            yield return entry;
            Find(Get(entry, "data"));
        }
    }

    private static YamlNode? Get(YamlMappingNode? body, string key) => body != null && body.Children.TryGetValue(key, out var node) ? node : null;
    private static YamlMappingNode? Map(YamlMappingNode? body, string key) => Get(body, key) as YamlMappingNode;
    private static string Text(YamlMappingNode? body, string key) => (Get(body, key) as YamlScalarNode)?.Value ?? "";
    private static long Id(YamlMappingNode? body, string key) => Get(body, key) is YamlScalarNode scalar && long.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : Id(Map(body, key));
    private static long Id(YamlMappingNode? body) => long.TryParse(Text(body, "fileID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : 0;
    private static IEnumerable<YamlNode> Sequence(YamlMappingNode? body, string key) => Get(body, key) is YamlSequenceNode sequence ? sequence.Children : Array.Empty<YamlNode>();
    private static bool Bool(YamlMappingNode? body, string key, bool fallback) => Get(body, key) == null ? fallback : Text(body, key) is "1" or "true";
    private static float Float(YamlMappingNode body, string key) => float.TryParse(Text(body, key), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : float.NaN;
}
