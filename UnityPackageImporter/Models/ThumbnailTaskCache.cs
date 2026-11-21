#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UnityPackageImporter.Models;






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


            if (task.IsFaulted || task.IsCanceled)
            {
                taskCache.TryRemove(new KeyValuePair<string, Task<Uri?>>(guid, task));
            }
            throw;
        }
        catch (Exception)
        {

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
