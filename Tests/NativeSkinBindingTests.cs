using UnityPackageImporter.Models;

internal static class NativeSkinBindingTests
{
    internal static void Register(Action<string, Action> test)
    {
        test("Native skin mapping uses node identity instead of same-named hierarchy objects", () =>
        {
            var joint = new object();
            var mapping = SkinBoneMapping.ResolveNative(new[] { "Head" },
                new Dictionary<string, object> { ["Head"] = joint },
                () => throw new Exception("Native mapping must not search the hierarchy."));
            Check(ReferenceEquals(mapping["Head"], joint), "Lost the engine's per-mesh joint identity.");
        });

        test("An incomplete native skin map fails rather than silently binding another rig", () =>
        {
            Reject(() => SkinBoneMapping.ResolveNative(new[] { "Head" },
                new Dictionary<string, object>(), () => new[] { ("Head", new object()) }));
            Reject(() => SkinBoneMapping.ResolveNative(new[] { "Head" },
                new Dictionary<string, object> { ["Head"] = null! }, () => new[] { ("Head", new object()) }));
        });

        test("Models without native bone maps resolve unique exact names", () =>
        {
            var hip = new object();
            var head = new object();
            var mapping = SkinBoneMapping.ResolveNative<object>(new[] { "Head", "Hips" }, null!,
                () => new[] { ("Hips", hip), ("Head", head) });
            Check(ReferenceEquals(mapping["Head"], head) && ReferenceEquals(mapping["Hips"], hip),
                "Legacy map did not preserve exact identities.");
            Reject(() => SkinBoneMapping.ResolveNative<object>(new[] { "Head" }, null!,
                () => new[] { ("Head", head), ("Head", hip) }));
            Reject(() => SkinBoneMapping.ResolveNative<object>(new[] { "Head" }, null!,
                () => new[] { ("Head 1", head) }));
        });

        test("Uninitialized and partially initialized skins cannot pass copy validation", () =>
        {
            // ImportNode has already created the renderer, but ImportModel's
            // later SetupBones stage has not run: a foreach-only check passed it.
            Reject(() => SkinBoneMapping.Validate(2, Array.Empty<object>(), _ => true));
            Reject(() => SkinBoneMapping.Validate(2, new[] { new object() }, _ => true));
            Reject(() => SkinBoneMapping.Validate(1, new[] { new object(), new object() }, _ => true));
        });

        test("Copied skins reject null, destroyed, and source-template bone references", () =>
        {
            var copyBone = new object();
            var templateBone = new object();
            Reject(() => SkinBoneMapping.Validate(2, new[] { copyBone, null! }, bone => bone == copyBone));
            Reject(() => SkinBoneMapping.Validate(2, new[] { copyBone, templateBone }, bone => bone == copyBone));
            Reject(() => SkinBoneMapping.Validate(1, new[] { copyBone }, _ => false));
            SkinBoneMapping.Validate(2, new[] { copyBone, copyBone }, bone => bone == copyBone);
        });

        test("Blendshape-only meshes may legitimately have no skin bones", () =>
        {
            SkinBoneMapping.Validate(0, Array.Empty<object>(), _ => false);
            var mapping = SkinBoneMapping.ResolveNative<object>(Array.Empty<string>(), null!,
                () => throw new Exception("A boneless mesh needs no bone search."));
            Check(mapping.Count == 0, "Invented bones for a blendshape-only renderer.");
        });
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new Exception("Invalid skin binding was accepted.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
