using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace UnityPackageImporter.UI;

// No world RefIDs are persisted here. A contract is versioned alongside its native package.
public sealed class TemplateContract
{
    public int SchemaVersion { get; set; }
    public string TemplateVersion { get; set; }
    public string RootName { get; set; }
    public Dictionary<string, TemplateBinding> Bindings { get; set; }
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static TemplateContract Read(Stream stream)
    {
        var contract = JsonSerializer.Deserialize<TemplateContract>(stream, JsonOptions);
        if (contract?.SchemaVersion != 1 || string.IsNullOrWhiteSpace(contract.TemplateVersion) ||
            string.IsNullOrWhiteSpace(contract.RootName) || contract.Bindings == null || contract.Bindings.Count == 0)
            throw new InvalidDataException("Unsupported or incomplete UI template contract.");
        foreach (var pair in contract.Bindings)
        {
            var binding = pair.Value;
            if (string.IsNullOrWhiteSpace(pair.Key) || binding?.Path == null || binding.Index < 0 ||
                string.IsNullOrWhiteSpace(binding.Type))
                throw new InvalidDataException("Invalid UI binding: " + pair.Key);
            foreach (string segment in binding.Path)
                if (string.IsNullOrEmpty(segment) || segment is "." or ".." || segment.Contains('/') || segment.Contains('\\'))
                    throw new InvalidDataException("Invalid UI binding path: " + pair.Key);
        }
        return contract;
    }
}

public sealed class TemplateBinding
{
    public string[] Path { get; set; }
    public string Type { get; set; }
    public int Index { get; set; }
}

public sealed class TemplateManifest
{
    public int SchemaVersion { get; set; }
    public string Version { get; set; }
    public Dictionary<string, TemplateHashes> Templates { get; set; }
}

public sealed class TemplateHashes
{
    public string PackageSha256 { get; set; }
    public string ContractSha256 { get; set; }
}

public static class TemplateIntegrity
{
    public static void Verify(ReadOnlySpan<byte> data, string expected)
    {
        if (expected?.Length != 64 || !Convert.ToHexString(SHA256.HashData(data)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("UI template checksum mismatch. Re-export and prepare the template bundle.");
    }

    public static string SafeCacheFile(string directory, string hash)
    {
        if (hash?.Length != 64 || !System.Text.RegularExpressions.Regex.IsMatch(hash, "\\A[0-9a-fA-F]{64}\\z"))
            throw new InvalidDataException("Invalid template hash.");
        return Path.Combine(Path.GetFullPath(directory), hash.ToLowerInvariant() + ".ResonitePackage");
    }

    public static byte[] EnsureStandalonePackage(byte[] package, Func<byte[]> getSharedSource)
    {
        using var packageStream = new MemoryStream(package);
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read);
        var recordEntry = archive.GetEntry("R-Main.record")
            ?? throw new InvalidDataException("UI package is missing R-Main.record.");
        using var recordStream = recordEntry.Open();
        using var record = JsonDocument.Parse(recordStream);
        string assetUri = record.RootElement.TryGetProperty("assetUri", out var uriProp) ? uriProp.GetString() : null;
        if (string.IsNullOrEmpty(assetUri) || !assetUri.StartsWith("packdb:///", StringComparison.Ordinal))
            throw new InvalidDataException("UI package has an invalid primary assetUri.");
        string primaryAsset = "Assets/" + assetUri["packdb:///".Length..];
        if (archive.GetEntry(primaryAsset) == null)
            throw new InvalidDataException("UI package is missing its primary object asset.");

        var missing = new List<(string EntryName, string Hash, long Bytes)>();
        if (record.RootElement.TryGetProperty("assetManifest", out var manifestProp) &&
            manifestProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in manifestProp.EnumerateArray())
            {
                string hash = item.GetProperty("hash").GetString()
                    ?? throw new InvalidDataException("UI package assetManifest entry is missing hash.");
                long expectedBytes = item.GetProperty("bytes").GetInt64();
                string entryName = "Assets/" + hash;
                var existing = archive.GetEntry(entryName);
                if (existing != null)
                {
                    if (existing.Length != expectedBytes)
                        throw new InvalidDataException("UI package asset size mismatch: " + hash);
                }
                else
                {
                    missing.Add((entryName, hash, expectedBytes));
                }
            }
        }
        if (missing.Count == 0)
            return package;

        if (getSharedSource == null)
            throw new InvalidDataException("UI package requires shared assets, but no shared source was provided.");
        byte[] sharedBytes = getSharedSource();
        using var sharedStream = new MemoryStream(sharedBytes);
        using var sharedArchive = new ZipArchive(sharedStream, ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var combined = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in archive.Entries)
            {
                var target = combined.CreateEntry(entry.FullName, CompressionLevel.Fastest);
                using var sourceStream = entry.Open();
                using var targetStream = target.Open();
                sourceStream.CopyTo(targetStream);
            }
            foreach (var (entryName, hash, expectedBytes) in missing)
            {
                var sharedEntry = sharedArchive.GetEntry(entryName)
                    ?? throw new InvalidDataException("Shared UI asset is missing from station bundle: " + hash);
                using var sourceStream = sharedEntry.Open();
                using var buffer = new MemoryStream();
                sourceStream.CopyTo(buffer);
                byte[] data = buffer.ToArray();
                if (data.LongLength != expectedBytes)
                    throw new InvalidDataException("Shared UI asset size mismatch: " + hash);
                Verify(data, hash);
                var target = combined.CreateEntry(entryName, CompressionLevel.NoCompression);
                using var targetStream = target.Open();
                targetStream.Write(data, 0, data.Length);
            }
        }
        return output.ToArray();
    }
}
