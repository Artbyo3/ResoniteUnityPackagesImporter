#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using YamlDotNet.RepresentationModel;

namespace UnityPackageImporter.Models;

internal sealed record UnityShapeDelta(int VertexIndex, Vector3 Position, Vector3 Normal, Vector3 Tangent);
internal sealed record UnityShapeFrame(float Weight, bool HasNormals, bool HasTangents, UnityShapeDelta[] Deltas);
internal sealed record UnityShapeChannel(string Name, UnityShapeFrame[] Frames);
internal sealed record UnityVertexSkin(int[] BoneIndices, float[] Weights);

internal sealed class UnitySerializedSkinData
{
    internal float[][] BindPoses { get; private init; } = Array.Empty<float[]>();
    internal UnityVertexSkin[] Vertices { get; private init; } = Array.Empty<UnityVertexSkin>();
    internal UnityShapeChannel[] Shapes { get; private init; } = Array.Empty<UnityShapeChannel>();

    internal void ValidateNativeShapes()
    {
        foreach (var shape in Shapes)
            if (shape.Frames.Length != 1 || shape.Frames[0].Weight != 1f)
                throw new InvalidDataException("Native serialized skin requires one 100-percent frame per blend shape; authored in-between frames cannot be evaluated faithfully.");
    }

    private static YamlNode? Get(YamlMappingNode node, string key) => node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;
    private static string Text(YamlMappingNode node, string key) => Get(node, key) is YamlScalarNode scalar ? scalar.Value ?? "" : throw new InvalidDataException("Missing or invalid skin scalar: " + key);
    private static int Integer(YamlMappingNode node, string key) => int.TryParse(Text(node, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : throw new InvalidDataException("Invalid skin integer: " + key);
    private static float Number(string value) => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result) && float.IsFinite(result) ? result : throw new InvalidDataException("Nonfinite or invalid skin number.");
    private static YamlMappingNode Map(YamlNode node) => node as YamlMappingNode ?? throw new InvalidDataException("Invalid skin mapping.");
    private static YamlNode[] Items(YamlMappingNode node, string key) => Get(node, key) switch
    {
        null => Array.Empty<YamlNode>(),
        YamlSequenceNode sequence => sequence.Children.ToArray(),
        _ => throw new InvalidDataException("Invalid skin sequence: " + key)
    };
    private static bool Flag(YamlMappingNode node, string key) => Integer(node, key) switch { 0 => false, 1 => true, _ => throw new InvalidDataException("Invalid shape flag: " + key) };
    private static Vector3 Vector(YamlMappingNode node, string key)
    {
        var vector = Map(Get(node, key) ?? throw new InvalidDataException("Missing shape vector: " + key));
        return new Vector3(Number(Text(vector, "x")), Number(Text(vector, "y")), Number(Text(vector, "z")));
    }

    internal static void ValidateInfluenceStorage(YamlMappingNode mesh)
    {
        foreach (string key in new[] { "m_Skin", "m_VariableBoneCountWeights" })
            if (Get(mesh, key) is YamlNode legacy && legacy switch
                { YamlScalarNode scalar => !string.IsNullOrEmpty(scalar.Value), YamlSequenceNode sequence => sequence.Children.Count != 0,
                    YamlMappingNode mapping when key == "m_VariableBoneCountWeights" => mapping.Children.Count != 1 ||
                        Get(mapping, "m_Data") is not YamlScalarNode data || !string.IsNullOrEmpty(data.Value), _ => true })
                throw new InvalidDataException("Unsupported legacy or variable-count skin data: " + key);
    }

    internal static UnitySerializedSkinData Decode(YamlMappingNode mesh, UnityMeshData geometry, Vector4[] weights, Vector4[] indices)
    {
        ValidateInfluenceStorage(mesh);
        var poses = Items(mesh, "m_BindPose").Select(node =>
        {
            var matrix = Map(node);
            var values = Enumerable.Range(0, 16).Select(index => Number(Text(matrix, "e" + index / 4 + index % 4))).ToArray();
            var numeric = new Matrix4x4(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7],
                values[8], values[9], values[10], values[11], values[12], values[13], values[14], values[15]);
            if (values[12] != 0 || values[13] != 0 || values[14] != 0 || values[15] != 1 ||
                !float.IsFinite(numeric.GetDeterminant()) || numeric.GetDeterminant() == 0)
                throw new InvalidDataException("Skin bind pose is singular or non-affine.");
            return values;
        }).ToArray();
        if (poses.Length == 0 || poses.Length > 65535 || weights.Length != geometry.Positions.Length || indices.Length != weights.Length)
            throw new InvalidDataException("Skin bind poses or vertex influence counts are unavailable or invalid.");
        var bounds = Items(mesh, "m_BonesAABB");
        if (bounds.Length != 0 && bounds.Length != poses.Length) throw new InvalidDataException("Skin bone bounds do not match bind poses.");
        if (Get(mesh, "m_BoneNameHashes") != null)
            Text(mesh, "m_BoneNameHashes");
        var vertices = new UnityVertexSkin[weights.Length];
        for (int vertex = 0; vertex < vertices.Length; vertex++)
        {
            float[] values = { weights[vertex].X, weights[vertex].Y, weights[vertex].Z, weights[vertex].W };
            float[] rawIndices = { indices[vertex].X, indices[vertex].Y, indices[vertex].Z, indices[vertex].W };
            if (values.Any(value => !float.IsFinite(value) || value < 0 || value > 1) || MathF.Abs(values.Sum() - 1) > 0.0001f ||
                rawIndices.Any(value => !float.IsFinite(value) || value < 0 || value >= poses.Length || value != MathF.Truncate(value)))
                throw new InvalidDataException("Skin influences have invalid weights or out-of-range bone indices.");
            vertices[vertex] = new UnityVertexSkin(rawIndices.Select(value => (int)value).ToArray(), values);
        }
        var usedBones = vertices.SelectMany(vertex => vertex.BoneIndices.Where((bone, index) => vertex.Weights[index] > 0)).ToHashSet();
        for (int bone = 0; bone < bounds.Length; bone++)
        {
            var box = Map(bounds[bone]);
            if (Get(box, "m_Min") != null || Get(box, "m_Max") != null)
            {
                var minimum = Map(Get(box, "m_Min") ?? throw new InvalidDataException("Missing bone minimum."));
                var maximum = Map(Get(box, "m_Max") ?? throw new InvalidDataException("Missing bone maximum."));
                bool empty = new[] { "x", "y", "z" }.All(axis => Text(minimum, axis) == "Infinity" && Text(maximum, axis) == "-Infinity");
                if (empty && !usedBones.Contains(bone)) continue;
                var min = Vector(box, "m_Min");
                var max = Vector(box, "m_Max");
                if (min.X > max.X || min.Y > max.Y || min.Z > max.Z) throw new InvalidDataException("Invalid skin bone bounds.");
            }
            else
            {
                Vector(box, "m_Center");
                var extent = Vector(box, "m_Extent");
                if (extent.X < 0 || extent.Y < 0 || extent.Z < 0) throw new InvalidDataException("Invalid skin bone extent.");
            }
        }
        return new UnitySerializedSkinData { BindPoses = poses, Vertices = vertices, Shapes = ReadShapes(mesh, geometry) };
    }

