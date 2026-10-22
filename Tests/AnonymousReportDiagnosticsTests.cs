using System.Buffers.Binary;
using System.Text;
using UnityPackageImporter.Models;

internal static class AnonymousReportDiagnosticsTests
{
    internal static ImportReportData Sample()
    {
        var sample = StructuredImportReportTests.Sample();
        var error = new ReportException("AssimpException", null, new[] { "Assimp.AssimpContext.ImportFile", "UnityPackageImporter.Models.FileImportTaskScene.ImportFileMeshes" },
            ReportFailureReason.FbxInvalidIdType);
        var model = new ReportModelFailure("synthetic-model", new(ReportModelFormat.FbxBinary, 65536, 7400), "5.0.0",
            new[] { ReportModelProcessing.JoinIdenticalVertices, ReportModelProcessing.LimitBoneWeights }, error, ReportModelPhase.Decode);
        return sample with { Stage = ImportReportStage.Models, Exceptions = new[] { error },
            Packages = new[] { new ReportPackage(ReportFileStatus.Available, 734003200, UnityPackageImporter.Extractor.PackageInputKind.Gzip, null,
                new Dictionary<ReportAssetCategory, int> { [ReportAssetCategory.Prefab] = 1, [ReportAssetCategory.Model] = 2,
                    [ReportAssetCategory.Material] = 3, [ReportAssetCategory.Texture] = 4 }, true) },
            Result = null, SourceFailures = 0, SourceFailureDetails = Array.Empty<ReportObjectFailure>(),
            WarningCounts = new Dictionary<ReportWarningCategory, int>(), Warnings = Array.Empty<ReportWarning>(),
            ModelFailures = new[] { model }, ModelFailureCount = 1, ReportId = Guid.Parse("00000000-0000-0000-0000-000000000002") };
    }

