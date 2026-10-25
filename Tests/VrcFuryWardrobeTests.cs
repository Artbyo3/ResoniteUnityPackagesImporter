using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using UnityPackageImporter.Models;

internal static class VrcFuryWardrobeTests
{
    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        string Describe(AvatarWardrobePlan plan) => string.Join("; ", plan.Diagnostics.Select(item => item.Code + ": " + item.Message));
        const string clipGuid = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string baselineMatGuid = "11111111111111111111111111111111";
        const string altMatGuid = "22222222222222222222222222222222";
        string Clip(string path = "Body", int classId = 1, string property = "m_IsActive", string value = "0", long id = 7400000) =>
            "--- !u!74 &" + id + "\nAnimationClip:\n  m_FloatCurves:\n  - curve:\n      m_Curve: [{time: 0, value: " + value + "}]\n    path: '" + path + "'\n    classID: " + classId + "\n    attribute: '" + property + "'\n";
        string ClipAction(long id = 7400000) => "clip: {version: 1, id: '" + clipGuid + ":" + id + "'}";
        string ClipScene(string components) => Scene(components).Replace("--- !u!1 &10\nGameObject:\n  m_Name: Clothing",
            "--- !u!1 &10\nGameObject:\n  m_Name: Body", StringComparison.Ordinal);
        AvatarWardrobePlan CompileClip(string clip, string flags = "") => VrcFuryInitialAppearance.CompileWardrobe(
            ClipScene(Toggle(0, "Wardrobe/Clip", ClipAction(), flags, "AnimationClipAction")), Context(),
            (guid, identity) => guid == clipGuid ? UnityStaticAnimationClip.Parse(clip, identity) : null);
        test("Wardrobe constant clip visibility restores authored off state", () =>
        {
            var plan = CompileClip(Clip(), "defaultOn: 1");
            var binding = plan.Controls.Single().Bindings.Single();
            check(plan.CanBuild && plan.Controls.Single().DefaultOn && binding.On.TargetId == 10 &&
                !binding.On.Active && binding.Off.Active, Describe(plan));
        });
        test("Wardrobe constant clip shape restores source baseline", () =>
        {
            var plan = CompileClip(Clip(classId: 137, property: "blendShape.Fit", value: "82.5"));
            var binding = plan.Controls.Single().Bindings.Single();
            check(plan.CanBuild && binding.On.TargetId == 300 && binding.On.Weight == 82.5f && binding.Off.Weight == 20, Describe(plan));
        });
        test("Wardrobe thirty direct and clip controls share exact native plan", () =>
        {
            string components = string.Concat(Enumerable.Range(0, 30).Select(index => Toggle(index, "Outfits/" + index,
                index % 2 == 0 ? ObjectAction(10 + index) : ClipAction(7400000 + index),
                actionType: index % 2 == 0 ? "ObjectToggleAction" : "AnimationClipAction")));
            string yaml = Scene(components);
            for (int index = 0; index < 30; index++)
                yaml = yaml.Replace("--- !u!1 &" + (10 + index) + "\nGameObject:\n  m_Name: Clothing",
                    "--- !u!1 &" + (10 + index) + "\nGameObject:\n  m_Name: Outfit" + index, StringComparison.Ordinal);
            string clips = string.Concat(Enumerable.Range(0, 30).Select(index => Clip("Outfit" + index, value: "0", id: 7400000 + index)));
            var plan = VrcFuryInitialAppearance.CompileWardrobe(yaml, Context(), (_, identity) => UnityStaticAnimationClip.Parse(clips, identity));
            check(plan.CanBuild && plan.Controls.Count == 30 && plan.Controls.Select(control => control.Bindings.Single().On.TargetId).Distinct().Count() == 30, Describe(plan));
        });
        test("Wardrobe clip GUID and fileID are passed to exact resolver", () =>
        {
            bool exact = false;
            var plan = VrcFuryInitialAppearance.CompileWardrobe(ClipScene(Toggle(0, "A", ClipAction(), actionType: "AnimationClipAction")), Context(),
                (guid, id) => { exact = guid == clipGuid && id == 7400000; return UnityStaticAnimationClip.Parse(Clip(), id); });
            check(plan.CanBuild && exact, Describe(plan));
        });
        test("Wardrobe unresolved clip cannot produce partial controls", () =>
        {
            var plan = VrcFuryInitialAppearance.CompileWardrobe(ClipScene(Toggle(0, "A", ClipAction(), actionType: "AnimationClipAction")), Context(), (_, _) => null);
            check(!plan.CanBuild && plan.Diagnostics.Any(item => item.Code.EndsWith("CLIP_MISSING", StringComparison.Ordinal)), Describe(plan));
        });
        test("Wardrobe missing clip resolver is reported", () =>
            Reject(Toggle(0, "A", ClipAction(), actionType: "AnimationClipAction"), "CLIP_RESOLVER"));
        foreach (string path in new[] { "Foreign/Body", "../Body", "/Body", "Body//Child", "Missing", "" })
            test("Wardrobe clip rejects unsafe foreign missing or host path " + path, () =>
            {
                var plan = CompileClip(Clip(path));
                check(!plan.CanBuild, Describe(plan));
            });
        test("Wardrobe duplicate named clip paths remain ambiguous", () =>
        {
            var plan = VrcFuryInitialAppearance.CompileWardrobe(Scene(Toggle(0, "A", ClipAction(), actionType: "AnimationClipAction")), Context(),
                (_, id) => UnityStaticAnimationClip.Parse(Clip("Clothing"), id));
            check(!plan.CanBuild && plan.Diagnostics.Any(item => item.Code.EndsWith("CLIP_PATH", StringComparison.Ordinal)), Describe(plan));
        });
        test("Wardrobe variable animation curves are not sampled as constant controls", () =>
        {
            var plan = CompileClip(Clip().Replace("[{time: 0, value: 0}]", "[{time: 0, value: 0}, {time: 1, value: 1}]", StringComparison.Ordinal));
            check(!plan.CanBuild, Describe(plan));
        });
        test("Wardrobe animation events are rejected rather than executed", () =>
        {
            var plan = CompileClip(Clip() + "  m_Events: [{time: 0, functionName: ExecutePackageCode}]\n");
            check(!plan.CanBuild, Describe(plan));
        });
        test("Wardrobe unsupported transform curves are rejected", () =>
        {
            var plan = CompileClip(Clip(classId: 4, property: "m_LocalPosition.x", value: "1"));
            check(!plan.CanBuild, Describe(plan));
        });
        test("Wardrobe clip shape requires exact renderer and shape", () =>
        {
            var plan = CompileClip(Clip(classId: 137, property: "blendShape.Missing", value: "75"));
            check(!plan.CanBuild && plan.Diagnostics.Any(item => item.Code.EndsWith("CLIP_SHAPE", StringComparison.Ordinal)), Describe(plan));
        });
        test("Wardrobe clip and direct action cannot compete for one field", () =>
        {
            string components = Toggle(0, "Clip", ClipAction(), actionType: "AnimationClipAction") + Toggle(1, "Direct", ObjectAction(10));
            var plan = VrcFuryInitialAppearance.CompileWardrobe(ClipScene(components), Context(), (_, id) => UnityStaticAnimationClip.Parse(Clip(), id));
            check(!plan.CanBuild && plan.Diagnostics.Any(item => item.Code.EndsWith("CONFLICT", StringComparison.Ordinal)), Describe(plan));
        });
        test("Wardrobe cancellation during clip resolution propagates", () =>
        {
            using var cancellation = new CancellationTokenSource();
            bool canceled = false;
            try
            {
                VrcFuryInitialAppearance.CompileWardrobe(ClipScene(Toggle(0, "A", ClipAction(), actionType: "AnimationClipAction")), Context(),
                    (_, identity) => { cancellation.Cancel(); return UnityStaticAnimationClip.Parse(Clip(), identity); }, cancellation.Token);
            }
            catch (OperationCanceledException) { canceled = true; }
            check(canceled, "Clip resolution cancellation was swallowed.");
        });
        test("Exact clip asset resolution rejects absent wrong-extension and wrong-fileID assets", () =>
        {
            string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wardrobe-clip-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            try
            {
                string path = System.IO.Path.Combine(directory, "wardrobe.anim");
                System.IO.File.WriteAllText(path, Clip());
                var assets = new Dictionary<string, string> { [clipGuid] = path, ["wrong-extension"] = path + ".mat" };
                check(UnityStaticAnimationClip.Resolve(assets, clipGuid, 7400000)?.CanApply == true, "Exact clip asset did not resolve.");
                check(UnityStaticAnimationClip.Resolve(assets, "missing", 7400000) == null && UnityStaticAnimationClip.Resolve(assets, "wrong-extension", 7400000) == null,
                    "Absent/wrong extension asset was borrowed.");
                check(UnityStaticAnimationClip.Resolve(assets, clipGuid, 7400001)?.CanApply == false, "Wrong clip fileID was silently substituted.");
            }
            finally { System.IO.Directory.Delete(directory, true); }
        });

