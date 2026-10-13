using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FrooxEngine;
using UnityPackageImporter.Models;

namespace UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes;

public class PrefabInstance : IUnityObject
{
    public bool instanciated { get; set; }
    public ulong id { get; set; }
    public int m_ObjectHideFlags { get; set; }
    public int serializedVersion { get; set; }
    public SourceObj m_CorrespondingSourceObject { get; set; }
    public SourceObj m_SourcePrefab { get; set; }
    public FileImportTaskScene importask = null;
    public GameObject ImportRoot { get; set; }
    public ModPrefab m_Modification { get; set; }
    public Dictionary<string, ulong> m_PrefabInstance { get; set; }

    public Dictionary<SourceObj, IUnityObject> PrefabHashes = new Dictionary<SourceObj, IUnityObject>(new SourceObjCompare());

    public async Task InstanciateAsync(IUnityStructureImporter importer)
    {
        if (instanciated) return;

        await default(ToBackground);

        UnityPackageImporter.Msg("is m_SourcePrefab instanciated?: " + (m_SourcePrefab != null));
        UnityPackageImporter.Msg("is m_Modification instanciated?: " + (m_Modification != null));
        UnityPackageImporter.Msg("is m_ObjectHideFlags instanciated?: " + m_ObjectHideFlags.ToString());
        UnityPackageImporter.Msg("is serializedVersion instanciated?: " + serializedVersion.ToString());

        if (m_SourcePrefab == null)
        {
            instanciated = true;
            return;
        }

        if (importer.unityProjectImporter.AssetIDDict.ContainsKey(m_SourcePrefab.guid))
        {
            UnityPackageImporter.Msg("starting instanciation of inline prefab \"" + id.ToString() + "\"");
            // Find FBX's in our scene that need importing, so we can import them and then attach our prefab objects to it.
            // We are sharing the FBX's, so if it's a project with X prefabs of the same model but with different changes can all use the same base model
            // Next we associate each shared FBX to prefabs that use them. then we duplicate the shared FBX's to each scene including this one, and then those duplicated FBXs become the prefabs themselves
            // Instanciate our objects to generate our prefab entirely, using the ids we assigned ealier to identify our prefab elements in our list.
            Slot targetParent;
            if (m_Modification == null)
            {
                targetParent = importer.CurrentStructureRootSlot;
            }
            else if (importer.existingIUnityObjects.TryGetValue(m_Modification.m_TransformParent["fileID"], out IUnityObject foundtransformparent))
            {
                await default(ToWorld);
                await foundtransformparent.InstanciateAsync(importer);
                await default(ToBackground);
                if (importer.existingIUnityObjects.TryGetValue((foundtransformparent as Transform).m_GameObjectID, out IUnityObject parentobjnew))
                {
                    await default(ToWorld);
                    await parentobjnew.InstanciateAsync(importer);
                    await default(ToBackground);
                    targetParent = (parentobjnew as GameObject).frooxEngineSlot;
                }
                else
                {
                    UnityPackageImporter.Warn("targetParent had a value for prefab \"" + id.ToString() + "\" but could not find the id \"" + (foundtransformparent as Transform).m_GameObjectID + "\" in the list of game objects that exist in scene!");
                    targetParent = importer.CurrentStructureRootSlot;
                }
            }
            else
            {
                targetParent = importer.CurrentStructureRootSlot;
            }

            UnityPackageImporter.Msg("is targetParent instanciated?: " + (targetParent != null));

            if (importask != null) return;

            ImportRoot = new GameObject();
            string resolvedFbxGuid = ResolveFBXGuid(importer, m_SourcePrefab?.guid);
            if (!string.IsNullOrEmpty(resolvedFbxGuid) && importer.unityProjectImporter.SharedImportedFBXScenes.ContainsKey(resolvedFbxGuid))
            {
                importask = await importer.unityProjectImporter.SharedImportedFBXScenes[resolvedFbxGuid].MakeCopyAndPopulatePrefabData();
                ImportRoot.frooxEngineSlot = importask.FinishedFileSlot;
                await default(ToWorld);
                // The template is authored in prefab-local space. Keeping its world
                // transform cancels the import container's placement near the user.
                ImportRoot.frooxEngineSlot.SetParent(targetParent, false);
                await default(ToBackground);

                SourceObj importrootsource = new SourceObj();
                importrootsource.guid = this.m_SourcePrefab.guid;
                importrootsource.fileID = 919132149155446097; // Hash of root object path
                importrootsource.type = 3;

                this.PrefabHashes = importask.FILEID_To_Slot_Pairs;
                this.PrefabHashes.Add(importrootsource, ImportRoot);
                List<IUnityObject> modifiedObjects = new List<IUnityObject>();

                var lineageGuids = PrefabVariantResolver.GetLineageGuids(
                    this.m_SourcePrefab?.guid,
                    resolvedFbxGuid,
                    importer.unityProjectImporter?.ListOfPrefabs);

                // Dictionary to map ancestor file IDs (and GUIDs) to the instantiated objects in PrefabHashes
                var ancestorTargetMap = new Dictionary<SourceObj, IUnityObject>(new SourceObjCompare());
                var ancestorIdToTarget = new Dictionary<long, IUnityObject>();
                var hashIdToTarget = new Dictionary<long, IUnityObject>();
                foreach (var kvp in this.PrefabHashes)
                {
                    PrefabVariantResolver.RegisterUniqueTarget(hashIdToTarget, kvp.Key.fileID, kvp.Value);
                }

                var ancestorPiIds = new HashSet<long>();
                if (this.id != 0)
                    ancestorPiIds.Add((long)this.id);

                // 1. Replay modifications from ancestor prefabs (oldest ancestor first)
                var ancestors = PrefabVariantResolver.GetAncestorPrefabs(
                    this.m_SourcePrefab?.guid,
                    importer.unityProjectImporter?.ListOfPrefabs);

                foreach (var (_, ancestorFile) in ancestors)
                {
                    foreach (var piId in PrefabVariantResolver.FindPrefabInstanceIds(ancestorFile))
                    {
                        ancestorPiIds.Add(piId);
                    }
                }

                foreach (var (ancestorGuid, ancestorFile) in ancestors)
                {
                    try
                    {
                        var ancestorObjects = YamlToFrooxEngine.parseYaml(ancestorFile);
                        var goNames = new Dictionary<ulong, string>();
                        foreach (var kvp in ancestorObjects)
                        {
                            if (kvp.Value is GameObject go && !string.IsNullOrEmpty(go.m_Name))
                                goNames[kvp.Key] = go.m_Name;
                            if (kvp.Value is PrefabInstance pi && pi.id != 0)
                                ancestorPiIds.Add((long)pi.id);
                            if (kvp.Value?.m_PrefabInstance != null && kvp.Value.m_PrefabInstance.TryGetValue("fileID", out ulong piVal) && piVal != 0)
                                ancestorPiIds.Add((long)piVal);
                        }

                        // Pre-populate derived IDs for any ancestor PrefabInstances
                        foreach (long piId in ancestorPiIds)
                        {
                            foreach (var kvp in this.PrefabHashes)
                            {
                                long derivedId = PrefabVariantResolver.ComputeDerivedFileId(piId, kvp.Key.fileID);
                                PrefabVariantResolver.RegisterUniqueTarget(ancestorIdToTarget, derivedId, kvp.Value);
                                PrefabVariantResolver.RegisterUniqueTarget(ancestorTargetMap, new SourceObj(derivedId, ancestorGuid, kvp.Key.type), kvp.Value);
                                PrefabVariantResolver.RegisterUniqueTarget(ancestorTargetMap, new SourceObj(derivedId, resolvedFbxGuid, kvp.Key.type), kvp.Value);
                                PrefabVariantResolver.RegisterUniqueTarget(ancestorTargetMap, new SourceObj(derivedId, "", kvp.Key.type), kvp.Value);
                            }
                        }

                        var hashSkins = this.PrefabHashes.Values.OfType<SkinnedMeshRenderer>().ToList();
                        var hashGos = this.PrefabHashes.Values.OfType<GameObject>().ToList();

                        foreach (var kvp in ancestorObjects)
                        {
                            if (kvp.Value is PrefabInstance ancestorPi && ancestorPi.m_Modification?.m_Modifications != null)
                            {
                                ApplyModifications(ancestorPi.m_Modification.m_Modifications, importer, resolvedFbxGuid, lineageGuids, modifiedObjects, ancestorTargetMap, ancestorIdToTarget, ancestorPiIds, hashIdToTarget);
                            }
                            else if (kvp.Value is SkinnedMeshRenderer ancestorSkin)
                            {
                                SkinnedMeshRenderer targetSkin = null;
                                if (ancestorSkin.m_CorrespondingSourceObject != null && ancestorSkin.m_CorrespondingSourceObject.fileID != 0)
                                {
                                    if (this.PrefabHashes.TryGetValue(new SourceObj(ancestorSkin.m_CorrespondingSourceObject.fileID, resolvedFbxGuid, 3), out var direct) && direct is SkinnedMeshRenderer dSkin)
                                        targetSkin = dSkin;
                                    if (targetSkin == null && hashIdToTarget.TryGetValue(ancestorSkin.m_CorrespondingSourceObject.fileID, out var directObj) && directObj is SkinnedMeshRenderer dSkin2)
                                        targetSkin = dSkin2;
                                    if (targetSkin == null && ancestorIdToTarget.TryGetValue(ancestorSkin.m_CorrespondingSourceObject.fileID, out var mappedObj) && mappedObj is SkinnedMeshRenderer dSkin3)
                                        targetSkin = dSkin3;
                                    if (targetSkin == null && ancestorIdToTarget.TryGetValue(ancestorSkin.m_CorrespondingSourceObject.fileID, out var conflictedSkin) && conflictedSkin == null)
                                        continue;

                                    if (targetSkin == null && ancestorPiIds.Count > 0)
                                    {
                                        PrefabVariantResolver.TryResolveDerivedTarget(ancestorSkin.m_CorrespondingSourceObject.fileID,
                                            ancestorPiIds, candidateId => FindDerivedCandidates(candidateId, null, resolvedFbxGuid,
                                                ancestorIdToTarget, hashIdToTarget), out IUnityObject derivedTarget, out bool ambiguous);
                                        if (ambiguous) continue;
                                        targetSkin = derivedTarget as SkinnedMeshRenderer;
                                    }
                                }

                                if (targetSkin == null && ancestorSkin.m_Mesh != null && ancestorSkin.m_Mesh.fileID != 0)
                                {
                                    var matchingMeshSkins = hashSkins.Where(h => h.m_Mesh != null && h.m_Mesh.fileID == ancestorSkin.m_Mesh.fileID).ToList();
                                    if (matchingMeshSkins.Count == 1)
                                    {
                                        targetSkin = matchingMeshSkins[0];
                                    }
                                    else if (matchingMeshSkins.Count > 1)
                                    {
                                        UnityPackageImporter.Warn($"Ambiguous ancestor SkinnedMeshRenderer match by mesh fileID {ancestorSkin.m_Mesh.fileID} ({matchingMeshSkins.Count} candidates); skipping heuristic match to prevent incorrect binding.");
                                    }
                                }

                                if (targetSkin == null && ancestorSkin.m_GameObject != null && ancestorSkin.m_GameObject.TryGetValue("fileID", out var goId) && goNames.TryGetValue(goId, out var goName))
                                {
                                    var matchingByName = hashSkins.Where(h => h.createdMeshRenderer?.Slot?.Name == goName).ToList();
                                    if (matchingByName.Count == 1)
                                    {
                                        targetSkin = matchingByName[0];
                                    }
                                    else if (matchingByName.Count == 0)
                                    {
                                        var matchingByCiName = hashSkins.Where(h => h.createdMeshRenderer?.Slot?.Name?.Equals(goName, StringComparison.OrdinalIgnoreCase) == true).ToList();
                                        if (matchingByCiName.Count == 1)
                                        {
                                            targetSkin = matchingByCiName[0];
                                        }
                                        else if (matchingByCiName.Count > 1)
                                        {
                                            UnityPackageImporter.Warn($"Ambiguous ancestor SkinnedMeshRenderer match by case-insensitive name '{goName}' ({matchingByCiName.Count} candidates); skipping heuristic match.");
                                        }
                                    }
                                    else
                                    {
                                        UnityPackageImporter.Warn($"Ambiguous ancestor SkinnedMeshRenderer match by name '{goName}' ({matchingByName.Count} candidates); skipping heuristic match.");
                                    }
                                }

                                if (targetSkin == null && hashSkins.Count == 1)
                                {
                                    targetSkin = hashSkins[0];
                                }

                                if (targetSkin != null)
                                {
                                    PrefabVariantResolver.RegisterUniqueTarget(ancestorTargetMap, new SourceObj((long)ancestorSkin.id, ancestorGuid, 3), targetSkin);
                                    PrefabVariantResolver.RegisterUniqueTarget(ancestorTargetMap, new SourceObj((long)ancestorSkin.id, resolvedFbxGuid, 3), targetSkin);
                                    PrefabVariantResolver.RegisterUniqueTarget(ancestorTargetMap, new SourceObj((long)ancestorSkin.id, "", 3), targetSkin);
                                    PrefabVariantResolver.RegisterUniqueTarget(ancestorIdToTarget, (long)ancestorSkin.id, targetSkin);
                                    if (ancestorSkin.m_CorrespondingSourceObject != null && ancestorSkin.m_CorrespondingSourceObject.fileID != 0)
                                        PrefabVariantResolver.RegisterUniqueTarget(ancestorIdToTarget, ancestorSkin.m_CorrespondingSourceObject.fileID, targetSkin);

                                    if (ancestorSkin.m_Materials != null && ancestorSkin.m_Materials.Count > 0)
                                    {
                                        for (int i = 0; i < ancestorSkin.m_Materials.Count; i++)
                                        {
                                            var mat = ancestorSkin.m_Materials[i];
                                            if (mat != null && !string.IsNullOrEmpty(mat.guid))
                                            {
                                                while (targetSkin.m_Materials.Count <= i)
                                                    targetSkin.m_Materials.Add(new SourceObj());
                                                if (string.IsNullOrEmpty(targetSkin.m_Materials[i]?.guid))
                                                {
                                                    targetSkin.m_Materials[i] = mat;
                                                    if (!modifiedObjects.Contains(targetSkin))
                                                        modifiedObjects.Add(targetSkin);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            else if (kvp.Value is GameObject ancestorGo)
                            {
                                GameObject targetGo = null;
                                if (ancestorGo.m_CorrespondingSourceObject != null && ancestorGo.m_CorrespondingSourceObject.fileID != 0)
                                {
                                    if (this.PrefabHashes.TryGetValue(new SourceObj(ancestorGo.m_CorrespondingSourceObject.fileID, resolvedFbxGuid, 1), out var directGo) ||
                                        this.PrefabHashes.TryGetValue(new SourceObj(ancestorGo.m_CorrespondingSourceObject.fileID, resolvedFbxGuid, 3), out directGo) ||
                                        this.PrefabHashes.TryGetValue(new SourceObj(ancestorGo.m_CorrespondingSourceObject.fileID, ancestorGo.m_CorrespondingSourceObject.guid, 3), out directGo))
                                    {
                                        targetGo = directGo as GameObject;
                                    }
                                    if (targetGo == null && hashIdToTarget.TryGetValue(ancestorGo.m_CorrespondingSourceObject.fileID, out var directObj) && directObj is GameObject dGo)
                                        targetGo = dGo;
                                    if (targetGo == null && ancestorIdToTarget.TryGetValue(ancestorGo.m_CorrespondingSourceObject.fileID, out var mappedObj) && mappedObj is GameObject mGo)
                                        targetGo = mGo;
                                    if (targetGo == null && ancestorIdToTarget.TryGetValue(ancestorGo.m_CorrespondingSourceObject.fileID, out var conflictedGo) && conflictedGo == null)
                                        continue;

                                    if (targetGo == null && ancestorPiIds.Count > 0)
                                    {
                                        PrefabVariantResolver.TryResolveDerivedTarget(ancestorGo.m_CorrespondingSourceObject.fileID,
                                            ancestorPiIds, candidateId => FindDerivedCandidates(candidateId, null, resolvedFbxGuid,
                                                ancestorIdToTarget, hashIdToTarget), out IUnityObject derivedTarget, out bool ambiguous);
                                        if (ambiguous) continue;
                                        targetGo = derivedTarget as GameObject;
                                    }
                                }
                                if (targetGo == null && !string.IsNullOrEmpty(ancestorGo.m_Name))
                                {
                                    var matchingByName = hashGos.Where(h => h.frooxEngineSlot?.Name == ancestorGo.m_Name).ToList();
                                    if (matchingByName.Count == 1)
                                    {
                                        targetGo = matchingByName[0];
                                    }
                                    else if (matchingByName.Count == 0)
                                    {
                                        var matchingByCiName = hashGos.Where(h => h.frooxEngineSlot?.Name?.Equals(ancestorGo.m_Name, StringComparison.OrdinalIgnoreCase) == true).ToList();
                                        if (matchingByCiName.Count == 1)
                                        {
                                            targetGo = matchingByCiName[0];
                                        }
                                        else if (matchingByCiName.Count > 1)
                                        {
                                            UnityPackageImporter.Warn($"Ambiguous ancestor GameObject match by case-insensitive name '{ancestorGo.m_Name}' ({matchingByCiName.Count} candidates); skipping heuristic match.");
                                        }
                                    }
                                    else
                                    {
                                        UnityPackageImporter.Warn($"Ambiguous ancestor GameObject match by name '{ancestorGo.m_Name}' ({matchingByName.Count} candidates); skipping heuristic match.");
                                    }
                                }
                                if (targetGo != null)
                                {
                                    PrefabVariantResolver.RegisterUniqueTarget(ancestorTargetMap, new SourceObj((long)ancestorGo.id, ancestorGuid, 1), targetGo);
                                    PrefabVariantResolver.RegisterUniqueTarget(ancestorTargetMap, new SourceObj((long)ancestorGo.id, resolvedFbxGuid, 1), targetGo);
                                    PrefabVariantResolver.RegisterUniqueTarget(ancestorTargetMap, new SourceObj((long)ancestorGo.id, "", 1), targetGo);
                                    PrefabVariantResolver.RegisterUniqueTarget(ancestorIdToTarget, (long)ancestorGo.id, targetGo);
                                    if (ancestorGo.m_CorrespondingSourceObject != null && ancestorGo.m_CorrespondingSourceObject.fileID != 0)
                                        PrefabVariantResolver.RegisterUniqueTarget(ancestorIdToTarget, ancestorGo.m_CorrespondingSourceObject.fileID, targetGo);
                                }
                            }
                            else if (kvp.Value is Transform ancestorTr)
                            {
                                IUnityObject targetTr = null;
                                if (ancestorTr.m_CorrespondingSourceObject != null && ancestorTr.m_CorrespondingSourceObject.fileID != 0)
                                {
                                    if (this.PrefabHashes.TryGetValue(new SourceObj(ancestorTr.m_CorrespondingSourceObject.fileID, resolvedFbxGuid, 4), out var directTr) ||
                                        this.PrefabHashes.TryGetValue(new SourceObj(ancestorTr.m_CorrespondingSourceObject.fileID, resolvedFbxGuid, 3), out directTr))
                                    {
                                        targetTr = directTr;
                                    }
                                    if (targetTr == null && hashIdToTarget.TryGetValue(ancestorTr.m_CorrespondingSourceObject.fileID, out var directObj))
                                        targetTr = directObj;
                                    if (targetTr == null && ancestorIdToTarget.TryGetValue(ancestorTr.m_CorrespondingSourceObject.fileID, out var mappedObj))
                                        targetTr = mappedObj;
                                    if (targetTr == null && ancestorIdToTarget.TryGetValue(ancestorTr.m_CorrespondingSourceObject.fileID, out var conflictedTr) && conflictedTr == null)
                                        continue;

                                    if (targetTr == null && ancestorPiIds.Count > 0)
                                    {
                                        PrefabVariantResolver.TryResolveDerivedTarget(ancestorTr.m_CorrespondingSourceObject.fileID,
                                            ancestorPiIds, candidateId => FindDerivedCandidates(candidateId, null, resolvedFbxGuid,
                                                ancestorIdToTarget, hashIdToTarget), out targetTr, out bool ambiguous);
                                        if (ambiguous) continue;
                                    }
                                }
                                if (targetTr == null && ancestorTr.m_GameObjectID != 0)
                                {
                                    if (ancestorIdToTarget.TryGetValue((long)ancestorTr.m_GameObjectID, out var parentGo))
                                        targetTr = parentGo;
                                }
                                if (targetTr != null)
                                {
                                    PrefabVariantResolver.RegisterUniqueTarget(ancestorTargetMap, new SourceObj((long)ancestorTr.id, ancestorGuid, 4), targetTr);
                                    PrefabVariantResolver.RegisterUniqueTarget(ancestorTargetMap, new SourceObj((long)ancestorTr.id, resolvedFbxGuid, 4), targetTr);
                                    PrefabVariantResolver.RegisterUniqueTarget(ancestorTargetMap, new SourceObj((long)ancestorTr.id, "", 4), targetTr);
                                    PrefabVariantResolver.RegisterUniqueTarget(ancestorIdToTarget, (long)ancestorTr.id, targetTr);
                                    if (ancestorTr.m_CorrespondingSourceObject != null && ancestorTr.m_CorrespondingSourceObject.fileID != 0)
                                        PrefabVariantResolver.RegisterUniqueTarget(ancestorIdToTarget, ancestorTr.m_CorrespondingSourceObject.fileID, targetTr);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        UnityPackageImporter.Warn("Failed to replay ancestor modifications from " + ancestorFile + ": " + ex.Message);
                    }
                }

                // 2. Apply this variant's modifications on top
                if (m_Modification != null && this.PrefabHashes != null)
                {
                    ApplyModifications(m_Modification.m_Modifications, importer, resolvedFbxGuid, lineageGuids, modifiedObjects, ancestorTargetMap, ancestorIdToTarget, ancestorPiIds, hashIdToTarget);
                }

                instanciated = true;
                // Instanciate our objects with our modifications, so in case they're not referenced in the scene, the imported FBX we duplicated to make this inline prefab has the M_Modifications still applied.
                //this is important to do, especially for skinned mesh renderers! - @989onan
                UnityPackageImporter.Msg("reinitializing objects that were modified.");
                foreach (IUnityObject obj in modifiedObjects)
                {
                    // This is so that it can find it's source, which is ourselves to prevent errors - @989onan
                    obj.m_PrefabInstance = new Dictionary<string, ulong> { { "fileID", this.id } };
                    obj.instanciated = false;
                    try
                    {
                        UnityPackageImporter.Msg("reinitializing object that was modified. the object has an id of: \"" + obj.m_CorrespondingSourceObject.fileID + "\"");
                    }
                    catch (Exception e)
                    {
                        try
                        {
                            UnityPackageImporter.Msg("reinitializing object that was modified. the object has an id of: \"" + obj.m_CorrespondingSourceObject.fileID + "\" has failed to instanciate. This is probably fine.");
                        }
                        catch (Exception ex)
                        {
                            UnityPackageImporter.Msg("reinitializing object that was modified. the object has an id of: \"NULL\" has failed to instanciate. This is probably fine.");
                            UnityPackageImporter.Msg(ex.Message, ex.StackTrace);
                        }
                        UnityPackageImporter.Msg(e.Message, e.StackTrace);
                    }
                    try
                    {
                        await default(ToWorld);
                        await obj.InstanciateAsync(importer);
                        await default(ToBackground);
                    }
                    catch (Exception e)
                    {
                        try
                        {
                            UnityPackageImporter.Msg("reinitializing object that was modified. the object has an id of: \"" + obj.m_CorrespondingSourceObject.fileID + "\" has failed to instanciate. This is probably fine.");
                        }
                        catch (Exception ex)
                        {
                            UnityPackageImporter.Msg("reinitializing object that was modified. the object has an id of: \"NULL\" has failed to instanciate. This is probably fine.");
                            UnityPackageImporter.Msg(ex.Message, ex.StackTrace);
                        }
                        UnityPackageImporter.Msg(e.Message, e.StackTrace);
                    }

                }
            }
            else
            {
                UnityPackageImporter.Warn("The prefab with a file id of \"" + id.ToString() + "\" in the structure scene is malformed!!! cannot find file with GUID of \"" + m_SourcePrefab.guid.ToString() + "\" for this prefab! (does it exist in the unity project?)");
            }
        }
        else
        {
            UnityPackageImporter.Warn("The prefab with a file id of \"" + id.ToString() + "\" in the structure scene is malformed!!! The file source of the prefab doesn't exist in the imported package or file list set.");
        }


        instanciated = true;
    }


    //a detailed to string for debugging.
    public override string ToString()
    {
        StringBuilder result = new StringBuilder();

        if (m_SourcePrefab != null)
        {
            result.AppendLine("m_SourcePrefab: " + m_SourcePrefab.ToString());

        }
        else
        {
            result.AppendLine("m_SourcePrefab: null");
        }
        if (m_CorrespondingSourceObject != null)
        {
            result.AppendLine("m_CorrespondingSourceObject: " + m_CorrespondingSourceObject.ToString());

        }
        else
        {
            result.AppendLine("m_CorrespondingSourceObject: null");
        }
        if (m_PrefabInstance != null)
        {
            result.AppendLine("m_PrefabInstance: " + m_PrefabInstance.ToString());
        }
        else
        {
            result.AppendLine("m_PrefabInstance: null");
        }
        if (m_Modification != null)
        {
            result.AppendLine("m_Modification: " + m_Modification.ToString());

        }


        return result.ToString();
    }

    private IUnityObject FindTargetObject(
        IUnityStructureImporter importer,
        ModsPrefab mod,
        string resolvedFbxGuid,
        HashSet<string> lineageGuids,
        Dictionary<SourceObj, IUnityObject> ancestorTargetMap = null,
        Dictionary<long, IUnityObject> ancestorIdToTarget = null,
        HashSet<long> ancestorPiIds = null,
        Dictionary<long, IUnityObject> hashIdToTarget = null)
    {
        if (mod?.target == null) return null;

        // 1. Direct match in PrefabHashes
        if (this.PrefabHashes != null && this.PrefabHashes.TryGetValue(mod.target, out IUnityObject directTarget))
            return directTarget;

        // 2. Ancestor target map match (by SourceObj or fileID across ancestor prefabs)
        if (ancestorTargetMap != null && ancestorTargetMap.TryGetValue(mod.target, out IUnityObject ancestorTarget))
            return ancestorTarget;

        if (ancestorTargetMap != null && !string.IsNullOrEmpty(resolvedFbxGuid))
        {
            SourceObj fbxTarget = new SourceObj(mod.target.fileID, resolvedFbxGuid, mod.target.type);
            if (ancestorTargetMap.TryGetValue(fbxTarget, out IUnityObject mappedFbxTarget))
                return mappedFbxTarget;

            SourceObj emptyTarget = new SourceObj(mod.target.fileID, "", mod.target.type);
            if (ancestorTargetMap.TryGetValue(emptyTarget, out IUnityObject mappedEmptyTarget))
                return mappedEmptyTarget;
        }

        if (ancestorIdToTarget != null && ancestorIdToTarget.TryGetValue(mod.target.fileID, out IUnityObject idTarget))
            return idTarget;

        // 3. Lineage match: if mod.target.guid belongs to the ancestor lineage or is empty,
        // map it to the underlying FBX GUID that keys the duplicated slot components in PrefabHashes.
        if (this.PrefabHashes != null && !string.IsNullOrEmpty(resolvedFbxGuid))
        {
            if (PrefabVariantResolver.TryResolveTargetKey(
                mod.target.fileID,
                mod.target.guid,
                resolvedFbxGuid,
                lineageGuids,
                out string resolvedTargetGuid))
            {
                SourceObj resolvedTarget = new SourceObj(mod.target.fileID, resolvedTargetGuid, mod.target.type);
                if (this.PrefabHashes.TryGetValue(resolvedTarget, out IUnityObject lineageTarget))
                    return lineageTarget;
            }
        }

        if (hashIdToTarget != null &&
            (string.IsNullOrEmpty(mod.target.guid) || (lineageGuids != null && lineageGuids.Contains(mod.target.guid))))
        {
            if (hashIdToTarget.TryGetValue(mod.target.fileID, out IUnityObject directHashTarget))
                return directHashTarget;
        }

        // 4. Reverse derived file ID match: when a Prefab Variant modifies objects inside a nested
        // PrefabInstance, Unity derives targetFileId = (parentPrefabInstanceId ^ baseFileId) & 0x7FFFFFFFFFFFFFFF.
        if (ancestorPiIds != null && ancestorPiIds.Count > 0)
        {
            if (PrefabVariantResolver.TryResolveDerivedTarget(mod.target.fileID, ancestorPiIds,
                candidateId => FindDerivedCandidates(candidateId, importer, resolvedFbxGuid,
                    ancestorIdToTarget, hashIdToTarget), out IUnityObject matchedTarget, out bool ambiguous))
                return matchedTarget;
            if (ambiguous)
            {
                UnityPackageImporter.Warn($"Ambiguous reverse derived file ID match for target fileID {mod.target.fileID}; skipping ambiguous binding.");
                return null;
            }
        }

        // 5. Fallback: match by file ID in existing objects deserialized in current task
        if (importer?.existingIUnityObjects != null)
        {
            if (importer.existingIUnityObjects.TryGetValue((ulong)mod.target.fileID, out IUnityObject localTarget))
                return localTarget;
            if (importer.existingIUnityObjects.TryGetValue(unchecked((ulong)mod.target.fileID), out localTarget))
                return localTarget;
        }

        return null;
    }

    private IEnumerable<IUnityObject> FindDerivedCandidates(
        long candidateId,
        IUnityStructureImporter importer,
        string resolvedFbxGuid,
        Dictionary<long, IUnityObject> ancestorIdToTarget,
        Dictionary<long, IUnityObject> hashIdToTarget)
    {
        if (ancestorIdToTarget != null && ancestorIdToTarget.TryGetValue(candidateId, out var ancestorTarget))
            yield return ancestorTarget;
        if (hashIdToTarget != null && hashIdToTarget.TryGetValue(candidateId, out var hashedTarget))
            yield return hashedTarget;
        if (this.PrefabHashes != null && !string.IsNullOrEmpty(resolvedFbxGuid) &&
            this.PrefabHashes.TryGetValue(new SourceObj(candidateId, resolvedFbxGuid, 3), out var sourceTarget))
            yield return sourceTarget;
        if (importer?.existingIUnityObjects != null &&
            importer.existingIUnityObjects.TryGetValue(unchecked((ulong)candidateId), out var localTarget))
            yield return localTarget;
    }

    private void ApplyModifications(
        IEnumerable<ModsPrefab> modifications,
        IUnityStructureImporter importer,
        string resolvedFbxGuid,
        HashSet<string> lineageGuids,
        List<IUnityObject> modifiedObjects,
        Dictionary<SourceObj, IUnityObject> ancestorTargetMap = null,
        Dictionary<long, IUnityObject> ancestorIdToTarget = null,
        HashSet<long> ancestorPiIds = null,
        Dictionary<long, IUnityObject> hashIdToTarget = null)
    {
        if (modifications == null) return;
        foreach (ModsPrefab mod in modifications)
        {
            IUnityObject targetobj = FindTargetObject(importer, mod, resolvedFbxGuid, lineageGuids, ancestorTargetMap, ancestorIdToTarget, ancestorPiIds, hashIdToTarget);
            if (targetobj != null)
            {
                if (MModificationsParser.ParseModifcation(targetobj, mod))
                {
                    UnityPackageImporter.Msg("The prefab with a file id of \"" + id.ToString() + "\" in the structure scene has parsed a modification with the hash \"" + mod.target.ToString() + "\" successfully.");
                    if (!modifiedObjects.Contains(targetobj))
                    {
                        modifiedObjects.Add(targetobj);
                    }
                }
                else
                {
                    UnityPackageImporter.Warn("The prefab with a file id of \"" + id.ToString() + "\" in the structure scene is malformed!!! The modification with the hash \"" + mod.target.ToString() + "\" could not be parsed!");
                }
            }
            else
            {
                UnityPackageImporter.Warn("The prefab with a file id of \"" + id.ToString() + "\" in the structure scene is malformed!!! The modification with the hash \"" + mod.target.ToString() + "\" does not match any in the list of hashes on the prefab \"" + id.ToString() + "\"");
            }
        }
    }

    private string ResolveFBXGuid(IUnityStructureImporter importer, string sourceGuid)
    {
        return PrefabVariantResolver.ResolveFbxGuid(
            sourceGuid,
            importer?.unityProjectImporter?.ListOfPrefabs,
            importer?.unityProjectImporter?.SharedImportedFBXScenes?.Keys);
    }

    private static string FindSourceGuidInPrefabFile(string file)
    {
        return PrefabVariantResolver.FindSourceGuidInPrefabFile(file);
    }
}
