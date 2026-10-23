using System;
using System.Collections.Generic;
using System.Linq;
using FrooxEngine;
using FrooxEngine.CommonAvatar;
using FrooxEngine.FinalIK;
using Renderite.Shared;
using UnityPackageImporter.FrooxEngineRepresentation;
using UnityTypes = UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes;

namespace UnityPackageImporter.Models;

// Engine observations stay at this boundary; the readiness policy is engine-independent.
// Called only on the world context, including again immediately before an output action.
internal static class ImportResultAdapter
{
    internal static ReportResult CaptureReport(PreparedImportItem item, UnityProjectImporter project, bool includeNames)
    {
        ImportResult result = Read(item, project);
        var native = new ReportNativeReadiness();
        if (item.CurrentStructureRootSlot is { IsDestroyed: false } root)
        {
            var allRigs = root.GetComponentsInChildren<BipedRig>().ToArray();
            var rigs = allRigs.Where(rig => rig.IsBiped).ToArray();
            var rig = rigs.Length == 1 ? rigs[0] : null;
            var ik = rig?.Slot.GetComponent<VRIK>();
            native = new(allRigs.Length, rig != null ? ik != null : null,
                ik != null ? ik.Solver.BoneReferences.leftHand.Target != null : null,
                ik != null ? ik.Solver.BoneReferences.rightHand.Target != null : null,
                rig != null ? rig.TryGetBone(BodyNode.LeftEye) != null : null,
                rig != null ? rig.TryGetBone(BodyNode.RightEye) != null : null,
                result.NativeReadiness == NativeAvatarReadiness.Ready ? true : result.NativeReadiness == NativeAvatarReadiness.MissingViewpoint ? false : null,
                ik?.Solver.Initiated, rigs.Length);
        }
        var objects = item.FailedObjectIds.Distinct().Select(id =>
        {
            var ownership = item.IsScopedSceneItem ? Scope(item, unchecked((long)id)) : ImportOwnership.Owned;
            return (Id: id, Ownership: ownership);
        }).Where(value => value.Ownership != ImportOwnership.Other).Take(32).Select(value =>
            item.existingIUnityObjects.TryGetValue(value.Id, out var obj)
                ? ImportReportService.Object(obj, value.Ownership, includeNames: includeNames, sourceKey: item.SourcePath)
                : new ReportObjectFailure(ReportObjectType.Other, null, unchecked((long)value.Id), Ownership: value.Ownership, SourceKey: item.SourcePath)).ToArray();
        var operations = item.ConversionPlan?.Operations.Where(op => !item.IsScopedSceneItem ||
            ImportResultScope.Operation(op, item.SourcePath, id => Scope(item, id)) != ImportOwnership.Other)
            .Take(24).Select(op => new ReportOperation(op.Kind, op.ExecutionState,
                item.IsScopedSceneItem ? ImportResultScope.Operation(op, item.SourcePath, id => Scope(item, id)) : ImportOwnership.Owned,
                op.Source.ComponentFileId, op.Source.GameObjectFileId, op.Diagnostics.Select(d => d.Code).Distinct().Take(8).ToArray(),
                includeNames ? op.Source.GameObjectName : null,
                op.ExecutionState == AvatarOperationExecutionState.DeferredNestedPrefab && item.NestedPrefabs?.ContainsSource(op.Source.SourcePath) == true,
                op.Source.SourcePath))
            .ToArray() ?? Array.Empty<ReportOperation>();
        return new(result.StructureReady, result.NativeReadiness, result.Issues.ToArray(), result.FailedObjectCount,
            result.OperationCount, result.CompletedOperationCount, result.MissingMaterialAssets, result.MissingMeshReferences,
            result.UnsupportedNestedPrefabCount, result.MissingNestedPrefabCount, native, objects, operations,
            item.ConversionPlan?.Diagnostics.Count ?? 0, item.ConversionPlan?.Diagnostics.Select(d => d.Code).Distinct().Take(24).ToArray(),
            includeNames ? item.DisplayName : null, result.UnsupportedFeatureCount, result.MaterialWarningCount, result.RequiresInstallation);
    }

