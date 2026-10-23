#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityPackageImporter.Models;

internal static class HierarchyIdentity
{
    internal static TPoint PointInAncestor<T, TPoint>(T root, T target, TPoint point,
        Func<T, T?> parent, Func<T, TPoint, TPoint> toParent) where T : class
    {
        var visited = new HashSet<T>();
        var current = target;
        while (!ReferenceEquals(current, root))
        {
            if (!visited.Add(current)) throw new InvalidOperationException("The preview anchor hierarchy contains a cycle.");
            var owner = parent(current) ?? throw new InvalidOperationException("The preview anchor is outside the staged output.");
            point = toParent(current, point);
            current = owner;
        }
        return point;
    }

    internal static IReadOnlyList<int> Path<T>(T root, T target, Func<T, T?> parent, Func<T, IEnumerable<T>> children) where T : class
    {
        var result = new List<int>();
        var visited = new HashSet<T>();
        var current = target;
        while (!ReferenceEquals(current, root))
        {
            if (!visited.Add(current)) throw new InvalidOperationException("The preview anchor hierarchy contains a cycle.");
            var owner = parent(current) ?? throw new InvalidOperationException("The preview anchor is outside the staged output.");
            var siblings = children(owner).ToArray();
            int index = Array.FindIndex(siblings, candidate => ReferenceEquals(candidate, current));
            if (index < 0) throw new InvalidOperationException("The preview anchor is no longer in its parent.");
            result.Add(index);
            current = owner;
        }
        result.Reverse();
        return result;
    }

    internal static T Resolve<T>(T root, IReadOnlyList<int> path, Func<T, IEnumerable<T>> children) where T : class
    {
        var current = root;
        foreach (int index in path)
        {
            var descendants = children(current).ToArray();
            if (index < 0 || index >= descendants.Length) throw new InvalidOperationException("The copied preview hierarchy no longer matches the staged output.");
            current = descendants[index];
        }
        return current;
    }
}
