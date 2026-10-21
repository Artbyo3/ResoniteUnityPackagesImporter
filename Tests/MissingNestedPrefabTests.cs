using UnityPackageImporter.Models;

internal static class MissingNestedPrefabTests
{
    private const string Guid = "000000000000000000000000000000ab";
    private const string EmptyPrefab = "--- !u!1 &1\nGameObject:\n  m_Name: LogicRoot\n";
    private static NestedPrefabSourceIndex Index(string[]? prefabs = null, string[]? assets = null, string?[]? metadata = null) =>
        new(prefabs ?? Array.Empty<string>(), assets ?? Array.Empty<string>(), metadata ?? Array.Empty<string?>());
    private static ImportResult Result(int count, int failures = 0) =>
        ImportResult.Evaluate(true, false, NativeAvatarReadiness.Ready, 0, 0, failures, missingNestedPrefabs: count);

    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        test("Absent nested prefab source is classified as missing", () =>
        {
            check(Index().IsMissing(Guid), "No index contains the referenced source.");
            check(!Index().IsMissing(null) && !Index().IsMissing(""), "A missing source field is not evidence of an absent asset GUID.");
        });
        foreach (string index in new[] { "prefabs", "assets", "metadata" })
            test("Nested prefab source present in " + index + " is not missing", () =>
            {
                var present = index switch
                {
                    "prefabs" => Index(prefabs: new[] { Guid.ToUpperInvariant() }),
                    "assets" => Index(assets: new[] { Guid.ToUpperInvariant() }),
                    _ => Index(metadata: new[] { Guid.ToUpperInvariant() })
                };
                check(!present.IsMissing(Guid), "Presence in any index must retain existing behavior, case-insensitively.");
            });
        test("Metadata-only conflicting prefab declarations remain present", () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "nested-source-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string first = Path.Combine(directory, "First.prefab");
                string second = Path.Combine(directory, "Second.prefab");
                File.WriteAllText(first, EmptyPrefab);
                File.WriteAllText(second, EmptyPrefab + "--- !u!23 &2\nMeshRenderer: {}\n");
                File.WriteAllText(first + ".meta", "guid: " + Guid);
                File.WriteAllText(second + ".meta", "guid: " + Guid);
                string[] files = { first, first + ".meta", second, second + ".meta" };
                var loaded = UnityPackageAssetIndex.Build(files);
                check(!loaded.Assets.ContainsKey(Guid) && !loaded.Prefabs.ContainsKey(Guid), "Conflict must remove indexed assets.");
                var sources = new NestedPrefabSourceIndex(loaded.Prefabs.Keys, loaded.Assets.Keys,
                    files.Where(f => f.EndsWith(".meta")).Select(UnityPackageAssetIndex.ReadGuid));
                check(!sources.IsMissing(Guid), "Loaded metadata must prevent a conflict from becoming a missing-source warning.");
            }
            finally { Directory.Delete(directory, true); }
        });
        test("Present mesh-free source is deferred rather than missing", () =>
        {
            var deferred = new NestedPrefabDeferrals();
            if (!Index(prefabs: new[] { Guid }).IsMissing(Guid) &&
                NestedPrefabCapabilities.Scan(Guid, _ => EmptyPrefab).CanDefer)
                deferred.Register(31, new[] { "Logic.prefab" });
            check(deferred.InstanceIds.SequenceEqual(new ulong[] { 31 }) && !deferred.MissingInstanceIds.Any(),
                "The existing mesh-free outcome must retain its own count.");
        });
        test("Present mesh-bearing or unverifiable source remains a hard failure", () =>
        {
            foreach (string? yaml in new[] { EmptyPrefab + "--- !u!23 &2\nMeshRenderer: {}\n", null, "unrecognizable" })
                check(!Index(assets: new[] { Guid }).IsMissing(Guid) &&
                    !NestedPrefabCapabilities.Scan(Guid, _ => yaml).CanDefer,
                    "A known source cannot become missing or deferrable merely because it cannot be expanded.");
        });
        test("Missing instance skips stripped objects before the failure boundary", () =>
        {
            var deferred = new NestedPrefabDeferrals();
            deferred.RegisterMissing(31, Guid);
            var failed = new List<ulong>();
            var incomplete = new List<string>();
            int skipped = 0;
            foreach (ulong id in new ulong[] { 41, 42, 43 })
            {
                try
                {
                    deferred.InstantiateAsync(31, true, () => throw new InvalidDataException("Unresolved source"),
                        () => skipped++).GetAwaiter().GetResult();
                }
                catch (InvalidDataException e) { failed.Add(id); incomplete.Add(e.Message); }
            }
            check(skipped == 3 && failed.Count == 0 && incomplete.Count == 0,
                "Stripped transforms, objects and renderers must bypass the failure handler.");
            check(!deferred.ShouldSkipObject(31, false) && !deferred.ShouldSkipObject(32, true),
                "Authored objects and other instances must not be skipped.");
            check(!deferred.InstanceIds.Any() && deferred.MissingInstanceIds.Single() == 31 &&
                !deferred.ContainsSource("Unrelated.prefab"), "Missing instances must not change unsupported counts or operation ownership.");
        });
        test("Missing prefab warning identifies source GUID and instance", () =>
        {
            string warning = NestedPrefabDeferrals.MissingSourceWarning(31, Guid);
            check(warning.Contains(Guid) && warning.Contains("instance 31") && warning.Contains("missing from the package"),
                "The warning must identify what was not supplied.");
        });
        test("Only missing nested prefab warnings permit avatar creation and keeping", () =>
        {
            var result = Result(2);
            check(result.StructureReady && result.CanPerform(ImportOutputAction.CreateAvatar) && result.CanPerform(ImportOutputAction.KeepObject),
                "Missing content that was never supplied is non-structural.");
            check(result.IsPartial && result.MissingNestedPrefabCount == 2 &&
                result.Issues.SequenceEqual(new[] { ImportResultIssue.MissingNestedPrefabs }), "Count missing instances and expose their issue.");
        });
        test("Missing nested prefab warning plus real failure still blocks", () =>
        {
            var result = Result(1, failures: 1);
            check(!result.StructureReady && !result.CanPerform(ImportOutputAction.CreateAvatar) && !result.CanPerform(ImportOutputAction.KeepObject) &&
                result.Issues.Contains(ImportResultIssue.ReconstructionFailed), "Real failures retain the structural gate.");
        });
        test("Missing prefab under one scene avatar does not warn or block its sibling", () =>
        {
            var deferred = new NestedPrefabDeferrals();
            deferred.RegisterMissing(31, Guid);
            var first = new Node(null);
            var second = new Node(null);
            var parent = new Node(first);
            int Count(Node root) => deferred.MissingInstanceIds.Count(_ =>
                NestedPrefabInstanceScope.Affects(parent, node => Contains(root, node)));
            var affected = Result(Count(first));
            var sibling = Result(Count(second));
            check(affected.MissingNestedPrefabCount == 1 && sibling.MissingNestedPrefabCount == 0 &&
                !sibling.IsPartial && sibling.CanPerform(ImportOutputAction.CreateAvatar), "Parent identity must scope the warning to the selected avatar.");
            check(NestedPrefabInstanceScope.Affects<Node>(null, _ => false), "An unknown parent retains conservative warning scope.");
        });
        test("Missing source does not exempt unrelated differing-path operations", () =>
        {
            var deferred = new NestedPrefabDeferrals();
            deferred.RegisterMissing(31, Guid);
            var operation = new BoneAttachmentOperation
            {
                OperationId = "attach", Source = new() { SourcePath = "Unrelated.prefab", GameObjectFileId = 7 },
                SourceObjectFileId = 7, TargetObjectFileId = 8,
                ExecutionState = AvatarOperationExecutionState.Succeeded
            };
            var plan = new AvatarBuildPlan();
            plan.Operations.Add(operation);
            var result = ImportResult.Evaluate(true, false, NativeAvatarReadiness.Ready, 0, 0, 0, plan,
                op => ImportResultScope.Operation(op, "Scene.unity", _ => ImportOwnership.Owned),
                deferredNestedPrefab: op => deferred.ContainsSource(op.Source.SourcePath), missingNestedPrefabs: 1);
            check(!result.StructureReady && result.Issues.Contains(ImportResultIssue.OwnershipUnresolved),
                "Missing-source skipping must not change operation ownership.");
        });
        test("Installed copies retain missing nested prefab count", () =>
        {
            var source = ImportResult.Evaluate(true, true, NativeAvatarReadiness.NotApplicable, 0, 0, 0, missingNestedPrefabs: 2);
            var copy = source.ForInstalledCopy();
            check(copy.StructureReady && copy.MissingNestedPrefabCount == 2 && copy.Issues.Contains(ImportResultIssue.MissingNestedPrefabs),
                "Installed receipts must retain the warning count.");
        });
    }

    private sealed class Node(Node? parent)
    {
        internal Node? Parent { get; } = parent;
    }
    private static bool Contains(Node root, Node node)
    {
        for (Node? current = node; current != null; current = current.Parent)
            if (ReferenceEquals(root, current)) return true;
        return false;
    }
}
