using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityPackageImporter.Models;

#nullable enable

internal static class BoundedTaskSchedulerTests
{
    public static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        test("Bounded scheduler: Peak concurrency never exceeds maxDegreeOfParallelism", () =>
        {
            const int totalItems = 16;
            const int maxConcurrency = 4;
            int maxObserved = 0;
            int currentInFlight = 0;

            var items = Enumerable.Range(0, totalItems).ToList();
            var stats = BoundedTaskScheduler.RunAsync(items, async (item, token) =>
            {
                int inFlight = Interlocked.Increment(ref currentInFlight);
                int initial;
                do
                {
                    initial = maxObserved;
                    if (inFlight <= initial) break;
                }
                while (Interlocked.CompareExchange(ref maxObserved, inFlight, initial) != initial);

                await Task.Delay(10, token);
                Interlocked.Decrement(ref currentInFlight);
            }, maxDegreeOfParallelism: maxConcurrency).GetAwaiter().GetResult();

            check(stats.ScheduledCount == totalItems, $"Expected {totalItems} scheduled items, got {stats.ScheduledCount}");
            check(stats.CompletedCount == totalItems, $"Expected {totalItems} completed items, got {stats.CompletedCount}");
            check(stats.PeakInFlight <= maxConcurrency, $"Peak in-flight ({stats.PeakInFlight}) exceeded limit ({maxConcurrency})");
            check(maxObserved <= maxConcurrency, $"Observed in-flight ({maxObserved}) exceeded limit ({maxConcurrency})");
            check(stats.PeakInFlight > 1, $"Expected parallel execution with peak > 1, got {stats.PeakInFlight}");
        });

        test("Bounded scheduler: Limit of 1 forces strict sequential execution", () =>
        {
            const int totalItems = 6;
            int currentInFlight = 0;
            int maxObserved = 0;

            var items = Enumerable.Range(0, totalItems).ToList();
            var stats = BoundedTaskScheduler.RunAsync(items, async (item, token) =>
            {
                int inFlight = Interlocked.Increment(ref currentInFlight);
                int initial;
                do
                {
                    initial = maxObserved;
                    if (inFlight <= initial) break;
                }
                while (Interlocked.CompareExchange(ref maxObserved, inFlight, initial) != initial);

                await Task.Delay(5, token);
                Interlocked.Decrement(ref currentInFlight);
            }, maxDegreeOfParallelism: 1).GetAwaiter().GetResult();

            check(stats.CompletedCount == totalItems, "All items should complete.");
            check(stats.PeakInFlight == 1, $"Expected peak 1 for sequential execution, got {stats.PeakInFlight}");
            check(maxObserved == 1, $"Observed concurrency was {maxObserved}, expected 1.");
        });

        test("Bounded scheduler: Empty collection returns zero stats immediately", () =>
        {
            var stats = BoundedTaskScheduler.RunAsync(
                Array.Empty<int>(),
                (item, _) => Task.CompletedTask,
                maxDegreeOfParallelism: 4).GetAwaiter().GetResult();

            check(stats.ScheduledCount == 0 && stats.CompletedCount == 0 && stats.PeakInFlight == 0,
                "Empty collection did not produce zero stats.");
        });

        test("Bounded scheduler: Pre-cancelled token rejects execution without starting work", () =>
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            int started = 0;
            bool threw = false;
            try
            {
                BoundedTaskScheduler.RunAsync(
                    new[] { 1, 2, 3 },
                    (item, _) => { Interlocked.Increment(ref started); return Task.CompletedTask; },
                    maxDegreeOfParallelism: 2,
                    cancellationToken: cts.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                threw = true;
            }

            check(threw, "Pre-cancelled token did not throw OperationCanceledException.");
            check(started == 0, "Pre-cancelled scheduler started items.");
        });

        test("Bounded scheduler: In-flight cancellation stops unstarted items and drains active tasks", () =>
        {
            var lifetime = new ImportLifetime();
            const int totalItems = 10;
            const int maxConcurrency = 2;
            int started = 0;
            int finished = 0;
            var tcsReadyToCancel = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            bool threw = false;
            Task runTask = Task.Run(async () =>
            {
                try
                {
                    await BoundedTaskScheduler.RunAsync(
                        Enumerable.Range(0, totalItems),
                        async (item, token) =>
                        {
                            int count = Interlocked.Increment(ref started);
                            if (count == maxConcurrency)
                            {
                                tcsReadyToCancel.TrySetResult();
                            }
                            // Wait until cancellation fires
                            while (!token.IsCancellationRequested)
                            {
                                await Task.Delay(5, CancellationToken.None);
                            }
                            Interlocked.Increment(ref finished);
                        },
                        maxDegreeOfParallelism: maxConcurrency,
                        lifetime: lifetime);
                }
                catch (OperationCanceledException)
                {
                    threw = true;
                }
            });

            // Wait until exactly maxConcurrency items are actively running
            tcsReadyToCancel.Task.GetAwaiter().GetResult();

            // Cancel and drain through ImportLifetime
            var drainTask = lifetime.CancelAndDrainAsync();
            drainTask.GetAwaiter().GetResult();
            runTask.GetAwaiter().GetResult();

            check(threw, "Scheduler did not throw OperationCanceledException on cancellation.");
            check(started == maxConcurrency, $"Expected only {maxConcurrency} tasks to start before cancellation, but {started} started.");
            check(finished == maxConcurrency, $"Expected all {maxConcurrency} started tasks to drain, but {finished} finished.");
        });

        test("Bounded scheduler: Task exception releases semaphore and propagates", () =>
        {
            bool threw = false;
            try
            {
                BoundedTaskScheduler.RunAsync(
                    new[] { 1, 2, 3 },
                    (item, _) => item == 2 ? throw new InvalidOperationException("Synthetic failure") : Task.CompletedTask,
                    maxDegreeOfParallelism: 2).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is InvalidOperationException || (ex is AggregateException agg && agg.InnerExceptions.Any(e => e is InvalidOperationException)))
            {
                threw = true;
            }

            check(threw, "Task failure was not propagated.");
        });
    }
}
