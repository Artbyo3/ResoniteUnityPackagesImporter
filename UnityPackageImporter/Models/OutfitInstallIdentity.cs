using System;
using System.IO;
using System.Security.Cryptography;

namespace UnityPackageImporter.Models;

public enum OutfitInstallMatch
{
    None,
    Current,
    UpdateAvailable
}

/// <summary>
/// Stable source identity for an installed outfit. The Unity prefab GUID
/// identifies the outfit across package exports; the content hash identifies
/// the exact revision without storing a machine-specific source path.
/// </summary>
public sealed class OutfitInstallIdentity
{
    public const int CurrentSchemaVersion = 1;

    public string PrefabGuid { get; init; } = "";
    public string ContentHash { get; init; } = "";

    public static OutfitInstallIdentity FromPrefab(string prefabGuid, string prefabFile)
    {
        if (string.IsNullOrWhiteSpace(prefabGuid))
            throw new ArgumentException("A prefab GUID is required.", nameof(prefabGuid));
        if (string.IsNullOrWhiteSpace(prefabFile) || !File.Exists(prefabFile))
            throw new FileNotFoundException("The source prefab is unavailable.", prefabFile);

        using var stream = File.OpenRead(prefabFile);
        return new OutfitInstallIdentity
        {
            PrefabGuid = NormalizeGuid(prefabGuid),
            ContentHash = Convert.ToHexString(SHA256.HashData(stream))
        };
    }

    public OutfitInstallMatch Compare(string installedPrefabGuid, string installedContentHash)
    {
        if (!NormalizeGuid(installedPrefabGuid).Equals(PrefabGuid, StringComparison.Ordinal))
            return OutfitInstallMatch.None;

        return NormalizeHash(installedContentHash).Equals(ContentHash, StringComparison.Ordinal)
            ? OutfitInstallMatch.Current
            : OutfitInstallMatch.UpdateAvailable;
    }

    public static string NormalizeGuid(string value) =>
        (value ?? "").Trim().Replace("-", "").ToLowerInvariant();

    public static string NormalizeHash(string value) =>
        (value ?? "").Trim().ToUpperInvariant();
}
