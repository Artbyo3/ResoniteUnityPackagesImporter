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
    public List<Slot> oldSlots = new List<Slot>();
    public Dictionary<ulong, IUnityObject> existingIUnityObjects { get; set; }
    public Slot CurrentStructureRootSlot { get; set; }
    public Slot allimportsroot { get; set; }
    public KeyValuePair<string, string> ID { get; set; }
    private float3 GlobalIndicatorPosition;
    public ProgressBarInterface progressIndicator { get; set; }
    public UnityProjectImporter unityProjectImporter { get; set; }

    public UnityPrefabImportTask(float3 globalPosition, Slot root, KeyValuePair<string, string> ID, UnityProjectImporter unityProjectImporter)
    {
        this.ID = ID;
        this.unityProjectImporter = unityProjectImporter;
        this.allimportsroot = root;
        this.GlobalIndicatorPosition = globalPosition;
    }

    public async Task StartImport()
    {
        StringBuilder debugPrefab = new StringBuilder();
        try
        {
            existingIUnityObjects = new Dictionary<ulong, IUnityObject>();
            await default(ToWorld);
            this.CurrentStructureRootSlot = unityProjectImporter.world.AddSlot(Path.GetFileName(ID.Value));
            // A prefab is an independent world object; importer UI and temporary
            // FBX templates must not become its transform or lifetime owner.
            this.CurrentStructureRootSlot.GlobalPosition = this.GlobalIndicatorPosition;
            Slot indicator = this.unityProjectImporter.root.AddSlot("Unity Prefab Import Indicator");
            indicator.GlobalPosition = this.GlobalIndicatorPosition;
            indicator.PersistentSelf = false;
            this.progressIndicator = await indicator.SpawnEntity<ProgressBarInterface, LegacySegmentCircleProgress>(FavoriteEntity.ProgressBar);
            progressIndicator?.Initialize(false);
            await default(ToBackground);


            progressIndicator?.UpdateProgress(0f, "", "now loading unity YAML objects for Prefab.");
            AvatarPackageManifest avatarManifest = AvatarPackageIndex.ParseFile(this.ID.Value);
            this.existingIUnityObjects  = YamlToFrooxEngine.parseYaml(this.ID.Value);

            int totalProgress = 0;
            foreach (KeyValuePair<ulong, IUnityObject> obj in existingIUnityObjects)
            {
                Type type = obj.Value.GetType();
                int progressitem = 4;

                UnityEngineObjectWrapper.addedProgress.TryGetValue(type, out progressitem);
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
                counter++;
                Type type = obj.Value.GetType();
                int progressitem = 4;

                UnityEngineObjectWrapper.addedProgress.TryGetValue(type, out progressitem);
                progress += progressitem;
                progressIndicator?.UpdateProgress(MathX.Clamp01((float)progress / (float)totalProgress), "", "now loading " + this.existingIUnityObjects.Count.ToString() + "/" + counter.ToString() + " objects for Prefab");
                UnityPackageImporter.Msg("loading object for prefab \"" + ID.Value + "\" with an id of \"" + obj.Value.id.ToString() + "\"");
                try
                {
                    await obj.Value.InstanciateAsync(this);
                }
                catch (Exception e)
                {
                    UnityPackageImporter.Warn("Prefab IUnityObject failed to instanciate!");
                    UnityPackageImporter.Msg("Prefab IUnityObject ID: \"" + obj.Value.id.ToString() + "\"");
                    UnityPackageImporter.Warn(e.Message + e.StackTrace);
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
                catch
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

            // Clothing and other Modular Avatar attachments keep their imported
            // skin bones, but they are not standalone avatars. Adding BipedRig or
            // VRIK here creates an empty avatar rig and gets in the way of merging
            // the attachment onto the user's selected avatar later.
            if (avatarManifest.ShouldSetUpHumanoid)
            {
                UnityPackageImporter.Msg(
                    avatarManifest.IsAvatarPrefab
                        ? "Setting up humanoid rig for VRChat avatar prefab"
                        : "Setting up humanoid rig for ordinary prefab model");

                // Create humanoid stuff for prefabs that are inline.
                await default(ToWorld);
                foreach (var obj in existingIUnityObjects)
                {
                    if (obj.Value.GetType() == typeof(FrooxEngineRepresentation.GameObjectTypes.PrefabInstance))
                    {
                        FrooxEngineRepresentation.GameObjectTypes.PrefabInstance prefab = obj.Value as FrooxEngineRepresentation.GameObjectTypes.PrefabInstance;

                        if (prefab != null && prefab.importask != null && prefab.ImportRoot != null && prefab.ImportRoot.frooxEngineSlot != null)
                        {
                            await UnityProjectImporter.SettupHumanoid(
                                prefab.importask,
                                prefab.ImportRoot.frooxEngineSlot,
                                true);
                        }
                    }
                }

                await default(ToBackground);
            }
            else if (avatarManifest.IsModularAvatarAttachment)
            {
                UnityPackageImporter.Msg("Skipping standalone humanoid rig setup for Modular Avatar attachment");
            }

            var renderersToEnable = existingIUnityObjects.Values
                .OfType<FrooxEngineRepresentation.GameObjectTypes.SkinnedMeshRenderer>()
                .Select(renderer => renderer.createdMeshRenderer)
                .Where(renderer => renderer != null)
                .ToList();
            await SkinnedBoundsPolicy.StabilizeBeforeEnableAsync(
                renderersToEnable,
                "Unity prefab activation");

            foreach (var obj in existingIUnityObjects)
            {
                if (obj.Value.GetType() == typeof(FrooxEngineRepresentation.GameObjectTypes.SkinnedMeshRenderer))
                {
                    var newobj = (obj.Value as FrooxEngineRepresentation.GameObjectTypes.SkinnedMeshRenderer);
                    await default(ToWorld);
                    if (newobj.createdMeshRenderer == null)
                    {
                        UnityPackageImporter.Warn("Skipping missing renderer " + newobj.id + " in prefab " + ID.Value);
                        continue;
                    }
                    newobj.createdMeshRenderer.Enabled = newobj.m_Enabled == 1;
                    await default(ToBackground);
                }
            }

            if (avatarManifest.ShouldSetUpHumanoid)
            {
                progressIndicator?.UpdateProgress(0f, "", "setting up humanoid rig for prefab.");

                foreach (var obj in existingIUnityObjects)
                {
                    if (obj.Value.GetType() != typeof(FrooxEngineRepresentation.GameObjectTypes.SkinnedMeshRenderer))
                        continue;

                    var newobj = (obj.Value as FrooxEngineRepresentation.GameObjectTypes.SkinnedMeshRenderer);
                    if (newobj.createdMeshRenderer == null ||
                        string.IsNullOrEmpty(newobj.m_Mesh?.guid))
                        continue;

                    if (this.unityProjectImporter.SharedImportedFBXScenes.TryGetValue(newobj.m_Mesh.guid, out FileImportTaskScene importedfbx))
                    {
                        await default(ToWorld);
                        await UnityProjectImporter.SettupHumanoid(importedfbx, this.CurrentStructureRootSlot, true);
                        await default(ToBackground);
                        break;
                        // All skinned mesh renderers should go to the current prefab if they're under the root.
                        // I think that is the root above in the if statement with "RootNode" - @989onan
                    }
                    else
                    {
                        UnityPackageImporter.Msg("A prefab (source fbx id: \"" + newobj.m_Mesh.guid + "\") in prefab \"" + this.ID.Value + "\" that probably points to another prefab was attempted to be imported. TODO: FIX THIS"); //TODO: FIX THIS!
                    }
                }
            }

            // Reconstruct each avatar from the references on its own descriptor.
            // A package can contain many unrelated avatars, menus and controllers;
            // choosing the first package-wide menu mixes their behavior together.
            try
            {
                var animClips = AvatarStateReconstructor.ParseAllAnimationClips(this.unityProjectImporter.files);
                foreach (var avatar in avatarManifest.Avatars)
                {
                    if (avatar.GameObjectFileId <= 0 ||
                        !existingIUnityObjects.TryGetValue((ulong)avatar.GameObjectFileId, out IUnityObject descriptorObject) ||
                        descriptorObject is not FrooxEngineRepresentation.GameObjectTypes.GameObject avatarGameObject)
                    {
                        UnityPackageImporter.Warn("Skipping avatar descriptor without a resolvable GameObject in " + ID.Value);
                        continue;
                    }

                    await avatarGameObject.InstanciateAsync(this);
                    Slot targetAvatarSlot = avatarGameObject.frooxEngineSlot;
                    if (targetAvatarSlot == null)
                    {
                        UnityPackageImporter.Warn("Skipping avatar descriptor whose root slot was not created in " + ID.Value);
                        continue;
                    }

                    var (rootMenu, _) = ExpressionMenuParser.LoadMenuHierarchy(
                        avatar.ExpressionsMenu?.Guid,
                        avatar.ExpressionParameters?.Guid,
                        this.unityProjectImporter.AssetIDDict);
                    if (rootMenu == null || rootMenu.Controls.Count == 0) continue;

                    var paramToSlots = AvatarStateReconstructor.BuildParameterToSlotsMap(rootMenu, animClips, targetAvatarSlot);

                    await ContextMenuBuilder.BuildExpressionsMenuAsync(targetAvatarSlot, rootMenu, paramToSlots, this.unityProjectImporter);
                    await default(ToBackground);
                }

            }
            catch (Exception ex)
            {
                UnityPackageImporter.Warn("Failed to reconstruct expressions menu: " + ex);
            }

            if (avatarManifest.ModularAvatarComponents.Count > 0)
            {
                UnityPackageImporter.Msg(
                    "Detected " + avatarManifest.ModularAvatarComponents.Count +
                    " Modular Avatar component(s) in " + Path.GetFileName(ID.Value) +
                    ". Compatibility data was indexed; installation behavior will be applied only by supported component handlers.");

                if (avatarManifest.IsModularAvatarAttachment)
                {
                    try
                    {
                        await ModularAvatarAttachmentInstaller.AttachAsync(
                            this.CurrentStructureRootSlot,
                            avatarManifest,
                            this.existingIUnityObjects,
                            this.unityProjectImporter.AssetIDDict,
                            ID.Key,
                            ID.Value);
                        await default(ToBackground);
                    }
                    catch (Exception ex)
                    {
                        UnityPackageImporter.Warn(
                            "Failed to initialize Modular Avatar attachment installer for " +
                            Path.GetFileName(ID.Value) + ": " + ex);
                    }
                }
            }

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
