using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityPackageImporter.Models;

/// <summary>
/// Constructs and validates a deterministic AvatarBuildPlan from parsed
/// Modular Avatar and VRCFury metadata without requiring Unity or FrooxEngine runtime.
/// </summary>
public static class AvatarBuildPlanner
{
    public static AvatarBuildPlan CreatePlan(AvatarPackageManifest manifest, string avatarName = "")
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));

        var plan = new AvatarBuildPlan
        {
            AvatarName = string.IsNullOrWhiteSpace(avatarName) ? PathSafeName(manifest.SourcePath) : avatarName
        };

        // 1. Convert Modular Avatar Components
        foreach (var ma in manifest.ModularAvatarComponents)
        {
            if (ma.Kind == ModularAvatarComponentKind.MergeArmature)
            {
                long rootId = ma.ArmatureRootFileId is null or 0 ? ma.GameObjectFileId : ma.ArmatureRootFileId.Value;
                bool defaultRoot = string.IsNullOrWhiteSpace(ma.MergeTargetPath) && (ma.MergeTargetObjectFileId is null or 0);
                string targetPath = defaultRoot ? "$$$AVATAR_ROOT$$$" : ma.MergeTargetPath;
                var op = new ArmatureMergeOperation
                {
                    Source = new AvatarBuildOperationSource
                    {
                        Framework = "ModularAvatar",
                        ComponentType = "ModularAvatarMergeArmature",
                        ComponentFileId = ma.ComponentFileId,
                        GameObjectFileId = ma.GameObjectFileId,
                        GameObjectName = ma.GameObjectName,
                        SourcePath = ma.SourcePath
                    },
                    SourceArmatureRootFileId = rootId,
                    SourceArmatureRootName = ma.GameObjectName,
                    TargetReferencePath = targetPath,
                    TargetObjectFileId = ma.MergeTargetObjectFileId == 0 ? null : ma.MergeTargetObjectFileId,
                    Prefix = ma.Prefix ?? "",
                    Suffix = ma.Suffix ?? "",
                    Recursive = true
                };
                if (defaultRoot)
                    op.AddDiagnostic(AvatarOperationSeverity.Info, "Merge Armature has no explicit target; using the avatar root.", "DEFAULT_AVATAR_ROOT");
                plan.AddOperation(op);
            }
            else if (ma.Kind == ModularAvatarComponentKind.BoneProxy)
            {
                var op = new BoneAttachmentOperation
                {
                    Source = new AvatarBuildOperationSource
                    {
                        Framework = "ModularAvatar",
                        ComponentType = "ModularAvatarBoneProxy",
                        ComponentFileId = ma.ComponentFileId,
                        GameObjectFileId = ma.GameObjectFileId,
                        GameObjectName = ma.GameObjectName,
                        SourcePath = ma.SourcePath
                    },
                    SourceObjectFileId = ma.GameObjectFileId,
                    SourceObjectName = ma.GameObjectName,
                    TargetHumanoidBone = ma.BoneReference,
                    TargetOffsetPath = ma.BoneProxySubPath ?? "",
                    AlignPosition = true,
                    AlignRotation = true,
                    AlignScale = ma.BoneProxyMatchScale,
                    KeepOffsets = false
                };
                plan.AddOperation(op);
            }
        }

        // 2. Convert VRCFury Components
        foreach (var vf in manifest.VrcFuryComponents)
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

        // 3. Deterministic Validation and Conflict Detection
        ValidatePlan(plan);

        return plan;
    }

    public static void ValidatePlan(AvatarBuildPlan plan)
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

    private static string PathSafeName(string path)
    {
        if (string.IsNullOrEmpty(path)) return "Avatar";
        int lastSlash = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        return lastSlash >= 0 ? path.Substring(lastSlash + 1) : path;
    }
}
