using System;
using System.Collections.Generic;
using Assimp;

namespace UnityPackageImporter.Models;

// Convert the coordinate unit, not the authored object scale. For a uniform
// unit conversion C, points become C*p and transforms become C*M*C^-1.
// Thus translations change units while rotation/scale and bone weights do not.
internal static class ModelUnitConversion
{
    internal static void Apply(Scene scene, float factor)
    {
        if (scene?.RootNode == null) throw new ArgumentException("Model has no root node.", nameof(scene));
        if (!float.IsFinite(factor) || factor <= 0f) throw new ArgumentOutOfRangeException(nameof(factor));
        if (factor == 1f) return;

        ConvertNode(scene.RootNode, factor);
        var meshes = new HashSet<Mesh>();
        var bones = new HashSet<Bone>();
        var shapes = new HashSet<MeshAnimationAttachment>();
        foreach (var mesh in scene.Meshes)
        {
            if (!meshes.Add(mesh)) continue;
            ScalePositions(mesh.Vertices, factor);
            foreach (var bone in mesh.Bones)
                if (bones.Add(bone)) bone.OffsetMatrix = ConvertTransform(bone.OffsetMatrix, factor);
            foreach (var shape in mesh.MeshAnimationAttachments)
                if (shapes.Add(shape)) ScalePositions(shape.Vertices, factor);
        }
        var channels = new HashSet<NodeAnimationChannel>();
        foreach (var animation in scene.Animations)
            foreach (var channel in animation.NodeAnimationChannels)
            {
                if (!channels.Add(channel)) continue;
                for (int i = 0; i < channel.PositionKeys.Count; i++)
                {
                    var key = channel.PositionKeys[i];
                    key.Value *= factor;
                    channel.PositionKeys[i] = key;
                }
            }
    }

    private static void ConvertNode(Node node, float factor)
    {
        node.Transform = ConvertTransform(node.Transform, factor);
        foreach (var child in node.Children) ConvertNode(child, factor);
    }

    private static Matrix4x4 ConvertTransform(Matrix4x4 matrix, float factor)
    {
        matrix.A4 *= factor;
        matrix.B4 *= factor;
        matrix.C4 *= factor;
        return matrix;
    }

    private static void ScalePositions(List<Vector3D> positions, float factor)
    {
        for (int i = 0; i < positions.Count; i++) positions[i] *= factor;
    }
}
