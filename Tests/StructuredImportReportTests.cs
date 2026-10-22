using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using UnityPackageImporter.Extractor;
using UnityPackageImporter.Models;
using YamlDotNet.Serialization;

internal static class StructuredImportReportTests
{
    private static readonly ReportEnvironment EnvironmentData = new("0.0.5", "1.2.3", "1.2.3", ReportOsFamily.Windows, "en");
    private static readonly DateTimeOffset Time = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    internal static ImportReportData Sample()
    {
        var result = new ReportResult(false, NativeAvatarReadiness.MissingIk,
            new[] { ImportResultIssue.ReconstructionFailed, ImportResultIssue.ConversionFailed, ImportResultIssue.MissingIk, ImportResultIssue.MissingMeshes },
            1, 1, 0, 0, 1, 0, 0, new(1, true, true, false, true, true, null, false, 1),
            new[] { new ReportObjectFailure(ReportObjectType.Transform, 4, 301, "Synthetic object", ImportOwnership.Owned) },
            new[] { new ReportOperation(AvatarBuildOperationKind.ArmatureMerge, AvatarOperationExecutionState.Failed,
                ImportOwnership.Owned, 401, 301, new[] { "SOURCE_NOT_FOUND" }, "Synthetic operation") },
            1, new[] { "SOURCE_NOT_FOUND" }, "Synthetic item");
        return new(EnvironmentData, Guid.Parse("00000000-0000-0000-0000-000000000001"), Time,
            ImportReportStage.Validation, new[] { new ReportException("InvalidDataException", null, new[] { "Reconstruct" }) },
            new[] { new ReportPackage(ReportFileStatus.Available, 256, PackageInputKind.Gzip, 3,
                new Dictionary<ReportAssetCategory, int> { [ReportAssetCategory.Prefab] = 1 }, true, "Synthetic package") },
            1, result, new Dictionary<ReportWarningCategory, int> { [ReportWarningCategory.Reconstruction] = 1, [ReportWarningCategory.MissingReferences] = 1 },
            new[] { new ReportWarning(ReportWarningCategory.Reconstruction, ReportDiagnosticCode.ObjectReconstructionFailed, 301, "Synthetic warning"),
                new ReportWarning(ReportWarningCategory.MissingReferences, ReportDiagnosticCode.MissingMeshes) },
            1, result.Objects);
    }

