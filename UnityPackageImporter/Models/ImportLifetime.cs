#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
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

    public bool IsClosing
    {
        get
        {
            lock (sync) return closing;
        }
    }

    public bool TryEnter([NotNullWhen(true)] out IDisposable? lease)
    {
        lock (sync)
        {
            if (closing)
            {
                lease = null;
                return false;
            }
            operations++;
            lease = new Lease(this);
            return true;
        }
    }

    public IDisposable Enter()
    {
        if (TryEnter(out var lease)) return lease;
        throw new OperationCanceledException(Token);
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
        private ImportLifetime? owner;
        public Lease(ImportLifetime owner) => this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
        public void Dispose() => Interlocked.Exchange(ref owner, null)?.Exit();
    }
}
