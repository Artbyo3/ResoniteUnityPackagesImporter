using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;
using SkyFrost.Base;
using UnityPackageImporter.FrooxEngineRepresentation;

namespace UnityPackageImporter.Models;

public class UnitySceneImportTask: IUnityStructureImporter
{
    public Dictionary<ulong, IUnityObject> existingIUnityObjects { get; set; }
    public Slot CurrentStructureRootSlot { get; set; }

    public KeyValuePair<string, string> ID { get; set; }

    public List<string> IncompleteComponents { get; } = new List<string>();
    internal List<ulong> FailedObjectIds { get; } = new();
    internal NestedPrefabDeferrals NestedPrefabs { get; } = new();
    internal HashSet<ulong> UnsupportedNestedPrefabInstances { get; } = new();

    private void ScanNestedPrefabs()
    {
        var prefabs = unityProjectImporter.ListOfPrefabs;
        var sources = new NestedPrefabSourceIndex(prefabs.Keys, unityProjectImporter.AssetIDDict.Keys,
            unityProjectImporter.files.Where(file => string.Equals(Path.GetExtension(file), ".meta", StringComparison.OrdinalIgnoreCase))
                .Select(UnityPackageAssetIndex.ReadGuid));
        foreach (var instance in existingIUnityObjects.Values.OfType<FrooxEngineRepresentation.GameObjectTypes.PrefabInstance>())
        {
            string guid = instance.m_SourcePrefab?.guid;
            if (sources.IsMissing(guid))
            {
                NestedPrefabs.RegisterMissing(instance.id, guid);
                continue;
            }
            if (guid == null || !prefabs.TryGetValue(guid, out string path) || !File.Exists(path) ||
                PrefabVariantResolver.ResolveFbxGuid(guid, prefabs, unityProjectImporter.SharedImportedFBXScenes.Keys) != null)
                continue;
            UnsupportedNestedPrefabInstances.Add(instance.id);
            var capability = NestedPrefabCapabilities.Scan(guid, childGuid =>
                prefabs.TryGetValue(childGuid, out string childPath) && File.Exists(childPath)
                    ? File.ReadAllText(childPath) : null);
            if (capability.CanDefer)
                NestedPrefabs.Register(instance.id, capability.PrefabGuids.Select(childGuid => prefabs[childGuid]));
        }
    }

    internal bool IsDeferredObject(IUnityObject obj) =>
        obj.m_PrefabInstance != null && obj.m_PrefabInstance.TryGetValue("fileID", out ulong instanceId) &&
        NestedPrefabs.ShouldSkipObject(instanceId, obj.m_CorrespondingSourceObject?.fileID is not null and not 0);

    internal bool TryDeferObject(IUnityObject obj)
    {
        if (!IsDeferredObject(obj)) return false;
        if (!obj.instanciated)
        {
            ulong instanceId = obj.m_PrefabInstance["fileID"];
            UnityPackageImporter.Warn(NestedPrefabs.TryGetMissingSource(instanceId, out string guid)
                ? $"Skipped stripped object {obj.id}: " + NestedPrefabDeferrals.MissingSourceWarning(instanceId, guid)
                : $"Deferred stripped object {obj.id}: its mesh-free nested prefab instance is not expanded.");
        }
        obj.instanciated = true;
        return true;
    }
    internal AvatarPackageManifest Manifest { get; private set; }
    internal PreparedImportItem ScenePreparation { get; set; }
    internal IReadOnlyList<PreparedImportItem> PreparedItems { get; private set; } = Array.Empty<PreparedImportItem>();
    public AvatarBuildPlan ConversionPlan { get; private set; }
    public bool IsPartialCompatibility => PrefabVariantResolver.EvaluatePartialCompatibility(
        IncompleteComponents.Count,
        (Manifest?.HasUnsupportedVrcFuryFeatures ?? false) || (ConversionPlan?.HasErrors ?? false));

    private float3 GlobalIndicatorPosition;
    public ProgressBarInterface progressIndicator { get; set; }

    public UnityProjectImporter unityProjectImporter { get; set; }

    public UnitySceneImportTask(float3 globalPosition, KeyValuePair<string, string> ID, UnityProjectImporter unityProjectImporter)
    {
        this.ID = ID;
        this.unityProjectImporter = unityProjectImporter;
        this.GlobalIndicatorPosition = globalPosition;
    }

