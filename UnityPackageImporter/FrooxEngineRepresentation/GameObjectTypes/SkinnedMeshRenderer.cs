using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FrooxEngine;
using UnityPackageImporter.Models;
using UnityPackageImporter.Materials;

namespace UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes;

public partial class SkinnedMeshRenderer : IUnityObject
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
    [YamlDotNet.Serialization.YamlIgnore]
    internal bool HasMissingMeshReference { get; private set; }
    public List<Dictionary<string, ulong>> m_Bones;
    public FrooxEngine.SkinnedMeshRenderer createdMeshRenderer;
    public SourceObj m_CorrespondingSourceObject { get; set; }
    public Dictionary<string, ulong> m_PrefabInstance { get; set; }

    public AABB m_AABB;


    public async Task InstanciateAsync(IUnityStructureImporter importer)
    {
        if (instanciated) return;
        if (importer is UnitySceneImportTask scene && scene.TryDeferObject(this)) return;
        bool NotFromInLinePrefab = true;
        instanciated = true;

        if (IsStaticMesh)
        {
            await InstantiateStaticAsync(importer);
            NotFromInLinePrefab = false;
        }

        if (!IsStaticMesh && m_CorrespondingSourceObject != null)
            {
                if (m_CorrespondingSourceObject.guid != null)
                {
                    UnityPackageImporter.Msg("Importing skinned mesh renderer via corrosponding object. this id is: \"" + this.id + "\"");
                    NotFromInLinePrefab = false;
                    if (m_PrefabInstance != null &&
                        m_PrefabInstance.TryGetValue("fileID", out ulong piId) &&
                        importer.existingIUnityObjects.TryGetValue(piId, out IUnityObject parentobj_inc))
                    {
                        PrefabInstance prefab;
                        try
                        {
                            await default(ToWorld);
                            await parentobj_inc.InstanciateAsync(importer);
                            await default(ToWorld);
                            prefab = parentobj_inc as PrefabInstance;

                            if (prefab != null && prefab.TryFindObject(this.m_CorrespondingSourceObject, out IUnityObject gameobj))
                            {
                                try
                                {
                                    FrooxEngineRepresentation.GameObjectTypes.SkinnedMeshRenderer newskin = (gameobj as FrooxEngineRepresentation.GameObjectTypes.SkinnedMeshRenderer);
                                    await default(ToWorld);
                                    await gameobj.InstanciateAsync(importer);
                                    await default(ToWorld);
                                    if (newskin != null)
                                    {
                                        this.createdMeshRenderer = newskin.createdMeshRenderer;
                                        this.SourceBlendShapeNames = newskin.SourceBlendShapeNames;
                                        this.materials = new List<FileImportHelperTaskMaterial>(newskin.materials);
                                        var mergedMaterials = new List<SourceObj>(newskin.m_Materials);
                                        if (this.m_Materials != null)
                                        {
                                            for (int matIdx = 0; matIdx < this.m_Materials.Count; matIdx++)
                                            {
                                                while (mergedMaterials.Count <= matIdx)
                                                    mergedMaterials.Add(new SourceObj());
                                                if (!string.IsNullOrEmpty(this.m_Materials[matIdx]?.guid))
                                                    mergedMaterials[matIdx] = this.m_Materials[matIdx];
                                            }
                                        }
                                        this.m_Materials = mergedMaterials;
                                        if (newskin.createdMeshRenderer?.Slot != null)
                                            UnityPackageImporter.Warn("assigned skinned mesh renderer \"" + newskin.createdMeshRenderer.Slot.Name + "\" some properties from one already created by a prefab.");
                                    }
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
                if (m_Mesh?.guid != null && importer.unityProjectImporter.SharedImportedFBXScenes.TryGetValue(m_Mesh.guid, out FileImportTaskScene importedfbx2))
                {
                    if (m_GameObject != null && m_GameObject.TryGetValue("fileID", out ulong goId) && importer.existingIUnityObjects.TryGetValue(goId, out IUnityObject parentobj_inc))
                    {
                        parentobj = parentobj_inc as GameObject;
                    }

                    if (parentobj != null)
                    {
                        UnityPackageImporter.Msg("Importing skinned mesh renderer via mesh guid. this id is: \"" + this.id + "\", parentobj = " + parentobj.ToString());
                        var candidateMeshIds = new List<long>();
                        foreach (var pair in importedfbx2.FILEID_To_Slot_Pairs)
                        {
                            if (pair.Value.GetType() == typeof(SkinnedMeshRenderer))
                            {
                                UnityPackageImporter.Msg("Importing skinned mesh renderer via mesh guid 2. this id is: \"" + this.id + "\", parentobj = " + parentobj.ToString());

                                FrooxEngineRepresentation.GameObjectTypes.SkinnedMeshRenderer newskin = (pair.Value as FrooxEngineRepresentation.GameObjectTypes.SkinnedMeshRenderer);
                                candidateMeshIds.Add(newskin.m_Mesh.fileID);
                                if (newskin.m_Mesh.fileID == this.m_Mesh.fileID)
                                {
                                    UnityPackageImporter.Msg("Assigning parent object for skinned mesh renderer: \"" + this.id + "\"");

                                    await default(ToWorld);
                                    await parentobj.InstanciateAsync(importer);
                                    await default(ToWorld);

                                    if (newskin.createdMeshRenderer?.Slot != null && parentobj.frooxEngineSlot != null)
                                    {
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
                        if (MeshReferenceLookup.Evaluate(true, m_Mesh.fileID, candidateMeshIds) == MeshReferenceOutcome.MissingReference)
                        {
                            await default(ToWorld);
                            await parentobj.InstanciateAsync(importer);
                            await default(ToWorld);
                            if (parentobj.frooxEngineSlot != null && !parentobj.frooxEngineSlot.IsDestroyed)
                            {
                                HasMissingMeshReference = true;
                                UnityPackageImporter.Warn($"Missing mesh reference for renderer {id}: fileID {m_Mesh.fileID}, model GUID {m_Mesh.guid}, candidate meshes {candidateMeshIds.Count}. Renderer skipped; owning object retained.");
                                return;
                            }
                        }
                    }
                    else
                    {
                        UnityPackageImporter.Warn("Skinned renderer " + id + " has no parent GameObject.");
                    }
                }
                else
                {
                    UnityPackageImporter.Warn("The prefab is malformed!!! a skinned mesh render with a guid of \"" + m_Mesh?.guid + "\" could not find the pre-imported FBX!");
                }

                if (createdMeshRenderer == null)
                {
                    UnityPackageImporter.Warn("No imported mesh matches renderer " + id + ".");
                }
            }

        FrooxEngine.SkinnedMeshRenderer FoundMesh = this.createdMeshRenderer;
        if (FoundMesh == null)
        {
            throw new InvalidDataException($"Skinned renderer {id} could not be reconstructed (mesh: {m_Mesh?.guid}).");
        }


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
                        importer.unityProjectImporter.ClearMissingMaterial(FoundMesh, i);
                        string filemat = importer.unityProjectImporter.AssetIDDict[material.guid];
                        if (MaterialSlotAssignment.ReplacesExistingSlot(i, materials.Count))
                        {
                            materials[i] = new FileImportHelperTaskMaterial(material.guid, filemat, importer.unityProjectImporter);
                            UnityPackageImporter.Msg("Imported a material at index \"" + i + "\" on skinnedmeshrenderer named \"" + FoundMesh.Slot.Name + "\" by replacing one of the indices");
                        }
                        else
                        {
                            materials.Add(new FileImportHelperTaskMaterial(material.guid, filemat, importer.unityProjectImporter));
                            UnityPackageImporter.Msg("Imported a material at index \"" + i + "\" on skinnedmeshrenderer named \"" + FoundMesh.Slot.Name + "\" by tacking onto the end.");
                        }

                        await default(ToBackground);
                    }
                    else if (MaterialDependencyDiagnostics.IsBuiltinResource(material.guid, material.type))
                    {
                        await default(ToWorld);
                        importer.unityProjectImporter.ClearMissingMaterial(FoundMesh, i);
                        UnityPackageImporter.Msg("Importing a material at index \"" + i + "\" on skinnedmeshrenderer named \"" + FoundMesh.Slot.Name + "\" is a built-in Unity resource (type " + material.type + ", guid " + material.guid + "). Using fallback material without reporting missing package dependency.");
                        if (MaterialSlotAssignment.ReplacesExistingSlot(i, materials.Count))
                        {
                            materials[i] = new FileImportHelperTaskMaterial(importer.unityProjectImporter);
                        }
                        else
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
                        if (MaterialSlotAssignment.ReplacesExistingSlot(i, materials.Count))
                        {
                            materials[i] = new FileImportHelperTaskMaterial(importer.unityProjectImporter);
                        }
                        else
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
            importer.progressIndicator?.UpdateProgress(0f, "", $"Loading material {counter} on {FoundMesh.Slot.Name}");
            await MaterialSlotAssignment.AssignAsync<IAssetProvider<Material>>(counter,
                () => materialtask.runImportFileMaterialsAsync(),
                async error =>
                {
                    UnityPackageImporter.Warn($"Material conversion failed at slot {counter} on '{FoundMesh.Slot.Name}': {error.Message}");
                    await default(ToWorld);
                    importer.unityProjectImporter.Session?.Check();
                    return await new FileImportHelperTaskMaterial(importer.unityProjectImporter).runImportFileMaterialsAsync();
                },
                async (index, material) =>
                {
                    await default(ToWorld);
                    importer.unityProjectImporter.Session?.Check();
                    while (FoundMesh.Materials.Count <= index) FoundMesh.Materials.Add();
                    FoundMesh.Materials[index] = material;
                    await default(ToBackground);
                }, importer.unityProjectImporter.Session?.Token ?? default);
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