    internal static void Register(Action<string, Action> test)
    {
        const string idError = "Error importing file: FBX-Parser (TOK_DATA, offset 0x20) failed to parse ID, unexpected data type, expected L(ong) (binary)";
        test("Native FBX ID-type failure preserves its technical meaning without raw text", () =>
        {
            var reason = ReportFailureDetails.Assimp(idError);
            Check(reason == ReportFailureReason.FbxInvalidIdType && ReportFailureDetails.Describe(reason).Contains("64-bit integer"), "Native cause was lost.");
            Check(!ReportFailureDetails.Describe(reason).Contains("0x20"), "Original parser offset escaped.");
        });
        test("Native classifier never echoes names embedded in parser diagnostics", () =>
        {
            const string privateText = "synthetic-private-content";
            foreach (string message in new[] { idError + " " + privateText,
                "Error importing file: FBX-DOM (" + privateText + ") invalid data", privateText })
                Check(!ReportFailureDetails.Describe(ReportFailureDetails.Assimp(message)).Contains(privateText), "Native diagnostic echoed content.");
        });
        test("Unknown native text stays unknown rather than inventing a diagnosis", () =>
            Check(ReportFailureDetails.Assimp("unrecognized synthetic message") == ReportFailureReason.Unknown, "Unknown error was guessed."));
        test("Model-reader and end-of-file categories use fixed technical explanations", () =>
        {
            Check(ReportFailureDetails.Assimp("Error importing file: No suitable reader found for the file format of synthetic.fbx") == ReportFailureReason.ModelFormatUnsupported, "Reader failure not classified.");
            Check(ReportFailureDetails.Assimp("Unexpected end of file in synthetic-data") == ReportFailureReason.ModelUnexpectedEnd, "EOF failure not classified.");
        });
        test("Common managed failure categories retain causes while withholding private messages", () =>
        {
            var cases = new (Exception Error, ReportFailureReason Reason)[] {
                (new FileNotFoundException("private"), ReportFailureReason.FileMissing),
                (new UnauthorizedAccessException("private"), ReportFailureReason.AccessDenied),
                (new OutOfMemoryException("private"), ReportFailureReason.OutOfMemory),
                (new FormatException("private"), ReportFailureReason.InvalidNumber),
                (new InvalidDataException("private"), ReportFailureReason.InvalidStructuredData),
                (new ObjectDisposedException("private"), ReportFailureReason.ObjectDisposed),
                (new DllNotFoundException("private"), ReportFailureReason.NativeLibraryUnavailable) };
            foreach (var (error, reason) in cases)
                Check(ReportFailureDetails.Classify(error) == reason && !ReportFailureDetails.Describe(reason).Contains("private"), "Unsafe or incorrect managed cause.");
        });
        test("Async stack frames retain the caller class and logical method without runtime plumbing", () =>
        {
            Exception error = AsyncFailure().GetAwaiter().GetResult();
            string[] frames = ReportFailureDetails.Methods(error);
            Check(frames.Any(frame => frame.EndsWith("AnonymousReportDiagnosticsTests.AsyncFailure")), "Async logical caller was lost.");
            Check(frames.All(frame => !frame.EndsWith(".MoveNext") && !frame.EndsWith(".Throw") && !frame.Contains(".cs:")), "Runtime plumbing or source locations escaped.");
        });
        test("Anonymous aliases preserve same-source relationships and distinguish different sources", () =>
        {
            var aliases = new ReportAliases();
            Check(aliases.Object(-900, "source-a") == aliases.Object(-900, "source-a"), "Alias was unstable within report.");
            Check(aliases.Object(-900, "source-a") != aliases.Object(-900, "source-b"), "File-local identities collided.");
            Check(aliases.Object(0) == "none", "Null reference was presented as an object.");
        });
        test("Reports do not publish original IDs source paths or content fingerprints", () =>
        {
            var sample = StructuredImportReportTests.Sample();
            const long original = -7300049123467890;
            const string source = "private-content-source";
            var result = sample.Result! with { Objects = new[] { new ReportObjectFailure(ReportObjectType.Transform, 4, original, SourceKey: source) },
                OperationDetails = new[] { sample.Result!.OperationDetails[0] with { ComponentId = original, GameObjectId = original, SourceKey = source } } };
            var data = sample with { Result = result, SourceFailureDetails = result.Objects,
                Warnings = new[] { new ReportWarning(ReportWarningCategory.Reconstruction, ReportDiagnosticCode.ObjectReconstructionFailed, original, SourceKey: source) } };
            var text = ImportFailureReport.Build(data, new ReportSanitizer());
            Check(!text.Contains(original.ToString()) && !text.Contains(source) && !text.Contains("SHA-256:"), "An identifying value escaped.");
            Check(text.Contains("component Object 1; object Object 1"), "Cross-references no longer match.");
        });
        test("Changing original identities alone does not change an anonymous report", () =>
        {
            var sample = StructuredImportReportTests.Sample();
            string Render(long id) => ImportFailureReport.Build(sample with { Result = sample.Result! with {
                Objects = new[] { new ReportObjectFailure(ReportObjectType.Transform, 4, id) }, OperationDetails = Array.Empty<ReportOperation>() },
                SourceFailureDetails = Array.Empty<ReportObjectFailure>(), Warnings = Array.Empty<ReportWarning>() }, new ReportSanitizer());
            Check(Render(7100001234567890) == Render(8200009876543210), "Anonymous output retained original identity information.");
        });
        test("File size ranges avoid exact file-size fingerprints and handle extreme values", () =>
        {
            Check(ReportAliases.Size(734003200) == "512–1024 MiB", "Package size was not bucketed.");
            Check(ReportAliases.Size(1024) == "1–2 KiB" && ReportAliases.Size(0) == "empty" && ReportAliases.Size(null) == "unknown", "Size boundaries failed.");
            Check(ReportAliases.Size(long.MaxValue).StartsWith("at least"), "Size arithmetic overflowed.");
        });
        test("Model failure facts include anonymous format version processing and safe reason", () =>
        {
            var sample = StructuredImportReportTests.Sample();
            const string assetKey = "0123456789abcdef0123456789abcdef";
            var failure = new ReportModelFailure(assetKey, new(ReportModelFormat.FbxBinary, 1600000, 7400), "5.0.0",
                new[] { ReportModelProcessing.JoinIdenticalVertices }, new("AssimpException", null, new[] { "Assimp.AssimpContext.ImportFile" }, ReportFailureReason.FbxInvalidIdType));
            var report = ImportFailureReport.Build(sample with { Result = null, ModelFailures = new[] { failure }, ModelFailureCount = 1 }, new ReportSanitizer());
            foreach (string expected in new[] { "Model 1: FbxBinary", "Format version: 7400", "Model library version: 5.0.0", "Post-process: JoinIdenticalVertices", "64-bit integer" })
                Check(report.Contains(expected), "Missing model detail: " + expected);
            Check(!report.Contains(assetKey) && !report.Contains("1600000"), "Identifying model facts escaped.");
        });
        test("Concurrent model-failure snapshots preserve counts and bounded details", () =>
        {
            var context = new ImportReportContext(StructuredImportReportTests.Sample().Environment, Array.Empty<string>());
            Parallel.For(0, 40, value => context.ModelFailed(new(value.ToString(), new(ReportModelFormat.Fbx), "unknown",
                Array.Empty<ReportModelProcessing>(), new("AssimpException", null, Array.Empty<string>()))));
            var data = context.Capture(null, null);
            Check(data.ModelFailureCount == 40 && data.ModelFailures!.Count == 32, "Concurrent model details were lost or unbounded.");
        });
        test("Reports use independent tokens rather than exposing the import session or precise event time", () =>
        {
            var context = new ImportReportContext(StructuredImportReportTests.Sample().Environment, Array.Empty<string>());
            var first = context.Capture(null, null); var second = context.Capture(null, null);
            string text = ImportFailureReport.Build(first, new ReportSanitizer());
            Check(first.ReportId != Guid.Empty && first.ReportId != second.ReportId && !text.Contains(context.SessionId.ToString("N")), "Cross-report session identity escaped.");
            Check(!text.Contains(first.Time.ToUniversalTime().ToString("O")) && text.Contains("Date (UTC):"), "Precise event time escaped.");
        });
        test("Model header inspection reads only typed binary facts and retains no names", () => InTemp(path =>
        {
            byte[] header = new byte[128];
            "Kaydara FBX Binary  \0\x1a\0"u8.CopyTo(header); BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(23), 7400);
            Encoding.UTF8.GetBytes("synthetic-private-model-name").CopyTo(header, 27);
            File.WriteAllBytes(path, header);
            var facts = ReportModelFactsReader.Read(path);
            Check(facts.Format == ReportModelFormat.FbxBinary && facts.Version == 7400 && facts.Bytes == 128 && !facts.ToString().Contains("synthetic-private"), "Model header facts failed.");
        }));
        test("Malformed model version does not echo an arbitrary identifying header number", () => InTemp(path =>
        {
            byte[] header = new byte[27]; "Kaydara FBX Binary  \0\x1a\0"u8.CopyTo(header);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(23), uint.MaxValue); File.WriteAllBytes(path, header);
            var facts = ReportModelFactsReader.Read(path);
            Check(facts.Version == null && facts.UnrecognizedVersion, "Unrecognized header number was retained.");
        }));
        test("Missing model-header facts cannot prevent a diagnostic report", () => InTemp(path =>
        {
            var facts = ReportModelFactsReader.Read(path);
            Check(facts.Bytes == null && facts.Format == ReportModelFormat.Fbx, "Optional missing facts threw or guessed content.");
        }));
        test("Compact GitHub drafts preserve the model failure reason without its asset key", () =>
        {
            var sample = StructuredImportReportTests.Sample();
            var failure = new ReportModelFailure("private-model-key", new(ReportModelFormat.FbxBinary, 1600000, 7400), "5.0.0",
                Array.Empty<ReportModelProcessing>(), new("AssimpException", null, Array.Empty<string>(), ReportFailureReason.FbxInvalidIdType));
            var data = sample with { IncludeNames = true, Result = sample.Result! with { OperationDetails =
                Enumerable.Repeat(sample.Result!.OperationDetails[0] with { Name = new string('x', 400) }, 24).ToArray() },
                ModelFailures = new[] { failure }, ModelFailureCount = 1 };
            var draft = ImportIssueDraft.Build(data, new ReportSanitizer());
            string body = Uri.UnescapeDataString(draft.IssueUrl.Query);
            Check(!draft.FullReportIncluded && body.Contains("FbxInvalidIdType") && !body.Contains("private-model-key"), "Compact draft lost anonymous cause.");
        });
    }

    private static async Task<Exception> AsyncFailure()
    {
        await Task.Yield();
        try { throw new InvalidOperationException("synthetic-private-message"); }
        catch (InvalidOperationException error) { return error; }
    }
    private static void InTemp(Action<string> action)
    {
        string root = Path.Combine(Path.GetTempPath(), "anonymous-report-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { action(Path.Combine(root, "synthetic.fbx")); }
        finally { Directory.Delete(root, true); }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
