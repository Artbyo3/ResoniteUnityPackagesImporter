#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityPackageImporter.Extractor;

namespace UnityPackageImporter.Models;

// Import-local diagnostics. No global log subscription or native world references.
internal sealed class ImportReportContext
{
    private readonly object gate = new();
    private readonly Dictionary<ReportWarningCategory, int> warningCounts = new();
    private readonly List<ReportWarning> warnings = new();
    private readonly List<ReportObjectFailure> failures = new();
    private readonly List<ReportModelFailure> modelFailures = new();
    private int modelFailureCount;
    private readonly HashSet<object> reported = new(ReferenceEqualityComparer.Instance);
    private readonly PackageObservation[] packages;
    private readonly int packageCount;
    private int sourceFailures;
    internal Guid SessionId { get; } = Guid.NewGuid();
    internal ReportEnvironment Environment { get; private set; }
    internal bool IncludeNames { get; }
    private int stage;
    internal ImportReportStage Stage { get => (ImportReportStage)Volatile.Read(ref stage); set => Volatile.Write(ref stage, (int)value); }

    internal ImportReportContext(ReportEnvironment environment, IEnumerable<string> paths, bool includeNames = false)
    {
        Environment = environment;
        IncludeNames = includeNames;
        string[] inputs = paths.ToArray();
        packageCount = inputs.Length;
        packages = inputs.Take(16).Select(path => new PackageObservation(path)).ToArray();
    }

    internal bool TryBeginReport(object identity)
    {
        lock (gate) return reported.Count < 64 && reported.Add(identity);
    }

    internal void SetLocale(string locale)
    {
        lock (gate) Environment = Environment with { Locale = locale };
    }

    internal void Warning(ReportWarningCategory category, ReportDiagnosticCode code, long? id = null, string? name = null, string? sourceKey = null)
    {
        lock (gate)
        {
            warningCounts[category] = warningCounts.GetValueOrDefault(category) + 1;
            if (warnings.Count < 12) warnings.Add(new(category, code, id, IncludeNames ? name : null, sourceKey));
        }
    }

    internal void ObjectFailed(ReportObjectFailure failure)
    {
        lock (gate)
        {
            sourceFailures++;
            if (failures.Count < 32) failures.Add(IncludeNames ? failure : failure with { Name = null });
        }
        Warning(ReportWarningCategory.Reconstruction, ReportDiagnosticCode.ObjectReconstructionFailed, failure.Id, sourceKey: failure.SourceKey);
    }

    internal void ModelFailed(ReportModelFailure failure)
    {
        lock (gate)
        {
            modelFailureCount++;
            if (modelFailures.Count < 32) modelFailures.Add(failure);
        }
    }

    internal void EntryRead(string path, int count)
    {
        lock (gate) foreach (var package in packages.Where(p => p.Path == path)) package.Entries = count;
    }

    internal void Extracted(string path, IEnumerable<string> files)
    {
        var counts = Enum.GetValues<ReportAssetCategory>().ToDictionary(category => category, _ => 0);
        foreach (string file in files)
        {
            string extension = System.IO.Path.GetExtension(file).ToLowerInvariant();
            if (extension == ".meta") continue;
            counts[Category(extension)]++;
        }
        lock (gate) foreach (var package in packages.Where(p => p.Path == path)) package.Assets = counts;
    }

    internal static ReportAssetCategory Category(string extension) => extension switch
    {
        ".prefab" => ReportAssetCategory.Prefab, ".unity" => ReportAssetCategory.Scene,
        ".fbx" or ".obj" or ".dae" or ".gltf" or ".glb" or ".blend" => ReportAssetCategory.Model,
        ".mat" => ReportAssetCategory.Material,
        ".png" or ".jpg" or ".jpeg" or ".tga" or ".psd" or ".bmp" or ".exr" or ".hdr" or ".tif" or ".tiff" => ReportAssetCategory.Texture,
        ".anim" or ".controller" or ".overridecontroller" => ReportAssetCategory.Animation,
        ".cs" or ".dll" or ".js" => ReportAssetCategory.Script, _ => ReportAssetCategory.Other
    };

