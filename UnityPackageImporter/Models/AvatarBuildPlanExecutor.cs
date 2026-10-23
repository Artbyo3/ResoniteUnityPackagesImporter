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

        var manifest = importer switch
        {
            UnityPrefabImportTask prefab => prefab.Manifest,
            UnitySceneImportTask scene => scene.Manifest,
            _ => null
        };
        var avatarRoots = (manifest?.Avatars ?? Array.Empty<VrcAvatarDefinition>())
            .Select(avatar => ResolveObjectSlot(avatar.GameObjectFileId, unityObjects)).ToArray();
        if (avatarRoots.Any(root => root == null || !IsWithin(root, avatarRootSlot)))
        {
            plan.AddDiagnostic(AvatarOperationSeverity.Error, "An authored avatar root could not be reconstructed.", plan.AvatarName, "AVATAR_ROOT_NOT_FOUND");
            return;
        }
        var modelBones = await ReadModelBonesAsync(unityObjects, cancellationToken);
        var mergeRoots = plan.Operations.OfType<ArmatureMergeOperation>()
            .Select(operation => ResolveObjectSlot(operation.SourceArmatureRootFileId, unityObjects))
            .Where(slot => slot != null).ToHashSet();

        await AvatarPlanExecution.ExecuteAsync(plan, async (op, token) =>
        {
            await default(ToWorld);
            Slot source = ResolveObjectSlot(op.Source.GameObjectFileId, unityObjects);
            if (source == null)
            {
                op.AddDiagnostic(AvatarOperationSeverity.Error,
                    "The operation source could not be reconstructed. Check the source-object and missing-prefab diagnostics.",
                    "SOURCE_NOT_FOUND");
                return;
            }
            Slot scope = AvatarConversionScope.RootFor(source, avatarRootSlot, avatarRoots, IsWithin);
            if (scope == null)
            {
                op.AddDiagnostic(AvatarOperationSeverity.Error, "The operation has no unique owning avatar root.", "AVATAR_SCOPE_UNRESOLVED");
                return;
            }
            if (op is ArmatureMergeOperation merge)
            {
                await ExecuteArmatureMergeAsync(merge, scope, unityObjects, importer, modelBones, mergeRoots, token);
            }
            else if (op is BoneAttachmentOperation attach)
            {
                await ExecuteBoneAttachmentAsync(attach, scope, unityObjects, importer, modelBones, token);
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
        ModelBoneIndex<Slot> modelBones,
        IReadOnlySet<Slot> mergeRoots,
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
            bone => FindBoneInAvatar(avatarRootSlot, ModularAvatarAttachmentInstaller.MapUnityHumanoidBone(bone), sourceSlot, modelBones),
            ResolveRelativeSlotPath, target => IsWithin(target, avatarRootSlot) && !IsWithin(target, sourceSlot));
        if (targetSlot == null) return;

        if (!ValidateResolvedSlots(merge, avatarRootSlot, sourceSlot, targetSlot)) return;

        UnityPackageImporter.Msg($"Merging armature from '{sourceSlot.Name}' into '{targetSlot.Name}' (Source: {merge.Source.Framework})");

        // 3. Build hierarchy nodes and plan merge
        var sourceSlotMap = new Dictionary<string, Slot>(StringComparer.Ordinal);
        var targetSlotMap = new Dictionary<string, Slot>(StringComparer.Ordinal);
        var sourceNode = BoneAttachmentHierarchy.Snapshot(sourceSlot, slot => slot.Children,
            slot => (slot.ReferenceID.ToString(), (string)slot.Name), sourceSlotMap, separatePlanRoots: mergeRoots);
        var targetNode = BoneAttachmentHierarchy.Snapshot(targetSlot, slot => slot.Children,
            slot => (slot.ReferenceID.ToString(), (string)slot.Name), targetSlotMap,
            excludedRoots: mergeRoots.Where(slot => slot != targetSlot).ToHashSet());

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

        var transfer = SkinBindingTransfer.Plan(boneMap.Select(pair => (pair.Key, pair.Value)), IsWithin);
        var originalTransforms = transfer.ToDictionary(pair => pair.Source,
            pair => (Position: pair.Source.GlobalPosition, Rotation: pair.Source.GlobalRotation, Scale: pair.Source.GlobalScale));
        SkinBindingTransfer.Apply(transfer, (source, target) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var desired = VrcFuryArmatureSemantics.SelectWorldTransform(originalTransforms[source],
                (target.GlobalPosition, target.GlobalRotation, target.GlobalScale), merge.AlignPosition, merge.AlignRotation, merge.AlignScale);
            source.GlobalPosition = desired.Position;
            source.GlobalRotation = desired.Rotation;
            source.GlobalScale = desired.Scale;
            SkinBindingTransferAdapter.Preserve(source, target);
        });
        foreach (var renderer in avatarRootSlot.GetComponentsInChildren<FrooxEngine.SkinnedMeshRenderer>())
            SkinnedBoundsPolicy.ApplyAfterBones(renderer, $"Unified Avatar Conversion ({merge.Source.Framework})");
    }

    private static async Task ExecuteBoneAttachmentAsync(
        BoneAttachmentOperation attach,
        Slot avatarRootSlot,
        IReadOnlyDictionary<ulong, IUnityObject> unityObjects,
        IUnityStructureImporter importer,
        ModelBoneIndex<Slot> modelBones,
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
            bone => FindBoneInAvatar(avatarRootSlot, ModularAvatarAttachmentInstaller.MapUnityHumanoidBone(bone), sourceSlot, modelBones),
            ResolveRelativeSlotPath, target => IsWithin(target, avatarRootSlot) && !IsWithin(target, sourceSlot));
        if (targetSlot == null) return;

        if (!ValidateResolvedSlots(attach, avatarRootSlot, sourceSlot, targetSlot)) return;

        UnityPackageImporter.Msg($"Attaching '{sourceSlot.Name}' to '{targetSlot.Name}' (Source: {attach.Source.Framework})");

        var desired = VrcFuryArmatureSemantics.SelectWorldTransform(
            (sourceSlot.GlobalPosition, sourceSlot.GlobalRotation, sourceSlot.GlobalScale),
            (targetSlot.GlobalPosition, targetSlot.GlobalRotation, targetSlot.GlobalScale),
            attach.AlignPosition, attach.AlignRotation, attach.AlignScale);
        sourceSlot.GlobalPosition = desired.Position;
        sourceSlot.GlobalRotation = desired.Rotation;
        sourceSlot.GlobalScale = desired.Scale;
        SkinBindingTransferAdapter.Preserve(sourceSlot, targetSlot);
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

    private static async Task<ModelBoneIndex<Slot>> ReadModelBonesAsync(
        IReadOnlyDictionary<ulong, IUnityObject> unityObjects, CancellationToken cancellationToken)
    {
        var result = new ModelBoneIndex<Slot>();
        foreach (var prefab in unityObjects.Values.OfType<FrooxEngineRepresentation.GameObjectTypes.PrefabInstance>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (prefab.importask == null || prefab.ImportRoot?.frooxEngineSlot == null) continue;
            await default(ToBackground);
            var metadata = ModelMetaParser.ParseFile(prefab.importask.file + UnityPackageImporter.UNITY_META_EXTENSION);
            await default(ToWorld);
            var owned = prefab.importask.FILEID_To_Slot_Pairs.Values
                .OfType<FrooxEngineRepresentation.GameObjectTypes.GameObject>()
                .Select(gameObject => gameObject.frooxEngineSlot).Where(slot => slot != null && !slot.IsDestroyed).Distinct().ToArray();
            foreach (var (boneName, humanName) in metadata.HumanBones)
            {
                BodyNode node = MetaDataFile.HumanoidNameToEnum(humanName);
                if (node == BodyNode.NONE) continue;
                Slot bone = ModelBoneIndex<Slot>.FindOwned(owned, boneName, slot => slot.Name);
                result.Add(prefab.ImportRoot.frooxEngineSlot, (int)node, bone);
            }
        }
        return result;
    }

    private static Slot FindBoneInAvatar(Slot avatarRoot, BodyNode node, Slot excludedSource,
        ModelBoneIndex<Slot> modelBones)
    {
        if (avatarRoot == null || avatarRoot.IsDestroyed || node == BodyNode.NONE) return null;

        var rigBones = avatarRoot.GetComponentsInChildren<BipedRig>()
            .Where(rig => !IsWithin(rig.Slot, excludedSource)).Select(rig => rig.TryGetBone(node))
            .Where(bone => bone != null && !bone.IsDestroyed && IsWithin(bone, avatarRoot) && !IsWithin(bone, excludedSource)).Distinct().ToArray();
        if (rigBones.Length > 0) return AvatarConversionScope.Unique(rigBones);
        if (modelBones.TryResolve((int)node, avatarRoot, excludedSource, IsWithin, slot => !slot.IsDestroyed, out var mapped))
            return mapped;

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

            current = BoneAttachmentTarget.Resolve(current.Children, seg, null,
                child => child.Name, child => child.Name);
            if (current == null || current.IsDestroyed) return null;
        }

        return current;
    }

}
