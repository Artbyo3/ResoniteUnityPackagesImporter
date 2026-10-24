using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using UnityPackageImporter.Models;

internal static class ModularAvatarWardrobeTests
{
    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        const string baseMat = "11111111111111111111111111111111";
        const string altMat = "22222222222222222222222222222222";
        const string iconGuid = "33333333333333333333333333333333";

        string Describe(AvatarWardrobePlan plan) => string.Join("; ", plan.Diagnostics.Select(item => item.Code + ": " + item.Message));
        InitialAppearanceContext Context(bool externalVerifiedAbsent = true) => new()
        {
            AvatarRootId = 1,
            ExternalAnimationAndParameterContextVerifiedAbsent = externalVerifiedAbsent,
            Renderers = new Dictionary<long, InitialAppearanceRenderer>
            {
                [300] = new(3, new Dictionary<string, float> { ["Fit"] = 15, ["Shrink"] = 0 },
                    new[] { new InitialAppearanceMaterial(2100000, baseMat) }),
                [301] = new(4, new Dictionary<string, float> { ["Fit"] = 30 },
                    new[] { new InitialAppearanceMaterial(0, "") })
            }
        };

        string SimpleScene(string menuExtra = "", string reactiveComponents = "502", string extraObjects = "", string extraRootComponents = "") =>
            "--- !u!1 &1\nGameObject:\n  m_Name: Avatar\n  m_IsActive: 1\n  m_Component:\n  - component: {fileID: 101}\n" + extraRootComponents +
            "--- !u!4 &101\nTransform:\n  m_GameObject: {fileID: 1}\n  m_Father: {fileID: 0}\n  m_Children:\n  - {fileID: 102}\n  - {fileID: 103}\n  - {fileID: 104}\n" +
            "--- !u!1 &2\nGameObject:\n  m_Name: JacketToggle\n  m_IsActive: 1\n  m_Component:\n  - component: {fileID: 102}\n  - component: {fileID: 500}\n  - component: {fileID: 501}\n" +
            string.Concat(reactiveComponents.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(id => "  - component: {fileID: " + id.Trim() + "}\n")) +
            "--- !u!4 &102\nTransform:\n  m_GameObject: {fileID: 2}\n  m_Father: {fileID: 101}\n  m_Children: []\n" +
            "--- !u!1 &3\nGameObject:\n  m_Name: JacketMesh\n  m_IsActive: 1\n  m_Component:\n  - component: {fileID: 103}\n  - component: {fileID: 300}\n" +
            "--- !u!4 &103\nTransform:\n  m_GameObject: {fileID: 3}\n  m_Father: {fileID: 101}\n  m_Children: []\n" +
            "--- !u!137 &300\nSkinnedMeshRenderer:\n  m_GameObject: {fileID: 3}\n" +
            "--- !u!1 &4\nGameObject:\n  m_Name: HatMesh\n  m_IsActive: 0\n  m_Component:\n  - component: {fileID: 104}\n  - component: {fileID: 301}\n" +
            "--- !u!4 &104\nTransform:\n  m_GameObject: {fileID: 4}\n  m_Father: {fileID: 101}\n  m_Children: []\n" +
            "--- !u!137 &301\nSkinnedMeshRenderer:\n  m_GameObject: {fileID: 4}\n" +
            "--- !u!114 &500\nMonoBehaviour:\n  m_GameObject: {fileID: 2}\n  m_Script: {fileID: 11500000, guid: " + AvatarPackageIndex.MenuInstallerScriptGuid + "}\n" +
            "--- !u!114 &501\nMonoBehaviour:\n  m_GameObject: {fileID: 2}\n  m_Script: {fileID: 11500000, guid: " + ModularAvatarInitialAppearance.MenuItemGuid + "}\n  Control:\n    type: 2\n    value: 1\n    parameter:\n      name: Jacket\n    icon: {fileID: 2800000, guid: " + iconGuid + "}\n  isDefault: 1\n  automaticValue: 1\n  label: Jacket\n" + menuExtra +
            extraObjects;

        string ObjectToggle(long id = 502, long owner = 2, string path = "JacketMesh", long target = 3, int active = 0, int inverted = 0) =>
            "--- !u!114 &" + id + "\nMonoBehaviour:\n  m_GameObject: {fileID: " + owner + "}\n  m_Script: {fileID: 11500000, guid: " + ModularAvatarInitialAppearance.ObjectToggleGuid + "}\n  m_inverted: " + inverted + "\n  m_objects:\n  - Object:\n      referencePath: " + path + "\n      targetObject: {fileID: " + target + "}\n    Active: " + active + "\n";

