using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.CommonAvatar;
using UnityPackageImporter.FrooxEngineRepresentation;
using UnityGameObject = UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes.GameObject;
using UnityPrefabInstance = UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes.PrefabInstance;
using UnitySkin = UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes.SkinnedMeshRenderer;

namespace UnityPackageImporter.Models;

// A selectable output, not a source-file import task. Owns preparation through
// native creation for both prefab models and individual scene avatars.
internal sealed class PreparedImportItem
{
    private readonly IUnityStructureImporter source;
    private readonly List<ImportedHumanoidRig> preparedHumanoids = new();
    private bool expressionsBuilt;
    internal Slot CurrentStructureRootSlot { get; }
    internal AvatarPackageManifest Manifest { get; }
    internal AvatarBuildPlan ConversionPlan { get; }
    internal IReadOnlyList<string> IncompleteComponents { get; }
    internal string DisplayName { get; }
    internal IReadOnlyList<ulong> FailedObjectIds { get; }
    internal bool IsScopedSceneItem { get; }
    internal bool OptionalControlsFailed { get; private set; }
    internal string SourcePath => source.ID.Value;
    internal NestedPrefabDeferrals NestedPrefabs => (source as UnitySceneImportTask)?.NestedPrefabs;
    internal ImportResult ReadResult() => ImportResultAdapter.Read(this, Project);
    internal IReadOnlyList<string> MaterialWarnings => ReadResult().MaterialWarnings;
    internal KeyValuePair<string, string> ID => source.ID;
    internal Dictionary<ulong, IUnityObject> existingIUnityObjects => source.existingIUnityObjects;
    private UnityProjectImporter Project => source.unityProjectImporter;

    internal PreparedImportItem(IUnityStructureImporter source, Slot root, AvatarPackageManifest manifest,
        AvatarBuildPlan plan, IReadOnlyList<string> incomplete, string displayName,
        IReadOnlyList<ulong> failedObjectIds, bool scopedSceneItem = false)
    {
        this.source = source;
        CurrentStructureRootSlot = root;
        Manifest = manifest;
        ConversionPlan = plan;
        IncompleteComponents = incomplete;
        DisplayName = displayName;
        FailedObjectIds = failedObjectIds;
        IsScopedSceneItem = scopedSceneItem;
    }

    internal static bool Contains(Slot root, Slot child) =>
        child != null && !child.IsDestroyed && (child == root || child.IsChildOf(root));

    internal Slot ResolveGameObject(long id) =>
        existingIUnityObjects.TryGetValue(unchecked((ulong)id), out var obj) && obj is UnityGameObject go
            ? go.frooxEngineSlot : null;

    internal async Task PrepareHumanoidsAsync(bool includeAuthoredSkeleton = true)
    {
        if (!Manifest.ShouldSetUpHumanoid) return;
        await default(ToWorld);
        Project.Session?.Check();
        foreach (var prefab in existingIUnityObjects.Values.OfType<UnityPrefabInstance>())
        {
            Slot root = prefab.ImportRoot?.frooxEngineSlot;
            if (prefab.importask != null && Contains(CurrentStructureRootSlot, root))
                await PrepareHumanoidAsync(prefab.importask, root);
            await default(ToWorld);
        }

        if (!includeAuthoredSkeleton || CurrentStructureRootSlot.GetComponentsInChildren<BipedRig>().Any(rig => rig.IsBiped)) return;

        // Directly authored skeletons have no FBX PrefabInstance. Resolve their
        // humanoid metadata through the skinned meshes, inside this item's scope.
        var sources = existingIUnityObjects.Values.OfType<UnitySkin>()
            .Where(skin => skin.createdMeshRenderer != null &&
                Contains(CurrentStructureRootSlot, skin.createdMeshRenderer.Slot))
            .Select(skin => skin.m_Mesh?.guid).Where(guid => !string.IsNullOrEmpty(guid)).Distinct().ToArray();
        foreach (var guid in sources)
        {
            Project.Session?.Check();
            if (!Project.SharedImportedFBXScenes.TryGetValue(guid, out var model)) continue;
            await PrepareHumanoidAsync(model, CurrentStructureRootSlot);
            await default(ToWorld);
            if (CurrentStructureRootSlot.GetComponentsInChildren<BipedRig>().Any(rig => rig.IsBiped)) break;
        }
    }

    private async Task PrepareHumanoidAsync(FileImportTaskScene model, Slot root)
    {
        var prepared = await HumanoidRigSetup.SetupAsync(model, root);
        await default(ToWorld);
        if (prepared != null) preparedHumanoids.Add(prepared);
    }

    internal void CompleteHumanoidHierarchy(Slot avatarRoot)
    {
        foreach (var prepared in preparedHumanoids) prepared.CompleteAvatar(avatarRoot);
    }