    internal static void Register(Action<string, Action> test)
    {
        test("Typed report renders all diagnostic sections and real readiness values", () =>
        {
            string report = Render(Sample());
            foreach (string value in new[] { "report format 2", "Mod version: 0.0.5", "Version matches last verified: yes",
                "OS family: Windows", "Date (UTC): 2026-01-01", "Report token:", "Locale: en", "Stage: Validation",
                "Method: Reconstruct", "Size range: under 1 KiB", "Detected format: Gzip", "Prefab: 1", "Archive entries observed: 3",
                "Issue: ReconstructionFailed", "Structure ready: no", "Native readiness: MissingIk", "Rig count: 1",
                "IK initialized: no", "Right hand reference: no", "Viewpoint resolves: unknown", "Object 1: Transform; Unity class 4",
                "state Failed; ownership Owned", "Code: SOURCE_NOT_FOUND", "Warning summary", "ObjectReconstructionFailed" })
                Check(report.Contains(value), "Missing section value: " + value);
        });
        test("Report excludes all explicit names by default", () =>
            Check(!Render(Sample()).Contains("Synthetic"), "Names escaped their opt-in fields."));
        test("Report opt-in adds explicit names without adding raw messages", () =>
        {
            string report = Render(Sample() with { IncludeNames = true });
            foreach (string name in new[] { "Synthetic package", "Synthetic item", "Synthetic object", "Synthetic operation", "Synthetic warning" })
                Check(report.Contains(name), "Opt-in name missing.");
        });
        test("Every report field passes the central personal-data sanitizer", () =>
        {
            string[] secrets = { "sample-account", "sample-machine", @"C:\Users\sample-account\profile", "/home/sample-account",
                "sample@example.invalid", "https://example.invalid/private", "www.example.invalid/private", @"\\sample-server\share\file", "file:///C:/private/file" };
            var sanitizer = new ReportSanitizer(secrets[0], secrets[1], secrets[2], secrets[3]);
            foreach (string secret in secrets)
            {
                var data = Sample() with { IncludeNames = true, Environment = EnvironmentData with { ModVersion = secret, ResoniteVersion = secret, Locale = secret },
                    Packages = new[] { Sample().Packages[0] with { Name = secret } },
                    Result = Sample().Result! with { Name = secret, PlanDiagnosticCodes = new[] { secret } },
                    Exceptions = new[] { new ReportException(secret, null, new[] { secret }) },
                    Warnings = new[] { new ReportWarning(ReportWarningCategory.Reporting, ReportDiagnosticCode.SnapshotUnavailable, Name: secret) },
                    SourceFailureDetails = new[] { new ReportObjectFailure(ReportObjectType.Other, null, 1, secret) } };
                Check(!ImportFailureReport.Build(data, sanitizer).Contains(secret, StringComparison.OrdinalIgnoreCase), "Personal data leaked.");
            }
            Check(sanitizer.Clean("SAMPLE-ACCOUNT sample-machine").Contains("<private>"), "Identity matching must ignore case.");
            Check(!sanitizer.Clean("prefix_sample-account_suffix").Contains("sample-account"), "Embedded identity text leaked.");
        });
        test("Unknown exception messages and injected warning text never enter a report", () =>
        {
            var context = Context();
            var report = Render(context.Capture(new InvalidOperationException("private-asset-name private-material-name"), null));
            Check(!report.Contains("private-asset-name") && !report.Contains("private-material-name"), "Free text leaked.");
        });
        test("Report works before station or result creation", () =>
        {
            string report = Render(Context().Capture(new IOException("untrusted"), null));
            Check(report.Contains("Unavailable (failure occurred before a result snapshot existed)."), "No-result report failed.");
        });
        test("Known package errors retain actionable safe English messages", () =>
        {
            string report = Render(Context().Capture(PackageInputFormat.Error(PackageInputFailure.Zip), null));
            Check(report.Contains("ZIP archive. Extract it first"), "Safe package error was lost.");
        });
        test("Exception capture includes inner and aggregate causes with a depth limit", () =>
        {
            Exception chain = new IOException("hidden");
            for (int i = 0; i < 20; i++) chain = new InvalidOperationException("hidden", chain);
            var captured = ImportFailureReport.CaptureExceptions(chain, out bool truncated);
            Check(captured.Count == 8 && truncated, "Exception chain was not bounded.");
            captured = ImportFailureReport.CaptureExceptions(new AggregateException(new IOException(), new ArgumentException()), out truncated);
            Check(captured.Count == 3 && !truncated, "Aggregate causes were omitted.");
        });
        test("Stack capture emits method names without source locations", () =>
        {
            var exceptions = ImportFailureReport.CaptureExceptions(Thrown(), out _);
            Check(exceptions[0].Methods.Any(method => method.EndsWith("." + nameof(Thrown))), "Method frame missing.");
            Check(!Render(Context().Capture(Thrown(), null)).Contains(".cs:"), "Stack source path leaked.");
        });
        test("Report size and detail samples are bounded", () =>
        {
            var sample = Sample();
            var huge = sample with { IncludeNames = true, Exceptions = Enumerable.Repeat(new ReportException("InvalidDataException", null,
                Enumerable.Repeat(new string('a', 100), 1000).ToArray()), 1000).ToArray(),
                Result = sample.Result! with { OperationDetails = Enumerable.Repeat(sample.Result!.OperationDetails[0] with { Name = new string('x', 10000) }, 1000).ToArray() } };
            Check(Render(huge).Length <= ImportFailureReport.MaximumCharacters, "Report exceeded its size budget.");
        });
        test("Per-import context isolates sessions and deduplicates repeated error objects", () =>
        {
            var first = Context(); var second = Context(); var error = new IOException();
            Check(first.SessionId != second.SessionId, "Sessions must have unrelated random IDs.");
            Check(first.TryBeginReport(error) && !first.TryBeginReport(error) && second.TryBeginReport(error), "Error dedup crossed imports.");
            first.Warning(ReportWarningCategory.Materials, ReportDiagnosticCode.MaterialEffectsLimited);
            Check(second.Capture(null, null).Warnings.Count == 0, "Warning data crossed imports.");
        });
        test("A report captures the active locale without mutating an earlier snapshot", () =>
        {
            var context = Context(); var before = context.Capture(null, null); context.SetLocale("fr");
            Check(before.Environment.Locale == "en" && context.Capture(null, null).Environment.Locale == "fr", "Locale snapshots were mutable or stale.");
        });
        test("Concurrent warning collection keeps exact counts and bounded samples", () =>
        {
            var context = Context();
            Parallel.For(0, 100, _ => context.Warning(ReportWarningCategory.Materials, ReportDiagnosticCode.MaterialEffectsLimited));
            var data = context.Capture(null, null);
            Check(data.WarningCounts[ReportWarningCategory.Materials] == 100 && data.Warnings.Count == 12, "Concurrent warning counts were lost.");
        });
        test("Result warnings supplement the session without raw material text", () =>
        {
            var result = Sample().Result!;
            var data = Context().Capture(null, result with { MissingMeshes = 2, MissingMaterials = 3, MissingNestedPrefabs = 1 });
            Check(data.WarningCounts[ReportWarningCategory.MissingReferences] == 6, "Result warning counts missing.");
        });
        test("Object failures retain internal signed identity while anonymizing output labels", () =>
        {
            var context = Context(); context.ObjectFailed(new(ReportObjectType.SkinnedMeshRenderer, 137, -9, "Synthetic object"));
            var data = context.Capture(null, null);
            Check(data.SourceFailures == 1 && data.SourceFailureDetails[0].Id == -9 && data.SourceFailureDetails[0].Name == null, "Failure metadata changed.");
            Check(Render(data).Contains("Object 1: SkinnedMeshRenderer; Unity class 137") && !Render(data).Contains("ID -9"), "Class metadata or anonymous identity missing.");
        });
        test("Object failures retain sanitized exception types and method frames without raw messages", () =>
        {
            var context = Context();
            context.ObjectFailed(new(ReportObjectType.Transform, 4, 7, Exceptions: ImportFailureReport.CaptureExceptions(Thrown(), out _)));
            string report = Render(context.Capture(null, null));
            Check(report.Contains("Object error: InvalidOperationException") && report.Contains("StructuredImportReportTests.Thrown") && !report.Contains("synthetic-private-error"), "Object error details lost privacy or diagnostics.");
        });
        test("A deferred operation is explicitly distinguished from an unresolved ownership failure", () =>
        {
            var sample = Sample();
            var result = sample.Result! with { OperationDetails = new[] { sample.Result!.OperationDetails[0] with {
                State = AvatarOperationExecutionState.DeferredNestedPrefab, Ownership = ImportOwnership.Unknown, DeferredByPolicy = true } } };
            Check(Render(sample with { Result = result }).Contains("Deferred by policy: yes"), "Deferred operation could be mistaken for a blocker.");
        });
        test("Unity YAML captures actual class tags and signed document IDs", () =>
        {
            var classes = new Dictionary<ulong, int?>();
            var deserializer = new DeserializerBuilder().WithTagMapping("tag:unity3d.com,2011:4", typeof(Dictionary<string, object>))
                .WithTagMapping("tag:unity3d.com,2011:137", typeof(Dictionary<string, object>)).Build();
            UnityYamlDocumentParser.Parse<Dictionary<string, object>>("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!4 &-9\nTransform: {}\n--- !u!137 &12\nSkinnedMeshRenderer: {}\n",
                deserializer, (id, value) => classes[id] = value);
            Check(classes[unchecked((ulong)-9L)] == 4 && classes[12] == 137, "Unity class metadata must come from the document tag.");
        });
        test("Failure facts retain format and extraction counts without hashing content", () => InTemp(root =>
        {
            string path = Path.Combine(root, "synthetic.unitypackage"); File.WriteAllBytes(path, Gzip(Tar()));
            var context = Context(path); context.EntryRead(path, 3);
            context.Extracted(path, new[] { "Assets/a.prefab", "Assets/a.prefab.meta", "Assets/b.fbx", "Assets/c.mat", "Assets/d.png", "Assets/e.anim", "Assets/f.cs", "Assets/g.bin", "Assets/h.unity" });
            var fact = context.ReadPackageFactsAsync(default).GetAwaiter().GetResult().Single();
            Check(fact.Status == ReportFileStatus.Available && fact.Bytes == new FileInfo(path).Length, "Input observation missing.");
            Check(fact.Format == PackageInputKind.Gzip && fact.Entries == 3 && fact.Assets!.Values.Sum() == 8 && fact.ExtractionComplete, "Extraction facts missing.");
            Check(fact.Name == null, "Package basename leaked by default.");
        }));
        test("Package facts distinguish missing input without preventing the report", () => InTemp(root =>
        {
            var context = Context(Path.Combine(root, "absent"));
            Check(context.ReadPackageFactsAsync(default).GetAwaiter().GetResult()[0].Status == ReportFileStatus.Missing, "Missing file was not explicit.");
        }));
        test("Package observation cancellation is reported as unavailable rather than an import error", () => InTemp(root =>
        {
            var context = Context(Path.Combine(root, "absent")); using var cancel = new CancellationTokenSource(); cancel.Cancel();
            Check(context.ReadPackageFactsAsync(cancel.Token).GetAwaiter().GetResult()[0].Status == ReportFileStatus.Cancelled, "Hash cancellation escaped.");
        }));
        test("Changed input retains an explicit observation status", () => InTemp(root =>
        {
            string path = Path.Combine(root, "input"); File.WriteAllText(path, "before"); var context = Context(path);
            File.WriteAllText(path, "changed length");
            var fact = context.ReadPackageFactsAsync(default).GetAwaiter().GetResult()[0];
            Check(fact.Status == ReportFileStatus.Changed, "Changed input was not marked explicitly.");
        }));
        test("Package observation snapshots do not observe later extraction mutations", () => InTemp(root =>
        {
            string path = Path.Combine(root, "input"); File.WriteAllText(path, "text"); var context = Context(path);
            context.EntryRead(path, 1); var read = context.CapturePackageReader(); context.EntryRead(path, 20);
            Check(read(default).GetAwaiter().GetResult()[0].Entries == 1, "Background writer observed mutable context.");
        }));
        test("Package read errors remain best-effort observation statuses", () => InTemp(root =>
        {
            string path = Path.Combine(root, "input"); File.WriteAllText(path, "text"); var context = Context(path);
            using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var fact = context.ReadPackageFactsAsync(default).GetAwaiter().GetResult()[0];
            Check(fact.Status is ReportFileStatus.ReadFailed or ReportFileStatus.AccessDenied, "Sharing failure escaped package facts.");
        }));
        test("Opt-in package facts include only a basename", () => InTemp(root =>
        {
            string path = Path.Combine(root, "synthetic.unitypackage"); File.WriteAllText(path, "text");
            var context = new ImportReportContext(EnvironmentData, new[] { path }, true);
            Check(context.ReadPackageFactsAsync(default).GetAwaiter().GetResult()[0].Name == "synthetic.unitypackage", "Package name policy changed.");
        }));
        test("Multiple input facts preserve input count while bounding detailed observations", () =>
        {
            var context = new ImportReportContext(EnvironmentData, Enumerable.Range(0, 20).Select(i => "absent-" + i));
            var data = context.Capture(null, null) with { Packages = context.ReadPackageFactsAsync(default).GetAwaiter().GetResult() };
            Check(data.PackageCount == 20 && data.Packages.Count == 16 && Render(data).Contains("Additional package facts omitted."), "Input facts were not bounded.");
        });
        test("Concurrent atomic report writes retain only the newest twenty owned files", () => InTemp(root =>
        {
            File.WriteAllText(Path.Combine(root, "notes.txt"), "keep");
            File.WriteAllText(Path.Combine(root, ImportReportStore.Prefix + "manual.txt"), "keep");
            Task.WhenAll(Enumerable.Range(0, 25).Select(i => ImportReportStore.WriteAsync(root, "report " + i, Time.AddSeconds(i)))).GetAwaiter().GetResult();
            var reports = Directory.GetFiles(root, "*.txt").Where(p => File.ReadAllText(p).StartsWith("report ")).ToArray();
            Check(reports.Length == 20 && reports.All(p => int.Parse(File.ReadAllText(p)[7..]) >= 5), "Retention removed the wrong files.");
            Check(File.Exists(Path.Combine(root, "notes.txt")) && File.Exists(Path.Combine(root, ImportReportStore.Prefix + "manual.txt")), "Retention touched unrelated files.");
            Check(!Directory.GetFiles(root, "*.tmp").Any(), "Atomic-write temporary file leaked.");
        }));
        test("A report write failure does not replace the original import failure", () => InTemp(root =>
        {
            string file = Path.Combine(root, "not-directory"); File.WriteAllText(file, "occupied");
            var original = new InvalidOperationException("original"); string? logged = null;
            bool written = ImportReportStore.SaveBestEffortAsync(async () => { await ImportReportStore.WriteAsync(file, "report", Time); }, type => logged = type).GetAwaiter().GetResult();
            Check(!written && logged != null && original.Message == "original", "Report errors escaped their boundary.");
        }));
        test("Report storage rejects oversize text without publishing a file", () => InTemp(root =>
        {
            Throws<InvalidDataException>(() => ImportReportStore.WriteAsync(root, new string('x', 129 * 1024), Time).GetAwaiter().GetResult());
            Check(!Directory.GetFiles(root).Any(), "Oversize report was published.");
        }));
        RegisterFormats(test);
    }

