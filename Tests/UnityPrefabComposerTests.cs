using System.Globalization;
using System.Text.RegularExpressions;
using UnityPackageImporter.Models;
using YamlDotNet.RepresentationModel;

internal static class UnityPrefabComposerTests
{
    private const string Native = """
--- !u!1 &1
GameObject:
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_Name: Avatar
  m_IsActive: 1
  m_Component:
  - component: {fileID: 2}
  - component: {fileID: 3}
--- !u!4 &2
Transform:
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_GameObject: {fileID: 1}
  m_Father: {fileID: 0}
  m_Children: []
  m_LocalPosition: {x: 0, y: 0, z: 0}
--- !u!114 &3
MonoBehaviour:
  m_GameObject: {fileID: 1}
  m_Name: Original
  m_Script: {fileID: 11500000, guid: 2df373bf91cf30b4bbd495e11cb1a2ec, type: 3}
  mergeTarget:
    referencePath: Armature
    targetObject: {fileID: 1}
  settings:
  - enabled: 1
    material: {fileID: 2100000, guid: material-a, type: 2}
""" + "\n";

    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        void Sources(Action<Dictionary<string, string>> run, params (string Guid, string Yaml)[] sources)
        {
            string root = Path.Combine(Path.GetTempPath(), "prefab-composer-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var source in sources)
                {
                    string path = Path.Combine(root, source.Guid + ".prefab");
                    File.WriteAllText(path, source.Yaml);
                    paths.Add(source.Guid, path);
                }
                run(paths);
            }
            finally { Directory.Delete(root, true); }
        }

        void Reject(Action run)
        {
            try { run(); }
            catch (InvalidDataException) { return; }
            throw new Exception("Expected explicit invalid prefab diagnostics.");
        }

        test("Native prefab composition preserves authored objects and MA identities", () => Sources(paths =>
        {
            string before = File.ReadAllText(paths["native"]);
            string output = UnityPrefabComposer.Compose(Instance(100, "native") + Alias(101, 1, 1, 100, "native") + Alias(102, 4, 2, 100, "native"), paths);
            var documents = Documents(output);
            check(documents.Count == 3 && documents.ContainsKey(101) && documents.ContainsKey(102), "Stripped aliases did not retain their identities.");
            var manifest = AvatarPackageIndex.ParseText(output);
            check(manifest.ModularAvatarComponents.Count == 1 && manifest.ModularAvatarComponents[0].GameObjectFileId == 101, "MA owner was not remapped with the actual graph.");
            var component = documents.Values.Single(body => body.Children.ContainsKey("mergeTarget"));
            check(Ref((YamlMappingNode)component["mergeTarget"], "targetObject") == 101, "Nested local object reference was not remapped.");
            check(File.ReadAllText(paths["native"]) == before, "Composition mutated the extraction cache.");
        }, ("native", Native)));

        test("Repeated native prefab instances keep independent component identities", () => Sources(paths =>
        {
            string yaml = Instance(100, "native") + Alias(101, 1, 1, 100, "native") + Instance(200, "native") + Alias(201, 1, 1, 200, "native");
            string output = UnityPrefabComposer.Compose(yaml, paths);
            var manifest = AvatarPackageIndex.ParseText(output);
            check(Documents(output).Count == 6, "Repeated instances collided.");
            check(manifest.ModularAvatarComponents.Select(component => component.GameObjectFileId).Order().SequenceEqual(new long[] { 101, 201 }), "Instance-local MA ownership collided.");
            check(manifest.ModularAvatarComponents.Select(component => component.ComponentFileId).Distinct().Count() == 2, "Components were deduplicated across instances.");
        }, ("native", Native)));

        test("Accessory-first assembly retains the actual avatar descriptor and complete MA plan", () => Sources(paths =>
        {
            string yaml = Instance(100, "outfit", parent: 202) + Alias(101, 1, 1, 100, "outfit") +
                Instance(200, "avatar") + Alias(201, 1, 1, 200, "avatar") + Alias(202, 4, 2, 200, "avatar");
            var manifest = AvatarPackageIndex.ParseText(UnityPrefabComposer.Compose(yaml, paths));
            check(manifest.Avatars.Count == 1 && manifest.Avatars[0].GameObjectFileId == 201, "First accessory source replaced the owning avatar descriptor.");
            check(!manifest.IsModularAvatarAttachment && manifest.ModularAvatarComponents.Count == 2, "Assembly was misclassified as standalone clothing or lost nested MA components.");
            var plan = AvatarBuildPlanner.CreatePlan(manifest);
            check(plan.Operations.Count == 2 && plan.Operations.Select(operation => operation.Source.ComponentFileId).Distinct().Count() == 2, "Nested operations were collapsed by raw source fileID.");
        }, ("outfit", Native), ("avatar", Native + "--- !u!114 &4\nMonoBehaviour:\n  m_GameObject: {fileID: 1}\n  m_Script: {fileID: 542108242, guid: 67cc4cb7839cd3741b63733d5adf0442, type: 3}\n")));

