using System.Numerics;
using System.Security.Cryptography;
using UnityPackageImporter.Models;

internal static class UnitySerializedMeshTests
{
    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        test("Static renderer selection excludes derived skin renderers", () =>
        {
            check(MeshReferenceLookup.IsExactStaticRenderer(typeof(StaticRenderer), typeof(StaticRenderer)), "Exact native static renderer was excluded.");
            check(!MeshReferenceLookup.IsExactStaticRenderer(typeof(SkinRenderer), typeof(StaticRenderer)), "Replacement skin renderer must never be selected for static disposal or indexing.");
        });
        test("Serialized mesh decoder preserves positions normals UVs and triangles", () =>
        {
            var mesh = UnitySerializedMesh.Parse(Fixture(), 4300000);
            check(mesh.Positions.SequenceEqual(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY }), "Position buffer changed.");
            check(mesh.Normals.All(normal => normal == Vector3.UnitZ), "Normals changed.");
            check(mesh.UV[0].SequenceEqual(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY }), "UV buffer changed.");
            check(mesh.Submeshes.Single().SequenceEqual(new[] { 0, 1, 2 }), "Triangle order changed.");
        });
        test("Serialized meshes retain separate material submeshes and 32-bit indices", () =>
        {
            string yaml = Fixture().Replace("m_IndexFormat: 0", "m_IndexFormat: 1")
                .Replace("m_IndexBuffer: 000001000200", "m_IndexBuffer: 000000000100000002000000020000000100000000000000")
                .Replace("  m_SubMeshes:\n", "  m_SubMeshes:\n  - firstByte: 12\n    indexCount: 3\n    topology: 0\n    baseVertex: 0\n");
            var mesh = UnitySerializedMesh.Parse(yaml, 4300000);
            check(mesh.Submeshes.Count == 2 && mesh.Submeshes[0].SequenceEqual(new[] { 2, 1, 0 }) && mesh.Submeshes[1].SequenceEqual(new[] { 0, 1, 2 }), "Submesh boundaries or 32-bit indices changed.");
        });
        test("Serialized mesh decoder honors aligned vertex streams", () =>
        {
            var interleaved = Bytes();
            var bytes = new byte[128];
            for (int index = 0; index < 3; index++)
            {
                Array.Copy(interleaved, index * 32, bytes, index * 12, 12);
                Array.Copy(interleaved, index * 32 + 12, bytes, 48 + index * 20, 12);
                Array.Copy(interleaved, index * 32 + 24, bytes, 48 + index * 20 + 12, 8);
            }
            string yaml = Fixture().Replace(Convert.ToHexString(interleaved), Convert.ToHexString(bytes))
                .Replace("m_DataSize: 96", "m_DataSize: 128")
                .Replace("{stream: 0, offset: 12, format: 0, dimension: 3}", "{stream: 1, offset: 0, format: 0, dimension: 3}")
                .Replace("{stream: 0, offset: 24, format: 0, dimension: 2}", "{stream: 1, offset: 12, format: 0, dimension: 2}");
            var mesh = UnitySerializedMesh.Parse(yaml, 4300000);
            check(mesh.Positions[1] == Vector3.UnitX && mesh.Normals[2] == Vector3.UnitZ && mesh.UV[0][2] == Vector3.UnitY, "Stream alignment was ignored.");
        });
        foreach (var fixture in new (string Name, Func<string, string> Mutate, string Diagnostic)[]
        {
            ("scalar bind poses", yaml => yaml.Replace("m_BindPose: []", "m_BindPose: invalid"), "m_BindPose"),
            ("null bind poses", yaml => yaml.Replace("m_BindPose: []", "m_BindPose: null"), "m_BindPose"),
            ("mapping bind poses", yaml => yaml.Replace("m_BindPose: []", "m_BindPose: {e00: 1}"), "m_BindPose"),
            ("legacy deformation without bind poses", yaml => yaml + "  m_Skin: [1]\n", "m_Skin"),
            ("variable deformation without bind poses", yaml => yaml + "  m_VariableBoneCountWeights: {m_Data: 01}\n", "m_VariableBoneCountWeights"),
            ("mapping bone bounds", yaml => yaml + "  m_BonesAABB: {center: 0}\n", "m_BonesAABB"),
            ("scalar shape container", yaml => yaml + "  m_Shapes: invalid\n", "m_Shapes"),
            ("sequence shape container", yaml => yaml + "  m_Shapes: []\n", "m_Shapes"),
            ("mapping shape vertices", yaml => yaml + "  m_Shapes: {vertices: {index: 0}}\n", "vertices"),
            ("scalar shape frames", yaml => yaml + "  m_Shapes: {shapes: invalid}\n", "shapes"),
            ("scalar shape channels", yaml => yaml + "  m_Shapes: {channels: invalid}\n", "channels"),
            ("orphan shape weights", yaml => yaml + "  m_Shapes: {fullWeights: [100]}\n", "unsupported decoder"),
            ("scalar shape weights", yaml => yaml + "  m_Shapes: {fullWeights: invalid}\n", "fullWeights"),
            ("scalar stream container", yaml => yaml + "  m_StreamData: invalid\n", "m_StreamData"),
            ("mapping stream path", yaml => yaml + "  m_StreamData: {path: {name: external}}\n", "path"),
            ("overlapping normals", yaml => yaml.Replace("offset: 12, format: 0, dimension: 3", "offset: 8, format: 0, dimension: 3"), "overlap"),
            ("overlapping UVs", yaml => yaml.Replace("offset: 24, format: 0, dimension: 2", "offset: 20, format: 0, dimension: 2"), "overlap"),
            ("overflowing stream extent", yaml => yaml.Replace("m_VertexCount: 3", "m_VertexCount: 10000000").Replace("offset: 24", "offset: 4096"), "buffer"),
            ("partial index element", yaml => yaml.Replace("m_IndexBuffer: 000001000200", "m_IndexBuffer: 000001000200FF"), "index buffer")
        })
            test("Serialized mesh rejects malformed metadata: " + fixture.Name, () =>
            {
                bool rejected = false;
                try { UnitySerializedMesh.Parse(fixture.Mutate(Fixture()), 4300000); }
                catch (InvalidDataException error) { rejected = error.Message.Contains(fixture.Diagnostic, StringComparison.OrdinalIgnoreCase); }
                check(rejected, "Malformed mesh must reject with a diagnostic identifying " + fixture.Diagnostic + ".");
            });
        test("Serialized mesh accepts explicitly empty optional deformation metadata", () =>
        {
            var mesh = UnitySerializedMesh.Parse(Fixture() + "  m_BonesAABB: []\n  m_Shapes: {vertices: [], shapes: [], channels: [], fullWeights: []}\n  m_StreamData: {path: ''}\n", 4300000);
            check(mesh.Positions.SequenceEqual(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY }) &&
                mesh.Submeshes.Single().SequenceEqual(new[] { 0, 1, 2 }), "Empty optional metadata changed the authored geometry.");
        });
        test("Serialized mesh accepts sparse stream IDs without final alignment padding", () =>
        {
            var interleaved = Bytes();
            var bytes = new byte[108];
            for (int index = 0; index < 3; index++)
            {
                Array.Copy(interleaved, index * 32, bytes, index * 12, 12);
                Array.Copy(interleaved, index * 32 + 12, bytes, 48 + index * 20, 20);
            }
            string yaml = Fixture().Replace(Convert.ToHexString(interleaved), Convert.ToHexString(bytes))
                .Replace("m_DataSize: 96", "m_DataSize: 108")
                .Replace("{stream: 0, offset: 12, format: 0, dimension: 3}", "{stream: 5, offset: 0, format: 0, dimension: 3}")
                .Replace("{stream: 0, offset: 24, format: 0, dimension: 2}", "{stream: 5, offset: 12, format: 0, dimension: 2}");
            var mesh = UnitySerializedMesh.Parse(yaml, 4300000);
            check(mesh.Positions.SequenceEqual(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY }) &&
                mesh.Normals.All(normal => normal == Vector3.UnitZ) &&
                mesh.UV[0].SequenceEqual(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY }),
                "Stream gaps, shared offsets across streams, or the final unpadded stream changed authored channels.");
        });
        test("Serialized mesh rejects partial 32-bit index elements", () =>
        {
            string yaml = Fixture().Replace("m_IndexFormat: 0", "m_IndexFormat: 1")
                .Replace("m_IndexBuffer: 000001000200", "m_IndexBuffer: 000000000100000002000000FFFF");
            bool rejected = false;
            try { UnitySerializedMesh.Parse(yaml, 4300000); }
            catch (InvalidDataException error) { rejected = error.Message.Contains("index buffer", StringComparison.OrdinalIgnoreCase); }
            check(rejected, "A trailing partial 32-bit index element was silently discarded.");
        });
        foreach (var invalid in new[] { "missing-id", "compressed", "truncated", "out-of-range", "topology", "nonfinite", "skin", "streamed", "unknown-format" })
            test("Serialized mesh rejects unsafe input: " + invalid, () =>
            {
                string yaml = Fixture();
                yaml = invalid switch
                {
                    "compressed" => yaml.Replace("m_MeshCompression: 0", "m_MeshCompression: 1"),
                    "truncated" => yaml.Replace("m_DataSize: 96", "m_DataSize: 95"),
                    "out-of-range" => yaml.Replace("m_IndexBuffer: 000001000200", "m_IndexBuffer: 000001000300"),
                    "topology" => yaml.Replace("topology: 0", "topology: 5"),
                    "skin" => yaml.Replace("m_BindPose: []", "m_BindPose: [1]"),
                    "streamed" => yaml + "  m_StreamData: {path: streamed.resS}\n",
                    "unknown-format" => yaml.Replace("format: 0, dimension: 3", "format: 99, dimension: 3"),
                    "nonfinite" => yaml.Replace(Convert.ToHexString(Bytes()), "0000C07F" + Convert.ToHexString(Bytes())[8..]),
                    _ => yaml
                };
                bool rejected = false;
                try { UnitySerializedMesh.Parse(yaml, invalid == "missing-id" ? 999 : 4300000); }
                catch (InvalidDataException) { rejected = true; }
                check(rejected, "Unsupported data must not produce guessed geometry.");
            });
        test("Built-in plane preserves dimensions tessellation normals winding and UVs", () =>
        {
            var mesh = UnitySerializedMesh.Builtin("0000000000000000e000000000000000", 10209);
            check(mesh.Positions.Length == 121 && mesh.Submeshes.Single().Length == 600, "Plane geometry is incomplete.");
            check(mesh.Positions.Min(position => position.X) == -5 && mesh.Positions.Max(position => position.X) == 5 &&
                mesh.Positions.Min(position => position.Z) == -5 && mesh.Positions.Max(position => position.Z) == 5 &&
                mesh.Positions.All(position => MathF.Abs(position.Y) < 1e-14f), "Plane dimensions or local orientation changed.");
            check(mesh.Normals.Length == 121 && mesh.Normals.All(normal => normal == Vector3.UnitY), "Plane normals changed.");
            check(mesh.UVDimensions[0] == 2 && mesh.UV[0].Length == 121 &&
                mesh.UV[0].First() == Vector3.Zero && mesh.UV[0].Last() == new Vector3(1, 1, 0), "Plane UV extent changed.");
            for (int row = 0; row < 11; row++)
                for (int column = 0; column < 11; column++)
                {
                    int vertex = row * 11 + column;
                    check(Vector3.Distance(mesh.UV[0][vertex], new Vector3(column / 10f, row / 10f, 0)) < 0.0000001f, "Plane UV grid changed.");
                    check(MathF.Abs(mesh.Positions[vertex].X - (5 - column)) < 0.000001f &&
                        MathF.Abs(mesh.Positions[vertex].Z - (5 - row)) < 0.000001f,
                        "Plane UV orientation disagrees with the Unity editor reference.");
                    check(mesh.Tangents[vertex] == new Vector4(-1, 0, 0, -1), "Plane tangent frame changed.");
                }
            var indices = mesh.Submeshes.Single();
            check(indices.Distinct().Count() == mesh.Positions.Length, "Plane contains disconnected vertices.");
            float area = 0;
            for (int triangle = 0; triangle < indices.Length; triangle += 3)
            {
                var normal = Vector3.Cross(mesh.Positions[indices[triangle + 1]] - mesh.Positions[indices[triangle]],
                    mesh.Positions[indices[triangle + 2]] - mesh.Positions[indices[triangle]]);
                check(MathF.Abs(normal.Y - 1) < 0.000002f && MathF.Abs(normal.X) < 1e-14f && MathF.Abs(normal.Z) < 1e-14f,
                    "Plane contains reversed or degenerate triangles.");
                area += normal.Length() * 0.5f;
            }
            check(area == 100, "Plane area changed.");
        });
        foreach (var reference in new[]
        {
            (Id: 10202, Vertices: 24, Indices: 36, Hash: "A65273EFBF951B7B7F7C27A092144933DC46FA68C9767556EFC922A3380023E0"),
            (Id: 10206, Vertices: 88, Indices: 240, Hash: "02AAFAFF4D0EF6C67D6F7C9A12E59F47D2BF73CA39A76C1CFA624A38547136D8"),
            (Id: 10207, Vertices: 515, Indices: 2304, Hash: "FFDD61863A5D5EC3106DF7F96C377081A25BF0E82A7A3FE82CF0E58C0E67F553"),
            (Id: 10208, Vertices: 550, Indices: 2496, Hash: "69084D7E1334B3FCEBE6620C72CACBFC2219CD8FF3F6EDA9CF826167D6DA2A97"),
            (Id: 10209, Vertices: 121, Indices: 600, Hash: "2B4BE7462FFD2DF465B133F4841D5D298532ED487D4019DDFC2BCD2E9B53D048"),
            (Id: 10210, Vertices: 4, Indices: 6, Hash: "ACFF13CD9679A83886B79967F053C052680390EDB1B953D998BF9CF5477E4937")
        })
        {
            test($"Built-in {reference.Id} matches all Unity editor reference channels", () =>
            {
                var mesh = UnitySerializedMesh.Builtin("0000000000000000e000000000000000", reference.Id);
                check(mesh.Positions.Length == reference.Vertices && mesh.Submeshes.Single().Length == reference.Indices,
                    "Verified primitive topology changed.");
                check(mesh.Normals.Length == reference.Vertices && mesh.Tangents.Length == reference.Vertices &&
                    mesh.UV[0].Length == reference.Vertices && mesh.UVDimensions[0] == 2, "Primitive channels are incomplete.");
                check(mesh.Submeshes[0].All(index => index >= 0 && index < reference.Vertices), "Primitive index is invalid.");
                check(Convert.ToHexString(SHA256.HashData(BuiltinBytes(reference.Id, mesh))) == reference.Hash,
                    "Primitive positions, normals, tangents, UVs or triangle order differ from the Unity editor capture.");
                var second = UnitySerializedMesh.Builtin("0000000000000000e000000000000000", reference.Id);
                mesh.Positions[0] = new Vector3(999);
                mesh.Submeshes[0][0] = -1;
                check(Convert.ToHexString(SHA256.HashData(BuiltinBytes(reference.Id, second))) == reference.Hash,
                    "An import mutated another import's primitive template.");
            });
            test($"Built-in {reference.Id} rejects corrupt truncated or foreign reference data", () =>
            {
                var mesh = UnitySerializedMesh.Builtin("0000000000000000e000000000000000", reference.Id);
                var bytes = BuiltinBytes(reference.Id, mesh);
                RejectBuiltin(() => UnityBuiltinMeshes.Decode(reference.Id, bytes[..^1]));
                RejectBuiltin(() => UnityBuiltinMeshes.Decode(reference.Id, bytes.Concat(new byte[] { 0 }).ToArray()));
                bytes[16] ^= 1;
                RejectBuiltin(() => UnityBuiltinMeshes.Decode(reference.Id, bytes));
                RejectBuiltin(() => UnityBuiltinMeshes.Decode(reference.Id == 10202 ? 10207 : 10202, bytes));
            });
        }
        test("Unknown built-in identities remain unsupported rather than malformed data", () =>
        {
            bool rejected = false;
            try { UnitySerializedMesh.Builtin("0000000000000000e000000000000000", 123456); }
            catch (NotSupportedException error)
            {
                rejected = ReportFailureDetails.Classify(error) == ReportFailureReason.UnsupportedOperation;
            }
            check(rejected, "Unknown built-in identity was substituted or misclassified as malformed input.");
            rejected = false;
            try { UnitySerializedMesh.Builtin("unrelated", 10209); }
            catch (System.IO.InvalidDataException) { rejected = true; }
            check(rejected, "A package mesh was mistaken for a built-in plane by fileID alone.");
        });
        test("Built-in cube uses exact resource identity and unit dimensions", () =>
        {
            var mesh = UnitySerializedMesh.Builtin("0000000000000000e000000000000000", 10202);
            check(mesh.Positions.Length == 24 && mesh.Submeshes.Single().Length == 36, "Cube geometry is incomplete.");
            check(mesh.Positions.Aggregate(Vector3.Min) == new Vector3(-0.5f) && mesh.Positions.Aggregate(Vector3.Max) == new Vector3(0.5f), "Cube size or origin is wrong.");
            for (int face = 0; face < 6; face++)
            {
                var indices = mesh.Submeshes[0];
                int index = face * 6;
                var normal = Vector3.Cross(mesh.Positions[indices[index + 1]] - mesh.Positions[indices[index]], mesh.Positions[indices[index + 2]] - mesh.Positions[indices[index]]);
                check(Vector3.Dot(normal, mesh.Normals[indices[index]]) > 0, "Cube winding disagrees with its normals.");
            }
            bool rejected = false;
            try { UnitySerializedMesh.Builtin("unrelated", 10202); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "A package mesh must not be mistaken for a built-in mesh by fileID alone.");
        });
    }

    private class StaticRenderer { }
    private sealed class SkinRenderer : StaticRenderer { }
    private static void RejectBuiltin(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new Exception("Corrupt or foreign primitive data was accepted.");
    }

    private static byte[] BuiltinBytes(int fileId, UnityMeshData mesh)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(0x314D5055u);
        writer.Write(fileId);
        writer.Write(mesh.Positions.Length);
        writer.Write(mesh.Submeshes.Single().Length);
        for (int vertex = 0; vertex < mesh.Positions.Length; vertex++)
        {
            foreach (var vector in new[] { mesh.Positions[vertex], mesh.Normals[vertex] })
            {
                writer.Write(vector.X);
                writer.Write(vector.Y);
                writer.Write(vector.Z);
            }
            var tangent = mesh.Tangents[vertex];
            writer.Write(tangent.X);
            writer.Write(tangent.Y);
            writer.Write(tangent.Z);
            writer.Write(tangent.W);
            writer.Write(mesh.UV[0][vertex].X);
            writer.Write(mesh.UV[0][vertex].Y);
        }
        foreach (int index in mesh.Submeshes.Single()) writer.Write(index);
        return stream.ToArray();
    }
    private static byte[] Bytes()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        foreach (var position in new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY })
            foreach (float value in new[] { position.X, position.Y, position.Z, 0f, 0f, 1f, position.X, position.Y }) writer.Write(value);
        return stream.ToArray();
    }
    private static string Fixture() => "--- !u!43 &4300000\nMesh:\n  m_MeshCompression: 0\n  m_BindPose: []\n  m_IndexFormat: 0\n  m_IndexBuffer: 000001000200\n  m_SubMeshes:\n  - firstByte: 0\n    indexCount: 3\n    topology: 0\n    baseVertex: 0\n  m_VertexData:\n    m_VertexCount: 3\n    m_Channels:\n    - {stream: 0, offset: 0, format: 0, dimension: 3}\n    - {stream: 0, offset: 12, format: 0, dimension: 3}\n    - {stream: 0, offset: 0, format: 0, dimension: 0}\n    - {stream: 0, offset: 0, format: 0, dimension: 0}\n    - {stream: 0, offset: 24, format: 0, dimension: 2}\n    m_DataSize: 96\n    _typelessdata: " + Convert.ToHexString(Bytes()) + "\n";
}
