#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace UnityPackageImporter.Models;

internal sealed class UnityMeshData
{
    internal Vector3[] Positions { get; init; } = Array.Empty<Vector3>();
    internal Vector3[] Normals { get; init; } = Array.Empty<Vector3>();
    internal Vector4[] Tangents { get; init; } = Array.Empty<Vector4>();
    internal Vector4[] Colors { get; init; } = Array.Empty<Vector4>();
    internal Vector3[][] UV { get; init; } = Enumerable.Range(0, 8).Select(_ => Array.Empty<Vector3>()).ToArray();
    internal int[] UVDimensions { get; init; } = new int[8];
    internal List<int[]> Submeshes { get; } = new();
}

internal static class UnitySerializedMesh
{
    private sealed record Channel(int Stream, int Offset, int Format, int Dimension);
    private static YamlNode? Get(YamlMappingNode node, string key) => node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;
    private static YamlMappingNode Map(YamlMappingNode node, string key) => Get(node, key) as YamlMappingNode ?? throw new InvalidDataException("Missing mesh mapping: " + key);
    private static string Text(YamlMappingNode node, string key) => (Get(node, key) as YamlScalarNode)?.Value ?? "";
    private static int Integer(YamlMappingNode node, string key) => int.TryParse(Text(node, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : throw new InvalidDataException("Invalid mesh integer: " + key);
    private static IEnumerable<YamlNode> Items(YamlMappingNode node, string key) => (Get(node, key) as YamlSequenceNode)?.Children ?? Enumerable.Empty<YamlNode>();
    private static int Size(int format) => format switch { 0 or 10 or 11 => 4, 1 or 4 or 5 or 8 or 9 => 2, >= 2 and <= 7 => 1, _ => throw new InvalidDataException("Unsupported vertex format: " + format) };
    private static byte[] Hex(string value)
    {
        if (value.Length % 2 != 0 || value.Any(character => !Uri.IsHexDigit(character))) throw new InvalidDataException("Invalid mesh byte buffer.");
        return Convert.FromHexString(value);
    }

    internal static UnityMeshData Parse(string yaml, long fileId)
    {
        var stream = new YamlStream();
        string normalized = Regex.Replace(AvatarPackageIndex.NormalizeDocumentHeaders(yaml), @"(?m)^(--- )!u!(\d+)", "$1!<tag:unity3d.com,2011:$2>");
        stream.Load(new StringReader(normalized));
        var matches = stream.Documents.Where(document => document.RootNode.Anchor.Value == fileId.ToString(CultureInfo.InvariantCulture)).ToArray();
        if (matches.Length != 1 || matches[0].RootNode is not YamlMappingNode root || Get(root, "Mesh") is not YamlMappingNode mesh)
            throw new InvalidDataException("Serialized mesh fileID is unavailable or ambiguous.");
        if (Integer(mesh, "m_MeshCompression") != 0 || Items(mesh, "m_BindPose").Any() ||
            Items(mesh, "m_BonesAABB").Any() || (Get(mesh, "m_Shapes") is YamlMappingNode shapes &&
                new[] { "vertices", "shapes", "channels" }.Any(key => Items(shapes, key).Any())) ||
            (Get(mesh, "m_StreamData") is YamlMappingNode external && !string.IsNullOrEmpty(Text(external, "path"))))
            throw new InvalidDataException("Compressed, streamed, or skinned serialized meshes require an unsupported decoder.");
        var vertex = Map(mesh, "m_VertexData");
        int count = Integer(vertex, "m_VertexCount");
        if (count <= 0 || count > 10000000) throw new InvalidDataException("Invalid serialized mesh vertex count.");
        var channels = Items(vertex, "m_Channels").Select(node => node is YamlMappingNode channel ?
            new Channel(Integer(channel, "stream"), Integer(channel, "offset"), Integer(channel, "format"), Integer(channel, "dimension")) :
            throw new InvalidDataException("Invalid vertex channel.")).ToArray();
        if (channels.Length < 1 || channels.Length > 14 || channels.Any(channel => channel.Stream < 0 || channel.Stream > 7 ||
            channel.Offset < 0 || channel.Offset > 4096 || channel.Dimension < 0 || channel.Dimension > 4) ||
            channels[0].Dimension != 3 || channels.Skip(12).Any(channel => channel.Dimension != 0))
            throw new InvalidDataException("Unsupported vertex channel layout.");
        byte[] bytes = Hex(Text(vertex, "_typelessdata"));
        if (Integer(vertex, "m_DataSize") != bytes.Length) throw new InvalidDataException("Vertex data size does not match its buffer.");
        var strides = new int[8];
        foreach (var channel in channels.Where(channel => channel.Dimension > 0))
            strides[channel.Stream] = Math.Max(strides[channel.Stream], checked(channel.Offset + channel.Dimension * Size(channel.Format)));
        var starts = new int[8];
        int end = 0;
        for (int index = 0; index < strides.Length; index++)
        {
            starts[index] = end;
            if (strides[index] != 0) end = checked((checked(end + checked(strides[index] * count)) + 15) & ~15);
        }
        if (channels.Where(channel => channel.Dimension > 0).Any(channel =>
            (long)starts[channel.Stream] + (long)strides[channel.Stream] * count > bytes.Length))
            throw new InvalidDataException("Vertex streams exceed their serialized buffer.");
        Vector4[] ReadChannel(int channelIndex, params int[] allowedDimensions)
        {
            if (channelIndex >= channels.Length || channels[channelIndex].Dimension == 0) return Array.Empty<Vector4>();
            var channel = channels[channelIndex];
            if (!allowedDimensions.Contains(channel.Dimension)) throw new InvalidDataException("Unsupported vertex attribute dimension.");
            var values = new Vector4[count];
            for (int vertexIndex = 0; vertexIndex < count; vertexIndex++)
            {
                var components = new float[4];
                for (int component = 0; component < channel.Dimension; component++)
                {
                    int offset = checked(starts[channel.Stream] + strides[channel.Stream] * vertexIndex + channel.Offset + Size(channel.Format) * component);
                    var data = bytes.AsSpan(offset);
                    float value = channel.Format switch
                    {
                        0 => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data)),
                        1 => (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(data)),
                        2 => data[0] / 255f,
                        3 => Math.Max(-1f, (sbyte)data[0] / 127f),
                        4 => BinaryPrimitives.ReadUInt16LittleEndian(data) / 65535f,
                        5 => Math.Max(-1f, BinaryPrimitives.ReadInt16LittleEndian(data) / 32767f),
                        6 => data[0],
                        7 => (sbyte)data[0],
                        8 => BinaryPrimitives.ReadUInt16LittleEndian(data),
                        9 => BinaryPrimitives.ReadInt16LittleEndian(data),
                        10 => BinaryPrimitives.ReadUInt32LittleEndian(data),
                        11 => BinaryPrimitives.ReadInt32LittleEndian(data),
                        _ => throw new InvalidDataException("Unsupported vertex component encoding.")
                    };
                    if (!float.IsFinite(value)) throw new InvalidDataException("Non-finite mesh vertex attribute.");
                    components[component] = value;
                }
                values[vertexIndex] = new Vector4(components[0], components[1], components[2], components[3]);
            }
            return values;
        }
        Vector3[] Vector3Channel(int index, params int[] dimensions) => ReadChannel(index, dimensions).Select(value => new Vector3(value.X, value.Y, value.Z)).ToArray();
        var result = new UnityMeshData
        {
            Positions = Vector3Channel(0, 3), Normals = Vector3Channel(1, 3), Tangents = ReadChannel(2, 4), Colors = ReadChannel(3, 4)
        };
        for (int index = 0; index < result.UV.Length; index++)
        {
            result.UV[index] = Vector3Channel(index + 4, 1, 2, 3);
            result.UVDimensions[index] = index + 4 < channels.Length ? channels[index + 4].Dimension : 0;
        }
        byte[] indices = Hex(Text(mesh, "m_IndexBuffer"));
        int indexSize = Integer(mesh, "m_IndexFormat") switch { 0 => 2, 1 => 4, _ => throw new InvalidDataException("Unsupported index format.") };
        foreach (var node in Items(mesh, "m_SubMeshes"))
        {
            if (node is not YamlMappingNode submesh || Integer(submesh, "topology") != 0) throw new InvalidDataException("Only triangle mesh topology is supported.");
            int first = Integer(submesh, "firstByte"), length = Integer(submesh, "indexCount"), baseVertex = Integer(submesh, "baseVertex");
            if (first < 0 || first % indexSize != 0 || length < 0 || length % 3 != 0 || (long)first + (long)length * indexSize > indices.Length)
                throw new InvalidDataException("Invalid submesh index range.");
            var triangles = new int[length];
            for (int index = 0; index < length; index++)
            {
                var data = indices.AsSpan(first + index * indexSize);
                long value = (indexSize == 2 ? BinaryPrimitives.ReadUInt16LittleEndian(data) : BinaryPrimitives.ReadUInt32LittleEndian(data)) + (long)baseVertex;
                if (value < 0 || value >= count) throw new InvalidDataException("Submesh index is outside the vertex buffer.");
                triangles[index] = (int)value;
            }
            result.Submeshes.Add(triangles);
        }
        if (result.Submeshes.Count == 0 || !result.Submeshes.Any(submesh => submesh.Length > 0)) throw new InvalidDataException("Serialized mesh contains no triangles.");
        return result;
    }

    internal static UnityMeshData Builtin(string guid, long fileId)
    {
        if (!string.Equals(guid, "0000000000000000e000000000000000", StringComparison.OrdinalIgnoreCase) || fileId != 10202)
            throw new InvalidDataException("Unsupported built-in Unity mesh identity.");
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var uv = new List<Vector3>();
        var indices = new List<int>();
        foreach (var normal in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ })
        {
            var horizontal = Math.Abs(normal.Y) > 0.5f ? Vector3.UnitX : Vector3.Normalize(Vector3.Cross(Vector3.UnitY, normal));
            var vertical = Vector3.Cross(normal, horizontal);
            int first = positions.Count;
            foreach (var corner in new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) })
            {
                positions.Add(normal * 0.5f + horizontal * (corner.X - 0.5f) + vertical * (corner.Y - 0.5f));
                normals.Add(normal);
                uv.Add(new Vector3(corner.X, corner.Y, 0));
            }
            indices.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
        }
        var result = new UnityMeshData { Positions = positions.ToArray(), Normals = normals.ToArray() };
        result.UV[0] = uv.ToArray();
        result.UVDimensions[0] = 2;
        result.Submeshes.Add(indices.ToArray());
        return result;
    }
}
