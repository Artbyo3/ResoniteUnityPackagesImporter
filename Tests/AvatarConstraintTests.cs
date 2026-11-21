#nullable enable
using System;
using System.IO;
using System.Linq;
using UnityPackageImporter.Models;

internal static class AvatarConstraintTests
{
    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        string Describe(AvatarConstraintPlan plan) => string.Join("; ", plan.Diagnostics.Select(item => item.Code + ": " + item.Message));

        string BaseScene() =>
            "--- !u!1 &1\nGameObject:\n  m_Name: Avatar\n  m_IsActive: 1\n  m_Component:\n  - component: {fileID: 101}\n" +
            "--- !u!4 &101\nTransform:\n  m_GameObject: {fileID: 1}\n  m_Father: {fileID: 0}\n  m_Children:\n  - {fileID: 102}\n  - {fileID: 103}\n  - {fileID: 104}\n" +
            "--- !u!1 &2\nGameObject:\n  m_Name: TargetBone\n  m_IsActive: 1\n  m_Component:\n  - component: {fileID: 102}\n" +
            "--- !u!4 &102\nTransform:\n  m_GameObject: {fileID: 2}\n  m_Father: {fileID: 101}\n  m_Children: []\n" +
            "--- !u!1 &3\nGameObject:\n  m_Name: SourceBone\n  m_IsActive: 1\n  m_Component:\n  - component: {fileID: 103}\n" +
            "--- !u!4 &103\nTransform:\n  m_GameObject: {fileID: 3}\n  m_Father: {fileID: 101}\n  m_Children: []\n" +
            "--- !u!1 &4\nGameObject:\n  m_Name: ExtraBone\n  m_IsActive: 1\n  m_Component:\n  - component: {fileID: 104}\n" +
            "--- !u!4 &104\nTransform:\n  m_GameObject: {fileID: 4}\n  m_Father: {fileID: 101}\n  m_Children: []\n";

        string UnityRotationConstraint(long compId = 500, long goId = 2, long srcTransId = 103, float weight = 0.85f) =>
            "--- !u!1083451514 &" + compId + "\nRotationConstraint:\n" +
            "  m_GameObject: {fileID: " + goId + "}\n" +
            "  m_IsContraintActive: 1\n" +
            "  m_Weight: " + weight.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" +
            "  m_Sources:\n" +
            "  - sourceTransform: {fileID: " + srcTransId + "}\n" +
            "    weight: 1\n" +
            "  m_AffectRotationX: 1\n  m_AffectRotationY: 1\n  m_AffectRotationZ: 1\n";

        string UnityPositionConstraint(long compId = 501, long goId = 2, long srcTransId = 103) =>
            "--- !u!2083052904 &" + compId + "\nPositionConstraint:\n" +
            "  m_GameObject: {fileID: " + goId + "}\n" +
            "  m_IsContraintActive: 1\n" +
            "  m_Weight: 1\n" +
            "  m_Sources:\n" +
            "  - sourceTransform: {fileID: " + srcTransId + "}\n" +
            "    weight: 1\n" +
            "  m_AffectTranslationX: 1\n  m_AffectTranslationY: 0\n  m_AffectTranslationZ: 1\n";

        string UnityParentConstraint(long compId = 502, long goId = 4, long srcTransId = 103) =>
            "--- !u!1773428102 &" + compId + "\nParentConstraint:\n" +
            "  m_GameObject: {fileID: " + goId + "}\n" +
            "  m_IsContraintActive: 1\n" +
            "  m_Weight: 1\n" +
            "  m_Sources:\n" +
            "  - sourceTransform: {fileID: " + srcTransId + "}\n" +
            "    weight: 1\n";

        string UnityLookAtConstraint(long compId = 503, long goId = 2, long srcTransId = 103) =>
            "--- !u!1953259897 &" + compId + "\nLookAtConstraint:\n" +
            "  m_GameObject: {fileID: " + goId + "}\n" +
            "  m_IsContraintActive: 1\n" +
            "  m_Weight: 1\n" +
            "  m_Sources:\n" +
            "  - sourceTransform: {fileID: " + srcTransId + "}\n" +
            "    weight: 1\n";

