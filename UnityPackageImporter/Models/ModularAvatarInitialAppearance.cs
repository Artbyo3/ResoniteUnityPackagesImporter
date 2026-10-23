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

internal static class InitialAppearanceTransaction
{
    internal static void Apply(IReadOnlyList<(Action Apply, Action Restore, Func<bool> Valid)> pending, CancellationToken token)
    {
        int attempted = 0;
        try
        {
            foreach (var write in pending)
            {
                token.ThrowIfCancellationRequested();
                if (!write.Valid()) throw new InvalidOperationException("Initial appearance destination changed after preflight.");
                attempted++;
                write.Apply();
            }
        }
        catch (Exception failure)
        {
            var failures = new List<Exception> { failure };
            for (int index = attempted - 1; index >= 0; index--)
                try { pending[index].Restore(); }
                catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            if (failures.Count > 1) throw new AggregateException(failures);
            throw;
        }
    }
}

internal sealed class InitialAppearanceMaterialMetadata<T>
{
    private readonly T[] originalEntries;
    internal List<T>? Original { get; }
    internal List<T> Replacement { get; }

    internal InitialAppearanceMaterialMetadata(List<T>? original, int nativeSlotCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nativeSlotCount);
        Original = original;
        originalEntries = original?.ToArray() ?? Array.Empty<T>();
        Replacement = new List<T>(originalEntries);
        while (Replacement.Count < nativeSlotCount) Replacement.Add(default!);
    }

    internal bool IsUnchanged(List<T>? current) => ReferenceEquals(current, Original) &&
        (current == null || current.SequenceEqual(originalEntries));
}

public enum InitialAppearanceSeverity { Info, Warning, Error }
public enum InitialAppearanceEffect { Active, Shape, Material }
public sealed record InitialAppearanceDiagnostic(InitialAppearanceSeverity Severity, string Code, string Message, long SourceId = 0);
public sealed record InitialAppearanceMaterial(long FileId, string Guid);
public sealed record InitialAppearanceWrite(InitialAppearanceEffect Effect, long TargetId, string Property,
    bool Active = false, float Weight = 0, InitialAppearanceMaterial? Material = null);
public sealed record InitialAppearanceRenderer(long GameObjectId, IReadOnlyDictionary<string, float> Shapes,
    IReadOnlyList<InitialAppearanceMaterial> Materials);

public sealed class InitialAppearanceContext
{
    public long AvatarRootId { get; init; }
    public IReadOnlyDictionary<long, InitialAppearanceRenderer> Renderers { get; init; } = new Dictionary<long, InitialAppearanceRenderer>();
    public bool ExternalAnimationAndParameterContextVerifiedAbsent { get; init; }
}

public sealed class InitialAppearanceResult
{
    public IReadOnlyDictionary<long, bool> ActiveSelf { get; internal init; } = new Dictionary<long, bool>();
    public IReadOnlyDictionary<long, InitialAppearanceRenderer> Renderers { get; internal init; } = new Dictionary<long, InitialAppearanceRenderer>();
    public IReadOnlyList<InitialAppearanceWrite> Writes { get; internal init; } = Array.Empty<InitialAppearanceWrite>();
    public IReadOnlyList<InitialAppearanceDiagnostic> Diagnostics { get; internal init; } = Array.Empty<InitialAppearanceDiagnostic>();
    public bool HasUnsupportedContext { get; internal init; }
    public bool CanApply => !HasUnsupportedContext && Diagnostics.All(item => item.Severity != InitialAppearanceSeverity.Error);
}

