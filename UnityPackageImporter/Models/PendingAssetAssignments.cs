using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UnityPackageImporter.Models;

// Retains the assignment as well as the missing GUID. Asset availability alone
// does not mean a material field has actually been repaired.
internal sealed class PendingAssetAssignments<TAsset> where TAsset : class
{
    private sealed record Assignment(string AssetGuid, Func<Task<TAsset>> Load,
        Func<TAsset, CancellationToken, Task<bool>> Apply);
    private readonly object sync = new();
    private readonly Dictionary<object, Assignment> assignments = new(ReferenceEqualityComparer.Instance);

    public string[] MissingAssetGuids
    {
        get { lock (sync) return assignments.Values.Select(value => value.AssetGuid).ToArray(); }
    }

    public void Register(object target, string assetGuid, Func<Task<TAsset>> load,
        Func<TAsset, CancellationToken, Task<bool>> apply)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(load);
        ArgumentNullException.ThrowIfNull(apply);
        if (string.IsNullOrWhiteSpace(assetGuid)) throw new ArgumentException("Missing asset GUID.", nameof(assetGuid));
        lock (sync) assignments[target] = new Assignment(assetGuid, load, apply);
    }

    public bool CanResolveAny(IEnumerable<string> availableGuids)
    {
        var available = new HashSet<string>(availableGuids ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        lock (sync) return assignments.Values.Any(value => available.Contains(value.AssetGuid));
    }

    // The import session serializes resolution requests. Registration can still
    // happen while a material is being converted; snapshot only this attempt.
    public async Task<int> ResolveAsync(IEnumerable<string> availableGuids, CancellationToken cancellation,
        Action<string, Exception> reportFailure)
    {
        cancellation.ThrowIfCancellationRequested();
        var available = new HashSet<string>(availableGuids ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        KeyValuePair<object, Assignment>[] pending;
        lock (sync) pending = assignments.Where(pair => available.Contains(pair.Value.AssetGuid)).ToArray();
        int restored = 0;
        foreach (var entry in pending)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                var asset = await entry.Value.Load();
                cancellation.ThrowIfCancellationRequested();
                if (asset == null) continue;
                // A field can be rebound while its old asset is loading.
                lock (sync)
                    if (!assignments.TryGetValue(entry.Key, out var expected) || !ReferenceEquals(expected, entry.Value))
                        continue;
                if (!await entry.Value.Apply(asset, cancellation)) continue;
                cancellation.ThrowIfCancellationRequested();
                lock (sync)
                {
                    if (assignments.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry.Value))
                    {
                        assignments.Remove(entry.Key);
                        restored++;
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                reportFailure?.Invoke(entry.Value.AssetGuid, exception);
            }
        }
        return restored;
    }
}
