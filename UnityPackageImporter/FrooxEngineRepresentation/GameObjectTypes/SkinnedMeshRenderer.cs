using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FrooxEngine;
using UnityPackageImporter.Models;

namespace UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes;

public class SkinnedMeshRenderer : IUnityObject
{
    public ulong id { get; set; }
    public bool instanciated { get; set; }
    public Dictionary<string, ulong> m_GameObject;
    public int m_Enabled = 1;
    public List<FileImportHelperTaskMaterial> materials = new List<FileImportHelperTaskMaterial>();
    public List<SourceObj> m_Materials = new List<SourceObj>();
    public List<float> m_BlendShapeWeights;
    [YamlDotNet.Serialization.YamlIgnore]
    public string[] SourceBlendShapeNames;
    public SourceObj m_Mesh;
    public List<Dictionary<string, ulong>> m_Bones;
    public FrooxEngine.SkinnedMeshRenderer createdMeshRenderer;
    public SourceObj m_CorrespondingSourceObject { get; set; }
    public Dictionary<string, ulong> m_PrefabInstance { get; set; }

    public AABB m_AABB;


    public async Task InstanciateAsync(IUnityStructureImporter importer)
    {
        if (instanciated) return;
        bool NotFromInLinePrefab = true;
        instanciated = true;

        try
        {

            if (m_CorrespondingSourceObject != null)
            {
                if (m_CorrespondingSourceObject.guid != null)
                {
                    UnityPackageImporter.Msg("Importing skinned mesh renderer via corrosponding object. this id is: \"" + this.id + "\"");
                    NotFromInLinePrefab = false;
                    if (importer.existingIUnityObjects.TryGetValue(m_PrefabInstance["fileID"], out IUnityObject parentobj_inc))
                    {
                        PrefabInstance prefab;
                        try
                        {
                            await default(ToWorld);
                            await parentobj_inc.InstanciateAsync(importer);
                            await default(ToWorld);
                            prefab = parentobj_inc as PrefabInstance;

                            if (prefab.PrefabHashes.TryGetValue(this.m_CorrespondingSourceObject, out IUnityObject gameobj))
                            {
                                try
                                {
                                    FrooxEngineRepresentation.GameObjectTypes.SkinnedMeshRenderer newskin = (gameobj as FrooxEngineRepresentation.GameObjectTypes.SkinnedMeshRenderer);
                                    await default(ToWorld);
                                    await gameobj.InstanciateAsync(importer);
                                    await default(ToWorld);
                                    this.createdMeshRenderer = newskin.createdMeshRenderer;
                                    this.SourceBlendShapeNames = newskin.SourceBlendShapeNames;
                                    this.materials = new List<FileImportHelperTaskMaterial>(newskin.materials);
                                    this.m_Materials = new List<SourceObj>(newskin.m_Materials);
                                    UnityPackageImporter.Warn("assigned skinned mesh renderer \"" + newskin.createdMeshRenderer.Slot.Name + "\" some properties from one already created by a prefab.");
                                }
                                catch (Exception)
                                {
                                    UnityPackageImporter.Warn("The prefab is malformed!!! a skinned mesh render with a guid of \"" + m_CorrespondingSourceObject.guid + "\" could not assign itself to a prefab hash!");
                                    throw;
                                }
                            }
                            else
                            {
                                UnityPackageImporter.Warn("The prefab is malformed!!! a skinned mesh render with a guid of \"" + m_CorrespondingSourceObject.guid + "\" could not find/create it's parent game obj!");
                            }
                        }
                        catch (Exception)
                        {
                            UnityPackageImporter.Warn("The prefab is malformed!!! a skinned mesh render with a guid of \"" + m_CorrespondingSourceObject.guid + "\" could not create it's parent game obj!");
                            throw;
                        }
                    }
                    else
                    {
                        UnityPackageImporter.Warn("The prefab is malformed!!! a skinned mesh render with a guid of \"" + m_CorrespondingSourceObject.guid + "\" could not find it's parent prefab obj!");
                    }

                }

            }
            GameObject parentobj = null;
            if (NotFromInLinePrefab)
            {
                UnityPackageImporter.Msg("Importing skinned mesh renderer via mesh guid. this id is: \"" + this.id + "\"");
                if (importer.unityProjectImporter.SharedImportedFBXScenes.TryGetValue(m_Mesh.guid, out FileImportTaskScene importedfbx2))
                {


                    importer.existingIUnityObjects.TryGetValue(m_GameObject["fileID"], out IUnityObject parentobj_inc);
                    parentobj = parentobj_inc as GameObject;
                    if (parentobj == null) throw new System.IO.InvalidDataException("Skinned renderer has no parent GameObject.");

                    UnityPackageImporter.Msg("Importing skinned mesh renderer via mesh guid. this id is: \"" + this.id + "\", parentobj = " + parentobj.ToString());
                    foreach (var pair in importedfbx2.FILEID_To_Slot_Pairs)
                    {
                        if (pair.Value.GetType() == typeof(SkinnedMeshRenderer))
                        {
                            UnityPackageImporter.Msg("Importing skinned mesh renderer via mesh guid 2. this id is: \"" + this.id + "\", parentobj = " + parentobj.ToString());

                            FrooxEngineRepresentation.GameObjectTypes.SkinnedMeshRenderer newskin = (pair.Value as FrooxEngineRepresentation.GameObjectTypes.SkinnedMeshRenderer);
                            if (newskin.m_Mesh.fileID == this.m_Mesh.fileID)
                            {

                                UnityPackageImporter.Msg("Assigning parent object for skinned mesh renderer: \"" + this.id + "\"");

                                await default(ToWorld);
                                await parentobj.InstanciateAsync(importer);
                                await default(ToWorld);

                                Slot newslot = newskin.createdMeshRenderer.Slot.Duplicate(parentobj.frooxEngineSlot.Parent, false, new DuplicationSettings());
                                newslot.TRS = parentobj.frooxEngineSlot.TRS;
                                foreach (Slot child in parentobj.frooxEngineSlot.Children.ToArray())
                                {
                                    child.SetParent(newslot, false);
                                }
                                await default(ToWorld);
                                parentobj.frooxEngineSlot.Destroy();
                                parentobj.frooxEngineSlot = newslot;
                                this.createdMeshRenderer = newslot.GetComponent<FrooxEngine.SkinnedMeshRenderer>();
                                this.SourceBlendShapeNames = newskin.SourceBlendShapeNames;
                                await default(ToBackground);
                                break;

                            }
                        }
                    }
                }
                else
                {
                    UnityPackageImporter.Warn("The prefab is malformed!!! a skinned mesh render with a guid of \"" + m_Mesh.guid + "\" could not find the pre-imported FBX!");
                }

                if (createdMeshRenderer == null)
                    throw new System.IO.InvalidDataException("No imported mesh matches renderer " + id + ".");
            }





        }
        catch (Exception)
        {
            try
            {
                UnityPackageImporter.Warn("The prefab is malformed!!! a skinned mesh render couldn't find it's parent an id of \"" + m_GameObject["fileID"].ToString() + "\" Your renderer will come out mis-shapen!");
            }
            catch
            {
                try
                {
                    UnityPackageImporter.Warn("The prefab is malformed!!! a skinned mesh render couldn't find it's parent a source obj of \"" + m_CorrespondingSourceObject.ToString() + "\" Your renderer will come out mis-shapen!");
                }
                catch
                {
                    UnityPackageImporter.Warn("The prefab is malformed!!! a skinned mesh render does not have a valid parent obj type! Your renderer will come out mis-shapen!");
                }
            }
            //UnityPackageImporter.Warn(ex.Message, ex.StackTrace);
            throw;
        }

        FrooxEngine.SkinnedMeshRenderer FoundMesh = this.createdMeshRenderer;
        if (FoundMesh == null)
            throw new System.IO.InvalidDataException("Skinned renderer " + id + " could not be reconstructed.");


        int counter = 0;
        if (NotFromInLinePrefab) //this basically says that this skinned renderer is not coming from an inline prefab from a scene import
        {

            //FoundMesh.Mesh.ReferenceID = (skinnedMeshRenderer as FrooxEngineRepresentation.GameObjectTypes.SkinnedMeshRenderer).createdMeshRenderer.Mesh.ReferenceID;
            await default(ToWorld);
            importer.progressIndicator?.UpdateProgress(0f, "", "now loading a skinned mesh renderer named \"" + FoundMesh.Slot.Name + "\" ");
            await ImportMeshReadiness.WaitAsync(new[] { FoundMesh }, importer.unityProjectImporter.Session?.Token ?? default);
            await default(ToWorld);
            //below comment is done at the humanoid stage, so that shite doesn't get in the way of players - @989onan
            //FoundMesh.Enabled = this.m_Enabled == 1; //in case it's disabled in unity, this will make it disabled when imported;
            FoundMesh.Enabled = false;


            await default(ToBackground);

            await default(ToWorld);
            var authored = new List<(string Name, Slot Bone)>();
            foreach (var reference in m_Bones ?? new List<Dictionary<string, ulong>>())
            {
                if (!reference.TryGetValue("fileID", out var fileId) ||
                    !importer.existingIUnityObjects.TryGetValue(fileId, out var entry) || entry is not Transform transform)
                    continue; // Native import may strip unused bones; required names are validated below.
                await transform.InstanciateAsync(importer);
                await default(ToWorld);
                importer.unityProjectImporter.Session?.Check();
                if (!importer.existingIUnityObjects.TryGetValue(transform.m_GameObjectID, out var gameEntry) ||
                    gameEntry is not GameObject game || game.frooxEngineSlot == null)
                    continue;
                authored.Add((game.frooxEngineSlot.Name, game.frooxEngineSlot));
            }
            var mapped = SkinBoneMapping.Resolve(FoundMesh.Mesh.Asset.Data.Bones.Select(bone => bone.Name), authored);
            FoundMesh.Bones.Clear();
            foreach (var bone in mapped) FoundMesh.Bones.Add().Target = bone;
            SkinnedBoundsPolicy.ApplyAfterBones(FoundMesh, "Unity prefab import");
            await default(ToBackground);

            UnityPackageImporter.Msg("Setting up blend shapes");
            await default(ToWorld);
            FrooxInternalBridge.SetupBlendShapes(FoundMesh);
            await default(ToBackground);




            await default(ToBackground);
        }

        await default(ToWorld);
        BlendShapeDefaults.ApplyNamed(m_BlendShapeWeights, SourceBlendShapeNames, FoundMesh.BlendShapeIndex,
            (index, weight) => FoundMesh.BlendShapeWeights[index] = weight,
            warning => UnityPackageImporter.Warn($"{FoundMesh.Slot.Name}: {warning}"));
        await default(ToBackground);

        int submeshCount = FoundMesh.Mesh.Asset?.Data?.SubmeshCount ?? int.MaxValue;
        for (int i = 0; i < m_Materials.Count; i++)
        {
            if (i >= submeshCount)
            {
                UnityPackageImporter.Msg("Skipping material at slot index \"" + i + "\" beyond submesh count (" + submeshCount + ") on skinnedmeshrenderer \"" + FoundMesh.Slot.Name + "\"");
                break;
            }
            SourceObj material = m_Materials[i];
            try
            {

                if (!string.IsNullOrEmpty(material?.guid))
                {
                    if (importer.unityProjectImporter.AssetIDDict.ContainsKey(material.guid))
                    {
                        await default(ToWorld);
                        string filemat = importer.unityProjectImporter.AssetIDDict[material.guid];
                        try
                        {
                            materials.RemoveAt(i);
                            materials.Insert(i, new FileImportHelperTaskMaterial(material.guid, filemat, importer.unityProjectImporter));
                            UnityPackageImporter.Msg("Imported a material at index \"" + i + "\" on skinnedmeshrenderer named \"" + FoundMesh.Slot.Name + "\" by replacing one of the indices");
                        }
                        catch
                        {
                            materials.Add(new FileImportHelperTaskMaterial(material.guid, filemat, importer.unityProjectImporter));
                            UnityPackageImporter.Msg("Imported a material at index \"" + i + "\" on skinnedmeshrenderer named \"" + FoundMesh.Slot.Name + "\" by tacking onto the end.");
                        }

                        await default(ToBackground);
                    }
                    else if (MaterialDependencyDiagnostics.IsBuiltinResource(material.guid, material.type))
                    {
                        await default(ToWorld);
                        UnityPackageImporter.Msg("Importing a material at index \"" + i + "\" on skinnedmeshrenderer named \"" + FoundMesh.Slot.Name + "\" is a built-in Unity resource (type " + material.type + ", guid " + material.guid + "). Using fallback material without reporting missing package dependency.");
                        try
                        {
                            materials.RemoveAt(i);
                            materials.Insert(i, new FileImportHelperTaskMaterial(importer.unityProjectImporter));
                        }
                        catch
                        {
                            materials.Add(new FileImportHelperTaskMaterial(importer.unityProjectImporter));
                        }
                        await default(ToBackground);
                    }
                    else
                    {
                        await default(ToWorld);
                        UnityPackageImporter.Msg("Importing a material at index \"" + i + "\" on skinnedmeshrenderer named \"" + FoundMesh.Slot.Name + "\" has an overridden material but we can't find the material that has the override's GUID. (is it in the package?)");
                        importer.unityProjectImporter.RegisterMissingMaterial(FoundMesh, i, material.guid);
                        try
                        {
                            materials.RemoveAt(i);
                            materials.Insert(i, new FileImportHelperTaskMaterial(importer.unityProjectImporter));
                        }
                        catch
                        {
                            materials.Add(new FileImportHelperTaskMaterial(importer.unityProjectImporter));
                        }
                        await default(ToBackground);
                    }
                }
                else
                {
                    UnityPackageImporter.Msg("Importing a material at index \"" + i + "\" on skinnedmeshrenderer named \"" + FoundMesh.Slot.Name + "\" did not override. Using material generated at import.");
                }


            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                UnityPackageImporter.Warn("Importing a material at index \"" + i + "\" on skinnedmeshrenderer named \"" + FoundMesh.Slot.Name + "\" threw an error!");
                UnityPackageImporter.Warn(e.Message + e.StackTrace);
            }


        }

        UnityPackageImporter.Msg("clearing bad material objects for: \"" + FoundMesh.Slot.Name + "\"");
        await default(ToWorld);
        FoundMesh.Materials.Clear();
        await default(ToBackground);
        counter = 0;
        UnityPackageImporter.Msg("getting good material objects for: \"" + FoundMesh.Slot.Name + "\" it has \"" + this.materials.Count.ToString() + " \" materials in the generated list to instanciate and m_Materials being instanciated is: \"" + (m_Materials.Count > 0) + "\" ");
        foreach (FileImportHelperTaskMaterial materialtask in this.materials)
        {
            try
            {
                importer.progressIndicator?.UpdateProgress(0f, "", "now loading material " + counter.ToString() + " on a skinned mesh renderer named \"" + FoundMesh.Slot.Name + "\" ");
                await default(ToWorld);
                UnityPackageImporter.Msg("assigning material slot for: \"" + FoundMesh.Slot.Name + "\"");
                FoundMesh.Materials.Add().Target = await materialtask.runImportFileMaterialsAsync();
                await default(ToBackground);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                UnityPackageImporter.Warn("Could not attach material \"" + counter.ToString() + "\" on mesh \"" + FoundMesh.Slot.Name + "\" from skinned mesh renderer data. It's probably not in the project or in the files you dragged over.");
                UnityPackageImporter.Warn("stacktrace for material \"" + counter.ToString() + "\" on mesh \"" + FoundMesh.Slot.Name + "\"");
                UnityPackageImporter.Warn(e.Message);
                FoundMesh.Materials.Add().Target = await new FileImportHelperTaskMaterial(importer.unityProjectImporter).runImportFileMaterialsAsync();
                await default(ToBackground);
            }
            counter++;
        }

        UnityPackageImporter.Msg("Skinned Mesh Renderer \"" + FoundMesh.Slot.Name + "\" imported, and imported \"" + counter.ToString() + "\" materials with it.");
    }

