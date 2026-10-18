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

internal class UnityPrefabImportTask : IUnityStructureImporter
{
    public Dictionary<ulong, IUnityObject> existingIUnityObjects { get; set; }
    public Slot CurrentStructureRootSlot { get; set; }
    public KeyValuePair<string, string> ID { get; set; }
    private float3 GlobalIndicatorPosition;
    private floatQ GlobalIndicatorRotation;
    public ProgressBarInterface progressIndicator { get; set; }
    internal Func<float, Task> ReportProgress { get; set; }
    internal AvatarPackageManifest Manifest { get; private set; }
    public List<string> IncompleteComponents { get; } = new();
    internal List<ulong> FailedObjectIds { get; } = new();
    public AvatarBuildPlan ConversionPlan { get; private set; }
    public bool IsPartialCompatibility => PrefabVariantResolver.EvaluatePartialCompatibility(
        IncompleteComponents.Count,
        (Manifest?.HasUnsupportedVrcFuryFeatures ?? false) || (ConversionPlan?.HasErrors ?? false));
    internal PreparedImportItem PreparedItem { get; private set; }
    public UnityProjectImporter unityProjectImporter { get; set; }

    public UnityPrefabImportTask(float3 globalPosition, KeyValuePair<string, string> ID, UnityProjectImporter unityProjectImporter, floatQ? globalRotation = null)
    {
        this.ID = ID;
        this.unityProjectImporter = unityProjectImporter;
        this.GlobalIndicatorPosition = globalPosition;
        this.GlobalIndicatorRotation = globalRotation ?? floatQ.Identity;
    }

