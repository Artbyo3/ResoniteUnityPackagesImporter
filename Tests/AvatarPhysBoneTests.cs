using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityPackageImporter.Models;

internal static class AvatarPhysBoneTests
{
    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        string Describe(AvatarPhysBonePlan plan) => string.Join("; ", plan.Diagnostics.Select(item => item.Code + ": " + item.Message));

        string BaseScene() =>
            "--- !u!1 &1\nGameObject:\n  m_Name: Avatar\n  m_IsActive: 1\n  m_Component:\n  - component: {fileID: 101}\n" +
            "--- !u!4 &101\nTransform:\n  m_GameObject: {fileID: 1}\n  m_Father: {fileID: 0}\n  m_Children:\n  - {fileID: 102}\n  - {fileID: 103}\n" +
            "--- !u!1 &2\nGameObject:\n  m_Name: HairRoot\n  m_IsActive: 1\n  m_Component:\n  - component: {fileID: 102}\n" +
            "--- !u!4 &102\nTransform:\n  m_GameObject: {fileID: 2}\n  m_Father: {fileID: 101}\n  m_Children:\n  - {fileID: 104}\n" +
            "--- !u!1 &3\nGameObject:\n  m_Name: HeadBone\n  m_IsActive: 1\n  m_Component:\n  - component: {fileID: 103}\n" +
            "--- !u!4 &103\nTransform:\n  m_GameObject: {fileID: 3}\n  m_Father: {fileID: 101}\n  m_Children: []\n" +
            "--- !u!1 &4\nGameObject:\n  m_Name: HairTip\n  m_IsActive: 1\n  m_Component:\n  - component: {fileID: 104}\n" +
            "--- !u!4 &104\nTransform:\n  m_GameObject: {fileID: 4}\n  m_Father: {fileID: 102}\n  m_Children: []\n";

        string PhysBone(long compId = 500, long goId = 2, long rootTransId = 102,
            float pull = 0.35f, float spring = 0.45f, float stiffness = 0.65f, float gravity = 0.5f,
            float radius = 0.04f, int allowGrabbing = 1, int allowCollision = 1,
            string extraIgnores = "", string extraColliders = "") =>
            "--- !u!114 &" + compId + "\nMonoBehaviour:\n  m_GameObject: {fileID: " + goId + "}\n" +
            "  m_Script: {fileID: 1661641543, guid: 2a2c05204084d904aa4945ccff20d8e5, type: 3}\n" +
            "  m_Name: \n  m_EditorClassIdentifier: \n" +
            "  rootTransform: {fileID: " + rootTransId + "}\n" +
            "  ignoreTransforms: [" + extraIgnores + "]\n" +
            "  endpointPosition: {x: 0, y: -0.1, z: 0}\n" +
            "  multiChildType: 0\n" +
            "  pull: " + pull.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" +
            "  spring: " + spring.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" +
            "  stiffness: " + stiffness.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" +
            "  gravity: " + gravity.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" +
            "  gravityFalloff: 0\n" +
            "  immobile: 0.1\n" +
            "  radius: " + radius.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" +
            "  allowCollision: " + allowCollision + "\n" +
            "  colliders: [" + extraColliders + "]\n" +
            "  allowGrabbing: " + allowGrabbing + "\n" +
            "  allowPosing: 1\n";