        test("Native prefab overrides retain scalars external references and array paths", () => Sources(paths =>
        {
            string changes = Change(1, "m_Name", "Dressed Avatar") + Change(3, "settings.Array.data[0].enabled", "0") +
                Change(3, "settings.Array.data[0].material", "", "{fileID: 2100000, guid: material-b, type: 2}") + Change(3, "m_Name", "");
            string output = UnityPrefabComposer.Compose(Instance(100, "native", changes) + Alias(101, 1, 1, 100, "native"), paths);
            var documents = Documents(output);
            check(((YamlScalarNode)documents[101]["m_Name"]).Value == "Dressed Avatar", "Name override was lost.");
            var component = documents.Values.Single(body => body.Children.ContainsKey("settings"));
            var item = (YamlMappingNode)((YamlSequenceNode)component["settings"]).Children.Single();
            check(((YamlScalarNode)item["enabled"]).Value == "0" && ((YamlScalarNode)component["m_Name"]).Value == "", "Scalar or empty-string override was lost.");
            check(((YamlScalarNode)((YamlMappingNode)item["material"])["guid"]).Value == "material-b", "External material reference was remapped or lost.");
        }, ("native", Native)));

        test("Native prefab null-reference override remains a reference", () => Sources(paths =>
        {
            string output = UnityPrefabComposer.Compose(Instance(100, "native", Change(3, "mergeTarget.targetObject", "")), paths);
            var component = Documents(output).Values.Single(body => body.Children.ContainsKey("mergeTarget"));
            check(Ref((YamlMappingNode)component["mergeTarget"], "targetObject") == 0, "Null objectReference became a scalar.");
        }, ("native", Native)));

        test("Native prefab children are parented in the authored assembly", () => Sources(paths =>
        {
            string output = UnityPrefabComposer.Compose(Native + Instance(100, "native", parent: 2) + Alias(102, 4, 2, 100, "native"), paths);
            check(Ref(Documents(output)[102], "m_Father") == 2, "Source root was detached from its declared parent.");
        }, ("native", Native)));

        test("Multi-layer native variants resolve implicit Unity-derived root identities", () => Sources(paths =>
        {
            long implicitTransform = PrefabVariantResolver.ComputeDerivedFileId(20, 2);
            string output = UnityPrefabComposer.Compose(Instance(100, "variant") + Alias(102, 4, implicitTransform, 100, "variant"), paths);
            check(Documents(output).ContainsKey(102), "Implicit nested root identity disappeared during expansion.");
            check(AvatarPackageIndex.ParseText(output).ModularAvatarComponents.Count == 1, "Nested MA component was lost.");
        }, ("native", Native), ("variant", Instance(20, "native"))));

        test("Native prefab removals drop components and their owner list entries", () => Sources(paths =>
        {
            string yaml = Instance(100, "native").Replace("m_RemovedComponents: []", "m_RemovedComponents:\n    - {fileID: 3, guid: native, type: 3}") + Alias(101, 1, 1, 100, "native");
            var documents = Documents(UnityPrefabComposer.Compose(yaml, paths));
            check(documents.Count == 2 && ((YamlSequenceNode)documents[101]["m_Component"]).Children.Count == 1, "Removed component or owner reference survived.");
        }, ("native", Native)));

