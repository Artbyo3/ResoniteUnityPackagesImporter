#nullable enable
using System.Collections.Generic;

namespace UnityPackageImporter.Models;

/// <summary>Tracks at most one expanded entry independently of its display name.</summary>
internal sealed class AccordionState<TKey> where TKey : notnull
{
    private readonly HashSet<TKey> expanded;

    public AccordionState(IEqualityComparer<TKey>? comparer = null)
    {
        expanded = new HashSet<TKey>(comparer);
    }

    public bool IsExpanded(TKey key) => expanded.Contains(key);

    public bool Toggle(TKey key)
    {
        if (expanded.Remove(key)) return false;
        expanded.Clear();
        expanded.Add(key);
        return true;
    }

    public void Clear() => expanded.Clear();
}
