using System;
using System.Collections.Generic;
using UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes;













































namespace UnityPackageImporter.FrooxEngineRepresentation;

public class UnityEngineObjectWrapper
{





    public static Dictionary<Type, int> addedProgress = new Dictionary<Type, int>
    {
        {typeof(Component), 0},
        {typeof(GameObject), 1},
        {typeof(Transform), 2},
        {typeof(NullType), 0},
        {typeof(SkinnedMeshRenderer), 10},
        {typeof(PrefabInstance), 20},
        {typeof(RotationConstraint), 2},
        {typeof(MeshCollider), 10},
        {typeof(MonoBehaviour), 30}
    };

    public Component Component;
    public GameObject GameObject;
    public Transform Transform;
    public NullType NullType;
    public SkinnedMeshRenderer SkinnedMeshRenderer;
    public SkinnedMeshRenderer MeshRenderer;
    public MeshFilter MeshFilter;
    public PrefabInstance PrefabInstance;
    public RotationConstraint RotationConstraint;
    public MeshCollider MeshCollider;
    public MonoBehaviour MonoBehaviour;

    public UnityEngineObjectWrapper()
    {

    }

    public IUnityObject Result()
    {
        if (MeshRenderer != null)
        {
            MeshRenderer.IsStaticMesh = true;
            return MeshRenderer;
        }
        IUnityObject returned = new List<IUnityObject>(){
            PrefabInstance,
            GameObject,
            Component,
            Transform,
            SkinnedMeshRenderer,
            MeshFilter,
            MeshCollider,
            RotationConstraint,
            MonoBehaviour,
            NullType
        }.Find(i => i != null);

        if(returned != null) {
            return returned;
        }
        else
        {
            return new NullType();
        }

    }
}