        AvatarWardrobePlan Compile(string components, InitialAppearanceContext? context = null) =>
            VrcFuryInitialAppearance.CompileWardrobe(Scene(components), context ?? Context());
        void Reject(string components, string code)
        {
            var plan = Compile(components);
            check(!plan.CanBuild && plan.Diagnostics.Any(item => item.Code == "VRCFURY_WARDROBE_" + code), Describe(plan));
        }
        test("Wardrobe compiles thirty exact independent native control plans", () =>
        {
            var plan = Compile(string.Concat(Enumerable.Range(0, 30).Select(index => Toggle(index, "Wardrobe/Outfit " + index,
                ObjectAction(10 + index), index % 2 == 0 ? "defaultOn: 1" : ""))));
            check(plan.CanBuild && plan.Controls.Count == 30, Describe(plan));
            for (int index = 0; index < 30; index++)
            {
                var control = plan.Controls[index];
                check(control.Path == "Wardrobe/Outfit " + index && control.DefaultOn == (index % 2 == 0), "Authored order/group/default lost.");
                check(control.Bindings.Single().On.TargetId == 10 + index && control.Bindings.Single().On.Active &&
                    !control.Bindings.Single().Off.Active, "Exact target/state mapping lost.");
            }
        });
        test("Wardrobe exact identities distinguish duplicate clothing names", () =>
        {
            var plan = Compile(Toggle(0, "A", ObjectAction(10)) + Toggle(1, "B", ObjectAction(11)));
            check(plan.CanBuild && plan.Controls[0].Bindings[0].On.TargetId != plan.Controls[1].Bindings[0].On.TargetId, Describe(plan));
        });
        test("Wardrobe multi-output TurnOn and TurnOff retain inverse states", () =>
        {
            string component = Toggle(0, "Outfit", ObjectAction(10));
            component = component.Replace("actions: [{rid: 1002}]", "actions: [{rid: 1002}, {rid: 1003}]", StringComparison.Ordinal) +
                Entry(1003, "ObjectToggleAction", "VF.Model.StateAction", ObjectAction(11, 1));
            var plan = Compile(component);
            check(plan.CanBuild && plan.Controls.Single().Bindings.Count == 2, Describe(plan));
            var bindings = plan.Controls.Single().Bindings;
            check(bindings[0].On.Active && !bindings[0].Off.Active && !bindings[1].On.Active && bindings[1].Off.Active, "Inverse state lost.");
        });
        test("Wardrobe shape off restores original snapshot rather than default-on result", () =>
        {
            var context = Context();
            var plan = Compile(Toggle(0, "Fit", "version: 0, allRenderers: 0, renderer: {fileID: 300}, blendShape: Fit, blendShapeValue: 80",
                "defaultOn: 1", "BlendShapeAction"), context);
            var binding = plan.Controls.Single().Bindings.Single();
            check(plan.CanBuild && binding.Off.Weight == 20 && binding.On.Weight == 80 && context.Renderers[300].Shapes["Fit"] == 20, Describe(plan));
        });
        test("Wardrobe allRenderers remains inside owning avatar", () =>
        {
            var plan = Compile(Toggle(0, "Fit", "blendShape: Fit, blendShapeValue: 75", actionType: "BlendShapeAction"));
            check(plan.CanBuild && plan.Controls.Single().Bindings.Count == 2 &&
                plan.Controls.Single().Bindings.All(item => item.On.TargetId != 900), Describe(plan));
        });
        foreach (bool defaultOn in new[] { true, false })
            test("Wardrobe authored blendshape snapshot survives initial appearance defaultOn=" + defaultOn, () =>
            {
                var authored = Context();
                string scene = Scene(Toggle(0, "Fit", "version: 0, allRenderers: 0, renderer: {fileID: 300}, blendShape: Fit, blendShapeValue: 80",
                    "defaultOn: " + (defaultOn ? "1" : "0"), "BlendShapeAction"));
                var appearance = VrcFuryInitialAppearance.Evaluate(scene, authored);
                check(appearance.CanApply && appearance.Renderers[300].Shapes["Fit"] == (defaultOn ? 80 : 20),
                    "Initial appearance did not produce the expected live weight.");
                var plan = VrcFuryInitialAppearance.CompileWardrobe(scene, authored);
                var binding = plan.Controls.Single().Bindings.Single();
                check(plan.CanBuild && plan.Controls.Single().DefaultOn == defaultOn && binding.Off.Weight == 20 &&
                    binding.On.Weight == 80 && authored.Renderers[300].Shapes["Fit"] == 20, Describe(plan));
            });
        test("Wardrobe MaterialAction v1 and legacy v0 obj resolve exact renderer and baseline material", () =>
        {
            var v1Plan = Compile(Toggle(0, "MatV1", "version: 1, renderer: {fileID: 300}, materialIndex: 0, mat: {version: 1, id: '" + altMatGuid + ":2100000'}",
                actionType: "MaterialAction"));
            var v1Binding = v1Plan.Controls.Single().Bindings.Single();
            check(v1Plan.CanBuild && v1Binding.On.Effect == InitialAppearanceEffect.Material && v1Binding.On.TargetId == 300 &&
                v1Binding.On.Property == "0" && v1Binding.Off.Material == new InitialAppearanceMaterial(2100000, baselineMatGuid) &&
                v1Binding.On.Material == new InitialAppearanceMaterial(2100000, altMatGuid), Describe(v1Plan));

            var v0Plan = Compile(Toggle(0, "MatV0", "version: 0, obj: {fileID: 10}, materialIndex: 0, mat: {version: 1, id: '" + altMatGuid + ":2100000'}",
                actionType: "MaterialAction"));
            var v0Binding = v0Plan.Controls.Single().Bindings.Single();
            check(v0Plan.CanBuild && v0Binding.On.TargetId == 300 &&
                v0Binding.Off.Material == new InitialAppearanceMaterial(2100000, baselineMatGuid) &&
                v0Binding.On.Material == new InitialAppearanceMaterial(2100000, altMatGuid), Describe(v0Plan));
        });
        foreach (bool defaultOn in new[] { true, false })
            test("Wardrobe authored material baseline survives initial appearance defaultOn=" + defaultOn, () =>
            {
                var authored = Context();
                string scene = Scene(Toggle(0, "Color", "version: 1, renderer: {fileID: 300}, materialIndex: 0, mat: {version: 1, id: '" + altMatGuid + ":2100000'}",
                    "defaultOn: " + (defaultOn ? "1" : "0"), "MaterialAction"));
                var appearance = VrcFuryInitialAppearance.Evaluate(scene, authored);
                check(appearance.CanApply && appearance.Renderers[300].Materials[0].Guid == (defaultOn ? altMatGuid : baselineMatGuid),
                    "Initial appearance did not produce the expected live material.");
                var plan = VrcFuryInitialAppearance.CompileWardrobe(scene, authored);
                var binding = plan.Controls.Single().Bindings.Single();
                check(plan.CanBuild && plan.Controls.Single().DefaultOn == defaultOn &&
                    binding.Off.Material == new InitialAppearanceMaterial(2100000, baselineMatGuid) &&
                    binding.On.Material == new InitialAppearanceMaterial(2100000, altMatGuid) &&
                    authored.Renderers[300].Materials[0].Guid == baselineMatGuid, Describe(plan));
            });
        test("Wardrobe MaterialAction rejects out-of-range slot, missing baseline, or conflicting slot writers", () =>
        {
            Reject(Toggle(0, "A", "version: 1, renderer: {fileID: 300}, materialIndex: 1, mat: {version: 1, id: '" + altMatGuid + ":2100000'}",
                actionType: "MaterialAction"), "MATERIAL_CAPACITY");
            Reject(Toggle(0, "A", "version: 1, renderer: {fileID: 301}, materialIndex: 0, mat: {version: 1, id: '" + altMatGuid + ":2100000'}",
                actionType: "MaterialAction"), "MATERIAL_BASELINE");
            string action = "version: 1, renderer: {fileID: 300}, materialIndex: 0, mat: {version: 1, id: '" + altMatGuid + ":2100000'}";
            Reject(Toggle(0, "A", action, actionType: "MaterialAction") + Toggle(1, "B", action, actionType: "MaterialAction"), "CONFLICT");
            Reject(Toggle(0, "A", "version: 0, obj: {fileID: broken}, renderer: {fileID: 300}, materialIndex: 0, mat: {version: 1, id: '" + altMatGuid + ":2100000'}",
                actionType: "MaterialAction"), "TARGET");
            Reject(Toggle(0, "A", "version: 0, obj: {fileID: 10, guid: " + altMatGuid + "}, renderer: {fileID: 300}, materialIndex: 0, mat: {version: 1, id: '" + altMatGuid + ":2100000'}",
                actionType: "MaterialAction"), "TARGET");
        });
        test("Wardrobe structural MA and VRCFury do not block independent controls", () =>
        {
            string structural = Component(800, AvatarPackageIndex.MergeArmatureScriptGuid) +
                Component(801, AvatarPackageIndex.BoneProxyScriptGuid) + Component(802, AvatarPackageIndex.OutfitRootScriptGuid) +
                Toggle(1, "", "", featureType: "ArmatureLink");
            var plan = Compile(Toggle(0, "Outfit", ObjectAction(10)) + structural);
            check(plan.CanBuild && plan.Controls.Count == 1, Describe(plan));
        });
        test("Wardrobe explicit external runtime verification is mandatory", () =>
        {
            var plan = Compile(Toggle(0, "Outfit", ObjectAction(10)), new InitialAppearanceContext { AvatarRootId = 1 });
            check(!plan.CanBuild && plan.HasUnsupportedContext, Describe(plan));
        });
        foreach (string guid in new[] { AvatarPackageIndex.MenuInstallerScriptGuid, AvatarPackageIndex.ParametersScriptGuid,
            AvatarPackageIndex.BlendshapeSyncScriptGuid, ModularAvatarInitialAppearance.ObjectToggleGuid,
            ModularAvatarInitialAppearance.MenuItemGuid, ModularAvatarInitialAppearance.MergeAnimatorGuid,
            ModularAvatarInitialAppearance.ShapeChangerGuid, ModularAvatarInitialAppearance.MaterialSetterGuid })
            test("Wardrobe rejects mixed MA runtime " + guid, () => Reject(Toggle(0, "A", ObjectAction(10)) + Component(800, guid), "CONTEXT"));
        test("Wardrobe rejects Animator even with absent-context assertion", () =>
            Reject(Toggle(0, "A", ObjectAction(10)) + "--- !u!95 &800\nAnimator:\n  m_GameObject: {fileID: 1}\n", "CONTEXT"));
        foreach (string field in new[] { "expressionParameters: {fileID: 1, guid: abcdef}",
            "baseAnimationLayers: [{animatorController: {fileID: 1, guid: abcdef}}]",
            "specialAnimationLayers: [{animatorController: {fileID: 1}}]" })
            test("Wardrobe rejects descriptor runtime despite absent-context assertion " + field, () =>
                Reject(Toggle(0, "A", ObjectAction(10)) + Component(800, AvatarPackageIndex.VrcAvatarDescriptorScriptGuid) + "  " + field + "\n", "CONTEXT"));
        test("Wardrobe alternate wrappers are reported rather than silently omitted", () =>
            Reject(Component(800, AvatarPackageIndex.VrcFuryLegacyScriptGuid), "WRAPPER"));
        test("Wardrobe foreign legacy wrapper alone is an empty no-op", () =>
        {
            var plan = Compile(Component(800, AvatarPackageIndex.VrcFuryLegacyScriptGuid)
                .Replace("m_GameObject: {fileID: 2}", "m_GameObject: {fileID: 9}", StringComparison.Ordinal));
            check(plan.Controls.Count == 0 && plan.Diagnostics.Count == 0 && !plan.HasUnsupportedContext, Describe(plan));
        });
        test("Wardrobe foreign legacy and malformed primary wrappers do not block owned controls", () =>
        {
            string foreign = (Component(800, AvatarPackageIndex.VrcFuryLegacyScriptGuid) + Component(801, VrcFuryInitialAppearance.ScriptGuid))
                .Replace("m_GameObject: {fileID: 2}", "m_GameObject: {fileID: 9}", StringComparison.Ordinal);
            var plan = Compile(Toggle(0, "Outfit", ObjectAction(10)) + foreign);
            check(plan.CanBuild && plan.Diagnostics.Count == 0, Describe(plan));
        });
        test("Wardrobe malformed owned primary wrapper retains diagnostics without a Toggle", () =>
        {
            var plan = Compile(Component(800, VrcFuryInitialAppearance.ScriptGuid));
            check(plan.HasUnsupportedContext && plan.Diagnostics.Any(item => item.SourceId == 800), Describe(plan));
        });
        test("Wardrobe primary wrapper requires an exact script fileID", () =>
            Reject(Toggle(0, "Outfit", ObjectAction(10))
                .Replace("fileID: 11500000, guid:", "fileID: 123, guid:", StringComparison.Ordinal), "WRAPPER"));
        test("Wardrobe primary wrapper without ownership cannot be silently omitted", () =>
            Reject(Component(800, VrcFuryInitialAppearance.ScriptGuid)
                .Replace("  m_GameObject: {fileID: 2}\n", "", StringComparison.Ordinal), "SCOPE"));
        foreach (string owner in new[] { "{fileID: 0}", "{fileID: 999}", "{fileID: invalid}", "{fileID: 2, guid: abcdef}" })
            test("Wardrobe unverified wrapper ownership fails closed " + owner, () =>
                Reject(Toggle(0, "Outfit", ObjectAction(10)) + Component(800, VrcFuryInitialAppearance.ScriptGuid)
                    .Replace("m_GameObject: {fileID: 2}", "m_GameObject: " + owner, StringComparison.Ordinal), "SCOPE"));
        test("Wardrobe rejects legacy Animation despite absent-context assertion", () =>
            Reject(Toggle(0, "Outfit", ObjectAction(10)) + "--- !u!111 &800\nAnimation:\n  m_GameObject: {fileID: 1}\n", "CONTEXT"));
        foreach (string guid in new[] { "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "" })
            test("Wardrobe unknown MonoBehaviour without editor identifier fails closed " + guid, () =>
                Reject(Toggle(0, "Outfit", ObjectAction(10)) + Component(800, guid), "CONTEXT"));
        test("Wardrobe foreign unknown MonoBehaviour and legacy Animation do not block owned controls", () =>
        {
            string foreign = (Component(800, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa") +
                "--- !u!111 &801\nAnimation:\n  m_GameObject: {fileID: 2}\n")
                .Replace("m_GameObject: {fileID: 2}", "m_GameObject: {fileID: 9}", StringComparison.Ordinal);
            var plan = Compile(Toggle(0, "Outfit", ObjectAction(10)) + foreign);
            check(plan.CanBuild, Describe(plan));
        });
        test("Wardrobe exact SDK descriptor without runtime references is safe", () =>
        {
            var plan = Compile(Toggle(0, "Outfit", ObjectAction(10)) + Component(800, AvatarPackageIndex.VrcAvatarDescriptorScriptGuid));
            check(plan.CanBuild, Describe(plan));
        });
        foreach (string guid in new[] { AvatarPackageIndex.VrcAvatarDescriptorScriptGuid, AvatarPackageIndex.MergeArmatureScriptGuid,
            AvatarPackageIndex.BoneProxyScriptGuid, AvatarPackageIndex.OutfitRootScriptGuid, AvatarPackageIndex.VrcFuryArmatureLinkScriptGuid })
            test("Wardrobe safe script GUID cannot allow a different fileID " + guid, () =>
                Reject(Toggle(0, "Outfit", ObjectAction(10)) + Component(800, guid)
                    .Replace("fileID: " + (guid == AvatarPackageIndex.VrcAvatarDescriptorScriptGuid ? AvatarPackageIndex.VrcAvatarDescriptorScriptFileId : 11500000) + ", guid:",
                        "fileID: 123, guid:", StringComparison.Ordinal), "CONTEXT"));
        test("Wardrobe physics reset is explicitly rejected", () =>
            Reject(Toggle(0, "A", ObjectAction(10), "resetPhysbones: [{fileID: 10}]"), "OPTION"));
        test("Wardrobe malformed optional booleans cannot fail open", () =>
            Reject(Toggle(0, "A", ObjectAction(10), "saved: perhaps"), "FIELD"));
        test("Wardrobe duplicate actions are terminal writer conflicts", () =>
        {
            string component = Toggle(0, "A", ObjectAction(10)).Replace("actions: [{rid: 1002}]",
                "actions: [{rid: 1002}, {rid: 1002}]", StringComparison.Ordinal);
            Reject(component, "CONFLICT");
        });
        test("Wardrobe duplicate managed IDs cannot build", () =>
        {
            var plan = Compile(Toggle(0, "A", ObjectAction(10)) + Entry(1002, "ObjectToggleAction", "VF.Model.StateAction", ObjectAction(11)));
            check(!plan.CanBuild && plan.HasUnsupportedContext, Describe(plan));
        });
        test("Wardrobe control cannot deactivate another Toggle host", () =>
        {
            string components = Toggle(0, "A", ObjectAction(10)) + Toggle(1, "B", ObjectAction(11))
                .Replace("m_GameObject: {fileID: 2}", "m_GameObject: {fileID: 10}", StringComparison.Ordinal);
            Reject(components, "TARGET");
        });
        test("Wardrobe cyclic hierarchy is rejected", () =>
        {
            string scene = Scene(Toggle(0, "A", ObjectAction(10))).Replace("m_Father: {fileID: 0}",
                "m_Father: {fileID: 102}", StringComparison.Ordinal);
            var plan = VrcFuryInitialAppearance.CompileWardrobe(scene, Context());
            check(!plan.CanBuild && plan.Diagnostics.Any(item => item.Code.EndsWith("HIERARCHY", StringComparison.Ordinal)), Describe(plan));
        });
        test("Wardrobe unknown managed type namespace is rejected", () =>
        {
            var plan = Compile(Toggle(0, "A", ObjectAction(10)).Replace("ns: VF.Model.StateAction", "ns: Unknown.Actions", StringComparison.Ordinal));
            check(!plan.CanBuild && plan.HasUnsupportedContext, Describe(plan));
        });
        foreach (string feature in new[] { "FullController", "BlendShapeLink", "ApplyDuringUpload" })
            test("Wardrobe rejects coexisting runtime/resting feature " + feature, () =>
                Reject(Toggle(0, "A", ObjectAction(10)) + Toggle(1, "Other", ObjectAction(11), featureType: feature), "CONTEXT"));
        foreach (string flag in new[] { "saved", "slider", "securityEnabled", "enableExclusiveTag", "exclusiveOffState",
            "hasTransition", "separateLocal", "useGlobalParam", "enableDriveGlobalParam", "holdButton", "invertRestLogic", "hasExitTime" })
            test("Wardrobe rejects uncompiled Toggle option " + flag, () => Reject(Toggle(0, "A", ObjectAction(10), flag + ": 1"), "OPTION"));
        foreach (string flag in new[] { "localOnly", "remoteOnly", "desktopActive", "androidActive" })
            test("Wardrobe rejects conditional action " + flag, () => Reject(Toggle(0, "A", ObjectAction(10) + ", " + flag + ": 1"), "FILTER"));
        foreach (string platformFlags in new[] { "desktopActive: 0, androidActive: 0", "desktopActive: 1, androidActive: 1" })
            test("Wardrobe equal platform action flags are unrestricted " + platformFlags, () =>
            {
                var plan = Compile(Toggle(0, "A", ObjectAction(10) + ", " + platformFlags));
                check(plan.CanBuild && plan.Controls.Single().Bindings.Single().On.Active, Describe(plan));
            });
        test("Wardrobe malformed platform action flags cannot fail open", () =>
            Reject(Toggle(0, "A", ObjectAction(10) + ", desktopActive: perhaps, androidActive: 0"), "FIELD"));
        foreach (string target in new[] { "1", "2", "9", "999" })
            test("Wardrobe rejects root host foreign or missing visibility target " + target, () =>
                Reject(Toggle(0, "A", "version: 1, obj: {fileID: " + target + "}, mode: 0"), "TARGET"));
        test("Wardrobe refuses external references rather than borrowing identical local IDs", () =>
            Reject(Toggle(0, "A", "version: 1, obj: {fileID: 10, guid: abcdef}, mode: 0"), "TARGET"));
        test("Wardrobe rejects legacy invert action mode", () => Reject(Toggle(0, "A", ObjectAction(10, 2)), "ACTION_VERSION"));
        test("Wardrobe rejects old ObjectToggleAction versions", () =>
            Reject(Toggle(0, "A", "version: 0, obj: {fileID: 10}"), "ACTION_VERSION"));
        test("Wardrobe rejects future action versions", () =>
            Reject(Toggle(0, "A", "version: 2, obj: {fileID: 10}"), "ACTION_VERSION"));
        test("Wardrobe rejects unsupported action without partial wardrobe", () =>
            Reject(Toggle(0, "A", ObjectAction(10)) + Toggle(1, "B", "", actionType: "FlipbookAction"), "ACTION"));
        test("Wardrobe rejects shared-property writers even when values agree", () =>
            Reject(Toggle(0, "A", ObjectAction(10)) + Toggle(1, "B", ObjectAction(10)), "CONFLICT"));
        test("Wardrobe rejects shared blendshape terminal writers", () =>
        {
            string action = "allRenderers: 0, renderer: {fileID: 300}, blendShape: Fit";
            Reject(Toggle(0, "A", action, actionType: "BlendShapeAction") + Toggle(1, "B", action, actionType: "BlendShapeAction"), "CONFLICT");
        });
        foreach (string path in new[] { "A", "A/B", "", "A//B", "../A" })
            test("Wardrobe rejects ambiguous menu path " + path, () => Reject(Toggle(0, "A", ObjectAction(10)) + Toggle(1, path, ObjectAction(11)), "PATH"));
        test("Wardrobe rejects missing exact blendshape", () =>
            Reject(Toggle(0, "A", "blendShape: Missing", actionType: "BlendShapeAction"), "SHAPE"));
        test("Wardrobe rejects nonfinite blendshape values", () =>
            Reject(Toggle(0, "A", "blendShape: Fit, blendShapeValue: NaN", actionType: "BlendShapeAction"), "WEIGHT"));
        test("Wardrobe reports unconverted optional icons without inventing binding", () =>
        {
            var plan = Compile(Toggle(0, "A", ObjectAction(10), "enableIcon: 1"));
            check(plan.CanBuild && plan.Diagnostics.Any(item => item.Code.EndsWith("ICON", StringComparison.Ordinal)), Describe(plan));
        });
        test("Wardrobe corrupt managed reference graph fails closed", () =>
        {
            var plan = Compile(Toggle(0, "A", ObjectAction(10)).Replace("actions: [{rid: 1002}]", "actions: [{rid: 99999}]", StringComparison.Ordinal));
            check(!plan.CanBuild && plan.Diagnostics.Any(item => item.Severity == InitialAppearanceSeverity.Error), Describe(plan));
        });
        test("Wardrobe cyclic managed reference graph fails closed", () =>
        {
            var plan = Compile(Toggle(0, "A", ObjectAction(10)).Replace("state: {actions: [{rid: 1002}]}", "state: {rid: 1000}", StringComparison.Ordinal));
            check(!plan.CanBuild, Describe(plan));
        });
        test("Wardrobe malformed YAML and duplicate IDs fail closed", () =>
        {
            check(!VrcFuryInitialAppearance.CompileWardrobe("bad", Context()).CanBuild, "Malformed YAML accepted.");
            check(!Compile(Toggle(0, "A", ObjectAction(10)) + Toggle(0, "B", ObjectAction(11))).CanBuild, "Duplicate component IDs accepted.");
        });
        test("Wardrobe no Toggle is an empty no-op", () =>
        {
            var plan = Compile("");
            check(!plan.CanBuild && plan.Controls.Count == 0 && plan.Diagnostics.Count == 0, Describe(plan));
        });
        test("Wardrobe cancellation is propagated", () =>
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            bool canceled = false;
            try { VrcFuryInitialAppearance.CompileWardrobe(Scene(Toggle(0, "A", ObjectAction(10))), Context(), cancellation.Token); }
            catch (OperationCanceledException) { canceled = true; }
            check(canceled, "Cancellation was swallowed.");
        });
        test("Wardrobe blendshape parsing remains culture invariant", () =>
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                var plan = Compile(Toggle(0, "A", "blendShape: Fit, blendShapeValue: 12.5", actionType: "BlendShapeAction"));
                check(plan.CanBuild && plan.Controls.Single().Bindings.All(item => item.On.Weight == 12.5f), Describe(plan));
            }
            finally { CultureInfo.CurrentCulture = original; }
        });

