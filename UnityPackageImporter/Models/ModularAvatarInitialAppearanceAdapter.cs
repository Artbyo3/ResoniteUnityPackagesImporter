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

namespace UnityPackageImporter.Models;

/// <summary>Bounded reversible INITIAL adapter. Unsupported contexts skip with diagnostics, never fake application.</summary>
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
            "MA reversible INITIAL snapshots only for assembled avatar roots. No runtime menus, animations, drivers, " +
            "scene-wide or standalone-outfit conversion is claimed. Destructive entries are warning-skipped.", code: "MA_INITIAL_STATIC_ONLY");
        var manifest = importer is UnityPrefabImportTask prefab ? prefab.Manifest : null;
        var stage = importer.CurrentStructureRootSlot;
        var roots = (manifest?.Avatars ?? Array.Empty<VrcAvatarDefinition>()).Select(avatar =>
            (Avatar: avatar, Slot: unityObjects.TryGetValue(unchecked((ulong)avatar.GameObjectFileId), out var entry) && entry is UnityGameObject game ? game.frooxEngineSlot : null))
            .Where(root => root.Slot != null && !root.Slot.IsDestroyed && PreparedImportItem.Contains(stage, root.Slot))
            .Select(root => (root.Avatar, Slot: root.Slot!)).ToArray();
        if (roots.Length == 0)
        {
            plan.AddDiagnostic(AvatarOperationSeverity.Warning, "MA initial appearance skipped: no verified assembled avatar root in this prefab scope.", code: "MA_INITIAL_SCOPE_UNSUPPORTED");
            return;
        }
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
            var snapshots = InitialAppearanceRuntime.CaptureRenderers(unityObjects, root.Slot);
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
            await InitialAppearanceRuntime.ApplyAsync(result.Writes, root.Slot, unityObjects, importer, plan, token, "MA " + rootContext);
        }
    }
}
