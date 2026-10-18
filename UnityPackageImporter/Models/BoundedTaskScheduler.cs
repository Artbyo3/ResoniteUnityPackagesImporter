#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UnityPackageImporter.Models;

internal sealed record BoundedSchedulingStats(int ScheduledCount, int CompletedCount, int PeakInFlight);

internal static class BoundedTaskScheduler
{
    public static int DefaultDegreeOfParallelism => Math.Clamp(Environment.ProcessorCount / 2, 2, 4);

    public static async Task<BoundedSchedulingStats> RunAsync<T>(
        IEnumerable<T> items,
        Func<T, CancellationToken, Task> action,
        int maxDegreeOfParallelism = 0,
        CancellationToken cancellationToken = default,
        ImportLifetime? lifetime = null)
    {
        if (maxDegreeOfParallelism <= 0)
            maxDegreeOfParallelism = DefaultDegreeOfParallelism;

        var list = items?.ToList() ?? new List<T>();
        if (list.Count == 0)
            return new BoundedSchedulingStats(0, 0, 0);

        var token = lifetime?.Token ?? cancellationToken;
        token.ThrowIfCancellationRequested();

        using var semaphore = new SemaphoreSlim(maxDegreeOfParallelism, maxDegreeOfParallelism);
        var tasks = new List<Task>(list.Count);
        int currentInFlight = 0;
        int peakInFlight = 0;
        int completedCount = 0;

        try
        {
            foreach (var item in list)
            {
                token.ThrowIfCancellationRequested();

                await semaphore.WaitAsync(token).ConfigureAwait(false);

                tasks.Add(Task.Run(async () =>
                {
                    using var lease = lifetime?.Enter();
                    int inFlight = Interlocked.Increment(ref currentInFlight);

                    // Thread-safe update of peak concurrent in-flight items
                    int initialPeak;
                    do
                    {
                        initialPeak = peakInFlight;
                        if (inFlight <= initialPeak) break;
                    }
                    while (Interlocked.CompareExchange(ref peakInFlight, inFlight, initialPeak) != initialPeak);

                    try
                    {
                        token.ThrowIfCancellationRequested();
                        await action(item, token).ConfigureAwait(false);
                        Interlocked.Increment(ref completedCount);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref currentInFlight);
                        semaphore.Release();
                    }
                }, token));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Drain already-launched tasks before rethrowing cancellation so no orphaned work is left behind
            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Discard exceptions from tasks that were cancelled during drain before rethrowing original cancellation.
            }
            throw;
        }

        return new BoundedSchedulingStats(list.Count, completedCount, peakInFlight);
    }
}
