#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace UnityPackageImporter.Models;

internal enum ImportStage { ModelAssets, SourceReconstruction }
internal sealed record ImportStageMeasurement(ImportStage Stage, int ScheduledItems, TimeSpan Elapsed);

// Wall time includes awaited engine work. Counts describe scheduled inputs,
// not peak resources or successfully reconstructed objects. No asset identities.
internal sealed class ImportMeasurements
{
    private readonly List<ImportStageMeasurement> samples = new();
    internal IReadOnlyList<ImportStageMeasurement> Snapshot()
    {
        lock (samples) return Array.AsReadOnly(samples.ToArray());
    }
    internal IDisposable Measure(ImportStage stage, int scheduledItems) => new Measurement(this, stage, scheduledItems);

    private sealed class Measurement : IDisposable
    {
        private readonly ImportMeasurements owner;
        private readonly ImportStage stage;
        private readonly int count;
        private readonly long start = Stopwatch.GetTimestamp();
        private int disposed;
        internal Measurement(ImportMeasurements owner, ImportStage stage, int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            this.owner = owner;
            this.stage = stage;
            this.count = count;
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            var sample = new ImportStageMeasurement(stage, count, Stopwatch.GetElapsedTime(start));
            lock (owner.samples) owner.samples.Add(sample);
        }
    }
}
