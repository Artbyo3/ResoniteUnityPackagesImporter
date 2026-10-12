using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UnityPackageImporter.Models;

internal static class SkinBoneMapping
{
    // Native ImportNode creates renderers before binding their bones. Prefer its
    // per-mesh Assimp-node map; it disambiguates equal names in different rigs.
    // Only older models without that map may use an unambiguous hierarchy lookup.
    internal static Dictionary<string, T> ResolveNative<T>(IEnumerable<string> meshBoneNames,
        IReadOnlyDictionary<string, T> nativeMapping, Func<IEnumerable<(string Name, T Bone)>> fallback) where T : class
    {
        var names = meshBoneNames.ToArray();
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        if (nativeMapping != null)
        {
            foreach (var name in names)
            {
                if (!nativeMapping.TryGetValue(name, out var bone) || bone == null)
                    throw new InvalidDataException("Native skin mapping has no target for '" + name + "'.");
                result[name] = bone;
            }
        }
        else if (names.Length > 0)
        {
            var bones = Resolve(names, fallback(), allowUnitySuffix: false);
            for (int i = 0; i < names.Length; i++) result[names[i]] = bones[i];
        }
        return result;
    }

    internal static void Validate<T>(int expectedCount, IEnumerable<T> bones, Func<T, bool> isValid) where T : class
    {
        var targets = bones.ToArray();
        if (targets.Length != expectedCount)
            throw new InvalidDataException($"Skin has {targets.Length} bone references; mesh requires {expectedCount}.");
        for (int i = 0; i < targets.Length; i++)
            if (targets[i] == null || !isValid(targets[i]))
                throw new InvalidDataException($"Skin bone {i} is missing, destroyed, or outside its imported hierarchy.");
    }

    /// <summary>
    /// Assimp and Unity can order bones differently. Resolve only within the
    /// renderer's authored file-ID references, rejecting missing/ambiguous names.
    /// </summary>
    internal static T[] Resolve<T>(IEnumerable<string> meshBoneNames, IEnumerable<(string Name, T Bone)> authored,
        bool allowUnitySuffix = true) where T : class
    {
        var candidates = authored.Where(item => item.Bone != null).ToArray();
        return meshBoneNames.Select(name =>
        {
            var matches = candidates.Where(item => item.Name == name).Select(item => item.Bone).Distinct().ToArray();
            // Unity may disambiguate a bone/mesh name collision with " 1".
            // Exact names always take precedence.
            if (matches.Length == 0 && allowUnitySuffix)
                matches = candidates.Where(item => item.Name == name + " 1").Select(item => item.Bone).Distinct().ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException(matches.Length == 0
                    ? "No authored skin bone matches '" + name + "'."
                    : "Several authored skin bones match '" + name + "'.");
            return matches[0];
        }).ToArray();
    }
}
