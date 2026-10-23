using UnityPackageImporter.Models;
using YamlDotNet.RepresentationModel;

internal static class UnityPrefabSchemaTests
{
    private const string Definition = "--- !u!1001 &100100000\nPrefab:\n  m_ParentPrefab: {fileID: 0}\n  m_IsPrefabParent: 1\n  m_RootGameObject: {fileID: 1}\n";
    private const string Objects = "--- !u!1 &1\nGameObject:\n  m_PrefabParentObject: {fileID: 0}\n  m_PrefabInternal: {fileID: 100100000}\n  m_Name: Legacy\n  m_Component:\n  - component: {fileID: 2}\n--- !u!4 &2\nTransform:\n  m_PrefabParentObject: {fileID: 0}\n  m_PrefabInternal: {fileID: 100100000}\n  m_GameObject: {fileID: 1}\n  m_Father: {fileID: 0}\n  m_Children: []\n  m_LocalPosition: {x: 0, y: 0, z: 0}\n";
    private const string Instance = "--- !u!1001 &50\nPrefabInstance:\n  m_SourcePrefab: {fileID: 100100000, guid: legacy, type: 3}\n  m_Modification:\n    m_TransformParent: {fileID: 0}\n    m_Modifications:\n    - target: {fileID: 1, guid: legacy, type: 3}\n      propertyPath: m_Name\n      value: Changed\n      objectReference: {fileID: 0}\n";

    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        void Reject(string yaml, int classId)
        {
            try { Normalize(yaml, classId); }
            catch (InvalidDataException) { return; }
            throw new Exception("Malformed legacy schema was accepted.");
        }

        test("Legacy prefab definitions normalize without inventing nested instances", () =>
        {
            var root = Normalize("Prefab:\n  m_ParentPrefab: {fileID: 0}\n  m_IsPrefabParent: 1\n  m_RootGameObject: {fileID: 1}\n", 1001);
            check(root.Children.ContainsKey("Prefab") && !root.Children.ContainsKey("PrefabInstance"), "Definition was converted to an instance.");
            var output = UnityPrefabComposer.Compose(Definition + Objects, new Dictionary<string, string>());
            var documents = Documents(output);
            check(documents.Count(root => root.Children.ContainsKey("GameObject")) == 1 && documents.Count(root => root.Children.ContainsKey("Transform")) == 1, "Legacy object graph was lost.");
            check(output.Contains("m_PrefabInstance:") && output.Contains("m_RootGameObject:"), "Ownership normalization or definition metadata was lost.");
        });

        test("Modern variants compose legacy definitions and apply authored overrides", () => WithSource(paths =>
        {
            string before = File.ReadAllText(paths["legacy"]);
            string output = UnityPrefabComposer.Compose(Instance, paths);
            var documents = Documents(output);
            check(documents.Count(root => root.Children.ContainsKey("GameObject")) == 1 && output.Contains("Changed"), "Legacy base objects or variant override were not composed.");
            check(!documents.Any(root => root.Children.ContainsKey("PrefabInstance")), "A definition survived as an unresolved instance.");
            check(File.ReadAllText(paths["legacy"]) == before, "Normalization changed the source cache.");
        }));

        test("Legacy linked instances resolve old source and owning-instance aliases", () => WithSource(paths =>
        {
            string linked = Instance.Replace("PrefabInstance:", "Prefab:").Replace("m_SourcePrefab:", "m_ParentPrefab:")
                .Replace("  m_Modification:", "  m_IsPrefabParent: 0\n  m_Modification:");
            string aliases = "--- !u!1 &51 stripped\nGameObject:\n  m_PrefabParentObject: {fileID: 1, guid: legacy, type: 3}\n  m_PrefabInternal: {fileID: 50}\n--- !u!4 &52 stripped\nTransform:\n  m_PrefabParentObject: {fileID: 2, guid: legacy, type: 3}\n  m_PrefabInternal: {fileID: 50}\n";
            string output = UnityPrefabComposer.Compose(linked + aliases, paths);
            check(output.Contains("&51") && output.Contains("&52") && output.Contains("Changed"), "Legacy aliases lost their local identities or overrides.");
        }));

