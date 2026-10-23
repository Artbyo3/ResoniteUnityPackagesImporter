using System.Globalization;
using System.Text.RegularExpressions;
using UnityPackageImporter.Models;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

internal static class PrefabCompositionLibraryTests
{
    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        test("Composition ignores malformed unrelated indexed sources", () => Sources(paths =>
        {
            string expected = UnityPrefabComposer.Compose(Instance(10, "good"), new Dictionary<string, string> { ["good"] = paths["good"] });
            check(UnityPrefabComposer.Compose(Instance(10, "good"), paths) == expected, "Unreachable malformed YAML contaminated a good root.");
        }, ("good", Object(1, "Good")), ("bad", "--- !u!1 &2\nGameObject: [\n")));

        test("Composition never opens unrelated indexed paths", () => Sources(paths =>
        {
            paths.Add("unavailable", Path.Combine(Path.GetDirectoryName(paths["good"])!, "absent.prefab"));
            check(Documents(UnityPrefabComposer.Compose(Instance(10, "good"), paths)).Count == 1, "An absent unrelated source blocked composition.");
        }, ("good", Object(1, "Good"))));

        test("Composition ignores indexed sources for a standalone root", () => Sources(paths =>
        {
            check(Documents(UnityPrefabComposer.Compose(Object(1, "Standalone"), paths)).Count == 1, "Standalone root inspected unrelated YAML.");
        }, ("bad", "not Unity YAML")));

        test("Composition rejects malformed reachable transitive sources", () => Sources(paths =>
        {
            Expect<YamlException>(() => UnityPrefabComposer.Compose(Instance(10, "branch"), paths));
        }, ("branch", Instance(20, "bad")), ("bad", "--- !u!1 &2\nGameObject: [\n")));

        test("Composition validates reachable document identities", () => Sources(paths =>
        {
            Expect<InvalidDataException>(() => UnityPrefabComposer.Compose(Instance(10, "bad"), paths));
        }, ("bad", Object(1, "First") + Object(1, "Duplicate"))));

        test("Composition reports absent reachable indexed files", () => Sources(paths =>
        {
            paths.Add("missing", Path.Combine(Path.GetDirectoryName(paths["branch"])!, "absent.prefab"));
            Expect<FileNotFoundException>(() => UnityPrefabComposer.Compose(Instance(10, "branch"), paths));
        }, ("branch", Instance(20, "missing"))));

        test("Composition retains unindexed dependency provenance", () => Sources(paths =>
        {
            string output = UnityPrefabComposer.Compose(Instance(10, "branch"), paths);
            var unresolved = Documents(output).Values.Single();
            check(((YamlScalarNode)((YamlMappingNode)unresolved["m_SourcePrefab"])["guid"]).Value == "missing", "Missing source provenance was silently erased.");
        }, ("branch", Instance(20, "missing"))));

        test("Composition reserves all reachable identities before allocation", () => Sources(paths =>
        {
            string output = UnityPrefabComposer.Compose(Instance(10, "first") + Instance(20, "branch"), paths);
            var documents = Documents(output);
            check(documents.Count == 2 && documents.ContainsKey(long.MaxValue - 1) && documents.ContainsKey(long.MaxValue - 3), "Later transitive source identity was not reserved before the first allocation.");
            check(Name(documents[long.MaxValue - 1]) == "First", "Allocation order changed.");
        }, ("first", Object(1, "First")), ("branch", Instance(30, "last")), ("last", Object(long.MaxValue, "Last"))));

        test("Composition ignores unrelated identities and dictionary order", () => Sources(paths =>
        {
            var selected = new Dictionary<string, string> { ["good"] = paths["good"] };
            string expected = UnityPrefabComposer.Compose(Instance(10, "good"), selected);
            var reversed = paths.Reverse().ToDictionary(entry => entry.Key, entry => entry.Value);
            check(UnityPrefabComposer.Compose(Instance(10, "good"), paths) == expected && UnityPrefabComposer.Compose(Instance(10, "good"), reversed) == expected, "Unrelated identity or index order affected allocated IDs.");
            check(Documents(expected).ContainsKey(long.MaxValue), "Unreachable identities must not reserve output IDs.");
        }, ("good", Object(1, "Good")), ("other", Object(long.MaxValue, "Other"))));