        test("Wardrobe compiles MaterialPropertyAction to property bindings", () =>
        {
            var plan = Compile(Toggle(0, "Tint", "propertyName: _Color, affectAllMeshes: 1, value: 0.8", actionType: "MaterialPropertyAction"));
            check(plan.CanBuild && plan.Controls.Count == 1, Describe(plan));
            var control = plan.Controls.Single();
            check(control.Bindings.Count == 2, $"Expected 2 bindings, got {control.Bindings.Count}");
            check(control.Bindings.All(b => b.On.Property == "_Color" && b.On.Weight == 0.8f && b.Off.Weight == 0f), "Wrong MaterialPropertyAction binding values");
        });

        test("Wardrobe compiles ScaleAction and FxFloatAction", () =>
        {
            var planScale = Compile(Toggle(0, "Scale", "obj: {fileID: 10}, scale: 1.5", actionType: "ScaleAction"));
            check(planScale.CanBuild && planScale.Controls.Count == 1, Describe(planScale));
            check(planScale.Controls[0].Bindings.Count == 1, "Expected 1 scale binding.");

            var planFloat = Compile(Toggle(0, "FxFloat", "name: Blush, value: 0.75", actionType: "FxFloatAction"));
            check(planFloat.CanBuild && planFloat.Controls.Count == 1, Describe(planFloat));
            check(planFloat.Controls[0].Bindings.Count == 1, "Expected 1 float binding.");
            check(planFloat.Controls[0].Bindings[0].On.Weight == 0.75f, "Expected float weight 0.75.");
        });