        string ShapeChanger(long id = 503, long owner = 2, string path = "JacketMesh", long target = 3, string shape = "Fit", float value = 85f, int changeType = 1, int inverted = 0) =>
            "--- !u!114 &" + id + "\nMonoBehaviour:\n  m_GameObject: {fileID: " + owner + "}\n  m_Script: {fileID: 11500000, guid: " + ModularAvatarInitialAppearance.ShapeChangerGuid + "}\n  m_inverted: " + inverted + "\n  m_shapes:\n  - Object:\n      referencePath: " + path + "\n      targetObject: {fileID: " + target + "}\n    ShapeName: " + shape + "\n    ChangeType: " + changeType + "\n    Value: " + value.ToString(CultureInfo.InvariantCulture) + "\n";

        string MaterialSetter(long id = 504, long owner = 2, string path = "JacketMesh", long target = 3, int index = 0, string guid = altMat, long fileId = 2100000, int inverted = 0) =>
            "--- !u!114 &" + id + "\nMonoBehaviour:\n  m_GameObject: {fileID: " + owner + "}\n  m_Script: {fileID: 11500000, guid: " + ModularAvatarInitialAppearance.MaterialSetterGuid + "}\n  m_inverted: " + inverted + "\n  m_objects:\n  - Object:\n      referencePath: " + path + "\n      targetObject: {fileID: " + target + "}\n    MaterialIndex: " + index + "\n    Material: {fileID: " + fileId + ", guid: " + guid + "}\n";

        test("MA wardrobe compiles ObjectToggle ShapeChanger and MaterialSetter under MenuInstaller", () =>
        {
            string yaml = SimpleScene(reactiveComponents: "502,503,504",
                extraObjects: ObjectToggle() + ShapeChanger() + MaterialSetter());
            var plan = ModularAvatarInitialAppearance.CompileWardrobe(yaml, Context());
            check(plan.CanBuild && plan.Controls.Count == 1, Describe(plan));
            var control = plan.Controls.Single();
            check(control.Path == "Jacket" && control.DefaultOn && control.IconGuid == iconGuid && control.Bindings.Count == 3, Describe(plan));
            var activeBinding = control.Bindings.Single(b => b.On.Effect == InitialAppearanceEffect.Active);
            check(activeBinding.On.TargetId == 3 && !activeBinding.On.Active && activeBinding.Off.Active, "ObjectToggle active/off mismatch.");
            var shapeBinding = control.Bindings.Single(b => b.On.Effect == InitialAppearanceEffect.Shape);
            check(shapeBinding.On.TargetId == 300 && shapeBinding.On.Weight == 85f && shapeBinding.Off.Weight == 15f, "ShapeChanger baseline/target mismatch.");
            var matBinding = control.Bindings.Single(b => b.On.Effect == InitialAppearanceEffect.Material);
            check(matBinding.On.TargetId == 300 && matBinding.Off.Material == new InitialAppearanceMaterial(2100000, baseMat) &&
                matBinding.On.Material == new InitialAppearanceMaterial(2100000, altMat), "MaterialSetter baseline/target mismatch.");
        });

