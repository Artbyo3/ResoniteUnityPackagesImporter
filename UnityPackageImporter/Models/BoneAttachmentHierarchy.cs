#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityPackageImporter.Models;

/// <summary>
/// Snapshots the authoritative attachment hierarchy. Previously installed outfit
/// branches are excluded as entire subtrees, so another outfit cannot borrow
/// bones whose lifetime belongs to a different installation.
/// </summary>
internal static class BoneAttachmentHierarchy
{
    public static MergeHierarchyNode Snapshot<T>(T root, Func<T, IEnumerable<T>> children,
        Func<T, (string Id, string Name)> identify, IDictionary<string, T> nodes,
        IReadOnlySet<T>? excludedRoots = null, IReadOnlySet<T>? separatePlanRoots = null) where T : class
    {
        ArgumentNullException.ThrowIfNull(root);
        if (excludedRoots?.Contains(root) == true)
            throw new InvalidOperationException("Attachment target belongs to another outfit.");
        return Visit(root);

        MergeHierarchyNode Visit(T item)
        {
            var identity = identify(item);
            if (nodes.ContainsKey(identity.Id))
                throw new InvalidOperationException("Attachment hierarchy contains duplicate or cyclic identities.");
            nodes.Add(identity.Id, item);
            bool separate = !ReferenceEquals(item, root) && separatePlanRoots?.Contains(item) == true;
            return new MergeHierarchyNode
            {
                Id = identity.Id,
                Name = identity.Name,
                StartsNestedMerge = separate,
                Children = separate ? Array.Empty<MergeHierarchyNode>() : children(item)
                    .Where(child => excludedRoots?.Contains(child) != true)
                    .Select(Visit).ToArray()
            };
        }
    }
}
