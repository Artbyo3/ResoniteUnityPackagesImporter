#nullable enable
using System;
using System.Buffers.Binary;
using System.IO;

namespace UnityPackageImporter.Models;

internal enum ReportModelFormat { Unknown, Fbx, FbxBinary, Obj, Dae, Gltf, Glb, Blend }
internal enum ReportModelPhase { Unknown, Initialize, Decode, Metadata, Preprocess, Nodes, Bindings, Identifiers }
internal enum ReportModelProcessing { JoinIdenticalVertices, ImproveCacheLocality, PopulateArmatureData, GenerateUVCoords, FindInstances, FlipWindingOrder, LimitBoneWeights }
internal sealed record ReportModelFacts(ReportModelFormat Format, long? Bytes = null, uint? Version = null, bool UnrecognizedVersion = false);
internal sealed record ReportModelFailure(string AssetKey, ReportModelFacts Facts, string BackendVersion,
    System.Collections.Generic.IReadOnlyList<ReportModelProcessing> Processing, ReportException Error, ReportModelPhase Phase = ReportModelPhase.Unknown);

internal static class ReportModelFactsReader
{
    internal static ReportModelFacts Read(string path)
    {
        ReportModelFormat format = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".fbx" => ReportModelFormat.Fbx, ".obj" => ReportModelFormat.Obj, ".dae" => ReportModelFormat.Dae,
            ".gltf" => ReportModelFormat.Gltf, ".glb" => ReportModelFormat.Glb, ".blend" => ReportModelFormat.Blend,
            _ => ReportModelFormat.Unknown
        };
        long? bytes = null;
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            bytes = file.Length;
            Span<byte> header = stackalloc byte[27];
            int count = file.ReadAtLeast(header, header.Length, false);
            if (count == 27 && header[..23].SequenceEqual("Kaydara FBX Binary  \0\x1a\0"u8))
            {
                uint version = BinaryPrimitives.ReadUInt32LittleEndian(header[23..]);
                bool recognized = version is >= 6000 and <= 10000 && version % 100 == 0;
                return new(ReportModelFormat.FbxBinary, bytes, recognized ? version : null, !recognized);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Optional header facts remain unavailable; the original import error is retained.
            return new(format, bytes);
        }
        return new(format, bytes);
    }
}
