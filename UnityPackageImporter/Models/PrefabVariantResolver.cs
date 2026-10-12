#nullable disable
using System;
using System.Collections.Generic;
using System.IO;

namespace UnityPackageImporter.Models;

public static class PrefabVariantResolver
{
    public static string FindSourceGuidInPrefabFile(string file)
    {
        if (string.IsNullOrEmpty(file) || !File.Exists(file)) return null;
        try
        {
            string meshGuid = null;
            foreach (var line in File.ReadLines(file))
            {
                if (line.Contains("m_SourcePrefab:") && line.Contains("guid:"))
                {
                    var parts = line.Split(new[] { "guid:" }, StringSplitOptions.None);
                    if (parts.Length > 1)
                    {
                        var tokens = parts[1].Split(new[] { ',', ' ', '}', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        if (tokens.Length > 0 && !string.IsNullOrEmpty(tokens[0]))
                            return tokens[0].Trim();
                    }
                }
                if (meshGuid == null && line.Contains("m_Mesh:") && line.Contains("guid:"))
                {
                    var parts = line.Split(new[] { "guid:" }, StringSplitOptions.None);
                    if (parts.Length > 1)
                    {
                        var tokens = parts[1].Split(new[] { ',', ' ', '}', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        if (tokens.Length > 0 && !string.IsNullOrEmpty(tokens[0]))
                            meshGuid = tokens[0].Trim();
                    }
                }
            }
            return meshGuid;
        }
        catch
        {
            return null;
        }
    }

    public static string ResolveFbxGuid(
        string sourceGuid,
        IReadOnlyDictionary<string, string> prefabs,
        IEnumerable<string> availableFbxGuids)
    {
        if (string.IsNullOrEmpty(sourceGuid)) return null;
        var fbxSet = availableFbxGuids != null
            ? new HashSet<string>(availableFbxGuids, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string currentGuid = sourceGuid;
        while (!string.IsNullOrEmpty(currentGuid) && visited.Add(currentGuid))
        {
            if (fbxSet.Contains(currentGuid))
            {
                return currentGuid;
            }

            if (prefabs != null && prefabs.TryGetValue(currentGuid, out string prefabFile) && File.Exists(prefabFile))
            {
                string nextGuid = FindSourceGuidInPrefabFile(prefabFile);
                if (!string.IsNullOrEmpty(nextGuid))
                {
                    currentGuid = nextGuid;
                    continue;
                }
            }
            break;
        }
        return null;
    }

    public static HashSet<string> GetLineageGuids(
        string sourceGuid,
        string resolvedFbxGuid,
        IReadOnlyDictionary<string, string> prefabs)
    {
        var lineage = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(resolvedFbxGuid))
            lineage.Add(resolvedFbxGuid);

        string currentGuid = sourceGuid;
        while (!string.IsNullOrEmpty(currentGuid) && lineage.Add(currentGuid))
        {
            if (prefabs != null && prefabs.TryGetValue(currentGuid, out string prefabFile) && File.Exists(prefabFile))
            {
                string nextGuid = FindSourceGuidInPrefabFile(prefabFile);
                if (!string.IsNullOrEmpty(nextGuid))
                {
                    currentGuid = nextGuid;
                    continue;
                }
            }
            break;
        }
        return lineage;
    }

    public static List<(string guid, string file)> GetAncestorPrefabs(
        string sourceGuid,
        IReadOnlyDictionary<string, string> prefabs)
    {
        var result = new List<(string guid, string file)>();
        if (string.IsNullOrEmpty(sourceGuid) || prefabs == null) return result;

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string currentGuid = sourceGuid;
        while (!string.IsNullOrEmpty(currentGuid) && visited.Add(currentGuid))
        {
            if (prefabs.TryGetValue(currentGuid, out string prefabFile) && File.Exists(prefabFile))
            {
                result.Add((currentGuid, prefabFile));
                string nextGuid = FindSourceGuidInPrefabFile(prefabFile);
                if (!string.IsNullOrEmpty(nextGuid))
                {
                    currentGuid = nextGuid;
                    continue;
                }
            }
            break;
        }

        // Return oldest ancestor first (root -> leaf)
        result.Reverse();
        return result;
    }

    public static List<string> GetAncestorPrefabFiles(
        string sourceGuid,
        IReadOnlyDictionary<string, string> prefabs)
    {
        var files = new List<string>();
        foreach (var (_, file) in GetAncestorPrefabs(sourceGuid, prefabs))
            files.Add(file);
        return files;
    }

    public static bool TryResolveTargetKey(
        long targetFileId,
        string targetGuid,
        string resolvedFbxGuid,
        IReadOnlyCollection<string> lineageGuids,
        out string resolvedGuid)
    {
        resolvedGuid = null;
        if (string.IsNullOrEmpty(resolvedFbxGuid)) return false;

        if (string.Equals(targetGuid, resolvedFbxGuid, StringComparison.OrdinalIgnoreCase))
        {
            resolvedGuid = resolvedFbxGuid;
            return true;
        }

        if (string.IsNullOrEmpty(targetGuid))
        {
            resolvedGuid = resolvedFbxGuid;
            return true;
        }

        if (lineageGuids != null)
        {
            foreach (var guid in lineageGuids)
            {
                if (string.Equals(guid, targetGuid, StringComparison.OrdinalIgnoreCase))
                {
                    resolvedGuid = resolvedFbxGuid;
                    return true;
                }
            }
        }

        return false;
    }
}
