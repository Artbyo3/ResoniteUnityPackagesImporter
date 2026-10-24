#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FrooxEngine;
using FrooxEngine.CommonAvatar;
using UnityPackageImporter.FrooxEngineRepresentation;
using UnityGameObject = UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes.GameObject;
using UnitySkin = UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes.SkinnedMeshRenderer;

namespace UnityPackageImporter.Models;

internal static class BlendshapeInitialAppearanceAdapter
{
    private static bool TryCollectScope(IReadOnlyDictionary<ulong, IUnityObject> unityObjects,
        IUnityStructureImporter importer, AvatarBuildPlan plan, string warningCode,
        out (long Id, Slot Slot)[] roots, out Dictionary<long, BlendshapeAppearanceObject> objects,
        out Dictionary<long, BlendshapeAppearanceRenderer> renderers, CancellationToken cancellationToken)
    {
        roots = Array.Empty<(long, Slot)>();
        objects = new Dictionary<long, BlendshapeAppearanceObject>();
        renderers = new Dictionary<long, BlendshapeAppearanceRenderer>();
        var manifest = importer is UnityPrefabImportTask prefab ? prefab.Manifest : null;
        var stage = importer.CurrentStructureRootSlot;
        roots = (manifest?.Avatars ?? Array.Empty<VrcAvatarDefinition>()).Select(avatar =>
            (Id: avatar.GameObjectFileId, Slot: unityObjects.TryGetValue(unchecked((ulong)avatar.GameObjectFileId), out var entry) && entry is UnityGameObject game ? game.frooxEngineSlot : null))
            .Where(root => root.Slot != null && !root.Slot.IsDestroyed && PreparedImportItem.Contains(stage, root.Slot))
            .Select(root => (root.Id, root.Slot!)).ToArray();
        if (roots.Length == 0)
        {
            plan.AddDiagnostic(AvatarOperationSeverity.Warning,
                "Blendshape synchronization skipped: no verified assembled avatar root in this import structure.",
                code: warningCode);
            return false;
        }
        var capturedRoots = roots;
        if (capturedRoots.Any(root => capturedRoots.Count(other => PreparedImportItem.Contains(root.Slot, other.Slot) ||
            PreparedImportItem.Contains(other.Slot, root.Slot)) != 1))
        {
            plan.AddDiagnostic(AvatarOperationSeverity.Warning,
                "Blendshape synchronization skipped: overlapping or duplicate avatar ownership; no staging-root fallback was used.",
                code: warningCode);
            return false;
        }
        foreach (var entry in unityObjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Value is not UnityGameObject game || game.frooxEngineSlot == null || game.frooxEngineSlot.IsDestroyed) continue;
            var slot = game.frooxEngineSlot;
            if (!PreparedImportItem.Contains(stage, slot)) continue;
            var scope = AvatarConversionScope.Unique(capturedRoots.Where(root => PreparedImportItem.Contains(root.Slot, slot)).Select(root => root.Slot));
            if (scope == null) continue;
            long scopeId = capturedRoots.Where(root => root.Slot == scope).Select(root => root.Id).Single();
            var names = new Stack<string>();
            for (var cursor = slot; cursor != scope; cursor = cursor.Parent) names.Push(cursor.Name);
            objects.Add(unchecked((long)entry.Key), new BlendshapeAppearanceObject(unchecked((long)entry.Key), scopeId, slot.Name, string.Join("/", names)));
        }
        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var entry in InitialAppearanceRuntime.CaptureRenderers(unityObjects, root.Slot))
            {
                if (!objects.TryGetValue(entry.Value.GameObjectId, out var owner) || owner.AvatarRootId != root.Id)
                {
                    plan.AddDiagnostic(AvatarOperationSeverity.Warning,
                        "Blendshape synchronization skipped: captured renderer has no verified owning avatar object.",
                        entry.Key.ToString(CultureInfo.InvariantCulture), warningCode);
                    return false;
                }
                renderers.Add(entry.Key, new BlendshapeAppearanceRenderer(entry.Key, entry.Value.GameObjectId, entry.Value.Shapes));
            }
        }
        return true;
    }

    internal static async Task ApplyAsync(string composedYaml, IReadOnlyDictionary<ulong, IUnityObject> unityObjects,
        IUnityStructureImporter importer, AvatarBuildPlan plan, CancellationToken cancellationToken)
    {
        await default(ToWorld);
        cancellationToken.ThrowIfCancellationRequested();
        if (plan.HasErrors) return;
        if (!TryCollectScope(unityObjects, importer, plan, "BLENDSHAPE_INITIAL_SCOPE_UNSUPPORTED",
            out var roots, out var objects, out var renderers, cancellationToken))
            return;
        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = BlendshapeInitialAppearance.Evaluate(composedYaml, objects, renderers, cancellationToken, root.Id);
            foreach (var diagnostic in result.Diagnostics)
                plan.AddDiagnostic(diagnostic.Severity == AvatarOperationSeverity.Info ? AvatarOperationSeverity.Info : AvatarOperationSeverity.Warning,
                    diagnostic.Message, diagnostic.SourceContext, diagnostic.Code);
            if (result.HasErrors) continue;
            var writes = new List<InitialAppearanceWrite>(result.Writes.Count);
            bool validScope = true;
            foreach (var write in result.Writes)
            {
                if (!renderers.TryGetValue(write.RendererId, out var renderer) ||
                    !objects.TryGetValue(renderer.GameObjectFileId, out var owner) || owner.AvatarRootId != root.Id)
                {
                    plan.AddDiagnostic(AvatarOperationSeverity.Warning,
                        "Blendshape initial synchronization skipped: destination has no verified owning avatar.",
                        write.RendererId.ToString(CultureInfo.InvariantCulture), "BLENDSHAPE_INITIAL_SCOPE_UNSUPPORTED");
                    validScope = false;
                    break;
                }
                writes.Add(new InitialAppearanceWrite(InitialAppearanceEffect.Shape, write.RendererId, write.Shape, Weight: write.UnityWeight));
            }
            if (!validScope || writes.Count == 0) continue;
            await InitialAppearanceRuntime.ApplyAsync(writes, root.Slot, unityObjects, importer, plan, cancellationToken,
                "Blendshape sync " + root.Id.ToString(CultureInfo.InvariantCulture));
        }
    }

    internal static async Task BuildRuntimeLinksAsync(string composedYaml, long avatarRootId, Slot avatarRoot,
        IReadOnlyDictionary<ulong, IUnityObject> unityObjects, IUnityStructureImporter importer,
        AvatarBuildPlan plan, CancellationToken cancellationToken)
    {
        await default(ToWorld);
        cancellationToken.ThrowIfCancellationRequested();
        if (plan.HasErrors || avatarRoot == null || avatarRoot.IsDestroyed || avatarRoot.GetComponentInParents<AvatarRoot>() == null) return;
        if (!TryCollectScope(unityObjects, importer, plan, "BLENDSHAPE_RUNTIME_SCOPE_UNSUPPORTED",
            out var roots, out var objects, out var renderers, cancellationToken))
            return;
        if (!roots.Any(root => root.Id == avatarRootId && root.Slot == avatarRoot)) return;
        var result = BlendshapeInitialAppearance.Evaluate(composedYaml, objects, renderers, cancellationToken, avatarRootId);
        if (result.HasErrors) return;
        var links = result.Links.Where(link => renderers.TryGetValue(link.TargetRendererId, out var target) &&
            objects.TryGetValue(target.GameObjectFileId, out var owner) && owner.AvatarRootId == avatarRootId).ToArray();
        if (links.Length == 0) return;

        var preflighted = new List<(SkinnedMeshRenderer SourceNative, int SourceIndex, IField<float> SourceField, SkinnedMeshRenderer TargetNative, int TargetIndex, IField<float> TargetField)>();
        var destinations = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var link in links)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (link.HasRemapCurve)
            {
                plan.AddDiagnostic(AvatarOperationSeverity.Warning,
                    "Continuous runtime blendshape sync skipped: remap curves cannot be represented by direct ValueCopy<float> links; static initial weights were retained.",
                    link.ComponentId.ToString(CultureInfo.InvariantCulture), "BLENDSHAPE_RUNTIME_SYNC_SKIPPED");
                return;
            }
            if (!unityObjects.TryGetValue(unchecked((ulong)link.SourceRendererId), out var sourceObj) || sourceObj is not UnitySkin sourceSkin ||
                sourceSkin.IsStaticMesh || sourceSkin.createdMeshRenderer == null || sourceSkin.createdMeshRenderer.IsDestroyed ||
                !PreparedImportItem.Contains(avatarRoot, sourceSkin.createdMeshRenderer.Slot) ||
                !unityObjects.TryGetValue(unchecked((ulong)link.TargetRendererId), out var targetObj) || targetObj is not UnitySkin targetSkin ||
                targetSkin.IsStaticMesh || targetSkin.createdMeshRenderer == null || targetSkin.createdMeshRenderer.IsDestroyed ||
                !PreparedImportItem.Contains(avatarRoot, targetSkin.createdMeshRenderer.Slot))
            {
                plan.AddDiagnostic(AvatarOperationSeverity.Warning,
                    "Continuous runtime blendshape sync skipped: a source or destination skinned renderer is missing or outside the owning avatar; static initial weights were retained.",
                    link.ComponentId.ToString(CultureInfo.InvariantCulture), "BLENDSHAPE_RUNTIME_SYNC_SKIPPED");
                return;
            }
            var sourceNative = sourceSkin.createdMeshRenderer;
            var targetNative = targetSkin.createdMeshRenderer;
            int sourceIndex = sourceNative.BlendShapeIndex(link.SourceShape);
            int targetIndex = targetNative.BlendShapeIndex(link.TargetShape);
            if (targetIndex < 0 || targetIndex >= targetNative.BlendShapeWeights.Count)
            {
                plan.AddDiagnostic(AvatarOperationSeverity.Warning,
                    "Continuous runtime blendshape sync skipped: destination blendshape '" + link.TargetShape + "' is unavailable on the native mesh; static initial weights were retained.",
                    link.ComponentId.ToString(CultureInfo.InvariantCulture), "BLENDSHAPE_RUNTIME_SYNC_SKIPPED");
                return;
            }
            if (sourceIndex < 0 || sourceIndex >= sourceNative.BlendShapeWeights.Count)
            {
                plan.AddDiagnostic(AvatarOperationSeverity.Warning,
                    "Continuous runtime blendshape sync skipped: source blendshape '" + link.SourceShape + "' was stripped by mesh import; static initial weights were retained.",
                    link.ComponentId.ToString(CultureInfo.InvariantCulture), "BLENDSHAPE_RUNTIME_SYNC_SKIPPED");
                return;
            }
            var sourceField = sourceNative.BlendShapeWeights.GetElement(sourceIndex);
            var targetField = targetNative.BlendShapeWeights.GetElement(targetIndex);
            if (ReferenceEquals(sourceField, targetField) || targetField.IsDriven || targetField.IsLinked || !destinations.Add(targetField))
            {
                plan.AddDiagnostic(AvatarOperationSeverity.Warning,
                    "Continuous runtime blendshape sync skipped: destination blendshape '" + link.TargetShape + "' is already driven or linked; static initial weights were retained.",
                    link.ComponentId.ToString(CultureInfo.InvariantCulture), "BLENDSHAPE_RUNTIME_SYNC_SKIPPED");
                return;
            }
            preflighted.Add((sourceNative, sourceIndex, sourceField, targetNative, targetIndex, targetField));
        }

        var pending = new List<(Action Apply, Action Restore, Func<bool> Valid)>();
        var owned = new OwnedAttachmentTransaction<Slot>();
        bool committed = false;
        try
        {
            Slot syncSlot = avatarRoot.AddSlot("Blendshape sync");
            owned.Track(syncSlot);
            foreach (var item in preflighted)
            {
                float before = item.TargetField.Value;
                ValueCopy<float>? copy = null;
                pending.Add((() =>
                {
                    copy = syncSlot.AttachComponent<ValueCopy<float>>();
                    copy.WriteBack.Value = false;
                    copy.Source.Target = item.SourceField;
                    copy.Target.ForceLink(item.TargetField);
                }, () =>
                {
                    if (copy != null && !copy.IsDestroyed) copy.Destroy();
                    item.TargetField.Value = before;
                }, () => !item.SourceNative.IsDestroyed && !item.TargetNative.IsDestroyed &&
                    PreparedImportItem.Contains(avatarRoot, item.SourceNative.Slot) && PreparedImportItem.Contains(avatarRoot, item.TargetNative.Slot) &&
                    item.SourceIndex < item.SourceNative.BlendShapeWeights.Count && ReferenceEquals(item.SourceNative.BlendShapeWeights.GetElement(item.SourceIndex), item.SourceField) &&
                    item.TargetIndex < item.TargetNative.BlendShapeWeights.Count && ReferenceEquals(item.TargetNative.BlendShapeWeights.GetElement(item.TargetIndex), item.TargetField) &&
                    !item.TargetField.IsDriven && !item.TargetField.IsLinked));
            }
            InitialAppearanceTransaction.Apply(pending, cancellationToken);
            committed = true;
            owned.Commit();
            plan.AddDiagnostic(AvatarOperationSeverity.Info,
                "Attached " + pending.Count.ToString(CultureInfo.InvariantCulture) + " continuous ValueCopy<float> blendshape synchronization link(s).",
                avatarRootId.ToString(CultureInfo.InvariantCulture), "BLENDSHAPE_RUNTIME_SYNC_BUILT");
        }
        catch (Exception failure)
        {
            var failures = new List<Exception> { failure };
            failures.AddRange(owned.RollBack(slot => { if (!slot.IsDestroyed) slot.Destroy(); }));
            if (committed)
                for (int index = pending.Count - 1; index >= 0; index--)
                    try { pending[index].Restore(); }
                    catch (Exception restore) { failures.Add(restore); }
            if (failures.Count > 1) throw new AggregateException(failures);
            throw;
        }
    }
}
