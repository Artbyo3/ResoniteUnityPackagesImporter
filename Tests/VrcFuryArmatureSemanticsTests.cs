using System.Numerics;
using UnityPackageImporter.Models;

internal static class VrcFuryArmatureSemanticsTests
{
    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        test("VRCFury forced unit-world scale cannot bypass unsupported-scale validation", () =>
        {
            var plan = AvatarBuildPlanner.CreatePlan(Parse("version: 7\nalignScale: 0\nforceOneWorldScale: 1"));
            check(plan.HasErrors && plan.Operations.Single().Diagnostics.Any(item => item.Code == "VRCFURY_UNSUPPORTED_SCALE_OPTIONS" && item.Severity == AvatarOperationSeverity.Error),
                "Forced world scale bypassed scale safety when normal alignment was disabled.");
        });
        foreach (int mode in Enumerable.Range(0, 5))
        {
            test($"VRCFury legacy mode {mode} follows upstream enum values", () =>
            {
                var normalized = Normalize(("version", "6"), ("linkMode", mode.ToString()), ("keepBoneOffsets2", "1"));
                check(normalized.LinkMode == mode, "Serialized mode changed");
                if (mode == 4)
                    check(normalized.Diagnostics.Any(diagnostic => diagnostic.Code == "VRCFURY_AUTO_REQUIRES_SKIN_ANALYSIS" && diagnostic.IsError), "Auto must require skin-reference analysis");
                else
                    check(normalized.Recursive == (mode != 3) && !normalized.Diagnostics.Any(diagnostic => diagnostic.IsError), "Legacy recursion does not match upstream migration");
                check(!normalized.AlignPosition && !normalized.AlignRotation && !normalized.AlignScale, "Keep-offsets Yes must retain every world axis");
            });
        }
        test("VRCFury modern defaults are nonrecursive and unaligned", () =>
        {
            var link = Parse("version: 7").AllVrcFuryArmatureLinks.Single();
            check(link.SerializedVersion == 7 && link.LinkMode == 4 && !link.Recursive, "Modern mode or recursive defaults changed");
            check(!link.AlignPosition && !link.AlignRotation && !link.AlignScale && link.AutoScaleFactor, "Modern alignment defaults changed");
            check(link.LinkToTargets.Single().UseBone && link.LinkToTargets.Single().Bone == 0, "Upstream default target is Hips");
            check(!AvatarBuildPlanner.CreatePlan(Parse("version: 7")).HasErrors, "Modern Auto is obsolete and must not require legacy skin analysis");
        });
        test("VRCFury feature-version scoping also supports flow-mapped data", () =>
        {
            var manifest = Parse("version: 7\nrecursive: 0\nlinkTo: [{bone: 10}]");
            var link = manifest.AllVrcFuryArmatureLinks.Single();
            check(link.SerializedVersion == 7 && !link.Recursive && link.LinkToTargets.Single().Bone == 10, "Feature version leaked from references envelope");
            string flow = "--- !u!114 &200\nMonoBehaviour:\n  m_GameObject: {fileID: 100}\n  m_Script: {fileID: 11500000, guid: " + AvatarPackageIndex.VrcFuryFullScriptGuid + ", type: 3}\n  references:\n    version: 2\n    RefIds:\n    - rid: 300\n      type: {class: ArmatureLink}\n      data: {version: 7, linkTo: [{bone: 10}]}";
            var flowed = AvatarPackageIndex.ParseText(flow, "flow.prefab").AllVrcFuryArmatureLinks.Single();
            check(flowed.SerializedVersion == 7 && !flowed.Recursive && flowed.LinkToTargets.Single().Bone == 10, "Flow data was silently mistaken for v0");
        });
        test("VRCFury malformed target booleans cannot invent avatar-root attachments", () =>
        {
            var plan = AvatarBuildPlanner.CreatePlan(Parse("version: 7\nlinkTo: [{useBone: garbage}, {useBone: false, useObj: []}]"));
            check(plan.HasErrors && plan.Operations.Single().Diagnostics.Any(diagnostic => diagnostic.Code == "TARGET_UNRESOLVED"), "Malformed booleans became an explicit root target");
        });
        foreach (int keepOffsets in Enumerable.Range(0, 3))
        {
            test($"VRCFury keepBoneOffsets2 tri-state {keepOffsets}", () =>
            {
                var recursive = Normalize(("version", "6"), ("linkMode", "0"), ("keepBoneOffsets2", keepOffsets.ToString()));
                var root = Normalize(("version", "6"), ("linkMode", "3"), ("keepBoneOffsets2", keepOffsets.ToString()));
                check(recursive.AlignPosition == (keepOffsets != 1) && recursive.AlignRotation == recursive.AlignPosition && recursive.AlignScale == recursive.AlignPosition, "Recursive tri-state normalization changed");
                check(root.AlignPosition == (keepOffsets == 2) && root.AlignRotation == root.AlignPosition && root.AlignScale == root.AlignPosition, "ReparentRoot No must align, not preserve offsets");
            });
        }
        test("VRCFury missing feature version migrates v0 despite references version", () =>
        {
            var link = Parse("linkMode: 3\nrecursive: 0\nlinkTo: []\nboneOnAvatar: 10\nfallbackBones: [11, 12]").AllVrcFuryArmatureLinks.Single();
            check(link.SerializedVersion == 0 && link.LinkMode == 1 && link.Recursive, "Missing feature version must run v0 useBoneMerging migration");
            check(link.AlignPosition && link.AlignRotation && link.AlignScale && link.AutoScaleFactor, "v0 keepBoneOffsets false migration changed");
            check(link.LinkToTargets.Select(target => target.Bone).SequenceEqual(new[] { 10, 11, 12 }), "v0 must replace stale linkTo with ordered legacy targets");
            check(link.SemanticDiagnostics.Any(diagnostic => diagnostic.Code == "VRCFURY_UNSUPPORTED_SCALE_OPTIONS" && diagnostic.IsError), "Unimplemented migrated auto-scaling must be explicit");
            var merging = Parse("useBoneMerging: 1\nkeepBoneOffsets: 1").AllVrcFuryArmatureLinks.Single();
            check(merging.LinkMode == 0 && merging.Recursive && !merging.AlignPosition && !merging.AutoScaleFactor, "v0 bone-merging and boolean offset migrations changed");
        });
        test("VRCFury pre-v6 legacy path replaces bone and fallback fields", () =>
        {
            var link = Parse("version: 5\nlinkMode: 3\nkeepBoneOffsets2: 1\nboneOnAvatar: 10\nfallbackBones: [11]\nbonePathOnAvatar: Armature/Socket\nlinkTo: [{bone: 0}]").AllVrcFuryArmatureLinks.Single();
            check(link.LinkToTargets.Count == 1 && !link.LinkToTargets[0].UseBone && link.LinkToTargets[0].Offset == "Armature/Socket", "Legacy path must resolve from avatar root, not from boneOnAvatar");
        });
        foreach (string fields in new[] { "version: 8", "version: -1", "version: invalid", "version: 6\nlinkMode: 99", "version: 6\nkeepBoneOffsets2: 9", "version: 7\nrecursive: invalid" })
            test("VRCFury rejects unknown serialized semantics: " + fields.Replace('\n', ' '), () =>
                check(AvatarBuildPlanner.CreatePlan(Parse(fields)).HasErrors, "Unknown semantics must never mutate the world"));
        foreach (string options in new[] { "autoScaleFactor: 0\nskinRewriteScalingFactor: 2", "autoScaleFactor: 1\nscalingFactorPowersOf10Only: 1", "autoScaleFactor: 1\nscalingFactorPowersOf10Only: 0" })
            test("VRCFury diagnoses active unsupported scaling: " + options.Replace('\n', ' '), () =>
            {
                var plan = AvatarBuildPlanner.CreatePlan(Parse("version: 7\nrecursive: 1\nalignScale: 1\n" + options));
                check(plan.HasErrors && plan.Operations.Single().Diagnostics.Any(diagnostic => diagnostic.Code == "VRCFURY_UNSUPPORTED_SCALE_OPTIONS"), "Unsupported scale semantics were silently ignored");
            });
        test("VRCFury inactive scale options remain diagnostic without blocking attachment", () =>
        {
            var plan = AvatarBuildPlanner.CreatePlan(Parse("version: 7\nskinRewriteScalingFactor: 2\nscalingFactorPowersOf10Only: 0"));
            check(!plan.HasErrors && plan.Operations.Single().Diagnostics.Any(diagnostic => diagnostic.Code == "VRCFURY_UNSUPPORTED_SCALE_OPTIONS"), "Inactive options must remain visible");
        });
        foreach (bool merge in new[] { false, true })
        {
            test($"VRCFury {(merge ? "merge" : "attachment")} ordered targets skip unresolved and invalid first candidates", () =>
            {
                var plan = AvatarBuildPlanner.CreatePlan(Parse("version: 7\nrecursive: " + (merge ? "1" : "0") + "\nlinkTo: [{useBone: 1, bone: 999}, {useBone: 0, useObj: 1, obj: {fileID: 404}}, {useBone: 1, bone: 0, offset: Missing}, {useBone: 1, bone: 10}]"));
                var operation = plan.Operations.Single();
                check(!plan.HasErrors && operation.GetTargetCandidates().Count == 4, "Invalid primary candidate blocked a declared fallback");
                var lookups = new List<int>();
                string? target = AvatarTargetResolution.Resolve(operation, "root", _ => null,
                    bone => { lookups.Add(bone); return bone == 0 ? "Hips" : "Head"; }, (_, _) => null);
                check(target == "Head" && operation.IsValid && lookups.SequenceEqual(new[] { 0, 10 }), "Candidates were reordered, invalid bone was queried, or missing offset prevented fallback");
            });
            test($"VRCFury {(merge ? "merge" : "attachment")} scope-invalid candidates do not block later fallback", () =>
            {
                var operation = AvatarBuildPlanner.CreatePlan(Parse("version: 7\nrecursive: " + (merge ? "1" : "0") + "\nlinkTo: [{useBone: 0, useObj: 1, obj: {fileID: 999}}, {useBone: 1, bone: 0}]" )).Operations.Single();
                string? result = AvatarTargetResolution.Resolve(operation, "root", _ => "outside", _ => "Hips", (root, _) => root, target => target != "outside");
                check(result == "Hips" && operation.IsValid, "Resolved target scope was not checked before accepting a candidate");
            });
        }
        test("VRCFury ordered candidate dependencies are deterministic and skip self candidates", () =>
        {
            var manifest = Parse("version: 7\nlinkTo: [{useBone: 0, useObj: 1, obj: {fileID: 100}}, {useBone: 0, useObj: 1, obj: {fileID: 200}}, {useBone: 0, useObj: 1, obj: {fileID: 300}}]");
            var plan = AvatarBuildPlanner.CreatePlan(manifest);
            plan.Operations.Add(new BoneAttachmentOperation { OperationId = "second", SourceObjectFileId = 200, TargetHumanoidBone = 0 });
            plan.Operations.Add(new BoneAttachmentOperation { OperationId = "third", SourceObjectFileId = 300, TargetHumanoidBone = 0 });
            AvatarBuildPlanner.ValidatePlan(plan);
            AvatarBuildPlanner.ValidatePlan(plan);
            check(!plan.HasErrors && plan.Operations[0].DependsOnOperationIds.SequenceEqual(new[] { "second", "third" }), "Candidate dependencies were omitted, duplicated, or included self");
        });
        test("VRCFury differing fallback order is a target conflict", () =>
        {
            var first = Parse("version: 7\nlinkTo: [{bone: 0}, {bone: 10}]").VrcFuryComponents.Single();
            var second = Parse("version: 7\nlinkTo: [{bone: 0}, {bone: 11}]").VrcFuryComponents.Single();
            var plan = AvatarBuildPlanner.CreatePlan(new AvatarPackageManifest { VrcFuryComponents = new[] { first, second } });
            check(plan.HasErrors && plan.Operations[1].Diagnostics.Any(diagnostic => diagnostic.Code == "CONFLICTING_TARGETS"), "Only primary target was compared");
        });
        test("VRCFury mutually exclusive fallback dependency cycles are explicitly conservative", () =>
        {
            var first = new BoneAttachmentOperation { OperationId = "first", SourceObjectFileId = 100,
                TargetCandidates = new (int, long?, string, string)[] { (-1, 200, "", ""), (0, null, "", "") } };
            var second = new BoneAttachmentOperation { OperationId = "second", SourceObjectFileId = 200,
                TargetCandidates = new (int, long?, string, string)[] { (-1, 100, "", ""), (10, null, "", "") } };
            var plan = new AvatarBuildPlan { Operations = new() { first, second } };
            AvatarBuildPlanner.ValidatePlan(plan);
            check(plan.HasErrors && plan.Diagnostics.Any(diagnostic => diagnostic.Code == "CYCLE_DETECTED"), "Conservative cycle must not be silently executed");
            check(first.Diagnostics.Any(diagnostic => diagnostic.Code == "CONDITIONAL_TARGET_DEPENDENCIES") && second.Diagnostics.Any(diagnostic => diagnostic.Code == "CONDITIONAL_TARGET_DEPENDENCIES"), "Conditional-dependency limitation must be visible");
            check(AvatarTargetResolution.Resolve(first, "root", _ => (string?)null, _ => "Hips", (root, _) => root) == "Hips", "Fixture must retain a viable fallback despite its conservative plan cycle");
        });
        foreach (int mask in Enumerable.Range(0, 8))
            test($"VRCFury per-axis world transform preservation mask {mask}", () =>
            {
                var original = (Position: new Vector3(7, -3, 5), Rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.8f), Scale: new Vector3(2, 3, 4));
                var target = (Position: new Vector3(-9, 4, 2), Rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -0.4f), Scale: new Vector3(0.5f, 2, 1));
                var desired = VrcFuryArmatureSemantics.SelectWorldTransform(original, target, (mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0);
                check(desired.Position == ((mask & 1) != 0 ? target.Position : original.Position) && desired.Rotation == ((mask & 2) != 0 ? target.Rotation : original.Rotation) && desired.Scale == ((mask & 4) != 0 ? target.Scale : original.Scale), "Unaligned world axis was lost");
                var parent = Matrix4x4.CreateScale(3) * Matrix4x4.CreateFromQuaternion(target.Rotation) * Matrix4x4.CreateTranslation(target.Position);
                check(Matrix4x4.Invert(parent, out var inverse), "Fixture parent must be invertible");
                var world = Matrix4x4.CreateScale(desired.Scale) * Matrix4x4.CreateFromQuaternion(desired.Rotation) * Matrix4x4.CreateTranslation(desired.Position);
                var local = world * inverse;
                foreach (var point in new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
                    check(Vector3.Distance(Vector3.Transform(point, local * parent), Vector3.Transform(point, world)) < 0.0001f, "Reparenting changed a world-space basis point");
            });
    }

    private static VrcFuryArmatureSemantics.Normalized Normalize(params (string Key, string Value)[] fields) =>
        VrcFuryArmatureSemantics.Normalize(fields.ToDictionary(field => field.Key, field => field.Value));

    private static AvatarPackageManifest Parse(string fields) => AvatarPackageIndex.ParseText(
        "--- !u!1 &100\nGameObject:\n  m_Name: Costume\n--- !u!114 &200\nMonoBehaviour:\n  m_GameObject: {fileID: 100}\n  m_Script: {fileID: 11500000, guid: " + AvatarPackageIndex.VrcFuryFullScriptGuid + ", type: 3}\n  references:\n    version: 2\n    RefIds:\n    - rid: 300\n      type: {class: ArmatureLink, ns: VF.Model.Feature, asm: VRCFury}\n      data:\n" +
        string.Join("\n", fields.Split('\n').Select(line => "        " + line)), "semantics.prefab");
}