        test("MA wardrobe respects m_inverted and restores authored m_IsActive baseline on ObjectToggle", () =>
        {
            string yaml = SimpleScene(reactiveComponents: "502,503,504",
                extraObjects: ObjectToggle(inverted: 1) + ShapeChanger(inverted: 1) + MaterialSetter(inverted: 1));
            var plan = ModularAvatarInitialAppearance.CompileWardrobe(yaml, Context());
            check(plan.CanBuild, Describe(plan));
            var bindings = plan.Controls.Single().Bindings;
            check(bindings.Single(b => b.On.Effect == InitialAppearanceEffect.Active).On.Active &&
                !bindings.Single(b => b.On.Effect == InitialAppearanceEffect.Active).Off.Active, "Inverted ObjectToggle failed.");
            check(bindings.Single(b => b.On.Effect == InitialAppearanceEffect.Shape).On.Weight == 15f &&
                bindings.Single(b => b.On.Effect == InitialAppearanceEffect.Shape).Off.Weight == 85f, "Inverted ShapeChanger failed.");
            check(bindings.Single(b => b.On.Effect == InitialAppearanceEffect.Material).On.Material!.Guid == baseMat &&
                bindings.Single(b => b.On.Effect == InitialAppearanceEffect.Material).Off.Material!.Guid == altMat, "Inverted MaterialSetter failed.");

            // Authored inactive HatMesh (m_IsActive: 0) with Active: 0 must restore baseline false when off and stay false when on
            string inactiveSame = SimpleScene(reactiveComponents: "502",
                extraObjects: ObjectToggle(path: "HatMesh", target: 4, active: 0, inverted: 0));
            var planInactiveSame = ModularAvatarInitialAppearance.CompileWardrobe(inactiveSame, Context());
            check(planInactiveSame.CanBuild, Describe(planInactiveSame));
            var bSame = planInactiveSame.Controls.Single().Bindings.Single();
            check(!bSame.Off.Active && !bSame.On.Active, "Authored inactive target with Active: 0 must not become active when off.");

            // Authored inactive HatMesh (m_IsActive: 0) with Active: 1 and m_inverted: 1 must be active when off and restore baseline false when on
            string inactiveInverted = SimpleScene(reactiveComponents: "502",
                extraObjects: ObjectToggle(path: "HatMesh", target: 4, active: 1, inverted: 1));
            var planInactiveInv = ModularAvatarInitialAppearance.CompileWardrobe(inactiveInverted, Context());
            check(planInactiveInv.CanBuild, Describe(planInactiveInv));
            var bInv = planInactiveInv.Controls.Single().Bindings.Single();
            check(bInv.Off.Active && !bInv.On.Active, "Inverted ObjectToggle on authored inactive target must restore false when on.");
        });

        test("MA wardrobe rejects momentary Button MenuItem controls and external reactive targetObject GUIDs", () =>
        {
            string buttonYaml = SimpleScene(extraObjects: ObjectToggle())
                .Replace("    type: 2\n", "    type: 1\n", StringComparison.Ordinal);
            var plan = ModularAvatarInitialAppearance.CompileWardrobe(buttonYaml, Context());
            check(!plan.CanBuild && plan.Diagnostics.Any(d => d.Code == "MA_WARDROBE_MENU_CONTROL_TYPE"), Describe(plan));

            string externalZeroFileId = SimpleScene(extraObjects: ObjectToggle(target: 0)
                .Replace("targetObject: {fileID: 0}", "targetObject: {fileID: 0, guid: 11111111111111111111111111111111}", StringComparison.Ordinal));
            var extPlan = ModularAvatarInitialAppearance.CompileWardrobe(externalZeroFileId, Context());
            check(!extPlan.CanBuild && extPlan.Diagnostics.Any(d => d.Code == "MA_WARDROBE_TARGET"), Describe(extPlan));
        });

        test("MA wardrobe fails closed on malformed ModularAvatarParameters", () =>
        {
            foreach (var (paramBody, expectedCode) in new[]
            {
                ("  parameters: broken\n", "MA_WARDROBE_PARAMETER_LIST"),
                ("  parameters:\n  - broken\n", "MA_WARDROBE_PARAMETER_ENTRY"),
                ("  parameters:\n  - nameOrPrefix: Jacket\n", "MA_WARDROBE_PARAMETER_SYNC_TYPE"),
                ("  parameters:\n  - nameOrPrefix: Jacket\n    syncType: 99\n", "MA_WARDROBE_PARAMETER_SYNC_TYPE"),
                ("  parameters:\n  - nameOrPrefix: Jacket\n    syncType: 3\n    defaultValue: .nan\n", "MA_WARDROBE_PARAMETER_DEFAULT"),
                ("  parameters:\n  - nameOrPrefix: Jacket\n    syncType: 2\n    defaultValue: 0.5\n", "MA_WARDROBE_PARAMETER_DEFAULT"),
                ("  parameters:\n  - nameOrPrefix: Jacket\n    syncType: 3\n    saved: maybe\n", "MA_WARDROBE_PARAMETER_BOOL")
            })
            {
                string paramComp = "--- !u!114 &600\nMonoBehaviour:\n  m_GameObject: {fileID: 2}\n  m_Script: {fileID: 11500000, guid: " + AvatarPackageIndex.ParametersScriptGuid + "}\n" + paramBody;
                string yaml = SimpleScene(reactiveComponents: "600,502", extraObjects: paramComp + ObjectToggle());
                var plan = ModularAvatarInitialAppearance.CompileWardrobe(yaml, Context());
                check(!plan.CanBuild && plan.Diagnostics.Any(d => d.Code == expectedCode), "Expected " + expectedCode + ", got: " + Describe(plan));
            }
        });

