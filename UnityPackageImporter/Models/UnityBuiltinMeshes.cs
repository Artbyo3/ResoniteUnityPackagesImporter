#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;

namespace UnityPackageImporter.Models;

internal static class UnityBuiltinMeshes
{
    private sealed record Reference(int Vertices, int Indices, string Hash);

    private static readonly IReadOnlyDictionary<long, Reference> References = new Dictionary<long, Reference>
    {
        [10202] = new(24, 36, "A65273EFBF951B7B7F7C27A092144933DC46FA68C9767556EFC922A3380023E0"),
        [10206] = new(88, 240, "02AAFAFF4D0EF6C67D6F7C9A12E59F47D2BF73CA39A76C1CFA624A38547136D8"),
        [10207] = new(515, 2304, "FFDD61863A5D5EC3106DF7F96C377081A25BF0E82A7A3FE82CF0E58C0E67F553"),
        [10208] = new(550, 2496, "69084D7E1334B3FCEBE6620C72CACBFC2219CD8FF3F6EDA9CF826167D6DA2A97"),
        [10209] = new(121, 600, "2B4BE7462FFD2DF465B133F4841D5D298532ED487D4019DDFC2BCD2E9B53D048"),
        [10210] = new(4, 6, "ACFF13CD9679A83886B79967F053C052680390EDB1B953D998BF9CF5477E4937")
    };

    private static Reference GetReference(long fileId) => References.TryGetValue(fileId, out var reference)
        ? reference : throw new NotSupportedException("Unsupported built-in Unity mesh identity: " + fileId.ToString(CultureInfo.InvariantCulture));

    internal static UnityMeshData Load(long fileId)
    {
        var reference = GetReference(fileId);
        string resource = "UnityPackageImporter.Models.BuiltinMeshes." + fileId.ToString(CultureInfo.InvariantCulture) + ".mesh.txt";
        using var stream = typeof(UnityBuiltinMeshes).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidDataException("Built-in Unity mesh resource is unavailable.");
        using var text = new StreamReader(stream);
        using var packed = new MemoryStream(Convert.FromBase64String(text.ReadToEnd()));
        using var gzip = new GZipStream(packed, CompressionMode.Decompress);
        var bytes = new byte[16 + reference.Vertices * 48 + reference.Indices * 4];
        gzip.ReadExactly(bytes);
        if (gzip.ReadByte() != -1) throw new InvalidDataException("Built-in Unity mesh resource has excess data.");
        return Decode(fileId, bytes);
    }

    internal static UnityMeshData Decode(long fileId, byte[] bytes)
    {
        var reference = GetReference(fileId);
        if (bytes.Length != 16 + reference.Vertices * 48 + reference.Indices * 4 ||
            !string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), reference.Hash, StringComparison.Ordinal))
            throw new InvalidDataException("Built-in Unity mesh resource does not match its verified reference.");
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt32() != 0x314D5055 || reader.ReadInt32() != fileId ||
            reader.ReadInt32() != reference.Vertices || reader.ReadInt32() != reference.Indices)
            throw new InvalidDataException("Built-in Unity mesh resource identity is invalid.");
        var positions = new Vector3[reference.Vertices];
        var normals = new Vector3[reference.Vertices];
        var tangents = new Vector4[reference.Vertices];
        var uv = new Vector3[reference.Vertices];
        for (int vertex = 0; vertex < positions.Length; vertex++)
        {
            positions[vertex] = new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            normals[vertex] = new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            tangents[vertex] = new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            uv[vertex] = new(reader.ReadSingle(), reader.ReadSingle(), 0);
        }
        var indices = new int[reference.Indices];
        for (int index = 0; index < indices.Length; index++) indices[index] = reader.ReadInt32();
        var mesh = new UnityMeshData { Positions = positions, Normals = normals, Tangents = tangents };
        mesh.UV[0] = uv;
        mesh.UVDimensions[0] = 2;
        mesh.Submeshes.Add(indices);
        return mesh;
    }
}
