#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using UnityPackageImporter.FrooxEngineRepresentation;

namespace UnityPackageImporter.Models;

// Shared by stripped Transform, GameObject and SkinnedMeshRenderer references.
// Keep this boundary engine-independent so the actual lookup can be regression tested.
internal static class PrefabObjectLookup
{
    internal static bool TryFind<T>(SourceObj source, IDictionary<SourceObj, T> hashes,
        IDictionary<SourceObj, T> ancestorTargets, string prefabGuid, string resolvedFbxGuid,
        ISet<string> lineageGuids, out T result, out bool ambiguous) where T : class
    {
        result = null;
        ambiguous = false;
        if (source == null || hashes == null) return false;

        // Preserve ordinary exact-reference precedence and the existing GUID checks.
        if (hashes.TryGetValue(source, out result)) return true;
        resolvedFbxGuid ??= prefabGuid;
        bool InLineage(string guid) => string.IsNullOrEmpty(guid) ||
            string.Equals(guid, prefabGuid, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(guid, resolvedFbxGuid, StringComparison.OrdinalIgnoreCase) ||
            lineageGuids?.Contains(guid) == true;
        if (!InLineage(source.guid)) return false;

        bool FindIn(IDictionary<SourceObj, T> targets, out T found, out bool conflict)
        {
            found = null;
            conflict = false;
            if (targets == null) return false;
            if (targets.TryGetValue(source, out found) ||
                (!string.IsNullOrEmpty(resolvedFbxGuid) &&
                 targets.TryGetValue(new SourceObj(source.fileID, resolvedFbxGuid, source.type), out found)) ||
                targets.TryGetValue(new SourceObj(source.fileID, "", source.type), out found))
            {
                // RegisterUniqueTarget retains null when derived aliases collide.
                conflict = found == null;
                return !conflict;
            }

            foreach (var pair in targets)
            {
                if (pair.Key.fileID != source.fileID || !InLineage(pair.Key.guid) ||
                    (source.type != 0 && pair.Key.type != 0 && source.type != pair.Key.type)) continue;
                if (pair.Value == null || (found != null && !ReferenceEquals(found, pair.Value)))
                {
                    found = null;
                    conflict = true;
                    return false;
                }
                found = pair.Value;
            }
            return found != null;
        }

        // Alias IDs are built by override replay with PrefabVariantResolver's shared
        // derived-ID helper. Do not discard them before stripped references resolve.
        if (FindIn(hashes, out result, out ambiguous) || ambiguous) return !ambiguous;
        return FindIn(ancestorTargets, out result, out ambiguous);
    }

    internal static InvalidDataException UnresolvedTransform(ulong id, SourceObj source) =>
        new($"Transform {id} could not resolve prefab target {source.fileID} in {source.guid}. Hierarchy integrity cannot be guaranteed.");
}
