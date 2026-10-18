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
                var primaryTarget = link.LinkToTargets.Count > 0 ? link.LinkToTargets[0] : null;
                long sourceId = link.PropBoneFileId ?? link.GameObjectFileId;

                int targetBone = primaryTarget?.UseBone == true ? primaryTarget.Bone : -1;
                long? targetObj = primaryTarget?.UseObj == true && primaryTarget.ObjFileId != 0 ? primaryTarget.ObjFileId : null;
                string offset = primaryTarget?.Offset ?? "";

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
                        TargetHumanoidBone = targetBone,
                        TargetObjectFileId = targetObj,
                        TargetOffsetPath = offset,
                        Suffix = link.RemoveBoneSuffix ?? "",
                        RemoveBoneSuffix = link.RemoveBoneSuffix ?? "",
                        Recursive = true,
                        AlignPosition = link.AlignPosition,
                        AlignRotation = link.AlignRotation,
                        AlignScale = link.AlignScale,
                        AutoScaleFactor = link.AutoScaleFactor
                    };
                    plan.AddOperation(op);
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
                        TargetHumanoidBone = targetBone,
                        TargetObjectFileId = targetObj,
                        TargetOffsetPath = offset,
                        AlignPosition = link.AlignPosition,
                        AlignRotation = link.AlignRotation,
                        AlignScale = link.AlignScale,
                        AutoScaleFactor = link.AutoScaleFactor,
                        KeepOffsets = link.KeepOffsets
                    };
                    plan.AddOperation(op);
                }
            }
        }
    }
}
