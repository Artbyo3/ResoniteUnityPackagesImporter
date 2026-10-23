#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FrooxEngine;
using UnityPackageImporter.FrooxEngineRepresentation;
using UnityGameObject = UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes.GameObject;
using UnitySkin = UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes.SkinnedMeshRenderer;

namespace UnityPackageImporter.Models;

internal static class InitialAppearanceRuntime
{
    private static bool Owned(UnitySkin renderer, IReadOnlyDictionary<ulong, IUnityObject> objects, Slot root) =>
        root != null && !root.IsDestroyed && renderer.createdMeshRenderer != null && !renderer.createdMeshRenderer.IsDestroyed &&
        renderer.m_GameObject?.TryGetValue("fileID", out ulong owner) == true &&
        objects.TryGetValue(owner, out var entry) && entry is UnityGameObject game && game.frooxEngineSlot != null &&
        PreparedImportItem.Contains(root, game.frooxEngineSlot) && PreparedImportItem.Contains(game.frooxEngineSlot, renderer.createdMeshRenderer.Slot);

    internal static IReadOnlyDictionary<long, InitialAppearanceRenderer> CaptureRenderers(
        IReadOnlyDictionary<ulong, IUnityObject> unityObjects, Slot avatarRoot)
    {
        var result = new Dictionary<long, InitialAppearanceRenderer>();
        var wrappers = unityObjects.Values.OfType<UnitySkin>().Distinct().Where(renderer => Owned(renderer, unityObjects, avatarRoot)).ToArray();
        foreach (var renderer in wrappers)
        {
            if (wrappers.Count(other => other.createdMeshRenderer == renderer.createdMeshRenderer) != 1) continue;
            var names = renderer.IsStaticMesh ? Array.Empty<string>() : renderer.SourceBlendShapeNames ?? Array.Empty<string>();
            if (names.Any(string.IsNullOrEmpty) || names.Distinct(StringComparer.Ordinal).Count() != names.Length) continue;
            var native = renderer.createdMeshRenderer;
            var current = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (string name in names)
            {
                int index = native.BlendShapeIndex(name);
                if (index >= 0 && index < native.BlendShapeWeights.Count) current.Add(name, native.BlendShapeWeights[index] * 100f);
            }
            var shapes = BlendshapeInitialAppearance.CaptureSourceWeights(names, renderer.m_BlendShapeWeights, current);
            var materials = Enumerable.Range(0, native.Materials.Count).Select(index =>
                renderer.m_Materials != null && index < renderer.m_Materials.Count && renderer.m_Materials[index] != null
                    ? new InitialAppearanceMaterial(renderer.m_Materials[index].fileID, renderer.m_Materials[index].guid ?? "")
                    : new InitialAppearanceMaterial(0, "")).ToArray();
            result.Add(unchecked((long)renderer.id), new(unchecked((long)renderer.m_GameObject!["fileID"]), shapes, materials));
        }
        return result;
    }

