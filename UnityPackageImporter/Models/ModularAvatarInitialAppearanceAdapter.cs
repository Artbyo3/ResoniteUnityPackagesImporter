#nullable enable
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

/// <summary>Bounded INITIAL Active/Shape adapter. Unsupported contexts skip with diagnostics, never fake application.</summary>
public static class ModularAvatarInitialAppearanceAdapter
{
    public static InitialAppearanceResult EvaluateAndReport(string composedYaml, InitialAppearanceContext context,
        AvatarBuildPlan plan, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var result = ModularAvatarInitialAppearance.Evaluate(composedYaml, context, token);
        foreach (var diagnostic in result.Diagnostics)
            plan.AddDiagnostic(diagnostic.Severity switch
            {
                InitialAppearanceSeverity.Error => AvatarOperationSeverity.Error,
                InitialAppearanceSeverity.Warning => AvatarOperationSeverity.Warning,
                _ => AvatarOperationSeverity.Info
            }, diagnostic.Message, diagnostic.SourceId.ToString(System.Globalization.CultureInfo.InvariantCulture), diagnostic.Code);
        return result;
    }

    public static async Task ApplyAsync(string composedYaml, IReadOnlyDictionary<ulong, IUnityObject> unityObjects,
        IUnityStructureImporter importer, AvatarBuildPlan plan, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(unityObjects);
        ArgumentNullException.ThrowIfNull(importer);
        ArgumentNullException.ThrowIfNull(plan);
        token.ThrowIfCancellationRequested();
        if (!ModularAvatarInitialAppearance.ContainsReactiveComponents(composedYaml, token)) return;
        await default(ToWorld);
        token.ThrowIfCancellationRequested();
        if (plan.HasErrors) return;
        plan.AddDiagnostic(AvatarOperationSeverity.Warning,
            "MA INITIAL Active/Shape snapshots only for assembled avatar roots. No runtime menus, animations, drivers, " +
            "scene-wide or standalone-outfit conversion is claimed. Material writes and ShapeChanger Delete are unsupported.", code: "MA_INITIAL_STATIC_ONLY");
        var manifest = importer is UnityPrefabImportTask prefab ? prefab.Manifest : null;
        var stage = importer.CurrentStructureRootSlot;
        var roots = (manifest?.Avatars ?? Array.Empty<VrcAvatarDefinition>()).Select(avatar =>
            (Avatar: avatar, Slot: unityObjects.TryGetValue(unchecked((ulong)avatar.GameObjectFileId), out var entry) && entry is UnityGameObject game ? game.frooxEngineSlot : null))
            .Where(root => root.Slot != null && !root.Slot.IsDestroyed && PreparedImportItem.Contains(stage, root.Slot)).ToArray();
        if (roots.Length == 0)
        {
            plan.AddDiagnostic(AvatarOperationSeverity.Warning, "MA initial appearance skipped: no verified assembled avatar root in this prefab scope.", code: "MA_INITIAL_SCOPE_UNSUPPORTED");
            return;
        }
        var skins = unityObjects.Values.OfType<UnitySkin>().Distinct().Where(skin => !skin.IsStaticMesh && skin.createdMeshRenderer != null &&
            !skin.createdMeshRenderer.IsDestroyed && PreparedImportItem.Contains(stage, skin.createdMeshRenderer.Slot)).ToDictionary(skin => unchecked((long)skin.id));
        foreach (var root in roots)
        {
            token.ThrowIfCancellationRequested();
            string rootContext = root.Avatar.GameObjectFileId.ToString(CultureInfo.InvariantCulture);
            if (roots.Any(other => other.Avatar.GameObjectFileId != root.Avatar.GameObjectFileId &&
                (PreparedImportItem.Contains(root.Slot, other.Slot) || PreparedImportItem.Contains(other.Slot, root.Slot))))
            {
                plan.AddDiagnostic(AvatarOperationSeverity.Warning, "MA initial appearance skipped: overlapping avatar ownership.", rootContext, "MA_INITIAL_SCOPE_UNSUPPORTED");
                continue;
            }
            bool externalContextAbsent = !(root.Avatar.ExpressionParameters?.HasGuid ?? false) &&
                !root.Avatar.BaseAnimationLayers.Any(layer => layer.AnimatorController?.HasGuid ?? false);
            var snapshots = new Dictionary<long, InitialAppearanceRenderer>();
            bool ambiguousNames = false;
            foreach (var entry in skins)
            {
                var skin = entry.Value;
                if (!PreparedImportItem.Contains(root.Slot, skin.createdMeshRenderer.Slot) || skin.m_GameObject == null ||
                    !skin.m_GameObject.TryGetValue("fileID", out ulong ownerId)) continue;
                var sourceNames = skin.SourceBlendShapeNames ?? Array.Empty<string>();
                if (sourceNames.Any(string.IsNullOrEmpty) || sourceNames.Distinct(StringComparer.Ordinal).Count() != sourceNames.Length)
                {
                    ambiguousNames = true;
                    continue;
                }
                var values = new Dictionary<string, float>(StringComparer.Ordinal);
                foreach (string name in sourceNames)
                {
                    int index = skin.createdMeshRenderer.BlendShapeIndex(name);
                    if (index >= 0 && !values.TryAdd(name, skin.createdMeshRenderer.BlendShapeWeights[index] * 100f)) ambiguousNames = true;
                }
                var sourceValues = BlendshapeInitialAppearance.CaptureSourceWeights(sourceNames, skin.m_BlendShapeWeights, values);
                snapshots.Add(entry.Key, new(unchecked((long)ownerId), sourceValues,
                    (skin.m_Materials ?? new List<SourceObj>()).Select(material => new InitialAppearanceMaterial(material?.fileID ?? 0, material?.guid ?? "")).ToArray()));
            }
            if (ambiguousNames)
            {
                plan.AddDiagnostic(AvatarOperationSeverity.Warning, "MA initial appearance skipped: ambiguous imported blendshape names.", rootContext, "MA_INITIAL_RENDERER");
                continue;
            }
            var result = ModularAvatarInitialAppearance.Evaluate(composedYaml, new()
            {
                AvatarRootId = root.Avatar.GameObjectFileId, Renderers = snapshots,
                ExternalAnimationAndParameterContextVerifiedAbsent = externalContextAbsent
            }, token);
            foreach (var diagnostic in result.Diagnostics)
                plan.AddDiagnostic(diagnostic.Severity == InitialAppearanceSeverity.Info ? AvatarOperationSeverity.Info : AvatarOperationSeverity.Warning,
                    (result.CanApply ? "" : "MA initial snapshot skipped: ") + diagnostic.Message,
                    diagnostic.SourceId.ToString(CultureInfo.InvariantCulture), diagnostic.Code);
            if (!result.CanApply) continue;
            if (result.Writes.Any(write => write.Effect == InitialAppearanceEffect.Material))
            {
                plan.AddDiagnostic(AvatarOperationSeverity.Warning, "MA initial snapshot skipped: MaterialSetter needs existing material-importer preflight and transactional material assignment; no material or partial Active/Shape writes were applied.",
                    rootContext, "MA_INITIAL_MATERIAL_UNSUPPORTED");
                continue;
            }
            var writes = new List<(Action Apply, Action Restore)>();
            bool valid = true;
            var activeTargets = new HashSet<Slot>();
            var shapeTargets = new HashSet<(FrooxEngine.SkinnedMeshRenderer Renderer, int Index)>();
            foreach (var write in result.Writes)
            {
                if (write.Effect == InitialAppearanceEffect.Active)
                {
                    if (!unityObjects.TryGetValue(unchecked((ulong)write.TargetId), out var entry) || entry is not UnityGameObject game ||
                        !PreparedImportItem.Contains(root.Slot, game.frooxEngineSlot) || game.frooxEngineSlot.ActiveSelf_Field.IsLinked || !activeTargets.Add(game.frooxEngineSlot))
                    {
                        valid = false;
                        break;
                    }
                    var slot = game.frooxEngineSlot;
                    bool before = slot.ActiveSelf;
                    writes.Add((() => slot.ActiveSelf = write.Active, () => { if (!slot.IsDestroyed) slot.ActiveSelf = before; }));
                }
                else if (write.Effect == InitialAppearanceEffect.Shape)
                {
                    if (!skins.TryGetValue(write.TargetId, out var skin) || !snapshots.ContainsKey(write.TargetId) ||
                        !PreparedImportItem.Contains(root.Slot, skin.createdMeshRenderer.Slot))
                    {
                        valid = false;
                        break;
                    }
                    var native = skin.createdMeshRenderer;
                    int index = native.BlendShapeIndex(write.Property);
                    if (index < 0 || !float.IsFinite(write.Weight / 100f) || !shapeTargets.Add((native, index)))
                    {
                        valid = false;
                        break;
                    }
                    float before = native.BlendShapeWeights[index];
                    writes.Add((() => native.BlendShapeWeights[index] = write.Weight / 100f,
                        () => { if (!native.IsDestroyed) native.BlendShapeWeights[index] = before; }));
                }
            }
            if (!valid)
            {
                plan.AddDiagnostic(AvatarOperationSeverity.Warning, "MA initial snapshot skipped: a destination is missing, native-stripped, ambiguous, driven or outside the owned avatar. No writes applied.", rootContext, "MA_INITIAL_RUNTIME_PREFLIGHT");
                continue;
            }
            token.ThrowIfCancellationRequested();
            int attempted = 0;
            try
            {
                foreach (var write in writes)
                {
                    token.ThrowIfCancellationRequested();
                    attempted++;
                    write.Apply();
                }
            }
            catch (Exception failure)
            {
                var rollbackFailures = new List<Exception>();
                for (int index = attempted - 1; index >= 0; index--)
                    try { writes[index].Restore(); }
                    catch (Exception rollbackFailure) { rollbackFailures.Add(rollbackFailure); }
                if (rollbackFailures.Count > 0) throw new AggregateException(new[] { failure }.Concat(rollbackFailures));
                throw;
            }
            if (writes.Count > 0)
                plan.AddDiagnostic(AvatarOperationSeverity.Info,
                    "Applied " + activeTargets.Count.ToString(CultureInfo.InvariantCulture) + " MA INITIAL activation and " +
                    shapeTargets.Count.ToString(CultureInfo.InvariantCulture) + " shape snapshot writes; no runtime conversion was performed.",
                    rootContext, "MA_INITIAL_APPLIED");
        }
    }
}
