using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using UnityPackageImporter.Models;

internal static class AvatarWardrobeCompositionTests
{
    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        const string baseMat = "11111111111111111111111111111111";
        const string altMat = "22222222222222222222222222222222";
        string Describe(AvatarWardrobePlan plan) => string.Join("; ", plan.Diagnostics.Select(item => item.Code + ": " + item.Message));

        InitialAppearanceContext Context() => new()
        {
            AvatarRootId = 1,
            Renderers = new Dictionary<long, InitialAppearanceRenderer>
            {
                [300] = new(3, new Dictionary<string, float> { ["Fit"] = 15 }, new[] { new InitialAppearanceMaterial(2100000, baseMat) }),
                [301] = new(4, new Dictionary<string, float> { ["Fit"] = 30 }, new[] { new InitialAppearanceMaterial(0, "") }),
                [302] = new(5, new Dictionary<string, float> { ["Fit"] = 45 }, new[] { new InitialAppearanceMaterial(2100000, baseMat) })
            }
        };

        string BaseScene() =>
            "--- !u!1 &1\nGameObject:\n  m_Name: Avatar\n  m_IsActive: 1\n  m_Component:\n  - component: {fileID: 101}\n" +
            "--- !u!4 &101\nTransform:\n  m_GameObject: {fileID: 1}\n  m_Father: {fileID: 0}\n  m_Children:\n  - {fileID: 102}\n  - {fileID: 103}\n  - {fileID: 104}\n  - {fileID: 105}\n" +
            "--- !u!1 &3\nGameObject:\n  m_Name: JacketMesh\n  m_IsActive: 1\n  m_Component:\n  - component: {fileID: 103}\n  - component: {fileID: 300}\n" +
            "--- !u!4 &103\nTransform:\n  m_GameObject: {fileID: 3}\n  m_Father: {fileID: 101}\n  m_Children: []\n" +
            "--- !u!137 &300\nSkinnedMeshRenderer:\n  m_GameObject: {fileID: 3}\n" +
            "--- !u!1 &4\nGameObject:\n  m_Name: HatMesh\n  m_IsActive: 0\n  m_Component:\n  - component: {fileID: 104}\n  - component: {fileID: 301}\n" +
            "--- !u!4 &104\nTransform:\n  m_GameObject: {fileID: 4}\n  m_Father: {fileID: 101}\n  m_Children: []\n" +
            "--- !u!137 &301\nSkinnedMeshRenderer:\n  m_GameObject: {fileID: 4}\n" +
            "--- !u!1 &5\nGameObject:\n  m_Name: ShoesMesh\n  m_IsActive: 1\n  m_Component:\n  - component: {fileID: 105}\n  - component: {fileID: 302}\n" +
            "--- !u!4 &105\nTransform:\n  m_GameObject: {fileID: 5}\n  m_Father: {fileID: 101}\n  m_Children: []\n" +
            "--- !u!137 &302\nSkinnedMeshRenderer:\n  m_GameObject: {fileID: 5}\n";

        string ModularAvatarToggle(long objId = 2, long transId = 102, long installerId = 500, long menuItemId = 501, long toggleId = 502,
            string label = "Jacket", long targetMesh = 3, string targetName = "JacketMesh", int active = 0) =>
            "--- !u!1 &" + objId + "\nGameObject:\n  m_Name: " + label + "Host\n  m_IsActive: 1\n  m_Component:\n  - component: {fileID: " + transId + "}\n  - component: {fileID: " + installerId + "}\n  - component: {fileID: " + menuItemId + "}\n  - component: {fileID: " + toggleId + "}\n" +
            "--- !u!4 &" + transId + "\nTransform:\n  m_GameObject: {fileID: " + objId + "}\n  m_Father: {fileID: 101}\n  m_Children: []\n" +
            "--- !u!114 &" + installerId + "\nMonoBehaviour:\n  m_GameObject: {fileID: " + objId + "}\n  m_Script: {fileID: 11500000, guid: " + AvatarPackageIndex.MenuInstallerScriptGuid + "}\n" +
            "--- !u!114 &" + menuItemId + "\nMonoBehaviour:\n  m_GameObject: {fileID: " + objId + "}\n  m_Script: {fileID: 11500000, guid: " + ModularAvatarInitialAppearance.MenuItemGuid + "}\n  Control:\n    type: 2\n    value: 1\n    parameter:\n      name: " + label + "\n  isDefault: 1\n  automaticValue: 1\n  label: " + label + "\n" +
            "--- !u!114 &" + toggleId + "\nMonoBehaviour:\n  m_GameObject: {fileID: " + objId + "}\n  m_Script: {fileID: 11500000, guid: " + ModularAvatarInitialAppearance.ObjectToggleGuid + "}\n  m_inverted: 0\n  m_objects:\n  - Object:\n      referencePath: " + targetName + "\n      targetObject: {fileID: " + targetMesh + "}\n    Active: " + active + "\n";