        test("MA wardrobe expands SubMenu and MenuGroup hierarchy into nested control paths and validates MenuSource", () =>
        {
            string yaml =
                "--- !u!1 &1\nGameObject:\n  m_Name: Avatar\n  m_IsActive: 1\n  m_Component:\n  - component: {fileID: 101}\n" +
                "--- !u!4 &101\nTransform:\n  m_GameObject: {fileID: 1}\n  m_Father: {fileID: 0}\n  m_Children:\n  - {fileID: 102}\n  - {fileID: 103}\n" +
                "--- !u!1 &2\nGameObject:\n  m_Name: OutfitsSubMenu\n  m_IsActive: 1\n  m_Component:\n  - component: {fileID: 102}\n  - component: {fileID: 500}\n  - component: {fileID: 501}\n" +
                "--- !u!4 &102\nTransform:\n  m_GameObject: {fileID: 2}\n  m_Father: {fileID: 101}\n  m_Children:\n  - {fileID: 105}\n" +
                "--- !u!1 &5\nGameObject:\n  m_Name: GroupNode\n  m_IsActive: 1\n  m_Component:\n  - component: {fileID: 105}\n  - component: {fileID: 505}\n" +
                "--- !u!4 &105\nTransform:\n  m_GameObject: {fileID: 5}\n  m_Father: {fileID: 102}\n  m_Children:\n  - {fileID: 106}\n" +
                "--- !u!1 &6\nGameObject:\n  m_Name: JacketItem\n  m_IsActive: 1\n  m_Component:\n  - component: {fileID: 106}\n  - component: {fileID: 506}\n  - component: {fileID: 502}\n" +
                "--- !u!4 &106\nTransform:\n  m_GameObject: {fileID: 6}\n  m_Father: {fileID: 105}\n  m_Children: []\n" +
                "--- !u!1 &3\nGameObject:\n  m_Name: JacketMesh\n  m_IsActive: 1\n  m_Component:\n  - component: {fileID: 103}\n  - component: {fileID: 300}\n" +
                "--- !u!4 &103\nTransform:\n  m_GameObject: {fileID: 3}\n  m_Father: {fileID: 101}\n  m_Children: []\n" +
                "--- !u!137 &300\nSkinnedMeshRenderer:\n  m_GameObject: {fileID: 3}\n" +
                "--- !u!114 &500\nMonoBehaviour:\n  m_GameObject: {fileID: 2}\n  m_Script: {fileID: 11500000, guid: " + AvatarPackageIndex.MenuInstallerScriptGuid + "}\n" +
                "--- !u!114 &501\nMonoBehaviour:\n  m_GameObject: {fileID: 2}\n  m_Script: {fileID: 11500000, guid: " + ModularAvatarInitialAppearance.MenuItemGuid + "}\n  Control:\n    type: 103\n  MenuSource: 1\n  label: Outfits\n" +
                "--- !u!114 &505\nMonoBehaviour:\n  m_GameObject: {fileID: 5}\n  m_Script: {fileID: 11500000, guid: " + ModularAvatarInitialAppearance.MenuGroupGuid + "}\n" +
                "--- !u!114 &506\nMonoBehaviour:\n  m_GameObject: {fileID: 6}\n  m_Script: {fileID: 11500000, guid: " + ModularAvatarInitialAppearance.MenuItemGuid + "}\n  Control:\n    type: 2\n    value: 1\n    parameter:\n      name: ''\n  isDefault: 0\n  automaticValue: 1\n  label: Winter Jacket\n" +
                ObjectToggle(id: 502, owner: 6);
            var plan = ModularAvatarInitialAppearance.CompileWardrobe(yaml, Context());
            check(plan.CanBuild && plan.Controls.Single().Path == "Outfits/Winter Jacket" && !plan.Controls.Single().DefaultOn, Describe(plan));

            var retainedOtherObjOnSource1 = ModularAvatarInitialAppearance.CompileWardrobe(
                yaml.Replace("  MenuSource: 1\n", "  MenuSource: 1\n  menuSource_otherObjectChildren: {fileID: 3}\n", StringComparison.Ordinal), Context());
            check(retainedOtherObjOnSource1.CanBuild && retainedOtherObjOnSource1.Controls.Single().Path == "Outfits/Winter Jacket", Describe(retainedOtherObjOnSource1));

            var invalidMenuSource = ModularAvatarInitialAppearance.CompileWardrobe(
                yaml.Replace("  MenuSource: 1\n", "  MenuSource: 99\n", StringComparison.Ordinal), Context());
            check(!invalidMenuSource.CanBuild && invalidMenuSource.Diagnostics.Any(d => d.Code == "MA_WARDROBE_MENU_SUBMENU_SOURCE"), Describe(invalidMenuSource));

            var missingOtherObj = ModularAvatarInitialAppearance.CompileWardrobe(
                yaml.Replace("  MenuSource: 1\n", "  MenuSource: 2\n", StringComparison.Ordinal), Context());
            check(!missingOtherObj.CanBuild && missingOtherObj.Diagnostics.Any(d => d.Code == "MA_WARDROBE_MENU_SUBMENU_TARGET"), Describe(missingOtherObj));

            var malformedOtherObj = ModularAvatarInitialAppearance.CompileWardrobe(
                yaml.Replace("  MenuSource: 1\n", "  MenuSource: 2\n  menuSource_otherObjectChildren: broken\n", StringComparison.Ordinal), Context());
            check(!malformedOtherObj.CanBuild && malformedOtherObj.Diagnostics.Any(d => d.Code == "MA_WARDROBE_MENU_SUBMENU_TARGET"), Describe(malformedOtherObj));
        });

