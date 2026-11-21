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

public static class VrcFuryInitialAppearanceAdapter
{
    public static InitialAppearanceResult EvaluateAndReport(string yaml, InitialAppearanceContext context, AvatarBuildPlan plan, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var result = VrcFuryInitialAppearance.Evaluate(yaml, context, token);
        Report(result, plan);
        return result;
    }

    private static void Report(InitialAppearanceResult result, AvatarBuildPlan plan)
    {
        foreach (var diagnostic in result.Diagnostics)
            plan.AddDiagnostic(diagnostic.Severity switch
            {
                InitialAppearanceSeverity.Error => AvatarOperationSeverity.Error,
                InitialAppearanceSeverity.Warning => AvatarOperationSeverity.Warning,
                _ => AvatarOperationSeverity.Info
            }, diagnostic.Message, diagnostic.SourceId.ToString(CultureInfo.InvariantCulture), diagnostic.Code);
    }

    public static async Task ApplyAsync(string yaml, IReadOnlyDictionary<ulong, IUnityObject> unityObjects,
        IUnityStructureImporter importer, AvatarBuildPlan plan, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(unityObjects);
        ArgumentNullException.ThrowIfNull(importer);
        ArgumentNullException.ThrowIfNull(plan);
        token.ThrowIfCancellationRequested();
        if (!VrcFuryInitialAppearance.ContainsComponents(yaml, token)) return;
        await default(ToWorld);
        token.ThrowIfCancellationRequested();
        if (plan.HasErrors) return;
        plan.AddDiagnostic(AvatarOperationSeverity.Warning,
            "VRCFury INITIAL reversible static appearance only: upload actions and supported Toggle resting states. " +
            "No VRChat animator first-frame, saved parameters, menus, transitions, security or runtime parity is claimed. " +
            "Unsupported actions are explicitly skipped; no destructive mesh/component editing is performed.", code: "VRCFURY_INITIAL_STATIC_ONLY");
        var stage = importer.CurrentStructureRootSlot;
        var manifest = UnityStructureComposition.ManifestFor(importer);
        var roots = (manifest?.Avatars ?? Array.Empty<VrcAvatarDefinition>()).Select(avatar =>
            (Avatar: avatar, Slot: unityObjects.TryGetValue(unchecked((ulong)avatar.GameObjectFileId), out var entry) && entry is UnityGameObject game ? game.frooxEngineSlot : null))
            .Where(root => root.Slot != null && !root.Slot.IsDestroyed && PreparedImportItem.Contains(stage, root.Slot)).ToArray();
        if (roots.Length == 0)
        {
            plan.AddDiagnostic(AvatarOperationSeverity.Warning, "VRCFury initial appearance skipped: no verified assembled avatar root; standalone outfits and scenes are not inferred.", code: "VRCFURY_INITIAL_SCOPE_UNSUPPORTED");
            return;
        }
        foreach (var root in roots)
        {
            token.ThrowIfCancellationRequested();
            var avatarRoot = root.Slot!;
            string rootContext = root.Avatar.GameObjectFileId.ToString(CultureInfo.InvariantCulture);
            if (roots.Any(other => other.Avatar.GameObjectFileId != root.Avatar.GameObjectFileId &&
                (PreparedImportItem.Contains(root.Slot, other.Slot) || PreparedImportItem.Contains(other.Slot, root.Slot))))
            {
                plan.AddDiagnostic(AvatarOperationSeverity.Warning, "VRCFury initial appearance skipped: overlapping avatar ownership.", rootContext, "VRCFURY_INITIAL_SCOPE_UNSUPPORTED");
                continue;
            }
            bool hasRuntimeContext = (root.Avatar.ExpressionParameters?.HasGuid ?? false) || root.Avatar.BaseAnimationLayers.Any(layer => layer.AnimatorController?.HasGuid ?? false);
            if (hasRuntimeContext)
                plan.AddDiagnostic(AvatarOperationSeverity.Warning, "Custom animation/parameter context exists. Only baked upload/resting appearance is compiled; subsequent animator or saved parameter overrides are not evaluated.", rootContext, "VRCFURY_INITIAL_RUNTIME_UNEVALUATED");
            var context = new InitialAppearanceContext
            {
                AvatarRootId = root.Avatar.GameObjectFileId,
                AuthoredHierarchy = AuthoredAppearanceSnapshot.For(importer)?.Hierarchy,
                Renderers = InitialAppearanceRuntime.CaptureRenderers(unityObjects, avatarRoot),
                ExternalAnimationAndParameterContextVerifiedAbsent = !hasRuntimeContext
            };
            await default(ToBackground);
            var clipInputs = new Dictionary<(string Guid, long Id), AppearanceClipInput>();
            var clipResults = new Dictionary<(string Guid, long Id), UnityStaticClipResult?>();
            UnityStaticClipResult? ResolveClip(string guid, long fileId)
            {
                if (clipResults.TryGetValue((guid, fileId), out var prior)) return prior;
                var parsed = UnityStaticAnimationClip.Resolve(importer.unityProjectImporter.AssetIDDict, guid, fileId, token, source =>
                {
                    importer.unityProjectImporter.AssetIDDict.TryGetValue(guid, out var path);
                    clipInputs.Add((guid, fileId), new(guid, fileId, path, source, source == null ? null : InitialAppearanceReplay.Hash(source)));
                });
                clipResults.Add((guid, fileId), parsed);
                return parsed;
            }
            var result = VrcFuryInitialAppearance.Evaluate(yaml, context, ResolveClip, token);
            var replay = InitialAppearanceReplay.Capture(yaml, context, clipInputs.Values, result, ImportReportService.LoadedBuildIdentity,
                importer.unityProjectImporter.Session?.Reports?.SessionId ?? Guid.Empty, importer.ID.Value);
            string? replayPath = await ImportReportService.SaveAppearanceReplayAsync(replay, token);
            await default(ToWorld);
            token.ThrowIfCancellationRequested();
            Report(result, plan);
            plan.AddDiagnostic(replayPath == null ? AvatarOperationSeverity.Warning : AvatarOperationSeverity.Info,
                replayPath == null ? "Exact appearance replay could not be saved; check local diagnostic storage before requesting another import." :
                    "Exact local compiler inputs and clip dependencies captured: " + System.IO.Path.GetFileName(replayPath) + ". No native writes or playback are certified by replay.",
                rootContext, replayPath == null ? "APPEARANCE_REPLAY_UNAVAILABLE" : "APPEARANCE_REPLAY_SAVED");
            if (!result.CanApply) continue;
            await InitialAppearanceRuntime.ApplyAsync(result.Writes, avatarRoot, unityObjects, importer, plan, token, "VRCFURY_INITIAL");
        }
    }
}
