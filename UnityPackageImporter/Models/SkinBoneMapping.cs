using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UnityPackageImporter.Models;

internal static class SkinBoneMapping
{
    /// <summary>
    /// Assimp and Unity can order bones differently. Resolve only within the
    /// renderer's authored file-ID references, rejecting missing/ambiguous names.
    /// </summary>
    internal static T[] Resolve<T>(IEnumerable<string> meshBoneNames, IEnumerable<(string Name, T Bone)> authored) where T : class
    {
        var candidates = authored.Where(item => item.Bone != null).ToArray();
        return meshBoneNames.Select(name =>
        {
            var matches = candidates.Where(item => item.Name == name).Select(item => item.Bone).Distinct().ToArray();
            // Unity may disambiguate a bone/mesh name collision with " 1".
            // Exact names always take precedence.
            if (matches.Length == 0)
                matches = candidates.Where(item => item.Name == name + " 1").Select(item => item.Bone).Distinct().ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException(matches.Length == 0
                    ? "No authored skin bone matches '" + name + "'."
                    : "Several authored skin bones match '" + name + "'.");
            return matches[0];
        }).ToArray();
    }
}
