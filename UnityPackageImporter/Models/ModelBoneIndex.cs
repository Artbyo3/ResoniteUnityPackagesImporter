#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityPackageImporter.Models;

internal sealed class ModelBoneIndex<T> where T : class
{
    private readonly List<(T Model, int Node, T? Bone)> bindings = new();

    internal void Add(T model, int node, T? bone) => bindings.Add((model, node, bone));

    internal T? Resolve(int node, T avatar, T excluded, Func<T, T, bool> contains, Func<T, bool> alive)
    {
        TryResolve(node, avatar, excluded, contains, alive, out var bone);
        return bone;
    }

    internal bool TryResolve(int node, T avatar, T excluded, Func<T, T, bool> contains, Func<T, bool> alive, out T? bone)
    {
        var candidates = bindings.Where(binding => binding.Node == node &&
            contains(binding.Model, avatar) && !contains(binding.Model, excluded)).ToArray();
        var primary = candidates.Where(binding => ReferenceEquals(binding.Model, avatar)).ToArray();
        var selected = primary.Length > 0 ? primary : candidates;
        bone = selected.Any(binding => binding.Bone == null || !alive(binding.Bone) ||
                !contains(binding.Bone, avatar) || contains(binding.Bone, excluded))
            ? null : AvatarConversionScope.Unique(selected.Select(binding => binding.Bone!));
        return selected.Length > 0;
    }

    internal static T? FindOwned(IEnumerable<T> objects, string name, Func<T, string> nameOf) =>
        AvatarConversionScope.Unique(objects.Where(item => string.Equals(nameOf(item), name, StringComparison.Ordinal)));
}
