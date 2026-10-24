#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityPackageImporter.Models;

internal sealed record AvatarAnimationObject(long Id, long ParentId, string Name);
internal sealed class AvatarAnimationPathResolver
{
    private readonly long root;
    private readonly Dictionary<long, AvatarAnimationObject> objects;
    private readonly Dictionary<(long Parent, string Name), long[]> children;

    internal AvatarAnimationPathResolver(long avatarRootId, IEnumerable<AvatarAnimationObject> source)
    {
        root = avatarRootId;
        objects = source.ToDictionary(item => item.Id);
        children = objects.Values.GroupBy(item => (item.ParentId, item.Name)).ToDictionary(group => group.Key, group => group.Select(item => item.Id).ToArray());
    }
    private bool Owned(long identity)
    {
        var visited = new HashSet<long>();
        while (identity != 0 && visited.Add(identity) && objects.TryGetValue(identity, out var current))
        {
            if (identity == root) return true;
            identity = current.ParentId;
        }
        return false;
    }
    internal long Resolve(long host, string path, Func<long, bool> accepts)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(accepts);
        if (!Owned(host) || path.Contains('\\')) return 0;
        if (path.Length == 0) return accepts(host) ? host : 0;
        var parts = path.Split('/');
        if (parts.Any(part => part is "" or "." or "..")) return 0;
        var visited = new HashSet<long>();
        while (Owned(host) && visited.Add(host))
        {
            long current = host;
            foreach (string part in parts)
            {
                if (!children.TryGetValue((current, part), out var matches)) { current = 0; break; }
                if (matches.Length != 1) return 0;
                current = matches[0];
            }
            if (current != 0 && accepts(current)) return current;
            if (host == root) break;
            host = objects[host].ParentId;
        }
        return 0;
    }
}

