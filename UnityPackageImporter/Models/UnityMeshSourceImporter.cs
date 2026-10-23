using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Assimp;
using FrooxEngine;
using UnityPackageImporter.FrooxEngineRepresentation;

namespace UnityPackageImporter.Models;

internal static class UnityMeshSourceImporter
{
    internal static Task<FrooxEngine.MeshRenderer> ResolveAsync(UnityProjectImporter importer, SourceObj source)
    {
        string key = source.guid.ToLowerInvariant() + ":" + source.fileID.ToString(CultureInfo.InvariantCulture);
        return importer.StaticMeshImports.GetOrAdd(key, async () =>
        {
            await default(ToBackground);
            importer.Session?.Check();
            UnityMeshData geometry;
            string path;
            if (source.guid.Equals("0000000000000000e000000000000000", StringComparison.OrdinalIgnoreCase))
            {
                geometry = UnitySerializedMesh.Builtin(source.guid, source.fileID);
                path = "UnityBuiltin_" + source.fileID.ToString(CultureInfo.InvariantCulture) + ".mesh";
            }
            else if (importer.AssetIDDict.TryGetValue(source.guid, out path))
                geometry = UnitySerializedMesh.Parse(await File.ReadAllTextAsync(path, importer.Session?.Token ?? default), source.fileID);
            else throw new InvalidDataException($"Static mesh source {source.guid}, fileID {source.fileID}, is not available in the package.");
            await default(ToWorld);
            importer.Session?.Check();
            var task = new FileImportTaskScene(importer.root, source.guid, importer, path, geometry);
            await task.RunnerWrapper();
            await default(ToWorld);
            var renderer = AvatarConversionScope.Unique(task.FinishedFileSlot.GetComponentsInChildren<FrooxEngine.MeshRenderer>()
                .Where(candidate => MeshReferenceLookup.IsExactStaticRenderer(candidate.GetType(), typeof(FrooxEngine.MeshRenderer))));
            if (renderer?.Mesh.Target == null) throw new InvalidDataException("Native import did not produce one static mesh template.");
            return renderer;
        });
    }

    internal static Scene CreateScene(UnityMeshData data)
    {
        var scene = new Scene { RootNode = new Node("UnitySerializedMeshRoot") };
        var node = new Node("UnitySerializedMesh", scene.RootNode);
        scene.RootNode.Children.Add(node);
        for (int submesh = 0; submesh < data.Submeshes.Count; submesh++)
        {
            var mesh = new Assimp.Mesh("UnitySerializedMesh", PrimitiveType.Triangle) { MaterialIndex = submesh };
            mesh.Vertices.AddRange(data.Positions.Select(value => new Vector3D(value.X, value.Y, value.Z)));
            mesh.Normals.AddRange(data.Normals.Select(value => new Vector3D(value.X, value.Y, value.Z)));
            if (data.Tangents.Length > 0)
                for (int index = 0; index < data.Tangents.Length; index++)
                {
                    var tangent = data.Tangents[index];
                    mesh.Tangents.Add(new Vector3D(tangent.X, tangent.Y, tangent.Z));
                    if (data.Normals.Length > 0)
                    {
                        var bitangent = System.Numerics.Vector3.Cross(data.Normals[index], new System.Numerics.Vector3(tangent.X, tangent.Y, tangent.Z)) * tangent.W;
                        mesh.BiTangents.Add(new Vector3D(bitangent.X, bitangent.Y, bitangent.Z));
                    }
                }
            mesh.VertexColorChannels[0].AddRange(data.Colors.Select(value => new Color4D(value.X, value.Y, value.Z, value.W)));
            for (int channel = 0; channel < data.UV.Length; channel++)
            {
                mesh.TextureCoordinateChannels[channel].AddRange(data.UV[channel].Select(value => new Vector3D(value.X, value.Y, value.Z)));
                mesh.UVComponentCount[channel] = data.UVDimensions[channel];
            }
            var triangles = data.Submeshes[submesh];
            for (int index = 0; index < triangles.Length; index += 3)
                mesh.Faces.Add(new Face(new[] { triangles[index], triangles[index + 1], triangles[index + 2] }));
            scene.Materials.Add(new Assimp.Material { Name = "UnitySubmesh_" + submesh.ToString(CultureInfo.InvariantCulture) });
            scene.Meshes.Add(mesh);
            node.MeshIndices.Add(submesh);
        }
        return scene;
    }
}
