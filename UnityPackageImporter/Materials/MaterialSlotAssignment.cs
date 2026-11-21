#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace UnityPackageImporter.Materials;


internal static class MaterialSlotAssignment
{

    internal static bool ReplacesExistingSlot(int index, int count) => index < count;

    internal static void EnsureSlotCount<T>(System.Collections.Generic.IList<T> slots, int count, Func<T> createFallback)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        while (slots.Count < count) slots.Add(createFallback());
    }

    internal static async Task AssignAsync<T>(int index, Func<Task<T>> resolve, Func<Exception, Task<T>> fallback,
        Func<int, T, Task> assign, CancellationToken cancellation = default) where T : class
    {
        cancellation.ThrowIfCancellationRequested();
        T material;
        try
        {
            material = await resolve();
            if (material == null) throw new InvalidDataException("Material conversion returned no material.");
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            cancellation.ThrowIfCancellationRequested();
            material = await fallback(error);
            if (material == null) throw new InvalidDataException("Material fallback returned no material.", error);
        }
        cancellation.ThrowIfCancellationRequested();
        await assign(index, material);
    }
}
