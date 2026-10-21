using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.FinalIK;
using Renderite.Shared;
using UnityPackageImporter.FrooxEngineRepresentation;

namespace UnityPackageImporter.Models;

/// <summary>
/// Executes a validated AvatarBuildPlan on the staged FrooxEngine slots
/// before native humanoid and avatar creation take ownership.
/// </summary>
public static class AvatarBuildPlanExecutor
{
    public static async Task ExecutePlanAsync(
        AvatarBuildPlan plan,
        Slot avatarRootSlot,
        IReadOnlyDictionary<ulong, IUnityObject> unityObjects,
        IUnityStructureImporter importer,
        CancellationToken cancellationToken = default)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));

        await default(ToWorld);
        cancellationToken.ThrowIfCancellationRequested();

        if (avatarRootSlot == null || avatarRootSlot.IsDestroyed)
        {
            plan.AddDiagnostic(AvatarOperationSeverity.Error, "The staged avatar root is unavailable.", plan.AvatarName, "AVATAR_ROOT_NOT_FOUND");
            return;
        }

        UnityPackageImporter.Msg($"Executing avatar conversion plan with {plan.Operations.Count} operations for '{plan.AvatarName}'");

        await AvatarPlanExecution.ExecuteAsync(plan, async (op, token) =>
        {
            if (op is ArmatureMergeOperation merge)
            {
                await ExecuteArmatureMergeAsync(merge, avatarRootSlot, unityObjects, importer, token);
            }
            else if (op is BoneAttachmentOperation attach)
            {
                await ExecuteBoneAttachmentAsync(attach, avatarRootSlot, unityObjects, importer, token);
            }
            else
            {
                op.AddDiagnostic(AvatarOperationSeverity.Error, $"Operation kind '{op.Kind}' has no executor.", "UNSUPPORTED_OPERATION");
            }
        }, cancellationToken, deferNestedPrefab: op =>
            importer is UnitySceneImportTask scene && scene.NestedPrefabs.ContainsSource(op.Source.SourcePath));

        foreach (var diagnostic in plan.Diagnostics.Concat(plan.Operations.SelectMany(op => op.Diagnostics)))
            UnityPackageImporter.Warn(diagnostic.ToString());

        await default(ToWorld);
    }

    private static async Task ExecuteArmatureMergeAsync(
        ArmatureMergeOperation merge,
        Slot avatarRootSlot,
        IReadOnlyDictionary<ulong, IUnityObject> unityObjects,
        IUnityStructureImporter importer,
        CancellationToken cancellationToken)
    {
        await default(ToWorld);

        // 1. Resolve source armature slot
        Slot sourceSlot = ResolveObjectSlot(merge.SourceArmatureRootFileId, unityObjects);

        if (sourceSlot == null)
        {
            merge.AddDiagnostic(
                AvatarOperationSeverity.Error,
                $"Source armature root '{merge.SourceArmatureRootName}' (ID:{merge.SourceArmatureRootFileId}) could not be resolved.",
                "SOURCE_NOT_FOUND");
            return;
        }

        // Resolve the same declared-target contract for merges and attachments.
        Slot targetSlot = AvatarTargetResolution.Resolve(merge, avatarRootSlot,
            id => ResolveObjectSlot(id, unityObjects),
            bone => FindBoneInAvatar(avatarRootSlot, ModularAvatarAttachmentInstaller.MapUnityHumanoidBone(bone), sourceSlot),
            ResolveRelativeSlotPath);
        if (targetSlot == null) return;

        if (!ValidateResolvedSlots(merge, avatarRootSlot, sourceSlot, targetSlot)) return;

        UnityPackageImporter.Msg($"Merging armature from '{sourceSlot.Name}' into '{targetSlot.Name}' (Source: {merge.Source.Framework})");

        // 3. Build hierarchy nodes and plan merge
        var sourceSlotMap = new Dictionary<string, Slot>(StringComparer.Ordinal);
        var targetSlotMap = new Dictionary<string, Slot>(StringComparer.Ordinal);
        var sourceNode = BuildHierarchyTree(sourceSlot, sourceSlotMap);
        var targetNode = BuildHierarchyTree(targetSlot, targetSlotMap, sourceSlot);

        AvatarBuildPlanner.PlanArmatureMerge(merge, sourceNode, targetNode);
        if (!merge.IsValid) return;

        var boneMap = new Dictionary<Slot, Slot>();
        foreach (var mapping in merge.Mappings)
        {
            if (sourceSlotMap.TryGetValue(mapping.SourceId, out var s) &&
                targetSlotMap.TryGetValue(mapping.TargetId, out var t))
            {
                boneMap[s] = t;
            }
        }

        // 4. Reparent retained / unmapped bones under their target parent
        foreach (var retained in merge.RetainedRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!sourceSlotMap.TryGetValue(retained.SourceId, out var unmappedChild)) continue;
            if (boneMap.ContainsKey(unmappedChild)) continue;
            if (!targetSlotMap.TryGetValue(retained.TargetParentId, out var targetParent)) continue;

            unmappedChild.SetParent(targetParent, false);
        }

        // 5. Rebind all SkinnedMeshRenderers
        var renderers = avatarRootSlot.GetComponentsInChildren<FrooxEngine.SkinnedMeshRenderer>();
        foreach (var renderer in renderers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool modified = false;
            for (int i = 0; i < renderer.Bones.Count; i++)
            {
                var bone = renderer.Bones[i];
                if (bone != null && boneMap.TryGetValue(bone, out var mappedBone))
                {
                    renderer.Bones[i] = mappedBone;
                    modified = true;
                }
            }

            if (modified)
            {
                SkinnedBoundsPolicy.ApplyAfterBones(renderer, $"Unified Avatar Conversion ({merge.Source.Framework})");
            }
        }

        // 6. Align transform if requested
        if (merge.AlignPosition) sourceSlot.GlobalPosition = targetSlot.GlobalPosition;
        if (merge.AlignRotation) sourceSlot.GlobalRotation = targetSlot.GlobalRotation;
        if (merge.AlignScale) sourceSlot.GlobalScale = targetSlot.GlobalScale;
    }

    private static async Task ExecuteBoneAttachmentAsync(
        BoneAttachmentOperation attach,
        Slot avatarRootSlot,
        IReadOnlyDictionary<ulong, IUnityObject> unityObjects,
        IUnityStructureImporter importer,
        CancellationToken cancellationToken)
    {
        await default(ToWorld);

        // 1. Resolve source slot
        Slot sourceSlot = ResolveObjectSlot(attach.SourceObjectFileId, unityObjects);

        if (sourceSlot == null)
        {
            attach.AddDiagnostic(
                AvatarOperationSeverity.Error,
                $"Source slot '{attach.SourceObjectName}' (ID:{attach.SourceObjectFileId}) could not be resolved.",
                "SOURCE_NOT_FOUND");
            return;
        }

        // Resolve the same declared-target contract for merges and attachments.
        Slot targetSlot = AvatarTargetResolution.Resolve(attach, avatarRootSlot,
            id => ResolveObjectSlot(id, unityObjects),
            bone => FindBoneInAvatar(avatarRootSlot, ModularAvatarAttachmentInstaller.MapUnityHumanoidBone(bone), sourceSlot),
            ResolveRelativeSlotPath);
        if (targetSlot == null) return;

        if (!ValidateResolvedSlots(attach, avatarRootSlot, sourceSlot, targetSlot)) return;

        UnityPackageImporter.Msg($"Attaching '{sourceSlot.Name}' to '{targetSlot.Name}' (Source: {attach.Source.Framework})");

        float3 originalGlobalPos = sourceSlot.GlobalPosition;
        floatQ originalGlobalRot = sourceSlot.GlobalRotation;
        float3 originalGlobalScale = sourceSlot.GlobalScale;

        sourceSlot.SetParent(targetSlot, false);

        if (attach.AlignPosition)
            sourceSlot.LocalPosition = float3.Zero;
        else if (attach.KeepOffsets)
            sourceSlot.GlobalPosition = originalGlobalPos;

        if (attach.AlignRotation)
            sourceSlot.LocalRotation = floatQ.Identity;
        else if (attach.KeepOffsets)
            sourceSlot.GlobalRotation = originalGlobalRot;

        if (attach.AlignScale)
            sourceSlot.LocalScale = float3.One;
        else if (attach.KeepOffsets)
            sourceSlot.GlobalScale = originalGlobalScale;
    }

    private static Slot ResolveObjectSlot(long? fileId, IReadOnlyDictionary<ulong, IUnityObject> unityObjects)
    {
        if (fileId is null or 0 || unityObjects == null || !unityObjects.TryGetValue(unchecked((ulong)fileId.Value), out var obj)) return null;
        var go = obj as FrooxEngineRepresentation.GameObjectTypes.GameObject;
        if (obj is FrooxEngineRepresentation.GameObjectTypes.Transform transform)
        {
            go = transform.parentHashedGameObj;
            if (go == null && transform.m_GameObject != null && transform.m_GameObject.TryGetValue("fileID", out var goId) &&
                unityObjects.TryGetValue(goId, out var owner))
                go = owner as FrooxEngineRepresentation.GameObjectTypes.GameObject;
        }
        return go?.frooxEngineSlot != null && !go.frooxEngineSlot.IsDestroyed ? go.frooxEngineSlot : null;
    }

    private static bool IsWithin(Slot slot, Slot root)
    {
        for (var current = slot; current != null; current = current.Parent)
            if (current == root) return true;
        return false;
    }

    private static bool ValidateResolvedSlots(AvatarBuildOperation op, Slot avatarRoot, Slot source, Slot target)
    {
        if (!IsWithin(source, avatarRoot) || !IsWithin(target, avatarRoot))
        {
            op.AddDiagnostic(AvatarOperationSeverity.Error, "Source and target must belong to the staged avatar.", "REFERENCE_OUTSIDE_AVATAR");
            return false;
        }
        if (IsWithin(target, source))
        {
            op.AddDiagnostic(AvatarOperationSeverity.Error, "The target is the source itself or one of its descendants.", "SELF_ATTACHMENT");
            return false;
        }
        return true;
    }

    private static Slot FindBoneInAvatar(Slot avatarRoot, BodyNode node, Slot excludedSource)
    {
        if (avatarRoot == null || avatarRoot.IsDestroyed || node == BodyNode.NONE) return null;

        foreach (var rig in avatarRoot.GetComponentsInChildren<BipedRig>())
        {
            if (IsWithin(rig.Slot, excludedSource)) continue;
            var bone = rig.TryGetBone(node);
            if (bone != null && !bone.IsDestroyed && !IsWithin(bone, excludedSource)) return bone;
        }

        string boneName = node.ToString();
        // Before humanoid setup, accept a unique named bone outside the source.
        // Multiple candidates must fail resolution rather than pick by traversal order.
        var candidates = new List<Slot>();
        CollectNamedSlots(avatarRoot, boneName, excludedSource, candidates);
        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static void CollectNamedSlots(Slot current, string name, Slot excludeSubtree, List<Slot> results)
    {
        if (current == null || current.IsDestroyed || current == excludeSubtree) return;
        if (current.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) results.Add(current);

        foreach (var child in current.Children)
        {
            CollectNamedSlots(child, name, excludeSubtree, results);
        }
    }

    private static Slot ResolveRelativeSlotPath(Slot root, string path)
    {
        if (root == null || root.IsDestroyed || string.IsNullOrWhiteSpace(path)) return root;

        string[] segments = path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        Slot current = root;

        foreach (var seg in segments)
        {
            if (seg == ".") continue;
            if (seg == "..")
            {
                current = current.Parent ?? current;
                continue;
            }

            var matches = current.Children.Where(c => c.Name.Equals(seg, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
            if (matches.Length != 1) return null;
            current = matches[0];
        }

        return current;
    }

    private static MergeHierarchyNode BuildHierarchyTree(Slot slot, Dictionary<string, Slot> slotMap, Slot excludedSubtree = null)
    {
        string id = slot.ReferenceID.ToString();
        slotMap[id] = slot;

        var children = new List<MergeHierarchyNode>();
        foreach (var child in slot.Children)
        {
            if (child != excludedSubtree)
                children.Add(BuildHierarchyTree(child, slotMap, excludedSubtree));
        }

        return new MergeHierarchyNode
        {
            Id = id,
            Name = slot.Name,
            Children = children
        };
    }
}
