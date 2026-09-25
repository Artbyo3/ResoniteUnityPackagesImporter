using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace UnityPackageImporter.Models;

// Cache the task, not an unfinished asset. All callers observe the same result or failure.
internal sealed class AsyncImportCache<T>
{
    private readonly ConcurrentDictionary<string, Lazy<Task<T>>> tasks = new(StringComparer.Ordinal);

    public Task<T> GetOrAdd(string assetId, Func<Task<T>> import) => tasks.GetOrAdd(assetId,
        _ => new Lazy<Task<T>>(async () => await import(), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
}