        test("MA wardrobe resolves ModularAvatarParameters default values and internal parameter isolation", () =>
        {
            string paramComp =
                "--- !u!114 &600\nMonoBehaviour:\n  m_GameObject: {fileID: 2}\n  m_Script: {fileID: 11500000, guid: " + AvatarPackageIndex.ParametersScriptGuid + "}\n  parameters:\n  - nameOrPrefix: Jacket\n    remapTo: ''\n    internalParameter: 1\n    isPrefix: 0\n    syncType: 3\n    defaultValue: 1\n";
            string yaml = SimpleScene(menuExtra: "", reactiveComponents: "600,502",
                extraObjects: paramComp + ObjectToggle())
                .Replace("  isDefault: 1\n", "  isDefault: 0\n", StringComparison.Ordinal);
            var plan = ModularAvatarInitialAppearance.CompileWardrobe(yaml, Context());
            check(plan.CanBuild && plan.Controls.Single().DefaultOn, Describe(plan));
        });

        test("MA wardrobe rejects destructive ShapeChanger Delete mode in runtime controls", () =>
        {
            string yaml = SimpleScene(reactiveComponents: "503", extraObjects: ShapeChanger(changeType: 0));
            var plan = ModularAvatarInitialAppearance.CompileWardrobe(yaml, Context());
            check(!plan.CanBuild && plan.Diagnostics.Any(d => d.Code == "MA_WARDROBE_SHAPE_CHANGE_TYPE"), Describe(plan));
        });

        test("MA wardrobe rejects uninstalled MenuItem or uncontrolled reactive component", () =>
        {
            string uninstalled = SimpleScene(extraObjects: ObjectToggle()).Replace("  - component: {fileID: 500}\n", "", StringComparison.Ordinal);
            var plan1 = ModularAvatarInitialAppearance.CompileWardrobe(uninstalled, Context());
            check(!plan1.CanBuild && plan1.Diagnostics.Any(d => d.Code == "MA_WARDROBE_MENU_UNINSTALLED"), Describe(plan1));

            string uncontrolled = SimpleScene(reactiveComponents: "", extraRootComponents: "  - component: {fileID: 502}\n",
                extraObjects: ObjectToggle(owner: 1));
            var plan2 = ModularAvatarInitialAppearance.CompileWardrobe(uncontrolled, Context());
            check(!plan2.CanBuild && plan2.Diagnostics.Any(d => d.Code == "MA_WARDROBE_UNCONTROLLED"), Describe(plan2));
        });