        test("Composition caches repeated sources without sharing mutable overrides", () => Sources(paths =>
        {
            string before = File.ReadAllText(paths["good"]);
            var documents = Documents(UnityPrefabComposer.Compose(Instance(10, "good", "First") + Alias(11, 1, 10, "good") + Instance(20, "GOOD", "Second") + Alias(21, 1, 20, "GOOD"), paths));
            check(documents.Count == 2 && Name(documents[11]) == "First" && Name(documents[21]) == "Second", "Source reuse leaked overrides or changed alias identities.");
            check(File.ReadAllText(paths["good"]) == before, "Composition mutated source YAML.");
            check(Name(Documents(UnityPrefabComposer.Compose(Instance(10, "good"), paths)).Values.Single()) == "Original", "A subsequent composition reused mutable output state.");
        }, ("good", Object(1, "Original"))));

        test("Concurrent compositions keep source and allocation state isolated", () => Sources(paths =>
        {
            string before = File.ReadAllText(paths["good"]);
            var outputs = new string[24];
            Parallel.For(0, outputs.Length, index => outputs[index] = UnityPrefabComposer.Compose(Instance(10, "good", "Output" + index), paths));
            for (int index = 0; index < outputs.Length; index++)
            {
                var documents = Documents(outputs[index]);
                check(documents.Count == 1 && documents.ContainsKey(long.MaxValue) && Name(documents[long.MaxValue]) == "Output" + index, "Concurrent composition shared an override or allocation state.");
            }
            check(File.ReadAllText(paths["good"]) == before, "Concurrent composition mutated its input.");
        }, ("good", Object(1, "Original"))));

        test("Composition rereads reachable sources on subsequent calls", () => Sources(paths =>
        {
            string first = UnityPrefabComposer.Compose(Instance(10, "good"), paths);
            File.WriteAllText(paths["good"], Object(1, "Updated"));
            string second = UnityPrefabComposer.Compose(Instance(10, "good"), paths);
            check(Name(Documents(first).Values.Single()) == "Original" && Name(Documents(second).Values.Single()) == "Updated", "A process-wide cache returned stale source data.");
        }, ("good", Object(1, "Original"))));

        test("Reachable source caching preserves strict unknown override errors", () => Sources(paths =>
        {
            Expect<InvalidDataException>(() => UnityPrefabComposer.Compose(Instance(10, "good", "Invalid", target: 999), paths));
        }, ("good", Object(1, "Original"))));

        test("Reachable source discovery terminates and expansion rejects cycles", () => Sources(paths =>
        {
            Expect<InvalidDataException>(() => UnityPrefabComposer.Compose(Instance(10, "first"), paths));
        }, ("first", Instance(20, "second")), ("second", Instance(30, "FIRST"))));
    }

    private static void Sources(Action<Dictionary<string, string>> run, params (string Guid, string Yaml)[] sources)
    {
        string root = Path.Combine(Path.GetTempPath(), "prefab-library-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = new Dictionary<string, string>();
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

    private static void Expect<TException>(Action run) where TException : Exception
    {
        try { run(); }
        catch (TException) { return; }
        throw new Exception("Expected explicit " + typeof(TException).Name + ".");
    }

    private static string Object(long id, string name) =>
        $"--- !u!1 &{id}\nGameObject:\n  m_Name: {name}\n";

    private static string Instance(long id, string guid, string? name = null, long target = 1) =>
        $"--- !u!1001 &{id}\nPrefabInstance:\n  m_Modification:\n    m_TransformParent: {{fileID: 0}}\n    m_Modifications:{(name == null ? " []\n" : $"\n    - target: {{fileID: {target}, guid: {guid}, type: 3}}\n      propertyPath: m_Name\n      value: {name}\n      objectReference: {{fileID: 0}}\n")}  m_SourcePrefab: {{fileID: 100100000, guid: {guid}, type: 3}}\n";

    private static string Alias(long id, long source, long instance, string guid) =>
        $"--- !u!1 &{id} stripped\nGameObject:\n  m_CorrespondingSourceObject: {{fileID: {source}, guid: {guid}, type: 3}}\n  m_PrefabInstance: {{fileID: {instance}}}\n";

    private static string Name(YamlMappingNode body) => ((YamlScalarNode)body["m_Name"]).Value!;

    private static Dictionary<long, YamlMappingNode> Documents(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(Regex.Replace(AvatarPackageIndex.NormalizeDocumentHeaders(yaml), @"(?m)^(--- )!u!(\d+)", "$1!<tag:unity3d.com,2011:$2>")));
        return stream.Documents.ToDictionary(document => long.Parse(document.RootNode.Anchor.Value, CultureInfo.InvariantCulture),
            document => (YamlMappingNode)((YamlMappingNode)document.RootNode).Children.Values.Single());
    }
}
