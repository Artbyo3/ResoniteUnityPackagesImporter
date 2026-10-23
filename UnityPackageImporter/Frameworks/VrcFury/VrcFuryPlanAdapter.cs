using System;
using System.Collections.Generic;
using UnityPackageImporter.Conversion;
using UnityPackageImporter.Models;

namespace UnityPackageImporter.Frameworks.VrcFury;

/// <summary>Translates only this framework's parsed settings into shared operations. No world mutation or cross-framework validation.</summary>
internal static class VrcFuryPlanAdapter
{
    internal static void AppendOperations(IEnumerable<VrcFuryComponentDefinition> components, IAvatarPlanSink plan)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(plan);

        foreach (var vf in components)
        {
            foreach (var unhandled in vf.UnsupportedFeatures)
            {
                plan.AddDiagnostic(
                    AvatarOperationSeverity.Warning,
                    $"VRCFury feature '{unhandled}' is deferred; base assets imported without this behavior.",
                    vf.GameObjectName,
                    "UNHANDLED_VRCFURY_FEATURE");
            }

            foreach (var link in vf.ArmatureLinks)
            {
                long sourceId = link.PropBoneFileId ?? link.GameObjectFileId;
                var targets = new List<(int Bone, long? ObjectId, string Path, string Offset)>();
                foreach (var target in link.LinkToTargets)
                    targets.Add((target.UseBone ? target.Bone : -1,
                        !target.UseBone && target.UseObj && target.ObjFileId != 0 ? target.ObjFileId : null,
                        !target.UseBone && !target.UseObj ? "$$$AVATAR_ROOT$$$" : "", target.Offset));
                var primary = targets.Count > 0 ? targets[0] : (-1, (long?)null, "", "");
                AvatarBuildOperation operation;

                if (link.Recursive)
                {
                    var op = new ArmatureMergeOperation
                    {
                        Source = new AvatarBuildOperationSource
                        {
                            Framework = "VRCFury",
                            ComponentType = "VRCFuryArmatureLink",
                            ComponentFileId = link.ComponentFileId,
                            GameObjectFileId = link.GameObjectFileId,
                            GameObjectName = link.GameObjectName,
                            SourcePath = link.SourcePath
                        },
                        SourceArmatureRootFileId = sourceId,
                        SourceArmatureRootName = link.GameObjectName,
                        TargetHumanoidBone = primary.Item1,
                        TargetObjectFileId = primary.Item2,
                        TargetReferencePath = primary.Item3,
                        TargetOffsetPath = primary.Item4,
                        TargetCandidates = targets.AsReadOnly(),
                        Suffix = link.RemoveBoneSuffix ?? "",
                        RemoveBoneSuffix = link.RemoveBoneSuffix ?? "",
                        Recursive = true,
                        AlignPosition = link.AlignPosition,
                        AlignRotation = link.AlignRotation,
                        AlignScale = link.AlignScale,
                        AutoScaleFactor = link.AutoScaleFactor
                    };
                    operation = op;
                }
                else
                {
                    var op = new BoneAttachmentOperation
                    {
                        Source = new AvatarBuildOperationSource
                        {
                            Framework = "VRCFury",
                            ComponentType = "VRCFuryArmatureLink",
                            ComponentFileId = link.ComponentFileId,
                            GameObjectFileId = link.GameObjectFileId,
                            GameObjectName = link.GameObjectName,
                            SourcePath = link.SourcePath
                        },
                        SourceObjectFileId = sourceId,
                        SourceObjectName = link.GameObjectName,
                        TargetHumanoidBone = primary.Item1,
                        TargetObjectFileId = primary.Item2,
                        TargetReferencePath = primary.Item3,
                        TargetOffsetPath = primary.Item4,
                        TargetCandidates = targets.AsReadOnly(),
                        AlignPosition = link.AlignPosition,
                        AlignRotation = link.AlignRotation,
                        AlignScale = link.AlignScale,
                        AutoScaleFactor = link.AutoScaleFactor,
                        KeepOffsets = link.KeepOffsets
                    };
                    operation = op;
                }
                foreach (var diagnostic in link.SemanticDiagnostics)
                    operation.AddDiagnostic(diagnostic.IsError ? AvatarOperationSeverity.Error : AvatarOperationSeverity.Warning,
                        diagnostic.Message, diagnostic.Code);
                plan.AddOperation(operation);
            }
        }
    }
}