        test("MA wardrobe rejects shared parameter across multiple MenuItems", () =>
        {
            string secondMenu =
                "--- !u!1 &7\nGameObject:\n  m_Name: HatToggle\n  m_IsActive: 1\n  m_Component:\n  - component: {fileID: 107}\n  - component: {fileID: 510}\n  - component: {fileID: 511}\n  - component: {fileID: 512}\n" +
                "--- !u!4 &107\nTransform:\n  m_GameObject: {fileID: 7}\n  m_Father: {fileID: 101}\n  m_Children: []\n" +
                "--- !u!114 &510\nMonoBehaviour:\n  m_GameObject: {fileID: 7}\n  m_Script: {fileID: 11500000, guid: " + AvatarPackageIndex.MenuInstallerScriptGuid + "}\n" +
                "--- !u!114 &511\nMonoBehaviour:\n  m_GameObject: {fileID: 7}\n  m_Script: {fileID: 11500000, guid: " + ModularAvatarInitialAppearance.MenuItemGuid + "}\n  Control:\n    type: 2\n    value: 1\n    parameter:\n      name: Jacket\n  isDefault: 0\n  automaticValue: 1\n  label: Hat\n" +
                ObjectToggle(id: 512, owner: 7, path: "HatMesh", target: 4, active: 1);
            string yaml = SimpleScene(extraObjects: ObjectToggle() + secondMenu)
                .Replace("  - {fileID: 104}\n", "  - {fileID: 104}\n  - {fileID: 107}\n", StringComparison.Ordinal);
            var plan = ModularAvatarInitialAppearance.CompileWardrobe(yaml, Context());
            check(!plan.CanBuild && plan.Diagnostics.Any(d => d.Code == "MA_WARDROBE_PARAMETER_SHARED_MENU"), Describe(plan));
        });

