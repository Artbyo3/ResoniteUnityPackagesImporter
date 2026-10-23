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
                bool avatarPath = ma.BoneProxySubPath == "$$AVATAR" ||
                    (ma.BoneReference == 55 && !string.IsNullOrWhiteSpace(ma.BoneProxySubPath));
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
                    TargetHumanoidBone = avatarPath || ma.BoneReference == 55 ? -1 : ma.BoneReference,
                    TargetReferencePath = avatarPath ? "$$$AVATAR_ROOT$$$" : "",
                    TargetOffsetPath = ma.BoneProxySubPath == "$$AVATAR" ? "" : ma.BoneProxySubPath ?? "",
                    AlignPosition = ma.BoneProxyAttachmentMode is not (2 or 4),
                    AlignRotation = ma.BoneProxyAttachmentMode is not (2 or 3),
                    AlignScale = ma.BoneProxyMatchScale,
                    KeepOffsets = ma.BoneProxyAttachmentMode is 2 or 3 or 4
                };
                if (ma.BoneProxyAttachmentMode is < 0 or > 4)
                    op.AddDiagnostic(AvatarOperationSeverity.Error, "The Bone Proxy attachment mode is not supported.", "INVALID_ATTACHMENT_MODE");
                plan.AddOperation(op);
            }
        }
    }
}