    private static UnityShapeChannel[] ReadShapes(YamlMappingNode mesh, UnityMeshData geometry)
    {
        if (Get(mesh, "m_Shapes") is not YamlNode shapesNode) return Array.Empty<UnityShapeChannel>();
        var shapes = Map(shapesNode);
        var deltas = Items(shapes, "vertices").Select(node =>
        {
            var delta = Map(node);
            int index = Integer(delta, "index");
            if (index < 0 || index >= geometry.Positions.Length) throw new InvalidDataException("Shape delta vertex is outside the mesh.");
            return new UnityShapeDelta(index, Vector(delta, "vertex"), Vector(delta, "normal"), Vector(delta, "tangent"));
        }).ToArray();
        var frames = Items(shapes, "shapes");
        var weights = Items(shapes, "fullWeights").Select(node => node is YamlScalarNode scalar ? Number(scalar.Value ?? "") : throw new InvalidDataException("Invalid shape frame weight.")).ToArray();
        if (frames.Length != weights.Length) throw new InvalidDataException("Shape frame weights do not match frame count.");
        var usedDeltas = new bool[deltas.Length];
        var decoded = frames.Select((node, frameIndex) =>
        {
            var frame = Map(node);
            int first = Integer(frame, "firstVertex"), count = Integer(frame, "vertexCount");
            bool normals = Flag(frame, "hasNormals"), tangents = Flag(frame, "hasTangents");
            if (first < 0 || count < 0 || (long)first + count > deltas.Length ||
                normals && geometry.Normals.Length == 0 || tangents && geometry.Tangents.Length == 0)
                throw new InvalidDataException("Shape frame range or required base attributes are invalid.");
            var selected = deltas.Skip(first).Take(count).ToArray();
            if (selected.Select(delta => delta.VertexIndex).Distinct().Count() != count ||
                Enumerable.Range(first, count).Any(index => usedDeltas[index]) ||
                selected.Any(delta => !normals && delta.Normal != Vector3.Zero || !tangents && delta.Tangent != Vector3.Zero))
                throw new InvalidDataException("Shape deltas overlap or disagree with frame attributes.");
            foreach (int index in Enumerable.Range(first, count)) usedDeltas[index] = true;
            return new UnityShapeFrame(weights[frameIndex] / 100f, normals, tangents, selected);
        }).ToArray();
        var usedFrames = new bool[frames.Length];
        var names = new HashSet<string>(StringComparer.Ordinal);
        var result = Items(shapes, "channels").Select(node =>
        {
            var channel = Map(node);
            string name = Text(channel, "name");
            int first = Integer(channel, "frameIndex"), count = Integer(channel, "frameCount");
            if (string.IsNullOrWhiteSpace(name) || !names.Add(name) || first < 0 || count <= 0 || (long)first + count > decoded.Length ||
                Enumerable.Range(first, count).Any(index => usedFrames[index]))
                throw new InvalidDataException("Shape channel identity or frame range is invalid.");
            var selected = decoded.Skip(first).Take(count).ToArray();
            for (int index = 0; index < selected.Length; index++)
                if (selected[index].Weight == 0 || index > 0 && selected[index].Weight <= selected[index - 1].Weight ||
                    selected[index].HasNormals != selected[0].HasNormals || selected[index].HasTangents != selected[0].HasTangents)
                    throw new InvalidDataException("Shape frame weights or attributes are incompatible.");
            foreach (int index in Enumerable.Range(first, count)) usedFrames[index] = true;
            return new UnityShapeChannel(name, selected);
        }).ToArray();
        if (usedDeltas.Any(used => !used) || usedFrames.Any(used => !used)) throw new InvalidDataException("Unowned shape deltas or frames remain.");
        return result;
    }

    internal T?[] ResolveBones<T>(IReadOnlyList<T?> authored) where T : class
    {
        if (authored.Count != BindPoses.Length) throw new InvalidDataException("Authored skin bone slots do not match serialized bind poses.");
        var used = new bool[BindPoses.Length];
        foreach (var vertex in Vertices)
            for (int influence = 0; influence < 4; influence++)
                if (vertex.Weights[influence] > 0) used[vertex.BoneIndices[influence]] = true;
        if (authored.Where((bone, index) => bone == null && used[index]).Any())
            throw new InvalidDataException("A weighted serialized skin bone has no exact authored target.");
        return authored.ToArray();
    }
}