        test("MA wardrobe validates saved, localOnly, and syncType settings and rejects uncompiled options", () =>
        {
            string MakeParam(string extraFields = "", int syncType = 3) =>
                "--- !u!114 &600\nMonoBehaviour:\n  m_GameObject: {fileID: 2}\n  m_Script: {fileID: 11500000, guid: " + AvatarPackageIndex.ParametersScriptGuid + "}\n  parameters:\n  - nameOrPrefix: Jacket\n    remapTo: ''\n    internalParameter: 0\n    isPrefix: 0\n    syncType: " + syncType + "\n" + extraFields;

            // 1. saved: 1 reports MA_WARDROBE_OPTION
            string savedYaml = SimpleScene(reactiveComponents: "600,502", extraObjects: MakeParam("    saved: 1\n") + ObjectToggle());
            var savedPlan = ModularAvatarInitialAppearance.CompileWardrobe(savedYaml, Context());
            check(!savedPlan.CanBuild && savedPlan.Diagnostics.Any(d => d.Code == "MA_WARDROBE_OPTION" && d.Message.Contains("saved")),
                "Expected MA_WARDROBE_OPTION for saved: 1, got: " + Describe(savedPlan));

            // 2. localOnly: 1 reports MA_WARDROBE_OPTION
            string localYaml = SimpleScene(reactiveComponents: "600,502", extraObjects: MakeParam("    localOnly: 1\n") + ObjectToggle());
            var localPlan = ModularAvatarInitialAppearance.CompileWardrobe(localYaml, Context());
            check(!localPlan.CanBuild && localPlan.Diagnostics.Any(d => d.Code == "MA_WARDROBE_OPTION" && d.Message.Contains("localOnly")),
                "Expected MA_WARDROBE_OPTION for localOnly: 1, got: " + Describe(localPlan));

            // 3. syncType: 0 (NotSynced) reports MA_WARDROBE_OPTION
            string unSyncedComp =
                "--- !u!114 &600\nMonoBehaviour:\n  m_GameObject: {fileID: 2}\n  m_Script: {fileID: 11500000, guid: " + AvatarPackageIndex.ParametersScriptGuid + "}\n  parameters:\n  - nameOrPrefix: Jacket\n    remapTo: ''\n    internalParameter: 0\n    isPrefix: 0\n    syncType: 0\n";
            string unSyncedYaml = SimpleScene(reactiveComponents: "600,502", extraObjects: unSyncedComp + ObjectToggle());
            var unSyncedPlan = ModularAvatarInitialAppearance.CompileWardrobe(unSyncedYaml, Context());
            check(!unSyncedPlan.CanBuild && unSyncedPlan.Diagnostics.Any(d => d.Code == "MA_WARDROBE_OPTION" && d.Message.Contains("localOnly")),
                "Expected MA_WARDROBE_OPTION for syncType: 0, got: " + Describe(unSyncedPlan));

            // 4. saved: 0 and localOnly: 0 (syncType: 3) compiles cleanly and populates Saved: false, Synced: true
            string cleanYaml = SimpleScene(reactiveComponents: "600,502", extraObjects: MakeParam("    saved: 0\n    localOnly: 0\n") + ObjectToggle());
            var cleanPlan = ModularAvatarInitialAppearance.CompileWardrobe(cleanYaml, Context());
            check(cleanPlan.CanBuild && cleanPlan.Controls.Count == 1, Describe(cleanPlan));
            var control = cleanPlan.Controls.Single();
            check(!control.Saved && control.Synced && control.ParameterType == AvatarParameterType.Bool, "Control must have Saved == false, Synced == true, Type == Bool");
            var paramDecl = cleanPlan.SharedState!.Parameters.Definitions.Values.Single();
            check(!paramDecl.Saved && paramDecl.Synced && paramDecl.Type == AvatarParameterType.Bool, "Parameter declaration must have Saved == false, Synced == true, Type == Bool");

            // 5. syncType: 1 (Int) preserves Int parameter type
            string intYaml = SimpleScene(reactiveComponents: "600,502", extraObjects: MakeParam(syncType: 1) + ObjectToggle());
            var intPlan = ModularAvatarInitialAppearance.CompileWardrobe(intYaml, Context());
            check(intPlan.CanBuild && intPlan.Controls.Count == 1, Describe(intPlan));
            var intControl = intPlan.Controls.Single();
            check(intControl.ParameterType == AvatarParameterType.Int, "Control must preserve Int parameter type");
            var intDecl = intPlan.SharedState!.Parameters.Definitions.Values.Single();
            check(intDecl.Type == AvatarParameterType.Int, "Shared parameter must declare Int");

            // 6. syncType: 2 (Float) preserves Float parameter type
            string floatYaml = SimpleScene(reactiveComponents: "600,502", extraObjects: MakeParam(syncType: 2) + ObjectToggle());
            var floatPlan = ModularAvatarInitialAppearance.CompileWardrobe(floatYaml, Context());
            check(floatPlan.CanBuild && floatPlan.Controls.Count == 1, Describe(floatPlan));
            var floatControl = floatPlan.Controls.Single();
            check(floatControl.ParameterType == AvatarParameterType.Float, "Control must preserve Float parameter type");
            var floatDecl = floatPlan.SharedState!.Parameters.Definitions.Values.Single();
            check(floatDecl.Type == AvatarParameterType.Float, "Shared parameter must declare Float");
        });

