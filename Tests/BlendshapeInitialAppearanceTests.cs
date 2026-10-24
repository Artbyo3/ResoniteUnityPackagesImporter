using System.Globalization;
using UnityPackageImporter.Models;

internal static class BlendshapeInitialAppearanceTests
{
    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        test("Initial appearance cancellation is honored for an empty document", () =>
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            bool cancelled = false;
            try { BlendshapeInitialAppearance.Evaluate("", new Dictionary<long, BlendshapeAppearanceObject>(), new Dictionary<long, BlendshapeAppearanceRenderer>(), cancellation.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            check(cancelled, "Empty input ignored cancellation.");
        });
        Dictionary<long, BlendshapeAppearanceObject> Objects() => new()
        {
            [1] = new(1, 1, "Avatar", ""), [2] = new(2, 1, "Body", "Body"), [3] = new(3, 1, "Outfit", "Outfit"),
            [4] = new(4, 10, "Body", "Body"), [10] = new(10, 10, "OtherAvatar", "")
        };
        Dictionary<long, BlendshapeAppearanceRenderer> Skins(float body = 65) => new()
        {
            [20] = new(20, 2, new Dictionary<string, float> { ["Size"] = body, ["Smile"] = 25 }),
            [30] = new(30, 3, new Dictionary<string, float> { ["Size"] = 0, ["Fit"] = 0, ["Smile"] = 0 }),
            [40] = new(40, 4, new Dictionary<string, float> { ["Size"] = 90 })
        };
        test("Initial MA blendshape transfer resolves named renderer ownership without mutating snapshots", () =>
        {
            var skins = Skins();
            var result = BlendshapeInitialAppearance.Evaluate(Ma(), Objects(), skins);
            check(!result.HasErrors && result.Writes.Single().UnityWeight == 65 && result.Writes[0].RendererId == 30, "Source weight did not reach local shape.");
            check(skins[30].Weights["Size"] == 0 && skins[20].Weights["Size"] == 65, "Compilation mutated source snapshots.");
            check(result.Diagnostics.Any(diagnostic => diagnostic.Code == "INITIAL_APPEARANCE_ONLY"), "Runtime propagation limitation was hidden.");
        });
        test("Initial MA transfer rejects malformed remap key nodes", () =>
        {
            var result = BlendshapeInitialAppearance.Evaluate(Ma(remap: "  RemapCurveIsValid: 1\n  RemapCurve:\n    m_Curve: [broken]"), Objects(), Skins());
            check(result.HasErrors && result.Writes.Count == 0 && result.Diagnostics.Any(diagnostic => diagnostic.Code == "BLENDSHAPE_REMAP_INVALID"), "Malformed remap key silently became an identity transfer.");
        });
        test("Initial VRCF transfer rejects malformed and negative serialized versions", () =>
        {
            foreach (string version in new[] { "broken", "-1" })
            {
                var result = BlendshapeInitialAppearance.Evaluate(Fury().Replace("        version: 1", "        version: " + version), Objects(), Skins());
                check(result.HasErrors && result.Writes.Count == 0, "Invalid version silently used legacy semantics.");
            }
        });
        test("Initial MA transfer supports renamed destinations and quoted shape names", () =>
        {
            var result = BlendshapeInitialAppearance.Evaluate(Ma(local: "Fit"), Objects(), Skins());
            check(result.Writes.Single().Shape == "Fit" && result.Writes[0].UnityWeight == 65, "Destination rename was lost.");
        });
        test("Initial MA references resolve object identity before renderer validation", () =>
        {
            var objects = Objects();
            objects.Add(5, new(5, 1, "NoSkin", "NoSkin"));
            var invalidDirect = BlendshapeInitialAppearance.Evaluate(Ma(sourceObject: 5), objects, Skins());
            check(invalidDirect.HasErrors && invalidDirect.Writes.Count == 0, "A valid direct object without a renderer borrowed the path object's renderer.");
            var emptyPath = BlendshapeInitialAppearance.Evaluate(Ma(path: ""), objects, Skins());
            check(emptyPath.HasErrors && emptyPath.Writes.Count == 0, "Empty path incorrectly accepted a cached direct object.");
        });
        test("Initial MA empty bindings do not require a renderer", () =>
        {
            string yaml = Ma(owner: 1);
            yaml = yaml.Substring(0, yaml.IndexOf("  Bindings:", StringComparison.Ordinal)) + "  Bindings: []\n";
            var result = BlendshapeInitialAppearance.Evaluate(yaml, Objects(), Skins());
            check(!result.HasErrors && result.Writes.Count == 0, "A rendererless no-op was rejected.");
        });
        test("Engine-stripped source shape retains serialized weight for linked clothing", () =>
        {
            var source = BlendshapeInitialAppearance.CaptureSourceWeights(new[] { "Size", "Smile" }, new[] { 65f, 10f }, new Dictionary<string, float> { ["Smile"] = 25 });
            var skins = Skins();
            skins[20] = new(20, 2, source);
            var result = BlendshapeInitialAppearance.Evaluate(Ma(), Objects(), skins);
            check(!result.HasErrors && result.Writes.Single().UnityWeight == 65 && source["Smile"] == 25, "An empty source shape lost its authored synchronization value or overwrote a native default.");
        });
        test("Initial VRCF duplicate and malformed managed identities return atomic diagnostics", () =>
        {
            foreach (string extra in new[] { "400", "999", "broken" })
            {
                string yaml = Fury();
                string record = "    - rid: " + extra + "\n      type: {class: Orphan, ns: VF.Model.Feature}\n      data: {}\n";
                yaml += record;
                if (extra == "999") yaml += record;
                var result = BlendshapeInitialAppearance.Evaluate(yaml, Objects(), Skins());
                check(result.HasErrors && result.Writes.Count == 0 && result.Diagnostics.Any(item => item.Code == "BLENDSHAPE_MANAGED_REFERENCES_INVALID"), "Invalid managed-reference table escaped diagnostic preflight.");
            }
        });
        test("Initial MA transfer falls back to a unique avatar-relative source path", () =>
        {
            var result = BlendshapeInitialAppearance.Evaluate(Ma(sourceObject: 999), Objects(), Skins());
            check(!result.HasErrors && result.Writes.Single().UnityWeight == 65, "Valid declared path fallback did not resolve.");
        });
        test("Initial MA transfer cannot borrow a similarly named renderer from another avatar", () =>
        {
            var result = BlendshapeInitialAppearance.Evaluate(Ma(sourceObject: 4, path: "Absent"), Objects(), Skins());
            check(result.HasErrors && result.Writes.Count == 0 && result.Diagnostics.Any(diagnostic => diagnostic.Code == "BLENDSHAPE_SOURCE_UNRESOLVED"), "A cross-avatar source was borrowed.");
        });
        test("Initial MA blendshape remaps interpolate and extrapolate in Unity percentage units", () =>
        {
            string oneKey = "  RemapCurveIsValid: 1\n  RemapCurve:\n    m_Curve:\n    - time: 0\n      value: 42\n";
            var maOneKey = BlendshapeInitialAppearance.Evaluate(Ma(remap: oneKey), Objects(), Skins());
            check(!maOneKey.HasErrors && maOneKey.Writes.Single().UnityWeight == 42f && maOneKey.Links.Single().HasRemapCurve,
                "One-key remap curve must produce its constant value and mark HasRemapCurve.");

            string zeroKey = "  RemapCurveIsValid: 1\n  RemapCurve:\n    m_Curve: []\n";
            var maZeroKey = BlendshapeInitialAppearance.Evaluate(Ma(remap: zeroKey), Objects(), Skins());
            check(maZeroKey.HasErrors && maZeroKey.Writes.Count == 0 && maZeroKey.Diagnostics.Any(d => d.Code == "BLENDSHAPE_REMAP_INVALID"),
                "Enabled empty remap curve must be rejected.");

            string curve = "  RemapCurveIsValid: 1\n  RemapCurve:\n    m_Curve:\n    - time: 0\n      value: 100\n    - time: 100\n      value: 0\n";
            foreach (var pair in new[] { (Input: 25f, Expected: 75f), (Input: -50f, Expected: 150f), (Input: 150f, Expected: -50f) })
            {
                var result = BlendshapeInitialAppearance.Evaluate(Ma(remap: curve), Objects(), Skins(pair.Input));
                check(result.Writes.Single().UnityWeight == pair.Expected, "Remap did not preserve upstream extrapolation semantics.");
            }
        });
        test("Initial MA identity transfer preserves negative and over-range weights", () =>
        {
            foreach (float value in new[] { -50f, 150f })
                check(BlendshapeInitialAppearance.Evaluate(Ma(), Objects(), Skins(value)).Writes.Single().UnityWeight == value, "Identity clamped an intentional out-of-range weight.");
        });
        test("Initial MA invalid legacy remap flag preserves identity and rejects malformed boolean flags", () =>
        {
            string remap = "  RemapCurveIsValid: 0\n  RemapCurve:\n    m_Curve:\n    - time: 0\n      value: 100\n    - time: 100\n      value: 0\n";
            check(BlendshapeInitialAppearance.Evaluate(Ma(remap: remap), Objects(), Skins()).Writes.Single().UnityWeight == 65, "Uninitialized legacy curve was treated as active.");

            string malformedFlag = "  RemapCurveIsValid: 2\n  RemapCurve:\n    m_Curve:\n    - time: 0\n      value: 100\n    - time: 100\n      value: 0\n";
            var malformedResult = BlendshapeInitialAppearance.Evaluate(Ma(remap: malformedFlag), Objects(), Skins());
            check(malformedResult.HasErrors && malformedResult.Writes.Count == 0 && malformedResult.Links.Count == 0 &&
                malformedResult.Diagnostics.Any(d => d.Code == "BLENDSHAPE_REMAP_INVALID"),
                "Malformed RemapCurveIsValid flag silently became an identity transfer.");
        });
        test("Initial MA duplicate and nonfinite curve keys reject the entire write set", () =>
        {
            foreach (string time in new[] { "0", ".nan" })
            {
                string remap = "  RemapCurveIsValid: 1\n  RemapCurve:\n    m_Curve:\n    - time: 0\n      value: 0\n    - time: " + time + "\n      value: 100\n";
                var result = BlendshapeInitialAppearance.Evaluate(Ma(remap: remap), Objects(), Skins());
                check(result.HasErrors && result.Writes.Count == 0, "Invalid remap reached mutation output.");
            }
        });
        test("Initial MA missing named source or local shape is explicit", () =>
        {
            var result = BlendshapeInitialAppearance.Evaluate(Ma(local: "Missing"), Objects(), Skins());
            check(result.HasErrors && result.Writes.Count == 0, "Missing local shape was silently ignored.");
        });
        test("Initial synchronization rejects chained and cyclic binding graphs", () =>
        {
            string first = Ma();
            string second = Ma(component: 101, owner: 2, sourceObject: 3, path: "Outfit");
            var result = BlendshapeInitialAppearance.Evaluate(first + second, Objects(), Skins());
            check(result.HasErrors && result.Writes.Count == 0 && result.Diagnostics.Any(diagnostic => diagnostic.Code == "BLENDSHAPE_CHAIN_UNSUPPORTED"), "Unsupported chaining became order-dependent propagation.");
        });
        test("Initial synchronization rejects acyclic chains and self links explicitly", () =>
        {
            var objects = Objects();
            objects.Add(5, new(5, 1, "Hat", "Hat"));
            var skins = Skins();
            skins.Add(50, new(50, 5, new Dictionary<string, float> { ["Size"] = 0 }));
            foreach (string yaml in new[] { Ma() + Ma(component: 101, owner: 5, sourceObject: 3, path: "Outfit"), Ma(owner: 2) })
            {
                var result = BlendshapeInitialAppearance.Evaluate(yaml, objects, skins);
                check(result.HasErrors && result.Writes.Count == 0 && result.Diagnostics.Any(item => item.Code == "BLENDSHAPE_CHAIN_UNSUPPORTED"), "Unsupported chain or self-link was traversal-dependent.");
            }
        });
        test("Initial VRCF blank include rows remain harmless upstream no-ops", () =>
        {
            var result = BlendshapeInitialAppearance.Evaluate(Fury("includes: [{nameOnBase: '', nameOnLinked: ''}]"), Objects(), Skins());
            check(!result.HasErrors && result.Writes.Count == 2, "Blank include row prevented automatic mappings.");
        });
        test("Initial synchronization rejects conflicting writes rather than last-writer selection", () =>
        {
            string first = Ma();
            string second = Ma(component: 101).Replace("Blendshape: Size", "Blendshape: Smile").Replace("LocalBlendshape: ''", "LocalBlendshape: Size");
            var result = BlendshapeInitialAppearance.Evaluate(first + second, Objects(), Skins());
            check(result.HasErrors && result.Writes.Count == 0 && result.Diagnostics.Any(diagnostic => diagnostic.Code == "BLENDSHAPE_WRITE_CONFLICT"), "Conflicting weights were selected by traversal order.");
        });
        test("Initial VRCFury link compiles only reachable managed-reference features", () =>
        {
            var result = BlendshapeInitialAppearance.Evaluate(Fury(), Objects(), Skins());
            check(!result.HasErrors && result.Writes.Count == 2 && result.Writes.Single(write => write.Shape == "Size").UnityWeight == 65, "Reachable default includes were not compiled.");
            var orphan = BlendshapeInitialAppearance.Evaluate(Fury().Replace("content: {rid: 400}", "content: {rid: -2}"), Objects(), Skins());
            check(orphan.Writes.Count == 0 && orphan.Components.Count == 0, "Orphan managed-reference data was executed.");
        });
        test("Initial VRCFury excludes and explicit includes preserve precedence and reject malformed filter lists", () =>
        {
            string extra = "includeAll: 1\nexcludes: [{name: Smile}]\nincludes: [{nameOnBase: Size, nameOnLinked: Fit}]\n";
            var result = BlendshapeInitialAppearance.Evaluate(Fury(extra), Objects(), Skins());
            check(!result.HasErrors && result.Writes.Count == 1 && result.Writes[0].Shape == "Fit", "Exclusion or explicit mapping precedence was lost.");

            foreach (string badFilter in new[] { "includes: broken\n", "includes: [broken]\n", "includes: [{}]\n", "includes: [{unrelated: 1}]\n", "excludes: broken\n", "excludes: [broken]\n" })
            {
                var badResult = BlendshapeInitialAppearance.Evaluate(Fury(badFilter), Objects(), Skins());
                check(badResult.HasErrors && badResult.Writes.Count == 0 && badResult.Links.Count == 0 &&
                    badResult.Diagnostics.Any(d => d.Code == "BLENDSHAPE_BINDINGS_INVALID"),
                    "Malformed BlendShapeLink filter failed open: " + badFilter);
            }
        });
        test("Per-avatar blendshape evaluation isolates valid avatars from invalid bindings on other avatars", () =>
        {
            string validAvatarA = Ma(component: 100, owner: 3, sourceObject: 2, path: "Body", local: "Fit");
            string brokenAvatarBMa = Ma(component: 101, owner: 4, sourceObject: 4, path: "Missing", local: "Missing");
            string brokenAvatarBFury = Fury().Replace("&100", "&102").Replace("m_GameObject: {fileID: 3}", "m_GameObject: {fileID: 4}")
                .Replace("rid: 400", "rid: broken");
            string combined = validAvatarA + brokenAvatarBMa + brokenAvatarBFury;

            var avatarAResult = BlendshapeInitialAppearance.Evaluate(combined, Objects(), Skins(), avatarRootId: 1);
            check(!avatarAResult.HasErrors && avatarAResult.Writes.Count == 1 && avatarAResult.Links.Count == 1 &&
                avatarAResult.Writes[0].RendererId == 30 && avatarAResult.Writes[0].Shape == "Fit" && avatarAResult.Writes[0].UnityWeight == 65,
                "A broken blendshape component on avatar B blocked valid writes or links on avatar A.");

            var avatarBResult = BlendshapeInitialAppearance.Evaluate(combined, Objects(), Skins(), avatarRootId: 10);
            check(avatarBResult.HasErrors && avatarBResult.Writes.Count == 0 && avatarBResult.Links.Count == 0,
                "Avatar B's invalid blendshape bindings did not fail closed for avatar B.");
        });
        test("Per-avatar blendshape evaluation ignores components on unrelated non-avatar branches", () =>
        {
            string validAvatarA = Ma(component: 100, owner: 3, sourceObject: 2, path: "Body", local: "Fit");
            string unrelatedHierarchy =
                "--- !u!1 &999\nGameObject:\n  m_Name: UnrelatedProp\n" +
                "--- !u!4 &9990\nTransform:\n  m_GameObject: {fileID: 999}\n  m_Father: {fileID: 0}\n";
            string unrelatedComp = Ma(component: 900, owner: 999, sourceObject: 998, path: "Prop", local: "Fit");
            string combined = validAvatarA + unrelatedHierarchy + unrelatedComp;

            var avatarAResult = BlendshapeInitialAppearance.Evaluate(combined, Objects(), Skins(), avatarRootId: 1);
            check(!avatarAResult.HasErrors && avatarAResult.Writes.Count == 1 && avatarAResult.Links.Count == 1 &&
                avatarAResult.Writes[0].RendererId == 30 && avatarAResult.Writes[0].Shape == "Fit" && avatarAResult.Writes[0].UnityWeight == 65,
                "A blendshape component on an unrelated non-avatar branch blocked valid writes or links on avatar A.");
        });
        test("Initial VRCFury source-name matching uses exact then case-and-whitespace matching", () =>
        {
            var skins = Skins();
            skins[30] = new(30, 3, new Dictionary<string, float> { ["s i z e"] = 0 });
            var fuzzy = BlendshapeInitialAppearance.Evaluate(Fury(), Objects(), skins);
            check(fuzzy.Writes.Single().Shape == "s i z e", "Documented fuzzy fallback was not applied.");
            var exact = BlendshapeInitialAppearance.Evaluate(Fury("exactMatch: 1\n"), Objects(), skins);
            check(!exact.HasErrors && exact.Writes.Count == 0, "Exact matching used a fuzzy candidate.");
        });
        test("Initial VRCFury legacy object links migrate to a renderer identity", () =>
        {
            string yaml = Fury().Replace("version: 1", "version: 0").Replace("linkSkins: [{renderer: {fileID: 30}}]", "objs: [{fileID: 3}]");
            var result = BlendshapeInitialAppearance.Evaluate(yaml, Objects(), Skins());
            check(!result.HasErrors && result.Writes.Count == 2, "Legacy object references were not migrated.");
        });
        test("Initial VRCFury unknown serialized versions remain explicit", () =>
        {
            var result = BlendshapeInitialAppearance.Evaluate(Fury().Replace("version: 1", "version: 2"), Objects(), Skins());
            check(result.HasErrors && result.Writes.Count == 0, "Unknown serialized semantics were guessed.");
        });
        test("Initial VRCFury linked renderers cannot cross avatar ownership", () =>
        {
            var result = BlendshapeInitialAppearance.Evaluate(Fury().Replace("fileID: 30", "fileID: 40"), Objects(), Skins());
            check(result.HasErrors && result.Writes.Count == 0, "A different avatar was modified.");
        });
        test("Blendshape Sync and BlendShapeLink reject external GUID references colliding with local fileIDs", () =>
        {
            string maExternal = Ma().Replace("targetObject: {fileID: 2}", "targetObject: {fileID: 2, guid: 11111111111111111111111111111111}");
            var maResult = BlendshapeInitialAppearance.Evaluate(maExternal, Objects(), Skins());
            check(maResult.HasErrors && maResult.Writes.Count == 0 && maResult.Links.Count == 0 &&
                maResult.Diagnostics.Any(d => d.Code == "BLENDSHAPE_SOURCE_UNRESOLVED"),
                "External MA targetObject GUID was resolved to local object.");

            string furyExternal = Fury().Replace("linkSkins: [{renderer: {fileID: 30}}]", "linkSkins: [{renderer: {fileID: 30, guid: 11111111111111111111111111111111}}]");
            var furyResult = BlendshapeInitialAppearance.Evaluate(furyExternal, Objects(), Skins());
            check(furyResult.HasErrors && furyResult.Writes.Count == 0 && furyResult.Links.Count == 0 &&
                furyResult.Diagnostics.Any(d => d.Code == "BLENDSHAPE_TARGET_UNRESOLVED"),
                "External VRCF linkSkins GUID was resolved to local renderer.");
        });
        test("Initial VRCFury ambiguous base skins do not depend on source enumeration", () =>
        {
            var objects = Objects(); objects[5] = new(5, 1, "Body", "Copy");
            var skins = Skins(); skins[50] = new(50, 5, new Dictionary<string, float> { ["Size"] = 95 });
            var result = BlendshapeInitialAppearance.Evaluate(Fury(), objects, skins);
            check(result.HasErrors && result.Writes.Count == 0, "Equal-length base candidates were guessed.");
        });
        test("Initial blendshape compiler is culture invariant and cancellation aware", () =>
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                check(BlendshapeInitialAppearance.Evaluate(Ma(), Objects(), Skins(12.5f)).Writes.Single().UnityWeight == 12.5f, "Weights depended on locale.");
                using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
                bool cancelled = false;
                try { BlendshapeInitialAppearance.Evaluate(Ma(), Objects(), Skins(), cancellation.Token); }
                catch (OperationCanceledException) { cancelled = true; }
                check(cancelled, "Cancelled evaluation produced a write set.");
            }
            finally { CultureInfo.CurrentCulture = previous; }
        });
        test("Runtime blendshape links emit identity and remap metadata and reject multi-source conflicts", () =>
        {
            var maIdentity = BlendshapeInitialAppearance.Evaluate(Ma(local: "Fit"), Objects(), Skins());
            var link = maIdentity.Links.Single();
            check(!maIdentity.HasErrors && link.SourceRendererId == 20 && link.SourceShape == "Size" &&
                link.TargetRendererId == 30 && link.TargetShape == "Fit" && !link.HasRemapCurve, "Identity link metadata mismatch.");

            string oneKey = "  RemapCurveIsValid: 1\n  RemapCurve:\n    m_Curve:\n    - time: 0\n      value: 42\n";
            var maOneKey = BlendshapeInitialAppearance.Evaluate(Ma(remap: oneKey), Objects(), Skins());
            check(!maOneKey.HasErrors && maOneKey.Writes.Single().UnityWeight == 42f && maOneKey.Links.Single().HasRemapCurve,
                "One-key remap curve must produce its constant value and mark HasRemapCurve.");

            string zeroKey = "  RemapCurveIsValid: 1\n  RemapCurve:\n    m_Curve: []\n";
            var maZeroKey = BlendshapeInitialAppearance.Evaluate(Ma(remap: zeroKey), Objects(), Skins());
            check(maZeroKey.HasErrors && maZeroKey.Writes.Count == 0 && maZeroKey.Diagnostics.Any(d => d.Code == "BLENDSHAPE_REMAP_INVALID"),
                "Enabled empty remap curve must be rejected.");

            string curve = "  RemapCurveIsValid: 1\n  RemapCurve:\n    m_Curve:\n    - time: 0\n      value: 100\n    - time: 100\n      value: 0\n";
            var maRemap = BlendshapeInitialAppearance.Evaluate(Ma(remap: curve), Objects(), Skins());
            check(!maRemap.HasErrors && maRemap.Links.Single().HasRemapCurve, "Remap curve flag was not set on runtime link.");

            var skinsSameWeight = Skins(25);
            string conflictSameInitialWeight = Ma(component: 100) +
                Ma(component: 101).Replace("Blendshape: Size", "Blendshape: Smile").Replace("LocalBlendshape: ''", "LocalBlendshape: Size");
            var conflictResult = BlendshapeInitialAppearance.Evaluate(conflictSameInitialWeight, Objects(), skinsSameWeight);
            check(conflictResult.HasErrors && conflictResult.Links.Count == 0 &&
                conflictResult.Diagnostics.Any(d => d.Code == "BLENDSHAPE_WRITE_CONFLICT"),
                "Competing sources with equal initial weight escaped runtime link conflict check.");
        });
    }

    private static string Ma(long component = 100, long owner = 3, long sourceObject = 2, string path = "Body", string local = "", string remap = "") =>
        $"--- !u!114 &{component}\nMonoBehaviour:\n  m_GameObject: {{fileID: {owner}}}\n  m_Script: {{fileID: 11500000, guid: {AvatarPackageIndex.BlendshapeSyncScriptGuid}, type: 3}}\n  Bindings:\n  - ReferenceMesh:\n      referencePath: {path}\n      targetObject: {{fileID: {sourceObject}}}\n    Blendshape: Size\n    LocalBlendshape: '{local}'\n" + string.Join("\n", remap.TrimEnd().Split('\n').Where(line => line.Length > 0).Select(line => "  " + line)) + "\n";

    private static string Fury(string settings = "") =>
        $"--- !u!114 &100\nMonoBehaviour:\n  m_GameObject: {{fileID: 3}}\n  m_Script: {{fileID: 11500000, guid: {AvatarPackageIndex.VrcFuryFullScriptGuid}, type: 3}}\n  content: {{rid: 400}}\n  references:\n    version: 2\n    RefIds:\n    - rid: 400\n      type: {{class: BlendShapeLink, ns: VF.Model.Feature, asm: VRCFury}}\n      data:\n        version: 1\n        baseObj: Body\n        linkSkins: [{{renderer: {{fileID: 30}}}}]\n" + string.Join("\n", settings.TrimEnd().Split('\n').Where(line => line.Length > 0).Select(line => "        " + line)) + "\n";
}
