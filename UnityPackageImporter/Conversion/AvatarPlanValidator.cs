using System;
using System.Collections.Generic;
using System.Linq;
using UnityPackageImporter.Models;

namespace UnityPackageImporter.Conversion;

/// <summary>Validates shared operations after every framework has contributed, including cross-framework conflicts and dependencies.</summary>
internal static class AvatarPlanValidator
{
    internal static void Validate(AvatarBuildPlan plan)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));

        var sourceTargets = new Dictionary<long, string>();

        foreach (var op in plan.Operations)
        {
            long sourceId = op switch
            {
                ArmatureMergeOperation merge => merge.SourceArmatureRootFileId,
                BoneAttachmentOperation attach => attach.SourceObjectFileId,
                SkinRebindOperation rebind => rebind.RendererFileId,
                _ => 0
            };

            long? targetObjId = op switch
            {
                ArmatureMergeOperation merge => merge.TargetObjectFileId,
                BoneAttachmentOperation attach => attach.TargetObjectFileId,
                _ => null
            };

            int targetBone = op switch
            {
                ArmatureMergeOperation merge => merge.TargetHumanoidBone,
                BoneAttachmentOperation attach => attach.TargetHumanoidBone,
                _ => -1
            };

            string targetPath = op switch
            {
                ArmatureMergeOperation merge => merge.TargetReferencePath,
                BoneAttachmentOperation attach => attach.TargetReferencePath,
                _ => ""
            };

            // A. Check for self-attachment
            if (sourceId != 0 && targetObjId.HasValue && sourceId == targetObjId.Value)
            {
                op.AddDiagnostic(
                    AvatarOperationSeverity.Error,
                    $"Operation attempts to attach source object (ID:{sourceId}) to itself.",
                    "SELF_ATTACHMENT");
            }

            // B. Check for unresolved target specifications
            if ((op is ArmatureMergeOperation or BoneAttachmentOperation) &&
                targetBone < 0 && (targetObjId is null or 0) && string.IsNullOrWhiteSpace(targetPath))
            {
                op.AddDiagnostic(
                    AvatarOperationSeverity.Error,
                    "Target is undefined: neither humanoid bone, target object fileID, nor reference path was provided.",
                    "TARGET_UNRESOLVED");
            }

            if (targetBone < -1 || targetBone >= 55)
                op.AddDiagnostic(AvatarOperationSeverity.Error, $"Humanoid bone index {targetBone} is outside the supported Unity bone range (0–54).", "INVALID_HUMANOID_BONE");

            // C. Check for conflicting attachment targets for the same source object
            if (sourceId != 0)
            {
                string targetDescriptor = targetObjId.HasValue
                    ? $"Object:{targetObjId.Value}"
                    : targetBone >= 0 ? $"Bone:{targetBone}" : $"Path:{targetPath}";
                string offset = op switch
                {
                    ArmatureMergeOperation merge => merge.TargetOffsetPath,
                    BoneAttachmentOperation attach => attach.TargetOffsetPath,
                    _ => ""
                };
                targetDescriptor += $"|Offset:{offset}";

                if (sourceTargets.TryGetValue(sourceId, out string existingTarget))
                {
                    if (!string.Equals(existingTarget, targetDescriptor, StringComparison.Ordinal))
                    {
                        op.AddDiagnostic(
                            AvatarOperationSeverity.Error,
                            $"Conflicting attachment targets for source object (ID:{sourceId}): '{existingTarget}' vs '{targetDescriptor}'.",
                            "CONFLICTING_TARGETS");
                    }
                }
                else
                {
                    sourceTargets[sourceId] = targetDescriptor;
                }
            }
        }

        // D. Resolve Dependencies:
        // If an operation's target matches another operation's source, it must execute after that operation.
        var operationsBySource = plan.Operations.ToLookup(op => op switch
        {
            ArmatureMergeOperation m => m.SourceArmatureRootFileId,
            BoneAttachmentOperation b => b.SourceObjectFileId,
            _ => 0
        });
        foreach (var opA in plan.Operations)
        {
            long? targetA = opA switch
            {
                ArmatureMergeOperation m => m.TargetObjectFileId,
                BoneAttachmentOperation b => b.TargetObjectFileId,
                _ => null
            };

            if (targetA is null or 0) continue;

            foreach (var opB in operationsBySource[targetA.Value])
            {
                if (ReferenceEquals(opA, opB)) continue;
                if (!opA.DependsOnOperationIds.Contains(opB.OperationId))
                    opA.DependsOnOperationIds.Add(opB.OperationId);
            }
        }

        // Detect cycles before callers inspect HasErrors, rather than only at execution.
        plan.GetOrderedOperations();
    }

}