        string UnityAimConstraint(long compId = 504, long goId = 2, long srcTransId = 103) =>
            "--- !u!1183024368 &" + compId + "\nAimConstraint:\n" +
            "  m_GameObject: {fileID: " + goId + "}\n" +
            "  m_IsContraintActive: 1\n" +
            "  m_Weight: 1\n" +
            "  m_Sources:\n" +
            "  - sourceTransform: {fileID: " + srcTransId + "}\n" +
            "    weight: 1\n";

        string UnityScaleConstraint(long compId = 505, long goId = 2, long srcTransId = 103) =>
            "--- !u!894565638 &" + compId + "\nScaleConstraint:\n" +
            "  m_GameObject: {fileID: " + goId + "}\n" +
            "  m_IsContraintActive: 1\n" +
            "  m_Weight: 1\n" +
            "  m_Sources:\n" +
            "  - sourceTransform: {fileID: " + srcTransId + "}\n" +
            "    weight: 1\n";

        string VrcConstraint(long compId = 600, long goId = 2, string className = "VRCParentConstraint", long srcTransId = 103) =>
            "--- !u!114 &" + compId + "\nMonoBehaviour:\n" +
            "  m_GameObject: {fileID: " + goId + "}\n" +
            "  m_Script: {fileID: 11500000, guid: " + AvatarConstraintPlanner.VrcConstraintScriptGuid + ", type: 3}\n" +
            "  m_Name: \n  m_EditorClassIdentifier: " + className + "\n" +
            "  IsActive: 1\n" +
            "  Weight: 1\n" +
            "  Sources:\n" +
            "  - SourceTransform: {fileID: " + srcTransId + "}\n" +
            "    Weight: 1\n";

        test("Unity RotationConstraint compiles with exact weights and sources", () =>
        {
            string yaml = BaseScene() + UnityRotationConstraint();
            var plan = AvatarConstraintPlanner.Compile(yaml, 1);
            check(plan.CanBuild, Describe(plan));
            check(plan.Constraints.Count == 1, "Expected 1 constraint");
            var c = plan.Constraints[0];
            check(c.Type == AvatarConstraintType.Rotation, "Expected Rotation constraint");
            check(c.TargetTransformId == 102, "Target must be 102");
            check(c.Sources.Count == 1 && c.Sources[0].SourceTransformId == 103, "Source must be 103");
            check(Math.Abs(c.Weight - 0.85f) < 0.01f, "Weight must be 0.85");
        });

        test("Unity PositionConstraint and ParentConstraint compile with correct types", () =>
        {
            string yaml = BaseScene() + UnityPositionConstraint() + UnityParentConstraint(compId: 503, goId: 4, srcTransId: 103);
            var plan = AvatarConstraintPlanner.Compile(yaml, 1);
            check(plan.CanBuild, Describe(plan));
            check(plan.Constraints.Count == 2, "Expected 2 constraints");
            check(plan.Constraints[0].Type == AvatarConstraintType.Position, "First must be Position");
            check(plan.Constraints[1].Type == AvatarConstraintType.Parent, "Second must be Parent");
        });

        test("Unity LookAt, Aim, and Scale constraints compile with correct types", () =>
        {
            string yaml = BaseScene() +
                UnityLookAtConstraint(compId: 510, goId: 2, srcTransId: 103) +
                UnityAimConstraint(compId: 511, goId: 4, srcTransId: 103) +
                UnityScaleConstraint(compId: 512, goId: 2, srcTransId: 104);
            var plan = AvatarConstraintPlanner.Compile(yaml, 1);
            check(plan.CanBuild, Describe(plan));
            check(plan.Constraints.Count == 3, "Expected 3 constraints");
            check(plan.Constraints[0].Type == AvatarConstraintType.LookAt, "First must be LookAt");
            check(plan.Constraints[1].Type == AvatarConstraintType.Aim, "Second must be Aim");
            check(plan.Constraints[2].Type == AvatarConstraintType.Scale, "Third must be Scale");
        });