    internal async Task BuildExpressionsAsync()
    {
        if (expressionsBuilt) return;
        await default(ToWorld);
        Project.Session?.Check();
        if (Manifest.Avatars.Any(avatar =>
            Contains(CurrentStructureRootSlot, ResolveGameObject(avatar.GameObjectFileId)) &&
            ResolveGameObject(avatar.GameObjectFileId).GetComponentInParents<AvatarRoot>() == null))
            return;
        expressionsBuilt = true;
        foreach (var avatar in Manifest.Avatars)
        {
            await default(ToWorld);
            Project.Session?.Check();
            Slot root = ResolveGameObject(avatar.GameObjectFileId);
            if (!Contains(CurrentStructureRootSlot, root))
            {
                UnityPackageImporter.Warn("Skipping an unresolved avatar expression root in " + ID.Value);
                continue;
            }
            VrcMenu menu = null;
            Dictionary<string, VrcParameter> parsedParameters = null;
            try
            {
                (menu, parsedParameters) = ExpressionMenuParser.LoadMenuHierarchy(
                    avatar.ExpressionsMenu?.Guid, avatar.ExpressionParameters?.Guid, Project.AssetIDDict);
                if (menu != null && menu.Controls.Count > 0)
                {
                    bool complete = await ContextMenuBuilder.BuildExpressionsMenuAsync(root, menu,
                        new Dictionary<VrcMenuControl, IField<bool>>(), Project);
                    OptionalControlsFailed |= !complete;
                    if (!complete)
                        ConversionPlan.AddDiagnostic(AvatarOperationSeverity.Warning,
                            "Base expression controls have no compiled Animator bindings and remain disabled. No clothing-name guesses were applied.",
                            avatar.GameObjectFileId.ToString(System.Globalization.CultureInfo.InvariantCulture), "EXPRESSION_RUNTIME_UNSUPPORTED");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await default(ToWorld);
                OptionalControlsFailed = true;
                ConversionPlan.AddDiagnostic(AvatarOperationSeverity.Warning,
                    "Optional expression menu reconstruction failed; no runtime parity is claimed. " + ex.Message,
                    avatar.GameObjectFileId.ToString(System.Globalization.CultureInfo.InvariantCulture), "EXPRESSION_RUNTIME_FAILED");
                UnityPackageImporter.Warn("Failed to reconstruct expressions menu: " + ex);
            }

            if (source is not UnityPrefabImportTask prefab || string.IsNullOrWhiteSpace(prefab.ComposedYaml)) continue;
            await default(ToWorld);
            var token = Project.Session?.Token ?? default;
            try
            {
                var context = new InitialAppearanceContext
                {
                    AvatarRootId = avatar.GameObjectFileId,
                    Renderers = prefab.AuthoredAppearanceRenderers,
                    AllowMixedFrameworks = true
                };
                await default(ToBackground);
                var composedWardrobe = AvatarWardrobeComposer.Compose(
                    prefab.ComposedYaml,
                    context,
                    (guid, fileId) => UnityStaticAnimationClip.Resolve(Project.AssetIDDict, guid, fileId, token),
                    menu,
                    parsedParameters,
                    token);
                await default(ToWorld);
                token.ThrowIfCancellationRequested();
                foreach (var diagnostic in composedWardrobe.Diagnostics)
                    ConversionPlan.AddDiagnostic(diagnostic.Severity == InitialAppearanceSeverity.Info
                        ? AvatarOperationSeverity.Info : AvatarOperationSeverity.Warning, diagnostic.Message,
                        diagnostic.SourceId.ToString(System.Globalization.CultureInfo.InvariantCulture), diagnostic.Code);
                if (!composedWardrobe.CanBuild)
                {
                    OptionalControlsFailed |= composedWardrobe.HasUnsupportedContext ||
                        composedWardrobe.Diagnostics.Any(diagnostic => diagnostic.Severity == InitialAppearanceSeverity.Error);
                }
                else if (root.GetComponentInParents<AvatarRoot>() == null)
                {
                    OptionalControlsFailed = true;
                    ConversionPlan.AddDiagnostic(AvatarOperationSeverity.Warning,
                        "Wardrobe controls require completed native avatar setup; no drivers were attached to this ordinary prefab.",
                        code: "WARDROBE_NATIVE_SETUP_REQUIRED");
                }
                else
                {
                    await AvatarWardrobeRuntime.BuildAsync(composedWardrobe, root, existingIUnityObjects, Project, token);
                    ConversionPlan.AddDiagnostic(AvatarOperationSeverity.Info,
                        "Created " + composedWardrobe.Controls.Count + " exact native wardrobe toggles across unified framework composition. Native per-copy state is not VRChat saved-parameter restoration or re-wear reset parity.",
                        avatar.GameObjectFileId.ToString(System.Globalization.CultureInfo.InvariantCulture), "WARDROBE_COMPOSED_BUILT");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await default(ToWorld);
                OptionalControlsFailed = true;
                ConversionPlan.AddDiagnostic(AvatarOperationSeverity.Warning,
                    "Optional wardrobe reconstruction failed; no runtime parity is claimed. " + ex.Message,
                    avatar.GameObjectFileId.ToString(System.Globalization.CultureInfo.InvariantCulture), "EXPRESSION_RUNTIME_FAILED");
                UnityPackageImporter.Warn("Failed to reconstruct wardrobe controls: " + ex);
            }

            await default(ToWorld);
            if (root.GetComponentInParents<AvatarRoot>() != null)
            {
                try
                {
                    await BlendshapeInitialAppearanceAdapter.BuildRuntimeLinksAsync(
                        prefab.ComposedYaml, avatar.GameObjectFileId, root, existingIUnityObjects, source, ConversionPlan, token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await default(ToWorld);
                    OptionalControlsFailed = true;
                    ConversionPlan.AddDiagnostic(AvatarOperationSeverity.Warning,
                        "Optional blendshape runtime synchronization failed; no runtime parity is claimed. " + ex.Message,
                        avatar.GameObjectFileId.ToString(System.Globalization.CultureInfo.InvariantCulture), "EXPRESSION_RUNTIME_FAILED");
                    UnityPackageImporter.Warn("Failed to build blendshape runtime links: " + ex);
                }
            }
        }
    }

    internal static async Task<IReadOnlyList<PreparedImportItem>> FromSceneAsync(UnitySceneImportTask scene)
    {
        await default(ToWorld);
        var project = scene.unityProjectImporter;
        var manifest = scene.Manifest;
        Slot Resolve(long id) => scene.existingIUnityObjects.TryGetValue(unchecked((ulong)id), out var obj) &&
            obj is UnityGameObject go ? go.frooxEngineSlot : null;

        if (manifest.Avatars.Count == 0)
        {
            // Preserve ordinary scene-model preparation without treating the
            // whole scene as one avatar or publishing it through avatar creation.
            scene.ScenePreparation = new PreparedImportItem(scene, scene.CurrentStructureRootSlot,
                manifest, scene.ConversionPlan, scene.IncompleteComponents, Path.GetFileName(scene.ID.Value), scene.FailedObjectIds);
            await scene.ScenePreparation.PrepareHumanoidsAsync(includeAuthoredSkeleton: false);
            return Array.Empty<PreparedImportItem>();
        }

        var candidates = new List<Slot>();
        foreach (var avatar in manifest.Avatars)
        {
            // Inherited IDs are not scene IDs. Until reference remapping exists,
            // do not accidentally select an unrelated object with a colliding ID.
            Slot root = string.Equals(avatar.SourcePath, manifest.SourcePath, StringComparison.OrdinalIgnoreCase)
                ? Resolve(avatar.GameObjectFileId) : null;
            if (!Contains(scene.CurrentStructureRootSlot, root))
            {
                scene.IncompleteComponents.Add("Unresolved scene avatar descriptor (" + avatar.ComponentFileId + ")");
                continue;
            }
            candidates.Add(root);
        }

        var roots = AvatarImportSelection.IndependentRoots(candidates, Contains);
        // Validate every root before moving any of them. Skin references must
        // survive independently when the station closes and deletes other items.
        AvatarImportSelection.ValidateSkinOwnership(roots,
            scene.CurrentStructureRootSlot.GetComponentsInChildren<SkinnedMeshRenderer>()
                .Select(skin => (skin.Slot, (IEnumerable<Slot>)skin.Bones.ToArray())), Contains);

        var items = new List<PreparedImportItem>();
        var envelopes = new OwnedAttachmentTransaction<Slot>();
        try
        {
            foreach (var root in roots)
            {
                project.Session?.Check();
                bool Owns(long id) => Contains(root, Resolve(id));
                var scoped = AvatarImportSelection.Scope(manifest, Owns);
                string name = scoped.Avatars.First().GameObjectName;
                if (string.IsNullOrWhiteSpace(name)) name = root.Name;
                // Preserve authored world transforms. Each output has a separate
                // session envelope, so committing it cannot commit its scene siblings.
                Slot wrapper = project.Session?.CreateStructure(name, root.GlobalPosition, root.GlobalRotation)
                    ?? project.world.AddSlot(name);
                envelopes.Track(wrapper);
                root.SetParent(wrapper, true);
                var item = new PreparedImportItem(scene, wrapper, scoped, scene.ConversionPlan,
                    scene.IncompleteComponents, name + " (" + Path.GetFileNameWithoutExtension(scene.ID.Value) + ")",
                    scene.FailedObjectIds, scopedSceneItem: true);
                await item.PrepareHumanoidsAsync();
                await default(ToWorld);
                items.Add(item);
                if (project.Station == null) await item.BuildExpressionsAsync();
                await default(ToWorld);
            }
            envelopes.Commit();
            return items;
        }
        catch
        {
            // Roll back the scene preparation envelopes, then rethrow.
            await default(ToWorld);
            // These wrappers were moved outside the original scene root.
            // The source-level cleanup alone cannot reach them after a failure.
            var errors = envelopes.RollBack(slot =>
            {
                if (!slot.IsDestroyed) slot.DestroyPreservingAssets();
            });
            foreach (var error in errors)
                UnityPackageImporter.Warn("Failed to clean up scene preparation: " + error.GetType().Name);
            throw;
        }
    }
}