    internal ImportReportData Capture(Exception? error, ReportResult? result, ImportReportStage? stage = null)
    {
        var exceptions = ImportFailureReport.CaptureExceptions(error, out bool truncated);
        lock (gate)
        {
            var counts = new Dictionary<ReportWarningCategory, int>(warningCounts);
            var samples = warnings.ToList();
            void Add(ReportWarningCategory category, ReportDiagnosticCode code, int count)
            {
                if (count <= 0) return;
                counts[category] = counts.GetValueOrDefault(category) + count;
                if (samples.Count < 12) samples.Add(new(category, code));
            }
            if (result != null)
            {
                Add(ReportWarningCategory.MissingReferences, ReportDiagnosticCode.MissingMeshes, result.MissingMeshes);
                Add(ReportWarningCategory.MissingReferences, ReportDiagnosticCode.MissingMaterials, result.MissingMaterials);
                Add(ReportWarningCategory.MissingReferences, ReportDiagnosticCode.MissingNestedPrefabs, result.MissingNestedPrefabs);
                Add(ReportWarningCategory.UnsupportedFeatures, ReportDiagnosticCode.UnsupportedNestedPrefabs, result.UnsupportedNestedPrefabs);
                Add(ReportWarningCategory.UnsupportedFeatures, ReportDiagnosticCode.UnsupportedFeatures, result.UnsupportedFeatures);
            }
            return new(Environment, SessionId, DateTimeOffset.UtcNow, stage ?? Stage, exceptions,
                Array.Empty<ReportPackage>(), packageCount, result, counts, samples.ToArray(), sourceFailures, failures.ToArray(), IncludeNames, truncated,
                modelFailures.ToArray(), modelFailureCount, Guid.NewGuid());
        }
    }

    internal async Task<IReadOnlyList<ReportPackage>> ReadPackageFactsAsync(CancellationToken token)
    {
        return await CapturePackageReader()(token).ConfigureAwait(false);
    }

    internal Func<CancellationToken, Task<IReadOnlyList<ReportPackage>>> CapturePackageReader()
    {
        PackageObservation[] observations;
        lock (gate) observations = packages.Select(p => p.Copy()).ToArray();
        return async token =>
        {
            var result = new List<ReportPackage>();
            foreach (var package in observations) result.Add(await package.ReadAsync(IncludeNames, token).ConfigureAwait(false));
            return result.AsReadOnly();
        };
    }

    private sealed class PackageObservation
    {
        internal string Path { get; }
        private long? initialLength;
        private DateTime? initialWrite;
        internal int? Entries;
        internal IReadOnlyDictionary<ReportAssetCategory, int>? Assets;
        internal PackageObservation(string path)
        {
            Path = path;
            try
            {
                var file = new FileInfo(path);
                if (file.Exists) { initialLength = file.Length; initialWrite = file.LastWriteTimeUtc; }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // Metadata is optional. ReadAsync will retain an explicit unavailable status.
                initialLength = null;
            }
        }
        private PackageObservation(PackageObservation source)
        {
            Path = source.Path;
            initialLength = source.initialLength;
            initialWrite = source.initialWrite;
            Entries = source.Entries;
            Assets = source.Assets;
        }
        internal PackageObservation Copy() => new(this);

        internal async Task<ReportPackage> ReadAsync(bool names, CancellationToken token)
        {
            long? bytes = initialLength;
            PackageInputKind? kind = null;
            ReportFileStatus status;
            try
            {
                token.ThrowIfCancellationRequested();
                await using var file = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
                bytes = file.Length;
                byte[] header = new byte[PackageInputFormat.HeaderBytes];
                int count = await file.ReadAtLeastAsync(header, header.Length, false, token).ConfigureAwait(false);
                kind = PackageInputFormat.Detect(header.AsSpan(0, count));
                if (initialLength.HasValue && (initialLength != bytes || new FileInfo(Path).LastWriteTimeUtc != initialWrite))
                    status = ReportFileStatus.Changed;
                else
                {
                    status = ReportFileStatus.Available;
                }
            }
            catch (OperationCanceledException) { status = ReportFileStatus.Cancelled; }
            catch (FileNotFoundException) { status = ReportFileStatus.Missing; }
            catch (DirectoryNotFoundException) { status = ReportFileStatus.Missing; }
            catch (UnauthorizedAccessException) { status = ReportFileStatus.AccessDenied; }
            catch (Exception error) when (error is IOException or ArgumentException or NotSupportedException) { status = ReportFileStatus.ReadFailed; }
            return new(status, bytes, kind, Entries, Assets, Assets != null, names ? System.IO.Path.GetFileName(Path) : null);
        }
    }
}
