#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace UnityPackageImporter.Models;

public static class VrcFuryInitialAppearance
{
    public const string UpstreamRevision = "6bd33f4cb453df816c943a335e47f64e6a37e188";
    public const string ScriptGuid = "d9e94e501a2d4c95bff3d5601013d923";
    private static readonly Regex Header = new(@"^--- !u!(?<class>\d+) &(?<id>-?\d+)(?: stripped)?\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private sealed record Document(long Id, int ClassId, YamlMappingNode Body);
    private sealed record Managed(string Class, string Namespace, string Assembly, YamlMappingNode Data);
    private sealed record Feature(long Source, long Owner, string Class, YamlMappingNode Data, IReadOnlyList<Managed> Actions);

    public static bool ContainsComponents(string yaml, CancellationToken token = default) => Read(yaml, token).Values.Any(IsComponent);

    public static InitialAppearanceResult Evaluate(string yaml, InitialAppearanceContext context, CancellationToken token = default)
        => Evaluate(yaml, context, null, token);

    internal static InitialAppearanceResult Evaluate(string yaml, InitialAppearanceContext context,
        Func<string, long, UnityStaticClipResult?>? clipResolver, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var diagnostics = new List<InitialAppearanceDiagnostic>();
        void Report(string code, string message, long source = 0, InitialAppearanceSeverity severity = InitialAppearanceSeverity.Error) =>
            diagnostics.Add(new(severity, "VRCFURY_INITIAL_" + code, message, source));
        Dictionary<long, Document> documents;
        try { documents = Read(yaml, token); }
        catch (Exception exception) when (exception is InvalidDataException or YamlException or FormatException or OverflowException or ArgumentException)
        {
            Report("YAML", "Malformed composed Unity YAML: " + exception.Message);
            return new() { Diagnostics = diagnostics.AsReadOnly() };
        }
        var objects = documents.Values.Where(item => item.ClassId == 1).ToDictionary(item => item.Id);
        var parents = new Dictionary<long, long>();
        foreach (var transform in documents.Values.Where(item => item.ClassId is 4 or 224))
        {
            long owner = Reference(transform.Body, "m_GameObject");
            long father = Reference(transform.Body, "m_Father");
            if (!objects.ContainsKey(owner) || parents.ContainsKey(owner))
            { Report("HIERARCHY", "Missing or duplicate Transform ownership.", transform.Id); continue; }
            if (father != 0 && (!documents.TryGetValue(father, out var parent) || parent.ClassId is not (4 or 224)))
                Report("HIERARCHY", "Missing parent Transform.", transform.Id);
            parents[owner] = father == 0 ? 0 : documents.TryGetValue(father, out var resolved) ? Reference(resolved.Body, "m_GameObject") : 0;
        }
        bool Within(long candidate)
        {
            var visited = new HashSet<long>();
            while (candidate != 0 && visited.Add(candidate))
            {
                if (candidate == context.AvatarRootId) return true;
                candidate = parents.TryGetValue(candidate, out long parent) ? parent : 0;
            }
            return false;
        }
        if (!objects.ContainsKey(context.AvatarRootId)) Report("SCOPE", "A verified owning avatar GameObject is required.");
        var authored = new Dictionary<long, bool>();
        foreach (var item in objects.Values.Where(item => Within(item.Id)))
        {
            string value = Text(item.Body, "m_IsActive");
            if (value is not ("0" or "1")) Report("ACTIVE", "Missing or malformed authored m_IsActive.", item.Id);
            authored[item.Id] = value == "1";
        }
        foreach (long owner in authored.Keys)
        {
            var visited = new HashSet<long>();
            long current = owner;
            while (current != 0 && parents.TryGetValue(current, out long parent))
            {
                if (!visited.Add(current)) { Report("HIERARCHY", "Cyclic avatar Transform hierarchy.", owner); break; }
                current = parent;
            }
        }
        var features = new List<Feature>();
        ReadFeatures(documents, Within, features, Report, token);
        long ResolvePath(long owner, string path, int classId)
        {
            bool Valid(long identity) => classId == 1 || documents.Values.Any(item => item.ClassId == classId && Reference(item.Body, "m_GameObject") == identity);
            long Find(long start)
            {
                long current = start;
                foreach (string part in path.Split('/'))
                {
                    if (part is "" or ".") continue;
                    if (part == "..")
                    {
                        if (current == context.AvatarRootId || !parents.TryGetValue(current, out current) || !Within(current)) return 0;
                        continue;
                    }
                    var matches = objects.Values.Where(item => Within(item.Id) && parents.TryGetValue(item.Id, out long parent) && parent == current && Text(item.Body, "m_Name") == part).ToArray();
                    if (matches.Length > 1) { Report("CLIP_PATH", "Ambiguous exact animation path '" + path + "'.", owner); return 0; }
                    if (matches.Length == 0) return 0;
                    current = matches[0].Id;
                }
                return Valid(current) ? current : 0;
            }
            if (path.StartsWith("/", StringComparison.Ordinal)) return Find(context.AvatarRootId);
            var visited = new HashSet<long>();
            while (Within(owner) && visited.Add(owner))
            {
                long target = Find(owner);
                if (target != 0) return target;
                if (owner == context.AvatarRootId || !parents.TryGetValue(owner, out owner)) break;
            }
            return 0;
        }
        return Compile(features, context, authored, Within, documents, ResolvePath, clipResolver, diagnostics, Report, token);
    }

    private static InitialAppearanceResult Compile(List<Feature> features, InitialAppearanceContext context, Dictionary<long, bool> authored,
        Func<long, bool> within, Dictionary<long, Document> documents, Func<long, string, int, long> resolvePath,
        Func<string, long, UnityStaticClipResult?>? clipResolver, List<InitialAppearanceDiagnostic> diagnostics,
        Action<string, string, long, InitialAppearanceSeverity> report, CancellationToken token)
    {
        void Error(string code, string message, long source) => report(code, message, source, InitialAppearanceSeverity.Error);
        void Unsupported(string message, long source, string code = "UNSUPPORTED") => report(code, message, source, InitialAppearanceSeverity.Warning);
        var active = new Dictionary<long, bool>(authored);
        var writes = new Dictionary<(InitialAppearanceEffect, long, string), InitialAppearanceWrite>();
        var eligible = new List<Feature>();
        foreach (var feature in features)
        {
            bool blocked = false;
            if (feature.Class == "Toggle")
            {
                foreach (string field in new[] { "slider", "hasTransition", "separateLocal", "securityEnabled", "useGlobalParam", "enableDriveGlobalParam", "enableExclusiveTag", "exclusiveOffState", "hasExitTime", "holdButton" })
                    if (Boolean(feature.Data, field, false, feature.Source, report))
                    {
                        Unsupported("Toggle '" + Text(feature.Data, "name") + "' uses " + field + "; its entire INITIAL feature is skipped rather than guessing dynamic state.", feature.Source, "DYNAMIC");
                        blocked = true;
                    }
                if (Get(feature.Data, "resetPhysbones") is YamlSequenceNode reset && reset.Children.Any(item => ReferenceNode(item) != 0))
                    Unsupported("Legacy resetPhysbones actions are skipped; physics reset is not reversible appearance.", feature.Source, "DESTRUCTIVE_SKIPPED");
                if (Boolean(feature.Data, "includeInRest", false, feature.Source, report))
                    report("OBSOLETE_INCLUDE_IN_REST", "Pinned ToggleBuilder ignores obsolete includeInRest; defaultOn XOR invertRestLogic selects the resting state.", feature.Source, InitialAppearanceSeverity.Info);
                if (Boolean(feature.Data, "saved", false, feature.Source, report))
                    Unsupported("Saved Toggle parameters are not restored; only the authored build-time resting pose is compiled.", feature.Source, "SAVED_RUNTIME_UNEVALUATED");
            }
            if (!blocked) eligible.Add(feature);
        }
        long Target(YamlMappingNode data, string field, long source)
        {
            if (Get(data, field) is not YamlMappingNode reference || !long.TryParse(Text(reference, "fileID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long identity) || Text(reference, "guid").Trim('0').Length != 0)
            { Error("TARGET", "Action requires an exact local " + field + " fileID; external/ambiguous references are not borrowed by name.", source); return 0; }
            if (identity == 0) Unsupported("Action has a null " + field + " reference and contributes no binding.", source, "NULL_TARGET");
            return identity;
        }
        void Phase(IEnumerable<Feature> selected, bool off)
        {
            var phaseWrites = new Dictionary<(InitialAppearanceEffect, long, string), InitialAppearanceWrite>();
            var phaseActive = new Dictionary<long, bool>(active);
            foreach (var feature in selected)
            {
                var featureWrites = new Dictionary<(InitialAppearanceEffect, long, string), InitialAppearanceWrite>();
                foreach (var action in feature.Actions)
                {
                    token.ThrowIfCancellationRequested();
                    int latest = action.Class is "ObjectToggleAction" or "MaterialAction" ? 1 : 0;
                    if (action.Class is not ("ObjectToggleAction" or "BlendShapeAction" or "MaterialAction" or "AnimationClipAction"))
                    {
                        string code = action.Class.Contains("Delete", StringComparison.Ordinal) || action.Class.Contains("Remove", StringComparison.Ordinal) ? "DESTRUCTIVE_SKIPPED" : "ACTION_UNSUPPORTED";
                        Unsupported("Action '" + action.Class + "' is explicitly skipped. No destructive mesh/component mutation is performed.", feature.Source, code);
                        continue;
                    }
                    if (!Version(action.Data, latest, feature.Source, report)) continue;
                    bool desktop = Boolean(action.Data, "desktopActive", false, feature.Source, report);
                    bool android = Boolean(action.Data, "androidActive", false, feature.Source, report);
                    if (desktop != android || Boolean(action.Data, "localOnly", false, feature.Source, report) || Boolean(action.Data, "remoteOnly", false, feature.Source, report))
                    { Unsupported("Action '" + action.Class + "' uses platform/local/remote filtering without a verified build context; skipped.", feature.Source, "ACTION_FILTER"); continue; }
                    void Add(InitialAppearanceWrite write)
                    {
                        var key = (write.Effect, write.TargetId, write.Property);
                        if (featureWrites.TryGetValue(key, out var previous) && previous != write)
                            Error("CONFLICT", "Competing action bindings within one State for " + key + ".", feature.Source);
                        else featureWrites[key] = write;
                    }
                    if (action.Class == "ObjectToggleAction")
                    {
                        long target = Target(action.Data, "obj", feature.Source);
                        if (target == 0) continue;
                        if (!authored.ContainsKey(target)) { Error("TARGET", "ObjectToggleAction target must be an exact GameObject in the owning avatar.", feature.Source); continue; }
                        int version = Math.Max(0, Integer(action.Data, "version", 0, feature.Source, report));
                        int mode = version == 0 ? 2 : Integer(action.Data, "mode", 0, feature.Source, report);
                        if (mode is < 0 or > 2) { Error("MODE", "Unknown ObjectToggleAction mode " + mode + ".", feature.Source); continue; }
                        bool on = mode == 0 || mode == 2 && !phaseActive[target];
                        Add(new(InitialAppearanceEffect.Active, target, "m_IsActive", Active: off ? !on : on));
                    }
                    else if (action.Class == "BlendShapeAction" && !off)
                    {
                        string shape = Text(action.Data, "blendShape");
                        float weight = 100;
                        if (Get(action.Data, "blendShapeValue") != null && (!float.TryParse(Text(action.Data, "blendShapeValue"), NumberStyles.Float, CultureInfo.InvariantCulture, out weight) || !float.IsFinite(weight)))
                        { Error("WEIGHT", "BlendShapeAction weight must be finite.", feature.Source); continue; }
                        bool all = Boolean(action.Data, "allRenderers", true, feature.Source, report);
                        long rendererId = all ? 0 : Target(action.Data, "renderer", feature.Source);
                        if (!all && (rendererId == 0 || !context.Renderers.TryGetValue(rendererId, out var renderer) || !within(renderer.GameObjectId)))
                        { Error("TARGET", "BlendShapeAction renderer is missing or outside its owning avatar.", feature.Source); continue; }
                        var targets = context.Renderers.Where(item => within(item.Value.GameObjectId) && (all || item.Key == rendererId) && item.Value.Shapes.ContainsKey(shape)).ToArray();
                        if (shape.Length == 0 || targets.Length == 0) Unsupported("BlendShapeAction cannot find exact blendshape '" + shape + "' on its selected owned renderer(s).", feature.Source, "SHAPE_MISSING");
                        foreach (var target in targets) Add(new(InitialAppearanceEffect.Shape, target.Key, shape, Weight: weight));
                    }
                    else if (action.Class == "MaterialAction" && !off)
                    {
                        long rendererId;
                        if (Integer(action.Data, "version", 0, feature.Source, report) < 1 && Reference(action.Data, "obj") != 0)
                        {
                            long owner = Target(action.Data, "obj", feature.Source);
                            var matches = documents.Values.Where(item => item.ClassId is 23 or 137 && Reference(item.Body, "m_GameObject") == owner).ToArray();
                            if (!within(owner) || matches.Length != 1) { Error("TARGET", "Legacy MaterialAction.obj must select one unambiguous owned Renderer.", feature.Source); continue; }
                            rendererId = matches[0].Id;
                        }
                        else rendererId = Target(action.Data, "renderer", feature.Source);
                        if (!context.Renderers.TryGetValue(rendererId, out var renderer) || !within(renderer.GameObjectId))
                        { Error("TARGET", "MaterialAction Renderer is missing or outside its owning avatar.", feature.Source); continue; }
                        int index = Integer(action.Data, "materialIndex", 0, feature.Source, report);
                        if (index < 0 || index >= renderer.Materials.Count) { Error("MATERIAL_CAPACITY", "MaterialAction index is outside the captured Renderer material capacity.", feature.Source); continue; }
                        var material = AssetReference(Get(action.Data, "mat"), feature.Source, report);
                        if (material != null) Add(new(InitialAppearanceEffect.Material, rendererId, index.ToString(CultureInfo.InvariantCulture), Material: material));
                    }
                    else if (action.Class == "AnimationClipAction" && !off)
                    {
                        var reference = AssetReference(Get(action.Data, "clip"), feature.Source, report);
                        if (reference == null) continue;
                        if (clipResolver == null) { Unsupported("AnimationClipAction requires a caller-supplied exact GUID/fileID constant-clip resolver; skipped.", feature.Source, "CLIP_UNSUPPORTED"); continue; }
                        var clip = clipResolver(reference.Guid, reference.FileId);
                        if (clip == null) { Error("CLIP_MISSING", "AnimationClipAction GUID/fileID cannot be resolved from this package.", feature.Source); continue; }
                        foreach (var diagnostic in clip.Diagnostics) diagnostics.Add(diagnostic with { SourceId = feature.Source });
                        if (!clip.CanApply) continue;
                        foreach (var curve in clip.Curves)
                        {
                            long target = resolvePath(feature.Owner, curve.Path, curve.ClassId);
                            if (target == 0) { Error("CLIP_PATH", "Animation binding '" + curve.Path + "' has no exact owned target.", feature.Source); continue; }
                            if (curve.ClassId == 1 && curve.Attribute == "m_IsActive") Add(new(InitialAppearanceEffect.Active, target, "m_IsActive", Active: curve.Value != 0));
                            else if (curve.ClassId == 137 && curve.Attribute.StartsWith("blendShape.", StringComparison.Ordinal))
                            {
                                string name = curve.Attribute.Substring("blendShape.".Length);
                                var renderers = context.Renderers.Where(item => item.Value.GameObjectId == target && documents.TryGetValue(item.Key, out var document) && document.ClassId == 137).ToArray();
                                if (renderers.Length != 1 || !renderers[0].Value.Shapes.ContainsKey(name)) { Error("CLIP_SHAPE", "Animation binding has missing/ambiguous Renderer or exact blendshape '" + name + "'.", feature.Source); continue; }
                                Add(new(InitialAppearanceEffect.Shape, renderers[0].Key, name, Weight: curve.Value));
                            }
                            else Error("CLIP_BINDING", "Clip resolver returned an unsupported binding.", feature.Source);
                        }
                    }
                }
                foreach (var pair in featureWrites)
                {
                    if (phaseWrites.TryGetValue(pair.Key, out var other) && other != pair.Value)
                        Error("CONFLICT", "Different features set competing values in the same upstream phase for " + pair.Key + ". No initial writes will be applied.", feature.Source);
                    else phaseWrites[pair.Key] = pair.Value;
                }
            }
            foreach (var pair in phaseWrites)
            {
                writes[pair.Key] = pair.Value;
                if (pair.Value.Effect == InitialAppearanceEffect.Active) active[pair.Value.TargetId] = pair.Value.Active;
            }
        }
        Phase(eligible.Where(item => item.Class == "ApplyDuringUpload"), false);
        Phase(eligible.Where(item => item.Class == "Toggle"), true);
        Phase(eligible.Where(item => item.Class == "Toggle" && (Boolean(item.Data, "defaultOn", false, item.Source, report) ^ Boolean(item.Data, "invertRestLogic", false, item.Source, report))), false);
        bool safe = diagnostics.All(item => item.Severity != InitialAppearanceSeverity.Error);
        var finalRenderers = new Dictionary<long, InitialAppearanceRenderer>();
        foreach (var renderer in context.Renderers.Where(item => within(item.Value.GameObjectId)))
        {
            var shapes = renderer.Value.Shapes.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            var materials = renderer.Value.Materials.ToArray();
            if (safe)
            {
                foreach (var write in writes.Values.Where(item => item.Effect == InitialAppearanceEffect.Shape && item.TargetId == renderer.Key)) shapes[write.Property] = write.Weight;
                foreach (var write in writes.Values.Where(item => item.Effect == InitialAppearanceEffect.Material && item.TargetId == renderer.Key)) materials[int.Parse(write.Property, CultureInfo.InvariantCulture)] = write.Material!;
            }
            finalRenderers[renderer.Key] = new(renderer.Value.GameObjectId, new ReadOnlyDictionary<string, float>(shapes), Array.AsReadOnly(materials));
        }
        return new()
        {
            ActiveSelf = new ReadOnlyDictionary<long, bool>(safe ? active : authored), Renderers = new ReadOnlyDictionary<long, InitialAppearanceRenderer>(finalRenderers),
            Writes = safe ? Array.AsReadOnly(writes.Values.ToArray()) : Array.Empty<InitialAppearanceWrite>(), Diagnostics = diagnostics.AsReadOnly()
        };
    }

    private static InitialAppearanceMaterial? AssetReference(YamlNode? node, long source, Action<string, string, long, InitialAppearanceSeverity> report)
    {
        void Unsupported(string message) => report("ASSET_UNSUPPORTED", message, source, InitialAppearanceSeverity.Warning);
        if (node is not YamlMappingNode wrapper) { Unsupported("Null GuidWrapper contributes no asset binding."); return null; }
        if (!Version(wrapper, 1, source, report)) return null;
        if (Get(wrapper, "objRef") is { } objectReference && (objectReference is not YamlMappingNode objectMapping ||
            !long.TryParse(Text(objectMapping, "fileID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
        {
            report("ASSET_REFERENCE", "Malformed GuidWrapper.objRef must not fall back to id or name hints.", source, InitialAppearanceSeverity.Error);
            return null;
        }
        string guid;
        long fileId;
        if (Get(wrapper, "objRef") is YamlMappingNode reference && ReferenceNode(reference) != 0)
        {
            guid = Text(reference, "guid");
            fileId = ReferenceNode(reference);
        }
        else
        {
            string identity = Text(wrapper, "id");
            if (Integer(wrapper, "version", 0, source, report) < 1 && !string.IsNullOrEmpty(Text(wrapper, "guid")))
                identity = Text(wrapper, "guid") + ":" + Text(wrapper, "fileID");
            string[] parts = identity.Split('|')[0].Split(':');
            guid = parts[0];
            if (parts.Length != 2 || !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out fileId))
            { Unsupported("GuidWrapper requires an exact GUID/fileID or objRef; main-asset-only/name hints are not substituted."); return null; }
        }
        if (!Regex.IsMatch(guid, @"\A[0-9a-fA-F]{32}\z") || fileId == 0)
        { Unsupported("GuidWrapper asset identity is local, null or lacks an exact fileID; binding skipped."); return null; }
        return new(fileId, guid.ToLowerInvariant());
    }

    private static bool Version(YamlMappingNode data, int latest, long source, Action<string, string, long, InitialAppearanceSeverity> report)
    {
        int version = Math.Max(0, Integer(data, "version", 0, source, report));
        if (version >= 0 && version <= latest) return true;
        report("VERSION", "Unsupported serialized version " + version + "; pinned latest is " + latest + ".", source, InitialAppearanceSeverity.Error);
        return false;
    }
    private static int Integer(YamlMappingNode data, string field, int fallback, long source, Action<string, string, long, InitialAppearanceSeverity> report)
    {
        if (Get(data, field) == null) return fallback;
        if (int.TryParse(Text(data, field), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)) return value;
        report("FIELD", "Malformed integer " + field + ".", source, InitialAppearanceSeverity.Error);
        return fallback;
    }
    private static bool Boolean(YamlMappingNode data, string field, bool fallback, long source, Action<string, string, long, InitialAppearanceSeverity> report)
    {
        if (Get(data, field) == null) return fallback;
        string value = Text(data, field);
        if (value is "1" or "true") return true;
        if (value is "0" or "false") return false;
        report("FIELD", "Malformed boolean " + field + ".", source, InitialAppearanceSeverity.Error);
        return fallback;
    }
    private static Dictionary<long, Document> Read(string yaml, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        var documents = new Dictionary<long, Document>();
        var headers = Header.Matches(yaml);
        if (headers.Count == 0 && !string.IsNullOrWhiteSpace(yaml)) throw new InvalidDataException("No Unity document headers.");
        for (int index = 0; index < headers.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var header = headers[index];
            int start = header.Index + header.Length;
            int end = index + 1 < headers.Count ? headers[index + 1].Index : yaml.Length;
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml.Substring(start, end - start)));
            if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode root || root.Children.Count != 1 || root.Children.Values.Single() is not YamlMappingNode body) throw new InvalidDataException("Malformed Unity document root.");
            long identity = long.Parse(header.Groups["id"].Value, CultureInfo.InvariantCulture);
            if (identity == 0 || !documents.TryAdd(identity, new(identity, int.Parse(header.Groups["class"].Value, CultureInfo.InvariantCulture), body))) throw new InvalidDataException("Duplicate or invalid Unity document identity.");
        }
        return documents;
    }
    private static bool IsComponent(Document document) => document.ClassId == 114 && string.Equals(Text(Get(document.Body, "m_Script") as YamlMappingNode, "guid"), ScriptGuid, StringComparison.OrdinalIgnoreCase);
    private static YamlNode? Get(YamlMappingNode? mapping, string key) => mapping != null && mapping.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;
    private static string Text(YamlMappingNode? mapping, string key) => (Get(mapping, key) as YamlScalarNode)?.Value ?? "";
    private static IEnumerable<YamlNode> Sequence(YamlMappingNode? mapping, string key) => Get(mapping, key) is YamlSequenceNode sequence ? sequence.Children : Array.Empty<YamlNode>();
    private static long Reference(YamlMappingNode mapping, string key) => ReferenceNode(Get(mapping, key));
    private static long ReferenceNode(YamlNode? node) => node is YamlMappingNode mapping && long.TryParse(Text(mapping, "fileID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long identity) ? identity : 0;

    private static void ReadFeatures(Dictionary<long, Document> documents, Func<long, bool> within, List<Feature> features,
        Action<string, string, long, InitialAppearanceSeverity> report, CancellationToken token)
    {
        foreach (var component in documents.Values.Where(IsComponent).Where(item => within(Reference(item.Body, "m_GameObject"))))
        {
            token.ThrowIfCancellationRequested();
            void Error(string code, string message) => report(code, message, component.Id, InitialAppearanceSeverity.Error);
            void Unsupported(string message) => report("UNSUPPORTED", message, component.Id, InitialAppearanceSeverity.Warning);
            if (!Version(component.Body, 3, component.Id, report)) continue;
            var graph = VrcFuryManagedReferenceGraph.Read(component.Body, token);
            foreach (var diagnostic in graph.Diagnostics) Error(diagnostic.Code, diagnostic.Message);
            if (!graph.IsValid) continue;
            var table = new Dictionary<long, Managed>();
            var references = Get(component.Body, "references") as YamlMappingNode;
            if (references != null && Text(references, "version") != "2")
            { Unsupported("Managed-reference serialization version is not 2; component skipped."); continue; }
            foreach (var entry in graph.Records.Values)
            {
                if (entry is not YamlMappingNode record || !long.TryParse(Text(record, "rid"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long identity) || identity < 0 ||
                    Get(record, "type") is not YamlMappingNode type || Get(record, "data") is not YamlMappingNode data)
                { Error("RID", "Malformed managed-reference table entry."); continue; }
                if (!table.TryAdd(identity, new(Text(type, "class"), Text(type, "ns"), Text(type, "asm"), data)))
                    Error("RID_DUPLICATE", "Duplicate managed-reference ID " + identity + ".");
            }
            var stack = new HashSet<long>();
            Managed? Resolve(YamlNode? node, string expectedNamespace)
            {
                if (node is not YamlMappingNode pointer || !long.TryParse(Text(pointer, "rid"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long identity))
                { Error("RID", "Expected a managed-reference rid mapping."); return null; }
                if (identity == -2) return null;
                if (identity < 0 || !table.TryGetValue(identity, out var value))
                { Error("RID_MISSING", "Unresolved managed-reference ID " + identity + "."); return null; }
                if (stack.Contains(identity))
                { Error("RID_CYCLE", "Cyclic reachable managed-reference ID " + identity + "."); return null; }
                if (value.Namespace != expectedNamespace || value.Assembly != "VRCFury")
                { Unsupported("Managed type " + value.Namespace + "." + value.Class + " in " + value.Assembly + " is not a pinned VRCFury type."); return null; }
                return value;
            }
            IReadOnlyList<Managed> Actions(YamlNode? state)
            {
                if (state == null) return Array.Empty<Managed>();
                long? stateId = null;
                YamlMappingNode? data = state as YamlMappingNode;
                if (data != null && Get(data, "rid") != null)
                {
                    var resolved = Resolve(state, "VF.Model");
                    if (resolved == null) return Array.Empty<Managed>();
                    if (resolved.Class != "State") { Error("STATE", "Expected VF.Model.State, not " + resolved.Class + "."); return Array.Empty<Managed>(); }
                    stateId = long.Parse(Text(data, "rid"), CultureInfo.InvariantCulture);
                    stack.Add(stateId.Value);
                    data = resolved.Data;
                }
                if (data == null || Get(data, "actions") is not YamlSequenceNode sequence)
                {
                    if (data == null || Get(data, "actions") != null) Error("STATE", "Reachable State.actions must be a sequence.");
                    if (stateId.HasValue) stack.Remove(stateId.Value);
                    return Array.Empty<Managed>();
                }
                var actions = new List<Managed>();
                foreach (var action in sequence.Children)
                {
                    var resolved = Resolve(action, "VF.Model.StateAction");
                    if (resolved != null) actions.Add(resolved);
                    else if (action is YamlMappingNode nullAction && Text(nullAction, "rid") == "-2") Error("ACTION", "Action list contains a null/corrupt action.");
                }
                if (stateId.HasValue) stack.Remove(stateId.Value);
                return actions.AsReadOnly();
            }
            var roots = graph.RootIds.Select(identity => (YamlNode)new YamlMappingNode { { "rid", new YamlScalarNode(identity.ToString(CultureInfo.InvariantCulture)) } }).ToList();
            if (roots.Count == 0) Error("CONTENT", "VRCFury component has no reachable content or legacy config.features.");
            foreach (var root in roots)
            {
                var feature = Resolve(root, "VF.Model.Feature");
                if (feature == null) continue;
                long rootId = long.Parse(Text((YamlMappingNode)root, "rid"), CultureInfo.InvariantCulture);
                stack.Add(rootId);
                if (feature.Class is not ("ApplyDuringUpload" or "Toggle")) Unsupported("Feature '" + feature.Class + "' has no reversible INITIAL static conversion; structural conversion is separate.");
                else if (Version(feature.Data, feature.Class == "Toggle" ? 3 : 0, component.Id, report))
                    features.Add(new(component.Id, Reference(component.Body, "m_GameObject"), feature.Class, feature.Data, Actions(Get(feature.Data, feature.Class == "Toggle" ? "state" : "action"))));
                stack.Remove(rootId);
            }
        }
    }
}