        test("Wardrobe compiles Modes feature to multi-state wardrobe controls", () =>
        {
            string modesFeature = @"--- !u!114 &999
MonoBehaviour:
  m_GameObject: {fileID: 1}
  m_Script: {fileID: 11500000, guid: " + VrcFuryInitialAppearance.ScriptGuid + @"}
  content: {rid: 2000}
  references:
    version: 2
    RefIds:
      - rid: 2000
        type: {class: Modes, ns: VF.Model.Feature, asm: VRCFury}
        data:
          name: Outfits
          modes:
            - name: Casual
              actions:
                - className: ObjectToggleAction
                  obj: {fileID: 10}
                  mode: 0
            - name: Formal
              actions:
                - className: ObjectToggleAction
                  obj: {fileID: 11}
                  mode: 0
";
            var plan = VrcFuryInitialAppearance.CompileWardrobe(Scene(modesFeature), Context());
            check(plan.CanBuild, Describe(plan));
            check(plan.Controls.Count == 2, $"Expected 2 mode controls, got {plan.Controls.Count}.");
            check(plan.Controls[0].Path == "Outfits/Casual", $"Expected Outfits/Casual, got {plan.Controls[0].Path}.");
            check(plan.Controls[0].DefaultOn, "Expected Casual to be DefaultOn.");
            check(plan.Controls[1].Path == "Outfits/Formal", $"Expected Outfits/Formal, got {plan.Controls[1].Path}.");
            check(!plan.Controls[1].DefaultOn, "Expected Formal not DefaultOn.");
        });

