using UnityPackageImporter.Models;

internal static class ImportResultTests
{
    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        ImportResult Result(AvatarBuildPlan? plan = null, int unsupported = 0, int materials = 0,
            int failures = 0, bool outfit = false, NativeAvatarReadiness native = NativeAvatarReadiness.Ready,
            Func<AvatarBuildOperation, ImportOwnership>? scope = null, bool unresolved = false, bool available = true,
            IEnumerable<string>? materialWarnings = null) =>
            ImportResult.Evaluate(available, outfit, native, materials, unsupported, failures, plan, scope, unresolved,
                materialWarnings: materialWarnings);
        void Blocked(ImportResult result)
        {
            foreach (var action in Enum.GetValues<ImportOutputAction>())
            {
                check(!result.CanPerform(action), "Invalid structure must not allow " + action);
                bool rejected = false;
                try { result.Require(action); } catch (InvalidOperationException) { rejected = true; }
                check(rejected, "Action boundary must reject invalid output.");
            }
        }
        test("Import result permits complete ordinary avatar", () =>
        {
            var r = Result();
            check(r.CanPerform(ImportOutputAction.CreateAvatar) && !r.IsPartial, "Complete avatar should be ready.");
        });
        test("Import result does not block optional unsupported behavior or missing materials", () =>
        {
            var r = Result(unsupported: 2, materials: 1);
            check(r.CanPerform(ImportOutputAction.CreateAvatar), "Optional behavior cannot disable valid structure.");
            check(r.IsPartial && r.UnsupportedFeatureCount == 2 && r.MissingMaterialAssets == 1, "Independent warning dimensions are retained.");
        });
        test("Import result surfaces material limitations without blocking structure", () =>
        {
            var r = Result(materialWarnings: new[] { "Dynamic emission scrolling is not supported", "MatCap unmasked only" });
            check(r.StructureReady, "Material limitations must not invalidate structural readiness.");
            check(r.CanPerform(ImportOutputAction.CreateAvatar), "Material limitations must not block avatar creation.");
            check(r.IsPartial, "Material limitations must mark the result partial.");
            check(r.Issues.Contains(ImportResultIssue.MaterialEffectsLimited), "MaterialEffectsLimited issue must be recorded.");
            check(r.MaterialWarnings.Count == 2 && r.MaterialWarningCount == 2, "Material warnings count mismatch.");
            check(r.MaterialWarnings[0] == "Dynamic emission scrolling is not supported", "First warning preserved.");
        });
        test("Import result deduplicates material warnings", () =>
        {
            var r = Result(materialWarnings: new[] { "Duplicate warning", "Duplicate warning", "Another warning" });
            check(r.MaterialWarnings.Count == 2, "Duplicate material warnings should be deduplicated.");
        });
        test("Import result reads recovery instead of retaining stale material status", () =>
        {
            var before = Result(materials: 1);
            var after = Result();
            check(before.IsPartial && !after.IsPartial, "Snapshots must not share mutable issues.");
        });
        test("World-visible material warnings redact paths before output and installed receipts", () =>
        {
            foreach (var path in new[] { @"C:\Users\PrivateUser\Cache\mask.png", "C:/Users/PrivateUser/Cache/mask.png",
                @"\\private-host\PrivateUser\mask.png", "/home/PrivateUser/mask.png", "file:///C:/Users/PrivateUser/mask.png" })
            {
                var warnings = new List<string> { $"Shirt.mat: Emission composition failed: Could not read '{path}'." };
                var source = Result(outfit: true, materialWarnings: warnings);
                warnings[0] = "Changed after snapshot";
                var installed = source.ForInstalledCopy();
                foreach (var result in new[] { source, installed })
                {
                    check(result.MaterialWarnings.Single() == "Shirt.mat: Emission composition failed: Could not read '<local path>'.",
                        "Receipts must preserve the useful failure without local paths or mutable source text.");
                    check(result.IsPartial && result.StructureReady, "Redaction must retain the material limitation status.");
                }
            }
        });
        test("Material warnings deduplicate after redaction", () =>
        {
            var result = Result(materialWarnings: new[] { @"Cannot read 'C:\Users\FirstPrivateUser\mask.png'.",
                @"Cannot read 'C:\Users\SecondPrivateUser\mask.png'." });
            check(result.MaterialWarningCount == 1, "Private paths must not create distinct world-visible warnings.");
        });
        test("Import result blocks missing source and failed reconstruction", () =>
        {
            Blocked(Result(available: false));
            Blocked(Result(failures: 1));
            Blocked(Result(unresolved: true));
        });
        foreach (var readiness in new[] { NativeAvatarReadiness.MissingRig, NativeAvatarReadiness.MultipleRigs,
            NativeAvatarReadiness.MissingIk, NativeAvatarReadiness.MissingViewpoint, NativeAvatarReadiness.AlreadyCreated })
            test("Import result separates structure from native readiness: " + readiness, () =>
            {
                var r = Result(native: readiness);
                check(r.StructureReady && !r.CanPerform(ImportOutputAction.CreateAvatar), "Native prerequisites must gate creation.");
                check(r.CanPerform(ImportOutputAction.KeepObject), "Native rig checks do not block ordinary scene publication.");
            });
        test("Import result never interprets an unexecuted valid plan as successful", () =>
        {
            var p = Plan();
            Blocked(Result(p));
            AvatarPlanExecution.ExecuteAsync(p, (_, _) => Task.CompletedTask).GetAwaiter().GetResult();
            var r = Result(p);
            check(r.CanPerform(ImportOutputAction.CreateAvatar) && r.CompletedOperationCount == 1, "Successful execution should establish completion.");
        });
        test("Import result carries diagnostic failure and unexecuted dependent work", () =>
        {
            var p = Plan(2);
            AvatarPlanExecution.ExecuteAsync(p, (op, _) =>
            {
                op.AddDiagnostic(AvatarOperationSeverity.Error, "Missing required target", "TARGET_NOT_FOUND");
                return Task.CompletedTask;
            }).GetAwaiter().GetResult();
            check(p.Operations[0].ExecutionState == AvatarOperationExecutionState.Failed, "Failed operation must be recorded.");
            check(p.Operations[1].ExecutionState == AvatarOperationExecutionState.Pending, "Subsequent operation was not executed.");
            Blocked(Result(p));
        });
        test("Import result records thrown execution failures", () =>
        {
            var p = Plan();
            AvatarPlanExecution.ExecuteAsync(p, (_, _) => throw new InvalidOperationException("Synthetic failure")).GetAwaiter().GetResult();
            Blocked(Result(p));
        });
        test("Import result execution cannot replay a mutated source or relabel it deferred", () =>
        {
            var p = Plan();
            int mutations = 0;
            Task Apply(AvatarBuildOperation _, CancellationToken __) { mutations++; return Task.CompletedTask; }
            AvatarPlanExecution.ExecuteAsync(p, Apply).GetAwaiter().GetResult();
            bool rejected = false;
            try { AvatarPlanExecution.ExecuteAsync(p, Apply).GetAwaiter().GetResult(); } catch (InvalidOperationException) { rejected = true; }
            check(rejected && mutations == 1, "Repeated execution must not mutate the staged source twice.");
            rejected = false;
            try { AvatarPlanExecution.DeferSupportedInstallation(p); } catch (InvalidOperationException) { rejected = true; }
            check(rejected, "Completed work cannot be relabeled as awaiting installation.");
        });
        test("Import result rejects completed operations with unresolved ownership", () =>
        {
            var p = Plan();
            AvatarPlanExecution.ExecuteAsync(p, (_, _) => Task.CompletedTask).GetAwaiter().GetResult();
            Blocked(Result(p, scope: _ => ImportOwnership.Unknown));
        });
        test("Import result preserves cancellation as cancellation", () =>
        {
            var p = Plan();
            bool cancelled = false;
            try { AvatarPlanExecution.ExecuteAsync(p, (_, _) => throw new OperationCanceledException()).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { cancelled = true; }
            check(cancelled && p.Operations[0].ExecutionState == AvatarOperationExecutionState.Cancelled, "Cancellation cannot become success.");
            Blocked(Result(p));
        });
        test("Import result isolates a completed avatar from a known failed sibling", () =>
        {
            var p = Plan(2);
            AvatarPlanExecution.ExecuteAsync(p, (op, _) =>
            {
                if (op == p.Operations[1]) throw new InvalidOperationException("Sibling failed");
                return Task.CompletedTask;
            }).GetAwaiter().GetResult();
            var r = Result(p, scope: op => op == p.Operations[0] ? ImportOwnership.Owned : ImportOwnership.Other);
            check(r.CanPerform(ImportOutputAction.CreateAvatar) && r.OperationCount == 1, "Known sibling failures must remain scoped.");
        });
        test("Import result blocks an avatar skipped after a sibling failure", () =>
        {
            var p = Plan(2);
            AvatarPlanExecution.ExecuteAsync(p, (_, _) => throw new InvalidOperationException("First failed")).GetAwaiter().GetResult();
            Blocked(Result(p, scope: op => op == p.Operations[1] ? ImportOwnership.Owned : ImportOwnership.Other));
        });
        test("Import result retains unattributed plan failures", () =>
        {
            var p = Plan(0);
            p.AddDiagnostic(AvatarOperationSeverity.Error, "Invalid dependency graph");
            Blocked(Result(p));
        });
        test("Import result reports supported outfit work as awaiting a target", () =>
        {
            var p = Plan();
            AvatarPlanExecution.DeferSupportedInstallation(p);
            var r = Result(p, outfit: true, native: NativeAvatarReadiness.NotApplicable);
            check(r.CanPerform(ImportOutputAction.InstallOutfit), "MA work can wait for the target avatar.");
            check(!r.CanPerform(ImportOutputAction.KeepObject) && r.CompletedOperationCount == 0, "Deferred is not converted.");
            Blocked(Result(p));
        });
        test("Import result does not defer structural features absent from the outfit installer", () =>
        {
            var p = new AvatarBuildPlan();
            p.Operations.Add(new BoneAttachmentOperation { Source = new() { Framework = "VRCFury" } });
            AvatarPlanExecution.DeferSupportedInstallation(p);
            Blocked(Result(p, outfit: true));
        });
        test("Import result records installation on the output copy without mutating its source", () =>
        {
            var p = Plan();
            AvatarPlanExecution.DeferSupportedInstallation(p);
            var source = Result(p, outfit: true, native: NativeAvatarReadiness.NotApplicable, materials: 1);
            var installed = source.ForInstalledCopy();
            check(installed.CompletedOperationCount == 1 && !installed.RequiresInstallation, "Installed copy should record completed work.");
            check(source.CompletedOperationCount == 0 && source.RequiresInstallation, "Reusable source must still await each target installation.");
            check(installed.MissingMaterialAssets == 1, "Successful attachment cannot erase material warnings.");
            var withMenuFailure = source.ForInstalledCopy(optionalControlsFailed: true);
            check(withMenuFailure.StructureReady && withMenuFailure.Issues.Contains(ImportResultIssue.OptionalControlsFailed),
                "Optional controls must be reported without misrepresenting a completed installation as failed.");
            var withMaterialLimitations = Result(p, outfit: true, native: NativeAvatarReadiness.NotApplicable,
                materialWarnings: new[] { "Clothes.mat: Second emission is not converted by the SDK-compatible path." }).ForInstalledCopy();
            check(withMaterialLimitations.MaterialWarnings.Count == 1, "Installed copy must retain material warnings.");
            check(withMaterialLimitations.Issues.Contains(ImportResultIssue.MaterialEffectsLimited), "Installed copy must retain MaterialEffectsLimited issue.");
        });
        test("Import result cannot hide failed conversion behind optional-feature warnings", () =>
        {
            var p = Plan();
            p.Operations[0].AddDiagnostic(AvatarOperationSeverity.Error, "Structural failure");
            Blocked(Result(p, unsupported: 3));
        });
        test("Import result scope checks source path before colliding signed IDs", () =>
        {
            var op = new BoneAttachmentOperation { Source = new() { SourcePath = "other.prefab", GameObjectFileId = -9 } };
            int lookups = 0;
            var scope = ImportResultScope.Operation(op, "scene.unity", _ => { lookups++; return ImportOwnership.Owned; });
            check(scope == ImportOwnership.Unknown && lookups == 0, "Foreign IDs must never resolve in a scene.");
        });
        test("Import result scope includes effects targeting the selected avatar", () =>
        {
            var op = new BoneAttachmentOperation { Source = new() { SourcePath = "scene.unity", GameObjectFileId = -9 }, SourceObjectFileId = -9, TargetObjectFileId = -10 };
            var scope = ImportResultScope.Operation(op, "scene.unity", id => id == -10 ? ImportOwnership.Owned : ImportOwnership.Other);
            check(scope == ImportOwnership.Owned, "Target-side effects also belong to the selected item.");
        });
        test("Import result scope distinguishes proven siblings from unresolved implicit targets", () =>
        {
            var explicitOp = new BoneAttachmentOperation { Source = new() { SourcePath = "scene.unity", GameObjectFileId = 1 }, SourceObjectFileId = 1, TargetObjectFileId = 2 };
            var implicitOp = new BoneAttachmentOperation { Source = explicitOp.Source, SourceObjectFileId = 1, TargetHumanoidBone = 0 };
            check(ImportResultScope.Operation(explicitOp, "scene.unity", _ => ImportOwnership.Other) == ImportOwnership.Other, "Known external effects may be excluded.");
            check(ImportResultScope.Operation(implicitOp, "scene.unity", _ => ImportOwnership.Other) == ImportOwnership.Unknown, "Unrecorded scene-wide target must not be guessed.");
        });
        test("Import measurements retain interrupted stages without double counting disposal", () =>
        {
            var metrics = new ImportMeasurements();
            var lease = metrics.Measure(ImportStage.ModelAssets, 3);
            try { using (lease) throw new OperationCanceledException(); } catch (OperationCanceledException) { }
            lease.Dispose();
            var snapshot = metrics.Snapshot();
            using (metrics.Measure(ImportStage.SourceReconstruction, 2)) { }
            check(snapshot.Count == 1 && metrics.Snapshot().Count == 2, "Snapshots must be stable and disposal idempotent.");
            check(snapshot[0].ScheduledItems == 3 && snapshot[0].Elapsed >= TimeSpan.Zero, "Stage timing must describe scheduled work.");
        });
    }

    private static AvatarBuildPlan Plan(int count = 1)
    {
        var p = new AvatarBuildPlan();
        for (int i = 0; i < count; i++) p.AddOperation(new BoneAttachmentOperation
        {
            Source = new() { Framework = "ModularAvatar", ComponentFileId = i + 1 },
            SourceObjectFileId = i + 10, TargetHumanoidBone = 0
        });
        return p;
    }
}
