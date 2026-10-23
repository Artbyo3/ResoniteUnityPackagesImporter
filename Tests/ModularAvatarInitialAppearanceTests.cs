using System.Globalization;
using System.Text;
using UnityPackageImporter.Models;

internal static class ModularAvatarInitialAppearanceTests
{
    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        test("MA initial toggles invert the entire ancestor conjunction and ignore component enabled", () =>
        {
            var nodes = Standard(false);
            var plain = Evaluate(Scene(nodes, Toggle(1002, 2, 3, false)));
            check(plain.CanApply && plain.ActiveSelf[3] && plain.Writes.Count == 0, "Disabled ancestor should retain authored fallback.");
            var inverted = Evaluate(Scene(nodes, Toggle(1002, 2, 3, false, true)));
            check(inverted.CanApply && !inverted.ActiveSelf[3], "Inversion must follow conjunction, not invert each parent.");
            var disabledComponent = Evaluate(Scene(Standard(), Toggle(1002, 2, 3, false) with { Extra = "  m_Enabled: 0\n" }));
            check(disabledComponent.CanApply && !disabledComponent.ActiveSelf[3], "Upstream enumerates disabled ReactiveComponents.");
        });
        test("MA initial BuildConditions excludes toggle target ancestors but not shape ancestors", () =>
        {
            var nodes = new[] { new Node(1, 0, "Avatar"), new Node(2, 1, "Controller", false), new Node(3, 2, "Body") };
            var toggle = Evaluate(Scene(nodes, Toggle(1002, 2, 3, false)));
            check(toggle.CanApply && !toggle.ActiveSelf[3], "Affected-object ancestor must not guard ObjectToggle.");
            var shape = Evaluate(Scene(nodes, Shape(1002, 2), Renderer()));
            check(shape.CanApply && shape.Renderers[2003].Shapes["Smile"] == 12, "Shape rules pass null affectedObject and retain disabled ancestor condition.");
        });
        test("MA initial hierarchy precedence uses child order rather than YAML document order", () =>
        {
            var nodes = new[] { new Node(1, 0, "Avatar", false), new Node(2, 1, "First"), new Node(4, 1, "Later"), new Node(3, 1, "Body") };
            string yaml = Scene(nodes, Toggle(1004, 4, 3, true), Toggle(1002, 2, 3, false));
            var result = Evaluate(yaml);
            check(result.CanApply && result.ActiveSelf[3], "Last hierarchy rule must win; avatar root activity is not a condition.");
            check(result.Diagnostics.Any(item => item.Code == "MA_INITIAL_PRECEDENCE"), "Competing rules require precedence diagnostic.");
            var fallback = Evaluate(Scene(nodes.Select(item => item.Id == 4 ? item with { Active = false } : item).ToArray(), Toggle(1004, 4, 3, true), Toggle(1002, 2, 3, false)));
            check(fallback.CanApply && !fallback.ActiveSelf[3], "Inactive later rule must fall back to earlier active rule.");
        });
        test("MA initial conflicting entries fail closed", () =>
        {
            var component = Toggle(1002, 2, 3, false);
            component = component with { Fields = component.Fields + ToggleEntry(3, true) };
            var result = Evaluate(Scene(Standard(), component));
            check(!result.CanApply && result.Writes.Count == 0 && result.Diagnostics.Any(item => item.Code == "MA_INITIAL_CONFLICT"), "Ambiguous writes must not be applied.");
        });
        test("MA initial references never borrow across avatars or ambiguous names", () =>
        {
            var nodes = Standard().Concat(new[] { new Node(8, 0, "OtherAvatar"), new Node(9, 8, "Body") }).ToArray();
            var foreign = Evaluate(Scene(nodes, Toggle(1002, 2, 9, false)));
            check(!foreign.CanApply && foreign.Writes.Count == 0 && !foreign.ActiveSelf.ContainsKey(9), "Other avatar must never supply a target.");
            var missing = Evaluate(Scene(Standard(), Toggle(1002, 2, 999, false)));
            check(!missing.CanApply && missing.Diagnostics.Any(item => item.Code == "MA_INITIAL_TARGET"), "Missing explicit target cannot fall back to Body.");
            var pathOnly = Toggle(1002, 2, 3, false) with { Fields = "  m_objects:\n" + ToggleEntry(0, false) };
            var ambiguous = Evaluate(Scene(Standard().Concat(new[] { new Node(4, 1, "Body") }).ToArray(), pathOnly));
            check(!ambiguous.CanApply, "Duplicate path must not select first sibling.");
            var exact = Evaluate(Scene(Standard(), pathOnly));
            check(exact.CanApply && !exact.ActiveSelf[3], "Unique avatar-relative path should resolve.");
        });
        test("MA initial Shape Set uses exact GUID, renderer metadata and invariant weight", () =>
        {
            var oldCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                string yaml = Scene(Standard(), Shape(1002, 2), Renderer());
                string original = yaml;
                var result = Evaluate(yaml);
                check(result.CanApply && result.Renderers[2003].Shapes["Smile"] == 37.5f && yaml == original, "Invariant Set or read-only YAML contract failed.");
                var wrongGuid = Shape(1002, 2) with { Guid = new string('a', 32) };
                var ignored = Evaluate(Scene(Standard(), wrongGuid, Renderer()));
                check(ignored.Writes.Count == 0, "Filename or field resemblance must not recognize a component.");
            }
            finally { CultureInfo.CurrentCulture = oldCulture; }
        });
        test("MA initial Shape Delete is explicit unsupported and never Set", () =>
        {
            var delete = Shape(1002, 2) with { Fields = Shape(1002, 2).Fields.Replace("ChangeType: 1", "ChangeType: 0") };
            var result = Evaluate(Scene(Standard(), delete, Renderer()));
            check(!result.CanApply && result.Writes.Count == 0 && result.Renderers[2003].Shapes["Smile"] == 12 &&
                result.Diagnostics.Any(item => item.Code == "MA_INITIAL_SHAPE_DELETE" && item.Severity == InitialAppearanceSeverity.Warning), "Delete must not become Set or destructive mesh mutation.");
        });
        test("MA initial material defaults preserve GUID/fileID and authored fallback", () =>
        {
            var result = Evaluate(Scene(Standard(), Material(1002, 2), Renderer()));
            check(result.CanApply && result.Renderers[2003].Materials[0].FileId == 2100000 && result.Renderers[2003].Materials[0].Guid == new string('b', 32), "Material identity or selected default lost.");
            var inactive = Evaluate(Scene(Standard(false), Material(1002, 2), Renderer()));
            check(inactive.CanApply && inactive.Renderers[2003].Materials[0].Guid == new string('a', 32), "Inactive material rule must retain original material.");
            var invalid = Material(1002, 2) with { Fields = Material(1002, 2).Fields.Replace("MaterialIndex: 0", "MaterialIndex: 3") };
            check(!Evaluate(Scene(Standard(), invalid, Renderer())).CanApply, "Out-of-range material slot must be diagnosed.");
        });
        test("MA initial nearest anonymous menu controls default, not ancestor menu", () =>
        {
            var nodes = new[] { new Node(1, 0, "Avatar"), new Node(4, 1, "Outer"), new Node(2, 4, "Inner"), new Node(3, 1, "Body") };
            var result = Evaluate(Scene(nodes, Menu(1004, 4, false), Menu(1005, 2, true), Toggle(1002, 2, 3, false)));
            check(result.CanApply && !result.ActiveSelf[3], "Only nearest menu should contribute its default.");
            var named = Menu(1005, 2, true) with { Fields = Menu(1005, 2, true).Fields.Replace("name: ''", "name: Outfit") };
            var unknown = Evaluate(Scene(nodes, named, Toggle(1002, 2, 3, false)));
            check(!unknown.CanApply && unknown.HasUnsupportedContext && unknown.Writes.Count == 0, "Named parameters require external context, not guessed defaults.");
        });
        test("MA initial unknown animation context blocks all writes", () =>
        {
            string yaml = Scene(Standard(), Toggle(1002, 2, 3, false));
            var uncertified = ModularAvatarInitialAppearance.Evaluate(yaml, new() { AvatarRootId = 1 });
            check(!uncertified.CanApply && uncertified.Writes.Count == 0, "External context must be explicitly certified.");
            var animator = new Behaviour(1005, 1, "", "  m_Controller: {fileID: 9100000, guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb}\n", 95);
            var animated = Evaluate(Scene(Standard(), animator, Toggle(1002, 2, 3, false)));
            check(!animated.CanApply && animated.Diagnostics.Any(item => item.Code == "MA_INITIAL_ANIMATION_CONTEXT"), "Serialized animator must prevent false parity claims.");
        });
        test("MA initial missing renderer or shape fails preflight", () =>
        {
            check(!Evaluate(Scene(Standard(), Shape(1002, 2))).CanApply, "Missing renderer cannot pass silently.");
            var missing = Shape(1002, 2) with { Fields = Shape(1002, 2).Fields.Replace("Smile", "Missing") };
            check(!Evaluate(Scene(Standard(), missing, Renderer())).CanApply, "Missing shape cannot pass silently.");
        });
        test("MA initial default descriptor layer declarations are not custom controllers", () =>
        {
            var descriptor = new Behaviour(1005, 1, new string('c', 32), "  baseAnimationLayers:\n  - isDefault: 1\n    animatorController: {fileID: 0}\n  specialAnimationLayers: []\n  expressionParameters: {fileID: 0}\n");
            var result = Evaluate(Scene(Standard(), descriptor, Toggle(1002, 2, 3, false)));
            check(result.CanApply && !result.ActiveSelf[3], "Merely declaring default layers must not invent an unsupported custom controller.");
            var custom = descriptor with { Fields = descriptor.Fields.Replace("animatorController: {fileID: 0}", "animatorController: {fileID: 9100000}") };
            check(!Evaluate(Scene(Standard(), custom, Toggle(1002, 2, 3, false))).CanApply, "Custom controller references must still block static evaluation.");
        });
        test("MA initial dependent toggles settle before renderer defaults", () =>
        {
            var nodes = new[] { new Node(1, 0, "Avatar"), new Node(2, 1, "Controller"), new Node(4, 1, "Dependent"), new Node(3, 1, "Body") };
            var result = Evaluate(Scene(nodes, Toggle(1002, 2, 4, false), Toggle(1004, 4, 3, false, true), Shape(1005, 4), Renderer()));
            check(result.CanApply && !result.ActiveSelf[4] && !result.ActiveSelf[3] && result.Renderers[2003].Shapes["Smile"] == 12,
                "Dependent toggles or subsequent shape conditions used stale initial activation.");
        });
        test("MA initial controller disabling after earlier activation restores authored fallback", () =>
        {
            var nodes = new[] { new Node(1, 0, "Avatar"), new Node(2, 1, "Controller"), new Node(4, 1, "Disabler"), new Node(3, 1, "Body", false) };
            var result = Evaluate(Scene(nodes, Toggle(1002, 2, 3, true), Toggle(1004, 4, 2, false)));
            check(result.CanApply && !result.ActiveSelf[2] && !result.ActiveSelf[3],
                "An earlier target activation must not latch after its controller becomes inactive in a later iteration.");
            check(result.Writes.Where(item => item.Effect == InitialAppearanceEffect.Active).All(item => item.Active == result.ActiveSelf[item.TargetId]) &&
                !result.Writes.Any(item => item.TargetId == 3 && item.Effect == InitialAppearanceEffect.Active),
                "Final writes must contain stable-state deltas only, never an intermediate enabled rule's stale value.");
        });
        test("MA initial oscillating toggle dependencies are unsupported instead of arbitrary", () =>
        {
            var nodes = new[] { new Node(1, 0, "Avatar"), new Node(2, 1, "First"), new Node(4, 1, "Second"), new Node(3, 1, "Body") };
            var result = Evaluate(Scene(nodes, Toggle(1002, 2, 4, false), Toggle(1003, 2, 4, true, true),
                Toggle(1004, 4, 2, false), Toggle(1005, 4, 2, true, true)));
            check(!result.CanApply && result.Writes.Count == 0 && result.Diagnostics.Any(item => item.Code == "MA_INITIAL_UNSTABLE"),
                "Oscillating controls must not publish an arbitrary iteration's appearance.");
        });
        test("MA initial invalid reference data cannot silently become valid fallback", () =>
        {
            var malformed = Toggle(1002, 2, 3, false) with { Fields = Toggle(1002, 2, 3, false).Fields.Replace("fileID: 3", "fileID: invalid") };
            check(!Evaluate(Scene(Standard(), malformed)).CanApply, "Malformed identity must not resolve via Body path.");
            var empty = Toggle(1002, 2, 3, false) with { Fields = Toggle(1002, 2, 3, false).Fields.Replace("referencePath: Body", "referencePath: ''") };
            check(!Evaluate(Scene(Standard(), empty)).CanApply, "Upstream empty referencePath is null even with targetObject.");
            var legacy = Shape(1002, 2) with { Fields = Shape(1002, 2).Fields.Replace("m_shapes:", "Shapes:") };
            check(Evaluate(Scene(Standard(), legacy, Renderer())).CanApply, "FormerlySerializedAs Shapes alias must remain supported.");
        });
        test("MA initial null material is an explicit clear, not a fabricated resource", () =>
        {
            var clear = Material(1002, 2) with { Fields = Material(1002, 2).Fields.Replace("{fileID: 2100000, guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb, type: 2}", "{fileID: 0}") };
            var result = Evaluate(Scene(Standard(), clear, Renderer()));
            check(result.CanApply && result.Renderers[2003].Materials[0] == new InitialAppearanceMaterial(0, ""), "Explicit null material should remain an explicit clear.");
        });
        test("MA initial cancellation and duplicate identities stop evaluation", () =>
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            bool cancelled = false;
            try { ModularAvatarInitialAppearance.Evaluate(Scene(Standard()), Context(), cancellation.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            check(cancelled, "Cancellation must propagate before parsing or writing.");
            bool rejected = false;
            try { Evaluate(Scene(Standard()) + "--- !u!1 &1\nGameObject:\n  m_Name: Duplicate\n"); }
            catch (System.IO.InvalidDataException) { rejected = true; }
            check(rejected, "Duplicate identity must not silently overwrite source object.");
        });
    }

    private sealed record Node(long Id, long Parent, string Name, bool Active = true);
    private sealed record Behaviour(long Id, long Owner, string Guid, string Fields, int ClassId = 114, string Extra = "");
    private static Node[] Standard(bool enabled = true) => new[] { new Node(1, 0, "Avatar"), new Node(2, 1, "Controller", enabled), new Node(3, 1, "Body") };
    private static InitialAppearanceContext Context() => new()
    {
        AvatarRootId = 1, ExternalAnimationAndParameterContextVerifiedAbsent = true,
        Renderers = new Dictionary<long, InitialAppearanceRenderer>
        {
            [2003] = new(3, new Dictionary<string, float> { ["Smile"] = 12 }, new[] { new InitialAppearanceMaterial(2100000, new string('a', 32)) })
        }
    };
    private static InitialAppearanceResult Evaluate(string yaml) => ModularAvatarInitialAppearance.Evaluate(yaml, Context());
    private static Behaviour Toggle(long id, long owner, long target, bool active, bool inverted = false) =>
        new(id, owner, ModularAvatarInitialAppearance.ObjectToggleGuid, "  m_inverted: " + (inverted ? "1" : "0") + "\n  m_objects:\n" + ToggleEntry(target, active));
    private static string ToggleEntry(long target, bool active) => "  - Object:\n      referencePath: Body\n      targetObject: {fileID: " + target + "}\n    Active: " + (active ? "1" : "0") + "\n";
    private static Behaviour Shape(long id, long owner) => new(id, owner, ModularAvatarInitialAppearance.ShapeChangerGuid,
        "  m_shapes:\n  - Object:\n      referencePath: Body\n      targetObject: {fileID: 3}\n    ShapeName: Smile\n    ChangeType: 1\n    Value: 37.5\n");
    private static Behaviour Material(long id, long owner) => new(id, owner, ModularAvatarInitialAppearance.MaterialSetterGuid,
        "  m_objects:\n  - Object:\n      referencePath: Body\n      targetObject: {fileID: 3}\n    Material: {fileID: 2100000, guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb, type: 2}\n    MaterialIndex: 0\n");
    private static Behaviour Renderer() => new(2003, 3, "", "", 137);
    private static Behaviour Menu(long id, long owner, bool selected) => new(id, owner, ModularAvatarInitialAppearance.MenuItemGuid,
        "  Control:\n    type: 2\n    parameter: {name: ''}\n    value: 1\n  automaticValue: 1\n  isDefault: " + (selected ? "1" : "0") + "\n");
    private static string Scene(Node[] nodes, params Behaviour[] components)
    {
        var yaml = new StringBuilder("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
        foreach (var node in nodes)
        {
            yaml.Append($"--- !u!1 &{node.Id}\nGameObject:\n  m_Name: {node.Name}\n  m_IsActive: {(node.Active ? 1 : 0)}\n  m_Component:\n  - component: {{fileID: {100 + node.Id}}}\n");
            foreach (var component in components.Where(item => item.Owner == node.Id)) yaml.Append($"  - component: {{fileID: {component.Id}}}\n");
            yaml.Append($"--- !u!4 &{100 + node.Id}\nTransform:\n  m_GameObject: {{fileID: {node.Id}}}\n  m_Father: {{fileID: {(node.Parent == 0 ? 0 : 100 + node.Parent)}}}\n");
            var children = nodes.Where(item => item.Parent == node.Id).ToArray();
            yaml.Append(children.Length == 0 ? "  m_Children: []\n" : "  m_Children:\n");
            foreach (var child in children) yaml.Append($"  - {{fileID: {100 + child.Id}}}\n");
        }
        foreach (var component in components)
        {
            yaml.Append($"--- !u!{component.ClassId} &{component.Id}\nComponent:\n  m_GameObject: {{fileID: {component.Owner}}}\n");
            if (component.Guid.Length != 0) yaml.Append($"  m_Script: {{fileID: 11500000, guid: {component.Guid}, type: 3}}\n");
            yaml.Append(component.Extra).Append(component.Fields);
        }
        return yaml.ToString();
    }
}
