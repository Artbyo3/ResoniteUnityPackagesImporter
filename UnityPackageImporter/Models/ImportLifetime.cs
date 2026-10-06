using System;
using System.Threading;
using System.Threading.Tasks;

namespace UnityPackageImporter.Models;

// Cancellation stops new work immediately; cleanup waits for existing native work to yield.
internal sealed class ImportLifetime
{
    private readonly object sync = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int operations;
    private bool closing;
    public CancellationToken Token => cancellation.Token;

    public IDisposable Enter()
    {
        lock (sync)
        {
            if (closing) throw new OperationCanceledException(Token);
            operations++;
            return new Lease(this);
        }
    }

    public Task CancelAndDrainAsync()
    {
        lock (sync)
        {
            closing = true;
            if (operations == 0) drained.TrySetResult();
        }
        cancellation.Cancel();
        return drained.Task;
    }

    private void Exit()
    {
        lock (sync)
            if (--operations == 0 && closing) drained.TrySetResult();
    }

    private sealed class Lease : IDisposable
    {
        private ImportLifetime owner;
        public Lease(ImportLifetime owner) => this.owner = owner;
        public void Dispose() => Interlocked.Exchange(ref owner, null)?.Exit();
    }
}
