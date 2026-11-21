using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.Store;
using UnityPackageImporter.FrooxEngineRepresentation;

namespace UnityPackageImporter.Models;

internal sealed record SerializedSkinMeshTemplate(StaticMesh Mesh, UnitySerializedSkinData Skin);

internal static class UnitySerializedSkinMeshImporter
{
    internal static Task<SerializedSkinMeshTemplate> ResolveAsync(UnityProjectImporter importer, SourceObj source)
    {
        string key = source.guid.ToLowerInvariant() + ":" + source.fileID.ToString(CultureInfo.InvariantCulture);
        return importer.SerializedSkinImports.GetOrAdd(key, async () =>
        {
            await default(ToBackground);
            importer.Session?.Check();
            if (!importer.AssetIDDict.TryGetValue(source.guid, out string path)) throw new InvalidDataException("Serialized skin source is absent from the package.");
            string yaml = await File.ReadAllTextAsync(path, importer.Session?.Token ?? default);
            UnitySerializedSkinData skin;
            MeshX mesh;
            try
            {
                var geometry = UnitySerializedMesh.Parse(yaml, source.fileID, true, out skin);
                mesh = Build(geometry, skin);
            }
            catch (InvalidDataException error)
            {
                ReportFailureDetails.Tag(error, ReportFailureReason.SerializedSkinDecodeRejected);
                throw;
            }
            var database = importer.world.Engine.LocalDB;
            string temporary = database.GetTempFilePath("meshx");
            mesh.SaveToFile(temporary);
            var url = await database.ImportLocalAssetAsync(temporary, LocalDB.ImportLocation.Move);
            await default(ToWorld);
            importer.Session?.Check();
            var provider = importer.importTaskAssetRoot.AddSlot("Serialized skin mesh").AttachComponent<StaticMesh>();
            provider.URL.Value = url;
            return new SerializedSkinMeshTemplate(provider, skin);
        });
    }

    internal static MeshX Build(UnityMeshData geometry, UnitySerializedSkinData skin)
    {
        if (skin == null || geometry.Positions.Length != skin.Vertices.Length) throw new InvalidDataException("Serialized skin data is incomplete.");
        skin.ValidateNativeShapes();
        var mesh = new MeshX();
        mesh.IncreaseVertexCount(geometry.Positions.Length);
        mesh.HasNormals = geometry.Normals.Length != 0;
        mesh.HasTangents = geometry.Tangents.Length != 0;
        mesh.HasColors = geometry.Colors.Length != 0;
        mesh.HasBoneBindings = true;
        for (int channel = 0; channel < geometry.UV.Length; channel++)
        {
            int dimension = geometry.UVDimensions[channel];
            if (dimension == 1 || dimension > 3) throw new InvalidDataException("Serialized skin UV dimension is unsupported.");
            mesh.SetUV_Dimension(channel, dimension);
        }
        for (int index = 0; index < geometry.Positions.Length; index++)
        {
            var vertex = mesh.GetVertex(index);
            vertex.PositionUnsafe = Convert(geometry.Positions[index]);
            if (mesh.HasNormals) vertex.NormalUnsafe = Convert(geometry.Normals[index]);
            if (mesh.HasTangents)
            {
                var tangent = geometry.Tangents[index];
                vertex.Tangent4Unsafe = new float4(tangent.X, tangent.Y, tangent.Z, -tangent.W);
            }
            if (mesh.HasColors)
            {
                var color = geometry.Colors[index];
                vertex.ColorUnsafe = new colorX(color.X, color.Y, color.Z, color.W).LinearColor;
            }
            for (int channel = 0; channel < geometry.UV.Length; channel++)
                if (geometry.UVDimensions[channel] == 2) mesh.GetRawUVs(channel)[index] = new float2(geometry.UV[channel][index].X, geometry.UV[channel][index].Y);
                else if (geometry.UVDimensions[channel] == 3) mesh.GetRawUVs_3D(channel)[index] = Convert(geometry.UV[channel][index]);
            var binding = skin.Vertices[index];
            var nativeBinding = new BoneBinding
            {
                boneIndex0 = binding.Weights[0] > 0f ? binding.BoneIndices[0] : -1,
                boneIndex1 = binding.Weights[1] > 0f ? binding.BoneIndices[1] : -1,
                boneIndex2 = binding.Weights[2] > 0f ? binding.BoneIndices[2] : -1,
                boneIndex3 = binding.Weights[3] > 0f ? binding.BoneIndices[3] : -1,
                weight0 = binding.Weights[0], weight1 = binding.Weights[1], weight2 = binding.Weights[2], weight3 = binding.Weights[3]
            };
            nativeBinding.Normalize();
            nativeBinding.Sort();
            vertex.BoneBinding = nativeBinding;
        }
        for (int index = 0; index < skin.BindPoses.Length; index++)
        {
            float[] matrix = skin.BindPoses[index];
            mesh.AddBone("UnitySerializedBone_" + index.ToString(CultureInfo.InvariantCulture)).BindPose = new float4x4(
                matrix[0], matrix[1], matrix[2], matrix[3], matrix[4], matrix[5], matrix[6], matrix[7],
                matrix[8], matrix[9], matrix[10], matrix[11], matrix[12], matrix[13], matrix[14], matrix[15]);
        }
        foreach (var indices in geometry.Submeshes)
        {
            var submesh = mesh.AddSubmesh<TriangleSubmesh>();
            for (int index = 0; index < indices.Length; index += 3) submesh.AddTriangle(indices[index], indices[index + 1], indices[index + 2]);
        }
        foreach (var shape in skin.Shapes)
        {
            var native = mesh.AddBlendShape(shape.Name);
            native.HasNormals = shape.Frames[0].HasNormals;
            native.HasTangents = shape.Frames[0].HasTangents;
            foreach (var frame in shape.Frames)
            {
                var target = native.AddFrame(frame.Weight);
                foreach (var delta in frame.Deltas)
                {
                    target.RawPositions[delta.VertexIndex] = Convert(delta.Position);
                    if (native.HasNormals) target.RawNormals[delta.VertexIndex] = Convert(delta.Normal);
                    if (native.HasTangents) target.RawTangents[delta.VertexIndex] = Convert(delta.Tangent);
                }
            }
        }
        return mesh;
    }

    private static float3 Convert(System.Numerics.Vector3 value) => new(value.X, value.Y, value.Z);
}
