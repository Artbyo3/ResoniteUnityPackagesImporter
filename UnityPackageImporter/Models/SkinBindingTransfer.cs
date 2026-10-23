#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityPackageImporter.Models;

internal static class SkinBindingTransfer
{
    internal static bool WithinTolerance(float error, float tolerance) =>
        float.IsFinite(error) && float.IsFinite(tolerance) && tolerance >= 0 && error <= tolerance;

    internal static IReadOnlyList<(T Source, T Target)> Plan<T>(IEnumerable<(T Source, T Target)> mappings,
        Func<T, T, bool> contains) where T : class
    {
        var targets = new Dictionary<T, T>();
        foreach (var (source, target) in mappings)
        {
            if (ReferenceEquals(source, target) || contains(target, source))
                throw new InvalidOperationException("A skin binding cannot follow itself or its descendant.");
            if (targets.TryGetValue(source, out var previous) && !ReferenceEquals(previous, target))
                throw new InvalidOperationException("A source bone has conflicting attachment targets.");
            targets[source] = target;
        }
        foreach (var source in targets.Keys)
        {
            var visited = new HashSet<T> { source };
            var current = targets[source];
            while (targets.TryGetValue(current, out var next))
            {
                if (!visited.Add(current))
                    throw new InvalidOperationException("Skin binding dependencies contain a cycle.");
                current = next;
            }
        }
        return targets.Select(pair => (pair.Key, pair.Value)).ToArray();
    }

    internal static void Apply<T>(IReadOnlyList<(T Source, T Target)> plan, Action<T, T> preserveWorldParent) where T : class
    {
        foreach (var (source, target) in plan) preserveWorldParent(source, target);
    }
}
