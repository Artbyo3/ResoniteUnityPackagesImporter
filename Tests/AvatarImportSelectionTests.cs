using UnityPackageImporter.Models;

internal static class AvatarImportSelectionTests
{
    public static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        test("Scene avatars keep independent roots even when their names match", () =>
        {
            var scene = new Node("Scene");
            var a = new Node("Avatar", scene);
            var b = new Node("Avatar", scene);
            var selected = AvatarImportSelection.IndependentRoots(new[] { a, b, a }, Contains);
            check(selected.SequenceEqual(new[] { a, b }), "Selection must deduplicate by identity, not name, without including the scene.");
        });
        foreach (bool reverse in new[] { false, true })
        {
            test("Nested descriptor ownership rejected before staging, reverse=" + reverse, () =>
            {
                var parent = new Node("Avatar");
                var child = new Node("Nested avatar", new Node("Intermediate", parent));
                try
                {
                    AvatarImportSelection.IndependentRoots(reverse ? new[] { child, parent } : new[] { parent, child }, Contains);
                    throw new Exception("Overlapping ownership was accepted.");
                }
                catch (InvalidOperationException) { }
            });
        }
        test("No scene descriptors produces no station avatar items", () =>
            check(AvatarImportSelection.IndependentRoots(Array.Empty<Node>(), Contains).Count == 0, "Invented avatar root."));

        foreach (bool outsideRenderer in new[] { false, true })
            test("Scene skin cannot cross avatar ownership, external renderer=" + outsideRenderer, () =>
            {
                var avatar = new Node("Avatar");
                var inside = new Node("Inside", avatar);
                var outside = new Node("Outside");
                var skins = new[] { (outsideRenderer ? outside : inside,
                    (IEnumerable<Node>)new[] { outsideRenderer ? inside : outside }) };
                try
                {
                    AvatarImportSelection.ValidateSkinOwnership(new[] { avatar }, skins, Contains);
                    throw new Exception("Cross-boundary skin would be broken by selection or station cleanup.");
                }
                catch (InvalidOperationException) { }
            });
        test("Independent avatars can share bone names without sharing bone objects", () =>
        {
            var a = new Node("Avatar");
            var b = new Node("Avatar");
            var skins = new[]
            {
                (new Node("Body", a), (IEnumerable<Node>)new[] { new Node("Hips", a) }),
                (new Node("Body", b), (IEnumerable<Node>)new[] { new Node("Hips", b) })
            };
            AvatarImportSelection.ValidateSkinOwnership(new[] { a, b }, skins, Contains);
        });

        test("Scene manifest scopes avatars and framework components using object ownership", () =>
        {
            var source = new AvatarPackageManifest
            {
                SourcePath = "scene.unity",
                Avatars = new[] { Avatar(-10, "scene.unity"), Avatar(20, "scene.unity"), Avatar(-10, "nested.prefab") },
                ModularAvatarComponents = new[]
                {
                    new ModularAvatarComponentDefinition { GameObjectFileId = -11, SourcePath = "scene.unity" },
                    new ModularAvatarComponentDefinition { GameObjectFileId = 21, SourcePath = "scene.unity" }
                },
                VrcFuryComponents = new[]
                {
                    new VrcFuryComponentDefinition { GameObjectFileId = -12, SourcePath = "scene.unity" },
                    new VrcFuryComponentDefinition { GameObjectFileId = -12, SourcePath = "nested.prefab" },
                    new VrcFuryComponentDefinition { GameObjectFileId = 22, SourcePath = "scene.unity" }
                }
            };
            var item = AvatarImportSelection.Scope(source, id => id < 0);
            check(item.Avatars.Count == 1 && item.Avatars[0].GameObjectFileId == -10, "Signed IDs or foreign reference scopes were lost.");
            check(item.ModularAvatarComponents.Count == 1 && item.VrcFuryComponents.Count == 1, "Sibling or foreign components escaped their scope.");
            check(source.Avatars.Count == 3 && source.VrcFuryComponents.Count == 3, "Scoping mutated the source manifest.");
        });

        foreach (bool descriptors in new[] { false, true })
            foreach (bool prepared in new[] { false, true })
                test($"Scene publication policy: descriptors={descriptors}, avatar/outfit outputs={prepared}", () =>
                    check(AvatarImportSelection.AutoCommitScenes(descriptors, prepared) == (!descriptors && !prepared),
                        "Only ordinary scene packages may be committed without selection, including when descriptors cannot resolve."));

        foreach (long typeId in new[] { 120943834L, 11500000L, 999L })
            test("Shared SDK assembly is not sufficient to classify avatar type " + typeId, () =>
            {
                var manifest = AvatarPackageIndex.ParseText(Component(typeId, "  seated: 1\n"));
                check(manifest.Avatars.Count == 0, "Unrelated SDK behaviour was classified as an avatar.");
            });
        test("Known SDK descriptor type does not require custom expressions", () =>
        {
            var manifest = AvatarPackageIndex.ParseText(Component(AvatarPackageIndex.VrcAvatarDescriptorScriptFileId, "  ViewPosition: {x: 0, y: 1.6, z: 0}\n"));
            check(manifest.Avatars.Count == 1, "Descriptor without expression assets was dropped.");
        });
        test("Descriptor schema fallback survives a changed SDK assembly identity", () =>
        {
            var yaml = Component(123, "  expressionsMenu: {fileID: 0}\n  expressionParameters: {fileID: 0}\n")
                .Replace(AvatarPackageIndex.VrcAvatarDescriptorScriptGuid, "different-sdk-assembly");
            check(AvatarPackageIndex.ParseText(yaml).Avatars.Count == 1, "Schema-compatible descriptor was lost.");
        });
    }

    private static VrcAvatarDefinition Avatar(long id, string source) => new() { GameObjectFileId = id, SourcePath = source };
    private sealed record Node(string Name, Node? Parent = null)
    {
        // Match engine object identity, even when names and parents coincide.
        public bool Equals(Node? other) => ReferenceEquals(this, other);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    private static bool Contains(Node parent, Node child)
    {
        for (Node? cursor = child; cursor != null; cursor = cursor.Parent)
            if (ReferenceEquals(cursor, parent)) return true;
        return false;
    }
    private static string Component(long typeId, string fields) =>
        "%YAML 1.1\n--- !u!1 &1\nGameObject:\n  m_Name: Model\n--- !u!114 &2\nMonoBehaviour:\n" +
        $"  m_GameObject: {{fileID: 1}}\n  m_Script: {{fileID: {typeId}, guid: {AvatarPackageIndex.VrcAvatarDescriptorScriptGuid}, type: 3}}\n" + fields;
}