    private static void RegisterFormats(Action<string, Action> test)
    {
        var headers = new Dictionary<PackageInputKind, byte[]>
        {
            [PackageInputKind.Gzip] = new byte[] { 0x1f, 0x8b }, [PackageInputKind.Zip] = "PK\x03\x04"u8.ToArray(),
            [PackageInputKind.SevenZip] = new byte[] { 0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c },
            [PackageInputKind.Rar] = new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 1 },
            [PackageInputKind.PlainTar] = Tar()[..512], [PackageInputKind.WebPage] = Encoding.UTF8.GetBytes("\ufeff <!DOCTYPE html><html>"),
            [PackageInputKind.Text] = "plain text"u8.ToArray(), [PackageInputKind.Empty] = Array.Empty<byte>(),
            [PackageInputKind.Unknown] = new byte[] { 0, 1, 2, 3 }
        };
        foreach (var (kind, bytes) in headers)
        {
            test("Detect package header kind " + kind, () => Check(PackageInputFormat.Detect(bytes) == kind, "Wrong header classification."));
            if (kind == PackageInputKind.Gzip) continue;
            test("Reject non-package input with a safe actionable message for " + kind, () => InTemp(root =>
            {
                string file = Path.Combine(root, "synthetic-input"); File.WriteAllBytes(file, bytes);
                var error = Throws<InvalidDataException>(() => UnityPackageExtractor.Unpack(file, Path.Combine(root, "cache")));
                Check(PackageInputFormat.Failure(error).HasValue && !error.Message.Contains(root) && !error.Message.Contains("synthetic-input"), "Unsafe or unclassified error.");
                Check(error.Message.Contains("Import", StringComparison.OrdinalIgnoreCase) || error.Message.Contains("Download", StringComparison.OrdinalIgnoreCase), "No actionable fix.");
            }));
        }
        test("Detect empty ZIP and UTF16 web pages", () =>
        {
            Check(PackageInputFormat.Detect("PK\x05\x06"u8) == PackageInputKind.Zip, "Empty ZIP signature missing.");
            Check(PackageInputFormat.Detect(new byte[] { 0xff, 0xfe }.Concat(Encoding.Unicode.GetBytes("<html>" )).ToArray()) == PackageInputKind.WebPage, "Unicode HTML missing.");
        });
        test("Detect older tar headers without a ustar marker", () => Check(PackageInputFormat.Detect(Tar(TarEntryFormat.V7)[..512]) == PackageInputKind.PlainTar, "V7 tar was not detected."));
        test("Valid tiny package extracts unchanged and reports entry progress", () => InTemp(root =>
        {
            string file = Path.Combine(root, "input"); File.WriteAllBytes(file, Gzip(Tar())); int entries = 0;
            var files = UnityPackageExtractor.Unpack(file, Path.Combine(root, "cache"), entriesObserved: count => entries = count);
            Check(files.Count == 2 && entries == 3 && File.ReadAllText(files.Single(p => p.EndsWith(".prefab"))) == "synthetic payload", "Valid extraction changed.");
        }));
        foreach (int removed in new[] { 1, 8, 15 })
            test("Truncated gzip with missing bytes " + removed + " reports incomplete package", () => InTemp(root =>
            {
                var bytes = Gzip(Tar()); AssertCorrupt(root, bytes[..^removed]);
            }));
        test("Gzip with a bad CRC reports corruption", () => InTemp(root =>
        {
            var bytes = Gzip(Tar()); bytes[^8] ^= 0x40; AssertCorrupt(root, bytes);
        }));
        test("Gzip containing text rather than tar reports corruption", () => InTemp(root => AssertCorrupt(root, Gzip("text"u8.ToArray()))));
        test("Truncated tar inside valid gzip reports corruption", () => InTemp(root => AssertCorrupt(root, Gzip(Tar()[..600]))));
        test("Malformed gzip header reports corruption", () => InTemp(root => AssertCorrupt(root, new byte[] { 0x1f, 0x8b, 0xff, 0, 0, 0, 0, 0, 0, 0 })));
        test("Concatenated gzip members preserve valid package extraction", () => InTemp(root =>
        {
            var tar = Tar(); int split = tar.Length / 2;
            string file = Path.Combine(root, "input"); File.WriteAllBytes(file, Gzip(tar[..split]).Concat(Gzip(tar[split..])).ToArray());
            Check(UnityPackageExtractor.Unpack(file, Path.Combine(root, "cache")).Count == 2, "Valid multi-member gzip regressed.");
        }));
        test("Extraction cancellation and safety limits are not mislabeled as corruption", () => InTemp(root =>
        {
            string file = Path.Combine(root, "input"); File.WriteAllBytes(file, Gzip(Tar()));
            using var cancel = new CancellationTokenSource();
            Throws<OperationCanceledException>(() => UnityPackageExtractor.Unpack(file, Path.Combine(root, "cancelled"), cancellationToken: cancel.Token,
                entriesObserved: _ => cancel.Cancel()));
            var limit = Throws<InvalidDataException>(() => UnityPackageExtractor.Unpack(file, Path.Combine(root, "limited"), maxBytes: 128));
            Check(PackageInputFormat.Failure(limit) == null && limit.Message.Contains("limit"), "Size limit was relabeled as corruption.");
            var count = Throws<InvalidDataException>(() => UnityPackageExtractor.Unpack(file, Path.Combine(root, "counted"), maxEntries: 1));
            Check(PackageInputFormat.Failure(count) == null, "Entry limit was relabeled as corruption.");
        }));
        test("Missing package remains an access failure rather than a format error", () => InTemp(root =>
            Throws<FileNotFoundException>(() => UnityPackageExtractor.Unpack(Path.Combine(root, "missing"), Path.Combine(root, "cache")))));
    }

    private static ImportReportContext Context(params string[] paths) => new(EnvironmentData, paths);
    private static string Render(ImportReportData data) => ImportFailureReport.Build(data, new ReportSanitizer());
    private static Exception Thrown()
    {
        try { throw new InvalidOperationException("synthetic-private-error"); }
        catch (InvalidOperationException error) { return error; }
    }
    private static void AssertCorrupt(string root, byte[] bytes)
    {
        string file = Path.Combine(root, "input"); File.WriteAllBytes(file, bytes);
        var error = Throws<InvalidDataException>(() => UnityPackageExtractor.Unpack(file, Path.Combine(root, "cache")));
        Check(PackageInputFormat.Failure(error) == PackageInputFailure.Corrupted && error.Message.Contains("corrupted or incomplete"), "Missing corruption diagnostic.");
    }
    private static byte[] Tar(TarEntryFormat format = TarEntryFormat.Ustar)
    {
        using var stream = new MemoryStream();
        using (var writer = new TarWriter(stream, format, leaveOpen: true))
            foreach (var (name, value) in new[] { ("synthetic/asset", "synthetic payload"), ("synthetic/asset.meta", "guid: synthetic"), ("synthetic/pathname", "Assets/synthetic.prefab") })
            {
                using var data = new MemoryStream(Encoding.UTF8.GetBytes(value));
                TarEntry entry = format == TarEntryFormat.V7 ? new V7TarEntry(TarEntryType.V7RegularFile, name) : new UstarTarEntry(TarEntryType.RegularFile, name);
                entry.DataStream = data; writer.WriteEntry(entry);
            }
        return stream.ToArray();
    }
    private static byte[] Gzip(byte[] input)
    {
        using var stream = new MemoryStream();
        using (var gzip = new GZipStream(stream, CompressionMode.Compress, leaveOpen: true)) gzip.Write(input);
        return stream.ToArray();
    }
    private static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void InTemp(Action<string> action)
    {
        string root = Path.Combine(Path.GetTempPath(), "structured-report-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { action(root); }
        finally { Directory.Delete(root, true); }
    }
}
