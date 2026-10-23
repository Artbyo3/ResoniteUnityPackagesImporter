using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FrooxEngine;
using UnityPackageImporter.FrooxEngineRepresentation;
using UnityGameObject = UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes.GameObject;
using UnitySkin = UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes.SkinnedMeshRenderer;

namespace UnityPackageImporter.Models;

internal static class BlendshapeInitialAppearanceAdapter
{
    internal static async Task ApplyAsync(string composedYaml, IReadOnlyDictionary<ulong, IUnityObject> unityObjects,
        IUnityStructureImporter importer, AvatarBuildPlan plan, CancellationToken cancellationToken)
    {
        await default(ToWorld);
        cancellationToken.ThrowIfCancellationRequested();
        if (plan.HasErrors) return;
        var manifest = importer is UnityPrefabImportTask prefab ? prefab.Manifest : null;
        var stage = importer.CurrentStructureRootSlot;
        var roots = (manifest?.Avatars ?? Array.Empty<VrcAvatarDefinition>()).Select(avatar =>
            (Id: avatar.GameObjectFileId, Slot: unityObjects.TryGetValue(unchecked((ulong)avatar.GameObjectFileId), out var entry) && entry is UnityGameObject game ? game.frooxEngineSlot : null))
            .Where(root => root.Slot != null && !root.Slot.IsDestroyed).ToArray();
        var objects = new Dictionary<long, BlendshapeAppearanceObject>();
        foreach (var entry in unityObjects)
        {
            if (entry.Value is not UnityGameObject game || game.frooxEngineSlot == null || game.frooxEngineSlot.IsDestroyed) continue;
            var slot = game.frooxEngineSlot;
            if (!PreparedImportItem.Contains(stage, slot)) continue;
            var scope = AvatarConversionScope.RootFor(slot, stage, roots.Select(root => root.Slot).ToArray(),
                (child, root) => PreparedImportItem.Contains(root, child));
            if (scope == null) continue;
            long scopeId = roots.Where(root => root.Slot == scope).Select(root => root.Id).SingleOrDefault();
            var names = new Stack<string>();
            for (var cursor = slot; cursor != scope; cursor = cursor.Parent) names.Push(cursor.Name);
            objects.Add(unchecked((long)entry.Key), new BlendshapeAppearanceObject(unchecked((long)entry.Key), scopeId, slot.Name, string.Join("/", names)));
        }
        var skins = unityObjects.Values.OfType<UnitySkin>().Distinct().Where(skin => !skin.IsStaticMesh && skin.createdMeshRenderer != null &&
            !skin.createdMeshRenderer.IsDestroyed && PreparedImportItem.Contains(stage, skin.createdMeshRenderer.Slot)).ToDictionary(skin => unchecked((long)skin.id));
        var renderers = new Dictionary<long, BlendshapeAppearanceRenderer>();
        foreach (var entry in skins)
        {
            if (entry.Value.m_GameObject == null || !entry.Value.m_GameObject.TryGetValue("fileID", out ulong ownerId)) continue;
            var native = entry.Value.createdMeshRenderer;
            var values = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (string name in entry.Value.SourceBlendShapeNames ?? Array.Empty<string>())
            {
                int index = native.BlendShapeIndex(name);
                if (index >= 0 && !values.TryAdd(name, native.BlendShapeWeights[index] * 100f))
                    throw new InvalidOperationException("Ambiguous source blendshape names for renderer " + entry.Key.ToString(CultureInfo.InvariantCulture));
            }
            var sourceValues = BlendshapeInitialAppearance.CaptureSourceWeights(entry.Value.SourceBlendShapeNames ?? Array.Empty<string>(),
                entry.Value.m_BlendShapeWeights, values);
            renderers.Add(entry.Key, new BlendshapeAppearanceRenderer(entry.Key, unchecked((long)ownerId), sourceValues));
        }
        var result = BlendshapeInitialAppearance.Evaluate(composedYaml, objects, renderers, cancellationToken);
        plan.Diagnostics.AddRange(result.Diagnostics);
        if (result.HasErrors || plan.HasErrors) return;
        var writes = result.Writes.Where(write => skins[write.RendererId].createdMeshRenderer.BlendShapeIndex(write.Shape) >= 0).Select(write =>
        {
            var native = skins[write.RendererId].createdMeshRenderer;
            int index = native.BlendShapeIndex(write.Shape);
            if (index < 0 || !float.IsFinite(write.UnityWeight / 100f))
                throw new InvalidOperationException("A preflighted blendshape destination became unavailable.");
            return (Native: native, Index: index, Before: native.BlendShapeWeights[index], After: write.UnityWeight / 100f);
        }).ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            foreach (var write in writes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                write.Native.BlendShapeWeights[write.Index] = write.After;
            }
        }
        catch (Exception)
        {
            foreach (var write in writes.Reverse())
                if (!write.Native.IsDestroyed) write.Native.BlendShapeWeights[write.Index] = write.Before;
            throw;
        }
    }
}
