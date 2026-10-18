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
            bool insideSourcePrefab = false;
            int sourcePrefabIndent = -1;
            bool insideMesh = false;
            int meshIndent = -1;

            foreach (var rawLine in File.ReadLines(file))
            {
                string line = rawLine;
                int indent = 0;
                while (indent < line.Length && line[indent] == ' ') indent++;

                if (insideSourcePrefab)
                {
                    if (indent <= sourcePrefabIndent && !string.IsNullOrWhiteSpace(line))
                    {
                        insideSourcePrefab = false;
                    }
                    else if (line.Contains("guid:"))
                    {
                        string guid = ExtractGuidFromLine(line);
                        if (!string.IsNullOrEmpty(guid)) return guid;
                    }
                }

                if (insideMesh && meshGuid == null)
                {
                    if (indent <= meshIndent && !string.IsNullOrWhiteSpace(line))
                    {
                        insideMesh = false;
                    }
                    else if (line.Contains("guid:"))
                    {
                        meshGuid = ExtractGuidFromLine(line);
                    }
                }

                if (line.Contains("m_SourcePrefab:"))
                {
                    if (line.Contains("guid:"))
                    {
                        string guid = ExtractGuidFromLine(line);
                        if (!string.IsNullOrEmpty(guid)) return guid;
                    }
                    else
                    {
                        insideSourcePrefab = true;
                        sourcePrefabIndent = indent;
                    }
                }

                if (meshGuid == null && line.Contains("m_Mesh:"))
                {
                    if (line.Contains("guid:"))
                    {
                        meshGuid = ExtractGuidFromLine(line);
                    }
                    else
                    {
                        insideMesh = true;
                        meshIndent = indent;
                    }
                }
            }
            return meshGuid;
        }
        catch (Exception ex)
        {
#if !REGRESSION_TESTS
            UnityPackageImporter.Warn($"Failed to scan prefab '{file}' for source GUID: {ex.Message}");
#endif
            return null;
        }
    }

    private static string ExtractGuidFromLine(string line)
    {
        var parts = line.Split(new[] { "guid:" }, StringSplitOptions.None);
        if (parts.Length > 1)
        {
            var tokens = parts[1].Split(new[] { ',', ' ', '}', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 0 && !string.IsNullOrEmpty(tokens[0]))
                return tokens[0].Trim();
        }
        return null;
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

    public static long ComputeDerivedFileId(long piId, long targetId)
    {
        return (piId ^ targetId) & 0x7FFFFFFFFFFFFFFFL;
    }

    public static (long cand1, long cand2) ReverseDerivedFileId(long piId, long derivedId)
    {
        long cand1 = piId ^ derivedId;
        long cand2 = cand1 ^ unchecked((long)0x8000000000000000UL);
        return (cand1, cand2);
    }

    public static List<long> FindPrefabInstanceIds(string file)
    {
        var list = new List<long>();
        if (string.IsNullOrEmpty(file) || !File.Exists(file)) return list;
        try
        {
            foreach (var line in File.ReadLines(file))
            {
                if (line.StartsWith("--- !u!1001 &", StringComparison.Ordinal))
                {
                    var rest = line.Substring("--- !u!1001 &".Length).Trim();
                    var tokens = rest.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length > 0 && long.TryParse(tokens[0], out long id) && id != 0)
                    {
                        if (!list.Contains(id))
                            list.Add(id);
                    }
                }
            }
        }
        catch (Exception ex)
        {
#if !REGRESSION_TESTS
            UnityPackageImporter.Warn($"Failed to read prefab instance IDs from '{file}': {ex.Message}");
#endif
        }
        return list;
    }

    public static bool TryResolveDerivedTarget<T>(
        long targetFileId,
        IEnumerable<long> ancestorPiIds,
        IReadOnlyDictionary<long, T> knownObjects,
        out T resolvedObject)
        where T : class
    {
        if (knownObjects == null)
        {
            resolvedObject = default;
            return false;
        }
        return TryResolveDerivedTarget(targetFileId, ancestorPiIds,
            candidateId => FindKnownTarget(knownObjects, candidateId), out resolvedObject);
    }

    public static bool TryResolveDerivedTarget<T>(
        long targetFileId,
        IEnumerable<long> ancestorPiIds,
        Func<long, IEnumerable<T>> findCandidates,
        out T resolvedObject)
        where T : class
    {
        return TryResolveDerivedTarget(targetFileId, ancestorPiIds, findCandidates, out resolvedObject, out _);
    }

    public static bool TryResolveDerivedTarget<T>(
        long targetFileId,
        IEnumerable<long> ancestorPiIds,
        Func<long, IEnumerable<T>> findCandidates,
        out T resolvedObject,
        out bool ambiguous)
        where T : class
    {
        resolvedObject = default;
        ambiguous = false;
        if (ancestorPiIds == null || findCandidates == null) return false;
        T candidate = default;
        bool found = false;
        foreach (long piId in ancestorPiIds)
        {
            if (piId == 0) continue;
            var (cand1, cand2) = ReverseDerivedFileId(piId, targetFileId);
            foreach (long candidateId in new[] { cand1, cand2 })
            {
                var matches = findCandidates(candidateId);
                if (matches == null) continue;
                foreach (var match in matches)
                {
                    // A null entry records an ID that already collided while the
                    // ancestor maps were built. It must not look like a missing ID.
                    if (match is null)
                    {
                        ambiguous = true;
                        return false;
                    }
                    if (!found)
                    {
                        candidate = match;
                        found = true;
                    }
                    else if (!ReferenceEquals(candidate, match))
                    {
                        ambiguous = true;
                        return false;
                    }
                }
            }
        }
        if (found)
        {
            resolvedObject = candidate;
            return true;
        }
        return false;
    }

    private static IEnumerable<T> FindKnownTarget<T>(IReadOnlyDictionary<long, T> knownObjects, long candidateId)
    {
        if (knownObjects.TryGetValue(candidateId, out var target))
            yield return target;
    }

    public static void RegisterUniqueTarget<TKey, T>(IDictionary<TKey, T> targets, TKey key, T target)
        where T : class
    {
        if (!targets.TryGetValue(key, out var existing))
            targets.Add(key, target);
        else if (!ReferenceEquals(existing, target))
            targets[key] = null;
    }

    public static bool TryResolveParentTransform(
        ulong parentTransformId,
        Func<ulong, (bool exists, ulong gameObjectId)> getTransformInfo,
        Func<ulong, (bool exists, bool hasValidSlot)> getGameObjectInfo,
        out string failureReason)
    {
        return TryResolveParentTransform(
            parentTransformId,
            id =>
            {
                var (exists, gId) = getTransformInfo(id);
                return (exists, gId, false);
            },
            getGameObjectInfo,
            out failureReason);
    }

    public static bool TryResolveParentTransform(
        ulong parentTransformId,
        Func<ulong, (bool exists, ulong gameObjectId, bool hasDirectSlot)> getTransformInfo,
        Func<ulong, (bool exists, bool hasValidSlot)> getGameObjectInfo,
        out string failureReason)
    {
        failureReason = null;
        if (parentTransformId == 0)
        {
            // Explicitly no parent -> valid root-level placement
            return true;
        }

        // Non-zero parent reference exists: it MUST resolve successfully.
        var (transformExists, gameObjectId, hasDirectSlot) = getTransformInfo(parentTransformId);
        if (!transformExists)
        {
            failureReason = $"Specifies parent transform fileID {parentTransformId}, but parent transform does not exist. Cannot reconstruct object hierarchy.";
            return false;
        }

        if (hasDirectSlot)
        {
            return true;
        }

        var (gameObjExists, hasValidSlot) = getGameObjectInfo(gameObjectId);
        if (!gameObjExists)
        {
            failureReason = $"Parent transform {parentTransformId} references GameObject {gameObjectId}, but GameObject does not exist. Cannot reconstruct object hierarchy.";
            return false;
        }

        if (!hasValidSlot)
        {
            failureReason = $"Parent GameObject {gameObjectId} has null slot. Cannot reconstruct object hierarchy.";
            return false;
        }

        return true;
    }

    public static bool EvaluateSkinnedMeshReconstruction(
        int authoredSkinCount,
        int successfulSkinCount,
        string prefabName,
        out string failureReason)
    {
        if (authoredSkinCount > 0 && successfulSkinCount == 0)
        {
            failureReason = $"Critical reconstruction failure: None of the {authoredSkinCount} skinned mesh renderer(s) could be reconstructed for prefab {prefabName}.";
            return false;
        }
        failureReason = null;
        return true;
    }

    public static bool EvaluatePartialCompatibility(int incompleteComponentCount, bool hasVrcFury)
    {
        return incompleteComponentCount > 0 || hasVrcFury;
    }

    public static bool EvaluatePartialCompatibility(int incompleteComponentCount, int unhandledVrcFuryFeatures, int planErrors = 0)
    {
        return incompleteComponentCount > 0 || unhandledVrcFuryFeatures > 0 || planErrors > 0;
    }

    public const long RootGameObjectFileId = 919132149155446097L;
    public const long RootTransformFileId = -8679921383154817045L;

    public static bool IsRootModelFileId(long fileId)
    {
        return fileId == RootGameObjectFileId || fileId == RootTransformFileId;
    }

}
