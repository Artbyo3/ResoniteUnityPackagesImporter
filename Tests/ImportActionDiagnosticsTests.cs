using System.Text;
using UnityPackageImporter.Models;

internal static class ImportActionDiagnosticsTests
{
    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        test("Action-gate groups and replay locator survive repeated warning saturation", () =>
        {
            var plan = new AvatarBuildPlan();
            for (int index = 0; index < 200; index++) plan.AddDiagnostic(AvatarOperationSeverity.Warning, "limited", "200", "LIMITED_FEATURE");
            plan.AddDiagnostic(AvatarOperationSeverity.Info, "appearance-replay-0123456789abcdef0123456789abcdef.json", "123", "APPEARANCE_REPLAY_SAVED");
            plan.AddDiagnostic(AvatarOperationSeverity.Error, "Clip aaaaaaaa:7400000: property", "222", "STATIC_CLIP_FORMAT");
            var result = ImportResult.Evaluate(true, false, NativeAvatarReadiness.Ready, 0, 0, 0, plan);
            string evidence = ImportActionDiagnostics.LocalEvidence(new(ImportOutputAction.CreateAvatar, new[] { ImportActionBlocker.StructureNotReady }),
                result, plan, "HASH", Guid.Empty, "item", "source", Array.Empty<ulong>());
            check(evidence.Contains("Appearance replay; source context: 123") && evidence.Contains("code: LIMITED_FEATURE; count: 200") &&
                evidence.Contains("code: STATIC_CLIP_FORMAT; count: 1; sources: 222"), "Repeated details hid the dependency replay or actual failure groups.");
        });
        test("Resolved model graph proof survives saturated plan warning samples", () =>
        {
            var plan = new AvatarBuildPlan();
            for (int index = 0; index < 120; index++) plan.AddDiagnostic(AvatarOperationSeverity.Warning, "warning", code: "UNSUPPORTED_OPTION");
            plan.AddDiagnostic(AvatarOperationSeverity.Info, "Using immutable pre-conversion source hierarchy: 700 objects, 41 exact Transform aliases.",
                "123", "VRCFURY_INITIAL_SOURCE_GRAPH_RESOLVED");
            var result = ImportResult.Evaluate(true, false, NativeAvatarReadiness.Ready, 0, 1, 0, plan);
            var gate = ImportActionPolicy.EvaluateMainAction(result, true, false, true, true, false, false);
            string evidence = ImportActionDiagnostics.LocalEvidence(gate, result, plan, "HASH", Guid.Empty,
                "Synthetic avatar", "private-source.unity", Array.Empty<ulong>());
            check(evidence.Contains("Resolved model graph; source context: 123") && evidence.Contains("700 objects, 41 exact Transform aliases"),
                "Warning saturation concealed whether the authored graph reached evaluation.");
        });
        test("Unavailable diagnostic build hash cannot prevent imports or leak the failed path", () =>
        {
            string failure = "";
            string identity = ImportActionDiagnostics.ReadBuildIdentity(() => throw new IOException("private-source-path"), type => failure = type);
            check(identity == "unknown" && failure == "IOException", "Diagnostic I/O failure escaped or exposed raw failure text.");
        });
        test("Main button explanation matches existing action policy across native and station states", () =>
        {
            foreach (var native in Enum.GetValues<NativeAvatarReadiness>())
            foreach (bool avatar in new[] { false, true })
            foreach (bool outfit in new[] { false, true })
            foreach (bool enabled in new[] { false, true })
            foreach (bool item in new[] { false, true })
            foreach (bool created in new[] { false, true })
            foreach (bool failed in new[] { false, true })
            {
                var result = ImportResult.Evaluate(true, outfit, native, 0, 0, 0);
                var gate = ImportActionPolicy.EvaluateMainAction(result, avatar, outfit, enabled, item, created, failed);
                bool expected = enabled && item && !created && !failed && ImportActionPolicy.CanPerformMainAction(result, avatar, outfit);
                check(gate.Enabled == expected, "Explained main-button predicate diverged from the existing policy.");
            }
        });
        test("Native and station blockers explain a grey button even when structure is ready", () =>
        {
            var result = ImportResult.Evaluate(true, false, NativeAvatarReadiness.MissingIk, 0, 0, 0);
            var gate = ImportActionPolicy.EvaluateMainAction(result, true, false, false, true, false, true);
            check(gate.Blockers.SequenceEqual(new[] { ImportActionBlocker.StationDisabled, ImportActionBlocker.PreviousCreationFailed,
                ImportActionBlocker.NativeNotReady }), "Non-structural disabled conditions disappeared.");
            var absent = ImportActionPolicy.EvaluateMainAction(null!, true, false, true, false, false, false);
            check(absent.Blockers.Contains(ImportActionBlocker.MissingResult) && absent.Blockers.Contains(ImportActionBlocker.MissingItem), "Unknown result was presented as ready.");
        });
        test("State-only action blockers record no import error report", () =>
        {
            var outfit = ImportResult.Evaluate(true, true, NativeAvatarReadiness.Ready, 0, 0, 0);
            var outfitGate = ImportActionPolicy.EvaluateMainAction(outfit, false, true, true, true, false, false);
            check(outfitGate.Blockers.SequenceEqual(new[] { ImportActionBlocker.OutfitRequiresInstallAction }) &&
                !ImportActionPolicy.ReportsFailure(outfitGate), "An outfit waiting for its install action recorded an error report.");
            var ready = ImportResult.Evaluate(true, false, NativeAvatarReadiness.Ready, 0, 0, 0);
            var busyGate = ImportActionPolicy.EvaluateMainAction(ready, true, false, false, true, false, false);
            check(busyGate.Blockers.SequenceEqual(new[] { ImportActionBlocker.StationDisabled }) &&
                !ImportActionPolicy.ReportsFailure(busyGate), "A transient station-disabled state recorded an error report.");
            var createdGate = ImportActionPolicy.EvaluateMainAction(ready, true, false, true, true, true, false);
            check(createdGate.Blockers.SequenceEqual(new[] { ImportActionBlocker.AlreadyCreated }) &&
                !ImportActionPolicy.ReportsFailure(createdGate), "An already-created item recorded an error report.");
            var installGate = ImportActionPolicy.EvaluateMainAction(outfit, false, false, true, true, false, false);
            check(installGate.Blockers.SequenceEqual(new[] { ImportActionBlocker.RequiresInstallation }) &&
                !ImportActionPolicy.ReportsFailure(installGate), "An item requiring installation recorded an error report.");
            check(!ImportActionPolicy.ReportsFailure(ImportActionPolicy.EvaluateMainAction(ready, true, false, true, true, false, false)),
                "An enabled main action recorded an error report.");
        });
        test("Genuine action-gate failures still record an import error report", () =>
        {
            var failed = ImportResult.Evaluate(true, false, NativeAvatarReadiness.Ready, 0, 0, 1);
            var gate = ImportActionPolicy.EvaluateMainAction(failed, true, false, true, true, false, false);
            check(gate.Blockers.Contains(ImportActionBlocker.StructureNotReady) && ImportActionPolicy.ReportsFailure(gate),
                "A structure failure no longer recorded an error report.");
            var busy = ImportActionPolicy.EvaluateMainAction(failed, true, false, false, true, false, false);
            check(busy.Blockers.Contains(ImportActionBlocker.StationDisabled) && ImportActionPolicy.ReportsFailure(busy),
                "A state blocker suppressed the report of a genuine structure failure.");
        });
        test("Successful operations cannot conceal a plan-level error and warnings do not independently block", () =>
        {
            var plan = new AvatarBuildPlan();
            plan.AddDiagnostic(AvatarOperationSeverity.Warning, "warning", code: "UNSUPPORTED_OPTION");
            var warning = ImportResult.Evaluate(true, false, NativeAvatarReadiness.Ready, 0, 1, 0, plan);
            check(ImportActionPolicy.EvaluateMainAction(warning, true, false, true, true, false, false).Enabled, "A warning became a blocker.");
            plan.AddDiagnostic(AvatarOperationSeverity.Error, "bad source hierarchy", "123456", "INITIAL_HIERARCHY");
            var result = ImportResult.Evaluate(true, false, NativeAvatarReadiness.Ready, 0, 1, 0, plan);
            var gate = ImportActionPolicy.EvaluateMainAction(result, true, false, true, true, false, false);
            string evidence = ImportActionDiagnostics.LocalEvidence(gate, result, plan, "HASH", Guid.Empty,
                "Synthetic avatar", "private-source.unity", Array.Empty<ulong>());
            check(!gate.Enabled && evidence.Contains("Plan Error count: 1") && evidence.Contains("bad source hierarchy") && evidence.Contains("123456"),
                "The exact plan error was not preserved independently of operation success.");
        });
        test("Plan error details remain visible after warning saturation", () =>
        {
            var plan = new AvatarBuildPlan();
            for (int index = 0; index < 160; index++) plan.AddDiagnostic(AvatarOperationSeverity.Warning, "warning", code: "WARNING");
            plan.AddDiagnostic(AvatarOperationSeverity.Error, "blocking detail", "42", "BLOCKING_ERROR");
            var selected = ImportActionDiagnostics.Prioritize(plan.Diagnostics);
            check(selected.Count == 64 && selected[0].Code == "BLOCKING_ERROR" && plan.Diagnostics.Count == 161, "Warning sampling hid a blocking diagnostic or changed the plan.");
        });
        test("Local action evidence is stable across repeated observations and changes with gate state", () =>
        {
            var result = ImportResult.Evaluate(true, false, NativeAvatarReadiness.Ready, 0, 0, 0);
            string Evidence(bool failed) => ImportActionDiagnostics.LocalEvidence(
                ImportActionPolicy.EvaluateMainAction(result, true, false, true, true, false, failed), result, null,
                "HASH", Guid.Empty, "Item\nName", "source.unity", new ulong[] { 2, 2 });
            check(Evidence(false) == Evidence(false) && Evidence(false) != Evidence(true), "Repeated polling cannot deduplicate stable evidence or distinguish transitions.");
            check(Evidence(false).Contains("LOCAL-ONLY") && Evidence(false).Contains("Item: Item Name"), "Local privacy boundary or line flattening was lost.");
        });
        test("Shareable action diagnostics retain severity and aliases without raw source context", () =>
        {
            var sample = StructuredImportReportTests.Sample();
            var result = sample.Result! with { PlanErrorCount = 1, PlanWarningCount = 160,
                PlanDetails = new[] { new ReportPlanDiagnostic(AvatarOperationSeverity.Error, "BLOCKING_ERROR", 987654321) },
                MainActionGate = new(ImportOutputAction.CreateAvatar, new[] { ImportActionBlocker.StructureNotReady }) };
            string report = ImportFailureReport.Build(sample with { Result = result }, new ReportSanitizer());
            check(report.Contains("Plan errors: 1") && report.Contains("Plan diagnostic: Error; code BLOCKING_ERROR; source Object") &&
                report.Contains("Main action blocker: StructureNotReady") && !report.Contains("987654321"), "Shareable evidence lost severity or leaked a source identity.");
        });
        test("Shareable reports identify the loaded DLL without admitting arbitrary build identity text", () =>
        {
            var sample = StructuredImportReportTests.Sample();
            string valid = new('a', 64);
            string report = ImportFailureReport.Build(sample with { Environment = sample.Environment with { BuildSha256 = valid } }, new ReportSanitizer());
            check(report.Contains("Loaded mod SHA256: " + valid.ToUpperInvariant()), "Loaded-build identity was omitted.");
            string invalid = ImportFailureReport.Build(sample with { Environment = sample.Environment with { BuildSha256 = "private-package-file" } }, new ReportSanitizer());
            check(invalid.Contains("Loaded mod SHA256: unknown") && !invalid.Contains("private-package-file"), "Unvalidated build identity text escaped into a report.");
        });
        test("Local action evidence remains within storage budget for untrusted Unicode diagnostics", () =>
        {
            var plan = new AvatarBuildPlan();
            for (int index = 0; index < 200; index++) plan.AddDiagnostic(AvatarOperationSeverity.Error,
                new string('\u754c', 2000), new string('\u754c', 2000), new string('\u754c', 2000));
            var result = ImportResult.Evaluate(true, false, NativeAvatarReadiness.Ready, 0, 0, 0, plan);
            string evidence = ImportActionDiagnostics.LocalEvidence(new(ImportOutputAction.CreateAvatar, new[] { ImportActionBlocker.StructureNotReady }),
                result, plan, "HASH", Guid.Empty, "name", "source", Array.Empty<ulong>());
            check(Encoding.UTF8.GetByteCount(evidence) < 128 * 1024 && evidence.Contains("Plan Error count: 200"), "Bounded local diagnostics exceeded storage size or lost aggregate counts.");
        });
        test("Action-gate retention is independently configurable and rejects unbounded counts", () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "import-gate-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                for (int index = 0; index < 3; index++)
                    ImportReportStore.WriteAsync(directory, "local gate", DateTimeOffset.UtcNow.AddSeconds(index), retainedReports: 2).GetAwaiter().GetResult();
                check(Directory.GetFiles(directory).Length == 2, "Independent gate retention was not bounded.");
                bool rejected = false;
                try { ImportReportStore.WriteAsync(directory, "bad", DateTimeOffset.UtcNow, retainedReports: 0).GetAwaiter().GetResult(); }
                catch (ArgumentOutOfRangeException) { rejected = true; }
                check(rejected && Directory.GetFiles(directory).Length == 2, "Invalid retention mutated stored evidence.");
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
    }
}
