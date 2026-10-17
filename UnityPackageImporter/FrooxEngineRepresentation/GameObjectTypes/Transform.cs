using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;
using UnityPackageImporter.Models;

namespace UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes;

public class Transform: IUnityObject
{    
    public TransformFloat4 m_LocalRotation;
    public TransformFloat3 m_LocalPosition;
    public TransformFloat3 m_LocalScale;
    public TransformFloat3 m_LocalEulerAnglesHint; // This is an error supressor. this does nothing don't use it. - @989onan

    public ulong id { get; set; }
    public int m_RootOrder;
    public bool instanciated {  get; set; }
    
    public Dictionary<string, ulong> m_PrefabInstance { get; set; }

    public ulong m_FatherID; //this is used during instanciation, and should never be used elsewhere. besides, you'll never need this for attaching components or anything - @989onan 
    public ulong m_GameObjectID; //use this when trying to find the parent gameobject of a component that points to a transform.
    public Dictionary<string, ulong> m_GameObject;
    public Dictionary<string, ulong> m_Father;
    public GameObject parentHashedGameObj; //instanciated on Transforms imported by FBXs. We can use this to find our parent game object via the prefab file hashing system.

    public SourceObj m_CorrespondingSourceObject { get; set; }


    // This is the magic that allows us to construct an entire game object prefab with just yaml parsing.
    public async Task InstanciateAsync(IUnityStructureImporter importer)
    {
        if (instanciated) return;

        instanciated = true;

        if (string.IsNullOrEmpty(m_CorrespondingSourceObject?.guid))
        {
            await CreateSelf(importer);
        }
        else
        {
            if (m_PrefabInstance != null &&
                m_PrefabInstance.TryGetValue("fileID", out ulong prefabInstanceId) &&
                importer.existingIUnityObjects.TryGetValue(prefabInstanceId, out IUnityObject PrefabInstanceObject))
            {
                PrefabInstance prefab = PrefabInstanceObject as PrefabInstance;
                if (prefab == null)
                {
                    throw new InvalidDataException($"Transform {id} references prefab instance {prefabInstanceId}, but it could not be resolved. Cannot reconstruct object hierarchy.");
                }

                await default(ToWorld);
                await prefab.InstanciateAsync(importer);
                await default(ToBackground);

                if (prefab.TryFindObject(m_CorrespondingSourceObject, out IUnityObject targetObj))
                {
                    Transform alreadydefined = (targetObj as Transform);
                    if (alreadydefined == null)
                    {
                        throw new InvalidDataException($"Transform {id} resolved target {m_CorrespondingSourceObject.fileID}, but target is not a Transform. Cannot reconstruct object hierarchy.");
                    }

                    this.parentHashedGameObj = alreadydefined.parentHashedGameObj;

                    UnityPackageImporter.Msg("transform import stage 1");
                    foreach (IUnityObject variab in importer.existingIUnityObjects.Values)
                    {
                        if (variab is GameObject actual &&
                            actual.m_CorrespondingSourceObject?.guid != null &&
                            actual.m_CorrespondingSourceObject.fileID != 0 &&
                            alreadydefined.parentHashedGameObj?.m_CorrespondingSourceObject != null)
                        {
                            if (actual.m_CorrespondingSourceObject.fileID == alreadydefined.parentHashedGameObj.m_CorrespondingSourceObject.fileID
                                && actual.m_CorrespondingSourceObject.guid.Equals(alreadydefined.parentHashedGameObj.m_CorrespondingSourceObject.guid))
                            {
                                UnityPackageImporter.Msg("transform import stage 1.5");
                                this.parentHashedGameObj = actual;
                                break;
                            }
                        }
                    }

                    if (this.parentHashedGameObj == null)
                    {
                        throw new InvalidDataException($"Transform {id} could not find its associated GameObject in prefab hierarchy. Cannot reconstruct object hierarchy.");
                    }

                    UnityPackageImporter.Msg("transform import stage 3");
                    await default(ToWorld);
                    await this.parentHashedGameObj.InstanciateAsync(importer);
                    await default(ToBackground);

                    UnityPackageImporter.Msg("transform import stage 4");
                    this.m_GameObject = new Dictionary<string, ulong>
                    {
                        { "fileID", this.parentHashedGameObj.id}
                    };
                    m_GameObjectID = m_GameObject["fileID"];

                    UnityPackageImporter.Msg("transform import stage 5");
                    await default(ToWorld);
                    this.m_LocalRotation = alreadydefined.m_LocalRotation;
                    this.m_LocalPosition = alreadydefined.m_LocalPosition;
                    this.m_LocalScale = alreadydefined.m_LocalScale;

                    if (this.parentHashedGameObj?.frooxEngineSlot != null)
                    {
                        if (this.m_LocalPosition != null)
                            this.parentHashedGameObj.frooxEngineSlot.LocalPosition = new float3(this.m_LocalPosition.x, this.m_LocalPosition.y, this.m_LocalPosition.z);
                        if (this.m_LocalRotation != null)
                            this.parentHashedGameObj.frooxEngineSlot.LocalRotation = new floatQ(this.m_LocalRotation.x, this.m_LocalRotation.y, this.m_LocalRotation.z, this.m_LocalRotation.w);
                        if (this.m_LocalScale != null)
                            this.parentHashedGameObj.frooxEngineSlot.LocalScale = new float3(this.m_LocalScale.x, this.m_LocalScale.y, this.m_LocalScale.z);
                    }
                    await default(ToBackground);
                    UnityPackageImporter.Msg("transform import stage 6");
                }
                else
                {
                    throw new InvalidDataException($"Transform {id} could not resolve prefab target {m_CorrespondingSourceObject.fileID} in {m_CorrespondingSourceObject.guid}. Hierarchy integrity cannot be guaranteed.");
                }
            }
            else
            {
                throw new InvalidDataException($"Transform {id} has prefab source {m_CorrespondingSourceObject.guid} but could not find its PrefabInstance. Hierarchy integrity cannot be guaranteed.");
            }
            instanciated = true;

        }
    }

