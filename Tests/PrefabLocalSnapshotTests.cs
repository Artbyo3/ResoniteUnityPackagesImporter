using UnityPackageImporter.Models;
using YamlDotNet.RepresentationModel;

internal static class PrefabLocalSnapshotTests
{
    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        test("Complete local accessory snapshot preserves objects, meshes and MA settings", () =>
        {
            var diagnostics = new PrefabCompositionDiagnostics();
            var documents = Documents(UnityPrefabComposer.Compose(Snapshot(), new Dictionary<string, string>(), null, diagnostics));
            check(diagnostics.MaterializedLocalObjects.SetEquals(new long[] { 1, 2, 3, 4, 5, 6, 7 }), "Every complete local object must survive.");
            check(documents.Count == 8 && documents.ContainsKey(90), "Missing source provenance must remain diagnostic.");
            check(Scalar(Map(documents[6], "m_Mesh"), "guid") == "model", "Static mesh reference must survive.");
            check(Scalar(documents[3], "boneReference") == "16" && Scalar(documents[3], "attachmentMode") == "2", "MA attachment settings must survive.");
            check(Scalar(Map(documents[3], "m_PrefabInstance"), "fileID") == "0", "Complete local records must instantiate without absent source aliases.");
        });
        test("Complete local accessory snapshot replays authored overrides", () =>
        {
            string changes = "    - target: {fileID: 1, guid: missing, type: 3}\n      propertyPath: m_IsActive\n      value: 0\n      objectReference: {fileID: 0}\n";
            var diagnostics = new PrefabCompositionDiagnostics();
            var documents = Documents(UnityPrefabComposer.Compose(Snapshot(changes), new Dictionary<string, string>(), null, diagnostics));
            check(Scalar(documents[1], "m_IsActive") == "0" && diagnostics.MaterializedLocalObjects.Count == 7, "Authored inactivity must not be discarded.");
        });
        foreach (var malformed in new[] { "stripped", "dangling", "cycle", "nonfinite", "incomplete", "owner-membership" })
            test("Incomplete local snapshot refuses promotion: " + malformed, () =>
            {
                string yaml = Snapshot();
                yaml = malformed switch
                {
                    "stripped" => yaml.Replace("&5\n", "&5 stripped\n"),
                    "dangling" => yaml.Replace("m_GameObject: {fileID: 4}", "m_GameObject: {fileID: 999}"),
                    "cycle" => yaml.Replace("m_Father: {fileID: 0}", "m_Father: {fileID: 5}"),
                    "nonfinite" => yaml.Replace("m_LocalScale: {x: 1, y: 1, z: 1}", "m_LocalScale: {x: NaN, y: 1, z: 1}"),
                    "owner-membership" => yaml.Replace("  - component: {fileID: 7}\n", ""),
                    _ => yaml.Replace("  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}\n", "")
                };
                var diagnostics = new PrefabCompositionDiagnostics();
                UnityPrefabComposer.Compose(yaml, new Dictionary<string, string>(), null, diagnostics);
                check(diagnostics.MaterializedLocalObjects.Count == 0, "Incomplete source records must remain unresolved.");
            });
        test("Available model source never enters missing-source snapshot recovery", () =>
        {
            var diagnostics = new PrefabCompositionDiagnostics();
            var models = new Dictionary<string, IReadOnlyDictionary<long, int>> { ["missing"] = new Dictionary<long, int>() };
            UnityPrefabComposer.Compose(Snapshot(), new Dictionary<string, string>(), models, diagnostics);
            check(diagnostics.MaterializedLocalObjects.Count == 0, "Native model sources must retain their identity handling.");
        });
        foreach (var incoming in new[] { "none", "component", "parent", "instance" })
            test("Only unreferenced missing Transform aliases defer: " + incoming, () =>
            {
                string yaml = Instance(90, "missing") + "--- !u!4 &50 stripped\nTransform:\n  m_CorrespondingSourceObject: {fileID: 999, guid: missing, type: 3}\n  m_PrefabInstance: {fileID: 90}\n";
                yaml += incoming switch
                {
                    "component" => "--- !u!114 &70\nMonoBehaviour:\n  target: {fileID: 50}\n",
                    "parent" => "--- !u!4 &71\nTransform:\n  m_Father: {fileID: 50}\n",
                    "instance" => Instance(72, "other", parent: 50),
                    _ => ""
                };
                var diagnostics = new PrefabCompositionDiagnostics();
                var documents = Documents(UnityPrefabComposer.Compose(yaml, new Dictionary<string, string>(), null, diagnostics));
                check(diagnostics.DeferredMissingAliases.Contains(50) == (incoming == "none"), "Referenced missing aliases must still block.");
                check(documents.ContainsKey(50), "Deferral must never delete serialized objects.");
            });
        test("Nested local snapshot recovers final remapped identities without changing source", () =>
        {
            string path = Path.Combine(Path.GetTempPath(), "local-snapshot-" + Guid.NewGuid().ToString("N") + ".prefab");
            string source = Snapshot();
            File.WriteAllText(path, source);
            try
            {
                var diagnostics = new PrefabCompositionDiagnostics();
                var documents = Documents(UnityPrefabComposer.Compose(Instance(100, "accessory"), new Dictionary<string, string> { ["accessory"] = path }, null, diagnostics));
                check(diagnostics.MaterializedLocalObjects.Count == 7 && !diagnostics.MaterializedLocalObjects.Contains(1), "Recovered identity must belong to the composed instance.");
                check(documents.Count == 8 && File.ReadAllText(path) == source, "Composing must not mutate the package or lose local records.");
            }
            finally { File.Delete(path); }
        });
        test("Static mesh identity lookup rejects ambiguity and unrelated meshes", () =>
        {
            var first = new object();
            var second = new object();
            check(ReferenceEquals(MeshReferenceLookup.ResolveUnique(42, new[] { (42L, first), (43L, second) }), first), "Lookup must use the exact mesh ID.");
            check(MeshReferenceLookup.ResolveUnique(42, new[] { (42L, first), (42L, second) }) == null, "Conflicting matches must fail.");
            check(MeshReferenceLookup.ResolveUnique(99, new[] { (42L, first) }) == null, "Unrelated mesh must not substitute.");
        });
    }

    private static string Snapshot(string changes = "") =>
        Document(1, 1, "GameObject", "  m_Name: Accessory\n  m_IsActive: 1\n  m_Component:\n  - component: {fileID: 2}\n  - component: {fileID: 3}\n") +
        Document(2, 4, "Transform", Pose(1, 0) + "  m_Children:\n  - {fileID: 5}\n") +
        Document(3, 114, "MonoBehaviour", "  m_GameObject: {fileID: 1}\n  m_Script: {fileID: 11500000, guid: 42581d8044b64899834d3d515ab3a144, type: 3}\n  boneReference: 16\n  attachmentMode: 2\n  subPath: ''\n") +
        Document(4, 1, "GameObject", "  m_Name: Display\n  m_IsActive: 1\n  m_Component:\n  - component: {fileID: 5}\n  - component: {fileID: 6}\n  - component: {fileID: 7}\n") +
        Document(5, 4, "Transform", Pose(4, 2) + "  m_Children: []\n") +
        Document(6, 33, "MeshFilter", "  m_GameObject: {fileID: 4}\n  m_Mesh: {fileID: 4300000, guid: model, type: 3}\n") +
        Document(7, 23, "MeshRenderer", "  m_GameObject: {fileID: 4}\n  m_Enabled: 1\n  m_Materials:\n  - {fileID: 2100000, guid: material, type: 3}\n") + Instance(90, "missing", changes);

    private static string Document(long id, int classId, string type, string fields) =>
        $"--- !u!{classId} &{id}\n{type}:\n  m_CorrespondingSourceObject: {{fileID: {id}, guid: missing, type: 3}}\n  m_PrefabInstance: {{fileID: 90}}\n" + fields;
    private static string Pose(long owner, long parent) =>
        $"  m_GameObject: {{fileID: {owner}}}\n  m_Father: {{fileID: {parent}}}\n" +
        "  m_LocalPosition: {x: 0, y: 0, z: 0}\n  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}\n  m_LocalScale: {x: 1, y: 1, z: 1}\n";
    private static string Instance(long id, string guid, string changes = "", long parent = 0) =>
        $"--- !u!1001 &{id}\nPrefabInstance:\n  m_SourcePrefab: {{fileID: 100100000, guid: {guid}, type: 3}}\n  m_Modification:\n    m_TransformParent: {{fileID: {parent}}}\n    m_Modifications:" + (changes.Length == 0 ? " []\n" : "\n" + changes);
    private static YamlMappingNode Map(YamlMappingNode node, string key) => (YamlMappingNode)node.Children[new YamlScalarNode(key)];
    private static string Scalar(YamlMappingNode node, string key) => ((YamlScalarNode)node.Children[new YamlScalarNode(key)]).Value ?? "";
    private static Dictionary<long, YamlMappingNode> Documents(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(AvatarPackageIndex.NormalizeDocumentHeaders(yaml)));
        return stream.Documents.ToDictionary(document => long.Parse(document.RootNode.Anchor.Value), document => (YamlMappingNode)((YamlMappingNode)document.RootNode).Children.Single().Value);
    }
}
