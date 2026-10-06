using UnityPackageImporter.Models;

internal static class ImportLifetimeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public static void Register(Action<string, Action> test)
    {
        test("Import lifetime rejects work after cancellation before any operation", () =>
        {
            var lifetime = new ImportLifetime();
            var drain = lifetime.CancelAndDrainAsync();

            Check(drain.IsCompletedSuccessfully, "An empty import must drain immediately.");
            Check(lifetime.Token.IsCancellationRequested, "Closing must request cancellation.");
            RejectEntry(lifetime);
        });

        test("Import lifetime holds cleanup until an in-flight native operation exits", () =>
        {
            var lifetime = new ImportLifetime();
            using var operation = lifetime.Enter();
            var drain = lifetime.CancelAndDrainAsync();

            Check(lifetime.Token.IsCancellationRequested, "Work must see cancellation before cleanup can proceed.");
            Check(!drain.IsCompleted, "Cancellation must not clean up beneath active native work.");
            RejectEntry(lifetime);
            operation.Dispose();
            Complete(drain);
        });

        test("Import lifetime drains nested and parallel leases only after the last exit", () =>
        {
            var lifetime = new ImportLifetime();
            using var outer = lifetime.Enter();
            using var nested = lifetime.Enter();
            using var parallel = lifetime.Enter();
            var drain = lifetime.CancelAndDrainAsync();

            nested.Dispose();
            Check(!drain.IsCompleted, "Finishing nested work must retain the outer and parallel operations.");
            outer.Dispose();
            Check(!drain.IsCompleted, "Finishing outer work must retain the parallel operation.");
            parallel.Dispose();
            Complete(drain);
        });

        test("Import lifetime duplicate concurrent disposal cannot release another operation", () =>
        {
            var lifetime = new ImportLifetime();
            using var remaining = lifetime.Enter();
            using var shared = lifetime.Enter();
            var drain = lifetime.CancelAndDrainAsync();

            Parallel.For(0, 64, _ => shared.Dispose());
            Check(!drain.IsCompleted, "Disposing the same lease concurrently must not consume the remaining lease.");
            remaining.Dispose();
            Complete(drain);
            shared.Dispose();
            Check(drain.IsCompletedSuccessfully, "Late duplicate disposal must leave successful drainage intact.");
        });

        test("Import lifetime concurrent close requests share one drain and one cancellation", () =>
        {
            var lifetime = new ImportLifetime();
            using var operation = lifetime.Enter();
            int notifications = 0;
            using var registration = lifetime.Token.Register(() => Interlocked.Increment(ref notifications));
            var drains = new Task[16];

            Parallel.For(0, drains.Length, i => drains[i] = lifetime.CancelAndDrainAsync());
            Check(drains.All(task => ReferenceEquals(task, drains[0])), "Repeated closes must await the same cleanup barrier.");
            Check(notifications == 1, "Cancellation callbacks must run only once.");
            Check(drains.All(task => !task.IsCompleted), "No close may bypass an active operation.");
            operation.Dispose();
            Complete(Task.WhenAll(drains));
            Check(ReferenceEquals(lifetime.CancelAndDrainAsync(), drains[0]), "Closing after drainage must stay idempotent.");
            RejectEntry(lifetime);
        });

        test("Import lifetime entry racing cancellation either joins drainage or is rejected", () =>
        {
            for (int attempt = 0; attempt < 64; attempt++)
            {
                var lifetime = new ImportLifetime();
                using var existing = lifetime.Enter();
                using var start = new ManualResetEventSlim();
                var entry = Task.Run<IDisposable?>(() =>
                {
                    start.Wait();
                    try { return lifetime.Enter(); }
                    catch (OperationCanceledException error)
                    {
                        Check(error.CancellationToken == lifetime.Token, "Rejected work must receive its own import token.");
                        return null;
                    }
                });
                // A tuple prevents Task.Run from unwrapping the drain task: the held
                // operations are intentionally released only after the race settles.
                var close = Task.Run(() =>
                {
                    start.Wait();
                    return (Drain: lifetime.CancelAndDrainAsync(), Requested: true);
                });

                start.Set();
                using var racedOperation = entry.WaitAsync(Timeout).GetAwaiter().GetResult();
                var drain = close.WaitAsync(Timeout).GetAwaiter().GetResult().Drain;
                RejectEntry(lifetime);
                Check(!drain.IsCompleted, "The preexisting operation must always hold the cleanup barrier.");
                existing.Dispose();
                if (racedOperation != null)
                    Check(!drain.IsCompleted, "Work accepted before cancellation must also hold the barrier.");
                racedOperation?.Dispose();
                Complete(drain);
            }
        });
    }

    private static void RejectEntry(ImportLifetime lifetime)
    {
        try
        {
            using var unexpected = lifetime.Enter();
        }
        catch (OperationCanceledException error)
        {
            Check(error.CancellationToken == lifetime.Token, "The rejection must carry the import cancellation token.");
            return;
        }
        throw new Exception("A closing import accepted a new operation.");
    }

    private static void Complete(Task task) => task.WaitAsync(Timeout).GetAwaiter().GetResult();
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
