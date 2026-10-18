using System;
using System.Collections.Generic;
using System.Linq;
using FrooxEngine;
using UnityPackageImporter.FrooxEngineRepresentation;
using UnityTypes = UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes;

namespace UnityPackageImporter.Models;

// Engine observations stay at this boundary; the readiness policy is engine-independent.
// Called only on the world context, including again immediately before an output action.
internal static class ImportResultAdapter
{
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
            materialWarnings);
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