        string Entry(long identity, string type, string space, string data) =>
            "    - rid: " + identity + "\n      type: {class: " + type + ", ns: " + space + ", asm: VRCFury}\n      data: {" + data + "}\n";

        string VrcFuryToggle(long objId = 6, long transId = 106, long vrcfId = 600, int toggleIndex = 0, string path = "Hat", long targetMesh = 4, int mode = 0)
        {
            long root = 9000 + toggleIndex * 10;
            string action = "version: 1, obj: {fileID: " + targetMesh + "}, mode: " + mode;
            return "--- !u!1 &" + objId + "\nGameObject:\n  m_Name: " + path.Replace('/', '_') + "Host\n  m_IsActive: 1\n  m_Component:\n  - component: {fileID: " + transId + "}\n  - component: {fileID: " + vrcfId + "}\n" +
                "--- !u!4 &" + transId + "\nTransform:\n  m_GameObject: {fileID: " + objId + "}\n  m_Father: {fileID: 101}\n  m_Children: []\n" +
                "--- !u!114 &" + vrcfId + "\nMonoBehaviour:\n  m_GameObject: {fileID: " + objId + "}\n  m_Script: {fileID: 11500000, guid: " + VrcFuryInitialAppearance.ScriptGuid + "}\n  version: 3\n  content: {rid: " + root + "}\n  references:\n    version: 2\n    RefIds:\n" +
                Entry(root, "Toggle", "VF.Model.Feature", "version: 3, name: '" + path + "', state: {actions: [{rid: " + (root + 2) + "}]}") +
                Entry(root + 2, "ObjectToggleAction", "VF.Model.StateAction", action);
        }

        test("Unified wardrobe composes Modular Avatar and VRCFury on the same avatar without mutual rejection", () =>
        {
            string yaml = BaseScene()
                .Replace("  - {fileID: 105}\n", "  - {fileID: 105}\n  - {fileID: 106}\n", StringComparison.Ordinal) +
                ModularAvatarToggle() + VrcFuryToggle();

            var plan = AvatarWardrobeComposer.Compose(yaml, Context());
            check(plan.CanBuild, Describe(plan));
            check(plan.Controls.Count == 2, "Expected 2 controls, got: " + plan.Controls.Count);
            check(plan.Controls.Any(c => c.Path == "Jacket"), "Missing MA control Jacket");
            check(plan.Controls.Any(c => c.Path == "Hat"), "Missing VRCFury control Hat");
        });