    public override string ToString()
    {
        StringBuilder result = new StringBuilder();
        result.AppendLine("id: " + id.ToString());
        result.AppendLine("instanciated: " + instanciated.ToString());

        if (m_CorrespondingSourceObject != null)
            result.AppendLine("m_CorrespondingSourceObject: " + m_CorrespondingSourceObject.ToString());
        else
            result.AppendLine("m_CorrespondingSourceObject: null");

        if (m_PrefabInstance != null)
            result.AppendLine("m_PrefabInstance: " + m_PrefabInstance.ToString());
        else
            result.AppendLine("m_PrefabInstance: null");


        if (this.m_GameObject != null)
        {
            if (this.m_GameObject.ContainsKey("fileID"))
            {
                result.AppendLine("m_GameObjectID: " + this.m_GameObject["fileID"].ToString());
            }
        }
        else
        {
            result.AppendLine("m_GameObjectID: null");
        }

        if (m_Mesh != null)
            result.AppendLine("m_Mesh: " + m_Mesh.ToString());
        else
            result.AppendLine("m_Mesh: null");

        if (createdMeshRenderer != null)
            result.AppendLine("createdMeshRenderer" + createdMeshRenderer.ToString());
        else
            result.AppendLine("createdMeshRenderer: null");

        return result.ToString();
    }


}
//to store aabb data to bring into froox engine for skinned mesh renderers
public class AABB
{
    public Dictionary<string, float> m_Center;
    public Dictionary<string, float> m_Extent;
    public AABB()
    {

    }
}
