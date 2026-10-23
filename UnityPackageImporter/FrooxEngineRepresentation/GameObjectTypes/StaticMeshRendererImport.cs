using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FrooxEngine;
using UnityPackageImporter.Models;

namespace UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes;

public partial class SkinnedMeshRenderer
{
    [YamlDotNet.Serialization.YamlIgnore]
    internal bool IsStaticMesh { get; set; }

    private async Task InstantiateStaticAsync(IUnityStructureImporter importer)
    {
        FrooxEngine.MeshRenderer template;
        Slot target;
        if (!string.IsNullOrEmpty(m_CorrespondingSourceObject?.guid))
        {
            if (m_PrefabInstance?.TryGetValue("fileID", out ulong instanceId) != true ||
                !importer.existingIUnityObjects.TryGetValue(instanceId, out var instance) || instance is not PrefabInstance prefab)
                throw new InvalidDataException($"Static renderer {id} cannot resolve its source instance.");
            await prefab.InstanciateAsync(importer);
            await default(ToWorld);
            if (prefab.importask == null || !string.Equals(prefab.ResolvedFbxGuid, m_CorrespondingSourceObject.guid, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Static renderer {id} cannot resolve its owning model identity.");
            template = prefab.importask.ResolveStaticRendererTemplate(m_CorrespondingSourceObject.fileID);
            target = template?.Slot;
        }
        else
        {
            if (m_GameObject == null || !m_GameObject.TryGetValue("fileID", out ulong ownerId) ||
                !importer.existingIUnityObjects.TryGetValue(ownerId, out var owner) || owner is not GameObject gameObject)
                throw new InvalidDataException($"Static renderer {id} has no local GameObject.");
            var filter = AvatarConversionScope.Unique(importer.existingIUnityObjects.Values.OfType<MeshFilter>()
                .Where(candidate => candidate.m_GameObject?.TryGetValue("fileID", out ulong candidateOwner) == true && candidateOwner == ownerId));
            m_Mesh = filter?.m_Mesh;
            if (m_Mesh == null || string.IsNullOrEmpty(m_Mesh.guid))
                throw new InvalidDataException($"Static renderer {id} has no unique MeshFilter with a mesh identity.");
            await default(ToWorld);
            template = importer.unityProjectImporter.SharedImportedFBXScenes.TryGetValue(m_Mesh.guid, out var model)
                ? model.ResolveStaticMeshTemplate(m_Mesh.fileID)
                : await UnityMeshSourceImporter.ResolveAsync(importer.unityProjectImporter, m_Mesh);
            await gameObject.InstanciateAsync(importer);
            await default(ToWorld);
            target = gameObject.frooxEngineSlot;
        }
        importer.unityProjectImporter.Session?.Check();
        if (template?.Mesh.Target == null || target == null || target.IsDestroyed)
        {
            HasMissingMeshReference = true;
            throw new InvalidDataException($"Static renderer {id} cannot resolve its exact mesh or owner identity.");
        }
        var originalMaterials = template.Materials.ToArray();
        var previousRenderer = target.GetComponents<FrooxEngine.MeshRenderer>()
            .SingleOrDefault(renderer => MeshReferenceLookup.IsExactStaticRenderer(renderer.GetType(), typeof(FrooxEngine.MeshRenderer)));
        createdMeshRenderer = target.GetComponentOrAttach<FrooxEngine.SkinnedMeshRenderer>();
        createdMeshRenderer.Mesh.Target = template.Mesh.Target;
        createdMeshRenderer.Enabled = m_Enabled != 0;
        createdMeshRenderer.Bones.Clear();
        createdMeshRenderer.Materials.Clear();
        materials.Clear();
        foreach (var material in originalMaterials)
        {
            createdMeshRenderer.Materials.Add().Target = material;
            materials.Add(material == null ? new FileImportHelperTaskMaterial(importer.unityProjectImporter) :
                new FileImportHelperTaskMaterial(importer.unityProjectImporter, material));
        }
        await ImportMeshReadiness.WaitAsync(new[] { createdMeshRenderer }, importer.unityProjectImporter.Session?.Token ?? default);
        await default(ToWorld);
        if (createdMeshRenderer.Mesh.Asset.Data.Bones.Any())
            throw new InvalidDataException($"Static renderer {id} refers to a mesh requiring skin bindings.");
        SkinnedBoundsPolicy.ApplyAfterBones(createdMeshRenderer, "Unity static renderer import");
        previousRenderer?.Destroy();
        await default(ToBackground);
    }
}