    public async Task StartImport()
    {
        StringBuilder debugPrefab = new StringBuilder();
        try
        {
            existingIUnityObjects = new Dictionary<ulong, IUnityObject>();
            await default(ToWorld);
            unityProjectImporter.Session?.Check();
            this.CurrentStructureRootSlot = unityProjectImporter.Session?.CreateStructure(
                Path.GetFileName(ID.Value), GlobalIndicatorPosition, GlobalIndicatorRotation)
                ?? unityProjectImporter.world.AddSlot(Path.GetFileName(ID.Value));
            if (unityProjectImporter.Session == null)
            {
                this.CurrentStructureRootSlot.GlobalPosition = this.GlobalIndicatorPosition;
                this.CurrentStructureRootSlot.GlobalRotation = this.GlobalIndicatorRotation;
            }
            if (this.progressIndicator == null && ReportProgress == null)
            {
                Slot indicator = this.unityProjectImporter.root.AddSlot("Unity Prefab Import Indicator");
                indicator.GlobalPosition = this.GlobalIndicatorPosition;
                indicator.GlobalRotation = this.GlobalIndicatorRotation;
                indicator.PersistentSelf = false;
                this.progressIndicator = await indicator.SpawnEntity<ProgressBarInterface, LegacySegmentCircleProgress>(FavoriteEntity.ProgressBar);
                progressIndicator?.Initialize(false);
            }
            await default(ToBackground);


            progressIndicator?.UpdateProgress(0f, "", "now loading unity YAML objects for Prefab.");
            AvatarPackageManifest avatarManifest = Manifest = AvatarPackageIndex.ParseFile(
                this.ID.Value,
                this.unityProjectImporter?.ListOfPrefabs);
            if (avatarManifest.HasVrcFuryComponents)
            {
                int linkCount = avatarManifest.AllVrcFuryArmatureLinks.Count;
                int unsupportedCount = avatarManifest.UnsupportedVrcFuryFeatureCount;
                UnityPackageImporter.Msg($"Avatar package contains {avatarManifest.VrcFuryComponents.Count} VRCFury component(s) with {linkCount} Armature Link(s).");
                if (unsupportedCount > 0)
                {
                    UnityPackageImporter.Warn($"{unsupportedCount} unsupported VRCFury feature(s) detected; non-structural processing is deferred.");
                }
            }
            this.existingIUnityObjects  = YamlToFrooxEngine.parseYaml(this.ID.Value);

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

            // Some debugging for the user to show them it worked or failed.

            UnityPackageImporter.Msg("Loaded " + existingIUnityObjects.Count.ToString() + " Unity objects/components/meshes for prefab!");

            await default(ToWorld);
            int counter = 0;
            int progress = 0;
            // Instanciate our objects to generate our prefab entirely, using the ids we assigned ealier to identify our prefab elements in our list.
            foreach (KeyValuePair<ulong,IUnityObject> obj in existingIUnityObjects)
            {
                unityProjectImporter.Session?.Check();
                counter++;
                Type type = obj.Value.GetType();
                if (!UnityEngineObjectWrapper.addedProgress.TryGetValue(type, out int progressitem))
                {
                    progressitem = 4;
                }
                progress += progressitem;
                if (ReportProgress != null) await ReportProgress(MathX.Clamp01((float)progress / (float)totalProgress) * 0.85f);
                progressIndicator?.UpdateProgress(MathX.Clamp01((float)progress / (float)totalProgress), "", "now loading " + this.existingIUnityObjects.Count.ToString() + "/" + counter.ToString() + " objects for Prefab");
                UnityPackageImporter.Msg("loading object for prefab \"" + ID.Value + "\" with an id of \"" + obj.Value.id.ToString() + "\"");
                try
                {
                    await obj.Value.InstanciateAsync(this);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    UnityPackageImporter.Warn("Prefab IUnityObject failed to instanciate!");
                    UnityPackageImporter.Msg("Prefab IUnityObject ID: \"" + obj.Value.id.ToString() + "\" (" + type.Name + ")");
                    UnityPackageImporter.Warn(e.Message + e.StackTrace);
                    IncompleteComponents.Add($"{type.Name} ({obj.Value.id})");
                    FailedObjectIds.Add(obj.Value.id);
                }
                try
                {
                    debugPrefab.Append(obj.Value.ToString());
                }
                catch (Exception e)
                {
                    UnityPackageImporter.Warn("Prefab IUnityObject could not be turned into a string!");
                    UnityPackageImporter.Msg("Prefab IUnityObject ID: \"" + obj.Value.id.ToString() + "\"");
                    UnityPackageImporter.Warn(e.Message + e.StackTrace);
                }
            }

            progressIndicator?.UpdateProgress(0f, "", "instanciated " + existingIUnityObjects.Count.ToString() + " Unity objects/components/meshes for prefab! Now cleaning up.");

            var authoredSkins = existingIUnityObjects.Values.OfType<FrooxEngineRepresentation.GameObjectTypes.SkinnedMeshRenderer>().ToList();
            int successfulSkins = authoredSkins.Count(s => s.createdMeshRenderer != null);
            if (!PrefabVariantResolver.EvaluateSkinnedMeshReconstruction(authoredSkins.Count, successfulSkins, Path.GetFileName(ID.Value), out string failureReason))
            {
                throw new InvalidDataException(failureReason);
            }

            List<IUnityObject> movethese = new List<IUnityObject>();

            foreach (var obj in existingIUnityObjects)
            {
                if (obj.Value.GetType() != typeof(FrooxEngineRepresentation.GameObjectTypes.Transform))
                    continue;

                var trans = obj.Value as FrooxEngineRepresentation.GameObjectTypes.Transform;
                if (trans == null)
                    continue;

                if (trans.m_FatherID != 0 || trans.parentHashedGameObj != null)
                    continue;

                try
                {
                    movethese.Add(existingIUnityObjects[trans.m_GameObjectID]);
                }
                catch (KeyNotFoundException)
                {
                    UnityPackageImporter.Warn("transform with id \"" + trans.id.ToString() + "\" does not have a parent game object! This is bad!");
                }
            }

            UnityPackageImporter.Msg("Moving orphaned objects");
            // Getting rid of objects that should go under the prefab slot.
            foreach (var obj in movethese)
            {
                FrooxEngineRepresentation.GameObjectTypes.GameObject gameobj = obj as FrooxEngineRepresentation.GameObjectTypes.GameObject;
                await default(ToWorld);
                gameobj.frooxEngineSlot.SetParent(this.CurrentStructureRootSlot, false);
                await default(ToBackground);
            }

            UnityPackageImporter.Msg("Yaml generation done");

            // Milestone 1: Unified Avatar Conversion Architecture
            if (avatarManifest != null && (avatarManifest.ModularAvatarComponents.Count > 0 || avatarManifest.VrcFuryComponents.Count > 0))
            {
                ConversionPlan = AvatarBuildPlanner.CreatePlan(avatarManifest, Path.GetFileName(ID.Value));
                if (avatarManifest.IsModularAvatarAttachment)
                {
                    // The target skeleton is selected in the station. The existing
                    // installer owns MA merge/proxy execution on a duplicate there.
                    AvatarPlanExecution.DeferSupportedInstallation(ConversionPlan);
                }
                else if (!ConversionPlan.HasErrors)
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

            PreparedItem = new PreparedImportItem(this, CurrentStructureRootSlot, Manifest,
                ConversionPlan, IncompleteComponents, Path.GetFileNameWithoutExtension(ID.Value), FailedObjectIds);
            await PreparedItem.PrepareHumanoidsAsync();

            await default(ToWorld);
            // The YAML index omits inherited FBX skins without overrides.
            // Finalize the complete imported hierarchy, including inactive skins.
            var renderersToEnable = CurrentStructureRootSlot
                .GetComponentsInChildren<FrooxEngine.SkinnedMeshRenderer>();
            await SkinnedBoundsPolicy.StabilizeBeforeEnableAsync(
                renderersToEnable,
                "Unity prefab activation", unityProjectImporter.Session?.Token ?? default);

            foreach (var obj in existingIUnityObjects)
            {
                if (obj.Value.GetType() == typeof(FrooxEngineRepresentation.GameObjectTypes.SkinnedMeshRenderer))
                {
                    var newobj = (obj.Value as FrooxEngineRepresentation.GameObjectTypes.SkinnedMeshRenderer);
                    await default(ToWorld);
                    if (newobj.createdMeshRenderer == null)
                    {
                        UnityPackageImporter.Warn("Skipping missing renderer " + newobj.id + " in prefab " + ID.Value);
                        if (!IncompleteComponents.Contains($"SkinnedMeshRenderer ({newobj.id})"))
                        {
                            IncompleteComponents.Add($"SkinnedMeshRenderer ({newobj.id})");
                        }
                        continue;
                    }
                    newobj.createdMeshRenderer.Enabled = newobj.m_Enabled == 1;
                    await default(ToBackground);
                }
            }

            await default(ToWorld);
            if (ReportProgress == null) await PreparedItem.BuildExpressionsAsync();

            if (avatarManifest.ModularAvatarComponents.Count > 0)
            {
                UnityPackageImporter.Msg(
                    "Detected " + avatarManifest.ModularAvatarComponents.Count +
                    " Modular Avatar component(s) in " + Path.GetFileName(ID.Value) +
                    ". Compatibility data was indexed; installation behavior will be applied only by supported component handlers.");
            }

            if (ReportProgress != null) await ReportProgress(1f);
            progressIndicator?.ProgressDone("Finished Prefab!");
            progressIndicator?.UpdateProgress(1f, "", "Finished!");

        }
        catch (Exception e)
        {
            UnityPackageImporter.Warn("Prefab hit critical import error! dumping!");
            UnityPackageImporter.Warn(e.Message + e.StackTrace);
            UnityPackageImporter.Msg(debugPrefab.ToString());
            progressIndicator?.ProgressFail("Failed to decode the Unity Prefab due to an error!");
            throw;
        }

        await default(ToBackground);
        UnityPackageImporter.Msg("Yaml generation done");
        UnityPackageImporter.Msg("Prefab finished!");
    }
}
