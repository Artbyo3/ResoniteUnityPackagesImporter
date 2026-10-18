using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

#nullable enable

namespace UnityPackageImporter.Models;

internal sealed class CompanionMergeResult
{
    public List<string> AddedGuids { get; } = new();
    public List<string> ConflictedGuids { get; } = new();
    public List<string> IgnoredEquivalentGuids { get; } = new();
}

internal sealed class UnityPackageAssetIndex
{
    public Dictionary<string, string> Assets { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Prefabs { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Scenes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> OtherFiles { get; } = new();
    public List<string> Conflicts { get; } = new();
    public HashSet<string> ConflictedGuids { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static UnityPackageAssetIndex Build(IEnumerable<string> files)
    {
        var result = new UnityPackageAssetIndex();
        foreach (string file in files ?? Array.Empty<string>())
        {
            string extension = Path.GetExtension(file).ToLowerInvariant();
            if (extension != ".prefab" && extension != ".meta" && extension != ".unity")
                result.OtherFiles.Add(file);
            if (extension != ".meta") continue;

            string assetPath = file[..^extension.Length];
            string? guid = ReadGuid(file);
            if (string.IsNullOrWhiteSpace(guid))
                throw new InvalidDataException("Unity metadata has no GUID: " + file);

            guid = NormalizeGuid(guid);

            if (result.ConflictedGuids.Contains(guid))
            {
                result.Conflicts.Add(
                    $"GUID '{guid}' conflict includes '{assetPath}'. Asset was omitted due to ambiguity.");
                continue;
            }

            if (result.Assets.TryGetValue(guid, out string? existingAsset))
            {
                if (Path.GetFullPath(existingAsset).Equals(
                        Path.GetFullPath(assetPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (AreAssetsEquivalent(existingAsset, assetPath))
                {
                    // Equivalent duplicates deduplicate safely.
                    // Pick the canonical path deterministically so reversing input order yields identical selection.
                    if (string.Compare(assetPath, existingAsset, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        result.Assets[guid] = assetPath;
                        if (result.Prefabs.ContainsKey(guid)) result.Prefabs[guid] = assetPath;
                        if (result.Scenes.ContainsKey(guid)) result.Scenes[guid] = assetPath;
                    }
                    continue;
                }

                // Conflicting assets!
                result.ConflictedGuids.Add(guid);
                result.Assets.Remove(guid);
                result.Prefabs.Remove(guid);
                result.Scenes.Remove(guid);
                result.Conflicts.Add(
                    $"GUID '{guid}' has conflicting definitions between '{existingAsset}' and '{assetPath}'. Neither asset was used due to ambiguity.");
                continue;
            }

            result.Assets.Add(guid, assetPath);
            string assetExtension = Path.GetExtension(assetPath).ToLowerInvariant();
            if (assetExtension == ".prefab") result.Prefabs.Add(guid, assetPath);
            else if (assetExtension == ".unity") result.Scenes.Add(guid, assetPath);
        }
        return result;
    }

    public static CompanionMergeResult MergeCompanion(
        IDictionary<string, string> currentAssets,
        HashSet<string> conflictedGuids,
        IReadOnlyDictionary<string, string>? companionAssets,
        IReadOnlyCollection<string>? companionConflictedGuids = null,
        Func<string, bool>? isAssetAssignedOrCached = null,
        Action<string>? onConflict = null)
    {
        var result = new CompanionMergeResult();
        if (companionAssets == null) return result;

        // First register any internal conflicts reported by the companion
        if (companionConflictedGuids != null)
        {
            foreach (var rawGuid in companionConflictedGuids)
            {
                string guid = NormalizeGuid(rawGuid);
                if (conflictedGuids.Add(guid))
                {
                    result.ConflictedGuids.Add(guid);
                    onConflict?.Invoke($"Companion package contains internal conflict for GUID '{guid}'.");
                }
                if (currentAssets.TryGetValue(guid, out var existingPath))
                {
                    if (isAssetAssignedOrCached != null && isAssetAssignedOrCached(guid))
                    {
                        onConflict?.Invoke($"GUID '{guid}' is conflicted in companion; preserving existing valid assignment '{existingPath}'.");
                    }
                    else
                    {
                        currentAssets.Remove(guid);
                    }
                }
            }
        }

        foreach (var pair in companionAssets)
        {
            string guid = NormalizeGuid(pair.Key);
            string companionPath = pair.Value;

            if (conflictedGuids.Contains(guid))
            {
                onConflict?.Invoke($"Companion asset '{companionPath}' matches conflicted GUID '{guid}'. Preserving conflict status due to ambiguity.");
                continue;
            }

            if (currentAssets.TryGetValue(guid, out string? existingPath))
            {
                if (Path.GetFullPath(existingPath).Equals(Path.GetFullPath(companionPath), StringComparison.OrdinalIgnoreCase) ||
                    AreAssetsEquivalent(existingPath, companionPath))
                {
                    result.IgnoredEquivalentGuids.Add(guid);
                    continue;
                }

                // Conflicting asset!
                conflictedGuids.Add(guid);
                result.ConflictedGuids.Add(guid);
                string message = $"Companion asset '{companionPath}' conflicts with existing asset '{existingPath}' for GUID '{guid}'.";
                if (isAssetAssignedOrCached != null && isAssetAssignedOrCached(guid))
                {
                    onConflict?.Invoke(message + " The existing assignment was preserved and companion asset was ignored due to ambiguity.");
                }
                else
                {
                    currentAssets.Remove(guid);
                    onConflict?.Invoke(message + " Neither asset was used due to ambiguity.");
                }
                continue;
            }

            // Legitimate missing / new dependency
            currentAssets.Add(guid, companionPath);
            result.AddedGuids.Add(guid);
        }

        return result;
    }

    public static bool AreAssetsEquivalent(string assetPathA, string assetPathB)
    {
        if (string.Equals(Path.GetFullPath(assetPathA), Path.GetFullPath(assetPathB), StringComparison.OrdinalIgnoreCase))
            return true;

        // Check metadata first
        string metaA = assetPathA.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ? assetPathA : assetPathA + ".meta";
        string metaB = assetPathB.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ? assetPathB : assetPathB + ".meta";
        bool metaExistsA = File.Exists(metaA);
        bool metaExistsB = File.Exists(metaB);
        if (metaExistsA != metaExistsB) return false;
        if (metaExistsA && !AreMetadataEquivalent(metaA, metaB)) return false;

        // Check payloads
        bool isDirA = Directory.Exists(assetPathA);
        bool isDirB = Directory.Exists(assetPathB);
        if (isDirA != isDirB) return false;
        if (isDirA) return true;

        bool existsA = File.Exists(assetPathA);
        bool existsB = File.Exists(assetPathB);
        if (existsA != existsB) return false;
        if (!existsA) return true; // Neither file exists on disk, metadata matched

        var fileInfoA = new FileInfo(assetPathA);
        var fileInfoB = new FileInfo(assetPathB);
        if (fileInfoA.Length != fileInfoB.Length) return false;
        if (fileInfoA.Length == 0) return true;

        using var streamA = File.OpenRead(assetPathA);
        using var streamB = File.OpenRead(assetPathB);
        byte[] bufferA = new byte[64 * 1024];
        byte[] bufferB = new byte[64 * 1024];
        while (true)
        {
            int readA = streamA.Read(bufferA, 0, bufferA.Length);
            int readB = streamB.Read(bufferB, 0, bufferB.Length);
            if (readA != readB) return false;
            if (readA == 0) break;
            if (!bufferA.AsSpan(0, readA).SequenceEqual(bufferB.AsSpan(0, readB)))
                return false;
        }

        return true;
    }

    public static bool AreMetadataEquivalent(string metaPathA, string metaPathB)
    {
        if (string.Equals(Path.GetFullPath(metaPathA), Path.GetFullPath(metaPathB), StringComparison.OrdinalIgnoreCase))
            return true;

        if (!File.Exists(metaPathA) || !File.Exists(metaPathB))
            return false;

        try
        {
            var yamlA = new YamlStream();
            var yamlB = new YamlStream();
            using var readerA = File.OpenText(metaPathA);
            using var readerB = File.OpenText(metaPathB);
            yamlA.Load(readerA);
            yamlB.Load(readerB);
            return yamlA.Documents.Count == 1 && yamlB.Documents.Count == 1 &&
                yamlA.Documents[0].RootNode is YamlMappingNode rootA &&
                yamlB.Documents[0].RootNode is YamlMappingNode rootB &&
                MetadataNodesEqual(rootA, rootB, 0);
        }
        catch (YamlException)
        {
            // Invalid metadata cannot establish that two assets are equivalent.
            return false;
        }
    }

    private static bool MetadataNodesEqual(YamlNode a, YamlNode b, int depth)
    {
        // Metadata does not need recursive aliases or arbitrary nesting. Fail
        // conservatively instead of recursing indefinitely on cyclic input.
        if (depth > 64 || !a.Tag.Equals(b.Tag)) return false;
        if (a is YamlScalarNode scalarA && b is YamlScalarNode scalarB)
            return scalarA.Style == scalarB.Style &&
                string.Equals(scalarA.Value, scalarB.Value, StringComparison.Ordinal);
        if (a is YamlSequenceNode sequenceA && b is YamlSequenceNode sequenceB)
        {
            return sequenceA.Children.Count == sequenceB.Children.Count &&
                sequenceA.Children.Zip(sequenceB.Children)
                    .All(pair => MetadataNodesEqual(pair.First, pair.Second, depth + 1));
        }
        if (a is YamlMappingNode mapA && b is YamlMappingNode mapB)
        {
            bool Ignore(KeyValuePair<YamlNode, YamlNode> pair) => depth == 0 &&
                pair.Key is YamlScalarNode { Value: "timeCreated" } && pair.Value is YamlScalarNode;
            var entriesA = mapA.Children.Where(pair => !Ignore(pair)).ToArray();
            var entriesB = mapB.Children.Where(pair => !Ignore(pair)).ToArray();
            if (entriesA.Length != entriesB.Length) return false;
            foreach (var pair in entriesA)
            {
                if (pair.Key is not YamlScalarNode || !mapB.Children.TryGetValue(pair.Key, out var other) ||
                    !MetadataNodesEqual(pair.Key, mapB.Children.Keys.First(key => key.Equals(pair.Key)), depth + 1))
                    return false;
                if (depth == 0 && pair.Key is YamlScalarNode { Value: "guid" } &&
                    pair.Value is YamlScalarNode guidA && other is YamlScalarNode guidB)
                {
                    if (!guidA.Tag.Equals(guidB.Tag) || NormalizeGuid(guidA.Value) != NormalizeGuid(guidB.Value)) return false;
                }
                else if (!MetadataNodesEqual(pair.Value, other, depth + 1)) return false;
            }
            return true;
        }
        return false;
    }

    public static string NormalizeGuid(string? value) =>
        (value ?? "").Trim().ToLowerInvariant();

    private static string? ReadGuid(string metadataPath)
    {
        foreach (string line in File.ReadLines(metadataPath))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("guid:", StringComparison.OrdinalIgnoreCase)) continue;
            return NormalizeGuid(trimmed[5..].Trim());
        }
        return null;
    }
}