    internal static async Task<bool> ApplyAsync(IReadOnlyList<InitialAppearanceWrite> writes, Slot avatarRoot,
        IReadOnlyDictionary<ulong, IUnityObject> unityObjects, IUnityStructureImporter importer,
        AvatarBuildPlan plan, CancellationToken token, string context)
    {
        token.ThrowIfCancellationRequested();
        await default(ToWorld);
        if (avatarRoot == null || avatarRoot.IsDestroyed || importer.CurrentStructureRootSlot == null ||
            !PreparedImportItem.Contains(importer.CurrentStructureRootSlot, avatarRoot))
        {
            plan.AddDiagnostic(AvatarOperationSeverity.Warning, context + ": initial snapshot skipped: avatar root is not owned by this import structure.", code: "INITIAL_RUNTIME_PREFLIGHT");
            return false;
        }
        var prepared = new List<(InitialAppearanceWrite Write, string? Path)>();
        foreach (var write in writes)
        {
            token.ThrowIfCancellationRequested();
            if (write.Effect != InitialAppearanceEffect.Material) { prepared.Add((write, null)); continue; }
            var material = write.Material;
            string? path = null;
            if (material != null && material.FileId != 0 && Regex.IsMatch(material.Guid, @"\A[0-9a-fA-F]{32}\z") &&
                importer.unityProjectImporter.AssetIDDict.TryGetValue(material.Guid, out path) &&
                string.Equals(System.IO.Path.GetExtension(path), ".mat", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            {
                await default(ToBackground);
                string yaml = await File.ReadAllTextAsync(path, token);
                if (ModularAvatarInitialAppearance.IsExactMaterialAsset(yaml, material.FileId))
                {
                    UnityMaterialData.Parse(yaml);
                    prepared.Add((write, path));
                    continue;
                }
            }
            plan.AddDiagnostic(AvatarOperationSeverity.Warning,
                context + ": material entry " + (material?.Guid ?? "<null>") + ":" + (material?.FileId.ToString(CultureInfo.InvariantCulture) ?? "<null>") +
                " skipped: only exact external .mat GUID/fileID identities are supported. Null clears cannot be represented by the existing material wrapper; no fallback/subasset was substituted.",
                write.TargetId.ToString(CultureInfo.InvariantCulture), "INITIAL_MATERIAL_REFERENCE_SKIPPED");
        }
        await default(ToWorld);
        token.ThrowIfCancellationRequested();
        var identities = prepared.Select(item => ModularAvatarInitialAppearance.InitialWriteIdentity(item.Write)).ToArray();
        foreach (var identity in identities)
            if (ModularAvatarInitialAppearance.InitialWriteConflicts(plan.InitialAppearanceValues, identity))
            {
                plan.AddDiagnostic(AvatarOperationSeverity.Warning, context + ": initial snapshot skipped: a prior framework snapshot assigned a different value to the same owned destination. No framework precedence was inferred.",
                    identity.Key.Target.ToString(CultureInfo.InvariantCulture), "INITIAL_FRAMEWORK_CONFLICT");
                return false;
            }
        var snapshots = CaptureRenderers(unityObjects, avatarRoot);
        var destinations = new HashSet<object>();
        var pending = new List<(Action Apply, Action Restore, Func<bool> Valid)>();
        var materialTasks = new Dictionary<(string Guid, long Id), FileImportHelperTaskMaterial>();
        var materialMetadata = new Dictionary<UnitySkin, InitialAppearanceMaterialMetadata<SourceObj>>();
        bool Reject()
        {
            plan.AddDiagnostic(AvatarOperationSeverity.Warning, context + ": initial writes skipped: destination missing, ambiguous, driven or outside owned avatar; no destination writes applied.", code: "INITIAL_RUNTIME_PREFLIGHT");
            return false;
        }
        foreach (var item in prepared)
        {
            var write = item.Write;
            if (!unityObjects.TryGetValue(unchecked((ulong)write.TargetId), out var ownedSource) ||
                !importer.existingIUnityObjects.TryGetValue(unchecked((ulong)write.TargetId), out var importedSource) ||
                !ReferenceEquals(ownedSource, importedSource)) return Reject();
            if (write.Effect == InitialAppearanceEffect.Active)
            {
                if (!unityObjects.TryGetValue(unchecked((ulong)write.TargetId), out var entry) || entry is not UnityGameObject game ||
                    game.frooxEngineSlot == null || !PreparedImportItem.Contains(avatarRoot, game.frooxEngineSlot)) return Reject();
                var slot = game.frooxEngineSlot;
                var field = slot.ActiveSelf_Field;
                if (field.IsLinked || field.IsDriven || !destinations.Add(field)) return Reject();
                bool before = slot.ActiveSelf;
                pending.Add((() => slot.ActiveSelf = write.Active, () => slot.ActiveSelf = before,
                    () => !slot.IsDestroyed && PreparedImportItem.Contains(avatarRoot, slot) && slot.ActiveSelf == before && !field.IsLinked && !field.IsDriven));
                continue;
            }
            if (!unityObjects.TryGetValue(unchecked((ulong)write.TargetId), out var source) || source is not UnitySkin renderer ||
                !snapshots.ContainsKey(write.TargetId) || !Owned(renderer, unityObjects, avatarRoot)) return Reject();
            var native = renderer.createdMeshRenderer;
            if (write.Effect == InitialAppearanceEffect.Shape)
            {
                int index = native.BlendShapeIndex(write.Property);
                if (renderer.IsStaticMesh || !snapshots[write.TargetId].Shapes.ContainsKey(write.Property) ||
                    index < 0 || index >= native.BlendShapeWeights.Count || !float.IsFinite(write.Weight / 100f)) return Reject();
                var field = native.BlendShapeWeights.GetElement(index);
                if (field.IsLinked || field.IsDriven || !destinations.Add(field)) return Reject();
                float before = field.Value;
                if (!float.IsFinite(before)) return Reject();
                pending.Add((() => field.Value = write.Weight / 100f, () => field.Value = before,
                    () => Owned(renderer, unityObjects, avatarRoot) && native.BlendShapeIndex(write.Property) == index &&
                        index < native.BlendShapeWeights.Count && ReferenceEquals(native.BlendShapeWeights.GetElement(index), field) && field.Value == before && !field.IsLinked && !field.IsDriven));
            }
            else if (write.Effect == InitialAppearanceEffect.Material)
            {
                if (renderer.materials == null ||
                    !int.TryParse(write.Property, NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index < 0 ||
                    index >= native.Materials.Count || index >= renderer.materials.Count) return Reject();
                var field = native.Materials.GetElement(index);
                if (field.IsLinked || field.IsDriven || !destinations.Add(field)) return Reject();
                var before = field.Target;
                var previousTask = renderer.materials[index];
                if (!materialMetadata.TryGetValue(renderer, out var metadata))
                {
                    metadata = new InitialAppearanceMaterialMetadata<SourceObj>(renderer.m_Materials, native.Materials.Count);
                    materialMetadata.Add(renderer, metadata);
                }
                var material = write.Material!;
                var key = (material.Guid, material.FileId);
                if (!materialTasks.TryGetValue(key, out var task))
                {
                    task = new FileImportHelperTaskMaterial(material.Guid, item.Path!, importer.unityProjectImporter);
                    materialTasks.Add(key, task);
                }
                metadata.Replacement[index] = new SourceObj { fileID = material.FileId, guid = material.Guid, type = 2 };
                pending.Add((() => { field.Target = task.finalMaterial; renderer.materials[index] = task; },
                    () => { try { field.Target = before; } finally { renderer.materials[index] = previousTask; } },
                    () => Owned(renderer, unityObjects, avatarRoot) && index < native.Materials.Count && ReferenceEquals(native.Materials.GetElement(index), field) &&
                        index < renderer.materials.Count && ReferenceEquals(field.Target, before) &&
                        ReferenceEquals(renderer.materials[index], previousTask) && metadata.IsUnchanged(renderer.m_Materials) && !field.IsLinked && !field.IsDriven));
            }
            else return Reject();
        }
        foreach (var entry in materialMetadata)
        {
            var renderer = entry.Key;
            var metadata = entry.Value;
            pending.Add((() => renderer.m_Materials = metadata.Replacement, () => renderer.m_Materials = metadata.Original!,
                () => Owned(renderer, unityObjects, avatarRoot) && metadata.IsUnchanged(renderer.m_Materials)));
        }
        foreach (var task in materialTasks.Values)
        {
            token.ThrowIfCancellationRequested();
            var provider = await task.runImportFileMaterialsAsync();
            await default(ToWorld);
            if (provider is not Component component || component.IsDestroyed ||
                !PreparedImportItem.Contains(importer.unityProjectImporter.importTaskAssetRoot, component.Slot)) return Reject();
        }
        await default(ToWorld);
        token.ThrowIfCancellationRequested();
        if (!PreparedImportItem.Contains(importer.CurrentStructureRootSlot, avatarRoot) || pending.Any(write => !write.Valid())) return Reject();
        foreach (var identity in identities)
            if (ModularAvatarInitialAppearance.InitialWriteConflicts(plan.InitialAppearanceValues, identity))
            {
                plan.AddDiagnostic(AvatarOperationSeverity.Warning, context + ": initial snapshot skipped: a conflicting framework snapshot completed during material resolution.",
                    identity.Key.Target.ToString(CultureInfo.InvariantCulture), "INITIAL_FRAMEWORK_CONFLICT");
                return false;
            }
        InitialAppearanceTransaction.Apply(pending, token);
        foreach (var identity in identities) plan.InitialAppearanceValues[identity.Key] = identity.Value;
        if (pending.Count > 0) plan.AddDiagnostic(AvatarOperationSeverity.Info,
            context + ": applied " + prepared.Count.ToString(CultureInfo.InvariantCulture) + " reversible INITIAL writes; no runtime animator/menu parity claimed.", code: "INITIAL_APPLIED");
        return true;
    }
}