        test("Legacy definitions do not prevent unrelated source batch imports", () => WithSource(paths =>
        {
            string modern = Path.Combine(Path.GetDirectoryName(paths["legacy"])!, "modern.prefab");
            File.WriteAllText(modern, Objects);
            File.WriteAllText(modern + ".meta", "fileFormatVersion: 2\nguid: modern\nPrefabImporter: {}\n");
            var assets = UnityPackageAssetIndex.Build(new[] { paths["legacy"], paths["legacy"] + ".meta", modern, modern + ".meta" });
            var selected = PrefabAssemblySelection.Create(assets.Prefabs);
            int invoked = 0;
            var batch = SourceImportBatch.RunAsync(selected, (source, _) =>
            {
                UnityPrefabComposer.Compose(File.ReadAllText(source.Value), assets.Prefabs);
                invoked++;
                return Task.CompletedTask;
            }, (_, _) => Task.CompletedTask, maxDegreeOfParallelism: 1).GetAwaiter().GetResult();
            check(selected.Count == 2 && invoked == 2 && batch.Failed.Count == 0 && assets.Prefabs.Count == 2, "A legacy definition blocked unrelated outputs or filtered the library.");
        }));

        test("Legacy linked sources participate in structural dependency selection", () => WithSource(paths =>
        {
            string variant = Path.Combine(Path.GetDirectoryName(paths["legacy"])!, "variant.prefab");
            File.WriteAllText(variant, Instance.Replace("PrefabInstance:", "Prefab:").Replace("m_SourcePrefab:", "m_ParentPrefab:")
                .Replace("  m_Modification:", "  m_IsPrefabParent: 0\n  m_Modification:"));
            paths.Add("variant", variant);
            check(PrefabAssemblySelection.Create(paths).Single().Key == "variant", "Legacy source was imported as a separate dependency-only output.");
        }));

        test("Legacy prefab normalization rejects inconsistent definitions and aliases", () =>
        {
            string body = "Prefab:\n  m_ParentPrefab: {fileID: 0}\n  m_IsPrefabParent: 1\n  m_RootGameObject: {fileID: 1}\n";
            Reject(body.Replace("m_IsPrefabParent: 1", "m_IsPrefabParent: 0"), 1001);
            Reject(body.Replace("m_RootGameObject: {fileID: 1}", "m_RootGameObject: {fileID: 0}"), 1001);
            Reject(body.Replace("m_RootGameObject: {fileID: 1}", "m_RootGameObject: {fileID: 1, guid: foreign}"), 1001);
            Reject(body.Replace("m_ParentPrefab: {fileID: 0}", "m_ParentPrefab: {fileID: 0, guid: foreign}"), 1001);
            Reject("Prefab:\n  m_ParentPrefab: {fileID: 1}\n  m_IsPrefabParent: 0\n", 1001);
            Reject("GameObject:\n  m_PrefabParentObject: broken\n", 1);
            Reject("GameObject:\n  m_PrefabParentObject: {fileID: 1}\n  m_PrefabInternal: {fileID: 50}\n", 1);
            Reject("GameObject:\n  m_PrefabParentObject: {fileID: 1, guid: legacy}\n  m_PrefabInternal: {fileID: 50, guid: foreign}\n", 1);
            Reject("GameObject:\n  m_PrefabParentObject: {fileID: 0}\n  m_CorrespondingSourceObject: {fileID: 2}\n", 1);
            Reject("GameObject:\n  m_PrefabParentObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 50}\n", 1);
        });
    }

    private static YamlMappingNode[] Documents(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(System.Text.RegularExpressions.Regex.Replace(AvatarPackageIndex.NormalizeDocumentHeaders(yaml), @"(?m)^(--- )!u!(\d+)", "$1!<tag:unity3d.com,2011:$2>")));
        return stream.Documents.Select(document => (YamlMappingNode)document.RootNode).ToArray();
    }

    private static YamlMappingNode Normalize(string yaml, int classId)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        var root = (YamlMappingNode)stream.Documents.Single().RootNode;
        UnityPrefabSchema.NormalizeDocument(root, classId);
        return root;
    }

    private static void WithSource(Action<Dictionary<string, string>> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "prefab-schema-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "legacy.prefab");
            File.WriteAllText(path, Definition + Objects);
            File.WriteAllText(path + ".meta", "fileFormatVersion: 2\nguid: legacy\nPrefabImporter: {}\n");
            action(new Dictionary<string, string> { ["legacy"] = path });
        }
        finally { Directory.Delete(directory, true); }
    }
}