        test("Wardrobe compiles Puppet feature to float parameter control", () =>
        {
            string puppetFeature = @"--- !u!114 &998
MonoBehaviour:
  m_GameObject: {fileID: 1}
  m_Script: {fileID: 11500000, guid: " + VrcFuryInitialAppearance.ScriptGuid + @"}
  content: {rid: 2001}
  references:
    version: 2
    RefIds:
      - rid: 2001
        type: {class: Puppet, ns: VF.Model.Feature, asm: VRCFury}
        data:
          name: Ears
          slider: 0.5
          actions:
            - rid: 2002
      - rid: 2002
        type: {class: BlendShapeAction, ns: VF.Model.StateAction, asm: VRCFury}
        data:
          blendShape: Fit
          blendShapeValue: 75
";
            var plan = VrcFuryInitialAppearance.CompileWardrobe(Scene(puppetFeature), Context());
            check(plan.CanBuild, Describe(plan));
            check(plan.Controls.Count == 1, $"Expected 1 puppet control, got {plan.Controls.Count}.");
            check(plan.Controls[0].Path == "Ears", $"Expected path Ears, got {plan.Controls[0].Path}.");
            check(plan.Controls[0].ParameterType == AvatarParameterType.Float, "Expected Float parameter type.");
            check(plan.Controls[0].Bindings.Count > 0, "Expected puppet bindings.");
        });

