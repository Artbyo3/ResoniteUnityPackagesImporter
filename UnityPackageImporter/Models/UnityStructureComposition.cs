using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityPackageImporter.FrooxEngineRepresentation;
using UnityTypes = UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes;

namespace UnityPackageImporter.Models;

internal static class UnityStructureComposition
{
    internal static AvatarPackageManifest ManifestFor(IUnityStructureImporter source) => source switch
    {
        UnityPrefabImportTask prefab => prefab.Manifest,
        UnitySceneImportTask scene => scene.Manifest,
        _ => null
    };

    internal static string Read(string path, UnityProjectImporter project, PrefabCompositionDiagnostics diagnostics)
    {
        var models = project.SharedImportedFBXScenes.ToDictionary(pair => pair.Key,
            pair => (IReadOnlyDictionary<long, int>)pair.Value.FILEID_To_Slot_Pairs
                .Select(entry => (FileId: entry.Key.fileID, ClassId: ClassId(entry.Value)))
                .Concat(pair.Value.StaticRendererIdentities())
                .GroupBy(entry => entry.FileId).ToDictionary(group => group.Key,
                    group => group.Select(entry => entry.ClassId).Distinct().Single()), StringComparer.OrdinalIgnoreCase);
        foreach (var model in models.Values.Cast<Dictionary<long, int>>())
        {
            model.TryAdd(919132149155446097, 1);
            model.TryAdd(-8679921383154817045, 4);
        }
        return UnityPrefabComposer.Compose(File.ReadAllText(path), project.ListOfPrefabs, models, diagnostics);
    }

    private static int ClassId(IUnityObject value) => value switch
    {
        UnityTypes.GameObject => 1,
        UnityTypes.Transform => 4,
        UnityTypes.SkinnedMeshRenderer => 137,
        _ => 0
    };
}