    internal static ImportResult Read(PreparedImportItem item, UnityProjectImporter project)
    {
        bool available = item.CurrentStructureRootSlot != null && !item.CurrentStructureRootSlot.IsDestroyed;
        bool outfit = item.Manifest?.IsModularAvatarAttachment == true;
        int failed = 0;
        bool unresolved = false;
        foreach (ulong id in item.FailedObjectIds.Distinct())
        {
            var scope = item.IsScopedSceneItem ? Scope(item, unchecked((long)id)) : ImportOwnership.Owned;
            if (scope == ImportOwnership.Owned) failed++;
            if (scope == ImportOwnership.Unknown) unresolved = true;
        }
        int missingMeshes = item.existingIUnityObjects.Values.OfType<UnityTypes.SkinnedMeshRenderer>()
            .Distinct().Count(skin => skin.HasMissingMeshReference && (!item.IsScopedSceneItem ||
                Scope(item, unchecked((long)skin.id)) != ImportOwnership.Other));
        int unsupportedNestedPrefabs = item.NestedPrefabs?.InstanceIds.Count(id =>
            !item.IsScopedSceneItem || DeferredInstanceAffects(item, id)) ?? 0;
        int missingNestedPrefabs = item.existingIUnityObjects.Values.OfType<UnityTypes.PrefabInstance>()
            .Where(instance => instance.HasMissingSource).Select(instance => instance.id)
            .Concat(item.NestedPrefabs?.MissingInstanceIds ?? Enumerable.Empty<ulong>()).Distinct()
            .Count(id => !item.IsScopedSceneItem || DeferredInstanceAffects(item, id));
        int unsupported = item.Manifest?.UnsupportedVrcFuryFeatureCount ?? 0;
        // These components are indexed, but are not executed by the structural planner.
        // An outfit's installer handles its own additional MA features at installation.
        if (!outfit && item.Manifest != null)
            unsupported += item.Manifest.ModularAvatarComponents.Count(c => c.Kind is not
                (ModularAvatarComponentKind.MergeArmature or ModularAvatarComponentKind.BoneProxy or ModularAvatarComponentKind.OutfitRoot));
        bool avatar = available && !outfit && (item.Manifest?.IsAvatarPrefab == true ||
            item.CurrentStructureRootSlot.GetComponentInChildren<BipedRig>() != null);
        var materialWarnings = (available && project != null) ? project.GetItemMaterialWarnings(item.CurrentStructureRootSlot) : Array.Empty<string>();
        return ImportResult.Evaluate(available, outfit,
            avatar ? NativeAvatarCreation.ReadReadiness(item) : NativeAvatarReadiness.NotApplicable,
            available && project != null ? project.GetMissingMaterialSummary(item.CurrentStructureRootSlot).MissingAssetCount : 0,
            unsupported, failed, item.ConversionPlan,
            op => item.IsScopedSceneItem
                ? ImportResultScope.Operation(op, item.SourcePath, id => Scope(item, id))
                : ImportOwnership.Owned,
            unresolved, item.OptionalControlsFailed,
            materialWarnings, missingMeshes, unsupportedNestedPrefabs,
            op => item.NestedPrefabs?.ContainsSource(op.Source.SourcePath) == true, missingNestedPrefabs);
    }

    private static bool DeferredInstanceAffects(PreparedImportItem item, ulong id)
    {
        var instance = (UnityTypes.PrefabInstance)item.existingIUnityObjects[id];
        Slot parent = null;
        if (instance.m_Modification?.m_TransformParent?.TryGetValue("fileID", out ulong parentId) == true &&
            item.existingIUnityObjects.TryGetValue(parentId, out var obj) && obj is UnityTypes.Transform transform)
            parent = transform.parentHashedGameObj?.frooxEngineSlot ?? GameObjectSlot(item, transform.m_GameObjectID);
        return NestedPrefabInstanceScope.Affects(parent, slot => PreparedImportItem.Contains(item.CurrentStructureRootSlot, slot));
    }

    private static ImportOwnership Scope(PreparedImportItem item, long id)
    {
        if (!item.existingIUnityObjects.TryGetValue(unchecked((ulong)id), out var obj)) return ImportOwnership.Unknown;
        Slot slot = obj switch
        {
            UnityTypes.GameObject go => go.frooxEngineSlot,
            UnityTypes.Transform transform => transform.parentHashedGameObj?.frooxEngineSlot ?? GameObjectSlot(item, transform.m_GameObjectID),
            UnityTypes.PrefabInstance prefab => prefab.ImportRoot?.frooxEngineSlot,
            UnityTypes.SkinnedMeshRenderer skin => GameObjectSlot(item, skin.m_GameObject),
            UnityTypes.MonoBehaviour behaviour => GameObjectSlot(item, behaviour.m_GameObject),
            UnityTypes.MeshCollider collider => GameObjectSlot(item, collider.m_GameObject),
            UnityTypes.RotationConstraint constraint => GameObjectSlot(item, constraint.m_GameObject),
            _ => null
        };
        if (slot == null || slot.IsDestroyed) return ImportOwnership.Unknown;
        return PreparedImportItem.Contains(item.CurrentStructureRootSlot, slot) ? ImportOwnership.Owned : ImportOwnership.Other;
    }

    private static Slot GameObjectSlot(PreparedImportItem item, Dictionary<string, ulong> reference) =>
        reference != null && reference.TryGetValue("fileID", out var id) ? GameObjectSlot(item, id) : null;

    private static Slot GameObjectSlot(PreparedImportItem item, ulong id) =>
        item.existingIUnityObjects.TryGetValue(id, out var obj) && obj is UnityTypes.GameObject go ? go.frooxEngineSlot : null;
}
