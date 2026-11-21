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

        test("Unused missing skin bones retain null slots and mesh index alignment", () =>
        {
            var hip = new object();
            int reads = 0;
            var mapped = SkinBoneMapping.Resolve(new[] { "Kasa_before", "Hips", "Kasa_after" },
                new[] { ("Hips", hip) }, readInfluences: () =>
                {
                    reads++;
                    return (1, new[] { (1, 1f), (0, 0f), (2, 0f), (-1, 0f) });
                });
            Check(mapped.Length == 3 && mapped[0] == null && ReferenceEquals(mapped[1], hip) && mapped[2] == null,
                "Unused bones shifted or substituted the weighted bone's mesh/bind-pose index.");
            Check(reads == 1, "Native influence evidence was not shared by missing slots.");
        });

        test("Existing exact and Unity suffix skin bindings do not require influence evidence", () =>
        {
            var exact = new object();
            var suffix = new object();
            var mapped = SkinBoneMapping.Resolve(new[] { "Head", "Hips" },
                new[] { ("Head", exact), ("Head 1", suffix), ("Hips 1", suffix) },
                readInfluences: () => throw new Exception("Resolved bones need no missing-bone exception."));
            Check(ReferenceEquals(mapped[0], exact) && ReferenceEquals(mapped[1], suffix),
                "Exact precedence or Unity suffix resolution changed.");
        });

        foreach (int slot in Enumerable.Range(0, 4))
            foreach (float weight in new[] { 1f, float.Epsilon })
                test($"Missing weighted bone rejects influence slot {slot} with weight {weight}", () =>
                {
                    var influences = Enumerable.Repeat((-1, 0f), 8).ToArray();
                    influences[0] = (0, 1f);
                    influences[4 + slot] = (1, weight);
                    Reject(() => ResolveMissing(2, influences));
                });

        test("Missing skin bones reject unknown influence evidence", () =>
        {
            Reject(() => SkinBoneMapping.Resolve(new[] { "Hips", "Kasa" }, new[] { ("Hips", new object()) }));
            Reject(() => ResolveMissing(1, null!));
            Reject(() => ResolveMissing(0, Array.Empty<(int, float)>()));
            Reject(() => ResolveMissing(-1, Array.Empty<(int, float)>()));
            Reject(() => SkinBoneMapping.Resolve(new[] { "Hips", "Kasa" }, new[] { ("Hips", new object()) },
                readInfluences: () => throw new InvalidDataException("Native bindings are unavailable.")));
        });

        foreach (int count in new[] { 0, 3, 5, 7, 9 })
            test($"Missing skin bones reject incomplete or excess influence count {count}", () =>
                Reject(() => ResolveMissing(2, Enumerable.Repeat((0, 0f), count))));

        foreach (float weight in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f, -float.Epsilon })
            test($"Missing skin bones reject malformed native weight {weight}", () =>
                Reject(() => ResolveMissing(1, new[] { (0, 1f), (0, weight), (-1, 0f), (-1, 0f) })));

        foreach (var influence in new[] { (-2, 0f), (2, 0f), (2, 1f), (-1, 1f) })
            test($"Missing skin bones reject malformed native index/weight {influence}", () =>
                Reject(() => ResolveMissing(1, new[] { (0, 1f), influence, (-1, 0f), (-1, 0f) })));

        test("Unused-bone evidence never excuses duplicate mesh bone names", () =>
        {
            Reject(() => SkinBoneMapping.Resolve(new[] { "Kasa", "Kasa" }, Array.Empty<(string, object)>(),
                readInfluences: () => (1, Enumerable.Repeat((-1, 0f), 4))));
            Reject(() => SkinBoneMapping.Resolve(new[] { "Hips", "Hips" }, new[] { ("Hips", new object()) }));
        });

        test("Unused-bone evidence never excuses ambiguous authored bindings", () =>
        {
            Reject(() => SkinBoneMapping.Resolve(new[] { "Kasa" }, new[] { ("Kasa", new object()), ("Kasa", new object()) },
                readInfluences: () => (1, Enumerable.Repeat((-1, 0f), 4))));
            Reject(() => SkinBoneMapping.Resolve(new[] { "Kasa" }, new[] { ("Kasa 1", new object()), ("Kasa 1", new object()) },
                readInfluences: () => (1, Enumerable.Repeat((-1, 0f), 4))));
        });

        foreach (string name in new[] { null!, "", " " })
            test($"Malformed mesh bone name '{name}' cannot use unused-bone evidence", () =>
                Reject(() => SkinBoneMapping.Resolve(new[] { name }, Array.Empty<(string, object)>(),
                    readInfluences: () => (1, Enumerable.Repeat((-1, 0f), 4)))));

        test("Zero-weight evidence does not relax native copy validation", () =>
        {
            var mapped = ResolveMissing(1, new[] { (0, 1f), (1, 0f), (-1, 0f), (-1, 0f) });
            Reject(() => SkinBoneMapping.Validate(2, mapped, _ => true));
            Reject(() => SkinBoneMapping.ResolveNative(new[] { "Hips", "Kasa" },
                new Dictionary<string, object> { ["Hips"] = mapped[0] }, () => Array.Empty<(string, object)>()));
        });

        test("Blendshape-only meshes may legitimately have no skin bones", () =>
        {
            SkinBoneMapping.Validate(0, Array.Empty<object>(), _ => false);
            var mapping = SkinBoneMapping.ResolveNative<object>(Array.Empty<string>(), null!,
                () => throw new Exception("A boneless mesh needs no bone search."));
            Check(mapping.Count == 0, "Invented bones for a blendshape-only renderer.");
        });
    }

    private static object[] ResolveMissing(int vertexCount, IEnumerable<(int BoneIndex, float Weight)> influences) =>
        SkinBoneMapping.Resolve(new[] { "Hips", "Kasa" }, new[] { ("Hips", new object()) },
            readInfluences: () => (vertexCount, influences));

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
