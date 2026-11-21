#nullable enable
using System;
using System.Collections.Generic;

namespace UnityPackageImporter.Models;






internal sealed class OwnedAttachmentTransaction<T> where T : class
{
    private readonly List<T> objects = new();
    private readonly HashSet<T> unique = new(ReferenceEqualityComparer.Instance);
    private bool completed;

    public void Track(T item)
    {
        if (completed) throw new InvalidOperationException("The attachment transaction is complete.");
        ArgumentNullException.ThrowIfNull(item);
        if (unique.Add(item)) objects.Add(item);
    }

    public void Commit()
    {
        completed = true;
        objects.Clear();
        unique.Clear();
    }

    public IReadOnlyList<Exception> RollBack(Action<T> destroy)
    {
        ArgumentNullException.ThrowIfNull(destroy);
        var errors = new List<Exception>();
        if (completed) return errors;
        completed = true;

        for (int i = objects.Count - 1; i >= 0; --i)
        {
            try { destroy(objects[i]); }
            catch (Exception error) { errors.Add(error); }
        }
        objects.Clear();
        unique.Clear();
        return errors;
    }
}
