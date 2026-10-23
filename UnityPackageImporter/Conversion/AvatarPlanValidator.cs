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
        foreach (var operation in plan.Operations)
        {
            long sourceId = SourceId(operation);
            var candidates = operation.GetTargetCandidates();
            var viable = candidates.Where(candidate => IsViable(candidate, sourceId)).ToArray();
            if (operation is ArmatureMergeOperation or BoneAttachmentOperation && viable.Length == 0)
            {
                if (candidates.Any(candidate => candidate.Bone < -1 || candidate.Bone >= 55))
                    operation.AddDiagnostic(AvatarOperationSeverity.Error, "No usable target remains; a humanoid bone index is outside the supported Unity bone range (0-54).", "INVALID_HUMANOID_BONE");
                else if (sourceId != 0 && candidates.Any(candidate => candidate.ObjectId == sourceId))
                    operation.AddDiagnostic(AvatarOperationSeverity.Error, $"Operation attempts to attach source object (ID:{sourceId}) to itself.", "SELF_ATTACHMENT");
                else
                    operation.AddDiagnostic(AvatarOperationSeverity.Error, "Target is undefined: neither humanoid bone, target object fileID, nor reference path was provided.", "TARGET_UNRESOLVED");
            }
            if (sourceId == 0) continue;
            string descriptor = string.Join(" -> ", candidates.Select(candidate =>
                $"Bone:{candidate.Bone}|Object:{candidate.ObjectId}|Path:{candidate.Path.Length}:{candidate.Path}|Offset:{candidate.Offset.Length}:{candidate.Offset}"));
            if (sourceTargets.TryGetValue(sourceId, out string existingTarget) && !string.Equals(existingTarget, descriptor, StringComparison.Ordinal))
                operation.AddDiagnostic(AvatarOperationSeverity.Error,
                    $"Conflicting attachment targets for source object (ID:{sourceId}): '{existingTarget}' vs '{descriptor}'.", "CONFLICTING_TARGETS");
            else sourceTargets[sourceId] = descriptor;
        }
        var operationsBySource = plan.Operations.ToLookup(SourceId);
        foreach (var operation in plan.Operations)
        {
            long sourceId = SourceId(operation);
            var viable = operation.GetTargetCandidates().Where(candidate => IsViable(candidate, sourceId)).ToArray();
            bool conditionalDependency = false;
            foreach (var candidate in viable)
            {
                if (candidate.ObjectId is null or 0) continue;
                foreach (var dependency in operationsBySource[candidate.ObjectId.Value])
                {
                    conditionalDependency = viable.Length > 1;
                    if (!ReferenceEquals(operation, dependency) && !operation.DependsOnOperationIds.Contains(dependency.OperationId))
                        operation.DependsOnOperationIds.Add(dependency.OperationId);
                }
            }
            if (conditionalDependency)
                operation.AddDiagnostic(AvatarOperationSeverity.Warning,
                    "Ordered target alternatives contribute conservative dependencies for every viable object candidate. Mutually exclusive fallbacks can form a cycle even when an available later target would avoid it; resolving conditional dependencies requires runtime target selection, which is not implemented.",
                    "CONDITIONAL_TARGET_DEPENDENCIES");
        }
        plan.GetOrderedOperations();
    }

    private static bool IsViable((int Bone, long? ObjectId, string Path, string Offset) candidate, long sourceId) =>
        candidate.Bone >= -1 && candidate.Bone < 55 &&
        (sourceId == 0 || candidate.ObjectId != sourceId) &&
        (candidate.Bone >= 0 || candidate.ObjectId is not (null or 0) || !string.IsNullOrWhiteSpace(candidate.Path));

    private static long SourceId(AvatarBuildOperation operation) => operation switch
    {
        ArmatureMergeOperation merge => merge.SourceArmatureRootFileId,
        BoneAttachmentOperation attach => attach.SourceObjectFileId,
        SkinRebindOperation rebind => rebind.RendererFileId,
        _ => 0
    };
}
