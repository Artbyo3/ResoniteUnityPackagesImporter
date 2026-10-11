#nullable enable
using System;
using System.Collections.Generic;

namespace UnityPackageImporter.Models;

/// <summary>
/// Tracks only objects created by an attachment operation. Enroll a cloned
/// object before moving it outside the clone, so even a partially failed move
/// can be rolled back. Original avatar and package objects are never enrolled.
/// </summary>
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
        // Most recently moved child roots are handled before their owners.
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
