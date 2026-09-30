using System;
using System.Collections.Generic;
using System.IO;
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
}
