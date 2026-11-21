#nullable enable
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace UnityPackageImporter.Models;






internal static class AttachmentInstallationGate
{
    private static readonly ConditionalWeakTable<object, SemaphoreSlim> Gates = new();

    public static async Task<IDisposable> EnterAsync(object avatar, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(avatar);
        var gate = Gates.GetValue(avatar, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Lease(gate);
    }

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? held = gate;
        public void Dispose() => Interlocked.Exchange(ref held, null)?.Release();
    }
}
