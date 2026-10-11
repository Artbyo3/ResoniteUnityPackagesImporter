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
        return matches.Length == 1 ? matches[0] : null;
    }

    private static string NormalizePath(string? path) =>
        string.Join("/", (path ?? "").Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries));
}