    public async Task StartImport()
    {
        StringBuilder debugScene = new StringBuilder();
        try
        {
            existingIUnityObjects = new Dictionary<ulong, IUnityObject>();
            await default(ToWorld);

            unityProjectImporter.Session?.Check();
            this.CurrentStructureRootSlot = unityProjectImporter.Session?.CreateStructure(
                Path.GetFileName(ID.Value), float3.Zero, floatQ.Identity)
                ?? unityProjectImporter.world.AddSlot(Path.GetFileName(ID.Value));
            if (unityProjectImporter.Station == null)
            {
                Slot indicator = this.unityProjectImporter.root.AddSlot("Unity Scene Import Indicator");
                indicator.GlobalPosition = GlobalIndicatorPosition;
                indicator.PersistentSelf = false;
                this.progressIndicator = await indicator.SpawnEntity<ProgressBarInterface, LegacySegmentCircleProgress>(FavoriteEntity.ProgressBar);
                progressIndicator?.Initialize(false);
            }

            progressIndicator?.UpdateProgress(0f, "", "now loading unity YAML objects for Scene.");
            await default(ToBackground);

            this.Manifest = AvatarPackageIndex.ParseFile(
                this.ID.Value,
                this.unityProjectImporter?.ListOfPrefabs);
            this.existingIUnityObjects = YamlToFrooxEngine.parseYaml(this.ID.Value);
            ScanNestedPrefabs();
            int totalProgress = 0;

            foreach (KeyValuePair<ulong, IUnityObject> obj in existingIUnityObjects)
            {
                Type type = obj.Value.GetType();
                if (!UnityEngineObjectWrapper.addedProgress.TryGetValue(type, out int progressitem))
                {
                    progressitem = 4;
                }
                totalProgress += progressitem;
            }
            totalProgress = Math.Max(totalProgress, 1);

            // Instanciate everything else.
            int counter = 0;
            int progress = 0;
            await default(ToWorld);
            foreach (var obj in existingIUnityObjects)
            {
                unityProjectImporter.Session?.Check();
                counter++;
                Type type = obj.Value.GetType();
                if (!UnityEngineObjectWrapper.addedProgress.TryGetValue(type, out int progressitem))
                {
                    progressitem = 4;
                }
                progress += progressitem;
                progressIndicator?.UpdateProgress(MathX.Clamp01((float)progress / (float)totalProgress), "", "now loading " + counter + "/" + existingIUnityObjects.Count + " objects for scene");
                UnityPackageImporter.Msg("loading object for scene \"" + ID.Value + "\" with an id of \"" + obj.Value.id.ToString() + "\"");
                try
                {
                    ulong instanceId = obj.Value.m_PrefabInstance?.TryGetValue("fileID", out ulong ownerId) == true ? ownerId : 0;
                    await NestedPrefabs.InstantiateAsync(instanceId,
                        obj.Value.m_CorrespondingSourceObject?.fileID is not null and not 0,
                        () => obj.Value.InstanciateAsync(this), () => TryDeferObject(obj.Value));
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    UnityPackageImporter.Warn("Scene IUnityObject failed to instanciate!");
                    UnityPackageImporter.Msg("Scene IUnityObject ID: \"" + obj.Value.id.ToString() + "\" (" + type.Name + ")");
                    UnityPackageImporter.Warn(e.Message + e.StackTrace);
                    IncompleteComponents.Add($"{type.Name} ({obj.Value.id})");
                    FailedObjectIds.Add(obj.Value.id);
                    ImportReportService.ObjectFailed(unityProjectImporter.Session?.Reports, obj.Value, e, ID.Value);
                }
                try
                {
                    debugScene.Append(obj.Value.ToString());
                }
                catch (Exception e)
                {
                    UnityPackageImporter.Warn("Scene IUnityObject could not be turned into a string!");
                    UnityPackageImporter.Msg("Scene IUnityObject ID: \"" + obj.Value.id.ToString() + "\"");
                    UnityPackageImporter.Warn(e.Message + e.StackTrace);
                }
            }

            if (this.CurrentStructureRootSlot == null || this.CurrentStructureRootSlot.IsDestroyed)
            {
                throw new InvalidDataException($"Root slot for scene {Path.GetFileName(ID.Value)} was not created.");
            }

            var authoredSkins = existingIUnityObjects.Values.OfType<FrooxEngineRepresentation.GameObjectTypes.SkinnedMeshRenderer>()
                .Where(skin => !IsDeferredObject(skin)).ToList();
            int successfulSkins = authoredSkins.Count(s => s.createdMeshRenderer != null);
            if (authoredSkins.Count > 0 && !PrefabVariantResolver.EvaluateSkinnedMeshReconstruction(authoredSkins.Count, successfulSkins, Path.GetFileName(ID.Value), out string failureReason))
            {
                throw new InvalidDataException(failureReason);
            }

            await default(ToBackground);

            List<IUnityObject> movethese = new List<IUnityObject>();

            foreach (var obj in existingIUnityObjects)
            {
                if (obj.Value is FrooxEngineRepresentation.GameObjectTypes.Transform trans)
                {
                    if (trans.m_FatherID == 0 && trans.parentHashedGameObj == null)
                    {
                        if (existingIUnityObjects.TryGetValue(trans.m_GameObjectID, out var go) && go != null)
                        {
                            movethese.Add(go);
                        }
                        else
                        {
                            UnityPackageImporter.Warn("transform with id \"" + trans.id.ToString() + "\" does not have a parent game object!");
                        }
                    }
                }
            }

            UnityPackageImporter.Msg("Moving orphaned objects");
            // Getting rid of objects that should go under the prefab slot.
            foreach (var obj in movethese)
            {
                if (obj is FrooxEngineRepresentation.GameObjectTypes.GameObject gameobj && gameobj.frooxEngineSlot != null)
                {
                    await default(ToWorld);
                    gameobj.frooxEngineSlot.SetParent(this.CurrentStructureRootSlot, false);
                    await default(ToBackground);
                }
            }

            // Milestone 1: Unified Avatar Conversion Architecture for Scene avatars
            if (this.Manifest != null && (this.Manifest.ModularAvatarComponents.Count > 0 || this.Manifest.VrcFuryComponents.Count > 0))
            {
                ConversionPlan = AvatarBuildPlanner.CreatePlan(this.Manifest, Path.GetFileName(ID.Value));
                if (!ConversionPlan.HasErrors)
                {
                    await default(ToWorld);
                    await AvatarBuildPlanExecutor.ExecutePlanAsync(
                        ConversionPlan,
                        this.CurrentStructureRootSlot,
                        this.existingIUnityObjects,
                        this,
                        unityProjectImporter?.Session?.Token ?? default);
                    await default(ToBackground);
                }
                else
                {
                    UnityPackageImporter.Warn($"Conversion plan for '{ID.Value}' has errors; skipping structural execution to prevent hierarchy corruption.");
                }
            }

            UnityPackageImporter.Msg("re-enabling skinned mesh renderers in scene \""+ID.Value+"\"");

            await default(ToWorld);
            // Include skins inherited from FBX instances, even when the scene
            // contains no individual YAML override for those renderers.
            var renderersToEnable = CurrentStructureRootSlot
                .GetComponentsInChildren<FrooxEngine.SkinnedMeshRenderer>();
            await SkinnedBoundsPolicy.StabilizeBeforeEnableAsync(
                renderersToEnable,
                "Unity scene activation", unityProjectImporter.Session?.Token ?? default);

            await default(ToBackground);
            foreach (var obj in existingIUnityObjects)
            {
                if (obj.Value.GetType() == typeof(FrooxEngineRepresentation.GameObjectTypes.SkinnedMeshRenderer))
                {
                    FrooxEngineRepresentation.GameObjectTypes.SkinnedMeshRenderer newobj = (obj.Value as FrooxEngineRepresentation.GameObjectTypes.SkinnedMeshRenderer);
                    await default(ToWorld);
                    if(newobj.createdMeshRenderer != null)
                    {
                        newobj.createdMeshRenderer.Enabled = newobj.m_Enabled == 1;
                    }
                    await default(ToBackground);
                }
            }

            PreparedItems = await PreparedImportItem.FromSceneAsync(this);
            await default(ToBackground);

            progressIndicator?.ProgressDone("Finished Scene!");
            progressIndicator?.UpdateProgress(1f,"","Finished!");

            UnityPackageImporter.Msg("Yaml generation done");
            UnityPackageImporter.Msg("Scene finished!");
        }
        catch (Exception e)
        {
            UnityPackageImporter.Warn("Scene \"" + ID.Value + "\" hit critical import error! dumping!");
            UnityPackageImporter.Warn(e.Message + e.StackTrace);
            UnityPackageImporter.Msg(debugScene.ToString());
            progressIndicator?.ProgressFail("Failed to decode the Unity Scene due to an error!");
            throw;
        }
    }
}