        test("Unified wardrobe merges Modular Avatar and VRCFury into shared submenus", () =>
        {
            string maSubmenu =
                "--- !u!1 &2\nGameObject:\n  m_Name: SubMenuHost\n  m_IsActive: 1\n  m_Component:\n  - component: {fileID: 102}\n  - component: {fileID: 500}\n  - component: {fileID: 501}\n" +
                "--- !u!4 &102\nTransform:\n  m_GameObject: {fileID: 2}\n  m_Father: {fileID: 101}\n  m_Children:\n  - {fileID: 107}\n" +
                "--- !u!1 &7\nGameObject:\n  m_Name: JacketItem\n  m_IsActive: 1\n  m_Component:\n  - component: {fileID: 107}\n  - component: {fileID: 507}\n  - component: {fileID: 502}\n" +
                "--- !u!4 &107\nTransform:\n  m_GameObject: {fileID: 7}\n  m_Father: {fileID: 102}\n  m_Children: []\n" +
                "--- !u!114 &500\nMonoBehaviour:\n  m_GameObject: {fileID: 2}\n  m_Script: {fileID: 11500000, guid: " + AvatarPackageIndex.MenuInstallerScriptGuid + "}\n" +
                "--- !u!114 &501\nMonoBehaviour:\n  m_GameObject: {fileID: 2}\n  m_Script: {fileID: 11500000, guid: " + ModularAvatarInitialAppearance.MenuItemGuid + "}\n  Control:\n    type: 103\n  MenuSource: 1\n  label: Clothing\n" +
                "--- !u!114 &507\nMonoBehaviour:\n  m_GameObject: {fileID: 7}\n  m_Script: {fileID: 11500000, guid: " + ModularAvatarInitialAppearance.MenuItemGuid + "}\n  Control:\n    type: 2\n    value: 1\n    parameter:\n      name: Jacket\n  isDefault: 1\n  automaticValue: 1\n  label: Jacket\n" +
                "--- !u!114 &502\nMonoBehaviour:\n  m_GameObject: {fileID: 7}\n  m_Script: {fileID: 11500000, guid: " + ModularAvatarInitialAppearance.ObjectToggleGuid + "}\n  m_inverted: 0\n  m_objects:\n  - Object:\n      referencePath: JacketMesh\n      targetObject: {fileID: 3}\n    Active: 0\n";

            string vrcfToggle = VrcFuryToggle(path: "Clothing/Hat");
            string yaml = BaseScene()
                .Replace("  - {fileID: 105}\n", "  - {fileID: 105}\n  - {fileID: 106}\n", StringComparison.Ordinal) +
                maSubmenu + vrcfToggle;

            var plan = AvatarWardrobeComposer.Compose(yaml, Context());
            check(plan.CanBuild, Describe(plan));
            check(plan.Controls.Count == 2, "Expected 2 controls, got: " + plan.Controls.Count);
            check(plan.Controls.Any(c => c.Path == "Clothing/Jacket"), "Missing Clothing/Jacket");
            check(plan.Controls.Any(c => c.Path == "Clothing/Hat"), "Missing Clothing/Hat");

            var state = plan.SharedState!.Evaluate(plan.SharedState.Parameters.CreateState());
            check(state.Count == 2, "Expected 2 property states evaluated.");
        });

        test("Unified wardrobe detects and rejects conflicting multi-writer property targets", () =>
        {
            string yaml = BaseScene()
                .Replace("  - {fileID: 105}\n", "  - {fileID: 105}\n  - {fileID: 106}\n", StringComparison.Ordinal) +
                ModularAvatarToggle(targetMesh: 3, active: 0) +
                VrcFuryToggle(path: "AltJacket", targetMesh: 3, mode: 1);

            var plan = AvatarWardrobeComposer.Compose(yaml, Context());
            check(!plan.CanBuild, "Conflicting property targets must reject unified graph.");
            check(plan.Diagnostics.Any(d => d.Code == "WARDROBE_COMPOSER_PROPERTY_CONFLICT"),
                "Expected WARDROBE_COMPOSER_PROPERTY_CONFLICT, got: " + Describe(plan));
        });

        test("Unified wardrobe detects identical path collision between different framework controls", () =>
        {
            string yaml = BaseScene()
                .Replace("  - {fileID: 105}\n", "  - {fileID: 105}\n  - {fileID: 106}\n", StringComparison.Ordinal) +
                ModularAvatarToggle(label: "Jacket", targetMesh: 3) +
                VrcFuryToggle(path: "Jacket", targetMesh: 4);

            var plan = AvatarWardrobeComposer.Compose(yaml, Context());
            check(!plan.CanBuild, "Path collision must reject unified graph.");
            check(plan.Diagnostics.Any(d => d.Code == "WARDROBE_COMPOSER_PATH_COLLISION"),
                "Expected WARDROBE_COMPOSER_PATH_COLLISION, got: " + Describe(plan));
        });

