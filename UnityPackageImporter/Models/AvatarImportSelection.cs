#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityPackageImporter.Models;

// Source-independent ownership rules, kept free of engine types for regression tests.
internal static class AvatarImportSelection
{
    internal static IReadOnlyList<T> IndependentRoots<T>(IEnumerable<T> candidates,
        Func<T, T, bool> contains) where T : class
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(contains);
        var roots = candidates.Distinct().ToArray();
        for (int i = 0; i < roots.Length; i++)
            for (int j = i + 1; j < roots.Length; j++)
                if (contains(roots[i], roots[j]) || contains(roots[j], roots[i]))
                    throw new InvalidOperationException("Nested avatar descriptors have overlapping ownership. Separate their roots before importing.");
        return roots;
    }

    internal static void ValidateSkinOwnership<T>(IEnumerable<T> roots,
        IEnumerable<(T Owner, IEnumerable<T> Bones)> skins, Func<T, T, bool> contains) where T : class
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(skins);
        ArgumentNullException.ThrowIfNull(contains);
        var consumers = skins.ToArray();
        foreach (var root in roots)
            foreach (var skin in consumers)
                foreach (var bone in skin.Bones)
                    if (bone != null && contains(root, skin.Owner) != contains(root, bone))
                        throw new InvalidOperationException("A scene skin crosses an avatar descriptor boundary and cannot be staged independently.");
    }

    internal static AvatarPackageManifest Scope(AvatarPackageManifest source, Func<long, bool> owns)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(owns);
        return new()
        {
            SourcePath = source.SourcePath,
            Avatars = source.Avatars.Where(a => Local(a.SourcePath, source.SourcePath) && owns(a.GameObjectFileId)).ToArray(),
            ModularAvatarComponents = source.ModularAvatarComponents.Where(c => Local(c.SourcePath, source.SourcePath) && owns(c.GameObjectFileId)).ToArray(),
            VrcFuryComponents = source.VrcFuryComponents.Where(c => Local(c.SourcePath, source.SourcePath) && owns(c.GameObjectFileId)).ToArray()
        };
    }

    private static bool Local(string? path, string? source) => string.Equals(path, source, StringComparison.OrdinalIgnoreCase);

    // Recognized but unresolved avatars must also remain staged, not leak into the world.
    internal static bool AutoCommitScenes(bool hasAvatarDefinitions, bool hasAvatarOrOutfitItems) =>
        !hasAvatarDefinitions && !hasAvatarOrOutfitItems;
}
