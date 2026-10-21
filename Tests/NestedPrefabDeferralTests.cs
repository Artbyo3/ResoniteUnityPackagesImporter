using UnityPackageImporter.Models;

internal static class NestedPrefabDeferralTests
{
    private const string Root = "0000000000000000000000000000000a";
    private const string Child = "0000000000000000000000000000000b";
    private const string Empty = "--- !u!1 &1\nGameObject:\n  m_Name: LogicRoot\n";
    private static string Nested(string guid) => $"--- !u!1001 &2\nPrefabInstance:\n  m_SourcePrefab: {{fileID: 100100000, guid: {guid}, type: 3}}\n";
    private static NestedPrefabCapabilities Scan(Dictionary<string, string> files) =>
        NestedPrefabCapabilities.Scan(Root, guid => files.GetValueOrDefault(guid));
    private static BoneAttachmentOperation Operation(string path, string id = "attach") => new()
    {
        OperationId = id,
        Source = new() { SourcePath = path, GameObjectFileId = 7 },
        SourceObjectFileId = 7, TargetObjectFileId = 8
    };
    private static ImportResult Result(NestedPrefabDeferrals deferred, AvatarBuildPlan? plan = null, int failures = 0) =>
        ImportResult.Evaluate(true, false, NativeAvatarReadiness.Ready, 0, 0, failures, plan,
            op => ImportResultScope.Operation(op, "SyntheticScene.unity", _ => ImportOwnership.Owned),
            unsupportedNestedPrefabs: deferred.InstanceIds.Count(),
            deferredNestedPrefab: op => deferred.ContainsSource(op.Source.SourcePath));

    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        test("Mesh-free plain prefab can defer a nested scene instance", () =>
        {
            var capability = Scan(new() { [Root] = Empty });
            var deferred = new NestedPrefabDeferrals();
            if (capability.CanDefer) deferred.Register(31, new[] { "Logic.prefab" });
            check(capability.CanDefer && deferred.ContainsInstance(31), "A mesh-free source can defer its scene instance.");
        });
        foreach (int classId in new[] { 33, 23, 137 })
        {
            test("Mesh-bearing component prevents deferral: " + classId, () =>
            {
                var capability = Scan(new() { [Root] = Empty + $"--- !u!{classId} &3\nComponent: {{}}\n" });
                check(!capability.CanDefer, "Each mesh-bearing class must retain hard-failure capability.");
            });
            test("Nested child mesh-bearing component prevents deferral: " + classId, () =>
            {
                var capability = Scan(new() { [Root] = Empty + Nested(Child),
                    [Child] = $"--- !u!{classId} &3\nComponent: {{}}\n" });
                check(!capability.CanDefer, "A parent cannot hide geometry in a nested child.");
            });
        }
        test("Self-reference and mutual prefab cycles terminate", () =>
        {
            var self = Scan(new() { [Root] = Empty + Nested(Root) });
            var pair = Scan(new() { [Root] = Nested(Child), [Child] = Nested(Root) });
            check(self.CanDefer && self.PrefabGuids.Count == 1 && pair.CanDefer && pair.PrefabGuids.Count == 2,
                "Each source must be scanned once even when it nests itself or an ancestor.");
            check(!Scan(new() { [Root] = Nested(Child), [Child] = Nested(Root) + "--- !u!23 &9\nMeshRenderer: {}\n" }).CanDefer,
                "Cycle detection must not hide a mesh later in the same source.");
        });
        test("Missing or unrecognizable nested sources are not proven mesh-free", () =>
        {
            check(!Scan(new() { [Root] = Nested(Child) }).CanDefer, "A missing nested dependency must not be deferred.");
            check(!Scan(new() { [Root] = "unrecognizable input" }).CanDefer, "Unknown source encoding must remain a failure.");
        });
        test("Block source references and component names are scanned structurally", () =>
        {
            var capability = Scan(new() { [Root] = "--- !u!1001 &2\nPrefabInstance:\n  m_SourcePrefab:\n    fileID: 100100000\n    guid: " + Child + "\n",
                [Child] = "--- !u!1 &1\nGameObject:\n  m_Name: MeshRenderer\n" });
            check(capability.CanDefer && capability.PrefabGuids.Count == 2,
                "Multiline references must recurse; a name is not a mesh-bearing component.");
        });
        test("Invalid YAML cannot establish mesh-free capability", () =>
        {
            bool rejected = false;
            try { Scan(new() { [Root] = "--- !u!1 &1\nGameObject: [\n" }); }
            catch (YamlDotNet.Core.YamlException) { rejected = true; }
            check(rejected, "Parse errors must escape rather than downgrade uncertain content.");
        });
        test("Deferred stripped objects never invoke instantiation or record failures", () =>
        {
            var deferred = new NestedPrefabDeferrals();
            deferred.Register(31, new[] { "Logic.prefab" });
            var failedObjects = new List<ulong>();
            var incomplete = new List<string>();
            int skipped = 0;
            foreach (ulong id in new ulong[] { 41, 42, 43 })
            {
                try
                {
                    deferred.InstantiateAsync(31, true, () => throw new InvalidDataException("Unexpanded object"),
                        () => skipped++).GetAwaiter().GetResult();
                }
                catch (InvalidDataException)
                {
                    failedObjects.Add(id);
                    incomplete.Add("Failed object");
                }
            }
            check(skipped == 3 && failedObjects.Count == 0 && incomplete.Count == 0,
                "The shared instantiation boundary must skip stripped objects before the failure handler.");
            check(!deferred.ShouldSkipObject(31, false) && !deferred.ShouldSkipObject(32, true),
                "Authored objects and unrelated instances must keep normal instantiation.");
        });
        test("Non-deferred object exceptions still reach the failure boundary", () =>
        {
            var deferred = new NestedPrefabDeferrals();
            bool failed = false;
            try { deferred.InstantiateAsync(32, true, () => throw new InvalidDataException("Real failure"),
                () => throw new Exception("Unexpected skip")).GetAwaiter().GetResult(); }
            catch (InvalidDataException) { failed = true; }
            check(failed, "A real instantiation failure must not be swallowed.");
        });
        test("Deferred nested operations are not executed or attributed as unknown", () =>
        {
            var deferred = new NestedPrefabDeferrals();
            deferred.Register(31, new[] { "Logic.prefab", "NestedLogic.prefab" });
            var plan = new AvatarBuildPlan();
            plan.Operations.Add(Operation("NestedLogic.prefab"));
            int executed = 0;
            AvatarPlanExecution.ExecuteAsync(plan, (_, _) => { executed++; return Task.CompletedTask; },
                deferNestedPrefab: op => deferred.ContainsSource(op.Source.SourcePath)).GetAwaiter().GetResult();
            var result = Result(deferred, plan);
            check(executed == 0 && plan.Operations[0].ExecutionState == AvatarOperationExecutionState.DeferredNestedPrefab &&
                plan.Operations[0].Diagnostics.Any(d => d.Severity == AvatarOperationSeverity.Warning && d.Code == "UNSUPPORTED_NESTED_PREFAB"),
                "Deferred work must have an explicit warning state without executing.");
            check(result.StructureReady && result.OperationCount == 0 && !result.Issues.Contains(ImportResultIssue.ConversionFailed) &&
                !result.Issues.Contains(ImportResultIssue.OwnershipUnresolved), "Deferred work is not required structural work or unknown ownership.");
        });
        test("Only unsupported nested prefab warnings permit both output actions", () =>
        {
            var deferred = new NestedPrefabDeferrals();
            deferred.Register(31, new[] { "Logic.prefab" });
            deferred.Register(32, new[] { "Logic.prefab" });
            var result = Result(deferred);
            check(result.StructureReady && result.CanPerform(ImportOutputAction.CreateAvatar) && result.CanPerform(ImportOutputAction.KeepObject),
                "The new warning must not invalidate otherwise ready output.");
            check(result.IsPartial && result.UnsupportedNestedPrefabCount == 2 &&
                result.Issues.SequenceEqual(new[] { ImportResultIssue.UnsupportedNestedPrefabs }), "Count instances rather than distinct files.");
        });
        test("Unsupported nested prefab warning plus real failure stays blocked", () =>
        {
            var deferred = new NestedPrefabDeferrals();
            deferred.Register(31, new[] { "Logic.prefab" });
            var result = Result(deferred, failures: 1);
            check(!result.StructureReady && !result.CanPerform(ImportOutputAction.CreateAvatar) && !result.CanPerform(ImportOutputAction.KeepObject) &&
                result.Issues.Contains(ImportResultIssue.ReconstructionFailed), "Real failures must retain the original structural policy.");
        });
        test("Unrelated differing-path operation remains unknown and blocked", () =>
        {
            var deferred = new NestedPrefabDeferrals();
            deferred.Register(31, new[] { "Logic.prefab" });
            var op = Operation("Unrelated.prefab");
            check(ImportResultScope.Operation(op, "SyntheticScene.unity", _ => ImportOwnership.Owned) == ImportOwnership.Unknown,
                "Different-source IDs cannot inherit scene ownership.");
            var plan = new AvatarBuildPlan();
            plan.Operations.Add(op);
            AvatarPlanExecution.ExecuteAsync(plan, (_, _) => Task.CompletedTask,
                deferNestedPrefab: candidate => deferred.ContainsSource(candidate.Source.SourcePath)).GetAwaiter().GetResult();
            var result = Result(deferred, plan);
            check(!result.StructureReady && result.Issues.Contains(ImportResultIssue.OwnershipUnresolved),
                "Only explicitly registered deferred source paths may bypass the ownership check.");
            op.ExecutionState = AvatarOperationExecutionState.DeferredNestedPrefab;
            check(!Result(deferred, plan).StructureReady, "A deferred state alone must not exempt an unrelated source.");
        });
        test("An active operation cannot execute against a deferred dependency", () =>
        {
            var plan = new AvatarBuildPlan();
            plan.Operations.Add(Operation("Logic.prefab", "deferred"));
            var active = Operation("SyntheticScene.unity", "active");
            active.DependsOnOperationIds.Add("deferred");
            plan.Operations.Add(active);
            int executed = 0;
            AvatarPlanExecution.ExecuteAsync(plan, (_, _) => { executed++; return Task.CompletedTask; },
                deferNestedPrefab: op => op.Source.SourcePath == "Logic.prefab").GetAwaiter().GetResult();
            check(executed == 0 && active.ExecutionState == AvatarOperationExecutionState.Failed &&
                active.Diagnostics.Any(d => d.Code == "DEFERRED_DEPENDENCY"), "Dependent work must not run against missing content.");
        });
        test("Installed result retains unsupported nested prefab count", () =>
        {
            var result = ImportResult.Evaluate(true, true, NativeAvatarReadiness.NotApplicable, 0, 0, 0,
                unsupportedNestedPrefabs: 2).ForInstalledCopy();
            check(result.StructureReady && result.UnsupportedNestedPrefabCount == 2 &&
                result.Issues.Contains(ImportResultIssue.UnsupportedNestedPrefabs), "Installed receipts must retain deferred-content warnings.");
        });
    }
}