        test("Unified wardrobe incorporates Base VRChat expression parameters into parameter catalog", () =>
        {
            string yaml = BaseScene()
                .Replace("  - {fileID: 105}\n", "  - {fileID: 105}\n  - {fileID: 106}\n", StringComparison.Ordinal) +
                ModularAvatarToggle() + VrcFuryToggle();

            var baseParams = new Dictionary<string, VrcParameter>(StringComparer.Ordinal)
            {
                ["VRCSDK_Mode"] = new() { Name = "VRCSDK_Mode", ValueType = 0, DefaultValue = 2, Saved = true, Synced = true },
                ["VRCSDK_Volume"] = new() { Name = "VRCSDK_Volume", ValueType = 1, DefaultValue = 0.75f, Saved = false, Synced = true }
            };

            var plan = AvatarWardrobeComposer.Compose(yaml, Context(), baseParameters: baseParams);
            check(plan.CanBuild, Describe(plan));
            var catalog = plan.SharedState!.Parameters;
            check(catalog.Definitions.ContainsKey(new AvatarParameterId("1", "vrc", "VRCSDK_Mode")), "Missing VRCSDK_Mode parameter");
            check(catalog.Definitions.ContainsKey(new AvatarParameterId("1", "vrc", "VRCSDK_Volume")), "Missing VRCSDK_Volume parameter");
            check(catalog.Definitions[new AvatarParameterId("1", "vrc", "VRCSDK_Mode")].Type == AvatarParameterType.Int, "Expected Int type");
            check(catalog.Definitions[new AvatarParameterId("1", "vrc", "VRCSDK_Volume")].Type == AvatarParameterType.Float, "Expected Float type");
        });

        test("Unified wardrobe composes 30 mixed-source toggles across MA and VRCFury", () =>
        {
            var childrenList = string.Concat(Enumerable.Range(10, 30).Select(i => "  - {fileID: " + (2000 + i) + "}\n"));
            var meshes = string.Concat(Enumerable.Range(10, 30).Select(i =>
                "--- !u!1 &" + (1000 + i) + "\nGameObject:\n  m_Name: Mesh" + i + "\n  m_IsActive: 1\n  m_Component:\n  - component: {fileID: " + (2000 + i) + "}\n" +
                "--- !u!4 &" + (2000 + i) + "\nTransform:\n  m_GameObject: {fileID: " + (1000 + i) + "}\n  m_Father: {fileID: 101}\n  m_Children: []\n"));

            var maControls = string.Concat(Enumerable.Range(10, 15).Select(i =>
                ModularAvatarToggle(objId: 3000 + i, transId: 4000 + i, installerId: 5000 + i, menuItemId: 6000 + i, toggleId: 7000 + i,
                    label: "MAToggle" + i, targetMesh: 1000 + i, targetName: "Mesh" + i)));

            var vrcfControls = string.Concat(Enumerable.Range(25, 15).Select(i =>
                VrcFuryToggle(objId: 8000 + i, transId: 9000 + i, vrcfId: 10000 + i, toggleIndex: i, path: "VRCToggle" + i, targetMesh: 1000 + i)));

            var extraTransforms = string.Concat(Enumerable.Range(10, 15).Select(i => "  - {fileID: " + (4000 + i) + "}\n")) +
                string.Concat(Enumerable.Range(25, 15).Select(i => "  - {fileID: " + (9000 + i) + "}\n"));

            string yaml =
                "--- !u!1 &1\nGameObject:\n  m_Name: Avatar\n  m_IsActive: 1\n  m_Component:\n  - component: {fileID: 101}\n" +
                "--- !u!4 &101\nTransform:\n  m_GameObject: {fileID: 1}\n  m_Father: {fileID: 0}\n  m_Children:\n" + childrenList + extraTransforms +
                meshes + maControls + vrcfControls;

            var plan = AvatarWardrobeComposer.Compose(yaml, Context());
            check(plan.CanBuild, Describe(plan));
            check(plan.Controls.Count == 30, "Expected 30 controls, got: " + plan.Controls.Count);
            check(plan.SharedState!.Baselines.Count == 30, "Expected 30 distinct property baselines, got: " + plan.SharedState.Baselines.Count);
            check(plan.SharedState.IndependentControls!.Count == 30, "Expected 30 independent controls.");
        });
    }
}
