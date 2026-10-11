#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UnityPackageImporter.Models;

// Cache the task, not an unfinished asset. All callers observe the same result or failure.
internal sealed class AsyncImportCache<T>
{
    private readonly ConcurrentDictionary<string, Lazy<Task<T>>> tasks = new(StringComparer.Ordinal);

    public Task<T> GetOrAdd(string assetId, Func<Task<T>> import) => tasks.GetOrAdd(assetId,
        _ => new Lazy<Task<T>>(async () => await import(), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    // A companion package may make previously unavailable assets loadable. Retry
    // only unsuccessful completed attempts: successful and in-flight imports
    // must retain their identity for all materials that share them.
    public void RetryUnsuccessful(string? assetId = null)
    {
        if (assetId != null)
        {
            if (tasks.TryGetValue(assetId, out var value))
                RemoveIfUnsuccessful(new KeyValuePair<string, Lazy<Task<T>>>(assetId, value));
            return;
        }
        foreach (var entry in tasks)
            RemoveIfUnsuccessful(entry);
    }

    private void RemoveIfUnsuccessful(KeyValuePair<string, Lazy<Task<T>>> entry)
    {
        if (!entry.Value.IsValueCreated) return;
        var task = entry.Value.Value;
        if (task.IsFaulted || task.IsCanceled || (task.IsCompletedSuccessfully && task.Result is null))
            ((ICollection<KeyValuePair<string, Lazy<Task<T>>>>)tasks).Remove(entry);
    }
}
