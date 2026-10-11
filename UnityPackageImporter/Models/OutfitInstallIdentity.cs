#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

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
/// the revision of the prefab and its reachable imported dependencies without
/// storing machine-specific source paths. Unity editor execution is not implied.
/// </summary>
public sealed class OutfitInstallIdentity
{
    public const int CurrentSchemaVersion = 1;

    public string PrefabGuid { get; init; } = "";
    public string ContentHash { get; init; } = "";

    private static readonly Regex GuidReference = new(
        @"\bguid:\s*([0-9a-fA-F]{32})\b", RegexOptions.CultureInvariant);

    public static OutfitInstallIdentity FromPrefab(string prefabGuid, string prefabFile,
        IReadOnlyDictionary<string, string>? assets = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prefabGuid))
            throw new ArgumentException("A prefab GUID is required.", nameof(prefabGuid));
        if (string.IsNullOrWhiteSpace(prefabFile) || !File.Exists(prefabFile))
            throw new FileNotFoundException("The source prefab is unavailable.", prefabFile);

        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var asset in assets ?? new Dictionary<string, string>())
            paths[NormalizeGuid(asset.Key)] = asset.Value;
        string rootGuid = NormalizeGuid(prefabGuid);
        paths[rootGuid] = prefabFile;
        var pending = new Queue<string>();
        var revisions = new SortedDictionary<string, string>(StringComparer.Ordinal);
        pending.Enqueue(rootGuid);
        while (pending.TryDequeue(out var guid))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (revisions.ContainsKey(guid)) continue;
            if (!paths.TryGetValue(guid, out var path) || !File.Exists(path))
            {
                revisions[guid] = "missing";
                continue;
            }

            string meta = path + ".meta";
            revisions[guid] = HashFile(path, cancellationToken) + ":" +
                (File.Exists(meta) ? HashFile(meta, cancellationToken) : "no-meta");
            ReadReferences(path);
            if (File.Exists(meta)) ReadReferences(meta);
        }
        string manifest = "outfit-dependencies-v2\n" + string.Join("\n",
            revisions.Select(entry => entry.Key + ":" + entry.Value));
        return new OutfitInstallIdentity
        {
            PrefabGuid = rootGuid,
            ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifest)))
        };

        void ReadReferences(string path)
        {
            // Only serialized Unity YAML can contain asset GUID references.
            // This avoids scanning binary models/textures or shader source as text.
            using var reader = new StreamReader(path, Encoding.UTF8, true);
            char[] header = new char[5];
            if (reader.ReadBlock(header, 0, header.Length) != header.Length) return;
            string prefix = new(header);
            if (prefix != "%YAML" && !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) return;
            reader.DiscardBufferedData();
            reader.BaseStream.Position = 0;
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (Match match in GuidReference.Matches(line))
                {
                    string dependency = NormalizeGuid(match.Groups[1].Value);
                    if (dependency.All(character => character == '0')) continue;
                    if (!revisions.ContainsKey(dependency)) pending.Enqueue(dependency);
                }
            }
        }
    }

    private static string HashFile(string path, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[64 * 1024];
        int count;
        while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, count);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public OutfitInstallMatch Compare(string installedPrefabGuid, string installedContentHash)
    {
        if (!NormalizeGuid(installedPrefabGuid).Equals(NormalizeGuid(PrefabGuid), StringComparison.Ordinal))
            return OutfitInstallMatch.None;

        // Legacy records hashed only the prefab. Their dependency revision is
        // unknown, so they intentionally remain UpdateAvailable instead of being
        // silently migrated to Current with unverified textures or mesh data.
        return NormalizeHash(installedContentHash).Equals(NormalizeHash(ContentHash), StringComparison.Ordinal)
            ? OutfitInstallMatch.Current
            : OutfitInstallMatch.UpdateAvailable;
    }

    public static string NormalizeGuid(string? value) =>
        (value ?? "").Trim().Replace("-", "").ToLowerInvariant();

    public static string NormalizeHash(string? value) =>
        (value ?? "").Trim().ToUpperInvariant();
}
