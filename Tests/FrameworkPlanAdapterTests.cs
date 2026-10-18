using UnityPackageImporter.Conversion;
using UnityPackageImporter.Frameworks.ModularAvatar;
using UnityPackageImporter.Frameworks.VrcFury;
using UnityPackageImporter.Models;

internal static class FrameworkPlanAdapterTests
{
    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        test("Modular Avatar adapter contributes through an append-only sink without requiring a plan", () =>
        {
            var sink = new RecordingSink();
            ModularAvatarPlanAdapter.AppendOperations(new[]
            {
                new ModularAvatarComponentDefinition { Kind = ModularAvatarComponentKind.MergeArmature, GameObjectFileId = -10, ComponentFileId = 20, Prefix = "left-", Suffix = "-cloth" },
                new ModularAvatarComponentDefinition { Kind = ModularAvatarComponentKind.BoneProxy, GameObjectFileId = 30, ComponentFileId = 40, BoneReference = 10, BoneProxySubPath = "Hat", BoneProxyMatchScale = true }
            }, sink);
            check(sink.Operations.Count == 2, "Adapter lost a contribution.");
            var merge = (ArmatureMergeOperation)sink.Operations[0];
            check(merge.SourceArmatureRootFileId == -10 && merge.Prefix == "left-" && merge.Suffix == "-cloth", "Source reference or naming semantics changed.");
            check(merge.TargetReferencePath == "$$$AVATAR_ROOT$$$" && merge.Diagnostics.Single().Code == "DEFAULT_AVATAR_ROOT", "Implicit target diagnostics changed.");
            var attach = (BoneAttachmentOperation)sink.Operations[1];
            check(attach.TargetHumanoidBone == 10 && attach.TargetOffsetPath == "Hat" && attach.AlignScale && !attach.KeepOffsets, "Bone Proxy settings changed.");
            check(sink.Operations.All(o => o.ExecutionState == AvatarOperationExecutionState.Pending), "Adapter executed an operation.");
        });
        test("VRCFury adapter contributes unsupported diagnostics and preserves link settings", () =>
        {
            var sink = new RecordingSink();
            VrcFuryPlanAdapter.AppendOperations(new[]
            {
                new VrcFuryComponentDefinition
                {
                    GameObjectName = "Fixture", UnsupportedFeatures = new[] { "ExampleUnsupported" },
                    ArmatureLinks = new[]
                    {
                        new VrcFuryArmatureLinkDefinition
                        {
                            GameObjectFileId = 100, ComponentFileId = 200, PropBoneFileId = -300,
                            Recursive = false, KeepOffsets = true, AlignPosition = false, AlignRotation = true, AlignScale = true, AutoScaleFactor = false,
                            LinkToTargets = new[] { new VrcFuryLinkToTarget { UseObj = true, ObjFileId = 400, Offset = "Socket" } }
                        }
                    }
                }
            }, sink);
            var op = (BoneAttachmentOperation)sink.Operations.Single();
            check(op.SourceObjectFileId == -300 && op.TargetObjectFileId == 400 && op.TargetOffsetPath == "Socket", "Explicit link references changed.");
            check(op.KeepOffsets && !op.AlignPosition && op.AlignRotation && op.AlignScale && !op.AutoScaleFactor, "Link flags changed.");
            check(sink.Diagnostics.Single().Code == "UNHANDLED_VRCFURY_FEATURE" && sink.Diagnostics[0].SourceContext == "Fixture", "Unsupported feature diagnostics changed.");
        });
        test("Framework composition retains contribution order and resolves cross-framework dependencies", () =>
        {
            var plan = AvatarBuildPlanner.CreatePlan(Mixed(100, 300));
            check(plan.Operations[0].Source.Framework == "ModularAvatar" && plan.Operations[1].Source.Framework == "VRCFury", "Contribution order changed.");
            check(plan.Operations[0].DependsOnOperationIds.SequenceEqual(new[] { plan.Operations[1].OperationId }), "Cross-framework dependency was not resolved centrally.");
            check(plan.GetOrderedOperations()[0] == plan.Operations[1] && !plan.HasErrors, "Shared execution ordering changed.");
        });
        test("Shared validation detects conflicting sources across framework adapters", () =>
        {
            var plan = AvatarBuildPlanner.CreatePlan(Mixed(100, 100));
            check(plan.HasErrors && plan.Operations[1].Diagnostics.Any(d => d.Code == "CONFLICTING_TARGETS"), "Separate adapters concealed a cross-framework conflict.");
        });
        test("Repeated framework composition preserves provenance and diagnostics without mutating input", () =>
        {
            var input = Mixed(100, 300);
            var first = AvatarBuildPlanner.CreatePlan(input);
            var second = AvatarBuildPlanner.CreatePlan(input);
            check(first.Operations.Select(o => o.OperationId).SequenceEqual(second.Operations.Select(o => o.OperationId)), "Unstable operation identity.");
            check(first.AvatarName == "fixture.prefab" && second.AvatarName == first.AvatarName, "Source-derived name changed.");
            check(input.ModularAvatarComponents.Single().MergeTargetObjectFileId == 300 && input.VrcFuryComponents.Single().ArmatureLinks.Single().GameObjectFileId == 300, "Adapter mutated parsed settings.");
            check(!ReferenceEquals(first.Operations[0], second.Operations[0]), "Plans share mutable operations.");
        });
    }

    private static AvatarPackageManifest Mixed(long maSource, long furySource) => new()
    {
        SourcePath = "Assets/fixture.prefab",
        ModularAvatarComponents = new[]
        {
            new ModularAvatarComponentDefinition { Kind = ModularAvatarComponentKind.MergeArmature, GameObjectFileId = maSource, ComponentFileId = 1, MergeTargetObjectFileId = 300 }
        },
        VrcFuryComponents = new[]
        {
            new VrcFuryComponentDefinition { ArmatureLinks = new[]
            {
                new VrcFuryArmatureLinkDefinition { GameObjectFileId = furySource, ComponentFileId = 2, Recursive = true,
                    LinkToTargets = new[] { new VrcFuryLinkToTarget { UseObj = true, ObjFileId = 500 } } }
            } }
        }
    };

    private sealed class RecordingSink : IAvatarPlanSink
    {
        internal List<AvatarBuildOperation> Operations { get; } = new();
        internal List<AvatarOperationDiagnostic> Diagnostics { get; } = new();
        public void AddOperation(AvatarBuildOperation operation) => Operations.Add(operation);
        public void AddDiagnostic(AvatarOperationSeverity severity, string message, string context = "", string code = "") =>
            Diagnostics.Add(new() { Severity = severity, Message = message, SourceContext = context, Code = code });
    }
}
