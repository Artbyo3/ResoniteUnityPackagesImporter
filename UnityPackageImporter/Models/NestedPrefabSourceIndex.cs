#nullable enable
using System;
using System.Collections.Generic;

namespace UnityPackageImporter.Models;

// Presence in any index prevents an unavailable asset from being treated as absent.
internal sealed class NestedPrefabSourceIndex
{
    private readonly HashSet<string> known = new(StringComparer.OrdinalIgnoreCase);

    internal NestedPrefabSourceIndex(IEnumerable<string> prefabs, IEnumerable<string> assets,
        IEnumerable<string?> metadataGuids)
    {
        known.UnionWith(prefabs);
        known.UnionWith(assets);
        foreach (string? guid in metadataGuids)
            if (!string.IsNullOrWhiteSpace(guid)) known.Add(guid);
    }

    internal bool IsMissing(string? guid) => !string.IsNullOrWhiteSpace(guid) && !known.Contains(guid);
}

internal static class NestedPrefabInstanceScope
{
    // An unresolved parent retains the existing conservative, source-wide warning scope.
    internal static bool Affects<T>(T? parent, Func<T, bool> contains) where T : class =>
        parent == null || contains(parent);
}