        test("Native prefab additions bind the authored parent and component owner", () => Sources(paths =>
        {
            string yaml = Instance(100, "native")
                .Replace("m_AddedGameObjects: []", "m_AddedGameObjects:\n    - targetCorrespondingSourceObject: {fileID: 2, guid: native, type: 3}\n      insertIndex: -1\n      addedObject: {fileID: 501}")
                .Replace("m_AddedComponents: []", "m_AddedComponents:\n    - targetCorrespondingSourceObject: {fileID: 1, guid: native, type: 3}\n      insertIndex: -1\n      addedObject: {fileID: 503}") +
                Alias(102, 4, 2, 100, "native") + Alias(101, 1, 1, 100, "native") +
                "--- !u!1 &501\nGameObject:\n  m_Name: Child\n--- !u!4 &502\nTransform:\n  m_GameObject: {fileID: 501}\n  m_Father: {fileID: 0}\n--- !u!114 &503\nMonoBehaviour:\n  m_GameObject: {fileID: 0}\n";
            var documents = Documents(UnityPrefabComposer.Compose(yaml, paths));
            check(Ref(documents[502], "m_Father") == 102 && Ref(documents[503], "m_GameObject") == 101, "Added objects lost their declared ownership.");
        }, ("native", Native)));

        test("FBX and missing prefab references remain explicit runtime dependencies", () =>
        {
            string yaml = Instance(10, "fbx") + Instance(20, "missing");
            string output = UnityPrefabComposer.Compose(yaml, new Dictionary<string, string>());
            check(Documents(output).Count == 2 && output.Contains("guid: fbx") && output.Contains("guid: missing"), "Unavailable dependencies were dropped or invented.");
        });

        test("Native variants forward implicit FBX overrides using exact model identities", () => Sources(paths =>
        {
            const long modelObject = -17;
            long derived = PrefabVariantResolver.ComputeDerivedFileId(20, modelObject);
            var models = new Dictionary<string, IReadOnlyDictionary<long, int>> { ["fbx"] = new Dictionary<long, int> { [modelObject] = 1 } };
            string output = UnityPrefabComposer.Compose(Instance(100, "variant", Change(derived, "m_IsActive", "0", guid: "variant")), paths, models);
            var instance = Documents(output).Values.Single(body => body.Children.ContainsKey("m_SourcePrefab"));
            var changes = (YamlSequenceNode)((YamlMappingNode)instance["m_Modification"])["m_Modifications"];
            check(changes.Children.OfType<YamlMappingNode>().Any(change => Ref(change, "target") == modelObject), "Inherited FBX override did not resolve against the model identity index.");
        }, ("variant", Instance(20, "fbx"))));

        foreach (var renderer in new[] { (ClassId: 23, Type: "MeshRenderer"), (ClassId: 137, Type: "SkinnedMeshRenderer") })
            test("Inherited model renderer preserves component type: " + renderer.Type, () => Sources(paths =>
            {
                const long modelRenderer = -17;
                long derived = PrefabVariantResolver.ComputeDerivedFileId(20, modelRenderer);
                var models = new Dictionary<string, IReadOnlyDictionary<long, int>>
                {
                    ["fbx"] = new Dictionary<long, int> { [modelRenderer] = renderer.ClassId }
                };
                string alias = $"--- !u!{renderer.ClassId} &99 stripped\n{renderer.Type}:\n  m_CorrespondingSourceObject: {{fileID: {derived}, guid: variant, type: 3}}\n  m_PrefabInstance: {{fileID: 100}}\n";
                string output = UnityPrefabComposer.Compose(Instance(100, "variant") + alias, paths, models);
                var stream = new YamlStream();
                stream.Load(new StringReader(AvatarPackageIndex.NormalizeDocumentHeaders(output)));
                var root = (YamlMappingNode)stream.Documents.Single(document => document.RootNode.Anchor.Value == "99").RootNode;
                check(root.Children.ContainsKey(new YamlScalarNode(renderer.Type)), "Inherited renderer was mislabeled as a GameObject.");
                check(Ref((YamlMappingNode)root.Children.Single().Value, "m_CorrespondingSourceObject") == modelRenderer,
                    "Inherited renderer lost its exact source identity.");
            }, ("variant", Instance(20, "fbx"))));

        test("Native prefab composition rejects cycles unresolved aliases and unknown properties", () =>
        {
            Sources(paths => Reject(() => UnityPrefabComposer.Compose(Instance(10, "native"), paths)), ("native", Instance(20, "native")));
            Sources(paths => Reject(() => UnityPrefabComposer.Compose(Instance(10, "native") + Alias(99, 4, 998, 10, "native"), paths)), ("native", Native));
            Sources(paths => Reject(() => UnityPrefabComposer.Compose(Instance(10, "native", Change(1, "unknown", "1")), paths)), ("native", Native));
        });

