using System.Globalization;
using System.Numerics;
using UnityPackageImporter.Models;

internal static class UnitySerializedSkinTests
{
    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        test("Native skin rejects decoded multi-frame shapes without discarding source evidence", () =>
        {
            UnitySerializedMesh.Parse(Fixture(), 4300000, true, out var skin);
            Reject(() => skin!.ValidateNativeShapes());
            check(skin!.Shapes[0].Frames.Length == 2, "Rejected source frames were lost.");
        });
        test("Native skin accepts a single full-weight sparse shape", () =>
        {
            UnitySerializedMesh.Parse(NativeFixture(), 4300000, true, out var skin);
            skin!.ValidateNativeShapes();
            check(skin.Shapes[0].Frames[0].Deltas.Length == 1, "Sparse deltas changed.");
        });
        test("Native skin rejects a single non-full-weight shape", () =>
        {
            UnitySerializedMesh.Parse(NativeFixture().Replace("fullWeights: [100]", "fullWeights: [50]"), 4300000, true, out var skin);
            Reject(() => skin!.ValidateNativeShapes());
        });
        test("Serialized skin retains bind matrices four influences and two shape frames", () =>
        {
            var geometry = UnitySerializedMesh.Parse(Fixture(), 4300000, true, out var skin);
            check(skin != null && skin.BindPoses.Length == 2 && skin.BindPoses[1][3] == -2, "Bind pose order or translation changed.");
            check(geometry.Positions.SequenceEqual(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY }), "Geometry indices changed.");
            check(skin!.Vertices.All(vertex => vertex.BoneIndices.SequenceEqual(new[] { 0, 1, 0, 0 }) && vertex.Weights.SequenceEqual(new[] { 0.25f, 0.75f, 0f, 0f })), "Weights were reordered or normalized.");
            var shape = skin.Shapes.Single();
            check(shape.Name == "Expression" && shape.Frames.Select(frame => frame.Weight).SequenceEqual(new[] { 0.5f, 1f }), "Frame identity or authored weights were flattened.");
            check(shape.Frames[1].Deltas.Single() == new UnityShapeDelta(1, new Vector3(0, 2, 0), Vector3.UnitY, Vector3.UnitX), "Sparse deltas changed.");
            object first = new(), second = new();
            check(skin.ResolveBones(new[] { first, second }).SequenceEqual(new[] { first, second }), "Bone indices were resolved by name instead of exact order.");
        });
        test("Serialized skin cannot flatten into the static decoder", () => Reject(() => UnitySerializedMesh.Parse(Fixture(), 4300000)));
        test("Serialized skin requires every weighted authored target", () =>
        {
            UnitySerializedMesh.Parse(Fixture(), 4300000, true, out var skin);
            Reject(() => skin!.ResolveBones(new object?[] { new(), null }));
            Reject(() => skin!.ResolveBones(new object?[] { new() }));
        });
        test("Serialized skin retains proven unused null bone slots", () =>
        {
            UnitySerializedMesh.Parse(Fixture(1, 0), 4300000, true, out var skin);
            object first = new();
            var bones = skin!.ResolveBones(new object?[] { first, null });
            check(bones.Length == 2 && bones[0] == first && bones[1] == null, "Unused slot was removed or assigned an arbitrary target.");
        });
        foreach (int bone in new[] { -1, 2, int.MaxValue })
            test("Serialized skin rejects encoded out-of-range bone index " + bone, () =>
                Reject(() => UnitySerializedMesh.Parse(Fixture(secondBoneIndex: bone), 4300000, true, out _)));
        test("Serialized skin retains zero-delta shape frames without dropping channels", () =>
        {
            string yaml = System.Text.RegularExpressions.Regex.Replace(Fixture(), @"(?ms)^    vertices:\n.*?(?=^    shapes:)", "    vertices: []\n")
                .Replace("vertexCount: 1", "vertexCount: 0").Replace("firstVertex: 1", "firstVertex: 0");
            UnitySerializedMesh.Parse(yaml, 4300000, true, out var skin);
            check(skin!.Shapes.Single().Frames.Length == 2 && skin.Shapes[0].Frames.All(frame => frame.Deltas.Length == 0), "Empty authored shape frames were removed.");
        });
        foreach (int count in new[] { 1, 2, 3, 4 })
            foreach (int format in new[] { 6, 8, 10 })
                test($"Serialized skin preserves {count} influences with index format {format}", () =>
                {
                    UnitySerializedMesh.Parse(Fixture(count == 1 ? 1 : 0.25f, count == 1 ? 0 : 0.75f, count, format), 4300000, true, out var skin);
                    check(skin!.Vertices.All(vertex => vertex.Weights.Length == 4 && vertex.Weights[0] == (count == 1 ? 1 : 0.25f) &&
                        vertex.Weights.Skip(count).All(value => value == 0) && vertex.BoneIndices.Skip(count).All(value => value == 0)), "Compact skin channels did not retain index correspondence or zero padding.");
                });
        test("Serialized skin binds by exact index independently of optional bone hashes", () =>
        {
            foreach (string hashes in new[] { "", "opaque/source/hash", "00000000" })
            {
                UnitySerializedMesh.Parse(Fixture().Replace("m_BoneNameHashes: 0000000001000000", "m_BoneNameHashes: " + hashes), 4300000, true, out var skin);
                object first = new(), second = new();
                check(skin!.ResolveBones(new[] { first, second }).SequenceEqual(new[] { first, second }), "Optional hash metadata was used to invent a bone identity.");
            }
        });
        test("Serialized skin accepts empty variable-weight storage and unused infinite bounds", () =>
        {
            string bounds = "  m_BonesAABB:\n  - {m_Min: {x: 0, y: 0, z: 0}, m_Max: {x: 1, y: 1, z: 1}}\n  - {m_Min: {x: Infinity, y: Infinity, z: Infinity}, m_Max: {x: -Infinity, y: -Infinity, z: -Infinity}}\n";
            string yaml = Fixture(1, 0).Replace("  m_BoneNameHashes:", bounds + "  m_BoneNameHashes:")
                .Replace("  m_VariableBoneCountWeights: \n", "  m_VariableBoneCountWeights:\n    m_Data:\n");
            UnitySerializedMesh.Parse(yaml, 4300000, true, out var skin);
            check(skin!.ResolveBones(new object?[] { new(), null })[1] == null, "Unused bounds did not retain its null slot.");
            Reject(() => UnitySerializedMesh.Parse(yaml.Replace("m_Max: {x: 1, y: 1, z: 1}", "m_Max: {x: -1, y: 1, z: 1}"), 4300000, true, out _));
        });
        foreach (var weights in new[] { (-0.25f, 1.25f), (0.2f, 0.2f), (0f, 0f), (float.NaN, 1f), (float.PositiveInfinity, 0f) })
            test("Serialized skin rejects invalid weights " + weights, () => Reject(() => UnitySerializedMesh.Parse(Fixture(weights.Item1, weights.Item2), 4300000, true, out _)));
        test("Serialized skin rejects a nonzero epsilon on a missing bone", () =>
        {
            UnitySerializedMesh.Parse(Fixture(1, float.Epsilon), 4300000, true, out var skin);
            Reject(() => skin!.ResolveBones(new object?[] { new(), null }));
        });
        foreach (var mutation in new (string Name, Func<string, string> Apply)[]
        {
            ("missing index channel", yaml => yaml.Replace("offset: 64, format: 10, dimension: 4", "offset: 64, format: 10, dimension: 0")),
            ("normalized bone indices", yaml => yaml.Replace("offset: 64, format: 10", "offset: 64, format: 0")),
            ("malformed hash scalar", yaml => yaml.Replace("m_BoneNameHashes: 0000000001000000", "m_BoneNameHashes: []")),
            ("unknown mesh version", yaml => yaml.Replace("serializedVersion: 11", "serializedVersion: 12")),
            ("unknown vertex version", yaml => yaml.Replace("m_VertexData:\n", "m_VertexData:\n    serializedVersion: 4\n")),
            ("nonfinite matrix", yaml => yaml.Replace("e03: -2", "e03: NaN")),
            ("singular matrix", yaml => yaml.Replace("e00: 1", "e00: 0")),
            ("nonaffine matrix", yaml => yaml.Replace("e30: 0", "e30: 1")),
            ("unsupported compression", yaml => yaml.Replace("m_MeshCompression: 0", "m_MeshCompression: 1")),
            ("legacy skin", yaml => yaml.Replace("m_Skin: []", "m_Skin: [1]")),
            ("variable weights", yaml => yaml.Replace("m_VariableBoneCountWeights: ", "m_VariableBoneCountWeights: 0102")),
            ("populated variable weight mapping", yaml => yaml.Replace("  m_VariableBoneCountWeights: \n", "  m_VariableBoneCountWeights:\n    m_Data: 0102\n")),
            ("implicit rigid weights without decoder evidence", yaml => yaml.Replace("offset: 48, format: 0, dimension: 4", "offset: 48, format: 0, dimension: 0")),
            ("malformed shapes", yaml => yaml.Replace("m_Shapes:\n", "m_Shapes: []\n  ignored:\n")),
            ("out of range delta", yaml => yaml.Replace("index: 1", "index: 3")),
            ("overlapping deltas", yaml => yaml.Replace("firstVertex: 1", "firstVertex: 0")),
            ("unowned frame", yaml => yaml.Replace("frameCount: 2", "frameCount: 1")),
            ("out of range channel", yaml => yaml.Replace("frameIndex: 0", "frameIndex: 2")),
            ("missing frame weights", yaml => yaml.Replace("fullWeights: [50, 100]", "fullWeights: [50]")),
            ("reversed frame weights", yaml => yaml.Replace("fullWeights: [50, 100]", "fullWeights: [100, 50]")),
            ("zero frame weight", yaml => yaml.Replace("fullWeights: [50, 100]", "fullWeights: [0, 100]")),
            ("unflagged normals", yaml => yaml.Replace("hasNormals: 1", "hasNormals: 0")),
            ("nonfinite shape delta", yaml => yaml.Replace("vertex: {x: 0, y: 2, z: 0}", "vertex: {x: 0, y: NaN, z: 0}")),
            ("empty channel name", yaml => yaml.Replace("name: Expression", "name: ''")),
            ("invalid frame flag", yaml => yaml.Replace("hasTangents: 1", "hasTangents: 2"))
        })
            test("Serialized skin rejects " + mutation.Name, () => Reject(() => UnitySerializedMesh.Parse(mutation.Apply(Fixture()), 4300000, true, out _)));
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new Exception("Invalid serialized deformation data was accepted.");
    }

    internal static string NativeFixture(float firstWeight = 0.25f, float secondWeight = 0.75f) => Fixture(firstWeight, secondWeight)
        .Replace("    - {vertex: {x: 0, y: 1, z: 0}, normal: {x: 0, y: 1, z: 0}, tangent: {x: 1, y: 0, z: 0}, index: 1}\n", "")
        .Replace("    - {firstVertex: 1, vertexCount: 1, hasNormals: 1, hasTangents: 1}\n", "")
        .Replace("frameCount: 2", "frameCount: 1").Replace("fullWeights: [50, 100]", "fullWeights: [100]");

    internal static string Fixture(float firstWeight = 0.25f, float secondWeight = 0.75f, int influences = 4, int indexFormat = 10, int secondBoneIndex = 1)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        foreach (var position in new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY })
        {
            foreach (float value in new[] { position.X, position.Y, position.Z, 0, 0, 1, 1, 0, 0, 1, position.X, position.Y }) writer.Write(value);
            foreach (float value in new[] { firstWeight, secondWeight, 0, 0 }.Take(influences)) writer.Write(value);
            foreach (int index in new[] { 0, secondBoneIndex, 0, 0 }.Take(influences))
                if (indexFormat == 6) writer.Write((byte)index);
                else if (indexFormat == 8) writer.Write((ushort)index);
                else writer.Write(index);
        }
        string matrix(float translation) => "{e00: 1, e01: 0, e02: 0, e03: " + translation.ToString(CultureInfo.InvariantCulture) + ", e10: 0, e11: 1, e12: 0, e13: 0, e20: 0, e21: 0, e22: 1, e23: 0, e30: 0, e31: 0, e32: 0, e33: 1}";
        return "--- !u!43 &4300000\nMesh:\n  serializedVersion: 11\n  m_MeshCompression: 0\n  m_BindPose:\n  - " + matrix(0) + "\n  - " + matrix(-2) + "\n" +
            "  m_BoneNameHashes: 0000000001000000\n  m_Skin: []\n  m_VariableBoneCountWeights: \n  m_IndexFormat: 0\n  m_IndexBuffer: 000001000200\n" +
            "  m_SubMeshes:\n  - {firstByte: 0, indexCount: 3, topology: 0, baseVertex: 0}\n" +
            "  m_Shapes:\n    vertices:\n    - {vertex: {x: 0, y: 1, z: 0}, normal: {x: 0, y: 1, z: 0}, tangent: {x: 1, y: 0, z: 0}, index: 1}\n" +
            "    - {vertex: {x: 0, y: 2, z: 0}, normal: {x: 0, y: 1, z: 0}, tangent: {x: 1, y: 0, z: 0}, index: 1}\n" +
            "    shapes:\n    - {firstVertex: 0, vertexCount: 1, hasNormals: 1, hasTangents: 1}\n    - {firstVertex: 1, vertexCount: 1, hasNormals: 1, hasTangents: 1}\n" +
            "    channels:\n    - {name: Expression, nameHash: 123, frameIndex: 0, frameCount: 2}\n    fullWeights: [50, 100]\n" +
            "  m_VertexData:\n    m_VertexCount: 3\n    m_Channels:\n    - {stream: 0, offset: 0, format: 0, dimension: 3}\n    - {stream: 0, offset: 12, format: 0, dimension: 3}\n" +
            "    - {stream: 0, offset: 24, format: 0, dimension: 4}\n    - {stream: 0, offset: 0, format: 0, dimension: 0}\n    - {stream: 0, offset: 40, format: 0, dimension: 2}\n" +
            string.Concat(Enumerable.Repeat("    - {stream: 0, offset: 0, format: 0, dimension: 0}\n", 7)) +
            $"    - {{stream: 0, offset: 48, format: 0, dimension: {influences}}}\n    - {{stream: 0, offset: {48 + 4 * influences}, format: {indexFormat}, dimension: {influences}}}\n    m_DataSize: {stream.Length}\n    _typelessdata: " + Convert.ToHexString(stream.ToArray()) + "\n";
    }
}