        test("VRC SDK constraints compile from MonoBehaviours", () =>
        {
            string yaml = BaseScene() +
                VrcConstraint(compId: 601, goId: 2, className: "VRCParentConstraint", srcTransId: 103) +
                VrcConstraint(compId: 602, goId: 4, className: "VRCRotationConstraint", srcTransId: 103) +
                VrcConstraint(compId: 603, goId: 2, className: "VRCLookAtConstraint", srcTransId: 103) +
                VrcConstraint(compId: 604, goId: 4, className: "VRCAimConstraint", srcTransId: 103) +
                VrcConstraint(compId: 605, goId: 2, className: "VRCScaleConstraint", srcTransId: 103) +
                VrcConstraint(compId: 606, goId: 4, className: "VRCPositionConstraint", srcTransId: 103);

            var plan = AvatarConstraintPlanner.Compile(yaml, 1);
            check(plan.CanBuild, Describe(plan));
            check(plan.Constraints.Count == 6, "Expected 6 VRC constraints");
            check(plan.Constraints[0].Type == AvatarConstraintType.Parent, "Expected VRCParentConstraint");
            check(plan.Constraints[1].Type == AvatarConstraintType.Rotation, "Expected VRCRotationConstraint");
            check(plan.Constraints[2].Type == AvatarConstraintType.LookAt, "Expected VRCLookAtConstraint");
            check(plan.Constraints[3].Type == AvatarConstraintType.Aim, "Expected VRCAimConstraint");
            check(plan.Constraints[4].Type == AvatarConstraintType.Scale, "Expected VRCScaleConstraint");
            check(plan.Constraints[5].Type == AvatarConstraintType.Position, "Expected VRCPositionConstraint");
        });

        test("Constraint self-reference fails closed with error diagnostic", () =>
        {

            string yaml = BaseScene() + UnityRotationConstraint(srcTransId: 102);
            var plan = AvatarConstraintPlanner.Compile(yaml, 1);
            check(!plan.CanBuild, "Constraint referencing itself must fail plan");
            check(plan.Diagnostics.Any(d => d.Code == "CONSTRAINT_SELF_REFERENCE"), "Expected CONSTRAINT_SELF_REFERENCE diagnostic");
        });

        test("Cyclic constraint dependency is detected and fails closed", () =>
        {

            string yaml = BaseScene() +
                UnityRotationConstraint(compId: 501, goId: 2, srcTransId: 103) +
                UnityRotationConstraint(compId: 502, goId: 3, srcTransId: 102);

            var plan = AvatarConstraintPlanner.Compile(yaml, 1);
            check(!plan.CanBuild, "Cyclic constraints must fail plan");
            check(plan.Diagnostics.Any(d => d.Code == "CONSTRAINT_CYCLE_DETECTED"), "Expected CONSTRAINT_CYCLE_DETECTED diagnostic");
        });

        test("Constraint outside avatar root hierarchy is ignored", () =>
        {
            string outsideScene = BaseScene() +
                "--- !u!1 &50\nGameObject:\n  m_Name: OutsideAvatar\n  m_IsActive: 1\n  m_Component:\n  - component: {fileID: 150}\n" +
                "--- !u!4 &150\nTransform:\n  m_GameObject: {fileID: 50}\n  m_Father: {fileID: 0}\n  m_Children: []\n" +
                UnityRotationConstraint(compId: 800, goId: 50, srcTransId: 103);

            var plan = AvatarConstraintPlanner.Compile(outsideScene, 1);
            check(plan.Constraints.Count == 0, "Constraints outside avatar hierarchy must be ignored");
        });
    }
}
