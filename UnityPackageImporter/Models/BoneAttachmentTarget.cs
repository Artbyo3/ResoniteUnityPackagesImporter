#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityPackageImporter.Models;

/// <summary>Resolves an adapter's requested target without silently weakening an explicit path.</summary>
internal static class BoneAttachmentTarget
{
    public static T? Resolve<T>(IEnumerable<T> candidates, string? requestedPath, string? inferredName,
        Func<T, string> name, Func<T, string> relativePath) where T : class
    {
        string path = NormalizePath(requestedPath);
        var matches = candidates.Where(candidate => path.Length > 0
            ? string.Equals(NormalizePath(relativePath(candidate)), path, StringComparison.Ordinal)
            : !string.IsNullOrEmpty(inferredName) && string.Equals(name(candidate), inferredName, StringComparison.Ordinal))
            .Take(2).ToArray();
        if (matches.Length == 1) return matches[0];
        if (matches.Length == 0)
        {
            var caseInsensitive = candidates.Where(candidate => path.Length > 0
                ? string.Equals(NormalizePath(relativePath(candidate)), path, StringComparison.OrdinalIgnoreCase)
                : !string.IsNullOrEmpty(inferredName) && string.Equals(name(candidate), inferredName, StringComparison.OrdinalIgnoreCase))
                .Take(2).ToArray();
            if (caseInsensitive.Length == 1) return caseInsensitive[0];
        }
        return null;
    }

    private static string NormalizePath(string? path) =>
        string.Join("/", (path ?? "").Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries));
}
