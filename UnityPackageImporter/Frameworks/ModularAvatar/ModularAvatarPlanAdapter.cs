using System;
using System.Collections.Generic;
using UnityPackageImporter.Conversion;
using UnityPackageImporter.Models;

namespace UnityPackageImporter.Frameworks.ModularAvatar;

/// <summary>Translates only this framework's parsed settings into shared operations. No world mutation or cross-framework validation.</summary>
internal static class ModularAvatarPlanAdapter
{
    internal static void AppendOperations(IEnumerable<ModularAvatarComponentDefinition> components, IAvatarPlanSink plan)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(plan);

        foreach (var ma in components)
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
    }
}
