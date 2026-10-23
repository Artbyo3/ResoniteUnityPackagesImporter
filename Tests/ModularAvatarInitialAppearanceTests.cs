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
        test("MA enabled same-value activation claims ledger but inactive fallback does not", () =>
        {
            var enabled = Evaluate(Scene(Standard(), Toggle(1002, 2, 3, true)));
            check(enabled.CanApply && enabled.ActiveSelf[3] && enabled.Writes.Count == 1 && enabled.Writes[0].Active,
                "An effective enabled Active=true assignment must survive even when the authored target is already active.");
            var ledger = new Dictionary<(int Effect, long Target, string Property), string>();
            foreach (var write in enabled.Writes)
            {
                var identity = ModularAvatarInitialAppearance.InitialWriteIdentity(write);
                ledger[identity.Key] = identity.Value;
            }
            var laterFalse = ModularAvatarInitialAppearance.InitialWriteIdentity(new(InitialAppearanceEffect.Active, 3, "m_IsActive", Active: false));
            check(ModularAvatarInitialAppearance.InitialWriteConflicts(ledger, laterFalse),
                "A later VRCF Active=false snapshot must conflict with the enabled same-value MA assignment.");
            var same = ModularAvatarInitialAppearance.InitialWriteIdentity(enabled.Writes[0]);
            check(!ModularAvatarInitialAppearance.InitialWriteConflicts(ledger, same), "Repeated identical enabled activation remains allowed.");
            var inactive = Evaluate(Scene(Standard(false), Toggle(1002, 2, 3, true)));
            check(inactive.CanApply && inactive.ActiveSelf[3] && inactive.Writes.Count == 0,
                "Inactive rules must not claim authored fallback values in the ledger.");
            var inactiveLedger = inactive.Writes.Select(ModularAvatarInitialAppearance.InitialWriteIdentity).ToDictionary(item => item.Key, item => item.Value);
            check(!ModularAvatarInitialAppearance.InitialWriteConflicts(inactiveLedger, laterFalse),
                "An inactive MA rule must not block a later supported VRCF assignment.");
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
        test("MA initial Shape Delete is warning-skipped and never Set", () =>
        {
            var delete = Shape(1002, 2) with { Fields = Shape(1002, 2).Fields.Replace("ChangeType: 1", "ChangeType: 0") };
            var result = Evaluate(Scene(Standard(), delete, Renderer()));
            check(result.CanApply && result.Writes.Count == 0 && result.Renderers[2003].Shapes["Smile"] == 12 &&
                result.Diagnostics.Any(item => item.Code == "MA_INITIAL_DESTRUCTIVE_SKIPPED" && item.Severity == InitialAppearanceSeverity.Warning), "Delete must not become Set or destructive mesh mutation.");
        });
        test("MA initial mixed Delete Set Toggle preserves reversible entries", () =>
        {
            var shape = Shape(1002, 2);
            var mixed = shape with { Fields = shape.Fields + shape.Fields.Replace("  m_shapes:\n", "").Replace("ChangeType: 1", "ChangeType: 0") };
            var result = Evaluate(Scene(Standard(), mixed, Toggle(1003, 2, 3, false), Renderer()));
            check(result.CanApply && !result.ActiveSelf[3] && result.Renderers[2003].Shapes["Smile"] == 37.5f &&
                result.Writes.Count == 2 && result.Diagnostics.Count(item => item.Code == "MA_INITIAL_DESTRUCTIVE_SKIPPED") == 1,
                "Delete must not poison supported Set/Toggle entries in the same context.");
            var unknown = shape with { Fields = shape.Fields.Replace("ChangeType: 1", "ChangeType: 17") };
            var rejected = Evaluate(Scene(Standard(), unknown, Toggle(1003, 2, 3, false), Renderer()));
            check(!rejected.CanApply && rejected.Writes.Count == 0 && rejected.Diagnostics.Any(item => item.Code == "MA_INITIAL_SHAPE_CHANGE_TYPE") &&
                !rejected.Diagnostics.Any(item => item.Code == "MA_INITIAL_DESTRUCTIVE_SKIPPED"), "Unknown ChangeType is unsupported, not destructive.");
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
        test("MA initial MaterialSetter accepts exact static MeshRenderer snapshot", () =>
        {
            var result = Evaluate(Scene(Standard(), Material(1002, 2), Renderer() with { ClassId = 23 }));
            check(result.CanApply && result.Writes.Count == 1 && result.Writes[0].Effect == InitialAppearanceEffect.Material &&
                result.Writes[0].TargetId == 2003 && result.Renderers[2003].Materials[0].Guid == new string('b', 32),
                "Static MeshRenderer material assignment must retain exact component and material identities.");
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
        test("MA initial MergeAnimator controller cannot be mistaken for absent external context", () =>
        {
            var merge = new Behaviour(1005, 2, ModularAvatarInitialAppearance.MergeAnimatorGuid,
                "  animator: {fileID: 9100000, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 2}\n");
            var result = Evaluate(Scene(Standard(), Toggle(1002, 2, 3, false), merge));
            check(!result.CanApply && result.Writes.Count == 0 && result.Diagnostics.Any(item => item.Code == "MA_INITIAL_ANIMATION_CONTEXT"),
                "A controller supplied through MA must prevent an unverified reactive snapshot just like a descriptor controller.");
            var empty = merge with { Fields = "  animator: {fileID: 0}\n" };
            check(Evaluate(Scene(Standard(), Toggle(1002, 2, 3, false), empty)).CanApply,
                "A null MergeAnimator controller must not invent an external animation dependency.");
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
                "Final writes must contain effective stable-state assignments only, never an intermediate enabled rule's stale value.");
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
        test("MA initial material asset preflight requires exact class fileID and single document", () =>
        {
            string yaml = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!21 &2100000\nMaterial:\n  m_Name: Exact\n";
            check(ModularAvatarInitialAppearance.IsExactMaterialAsset(yaml, 2100000), "Exact external .mat identity must be accepted.");
            check(!ModularAvatarInitialAppearance.IsExactMaterialAsset(yaml, 2100001), "A different material subasset must never select the first document.");
            check(!ModularAvatarInitialAppearance.IsExactMaterialAsset(yaml.Replace("!u!21", "!u!23"), 2100000), "Non-Material class identity must be rejected.");
            check(!ModularAvatarInitialAppearance.IsExactMaterialAsset(yaml.Replace("Material:", "Shader:"), 2100000), "Material class with a different body must be rejected.");
            check(!ModularAvatarInitialAppearance.IsExactMaterialAsset(yaml + "--- !u!21 &2100001\nMaterial:\n  m_Name: Other\n", 2100000), "Multi-subasset material files must be explicitly unsupported.");
        });
        test("MA initial shared ledger identities are canonical across frameworks", () =>
        {
            var active = ModularAvatarInitialAppearance.InitialWriteIdentity(new(InitialAppearanceEffect.Active, 3, "ignored", Active: true));
            check(active.Key == (0, 3L, "m_IsActive") && active.Value == "1", "Activation identity must not depend on producer property spelling.");
            var first = ModularAvatarInitialAppearance.InitialWriteIdentity(new(InitialAppearanceEffect.Material, 2003, "00", Material: new(2100000, new string('A', 32))));
            var second = ModularAvatarInitialAppearance.InitialWriteIdentity(new(InitialAppearanceEffect.Material, 2003, "0", Material: new(2100000, new string('a', 32))));
            check(first == second, "Material identity must canonicalize slot spelling and GUID case, retaining exact fileID.");
            var other = ModularAvatarInitialAppearance.InitialWriteIdentity(new(InitialAppearanceEffect.Material, 2003, "0", Material: new(2100001, new string('a', 32))));
            check(first.Key == other.Key && first.Value != other.Value, "Different material subassets must conflict on the same destination.");
            var ledger = new Dictionary<(int Effect, long Target, string Property), string>();
            check(!ModularAvatarInitialAppearance.InitialWriteConflicts(ledger, first), "A fresh per-plan ledger must not conflict.");
            ledger[first.Key] = first.Value;
            check(!ModularAvatarInitialAppearance.InitialWriteConflicts(ledger, second) && ModularAvatarInitialAppearance.InitialWriteConflicts(ledger, other),
                "Same-value repeated snapshots are allowed, but a differing material value must reject the later snapshot.");
            check(!ModularAvatarInitialAppearance.InitialWriteConflicts(new Dictionary<(int Effect, long Target, string Property), string>(), other),
                "Ledger state must not leak across plans.");
            var oldCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                var shape = ModularAvatarInitialAppearance.InitialWriteIdentity(new(InitialAppearanceEffect.Shape, 2003, "Smile", Weight: 37.5f));
                check(shape.Key == (1, 2003L, "Smile") && shape.Value == "37.5", "Shape identity must be invariant and retain the exact name.");
                ledger[shape.Key] = shape.Value;
                var changed = ModularAvatarInitialAppearance.InitialWriteIdentity(new(InitialAppearanceEffect.Shape, 2003, "Smile", Weight: 12));
                check(ModularAvatarInitialAppearance.InitialWriteConflicts(ledger, changed), "Differing shape values must conflict, independent of process culture.");
            }
            finally { CultureInfo.CurrentCulture = oldCulture; }
        });
        test("MA inherited native materials stage several slots without serialized entries", () =>
        {
            var original = new List<InitialAppearanceMaterial>();
            var current = original;
            var staged = new InitialAppearanceMaterialMetadata<InitialAppearanceMaterial>(original, 3);
            var first = new InitialAppearanceMaterial(2100000, new string('a', 32));
            var last = new InitialAppearanceMaterial(2100001, new string('b', 32));
            staged.Replacement[0] = first;
            staged.Replacement[2] = last;
            check(ReferenceEquals(current, original) && original.Count == 0 && staged.IsUnchanged(current),
                "Preflight must not extend or replace the original inherited renderer metadata list.");
            var providers = new[] { "inherited-first", "inherited-middle", "inherited-last" };
            var writes = new List<(Action Apply, Action Restore, Func<bool> Valid)>
            {
                (() => providers[0] = "first", () => providers[0] = "inherited-first", () => staged.IsUnchanged(current)),
                (() => providers[2] = "last", () => providers[2] = "inherited-last", () => staged.IsUnchanged(current)),
                (() => current = staged.Replacement, () => current = staged.Original!, () => staged.IsUnchanged(current))
            };
            InitialAppearanceTransaction.Apply(writes, default);
            check(ReferenceEquals(current, staged.Replacement) && current.Count == 3 && ReferenceEquals(current[0], first) &&
                current[1] == null && ReferenceEquals(current[2], last) && original.Count == 0 &&
                providers.SequenceEqual(new[] { "first", "inherited-middle", "last" }),
                "Multiple inherited slots must commit together without stale closures or guessed identities for untouched native slots.");
        });
        test("MA inherited material metadata rollback restores exact reference length and entries", () =>
        {
            var existing = new InitialAppearanceMaterial(2100000, new string('a', 32));
            var original = new List<InitialAppearanceMaterial> { existing };
            var current = original;
            var staged = new InitialAppearanceMaterialMetadata<InitialAppearanceMaterial>(original, 3);
            staged.Replacement[0] = new(2100001, new string('b', 32));
            staged.Replacement[2] = new(2100002, new string('c', 32));
            var providers = new[] { "old-first", "old-middle", "old-last" };
            var wrappers = new[] { "old-first-wrapper", "old-middle-wrapper", "old-last-wrapper" };
            var writes = new List<(Action Apply, Action Restore, Func<bool> Valid)>
            {
                (() => { providers[0] = "new-first"; wrappers[0] = "new-first-wrapper"; },
                    () => { providers[0] = "old-first"; wrappers[0] = "old-first-wrapper"; }, () => staged.IsUnchanged(current)),
                (() => { providers[2] = "new-last"; wrappers[2] = "new-last-wrapper"; },
                    () => { providers[2] = "old-last"; wrappers[2] = "old-last-wrapper"; }, () => staged.IsUnchanged(current)),
                (() => current = staged.Replacement, () => current = staged.Original!, () => staged.IsUnchanged(current)),
                (() => throw new InvalidOperationException("later renderer failed"), () => { }, () => true)
            };
            bool rejected = false;
            try { InitialAppearanceTransaction.Apply(writes, default); }
            catch (InvalidOperationException) { rejected = true; }
            check(rejected && ReferenceEquals(current, original) && original.Count == 1 && ReferenceEquals(original[0], existing) &&
                staged.IsUnchanged(current) && providers.SequenceEqual(new[] { "old-first", "old-middle", "old-last" }) &&
                wrappers.SequenceEqual(new[] { "old-first-wrapper", "old-middle-wrapper", "old-last-wrapper" }),
                "Failure after metadata commit must restore the exact prior list reference/length/entries and every attempted native/provider-wrapper assignment.");
        });
        test("MA inherited metadata validates stale lists and restores originally null reference", () =>
        {
            var original = new List<string> { "inherited" };
            var staged = new InitialAppearanceMaterialMetadata<string>(original, 2);
            check(!staged.IsUnchanged(new List<string>(original)), "A replacement source list must invalidate material preflight.");
            original.Add("later");
            check(!staged.IsUnchanged(original), "Changed source list length must invalidate preflight.");
            original.RemoveAt(1);
            original[0] = "changed";
            check(!staged.IsUnchanged(original), "Changed source metadata entries must invalidate preflight.");
            List<string>? current = null;
            var missing = new InitialAppearanceMaterialMetadata<string>(current, 2);
            missing.Replacement[1] = "exact-material";
            var writes = new List<(Action Apply, Action Restore, Func<bool> Valid)>
            {
                (() => current = missing.Replacement, () => current = missing.Original, () => missing.IsUnchanged(current)),
                (() => throw new InvalidOperationException("later failure"), () => { }, () => true)
            };
            bool rejected = false;
            try { InitialAppearanceTransaction.Apply(writes, default); }
            catch (InvalidOperationException) { rejected = true; }
            check(rejected && current == null && missing.IsUnchanged(current), "Originally null serialized metadata must roll back to null, not an invented empty or padded list.");
        });
        test("MA initial transaction restores attempted partial writes in reverse order", () =>
        {
            int active = 1;
            string provider = "previous";
            string metadata = "previous-source";
            var restored = new List<int>();
            var writes = new List<(Action Apply, Action Restore, Func<bool> Valid)>
            {
                (() => active = 0, () => { active = 1; restored.Add(1); }, () => true),
                (() => { provider = "replacement"; metadata = "replacement-source"; throw new InvalidOperationException("partial assignment"); },
                    () => { provider = "previous"; metadata = "previous-source"; restored.Add(2); }, () => true)
            };
            bool rejected = false;
            try { InitialAppearanceTransaction.Apply(writes, default); }
            catch (InvalidOperationException) { rejected = true; }
            check(rejected && active == 1 && provider == "previous" && metadata == "previous-source" && restored.SequenceEqual(new[] { 2, 1 }),
                "The failing attempted write and earlier native/provider metadata writes must roll back in reverse order.");
        });
        test("MA initial transaction aggregates rollback failures and continues rollback", () =>
        {
            bool restored = false;
            var writes = new List<(Action Apply, Action Restore, Func<bool> Valid)>
            {
                (() => { }, () => restored = true, () => true),
                (() => throw new InvalidOperationException("apply"), () => throw new InvalidOperationException("rollback"), () => true)
            };
            AggregateException? failure = null;
            try { InitialAppearanceTransaction.Apply(writes, default); }
            catch (AggregateException exception) { failure = exception; }
            check(restored && failure?.InnerExceptions.Count == 2 && failure.InnerExceptions[0].Message == "apply" && failure.InnerExceptions[1].Message == "rollback",
                "A failed rollback must not hide the original error or stop remaining restores.");
        });
        test("MA initial transaction cancels and rejects driven changes without retaining writes", () =>
        {
            using var cancellation = new CancellationTokenSource();
            int value = 0;
            var writes = new List<(Action Apply, Action Restore, Func<bool> Valid)>
            {
                (() => { value = 1; cancellation.Cancel(); }, () => value = 0, () => true),
                (() => value = 2, () => value = 1, () => true)
            };
            bool cancelled = false;
            try { InitialAppearanceTransaction.Apply(writes, cancellation.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            check(cancelled && value == 0, "Cancellation after an applied write must restore prior state.");
            writes[0] = (() => value = 1, () => value = 0, () => true);
            writes[1] = (() => value = 2, () => value = 1, () => false);
            bool rejected = false;
            try { InitialAppearanceTransaction.Apply(writes, default); }
            catch (InvalidOperationException) { rejected = true; }
            check(rejected && value == 0, "A driven/missing destination detected during application must restore prior writes.");
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
