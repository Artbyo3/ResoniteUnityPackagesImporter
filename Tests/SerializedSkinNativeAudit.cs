using System.Reflection;
using System.Runtime.Loader;

internal static class SerializedSkinNativeAudit
{
    internal static int Run(string dll, string engineDirectory, IEnumerable<string> sourceAssets)
    {
        var roots = new[] { Path.GetDirectoryName(Path.GetFullPath(dll))!, Path.GetFullPath(engineDirectory),
            Path.Combine(Path.GetFullPath(engineDirectory), "Libraries"), Path.Combine(Path.GetFullPath(engineDirectory), "rml_libs") };
        Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
        {
            string? path = roots.Select(root => Path.Combine(root, name.Name + ".dll")).FirstOrDefault(File.Exists);
            return path == null ? null : context.LoadFromAssemblyPath(path);
        }
        AssemblyLoadContext.Default.Resolving += Resolve;
        try
        {
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(dll));
            var parser = assembly.GetType("UnityPackageImporter.Models.UnitySerializedMesh", true)!.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                .Single(method => method.Name == "Parse" && method.GetParameters().Length == 4);
            var builder = assembly.GetType("UnityPackageImporter.Models.UnitySerializedSkinMeshImporter", true)!.GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Static)!;
            foreach (string unsupported in new[] { UnitySerializedSkinTests.Fixture(), UnitySerializedSkinTests.NativeFixture().Replace("fullWeights: [100]", "fullWeights: [50]") })
            {
                object?[] rejected = { unsupported, 4300000L, true, null };
                object decoded = parser.Invoke(null, rejected)!;
                bool failed = false;
                try { builder.Invoke(null, new[] { decoded, rejected[3] }); }
                catch (TargetInvocationException error) when (error.InnerException is InvalidDataException) { failed = true; }
                Check(failed, "Native builder accepted unsupported blend-shape evaluation.");
            }
            foreach (var weights in new[] { (First: 0.25f, Second: 0.74999f), (First: float.Epsilon, Second: 1f) })
            {
                object?[] source = { UnitySerializedSkinTests.NativeFixture(weights.First, weights.Second), 4300000L, true, null };
                object decoded = parser.Invoke(null, source)!;
                dynamic weighted = builder.Invoke(null, new[] { decoded, source[3] })!;
                dynamic binding = weighted.RawBoneBindings[0];
                Check(Math.Abs((float)binding.TotalWeight - 1f) < 0.000001f, "Native binding was not normalized.");
                Check(binding.weight1 > 0f && binding.boneIndex1 == 0, "A positive influence was trimmed or rebound.");
                Check(binding.boneIndex2 == -1 && binding.boneIndex3 == -1, "Inactive influences do not use native sentinel.");
            }
            Console.WriteLine("PASS native shape rejection and normalized untrimmed influence checks.");
            string fixture = UnitySerializedSkinTests.NativeFixture();
            foreach (var input in new[] { (Name: "synthetic single-frame skin", Text: fixture, Synthetic: true) }
                .Concat(sourceAssets.Select(path => (Name: Path.GetFileName(path), Text: File.ReadAllText(path), Synthetic: false))))
            {
                object?[] parameters = { input.Text, 4300000L, true, null };
                object geometry = parser.Invoke(null, parameters)!;
                dynamic native = builder.Invoke(null, new[] { geometry, parameters[3] })!;
                if (input.Synthetic) CheckSynthetic(native);
                Type meshType = native.GetType();
                var save = meshType.GetMethod("SaveToArray")!;
                foreach (string encoding in new[] { "Plain", "LZ4" })
                {
                    byte[] bytes = (byte[])save.Invoke(native, new[] { Enum.Parse(save.GetParameters()[0].ParameterType, encoding) })!;
                    dynamic restored = Activator.CreateInstance(meshType, new MemoryStream(bytes))!;
                    Compare(native, restored);
                    if (input.Synthetic)
                    {
                        restored.BakeBlendShapes(new Dictionary<int, float> { [0] = 0.5f });
                        Check(restored.RawPositions[1].x == 1f && restored.RawPositions[1].y == 1f,
                            "Native single-frame baking changed the expected half-weight position.");
                        Check(restored.RawPositions[0].x == 0f && restored.RawPositions[0].y == 0f,
                            "Native baking changed an untouched sparse vertex.");
                    }
                }
                Console.WriteLine($"PASS {input.Name}: {native.VertexCount} vertices; {native.BoneCount} bones; {native.BlendShapeCount} shapes; Plain/LZ4 round trips preserved.");
            }
            Console.WriteLine("Offline native asset checks only; renderer loading, deformation and appearance still require an in-game test.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL serialized skin native audit: " + (error is TargetInvocationException wrapper ? wrapper.InnerException : error));
            return 1;
        }
        finally
        {
            AssemblyLoadContext.Default.Resolving -= Resolve;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private static void CheckSynthetic(dynamic native)
    {
        Check(native.VertexCount == 3 && native.BoneCount == 2 && native.SubmeshCount == 1 && native.BlendShapeCount == 1, "Synthetic native identity/count mismatch.");
        Check(native.GetBone(1).BindPose.m03 == -2f, "Bind matrix was transposed or changed.");
        Check(native.RawBoneBindings[0].boneIndex0 == 1 && native.RawBoneBindings[0].weight0 == 0.75f && native.RawBoneBindings[0].boneIndex1 == 0, "Native sorted influences changed bone identity or weight.");
        Check(native.GetBlendShape(0).FrameCount == 1 && native.GetBlendShape(0)[0].Weight == 1f && native.GetBlendShape(0)[0].RawPositions[1].y == 2f, "Native shape frame changed.");
        Check(native.GetBlendShape(0)[0].RawPositions[0].y == 0f, "Untouched sparse shape vertex changed.");
        Check(native.RawTangents[0].w == -1f, "Native tangent handedness conversion changed.");
    }

    private static void Compare(dynamic original, dynamic restored)
    {
        Check(restored.VertexCount == original.VertexCount && restored.BoneCount == original.BoneCount &&
            restored.SubmeshCount == original.SubmeshCount && restored.BlendShapeCount == original.BlendShapeCount, "Native serialization changed counts.");
        for (int vertex = 0; vertex < original.VertexCount; vertex++)
        {
            Check(restored.RawPositions[vertex].Equals(original.RawPositions[vertex]), "Position changed.");
            if (original.HasNormals) Check(restored.RawNormals[vertex].Equals(original.RawNormals[vertex]), "Normal changed.");
            if (original.HasTangents) Check(restored.RawTangents[vertex].Equals(original.RawTangents[vertex]), "Tangent changed.");
            if (original.HasColors) Check(restored.RawColors[vertex].Equals(original.RawColors[vertex]), "Color changed.");
            for (int influence = 0; influence < 4; influence++)
                Check(restored.RawBoneBindings[vertex][influence].boneIndex == original.RawBoneBindings[vertex][influence].boneIndex &&
                    restored.RawBoneBindings[vertex][influence].weight == original.RawBoneBindings[vertex][influence].weight, "Bone binding changed.");
        }
        for (int channel = 0; channel < original.UV_ChannelCount; channel++)
        {
            int dimension = original.GetUV_Dimension(channel);
            Check(dimension == restored.GetUV_Dimension(channel), "UV dimension changed.");
            for (int vertex = 0; vertex < original.VertexCount; vertex++)
                if (dimension == 2) Check(original.GetRawUVs(channel)[vertex].Equals(restored.GetRawUVs(channel)[vertex]), "UV changed.");
                else if (dimension == 3) Check(original.GetRawUVs_3D(channel)[vertex].Equals(restored.GetRawUVs_3D(channel)[vertex]), "3D UV changed.");
        }
        for (int submesh = 0; submesh < original.SubmeshCount; submesh++)
        {
            Check(original.GetSubmesh(submesh).IndicieCount == restored.GetSubmesh(submesh).IndicieCount, "Submesh size changed.");
            for (int index = 0; index < original.GetSubmesh(submesh).IndicieCount; index++)
                Check(original.GetSubmesh(submesh).RawIndicies[index] == restored.GetSubmesh(submesh).RawIndicies[index], "Submesh index changed.");
        }
        for (int bone = 0; bone < original.BoneCount; bone++)
            Check(restored.GetBone(bone).Name == original.GetBone(bone).Name && restored.GetBone(bone).BindPose.Equals(original.GetBone(bone).BindPose), "Bone identity or bind pose changed.");
        for (int shape = 0; shape < original.BlendShapeCount; shape++)
        {
            dynamic sourceShape = original.GetBlendShape(shape), restoredShape = restored.GetBlendShape(shape);
            Check(sourceShape.Name == restoredShape.Name && sourceShape.FrameCount == restoredShape.FrameCount &&
                sourceShape.HasNormals == restoredShape.HasNormals && sourceShape.HasTangents == restoredShape.HasTangents, "Shape identity or channels changed.");
            for (int frame = 0; frame < sourceShape.FrameCount; frame++)
            {
                Check(sourceShape[frame].Weight == restoredShape[frame].Weight, "Frame weight changed.");
                for (int vertex = 0; vertex < original.VertexCount; vertex++)
                {
                    Check(sourceShape[frame].RawPositions[vertex].Equals(restoredShape[frame].RawPositions[vertex]), "Shape position delta changed.");
                    if (sourceShape.HasNormals) Check(sourceShape[frame].RawNormals[vertex].Equals(restoredShape[frame].RawNormals[vertex]), "Shape normal delta changed.");
                    if (sourceShape.HasTangents) Check(sourceShape[frame].RawTangents[vertex].Equals(restoredShape[frame].RawTangents[vertex]), "Shape tangent delta changed.");
                }
            }
        }
    }
}
