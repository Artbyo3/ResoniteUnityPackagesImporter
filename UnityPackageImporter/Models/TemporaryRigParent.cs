using System;
using System.Collections.Generic;

namespace UnityPackageImporter.Models;

// Tracks a parent created by this import, not an object selected by its name.
// The ownership/handoff rules are independent of the engine hierarchy API.
internal sealed class TemporaryRigParent<T> where T : class
{
    private readonly T parent;
    private readonly T rigRoot;
    private bool completed;

    internal TemporaryRigParent(T parent, T rigRoot)
    {
        this.parent = parent ?? throw new ArgumentNullException(nameof(parent));
        this.rigRoot = rigRoot ?? throw new ArgumentNullException(nameof(rigRoot));
        if (ReferenceEquals(parent, rigRoot))
            throw new ArgumentException("The temporary parent cannot be the rig itself.");
    }

    internal void Complete(T avatarRoot, T simulationSpace, T offsetSpace,
        Func<T, T> parentOf, Func<T, bool> isAlive, Action<T, T> promoteChildren)
    {
        if (completed) return;
        if (avatarRoot == null || !isAlive(avatarRoot) || !isAlive(parent) || !isAlive(rigRoot))
            throw new InvalidOperationException("The imported rig hierarchy is no longer available.");
        if (!ReferenceEquals(simulationSpace, avatarRoot) || !ReferenceEquals(offsetSpace, avatarRoot))
            throw new InvalidOperationException("Native avatar setup has not released the temporary rig reference space.");
        if (ReferenceEquals(parent, avatarRoot) || !ReferenceEquals(parentOf(rigRoot), parent))
            throw new InvalidOperationException("The temporary rig hierarchy changed before avatar completion.");

        var destination = parentOf(parent);
        var visited = new HashSet<T>(ReferenceEqualityComparer.Instance);
        var cursor = destination;
        while (cursor != null && !ReferenceEquals(cursor, avatarRoot))
        {
            if (!visited.Add(cursor) || !isAlive(cursor))
                throw new InvalidOperationException("The temporary rig hierarchy is invalid.");
            cursor = parentOf(cursor);
        }
        if (cursor == null)
            throw new InvalidOperationException("The temporary rig parent is outside the completed avatar.");

        // The engine adapter preserves world transforms and destroys only the
        // empty parent. Mark complete only after that operation succeeds.
        promoteChildren(parent, destination);
        completed = true;
    }
}