    private async Task CreateSelf(IUnityStructureImporter importer)
    {
        m_FatherID = (m_Father != null && m_Father.TryGetValue("fileID", out ulong fId)) ? fId : 0;
        m_GameObjectID = (m_GameObject != null && m_GameObject.TryGetValue("fileID", out ulong gId)) ? gId : 0;
        if (importer.existingIUnityObjects.TryGetValue(m_GameObjectID, out IUnityObject foundobject) && foundobject.GetType() == typeof(GameObject))
        {
            GameObject parentobj;
            parentobj = foundobject as GameObject;
            await default(ToWorld);
            await parentobj.InstanciateAsync(importer);
            await default(ToBackground);

            await default(ToWorld);
            if (parentobj.frooxEngineSlot != null)
            {
                if (m_LocalPosition != null)
                    parentobj.frooxEngineSlot.LocalPosition = new float3(m_LocalPosition.x, m_LocalPosition.y, m_LocalPosition.z);
                if (m_LocalRotation != null)
                    parentobj.frooxEngineSlot.LocalRotation = new floatQ(m_LocalRotation.x, m_LocalRotation.y, m_LocalRotation.z, m_LocalRotation.w);
                if (m_LocalScale != null)
                    parentobj.frooxEngineSlot.LocalScale = new float3(m_LocalScale.x, m_LocalScale.y, m_LocalScale.z);
            }
            
            await default(ToBackground);
            if (importer.existingIUnityObjects.TryGetValue(m_FatherID, out IUnityObject foundobjectparent) && foundobjectparent.GetType() == typeof(Transform))
            {
                Transform parentTransform = foundobjectparent as Transform;

                await parentTransform.InstanciateAsync(importer);
                if (importer.existingIUnityObjects.TryGetValue(parentTransform.m_GameObjectID, out IUnityObject parentobj_parent) && parentobj_parent.GetType() == typeof(GameObject))
                {
                    var targetSlot = (parentobj_parent as GameObject)?.frooxEngineSlot;
                    if (parentobj.frooxEngineSlot != null && targetSlot != null)
                    {
                        await default(ToWorld);
                        parentobj.frooxEngineSlot.SetParent(targetSlot, false);
                        await default(ToBackground);
                    }
                    else
                    {
                        throw new InvalidDataException($"Transform {id} could not parent slot to parent GameObject {parentTransform.m_GameObjectID} (slot is null). Hierarchy would be split.");
                    }
                }
                else if(parentTransform.parentHashedGameObj?.frooxEngineSlot != null && parentobj.frooxEngineSlot != null)
                {
                    //if this transform was instanciated through the FileImportTaskScene class, we parent ourselves to it's game object here, which is filled by the FileImportTaskScene class.
                    await default(ToWorld);
                    parentobj.frooxEngineSlot.SetParent(parentTransform.parentHashedGameObj.frooxEngineSlot, false);
                    await default(ToBackground);
                }
                else
                {
                    throw new InvalidDataException($"Transform {id} could not find parent transform's GameObject (parent transform {parentTransform.id}). Hierarchy would be split.");
                }
            }
            else
            {
                //this is to solve a problem where the root of prefabs can be called their game object name
                if(m_FatherID == 0 && parentobj.frooxEngineSlot != null)
                {
                    await default(ToWorld);
                    parentobj.frooxEngineSlot.Name = "RootNode";
                    await default(ToBackground);
                }
                else if (m_FatherID != 0)
                {
                    throw new InvalidDataException($"Transform {id} specifies parent transform fileID {m_FatherID}, but parent does not exist. Hierarchy would be split.");
                }
            }
        }
        else
        {
            throw new InvalidDataException($"Transform {id} did not find its associated GameObject {m_GameObjectID}. Cannot reconstruct object hierarchy.");
        }
    }