/// <summary>Static subset based on modular-avatar f21ad465bb623e841b532a66aa555dbcddf2db47.</summary>
public static class ModularAvatarInitialAppearance
{
    public const string ObjectToggleGuid = "a162bb8ec7e24a5abcf457887f1df3fa";
    public const string ShapeChangerGuid = "2db441f589c3407bb6fb5f02ff8ab541";
    public const string MaterialSetterGuid = "0adf335711644e34b6c635e94ae61fa7";
    public const string MenuItemGuid = "3b29d45007c5493d926d2cd45a489529";
    public const string MergeAnimatorGuid = "1bb122659f724ebf85fe095ac02dc339";
    private static readonly Regex Header = new(@"^--- !u!(?<class>\d+) &(?<id>-?\d+)(?: stripped)?\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private sealed record Document(long Id, int ClassId, YamlMappingNode Body);

    internal static bool IsExactMaterialAsset(string yaml, long fileId)
    {
        var headers = Header.Matches(yaml);
        if (headers.Count != 1 || headers[0].Groups["class"].Value != "21" ||
            !long.TryParse(headers[0].Groups["id"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long identity) ||
            identity != fileId || fileId == 0) return false;
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        return stream.Documents.Count == 1 && stream.Documents[0].RootNode is YamlMappingNode root &&
            root.Children.Count == 1 && Get(root, "Material") is YamlMappingNode;
    }

    internal static ((int Effect, long Target, string Property) Key, string Value) InitialWriteIdentity(InitialAppearanceWrite write)
    {
        string property = write.Effect == InitialAppearanceEffect.Active ? "m_IsActive" : write.Property;
        if (write.Effect == InitialAppearanceEffect.Material && int.TryParse(property, NumberStyles.None, CultureInfo.InvariantCulture, out int index))
            property = index.ToString(CultureInfo.InvariantCulture);
        string value = write.Effect switch
        {
            InitialAppearanceEffect.Active => write.Active ? "1" : "0",
            InitialAppearanceEffect.Shape => (write.Weight == 0 ? 0f : write.Weight).ToString("R", CultureInfo.InvariantCulture),
            InitialAppearanceEffect.Material when write.Material != null => write.Material.Guid.ToLowerInvariant() + ":" + write.Material.FileId.ToString(CultureInfo.InvariantCulture),
            _ => throw new InvalidDataException("Invalid initial appearance write identity.")
        };
        return (((int)write.Effect, write.TargetId, property), value);
    }

    internal static bool InitialWriteConflicts(IReadOnlyDictionary<(int Effect, long Target, string Property), string> ledger,
        ((int Effect, long Target, string Property) Key, string Value) identity) =>
        ledger.TryGetValue(identity.Key, out string? previous) && previous != identity.Value;
    private sealed record Rule(long Source, long Owner, long Affected, bool Inverted, InitialAppearanceWrite Write);

    public static bool ContainsReactiveComponents(string composedYaml, CancellationToken token = default) =>
        Read(composedYaml, token).Values.Any(item => IsReactive(Script(item)));

    /// <summary>Computes a stable static snapshot with authored fallbacks, not transient simulator proxy states.</summary>
    public static InitialAppearanceResult Evaluate(string composedYaml, InitialAppearanceContext context, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var documents = Read(composedYaml, token);
        var diagnostics = new List<InitialAppearanceDiagnostic>();
        bool unsupported = false;
        void Report(string code, string message, long source = 0, InitialAppearanceSeverity severity = InitialAppearanceSeverity.Error) =>
            diagnostics.Add(new(severity, code, message, source));
        void Unsupported(string code, string message, long source = 0)
        {
            unsupported = true;
            Report(code, message, source, InitialAppearanceSeverity.Warning);
        }
        var gameObjects = documents.Values.Where(item => item.ClassId == 1).ToDictionary(item => item.Id);
        var transformByObject = new Dictionary<long, Document>();
        var parent = new Dictionary<long, long>();
        foreach (var transform in documents.Values.Where(item => item.ClassId is 4 or 224))
        {
            long owner = Reference(transform.Body, "m_GameObject");
            if (!transformByObject.TryAdd(owner, transform)) Report("MA_INITIAL_HIERARCHY", "Duplicate Transform ownership.", transform.Id);
            long father = Reference(transform.Body, "m_Father");
            if (father != 0 && (!documents.TryGetValue(father, out var parentTransform) || parentTransform.ClassId is not (4 or 224)))
                Report("MA_INITIAL_HIERARCHY", "Unresolved parent Transform.", transform.Id);
            else parent[owner] = father == 0 ? 0 : Reference(documents[father].Body, "m_GameObject");
        }
        bool Within(long candidate, long ancestor)
        {
            var visited = new HashSet<long>();
            while (candidate != 0 && visited.Add(candidate))
            {
                if (candidate == ancestor) return true;
                candidate = parent.GetValueOrDefault(candidate);
            }
            return false;
        }
        if (context.AvatarRootId == 0 || !gameObjects.ContainsKey(context.AvatarRootId) || !transformByObject.ContainsKey(context.AvatarRootId))
        {
            Report("MA_INITIAL_ROOT", "An explicit assembled avatar GameObject identity with a Transform is required.");
            return new() { Diagnostics = diagnostics.AsReadOnly() };
        }
        var scoped = gameObjects.Keys.Where(identity => Within(identity, context.AvatarRootId)).ToHashSet();
        var ordered = new List<long>();
        var visitedObjects = new HashSet<long>();
        void Visit(long identity)
        {
            token.ThrowIfCancellationRequested();
            if (!visitedObjects.Add(identity))
            {
                Report("MA_INITIAL_HIERARCHY", "Cyclic or repeated hierarchy child.", identity);
                return;
            }
            ordered.Add(identity);
            if (!transformByObject.TryGetValue(identity, out var transform))
            {
                Report("MA_INITIAL_HIERARCHY", "Missing Transform.", identity);
                return;
            }
            foreach (var child in Sequence(transform.Body, "m_Children"))
            {
                long childTransform = ReferenceNode(child);
                if (!documents.TryGetValue(childTransform, out var childDocument) || childDocument.ClassId is not (4 or 224))
                {
                    Report("MA_INITIAL_HIERARCHY", "Unresolved child Transform.", identity);
                    continue;
                }
                long childObject = Reference(childDocument.Body, "m_GameObject");
                if (!scoped.Contains(childObject) || parent.GetValueOrDefault(childObject) != identity)
                    Report("MA_INITIAL_HIERARCHY", "Child and parent identities disagree.", childTransform);
                else Visit(childObject);
            }
        }
        Visit(context.AvatarRootId);
        if (!scoped.SetEquals(ordered)) Report("MA_INITIAL_HIERARCHY", "Incomplete child order; precedence cannot be inferred.");
        var active = scoped.ToDictionary(identity => identity, identity => Text(gameObjects[identity].Body, "m_IsActive") != "0");
        var originalActive = new Dictionary<long, bool>(active);
        foreach (long identity in scoped)
            if (Text(gameObjects[identity].Body, "m_IsActive") is not ("0" or "1"))
                Report("MA_INITIAL_VALUE", "Missing or malformed authored activeSelf; no initial state may be invented.", identity);
        var components = new List<Document>();
        foreach (long identity in ordered)
        {
            var componentIds = new HashSet<long>();
            foreach (var entry in Sequence(gameObjects[identity].Body, "m_Component"))
            {
                long componentId = Reference(entry as YamlMappingNode, "component");
                if (!componentIds.Add(componentId) || !documents.TryGetValue(componentId, out var component) || Reference(component.Body, "m_GameObject") != identity)
                    Report("MA_INITIAL_COMPONENT", "Invalid component identity in hierarchy order.", identity);
                else components.Add(component);
            }
        }
        foreach (var component in documents.Values.Where(item => IsReactive(Script(item)) && scoped.Contains(Reference(item.Body, "m_GameObject"))))
            if (!components.Contains(component)) Report("MA_INITIAL_COMPONENT", "Reactive component is absent from its owner's ordered list.", component.Id);
        var reactive = components.Where(item => IsReactive(Script(item))).ToList();
        if (reactive.Count == 0) return new() { ActiveSelf = active, Diagnostics = diagnostics.AsReadOnly() };
        Report("MA_INITIAL_STATIC_ONLY", "Only deterministic INITIAL appearance is evaluated; runtime menus, animations, parameters and drivers are not converted.", severity: InitialAppearanceSeverity.Warning);
        if (!context.ExternalAnimationAndParameterContextVerifiedAbsent)
            Unsupported("MA_INITIAL_EXTERNAL_CONTEXT", "Caller has not verified absence of external animation/parameter overrides; unsafe to apply.");
        foreach (var component in components)
            if ((component.ClassId == 95 && Reference(component.Body, "m_Controller") != 0) || component.ClassId == 111 ||
                (Script(component) == MergeAnimatorGuid && Reference(component.Body, "animator") != 0) ||
                Sequence(component.Body, "baseAnimationLayers").Concat(Sequence(component.Body, "specialAnimationLayers"))
                    .OfType<YamlMappingNode>().Any(layer => Reference(layer, "animatorController") != 0) || Reference(component.Body, "expressionParameters") != 0)
                Unsupported("MA_INITIAL_ANIMATION_CONTEXT", "Animation or avatar parameter context requires independent analysis.", component.Id);
        var menus = components.Where(item => Script(item) == MenuItemGuid).GroupBy(item => Reference(item.Body, "m_GameObject"))
            .ToDictionary(group => group.Key, group => group.ToList());
        long Resolve(YamlMappingNode? reference, long source)
        {
            string path = Text(reference, "referencePath");
            long target = Reference(reference, "targetObject");
            if (Get(reference, "targetObject") is YamlNode targetReference &&
                (targetReference is not YamlMappingNode targetMapping || !long.TryParse(Text(targetMapping, "fileID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
            {
                Report("MA_INITIAL_TARGET", "Malformed explicit object identity cannot become a path-only reference.", source);
                return 0;
            }
            if (path.Length == 0)
            {
                Report("MA_INITIAL_TARGET", "Empty AvatarObjectReference is unresolved, even with targetObject.", source);
                return 0;
            }
            if (target != 0)
            {
                if (scoped.Contains(target) && Text(Get(reference, "targetObject") as YamlMappingNode, "guid").Length == 0) return target;
                Report("MA_INITIAL_TARGET", "Explicit target is missing, external or outside this avatar; no path/name borrowing is permitted.", source);
                return 0;
            }
            if (path == "$$$AVATAR_ROOT$$$") return context.AvatarRootId;
            long cursor = context.AvatarRootId;
            foreach (string segment in path.Split('/'))
            {
                var matches = ordered.Where(identity => parent.GetValueOrDefault(identity) == cursor && Text(gameObjects[identity].Body, "m_Name") == segment).ToList();
                if (matches.Count != 1)
                {
                    Report("MA_INITIAL_TARGET", "Avatar-relative path is missing or ambiguous: " + path, source);
                    return 0;
                }
                cursor = matches[0];
            }
            return cursor;
        }
        long Renderer(long target, long source)
        {
            var matches = components.Where(item => (item.ClassId is 23 or 137) && Reference(item.Body, "m_GameObject") == target).ToList();
            if (matches.Count != 1 || !context.Renderers.TryGetValue(matches[0].Id, out var snapshot) || snapshot.GameObjectId != target)
            {
                Report("MA_INITIAL_RENDERER", "Target needs one exact renderer and an imported snapshot; no name-based fallback.", source);
                return 0;
            }
            return matches[0].Id;
        }
        var rules = new List<Rule>();
        foreach (var component in reactive)
        {
            token.ThrowIfCancellationRequested();
            string script = Script(component);
            if (Get(component.Body, "m_inverted") != null && Text(component.Body, "m_inverted") is not ("0" or "1"))
                Report("MA_INITIAL_VALUE", "Malformed reactive inversion boolean.", component.Id);
            var entries = Get(component.Body, script == ShapeChangerGuid ? "m_shapes" : "m_objects") ?? (script == ShapeChangerGuid ? Get(component.Body, "Shapes") : null);
            if (entries is not YamlSequenceNode sequence)
            {
                Report("MA_INITIAL_ENTRIES", "Missing or invalid reactive entry list.", component.Id);
                continue;
            }
            foreach (var entry in sequence.Children)
            {
                if (entry is not YamlMappingNode mapping)
                {
                    Report("MA_INITIAL_ENTRIES", "Malformed reactive entry.", component.Id);
                    continue;
                }
                if (script == ShapeChangerGuid && Text(mapping, "ChangeType") == "0")
                {
                    diagnostics.Add(new(InitialAppearanceSeverity.Warning, "MA_INITIAL_DESTRUCTIVE_SKIPPED",
                        "ShapeChanger Delete skipped by nondestructive initial-only policy; other reversible entries remain eligible.", component.Id));
                    continue;
                }
                if (script == ShapeChangerGuid && Text(mapping, "ChangeType") != "1")
                {
                    Unsupported("MA_INITIAL_SHAPE_CHANGE_TYPE", "Unknown ShapeChanger ChangeType; no operation is substituted.", component.Id);
                    continue;
                }
                long target = Resolve(Get(mapping, "Object") as YamlMappingNode, component.Id);
                if (target == 0) continue;
                InitialAppearanceWrite write;
                if (script == ObjectToggleGuid)
                {
                    string value = Text(mapping, "Active");
                    if (value is not ("0" or "1"))
                    {
                        Report("MA_INITIAL_VALUE", "ObjectToggle Active must be a serialized boolean.", component.Id);
                        continue;
                    }
                    write = new(InitialAppearanceEffect.Active, target, "m_IsActive", Active: value == "1");
                }
                else
                {
                    long renderer = Renderer(target, component.Id);
                    if (renderer == 0) continue;
                    var snapshot = context.Renderers[renderer];
                    if (script == ShapeChangerGuid)
                    {
                        string shape = Text(mapping, "ShapeName");
                        if (documents[renderer].ClassId != 137 || !snapshot.Shapes.ContainsKey(shape) ||
                            !float.TryParse(Text(mapping, "Value"), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value))
                        {
                            Report("MA_INITIAL_SHAPE", "Missing skinned blendshape or invalid Set weight.", component.Id);
                            continue;
                        }
                        write = new(InitialAppearanceEffect.Shape, renderer, shape, Weight: value);
                    }
                    else
                    {
                        var material = Get(mapping, "Material") as YamlMappingNode;
                        string guid = Text(material, "guid");
                        long materialId = ReferenceNode(material);
                        if (!int.TryParse(Text(mapping, "MaterialIndex"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) || index < 0 || index >= snapshot.Materials.Count ||
                            material == null || !long.TryParse(Text(material, "fileID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ||
                            (materialId != 0 && (guid.Length != 32 || !guid.All(Uri.IsHexDigit))))
                        {
                            Report("MA_INITIAL_MATERIAL", "Invalid material slot or unresolved external material identity.", component.Id);
                            continue;
                        }
                        write = new(InitialAppearanceEffect.Material, renderer, index.ToString(CultureInfo.InvariantCulture), Material: new(materialId, guid.ToLowerInvariant()));
                    }
                }
                rules.Add(new(component.Id, Reference(component.Body, "m_GameObject"), script == ObjectToggleGuid ? target : 0,
                    Text(component.Body, "m_inverted") == "1", write));
            }
        }
        bool MenuState(long owner, long source)
        {
            var candidates = menus[owner];
            var menu = candidates[0];
            var control = Get(menu.Body, "Control") as YamlMappingNode;
            string parameter = Text(Get(control, "parameter") as YamlMappingNode, "name");
            if (candidates.Count != 1 || control == null || Text(control, "type") is not ("1" or "2") || parameter.Length != 0 ||
                Get(control, "parameter") is not YamlMappingNode || Text(menu.Body, "isDefault") is not ("0" or "1") ||
                !float.TryParse(Text(control, "value"), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value) ||
                components.Count(item => Script(item) == MenuItemGuid && Text(gameObjects[Reference(item.Body, "m_GameObject")].Body, "m_Name") == Text(gameObjects[owner].Body, "m_Name")) > 1)
            {
                Unsupported("MA_INITIAL_MENU_CONTEXT", "Nearest controlling menu is ambiguous, named or not an anonymous Button/Toggle; default cannot be inferred.", source);
                return false;
            }
            if (Text(menu.Body, "automaticValue") != "1" && (value < 0 || value > 255 || value != MathF.Truncate(value)))
            {
                Unsupported("MA_INITIAL_MENU_CONTEXT", "Nonautomatic anonymous menu value requires parameter type/allocation analysis.", source);
                return false;
            }
            return Text(menu.Body, "isDefault") == "1";
        }
        var conditions = new Dictionary<Rule, (long[] Objects, bool Menu)>();
        foreach (var rule in rules)
        {
            var controls = new List<long>();
            bool seenMenu = false;
            bool menuActive = true;
            for (long cursor = rule.Owner; cursor != 0 && cursor != context.AvatarRootId; cursor = parent.GetValueOrDefault(cursor))
            {
                if (!seenMenu && menus.ContainsKey(cursor))
                {
                    seenMenu = true;
                    menuActive = MenuState(cursor, rule.Source);
                }
                if (rule.Affected == 0 || !Within(rule.Affected, cursor)) controls.Add(cursor);
            }
            conditions[rule] = (controls.ToArray(), menuActive);
        }
        bool Enabled(Rule rule, IReadOnlyDictionary<long, bool> states)
        {
            var condition = conditions[rule];
            return (condition.Menu && condition.Objects.All(identity => states[identity])) ^ rule.Inverted;
        }
        foreach (var group in rules.GroupBy(rule => (rule.Source, rule.Write.Effect, rule.Write.TargetId, rule.Write.Property)))
            if (group.Select(rule => rule.Write).Distinct().Count() > 1)
                Report("MA_INITIAL_CONFLICT", "One component contains conflicting writes to the same property.", group.Key.Source);
        foreach (var group in rules.GroupBy(rule => (rule.Write.Effect, rule.Write.TargetId, rule.Write.Property)))
            if (group.Select(rule => rule.Source).Distinct().Count() > 1)
                Report("MA_INITIAL_PRECEDENCE", "Multiple rules: last active rule in serialized hierarchy/component order wins; inactive rules retain fallback.", severity: InitialAppearanceSeverity.Info);
        bool settled = false;
        for (int iteration = 0; iteration < Math.Min(scoped.Count + 2, 1024); iteration++)
        {
            token.ThrowIfCancellationRequested();
            var next = new Dictionary<long, bool>(originalActive);
            foreach (var rule in rules.Where(item => item.Write.Effect == InitialAppearanceEffect.Active))
                if (Enabled(rule, active)) next[rule.Write.TargetId] = rule.Write.Active;
            settled = next.All(item => active[item.Key] == item.Value);
            active = next;
            if (settled) break;
        }
        if (!settled) Report("MA_INITIAL_UNSTABLE", "Toggle dependency states do not converge; no initial writes are safe.");
        var writes = new Dictionary<(InitialAppearanceEffect, long, string), InitialAppearanceWrite>();
        foreach (var rule in rules)
            if (rule.Write.Effect != InitialAppearanceEffect.Active && Enabled(rule, active))
                writes[(rule.Write.Effect, rule.Write.TargetId, rule.Write.Property)] = rule.Write;
        foreach (long target in rules.Where(item => item.Write.Effect == InitialAppearanceEffect.Active && Enabled(item, active)).Select(item => item.Write.TargetId).Distinct())
            writes[(InitialAppearanceEffect.Active, target, "m_IsActive")] = new(InitialAppearanceEffect.Active, target, "m_IsActive", Active: active[target]);
        bool safe = !unsupported && diagnostics.All(item => item.Severity != InitialAppearanceSeverity.Error);
        var finalRenderers = new Dictionary<long, InitialAppearanceRenderer>();
        foreach (var renderer in context.Renderers)
        {
            if (!scoped.Contains(renderer.Value.GameObjectId)) continue;
            var shapes = renderer.Value.Shapes.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            var materials = renderer.Value.Materials.ToArray();
            if (safe)
                foreach (var write in writes.Values.Where(item => item.TargetId == renderer.Key))
                {
                    if (write.Effect == InitialAppearanceEffect.Shape) shapes[write.Property] = write.Weight;
                    if (write.Effect == InitialAppearanceEffect.Material) materials[int.Parse(write.Property, CultureInfo.InvariantCulture)] = write.Material!;
                }
            finalRenderers[renderer.Key] = new(renderer.Value.GameObjectId, shapes, materials);
        }
        return new() { ActiveSelf = safe ? active : originalActive, Renderers = finalRenderers, Writes = safe ? writes.Values.ToArray() : Array.Empty<InitialAppearanceWrite>(),
            Diagnostics = diagnostics.AsReadOnly(), HasUnsupportedContext = unsupported };
    }

    private static Dictionary<long, Document> Read(string yaml, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        token.ThrowIfCancellationRequested();
        var result = new Dictionary<long, Document>();
        var headers = Header.Matches(yaml);
        if (headers.Count == 0 && !string.IsNullOrWhiteSpace(yaml)) throw new InvalidDataException("No composed Unity YAML headers.");
        for (int index = 0; index < headers.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var header = headers[index];
            int start = header.Index + header.Length;
            int end = index + 1 < headers.Count ? headers[index + 1].Index : yaml.Length;
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml.Substring(start, end - start)));
            if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode root || root.Children.Count != 1 || root.Children.Values.Single() is not YamlMappingNode body)
                throw new InvalidDataException("Invalid composed Unity YAML object.");
            long identity = long.Parse(header.Groups["id"].Value, CultureInfo.InvariantCulture);
            if (identity == 0 || !result.TryAdd(identity, new(identity, int.Parse(header.Groups["class"].Value, CultureInfo.InvariantCulture), body)))
                throw new InvalidDataException("Duplicate or invalid composed Unity object identity.");
        }
        return result;
    }
    private static bool IsReactive(string guid) => guid is ObjectToggleGuid or ShapeChangerGuid or MaterialSetterGuid;
    private static string Script(Document document) => document.ClassId == 114 ? Text(Get(document.Body, "m_Script") as YamlMappingNode, "guid").ToLowerInvariant() : "";
    private static YamlNode? Get(YamlMappingNode? mapping, string name) => mapping != null && mapping.Children.TryGetValue(new YamlScalarNode(name), out var value) ? value : null;
    private static string Text(YamlMappingNode? mapping, string name) => (Get(mapping, name) as YamlScalarNode)?.Value ?? "";
    private static IEnumerable<YamlNode> Sequence(YamlMappingNode mapping, string name) => Get(mapping, name) is YamlSequenceNode sequence ? sequence.Children : Array.Empty<YamlNode>();
    private static long Reference(YamlMappingNode? mapping, string name) => ReferenceNode(Get(mapping, name));
    private static long ReferenceNode(YamlNode? node) => node is YamlMappingNode mapping && long.TryParse(Text(mapping, "fileID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long identity) ? identity : 0;
}
