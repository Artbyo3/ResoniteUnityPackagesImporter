#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace UnityPackageImporter.Models;

internal sealed record AuthoredAppearanceGraphCapture<T>(AuthoredAppearanceHierarchy Hierarchy, IReadOnlyDictionary<long, T> Targets) where T : class
{
    internal static AuthoredAppearanceGraphCapture<T> Capture(T root, IReadOnlyDictionary<T, long> aliases,
        IReadOnlyDictionary<long, T> transformTargets, IReadOnlySet<T> scaffolds, IEnumerable<long> reservedIds,
        Func<T, IEnumerable<T>> children, Func<T, string> name, Func<T, bool> active)
    {
        var reserved = reservedIds.Concat(aliases.Values).Concat(transformTargets.Keys).ToHashSet();
        var identities = new Dictionary<T, long>();
        var targets = new Dictionary<long, T>();
        var nodes = new List<AuthoredAppearanceObject>();
        var seen = new HashSet<T> { root };
        var pending = new Stack<(T Node, long Parent)>();
        foreach (var child in children(root).Reverse()) pending.Push((child, 0));
        long next = long.MinValue;
        while (pending.TryPop(out var entry))
        {
            if (!seen.Add(entry.Node)) throw new InvalidDataException("Authored appearance source hierarchy is cyclic or has multiple parents.");
            long parent = entry.Parent;
            if (scaffolds.Contains(entry.Node))
            {
                if (aliases.ContainsKey(entry.Node) || !active(entry.Node))
                    throw new InvalidDataException("Model import scaffold has an authored identity or changes inherited visibility.");
            }
            else
            {
                if (!aliases.TryGetValue(entry.Node, out long identity))
                {
                    while (next == 0 || reserved.Contains(next)) next = checked(next + 1);
                    identity = next;
                    reserved.Add(identity);
                    next = checked(next + 1);
                }
                identities.Add(entry.Node, identity);
                if (!targets.TryAdd(identity, entry.Node)) throw new InvalidDataException("Multiple source nodes claim one appearance identity.");
                nodes.Add(new(identity, parent, name(entry.Node), active(entry.Node)));
                parent = identity;
            }
            foreach (var child in children(entry.Node).Reverse()) pending.Push((child, parent));
        }
        if (aliases.Keys.Any(node => !identities.ContainsKey(node)) || scaffolds.Any(node => !seen.Contains(node)))
            throw new InvalidDataException("Authored model identities or scaffolds are outside this import structure.");
        var owners = new Dictionary<long, long>();
        foreach (var entry in transformTargets)
        {
            if (!identities.TryGetValue(entry.Value, out long owner))
                throw new InvalidDataException("Authored Transform alias has no source GameObject in this import structure.");
            owners.Add(entry.Key, owner);
        }
        return new(new AuthoredAppearanceHierarchy(nodes, owners), new ReadOnlyDictionary<long, T>(targets));
    }
}
