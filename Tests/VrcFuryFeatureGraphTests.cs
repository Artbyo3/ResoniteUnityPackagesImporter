using UnityPackageImporter.Models;
using YamlDotNet.RepresentationModel;

internal static class VrcFuryFeatureGraphTests
{
    public static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        test("VRCFury orphan table entries never create structural operations", () =>
        {
            foreach (string roots in new[] { "", "content: {rid: -2}", "content: {rid: 400}" })
            {
                string yaml = Fixture(roots, Link(300) + Record(400, "Toggle", "VF.Model.Feature", "state: {rid: 401}") +
                    Record(401, "ObjectToggleAction", "VF.Model.StateAction", "obj: {fileID: 100}"));
                var graph = Graph(yaml);
                var manifest = AvatarPackageIndex.ParseText(yaml, "orphan.prefab");
                check(graph.IsValid && manifest.AllVrcFuryArmatureLinks.Count == 0, "An orphan ArmatureLink executed");
                check(AvatarBuildPlanner.CreatePlan(manifest).Operations.Count == 0, "An orphan created a plan operation");
                check(!manifest.VrcFuryComponents.Single().UnsupportedFeatures.Contains("ObjectToggleAction"), "A state action became an unsupported feature");
            }
        });
        test("VRCFury legacy roots preserve feature order before modern content", () =>
        {
            string yaml = Fixture("config:\n  features: [{rid: 302}, {rid: 301}]\ncontent: {rid: 300}",
                Link(300) + Link(301) + Link(302));
            var graph = Graph(yaml);
            check(graph.RootIds.SequenceEqual(new long[] { 302, 301, 300 }), "Root order differs from upstream GetAllFeatures");
            check(graph.ReachableFeatures.Select(Id).SequenceEqual(graph.RootIds), "Table order replaced root order");
            var manifest = AvatarPackageIndex.ParseText(yaml, "legacy.prefab");
            check(manifest.AllVrcFuryArmatureLinks.Select(link => link.PropBoneFileId).SequenceEqual(new long?[] { 302, 301, 300 }), "Manifest reordered root features");
            check(AvatarBuildPlanner.CreatePlan(manifest).Operations.Count == 3, "Reachable legacy roots were lost");
        });
        test("VRCFury indirect action cycles terminate without classifying actions as features", () =>
        {
            string yaml = Fixture("content: {rid: 400}",
                Link(300) + Record(400, "Toggle", "VF.Model.Feature", "state: {actions: [{rid: 401}]}") +
                Record(401, "ObjectToggleAction", "VF.Model.StateAction", "next: {rid: 402}") +
                Record(402, "AnimationClipAction", "VF.Model.StateAction", "loop: {rid: 401}\nfeature: {rid: 300}"));
            var graph = Graph(yaml);
            check(graph.IsValid && graph.Records.Count == 4, "Valid cyclic graph was rejected");
            check(graph.ReachableFeatures.Select(Id).SequenceEqual(new long[] { 400, 300 }), "Indirect features or action classification changed");
            var manifest = AvatarPackageIndex.ParseText(yaml, "cycle.prefab");
            check(manifest.VrcFuryComponents.Single().UnsupportedFeatures.SequenceEqual(new[] { "Toggle" }), "Partial initial support claimed full feature support, or actions were counted");
            check(AvatarBuildPlanner.CreatePlan(manifest).Operations.Count == 1, "Indirect ArmatureLink did not reach planner exactly once");
        });
        test("VRCFury duplicate orphan records invalidate the graph without inline fallback", () =>
        {
            string yaml = Fixture("content: {rid: 300}\npropBone: {fileID: 100}", Link(300) + Link(999) + Link(999));
            var graph = Graph(yaml);
            check(!graph.IsValid && graph.Diagnostics.Any(diagnostic => diagnostic.Code == "VRCFURY_DUPLICATE_REFERENCE_ID"), "Duplicate orphan ID was accepted");
            check(graph.ReachableFeatures.Count == 0, "Invalid graph exposed executable features");
            var manifest = AvatarPackageIndex.ParseText(yaml, "duplicate.prefab");
            check(AvatarBuildPlanner.CreatePlan(manifest).Operations.Count == 0, "Invalid graph fell back to an invented inline link");
            check(manifest.VrcFuryComponents.Single().UnsupportedFeatures.Any(feature => feature.StartsWith("VRCFURY_DUPLICATE_REFERENCE_ID:", StringComparison.Ordinal)), "Graph diagnostic was dropped by the manifest");
        });
        foreach (string recordId in new[] { "invalid", "-1", "-2", "9223372036854775808" })
        {
            test("VRCFury malformed table ID is rejected: " + recordId, () =>
            {
                string yaml = Fixture("content: {rid: 300}", Link(300).Replace("- rid: 300", "- rid: " + recordId));
                check(!Graph(yaml).IsValid, "Malformed table ID was accepted");
                check(AvatarBuildPlanner.CreatePlan(AvatarPackageIndex.ParseText(yaml, "invalid.prefab")).Operations.Count == 0, "Malformed table created an operation");
            });
        }
        test("VRCFury missing references fail closed but Unity null sentinel is not a dangling reference", () =>
        {
            string nullYaml = Fixture("content: {rid: -2}\nconfig:\n  features: [{rid: 300}]", Link(300));
            check(Graph(nullYaml).IsValid && AvatarPackageIndex.ParseText(nullYaml, "null.prefab").AllVrcFuryArmatureLinks.Count == 1, "Null modern content hid valid legacy roots");
            foreach (string rid in new[] { "999", "-1", "bad", "-3" })
            {
                string yaml = Fixture("content: {rid: 300}", Link(300, "next: {rid: " + rid + "}"));
                check(!Graph(yaml).IsValid && Graph(yaml).ReachableFeatures.Count == 0, "Bad nested reference did not invalidate features");
                check(AvatarBuildPlanner.CreatePlan(AvatarPackageIndex.ParseText(yaml, "missing.prefab")).Operations.Count == 0, "Bad nested reference permitted partial structural execution");
            }
        });
        test("VRCFury exact namespace and class matching rejects ArmatureLink lookalikes", () =>
        {
            string yaml = Fixture("config:\n  features: [{rid: 300}, {rid: 301}, {rid: 302}, {rid: 303}]",
                Record(300, "ArmatureLink", "VF.Model.StateAction", "propBone: {fileID: 100}") +
                Record(301, "NotArmatureLink", "VF.Model.Feature", "propBone: {fileID: 100}") +
                Record(302, "armaturelink", "VF.Model.Feature", "propBone: {fileID: 100}") +
                Record(303, "ArmatureLink", "Other.VF.Model.Feature", "propBone: {fileID: 100}"));
            var graph = Graph(yaml);
            check(graph.IsValid && graph.ReachableFeatures.Select(Id).SequenceEqual(new long[] { 301, 302 }), "Namespace matching was not exact");
            var manifest = AvatarPackageIndex.ParseText(yaml, "lookalike.prefab");
            check(manifest.VrcFuryComponents.Single().UnsupportedFeatures.SequenceEqual(new[] { "NotArmatureLink", "armaturelink" }), "Feature classification used class substrings");
            check(AvatarBuildPlanner.CreatePlan(manifest).Operations.Count == 0, "Lookalike type created a structural operation");
        });
        test("VRCFury legacy inline links remain available without managed graph", () =>
        {
            string yaml = Fixture("version: 7\npropBone: {fileID: 100}\nlinkTo: [{bone: 0}]", "");
            yaml = yaml.Replace("  references:\n    version: 2\n    RefIds:\n", "");
            check(AvatarPackageIndex.ParseText(yaml, "inline.prefab").AllVrcFuryArmatureLinks.Count == 1, "Legacy inline path was removed");
            string envelopeOnly = yaml + "  references: {version: 2}\n";
            check(AvatarPackageIndex.ParseText(envelopeOnly, "inline-envelope.prefab").AllVrcFuryArmatureLinks.Count == 1, "An envelope without RefIds suppressed legacy inline data");
        });
        test("VRCFury malformed RefIds cannot fall back to inline fields", () =>
        {
            string yaml = Fixture("propBone: {fileID: 100}\nlinkTo: [{bone: 0}]", "").Replace("RefIds:\n", "RefIds: invalid\n");
            check(!Graph(yaml).IsValid, "Malformed table was accepted");
            check(AvatarBuildPlanner.CreatePlan(AvatarPackageIndex.ParseText(yaml, "table.prefab")).Operations.Count == 0, "Malformed table triggered inline fallback");
        });
        test("VRCFury managed graph supports cancellation", () =>
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            bool cancelled = false;
            try { VrcFuryManagedReferenceGraph.Read(Body(Fixture("content: {rid: 300}", Link(300))), cancellation.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            check(cancelled, "Graph reader ignored cancellation");
        });
    }

