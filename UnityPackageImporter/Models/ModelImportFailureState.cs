#nullable enable
using System;
using System.Threading;

namespace UnityPackageImporter.Models;

internal sealed class ModelImportFailureState
{
    private Exception? materialFailure;
    internal Exception? MaterialFailure => Volatile.Read(ref materialFailure);

    internal T MapMaterial<T>(Func<T> map)
    {
        try { return map(); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            ReportFailureDetails.Tag(error, ReportFailureReason.NativeMaterialMappingFailed);
            Interlocked.CompareExchange(ref materialFailure, error, null);
            throw;
        }
    }

    internal Exception Interrupted() => MaterialFailure ?? ReportFailureDetails.Tag(
        new InvalidOperationException("Engine model import was interrupted."), ReportFailureReason.ModelImportInterrupted);
}
