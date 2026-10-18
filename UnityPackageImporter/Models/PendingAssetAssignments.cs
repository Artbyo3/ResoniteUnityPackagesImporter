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
    private sealed record Assignment(string[] AssetGuids, Func<Task<TAsset>> Load,
        Func<TAsset, CancellationToken, Task<bool>> Apply, bool Required);
    private readonly object sync = new();
    private readonly Dictionary<object, Assignment> assignments = new(ReferenceEqualityComparer.Instance);

    public string[] MissingAssetGuids => GetMissingAssetGuids();

    public string[] GetMissingAssetGuids(Func<object, bool> targetFilter = null)
    {
        lock (sync)
        {
            return assignments
                .Where(pair => pair.Value.Required && (targetFilter == null || targetFilter(pair.Key)))
                .SelectMany(pair => pair.Value.AssetGuids)
                .ToArray();
        }
    }

    public void Register(object target, string assetGuid, Func<Task<TAsset>> load,
        Func<TAsset, CancellationToken, Task<bool>> apply, bool required = true)
        => RegisterMany(target, new[] { assetGuid }, load, apply, required);

    public void RegisterMany(object target, IEnumerable<string> assetGuids, Func<Task<TAsset>> load,
        Func<TAsset, CancellationToken, Task<bool>> apply, bool required = true)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(load);
        ArgumentNullException.ThrowIfNull(apply);
        var guids = (assetGuids ?? Array.Empty<string>()).ToArray();
        if (guids.Length == 0 || guids.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Missing asset GUID.", nameof(assetGuids));
        lock (sync) assignments[target] = new Assignment(guids.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), load, apply, required);
    }

    public bool CanResolveAny(IEnumerable<string> availableGuids)
    {
        var available = new HashSet<string>(availableGuids ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        lock (sync) return assignments.Values.Any(value => value.AssetGuids.Any(available.Contains));
    }

    // The import session serializes resolution requests. Registration can still
    // happen while a material is being converted; snapshot only this attempt.
    public async Task<int> ResolveAsync(IEnumerable<string> availableGuids, CancellationToken cancellation,
        Action<string, Exception> reportFailure)
    {
        cancellation.ThrowIfCancellationRequested();
        var available = new HashSet<string>(availableGuids ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        KeyValuePair<object, Assignment>[] pending;
        lock (sync) pending = assignments.Where(pair => pair.Value.AssetGuids.Any(available.Contains)).ToArray();
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
                reportFailure?.Invoke(string.Join(", ", entry.Value.AssetGuids), exception);
            }
        }
        return restored;
    }
}
