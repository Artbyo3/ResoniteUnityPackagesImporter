#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityPackageImporter.Models;

internal static class AvatarConversionScope
{
    internal static T? Unique<T>(IEnumerable<T> candidates) where T : class
    {
        var matches = candidates.Distinct().Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    internal static T? RootFor<T>(T? source, T fallback, IReadOnlyList<T> avatarRoots, Func<T, T, bool> contains) where T : class =>
        source == null ? null : avatarRoots.Count == 0 ? fallback : Unique(avatarRoots.Where(root => contains(source, root)));
}
