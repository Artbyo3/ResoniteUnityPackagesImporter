using UnityPackageImporter.Models;

internal static class ClothingFoundationTests
{
    public static void Register(Action<string, Action> test)
    {
        test("Legacy prefab-only outfit record cannot claim verified dependency parity", () => InTemp(root =>
        {
            var assets = WriteAssets(root);
            string legacyHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                File.ReadAllBytes(assets[PrefabGuid])));
            Check(Identity(assets).Compare(PrefabGuid, legacyHash) == OutfitInstallMatch.UpdateAvailable,
                "Legacy record with unknown dependencies was silently treated as current.");
        }));

        test("Outfit revision follows material, texture, mesh, and importer settings", () => InTemp(root =>
        {
            var assets = WriteAssets(root);
            var initial = Identity(assets);
            foreach (string guid in new[] { MaterialGuid, TextureGuid, MeshGuid })
            {
                string original = File.ReadAllText(assets[guid]);
                File.AppendAllText(assets[guid], "\nchanged");
                Check(Identity(assets).Compare(initial.PrefabGuid, initial.ContentHash) == OutfitInstallMatch.UpdateAvailable,
                    "A dependency-only change was mistaken for the installed revision.");
                File.WriteAllText(assets[guid], original);
            }
            File.AppendAllText(assets[MeshGuid] + ".meta", "\nscale: 0.01");
            Check(Identity(assets).ContentHash != initial.ContentHash, "Mesh importer settings did not affect revision.");
        }));

        test("Outfit revision is stable across locations and ignores unrelated assets", () => InTemp(root =>
        {
            var first = WriteAssets(Path.Combine(root, "a"));
            var second = WriteAssets(Path.Combine(root, "b"));
            second.Add(new string('e', 32), Path.Combine(root, "unrelated.txt"));
            File.WriteAllText(second[new string('e', 32)], "unrelated");
            Check(Identity(first).ContentHash == Identity(second).ContentHash,
                "Locations or unrelated assets changed the outfit revision.");
        }));

        test("Missing outfit dependencies change revision when supplied later", () => InTemp(root =>
        {
            var assets = WriteAssets(root);
            string texture = assets[TextureGuid];
            assets.Remove(TextureGuid);
            string missing = Identity(assets).ContentHash;
            assets.Add(TextureGuid, texture);
            Check(Identity(assets).ContentHash != missing, "Supplying an omitted texture did not change revision.");
        }));

        test("Outfit dependency traversal tolerates cycles and cancelled hashing", () => InTemp(root =>
        {
            var assets = WriteAssets(root);
            File.AppendAllText(assets[MaterialGuid], "\ncycle: {fileID: 1, guid: " + PrefabGuid + "}");
            Check(Identity(assets).ContentHash.Length == 64, "Cyclic dependency graph could not be fingerprinted.");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Throws<OperationCanceledException>(() => OutfitInstallIdentity.FromPrefab(
                PrefabGuid, assets[PrefabGuid], assets, cancelled.Token));
        }));

        test("An explicit attachment path cannot fall back to a matching leaf name", () =>
        {
            var nodes = new[] { new Target("Armature", "Other/Armature"), new Target("Hips", "Armature/Hips") };
            Check(Resolve(nodes, "Missing/Armature", "Armature") == null, "A wrong hierarchy was accepted.");
            Check(Resolve(nodes, "Armature/Hips", "Armature") == nodes[1], "Explicit target path did not resolve.");
        });

        test("Inferred attachment targets require one unique matching name", () =>
        {
            var first = new Target("Armature", "Armature");
            var duplicate = new Target("Armature", "Other/Armature");
            Check(Resolve(new[] { first }, "", "Armature") == first, "Unique inferred target did not resolve.");
            Check(Resolve(new[] { first, duplicate }, "", "Armature") == null, "Ambiguous inferred target was accepted.");
            Check(Resolve(new[] { first, duplicate }, "Armature", "Armature") == first,
                "An explicit root-relative path was confused by a duplicate leaf name.");
        });

        test("Merge Armature adapter distinguishes avatar root references from inferred names", () =>
        {
            var root = new Target("Avatar", "");
            var armature = new Target("Armature", "Armature");
            var candidates = new[] { root, armature };
            Target? ResolveMa(string path) => ModularAvatarMergePlanner.ResolveTarget(root, candidates,
                path, "Outfit_Armature", "Outfit_", "", node => node.Name, node => node.Path);
            Check(ResolveMa("$$$AVATAR_ROOT$$$") == root, "Avatar-root reference was treated as a bone name.");
            Check(ResolveMa("") == armature, "Absent path did not use the supported affix-aware inference.");
            Check(ResolveMa("Other/Armature") == null, "Explicit invalid path used inferred name.");
        });

        test("New outfit cannot reuse another outfit's owned bones or target paths", () =>
        {
            var ownedTip = new HierarchyNode("a-tip", "Tip");
            var ownedCape = new HierarchyNode("a-cape", "Cape", ownedTip);
            var hips = new HierarchyNode("native-hips", "Hips", ownedCape);
            var avatar = new HierarchyNode("native-armature", "Armature", hips);
            var sourceCape = new HierarchyNode("b-cape", "Cape", new HierarchyNode("b-tip", "Tip"));
            var source = new HierarchyNode("b-armature", "Armature", new HierarchyNode("b-hips", "Hips", sourceCape));
            var targetNodes = new Dictionary<string, HierarchyNode>();
            var targetSnapshot = BoneAttachmentHierarchy.Snapshot(avatar, node => node.Children,
                node => (node.Id, node.Name), targetNodes, new HashSet<HierarchyNode> { ownedCape });
            var sourceSnapshot = BoneAttachmentHierarchy.Snapshot(source, node => node.Children,
                node => (node.Id, node.Name), new Dictionary<string, HierarchyNode>());
            var plan = ModularAvatarMergePlanner.CreatePlan(sourceSnapshot, targetSnapshot);
            Check(plan.CanApply && plan.RetainedRoots.Single().SourceId == "b-cape" &&
                  plan.RetainedRoots.Single().TargetParentId == "native-hips",
                "Outfit B depended on outfit A's retained bone instead of owning its own branch.");
            Check(!targetNodes.ContainsKey("a-cape") && !targetNodes.ContainsKey("a-tip"),
                "An owned subtree escaped target exclusion.");
            var explicitTarget = BoneAttachmentTarget.Resolve(targetNodes.Values, "Hips/Cape", "Cape",
                node => node.Name, node => node == avatar ? "" : "Hips");
            Check(explicitTarget == null, "Explicit path into another outfit was silently accepted.");
            Check(hips.Children.Single() == ownedCape && ownedCape.Children.Single() == ownedTip,
                "Planning modified the existing outfit.");
        });

        test("Two stations cannot mutate the same avatar concurrently", () => Run(async () =>
        {
            var avatar = new object();
            var first = await AttachmentInstallationGate.EnterAsync(avatar, default);
            var waiting = AttachmentInstallationGate.EnterAsync(avatar, default);
            Check(!waiting.IsCompleted, "Second station entered an active avatar installation.");
            using (await AttachmentInstallationGate.EnterAsync(new object(), default)) { }
            first.Dispose();
            using var second = await waiting;
            first.Dispose();
            var third = AttachmentInstallationGate.EnterAsync(avatar, default);
            Check(!third.IsCompleted, "Disposing an old lease twice unlocked the current installation.");
            second.Dispose();
            using (await third) { }
        }));

        test("Cancelling a waiting outfit leaves the active installation locked", () => Run(async () =>
        {
            var avatar = new object();
            var active = await AttachmentInstallationGate.EnterAsync(avatar, default);
            using var cancellation = new CancellationTokenSource();
            var waiting = AttachmentInstallationGate.EnterAsync(avatar, cancellation.Token);
            cancellation.Cancel();
            try { await waiting; throw new Exception("Cancelled waiter acquired installation."); }
            catch (OperationCanceledException) { }
            var next = AttachmentInstallationGate.EnterAsync(avatar, default);
            Check(!next.IsCompleted, "Cancelled waiter released the active installation.");
            active.Dispose();
            using (await next) { }
        }));

        test("Attachment rollback owns detached clones and continues after a cleanup failure", () =>
        {
            var root = new object();
            var detachedBone = new object();
            var record = new object();
            var transaction = new OwnedAttachmentTransaction<object>();
            transaction.Track(root);
            transaction.Track(detachedBone);
            transaction.Track(detachedBone);
            transaction.Track(record);
            var visited = new List<object>();
            var failures = transaction.RollBack(item =>
            {
                visited.Add(item);
                if (item == record) throw new InvalidOperationException("simulated cleanup failure");
            });
            Check(visited.SequenceEqual(new[] { record, detachedBone, root }),
                "Rollback missed, duplicated, or incorrectly ordered owned objects.");
            Check(failures.Count == 1, "Cleanup failure was not reported.");
            transaction.RollBack(visited.Add);
            Check(visited.Count == 3, "Completed rollback repeated destruction.");
        });

        test("Committed outfit objects cannot be rolled back by station cancellation", () =>
        {
            var transaction = new OwnedAttachmentTransaction<object>();
            transaction.Track(new object());
            transaction.Commit();
            transaction.RollBack(_ => throw new Exception("Committed output was destroyed."));
            Throws<InvalidOperationException>(() => transaction.Track(new object()));
        });
    }

    private const string PrefabGuid = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string MaterialGuid = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string TextureGuid = "cccccccccccccccccccccccccccccccc";
    private const string MeshGuid = "dddddddddddddddddddddddddddddddd";
    private sealed record Target(string Name, string Path);
    private sealed class HierarchyNode(string id, string name, params HierarchyNode[] children)
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        public HierarchyNode[] Children { get; } = children;
    }
    private static Target? Resolve(IEnumerable<Target> nodes, string path, string fallback) =>
        BoneAttachmentTarget.Resolve(nodes, path, fallback, node => node.Name, node => node.Path);
    private static OutfitInstallIdentity Identity(Dictionary<string, string> assets) =>
        OutfitInstallIdentity.FromPrefab(PrefabGuid, assets[PrefabGuid], assets);

    private static Dictionary<string, string> WriteAssets(string root)
    {
        Directory.CreateDirectory(root);
        var assets = new Dictionary<string, string>
        {
            [PrefabGuid] = Path.Combine(root, "outfit.prefab"),
            [MaterialGuid] = Path.Combine(root, "cloth.mat"),
            [TextureGuid] = Path.Combine(root, "cloth.png"),
            [MeshGuid] = Path.Combine(root, "cloth.fbx")
        };
        File.WriteAllText(assets[PrefabGuid], "%YAML 1.1\nmaterial: {fileID: 1, guid: " + MaterialGuid +
            "}\nmesh: {fileID: 2, guid: " + MeshGuid + "}");
        File.WriteAllText(assets[MaterialGuid], "%YAML 1.1\ntexture: {fileID: 3, guid: " + TextureGuid + "}");
        File.WriteAllText(assets[TextureGuid], "texture pixels");
        File.WriteAllText(assets[MeshGuid], "model data");
        foreach (var asset in assets)
            File.WriteAllText(asset.Value + ".meta", "fileFormatVersion: 2\nguid: " + asset.Key);
        return assets;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static void Run(Func<Task> action) => Task.Run(action).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
    private static void InTemp(Action<string> action)
    {
        string root = Path.Combine(Path.GetTempPath(), "outfit-foundation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { action(root); }
        finally { Directory.Delete(root, true); }
    }
}
