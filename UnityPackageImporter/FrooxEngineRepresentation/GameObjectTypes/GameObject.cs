using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FrooxEngine;
using UnityPackageImporter.Models;

namespace UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes;

[Serializable]
public class GameObject: IUnityObject
{
    public string m_Name;
    public int m_IsActive = 1;
    public bool instanciated { get; set; }
    public ulong id { get; set; }
    public SourceObj m_CorrespondingSourceObject { get; set; }
    public Dictionary<string, ulong> m_PrefabInstance { get; set; }

    public Slot frooxEngineSlot;

    public async Task InstanciateAsync(IUnityStructureImporter importer)
    {
        if (instanciated) return;
        if (importer is UnitySceneImportTask scene && scene.TryDeferObject(this)) return;

        await default(ToWorld);
        importer.unityProjectImporter.Session?.Check();
        if (string.IsNullOrEmpty(m_CorrespondingSourceObject?.guid) && frooxEngineSlot == null)
        {
            frooxEngineSlot = importer.CurrentStructureRootSlot.AddSlot(this.m_Name);
            frooxEngineSlot.ActiveSelf = m_IsActive == 1 ? true : false;
        }
        else if (frooxEngineSlot != null)
        {
            // If being instanciated through prefab import while still in prefab hashes.
            // Since it will have modifications done to it but have an in world object still.
            frooxEngineSlot.ActiveSelf = m_IsActive == 1 ? true : false;
            frooxEngineSlot.Name = m_Name;
        }
        else if (m_PrefabInstance != null &&
                 m_PrefabInstance.TryGetValue("fileID", out ulong piId) &&
                 importer.existingIUnityObjects.TryGetValue(piId, out IUnityObject prefab_inc))
        {
            PrefabInstance prefab = prefab_inc as PrefabInstance;
            if (prefab != null)
            {
                await default(ToWorld);
                await prefab.InstanciateAsync(importer);
                await default(ToBackground);
                if (prefab.TryFindObject(m_CorrespondingSourceObject, out IUnityObject existingobject) && existingobject is GameObject existing)
                {
                    await default(ToWorld);
                    if (string.IsNullOrEmpty(m_Name))
                        m_Name = existing.m_Name;
                    frooxEngineSlot = existing.frooxEngineSlot;
                    if (frooxEngineSlot != null)
                    {
                        frooxEngineSlot.Name = m_Name;
                        frooxEngineSlot.ActiveSelf = existing.m_IsActive == 1;
                    }
                    await default(ToBackground);
                }
            }
        }

        if (frooxEngineSlot == null)
        {
            throw new InvalidDataException($"Unresolved prefab reference for GameObject {id} (source: fileID {m_CorrespondingSourceObject?.fileID} in {m_CorrespondingSourceObject?.guid}). Cannot reconstruct object hierarchy.");
        }

        await default(ToBackground);
        instanciated = true;
    }

    public override string ToString()
    {
        StringBuilder result = new StringBuilder();
        result.AppendLine("id: " + id.ToString());
        result.AppendLine("instanciated: " + instanciated.ToString());
        result.AppendLine("m_IsActive: "+ m_IsActive.ToString());

        if (m_CorrespondingSourceObject != null)
            result.AppendLine("m_CorrespondingSourceObject: " + m_CorrespondingSourceObject.ToString());
        else
            result.AppendLine("m_CorrespondingSourceObject: null");

        if (m_PrefabInstance != null)
            result.AppendLine("m_PrefabInstance: " + m_PrefabInstance.ToString());
        else
            result.AppendLine("m_PrefabInstance: null");

        if (m_Name != null)
            result.AppendLine("m_Name: " + m_Name.ToString());
        else 
            result.AppendLine("m_Name: null");

        if (frooxEngineSlot != null)
            result.AppendLine("frooxEngineSlot: " + frooxEngineSlot.ToString());
        else
            result.AppendLine("frooxEngineSlot: null");

        return result.ToString();
    }
}
