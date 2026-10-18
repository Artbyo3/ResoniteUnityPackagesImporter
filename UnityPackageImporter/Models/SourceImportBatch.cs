#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UnityPackageImporter.Models;

internal sealed record SourceImportBatchResult<T>(IReadOnlyList<T> Succeeded, IReadOnlyList<T> Failed);

// Preparation can publish intermediate state before an import finishes. The
// batch outcome, not the presence of that state, controls output eligibility.
internal static class SourceImportBatch
{
    internal static async Task<SourceImportBatchResult<T>> RunAsync<T>(IEnumerable<T> sources,
        Func<T, CancellationToken, Task> import, Func<T, Exception, Task> cleanupFailure,
        int maxDegreeOfParallelism = 0, CancellationToken cancellationToken = default,
        ImportLifetime? lifetime = null)
    {
        var inputs = sources.ToArray();
        var succeeded = new bool[inputs.Length];
        await BoundedTaskScheduler.RunAsync(Enumerable.Range(0, inputs.Length), async (index, token) =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                await import(inputs[index], token);
                succeeded[index] = true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) { await cleanupFailure(inputs[index], error); }
        }, maxDegreeOfParallelism, cancellationToken, lifetime);
        return new SourceImportBatchResult<T>(
            Array.AsReadOnly(inputs.Where((_, index) => succeeded[index]).ToArray()),
            Array.AsReadOnly(inputs.Where((_, index) => !succeeded[index]).ToArray()));
    }
}
