using System;
using System.Collections.Generic;

namespace UnityPackageImporter.Models;

internal static class BlendShapeDefaults
{
    public static void ApplyNamed(IReadOnlyList<float> weights, IReadOnlyList<string> sourceNames,
        Func<string, int> findIndex, Action<int, float> setWeight, Action<string> warn)
    {
        if (weights == null || weights.Count == 0) return;
        if (sourceNames == null || weights.Count > sourceNames.Count)
        {
            warn("Cannot safely map prefab blendshape defaults: original FBX shape names are unavailable or incomplete.");
            return;
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in sourceNames)
            if (string.IsNullOrEmpty(name) || !seen.Add(name))
            {
                warn("Cannot safely map prefab blendshape defaults: original FBX shape names are empty or duplicated.");
                return;
            }
        for (int i = 0; i < weights.Count; i++)
        {
            if (!float.IsFinite(weights[i])) { warn($"Non-finite blendshape weight at index {i} was skipped."); continue; }
            int targetIndex = findIndex(sourceNames[i]);
            // The engine strips empty shapes. Their weights have no geometric effect.
            if (targetIndex >= 0) setWeight(targetIndex, weights[i] / 100f);
        }
    }
    // Unity stores percentage weights; Resonite uses unit weights. Do not clamp
    // intentional negative weights or values beyond the normal range.
    public static void Apply(IReadOnlyList<float> weights, int availableCount,
        Action<int, float> setWeight, Action<string> warn)
    {
        if (weights == null || weights.Count == 0) return;
        if (weights.Count > availableCount)
            warn($"Prefab has {weights.Count} blendshape weights, but the mesh has {availableCount}; extra weights were skipped.");
        for (int i = 0; i < Math.Min(weights.Count, availableCount); i++)
        {
            if (!float.IsFinite(weights[i]))
            {
                warn($"Non-finite blendshape weight at index {i} was skipped.");
                continue;
            }
            setWeight(i, weights[i] / 100f);
        }
    }
}
