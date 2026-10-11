#nullable enable
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace UnityPackageImporter.Models;

/// <summary>
/// Serializes mutations of one avatar across stations. A weak key avoids
/// retaining a destroyed avatar/world, and cancellation never releases another
/// installation's lease. Callers revalidate their target after acquiring it.
/// </summary>
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
