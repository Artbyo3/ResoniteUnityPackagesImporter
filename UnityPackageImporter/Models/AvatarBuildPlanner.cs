using System;
using UnityPackageImporter.Conversion;
using UnityPackageImporter.Importing;

namespace UnityPackageImporter.Models;

/// <summary>Compatibility entry points for callers during the module migration.</summary>
public static class AvatarBuildPlanner
{
    public static AvatarBuildPlan CreatePlan(AvatarPackageManifest manifest, string avatarName = "") =>
        AvatarPlanComposer.Create(manifest, avatarName);

    public static void ValidatePlan(AvatarBuildPlan plan) => AvatarPlanValidator.Validate(plan);

    public static void PlanArmatureMerge(
        ArmatureMergeOperation op,
        MergeHierarchyNode sourceRoot,
        MergeHierarchyNode targetRoot)
    {
        if (op == null) throw new ArgumentNullException(nameof(op));
        if (sourceRoot == null) throw new ArgumentNullException(nameof(sourceRoot));
        if (targetRoot == null) throw new ArgumentNullException(nameof(targetRoot));

        var bonePlan = ModularAvatarMergePlanner.CreatePlan(sourceRoot, targetRoot, op.Prefix, op.Suffix);

        op.Mappings.Clear();
        op.Mappings.AddRange(bonePlan.Mappings);

        op.RetainedRoots.Clear();
        op.RetainedRoots.AddRange(bonePlan.RetainedRoots);

        foreach (var conflict in bonePlan.Conflicts)
        {
            op.AddDiagnostic(
                AvatarOperationSeverity.Error,
                $"{conflict.Message} at source path '{conflict.SourcePath}'",
                "AMBIGUOUS_BONE_MATCH");
        }
    }

}
