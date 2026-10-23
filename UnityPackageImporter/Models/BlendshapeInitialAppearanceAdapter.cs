using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FrooxEngine;
using UnityPackageImporter.FrooxEngineRepresentation;
using UnityGameObject = UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes.GameObject;

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
            .Where(root => root.Slot != null && !root.Slot.IsDestroyed && PreparedImportItem.Contains(stage, root.Slot)).ToArray();
        if (roots.Length == 0)
        {
            plan.AddDiagnostic(AvatarOperationSeverity.Warning,
                "Blendshape initial synchronization skipped: no verified assembled avatar root in this import structure.",
                code: "BLENDSHAPE_INITIAL_SCOPE_UNSUPPORTED");
            return;
        }
        if (roots.Any(root => roots.Count(other => PreparedImportItem.Contains(root.Slot, other.Slot) ||
            PreparedImportItem.Contains(other.Slot, root.Slot)) != 1))
        {
            plan.AddDiagnostic(AvatarOperationSeverity.Warning,
                "Blendshape initial synchronization skipped: overlapping or duplicate avatar ownership; no staging-root fallback was used.",
                code: "BLENDSHAPE_INITIAL_SCOPE_UNSUPPORTED");
            return;
        }
        var objects = new Dictionary<long, BlendshapeAppearanceObject>();
        foreach (var entry in unityObjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Value is not UnityGameObject game || game.frooxEngineSlot == null || game.frooxEngineSlot.IsDestroyed) continue;
            var slot = game.frooxEngineSlot;
            if (!PreparedImportItem.Contains(stage, slot)) continue;
            var scope = AvatarConversionScope.Unique(roots.Where(root => PreparedImportItem.Contains(root.Slot, slot)).Select(root => root.Slot));
            if (scope == null) continue;
            long scopeId = roots.Where(root => root.Slot == scope).Select(root => root.Id).Single();
            var names = new Stack<string>();
            for (var cursor = slot; cursor != scope; cursor = cursor.Parent) names.Push(cursor.Name);
            objects.Add(unchecked((long)entry.Key), new BlendshapeAppearanceObject(unchecked((long)entry.Key), scopeId, slot.Name, string.Join("/", names)));
        }
        var renderers = new Dictionary<long, BlendshapeAppearanceRenderer>();
        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var entry in InitialAppearanceRuntime.CaptureRenderers(unityObjects, root.Slot))
            {
                if (!objects.TryGetValue(entry.Value.GameObjectId, out var owner) || owner.AvatarRootId != root.Id)
                {
                    plan.AddDiagnostic(AvatarOperationSeverity.Warning,
                        "Blendshape initial synchronization skipped: captured renderer has no verified owning avatar object.",
                        entry.Key.ToString(CultureInfo.InvariantCulture), "BLENDSHAPE_INITIAL_SCOPE_UNSUPPORTED");
                    return;
                }
                renderers.Add(entry.Key, new BlendshapeAppearanceRenderer(entry.Key, entry.Value.GameObjectId, entry.Value.Shapes));
            }
        }
        var result = BlendshapeInitialAppearance.Evaluate(composedYaml, objects, renderers, cancellationToken);
        plan.Diagnostics.AddRange(result.Diagnostics);
        if (result.HasErrors || plan.HasErrors) return;
        foreach (var write in result.Writes)
        {
            if (!renderers.TryGetValue(write.RendererId, out var renderer) ||
                !objects.TryGetValue(renderer.GameObjectFileId, out var owner) || !roots.Any(root => root.Id == owner.AvatarRootId))
            {
                plan.AddDiagnostic(AvatarOperationSeverity.Warning,
                    "Blendshape initial synchronization skipped: destination has no verified owning avatar.",
                    write.RendererId.ToString(CultureInfo.InvariantCulture), "BLENDSHAPE_INITIAL_SCOPE_UNSUPPORTED");
                return;
            }
        }
        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var writes = result.Writes.Where(write => objects[renderers[write.RendererId].GameObjectFileId].AvatarRootId == root.Id)
                .Select(write => new InitialAppearanceWrite(InitialAppearanceEffect.Shape, write.RendererId, write.Shape, Weight: write.UnityWeight)).ToArray();
            if (writes.Length == 0) continue;
            await InitialAppearanceRuntime.ApplyAsync(writes, root.Slot, unityObjects, importer, plan, cancellationToken,
                "Blendshape sync " + root.Id.ToString(CultureInfo.InvariantCulture));
        }
    }
}
