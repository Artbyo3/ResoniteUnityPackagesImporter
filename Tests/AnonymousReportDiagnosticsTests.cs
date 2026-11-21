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
        foreach (var reason in new[] { ReportFailureReason.PrefabExpansionUnsupported, ReportFailureReason.PrefabParentMissing,
            ReportFailureReason.PrefabParentUnresolved, ReportFailureReason.PrefabInstanceUnresolved,
            ReportFailureReason.PrefabTargetUnresolved, ReportFailureReason.HierarchyGameObjectUnresolved,
            ReportFailureReason.HierarchyParentMissing, ReportFailureReason.HierarchyParentUnresolved,
            ReportFailureReason.SerializedSkinSourceUnsupported, ReportFailureReason.ModelSourceMissing,
            ReportFailureReason.ModelTemplateUnavailable, ReportFailureReason.SkinRendererUnresolved,
            ReportFailureReason.ModelImportInterrupted, ReportFailureReason.NativeMaterialMappingFailed,
            ReportFailureReason.SerializedSkinDecodeRejected })
            test("Explicit source failure reason survives anonymous capture: " + reason, () =>
            {
                var original = new InvalidDataException("synthetic-private-asset C:\\Users\\synthetic-account\\private.fbx");
                var tagged = ReportFailureDetails.Tag(original, reason);
                var captured = ImportFailureReport.CaptureExceptions(tagged, out bool truncated);
                Check(ReferenceEquals(original, tagged) && captured.Count == 1 && !truncated && captured[0].Reason == reason,
                    "Tagging must preserve exception identity and its exact typed reason.");
                string report = ImportFailureReport.Build(StructuredImportReportTests.Sample() with { Exceptions = captured }, new ReportSanitizer());
                Check(report.Contains("Reason: " + reason) && !report.Contains("synthetic-private") && !report.Contains("synthetic-account"),
                    "The fixed diagnostic was lost or exposed raw source text.");
            });
        test("Untyped reconstruction text is not guessed into a specific diagnosis", () =>
        {
            var error = new InvalidDataException("Engine model import was interrupted. PrefabParentMissing");
            Check(ReportFailureDetails.Classify(error) == ReportFailureReason.InvalidStructuredData, "Raw text was treated as authoritative evidence.");
        });
        test("Unknown or invalid explicit diagnostic tags are rejected", () =>
        {
            foreach (var reason in new[] { ReportFailureReason.Unknown, (ReportFailureReason)int.MaxValue })
            {
                bool rejected = false;
                try { ReportFailureDetails.Tag(new InvalidOperationException(), reason); }
                catch (ArgumentOutOfRangeException) { rejected = true; }
                Check(rejected, "An invalid explicit diagnosis was accepted.");
            }
        });
        foreach (var source in new (bool Included, string? Extension, ReportFailureReason Reason)[] {
            (false, ".asset", ReportFailureReason.ModelSourceMissing),
            (true, ".asset", ReportFailureReason.SerializedSkinSourceUnsupported),
            (true, ".ASSET", ReportFailureReason.SerializedSkinSourceUnsupported),
            (true, ".fbx", ReportFailureReason.ModelTemplateUnavailable),
            (true, null, ReportFailureReason.ModelTemplateUnavailable) })
            test($"Missing skin template classifies indexed source {source.Included} {source.Extension}", () =>
                Check(ReportFailureDetails.MissingSkinSource(source.Included, source.Extension) == source.Reason,
                    "An included serialized skin source was misdiagnosed as a missing dependency."));
        test("Failure totals preserve a rare cause after the detailed sample limit", () =>
        {
            var context = new ImportReportContext(StructuredImportReportTests.Sample().Environment, Array.Empty<string>());
            var common = new ReportException("InvalidDataException", null, Array.Empty<string>(), ReportFailureReason.SerializedSkinSourceUnsupported);
            for (int index = 0; index < 100; index++)
                context.ObjectFailed(new(ReportObjectType.SkinnedMeshRenderer, 137, index + 1, "private-mesh", Exceptions: new[] { common }));
            var before = context.Capture(null, null);
            context.ObjectFailed(new(ReportObjectType.Transform, 4, 1001, "private-transform", Exceptions: new[] {
                common with { Reason = ReportFailureReason.HierarchyParentMissing } }));
            var after = context.Capture(null, null);
            Check(after.SourceFailures == 101 && after.SourceFailureDetails.Count == 32 && after.SourceFailureGroups!.Sum(group => group.Count) == 101,
                "Failure counts were lost or detailed samples became unbounded.");
            Check(before.SourceFailureGroups!.Count == 1 && before.SourceFailureGroups[0].Count == 100 && after.SourceFailureGroups.Count == 2,
                "A later update mutated an earlier snapshot or lost the rare group.");
            string report = ImportFailureReport.Build(after, new ReportSanitizer());
            Check(report.Contains("Transform; reason HierarchyParentMissing; count 1") &&
                report.Contains("SkinnedMeshRenderer; reason SerializedSkinSourceUnsupported; count 100") &&
                !report.Contains("private-mesh") && !report.Contains("private-transform"), "Anonymous group totals lost rare evidence or leaked names.");
        });
        test("Concurrent failure grouping keeps exact totals independently of sample limits", () =>
        {
            var context = new ImportReportContext(StructuredImportReportTests.Sample().Environment, Array.Empty<string>());
            Parallel.For(0, 200, index => context.ObjectFailed(new(ReportObjectType.Transform, 4, index + 1)));
            var data = context.Capture(null, null);
            Check(data.SourceFailureGroups!.Single() == new ReportSourceFailureGroup(ReportObjectType.Transform, ReportFailureReason.Unknown, 200) &&
                data.SourceFailures == 200 && data.SourceFailureDetails.Count == 32, "Concurrent aggregation lost failures.");
        });
        test("Invalid enum values cannot create unbounded failure group keys", () =>
        {
            var context = new ImportReportContext(StructuredImportReportTests.Sample().Environment, Array.Empty<string>());
            for (int index = 1; index <= 40; index++) context.ObjectFailed(new((ReportObjectType)(1000 + index), null, index,
                Exceptions: new[] { new ReportException("Exception", null, Array.Empty<string>(), (ReportFailureReason)(1000 + index)) }));
            Check(context.Capture(null, null).SourceFailureGroups!.Single() == new ReportSourceFailureGroup(ReportObjectType.Other, ReportFailureReason.Unknown, 40),
                "Unknown external enum values created arbitrary aggregation keys.");
        });
        test("Local log correlates report snapshots without exposing session tokens in shareable reports", () =>
        {
            var context = new ImportReportContext(StructuredImportReportTests.Sample().Environment, new[] { "private-package" });
            var first = context.Capture(null, null); var second = context.Capture(null, null);
            string local = ImportFailureReport.LocalLogSummary(first);
            Check(local.Contains(first.ReportId.ToString("N")) && local.Contains(context.SessionId.ToString("N")) &&
                ImportFailureReport.LocalLogSummary(second).Contains(context.SessionId.ToString("N")) &&
                !local.Contains("private-package"), "The local log lost safe correlation or exposed a package name.");
            string shared = ImportFailureReport.Build(first, new ReportSanitizer());
            Check(!shared.Contains(context.SessionId.ToString("N")) && !shared.Contains(first.Time.ToUniversalTime().ToString("O")),
                "Local-only correlation or exact time escaped into the shareable report.");
        });
        test("Local item correlation explains readiness without adding names to shareable reports", () =>
        {
            var data = StructuredImportReportTests.Sample();
            var result = data.Result! with { StructureReady = false, NativeReadiness = NativeAvatarReadiness.Ready,
                Issues = new[] { ImportResultIssue.OwnershipUnresolved, ImportResultIssue.UnsupportedNestedPrefabs } };
            data = data with { Result = result, IncludeNames = false };
            string local = ImportFailureReport.LocalItemSummary(data, "private-avatar\r\nlocal-label");
            Check(local.Contains("private-avatar  local-label") && local.Contains(data.ReportId.ToString("N")) &&
                local.Contains("native readiness: Ready") && local.Contains("OwnershipUnresolved") && !local.Contains('\n') && !local.Contains('\r'),
                "Local item attribution lost the exact report, gate reasons or single-line boundary.");
            string shared = ImportFailureReport.Build(data, new ReportSanitizer());
            Check(!shared.Contains("private-avatar") && !shared.Contains("local-label"), "Local-only item identity escaped into the shareable report.");
        });
        test("Local item correlation preserves an unavailable result snapshot as unknown", () =>
        {
            string local = ImportFailureReport.LocalItemSummary(Sample() with { Result = null }, "synthetic-item");
            Check(local.Contains("structure ready: unknown") && local.Contains("native readiness: unknown") && local.Contains("issues: unknown"),
                "An unavailable snapshot was represented as ready or successfully reconstructed.");
        });
        test("Native material mapping success preserves the exact result and leaves no failure", () =>
        {
            var state = new ModelImportFailureState(); var expected = new object();
            Check(ReferenceEquals(state.MapMaterial(() => expected), expected) && state.MaterialFailure == null,
                "Instrumentation changed a successful material mapping.");
        });
        test("Native material mapping failure retains original identity type and stack through interruption", () =>
        {
            var state = new ModelImportFailureState();
            var original = new NullReferenceException("private-native-material");
            bool rethrown = false;
            try { state.MapMaterial<object>(() => throw original); }
            catch (NullReferenceException error) { rethrown = ReferenceEquals(error, original); }
            var captured = ImportFailureReport.CaptureExceptions(state.Interrupted(), out _);
            Check(rethrown && ReferenceEquals(state.MaterialFailure, original) && ReferenceEquals(state.Interrupted(), original) &&
                captured.Single().Type == nameof(NullReferenceException) && captured.Single().Reason == ReportFailureReason.NativeMaterialMappingFailed &&
                captured.Single().Methods.Any(method => method.Contains(nameof(ModelImportFailureState))),
                "A known native failure was replaced with a generic interruption or lost its caller.");
        });
        test("Native material fault capture does not invent a cause for unexplained interruption", () =>
        {
            var state = new ModelImportFailureState();
            Check(ReportFailureDetails.Classify(state.Interrupted()) == ReportFailureReason.ModelImportInterrupted && state.MaterialFailure == null,
                "An unexplained interruption was attributed to a material failure.");
        });
        test("Native cancellation remains cancellation and is not recorded as a mapper fault", () =>
        {
            var state = new ModelImportFailureState();
            var original = new OperationCanceledException(); bool rethrown = false;
            try { state.MapMaterial<object>(() => throw original); }
            catch (OperationCanceledException error) { rethrown = ReferenceEquals(error, original); }
            Check(rethrown && state.MaterialFailure == null, "Cancellation was misclassified or swallowed.");
        });
        test("Native material fault states isolate concurrent model imports", () =>
        {
            var failed = new ModelImportFailureState(); var other = new ModelImportFailureState();
            var original = new InvalidOperationException();
            try { failed.MapMaterial<object>(() => throw original); }
            catch (InvalidOperationException error) { Check(ReferenceEquals(error, original), "Mapper exception identity changed."); }
            Check(other.MaterialFailure == null && ReportFailureDetails.Classify(other.Interrupted()) == ReportFailureReason.ModelImportInterrupted,
                "A failure from one model was assigned to another model.");
        });
        test("Concurrent native material faults keep one original cause without replacing it", () =>
        {
            var state = new ModelImportFailureState();
            var errors = Enumerable.Range(0, 32).Select(_ => new InvalidOperationException()).ToArray();
            Parallel.ForEach(errors, original =>
            {
                try { state.MapMaterial<object>(() => throw original); }
                catch (InvalidOperationException error) { Check(ReferenceEquals(original, error), "Mapper rethrew a different exception."); }
            });
            Check(errors.Any(error => ReferenceEquals(error, state.MaterialFailure)) && ReferenceEquals(state.Interrupted(), state.MaterialFailure),
                "Fault attribution was not stable across concurrent callbacks.");
        });
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