    private static string Fixture(string roots, string records) =>
        "--- !u!114 &200\nMonoBehaviour:\n  m_GameObject: {fileID: 100}\n  m_Script: {fileID: 11500000, guid: " +
        AvatarPackageIndex.VrcFuryFullScriptGuid + ", type: 3}\n" +
        (roots.Length == 0 ? "" : string.Join("\n", roots.Split('\n').Select(line => "  " + line)) + "\n") +
        "  references:\n    version: 2\n    RefIds:\n" + records;

    private static string Link(long id, string extra = "") => Record(id, "ArmatureLink", "VF.Model.Feature",
        "version: 7\npropBone: {fileID: " + id + "}\nlinkTo: [{bone: 0}]\n" + extra);

    private static string Record(long id, string className, string ns, string data) =>
        "    - rid: " + id + "\n      type: {class: " + className + ", ns: " + ns + ", asm: VRCFury}\n      data:\n" +
        string.Join("\n", data.Split('\n').Where(line => line.Length > 0).Select(line => "        " + line)) + "\n";

    private static VrcFuryManagedReferenceGraph Graph(string yaml) => VrcFuryManagedReferenceGraph.Read(Body(yaml));

    private static YamlMappingNode Body(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml[(yaml.IndexOf('\n') + 1)..]));
        return (YamlMappingNode)((YamlMappingNode)stream.Documents[0].RootNode).Children[new YamlScalarNode("MonoBehaviour")];
    }

    private static long Id(YamlMappingNode entry) => long.Parse(((YamlScalarNode)entry.Children[new YamlScalarNode("rid")]).Value!);
}