        test("Native prefab composition rejects foreign alias and removal identities", () => Sources(paths =>
        {
            Reject(() => UnityPrefabComposer.Compose(Instance(10, "native") + Alias(99, 1, 1, 10, "foreign"), paths));
            Reject(() => UnityPrefabComposer.Compose(Instance(10, "native") + Alias(99, 4, 1, 10, "native"), paths));
            string removal = Instance(10, "native").Replace("m_RemovedComponents: []", "m_RemovedComponents:\n    - {fileID: 3, guid: foreign, type: 3}");
            Reject(() => UnityPrefabComposer.Compose(removal, paths));
        }, ("native", Native)));

        test("Implicit FBX reference collisions cannot pick the first model instance", () => Sources(paths =>
        {
            const long first = 17;
            long derived = PrefabVariantResolver.ComputeDerivedFileId(20, first);
            long second = 30 ^ derived;
            var models = new Dictionary<string, IReadOnlyDictionary<long, int>>
            {
                ["fbx-a"] = new Dictionary<long, int> { [first] = 1 },
                ["fbx-b"] = new Dictionary<long, int> { [second] = 1 }
            };
            Reject(() => UnityPrefabComposer.Compose(Instance(100, "variant", Change(derived, "m_IsActive", "0", guid: "variant")), paths, models));
        }, ("variant", Instance(20, "fbx-a") + Instance(30, "fbx-b"))));

        test("Native prefab composition is deterministic and rejects duplicate source identities", () => Sources(paths =>
        {
            string yaml = Instance(100, "native");
            check(UnityPrefabComposer.Compose(yaml, paths) == UnityPrefabComposer.Compose(yaml, paths), "Composition identities depend on prior calls.");
            Reject(() => UnityPrefabComposer.Compose(Native + Native, paths));
        }, ("native", Native)));

        test("Avatar conversion scope never borrows another avatar or picks an ambiguous bone", () =>
        {
            string first = "AvatarA";
            string second = "AvatarB";
            bool Within(string source, string root) => source.StartsWith(root, StringComparison.Ordinal);
            check(AvatarConversionScope.RootFor("AvatarB/Accessory", "Container", new[] { first, second }, Within) == second, "Operation did not use its owning descriptor root.");
            check(AvatarConversionScope.RootFor("Outside", "Container", new[] { first, second }, Within) == null, "Operation borrowed a different avatar.");
            check(AvatarConversionScope.RootFor("AvatarA/Accessory", "Container", new[] { first, "AvatarA/Accessory" }, Within) == null, "Overlapping avatar ownership was guessed.");
            check(AvatarConversionScope.Unique(new[] { first, second }) == null && AvatarConversionScope.Unique(new[] { first, first }) == first, "Ambiguous bone identity was selected by order.");
        });
    }

    private static string Instance(long id, string guid, string changes = "", long parent = 0) =>
        $"--- !u!1001 &{id}\nPrefabInstance:\n  m_Modification:\n    m_TransformParent: {{fileID: {parent}}}\n    m_Modifications:{(changes.Length == 0 ? " []\n" : "\n" + changes)}    m_RemovedComponents: []\n    m_RemovedGameObjects: []\n    m_AddedGameObjects: []\n    m_AddedComponents: []\n  m_SourcePrefab: {{fileID: 100100000, guid: {guid}, type: 3}}\n";

    private static string Alias(long id, int classId, long source, long instance, string guid) =>
        $"--- !u!{classId} &{id} stripped\n{(classId == 4 ? "Transform" : "GameObject")}:\n  m_CorrespondingSourceObject: {{fileID: {source}, guid: {guid}, type: 3}}\n  m_PrefabInstance: {{fileID: {instance}}}\n";

    private static string Change(long target, string path, string value, string reference = "{fileID: 0}", string guid = "native") =>
        $"    - target: {{fileID: {target}, guid: {guid}, type: 3}}\n      propertyPath: {path}\n      value: {value}\n      objectReference: {reference}\n";

    private static Dictionary<long, YamlMappingNode> Documents(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(Regex.Replace(AvatarPackageIndex.NormalizeDocumentHeaders(yaml), @"(?m)^(--- )!u!(\d+)", "$1!<tag:unity3d.com,2011:$2>")));
        return stream.Documents.ToDictionary(document => long.Parse(document.RootNode.Anchor.Value, CultureInfo.InvariantCulture),
            document => (YamlMappingNode)((YamlMappingNode)document.RootNode).Children.Values.Single());
    }

    private static long Ref(YamlMappingNode mapping, string key) => long.Parse(((YamlScalarNode)((YamlMappingNode)mapping[key])["fileID"]).Value!, CultureInfo.InvariantCulture);
}