        test("MA wardrobe permits coexisting VRCFury ArmatureLink structural component", () =>
        {
            // Standalone VRCFury ArmatureLink component
            string furyArmatureComp =
                "--- !u!114 &700\nMonoBehaviour:\n  m_GameObject: {fileID: 2}\n  m_Script: {fileID: 11500000, guid: " + AvatarPackageIndex.VrcFuryArmatureLinkScriptGuid + "}\n" +
                "  propBone: {fileID: 2}\n  linkTo:\n  - useBone: 1\n    bone: 0\n    useObj: 0\n    obj: {fileID: 0}\n    offset: ''\n";
            string yaml = SimpleScene(reactiveComponents: "502,700", extraObjects: ObjectToggle() + furyArmatureComp);
            var manifestArmature = AvatarPackageIndex.ParseText(yaml, "avatar.prefab");
            check(manifestArmature.VrcFuryComponents.Count == 1 &&
                  manifestArmature.VrcFuryComponents[0].ArmatureLinks.Count == 1 &&
                  manifestArmature.VrcFuryComponents[0].UnsupportedFeatures.Count == 0,
                  "Manifest must record verified ArmatureLink for standalone component");
            bool maRuntimeContext = manifestArmature.VrcFuryComponents.Any(c => c.ArmatureLinks.Count == 0 || c.UnsupportedFeatures.Count > 0);
            check(!maRuntimeContext, "Pure structural ArmatureLink must not trigger maRuntimeContext");
            var plan = ModularAvatarInitialAppearance.CompileWardrobe(yaml, Context(!maRuntimeContext));
            check(plan.CanBuild && plan.Controls.Count == 1, Describe(plan));

            // Full VRCFury component containing RefIds ArmatureLink
            string furyRefIdsComp =
                "--- !u!114 &701\nMonoBehaviour:\n  m_GameObject: {fileID: 2}\n  m_Script: {fileID: 11500000, guid: " + AvatarPackageIndex.VrcFuryFullScriptGuid + "}\n" +
                "  config:\n    features: [{rid: 101}]\n  references:\n    version: 2\n    RefIds:\n    - rid: 101\n      type: {class: ArmatureLink, ns: VF.Model.Feature, asm: VRCFury}\n      data:\n        version: 7\n        propBone: {fileID: 2}\n        linkTo:\n        - useBone: 1\n          bone: 0\n          useObj: 0\n          obj: {fileID: 0}\n          offset: ''\n";
            string yamlRefIds = SimpleScene(reactiveComponents: "502,701", extraObjects: ObjectToggle() + furyRefIdsComp);
            var manifestRefIds = AvatarPackageIndex.ParseText(yamlRefIds, "avatar.prefab");
            check(manifestRefIds.VrcFuryComponents.Count == 1 &&
                  manifestRefIds.VrcFuryComponents[0].ArmatureLinks.Count == 1 &&
                  manifestRefIds.VrcFuryComponents[0].UnsupportedFeatures.Count == 0,
                  "Manifest must record verified ArmatureLink for full VRCFury component");
            bool maRuntimeContextRefIds = manifestRefIds.VrcFuryComponents.Any(c => c.ArmatureLinks.Count == 0 || c.UnsupportedFeatures.Count > 0);
            check(!maRuntimeContextRefIds, "Pure structural RefIds ArmatureLink must not trigger maRuntimeContext");
            var planRefIds = ModularAvatarInitialAppearance.CompileWardrobe(yamlRefIds, Context(!maRuntimeContextRefIds));
            check(planRefIds.CanBuild && planRefIds.Controls.Count == 1, Describe(planRefIds));

            // Verify the import gate fails closed when an unsupported VRCFury feature is present
            string furyUnsupportedComp =
                "--- !u!114 &702\nMonoBehaviour:\n  m_GameObject: {fileID: 2}\n  m_Script: {fileID: 11500000, guid: " + AvatarPackageIndex.VrcFuryFullScriptGuid + "}\n" +
                "  config:\n    features: [{rid: 101}, {rid: 102}]\n  references:\n    version: 2\n    RefIds:\n" +
                "    - rid: 101\n      type: {class: ArmatureLink, ns: VF.Model.Feature, asm: VRCFury}\n      data:\n        version: 7\n        propBone: {fileID: 2}\n        linkTo:\n        - useBone: 1\n          bone: 0\n          useObj: 0\n          obj: {fileID: 0}\n          offset: ''\n" +
                "    - rid: 102\n      type: {class: Toggle, ns: VF.Model.Feature, asm: VRCFury}\n      data:\n        name: Jacket\n";
            string yamlUnsupported = SimpleScene(reactiveComponents: "502,702", extraObjects: ObjectToggle() + furyUnsupportedComp);
            var manifestUnsupported = AvatarPackageIndex.ParseText(yamlUnsupported, "avatar.prefab");
            check(manifestUnsupported.VrcFuryComponents.Count == 1 &&
                  manifestUnsupported.VrcFuryComponents[0].UnsupportedFeatures.Count > 0,
                  "Manifest must record unsupported feature for non-structural VRCFury");
            bool maRuntimeContextUnsupported = manifestUnsupported.VrcFuryComponents.Any(c => c.ArmatureLinks.Count == 0 || c.UnsupportedFeatures.Count > 0);
            check(maRuntimeContextUnsupported, "Non-structural VRCFury feature must trigger maRuntimeContext");
            var planUnsupported = ModularAvatarInitialAppearance.CompileWardrobe(yamlUnsupported, Context(!maRuntimeContextUnsupported));
            check(!planUnsupported.CanBuild && planUnsupported.Diagnostics.Any(d => d.Code == "MA_WARDROBE_CONTEXT"),
                  "MA wardrobe must fail closed with MA_WARDROBE_CONTEXT when runtime context is present: " + Describe(planUnsupported));
        });
    }
}
