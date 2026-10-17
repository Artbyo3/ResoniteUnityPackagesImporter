#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UnityPackageImporter.Models;

/// <summary>
/// Thread-safe and cancellation-resilient cache for material thumbnails.
/// Decouples shared background thumbnail generation from transient UI tokens,
/// safely evicts faulted or canceled tasks, and prevents cache poisoning.
/// </summary>
internal sealed class ThumbnailTaskCache
{
    private readonly ConcurrentDictionary<string, Uri> uriCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<Uri?>> taskCache = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGetCachedUri(string guid, out Uri? uri)
    {
        if (uriCache.TryGetValue(guid, out var found))
        {
            uri = found;
            return true;
        }
        uri = null;
        return false;
    }

    public void StoreCachedUri(string guid, Uri uri)
    {
        if (!string.IsNullOrEmpty(guid) && uri != null)
        {
            uriCache[guid] = uri;
        }
    }

    /// <summary>
    /// Gets or creates a background task to generate/import a thumbnail.
    /// The generator factory produces the shared background task, which runs independent
    /// of transient UI tokens.
    /// The caller can await with their own UI cancellation token via <paramref name="callerToken"/>.
    /// </summary>
    public async Task<Uri?> GetOrLoadThumbnailAsync(
        string guid,
        Func<string, Task<Uri?>> generator,
        CancellationToken callerToken = default)
    {
        if (string.IsNullOrEmpty(guid)) return null;

        if (uriCache.TryGetValue(guid, out var cachedUri))
        {
            return cachedUri;
        }

        Task<Uri?> task;
        while (true)
        {
            if (taskCache.TryGetValue(guid, out var existingTask))
            {
                if (existingTask.IsFaulted || existingTask.IsCanceled ||
                    (existingTask.IsCompletedSuccessfully && existingTask.Result == null))
                {
                    taskCache.TryRemove(new KeyValuePair<string, Task<Uri?>>(guid, existingTask));
                    continue;
                }
                task = existingTask;
                break;
            }

            task = taskCache.GetOrAdd(guid, id => Task.Run(() => generator(id)));
            break;
        }

        Uri? result;
        try
        {
            result = await task.WaitAsync(callerToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Transient caller UI cancellation.
            // If the underlying shared task was itself canceled or faulted, evict it.
            if (task.IsFaulted || task.IsCanceled)
            {
                taskCache.TryRemove(new KeyValuePair<string, Task<Uri?>>(guid, task));
            }
            throw;
        }
        catch (Exception)
        {
            // Shared task faulted
            taskCache.TryRemove(new KeyValuePair<string, Task<Uri?>>(guid, task));
            throw;
        }

        if (result == null)
        {
            taskCache.TryRemove(new KeyValuePair<string, Task<Uri?>>(guid, task));
            return null;
        }

        uriCache[guid] = result;
        return result;
    }

    public void Clear()
    {
        taskCache.Clear();
        uriCache.Clear();
    }

    public int InFlightCount => taskCache.Count;
    public int CachedUriCount => uriCache.Count;
}