        string PhysBoneCollider(long compId = 600, long goId = 3, long rootTransId = 103,
            int shapeType = 0, float radius = 0.12f, float height = 0.25f, float posX = 0.01f, float posY = 0.05f, float posZ = -0.02f) =>
            "--- !u!114 &" + compId + "\nMonoBehaviour:\n  m_GameObject: {fileID: " + goId + "}\n" +
            "  m_Script: {fileID: -1631200402, guid: 2a2c05204084d904aa4945ccff20d8e5, type: 3}\n" +
            "  m_Name: \n  m_EditorClassIdentifier: \n" +
            "  rootTransform: {fileID: " + rootTransId + "}\n" +
            "  shapeType: " + shapeType + "\n" +
            "  radius: " + radius.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" +
            "  height: " + height.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" +
            "  position: {x: " + posX.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", y: " + posY.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", z: " + posZ.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}\n" +
            "  rotation: {x: 0, y: 0, z: 0, w: 1}\n" +
            "  insideBounds: 0\n";

        string ModularAvatarBlocker(long compId = 700, long goId = 4) =>
            "--- !u!114 &" + compId + "\nMonoBehaviour:\n  m_GameObject: {fileID: " + goId + "}\n" +
            "  m_Script: {fileID: 11500000, guid: a5bf908a199a4648845ebe2fd3b5a4bd, type: 3}\n" +
            "  m_Name: \n  m_EditorClassIdentifier: \n";

        test("VRCPhysBone compiler parses exact forces, endpoints, and collision settings", () =>
        {
            string yaml = BaseScene() + PhysBone();
            var plan = AvatarPhysBonePlanner.Compile(yaml, 1);
            check(plan.CanBuild, Describe(plan));
            check(plan.Bones.Count == 1, "Expected 1 bone chain, got: " + plan.Bones.Count);

            var bone = plan.Bones[0];
            check(bone.RootTransformId == 102, "Expected rootTransform 102, got: " + bone.RootTransformId);
            check(Math.Abs(bone.Pull - 0.35f) < 0.001f, "Expected pull 0.35, got: " + bone.Pull);
            check(Math.Abs(bone.Spring - 0.45f) < 0.001f, "Expected spring 0.45, got: " + bone.Spring);
            check(Math.Abs(bone.Stiffness - 0.65f) < 0.001f, "Expected stiffness 0.65, got: " + bone.Stiffness);
            check(Math.Abs(bone.Gravity - 0.5f) < 0.001f, "Expected gravity 0.5, got: " + bone.Gravity);
            check(Math.Abs(bone.Radius - 0.04f) < 0.001f, "Expected radius 0.04, got: " + bone.Radius);
            check(bone.AllowGrabbing, "Expected AllowGrabbing true");
            check(bone.AllowCollision, "Expected AllowCollision true");
        });

        test("VRCPhysBoneCollider compiler parses sphere and capsule dimensions with local offset", () =>
        {
            string yaml = BaseScene() + PhysBoneCollider();
            var plan = AvatarPhysBonePlanner.Compile(yaml, 1);
            check(plan.CanBuild, Describe(plan));
            check(plan.Colliders.Count == 1, "Expected 1 collider, got: " + plan.Colliders.Count);

            var col = plan.Colliders[0];
            check(col.RootTransformId == 103, "Expected rootTransform 103, got: " + col.RootTransformId);
            check(Math.Abs(col.Radius - 0.12f) < 0.001f, "Expected radius 0.12, got: " + col.Radius);
            check(Math.Abs(col.Height - 0.25f) < 0.001f, "Expected height 0.25, got: " + col.Height);
            check(Math.Abs(col.Position.X - 0.01f) < 0.001f, "Expected pos X 0.01, got: " + col.Position.X);
            check(Math.Abs(col.Position.Y - 0.05f) < 0.001f, "Expected pos Y 0.05, got: " + col.Position.Y);
        });

        test("VRCPhysBone resolves collider references to VRCPhysBoneCollider", () =>
        {
            string yaml = BaseScene() + PhysBone(extraColliders: "{fileID: 600}") + PhysBoneCollider(compId: 600);
            var plan = AvatarPhysBonePlanner.Compile(yaml, 1);
            check(plan.CanBuild, Describe(plan));
            check(plan.Bones.Count == 1 && plan.Colliders.Count == 1, "Expected 1 bone and 1 collider");
            check(plan.Bones[0].ColliderIds.Contains(600), "PhysBone must retain reference to collider 600");
        });

        test("ModularAvatarPBBlocker automatically excludes blocked branches from parent PhysBone", () =>
        {
            // HairTip (104) has ModularAvatarBlocker, which descends from HairRoot (102)
            string yaml = BaseScene() + PhysBone(rootTransId: 102) + ModularAvatarBlocker(compId: 700, goId: 4);
            var plan = AvatarPhysBonePlanner.Compile(yaml, 1);
            check(plan.CanBuild, Describe(plan));
            check(plan.Bones.Count == 1, "Expected 1 bone chain");
            check(plan.Bones[0].IgnoreTransformIds.Contains(104),
                "ModularAvatarPBBlocker on GameObject 4 must add Transform 104 to IgnoreTransformIds");
        });

        test("PhysBone compiler scopes chains to avatar root, rejecting foreign bones", () =>
        {
            string outsideScene = BaseScene() +
                "--- !u!1 &50\nGameObject:\n  m_Name: OutsideAvatar\n  m_IsActive: 1\n  m_Component:\n  - component: {fileID: 150}\n" +
                "--- !u!4 &150\nTransform:\n  m_GameObject: {fileID: 50}\n  m_Father: {fileID: 0}\n  m_Children: []\n" +
                PhysBone(compId: 800, goId: 50, rootTransId: 150);

            var plan = AvatarPhysBonePlanner.Compile(outsideScene, 1);
            check(plan.Bones.Count == 0, "Bones outside avatar hierarchy must be ignored");
        });

        test("Multi-chain PhysBone simulation compiles complex avatar with 4 chains and 3 colliders", () =>
        {
            string multiChainScene = BaseScene() +
                // Additional bones for Skirt, Ear, Tail
                "--- !u!1 &5\nGameObject:\n  m_Name: Skirt\n  m_IsActive: 1\n  m_Component:\n  - component: {fileID: 105}\n" +
                "--- !u!4 &105\nTransform:\n  m_GameObject: {fileID: 5}\n  m_Father: {fileID: 101}\n  m_Children: []\n" +
                "--- !u!1 &6\nGameObject:\n  m_Name: Ear\n  m_IsActive: 1\n  m_Component:\n  - component: {fileID: 106}\n" +
                "--- !u!4 &106\nTransform:\n  m_GameObject: {fileID: 6}\n  m_Father: {fileID: 101}\n  m_Children: []\n" +
                "--- !u!1 &7\nGameObject:\n  m_Name: Tail\n  m_IsActive: 1\n  m_Component:\n  - component: {fileID: 107}\n" +
                "--- !u!4 &107\nTransform:\n  m_GameObject: {fileID: 7}\n  m_Father: {fileID: 101}\n  m_Children: []\n" +
                PhysBone(compId: 501, goId: 2, rootTransId: 102) +
                PhysBone(compId: 502, goId: 5, rootTransId: 105) +
                PhysBone(compId: 503, goId: 6, rootTransId: 106) +
                PhysBone(compId: 504, goId: 7, rootTransId: 107) +
                PhysBoneCollider(compId: 601, goId: 3, rootTransId: 103) +
                PhysBoneCollider(compId: 602, goId: 5, rootTransId: 105) +
                PhysBoneCollider(compId: 603, goId: 6, rootTransId: 106);

            var plan = AvatarPhysBonePlanner.Compile(multiChainScene, 1);
            check(plan.CanBuild, Describe(plan));
            check(plan.Bones.Count == 4, "Expected 4 bone chains, got: " + plan.Bones.Count);
            check(plan.Colliders.Count == 3, "Expected 3 colliders, got: " + plan.Colliders.Count);
        });
    }
}
