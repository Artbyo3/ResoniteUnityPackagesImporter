#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace UnityPackageImporter.Models;

public sealed record AuthoredAppearanceObject(long Id, long ParentId, string Name, bool ActiveSelf);

public sealed class AuthoredAppearanceHierarchy
{
    public IReadOnlyDictionary<long, AuthoredAppearanceObject> Objects { get; }
    public IReadOnlyDictionary<long, long> TransformOwners { get; }

    public AuthoredAppearanceHierarchy(IEnumerable<AuthoredAppearanceObject> objects,
        IEnumerable<KeyValuePair<long, long>> transformOwners)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(transformOwners);

        var objectSnapshot = new Dictionary<long, AuthoredAppearanceObject>();
        foreach (var item in objects)
        {
            if (item == null)
                throw new ArgumentException("Authored appearance objects cannot contain null entries.", nameof(objects));
            if (item.Id == 0 || !objectSnapshot.TryAdd(item.Id, item))
                throw new ArgumentException($"Duplicate or zero authored appearance object ID: {item.Id}.", nameof(objects));
        }

        foreach (var item in objectSnapshot.Values)
        {
            if (item.ParentId != 0 && !objectSnapshot.ContainsKey(item.ParentId))
                throw new ArgumentException($"Authored appearance object {item.Id} has missing parent {item.ParentId}.", nameof(objects));
        }

        var validated = new HashSet<long>();
        foreach (long identity in objectSnapshot.Keys)
        {
            var path = new HashSet<long>();
            long current = identity;
            while (current != 0 && !validated.Contains(current))
            {
                if (!path.Add(current))
                    throw new ArgumentException($"Authored appearance hierarchy contains a parent cycle at object {current}.", nameof(objects));
                current = objectSnapshot[current].ParentId;
            }
            validated.UnionWith(path);
        }

        var transformSnapshot = new Dictionary<long, long>();
        var assignedOwners = new HashSet<long>();
        foreach (var assignment in transformOwners)
        {
            if (assignment.Key == 0 || !transformSnapshot.TryAdd(assignment.Key, assignment.Value))
                throw new ArgumentException($"Duplicate or zero authored appearance Transform ID: {assignment.Key}.", nameof(transformOwners));
            if (!objectSnapshot.ContainsKey(assignment.Value))
                throw new ArgumentException($"Authored appearance Transform {assignment.Key} has missing owner {assignment.Value}.", nameof(transformOwners));
            if (!assignedOwners.Add(assignment.Value))
                throw new ArgumentException($"Authored appearance object {assignment.Value} has multiple Transform assignments.", nameof(transformOwners));
        }

        Objects = new ReadOnlyDictionary<long, AuthoredAppearanceObject>(objectSnapshot);
        TransformOwners = new ReadOnlyDictionary<long, long>(transformSnapshot);
    }
}