        test("Wardrobe compiles WorldDropAction to active toggle binding", () =>
        {
            var plan = Compile(Toggle(0, "Drop", "obj: {fileID: 10}", actionType: "WorldDropAction"));
            check(plan.CanBuild && plan.Controls.Count == 1, Describe(plan));
            check(plan.Controls[0].Bindings.Count == 1, "Expected 1 WorldDrop binding.");
            check(!plan.Controls[0].Bindings[0].Off.Active && plan.Controls[0].Bindings[0].On.Active, "Expected off=false, on=true.");
        });

        test("Wardrobe compiles Toes feature to continuous toe curl control", () =>
        {
            string toesFeature = @"--- !u!114 &997
MonoBehaviour:
  m_GameObject: {fileID: 1}
  m_Script: {fileID: 11500000, guid: " + VrcFuryInitialAppearance.ScriptGuid + @"}
  content: {rid: 2005}
  references:
    version: 2
    RefIds:
      - rid: 2005
        type: {class: Toes, ns: VF.Model.Feature, asm: VRCFury}
        data: {}
";
            var plan = VrcFuryInitialAppearance.CompileWardrobe(Scene(toesFeature), Context());
            check(plan.CanBuild, Describe(plan));
            check(plan.Controls.Count == 1, $"Expected 1 toes control, got {plan.Controls.Count}.");
            check(plan.Controls[0].Path == "Toes", $"Expected path Toes, got {plan.Controls[0].Path}.");
            check(plan.Controls[0].ParameterType == AvatarParameterType.Float, "Expected Float parameter type.");
            check(plan.Controls[0].Bindings.Count > 0, "Expected toes bindings.");
        });
    }

    private static string ObjectAction(long target, int mode = 0) => "version: 1, obj: {fileID: " + target + "}, mode: " + mode;
    private static string Toggle(int index, string path, string action, string flags = "", string actionType = "ObjectToggleAction", string featureType = "Toggle")
    {
        long root = 1000 + index * 10;
        return "--- !u!114 &" + (500 + index) + "\nMonoBehaviour:\n  m_GameObject: {fileID: 2}\n  m_Script: {fileID: 11500000, guid: " + VrcFuryInitialAppearance.ScriptGuid + "}\n  version: 3\n  content: {rid: " + root + "}\n  references:\n    version: 2\n    RefIds:\n" +
            Entry(root, featureType, "VF.Model.Feature", "version: " + (featureType == "Toggle" ? 3 : 0) + ", name: '" + path + "', " +
                (featureType == "Toggle" ? "state" : "action") + ": {actions: [{rid: " + (root + 2) + "}]}" + (flags.Length == 0 ? "" : ", " + flags)) +
            Entry(root + 2, actionType, "VF.Model.StateAction", action);
    }
    private static string Entry(long identity, string type, string space, string data) =>
        "    - rid: " + identity + "\n      type: {class: " + type + ", ns: " + space + ", asm: VRCFury}\n      data: {" + data + "}\n";
    private static string Component(long identity, string guid) =>
        "--- !u!114 &" + identity + "\nMonoBehaviour:\n  m_GameObject: {fileID: 2}\n  m_Script: {fileID: " +
        (guid == AvatarPackageIndex.VrcAvatarDescriptorScriptGuid ? AvatarPackageIndex.VrcAvatarDescriptorScriptFileId : 11500000) + ", guid: " + guid + "}\n";
    private static string Node(long identity, long parent, string name) =>
        "--- !u!1 &" + identity + "\nGameObject:\n  m_Name: " + name + "\n  m_IsActive: 1\n--- !u!4 &" + (100 + identity) +
        "\nTransform:\n  m_GameObject: {fileID: " + identity + "}\n  m_Father: {fileID: " + (parent == 0 ? 0 : parent + 100) + "}\n";
    private static string Scene(string components) => Node(1, 0, "Avatar") + Node(2, 1, "Host") + Node(9, 0, "Foreign") +
        string.Concat(Enumerable.Range(10, 30).Select(identity => Node(identity, 1, "Clothing"))) +
        "--- !u!137 &300\nSkinnedMeshRenderer:\n  m_GameObject: {fileID: 10}\n--- !u!137 &301\nSkinnedMeshRenderer:\n  m_GameObject: {fileID: 11}\n--- !u!137 &900\nSkinnedMeshRenderer:\n  m_GameObject: {fileID: 9}\n" + components;
    private static InitialAppearanceContext Context() => new()
    {
        AvatarRootId = 1, ExternalAnimationAndParameterContextVerifiedAbsent = true,
        Renderers = new Dictionary<long, InitialAppearanceRenderer>
        {
            [300] = new(10, new Dictionary<string, float> { ["Fit"] = 20 }, new[] { new InitialAppearanceMaterial(2100000, "11111111111111111111111111111111") }),
            [301] = new(11, new Dictionary<string, float> { ["Fit"] = 30 }, new[] { new InitialAppearanceMaterial(0, "") }),
            [900] = new(9, new Dictionary<string, float> { ["Fit"] = 90 }, Array.Empty<InitialAppearanceMaterial>())
        }
    };
}
