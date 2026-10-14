#nullable enable
using System;
using System.Collections.Generic;

namespace UnityPackageImporter.Models;

/// <summary>
/// Tracks the import items that are still selectable in the station. Items leave
/// this list only when the operation that owns them consumes their source.
/// </summary>
internal sealed class SelectableImportItems<T> where T : class
{
    private readonly List<T> entries = new();

    public IReadOnlyList<T> Entries => entries;
    public int Count => entries.Count;
    public int SelectedIndex { get; private set; } = -1;
    public T? Current => SelectedIndex >= 0 && SelectedIndex < entries.Count ? entries[SelectedIndex] : null;
    public bool CanNavigate => entries.Count > 1;

    public void Reset(IEnumerable<T> values)
    {
        entries.Clear();
        entries.AddRange(values);
        SelectedIndex = entries.Count == 0 ? -1 : 0;
    }

    public T? Select(int index)
    {
        if (entries.Count == 0)
        {
            SelectedIndex = -1;
            return null;
        }

        SelectedIndex = ((index % entries.Count) + entries.Count) % entries.Count;
        return entries[SelectedIndex];
    }

    public bool Consume(T item)
    {
        int consumedIndex = entries.FindIndex(candidate => ReferenceEquals(candidate, item));
        if (consumedIndex < 0) return false;

        entries.RemoveAt(consumedIndex);
        SelectedIndex = entries.Count == 0 ? -1 : Math.Min(consumedIndex, entries.Count - 1);
        return true;
    }
}
