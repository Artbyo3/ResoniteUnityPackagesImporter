using System.Numerics;
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