    public override string ToString()
    {
        StringBuilder result = new StringBuilder();
        result.AppendLine("id: " + id.ToString());
        result.AppendLine("instanciated: " + (instanciated.ToString()));
        result.AppendLine("m_GameObjectID: " + (m_GameObjectID == 0? "null":m_GameObjectID.ToString()));
        result.AppendLine("m_GameObject: " + (m_GameObject == null ? "null" : m_GameObject.ToString()));
        result.AppendLine("m_Father: " + (m_Father == null ? "null" : m_Father.ToString()));
        result.AppendLine("m_FatherID: " + (m_FatherID == 0 ? "null" : m_FatherID.ToString()));
        result.AppendLine("m_LocalRotation: " + (m_LocalRotation == null ? "null" : m_LocalRotation.ToString()));
        result.AppendLine("m_LocalPosition: " + (m_LocalPosition == null ? "null" : m_LocalPosition.ToString()));
        result.AppendLine("m_LocalScale: " + (m_LocalScale == null ? "null" : m_LocalScale.ToString()));
        result.AppendLine("m_CorrespondingSourceObject" + (m_CorrespondingSourceObject == null ? "null" : m_CorrespondingSourceObject.ToString()));
        result.AppendLine("m_PrefabInstance: " + (m_PrefabInstance == null ? "null" : m_PrefabInstance.ToString()));
        return result.ToString();
    }

}

public class TransformFloat4
{
    public float x;
    public float y;
    public float z;
    public float w;

    public override string ToString()
    {
        StringBuilder result = new StringBuilder();
        result.AppendLine("x: " + x.ToString());
        result.AppendLine("y: " + y.ToString());
        result.AppendLine("z: " + z.ToString());
        result.AppendLine("w: " + w.ToString());

        return result.ToString();
    }

    public TransformFloat4() { }

    public TransformFloat4(float x, float y, float z, float w)
    {
        this.x = x;
        this.y = y;
        this.z = z;
        this.w = w;
    }
}

public class TransformFloat3
{
    public float x;
    public float y;
    public float z;

    public TransformFloat3() { }

    public TransformFloat3(float x, float y, float z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }

    public override string ToString()
    {
        StringBuilder result = new StringBuilder();
        result.AppendLine("x: " + x.ToString());
        result.AppendLine("y: " + y.ToString());
        result.AppendLine("z: " + z.ToString());

        return result.ToString();
    }
}
