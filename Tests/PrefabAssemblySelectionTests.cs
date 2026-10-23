#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityPackageImporter.Models;

internal static class PrefabAssemblySelectionTests
{
    private const string Assembly = "00000000000000000000000000000001";
    private const string Avatar = "00000000000000000000000000000002";
    private const string Outfit = "00000000000000000000000000000003";
    private const string Model = "00000000000000000000000000000004";
    private const string Other = "00000000000000000000000000000005";
    private const string Missing = "000000000000000000000000000000ff";
    private const string Plain = "--- !u!1 &1\nGameObject:\n  m_Name: Model\n";
    private const string Modular = "--- !u!114 &3\nMonoBehaviour:\n  m_GameObject: {fileID: 1}\n  m_Script: {fileID: 11500000, guid: 2df373bf91cf30b4bbd495e11cb1a2ec, type: 3}\n";

    public static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        void InTemp(Action<string> run)
        {
            string root = Path.Combine(Path.GetTempPath(), "prefab-assembly-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try { run(root); }
            finally { Directory.Delete(root, true); }
        }

        void Reject(Action action, string expected)
        {
            try { action(); }
            catch (InvalidDataException error)
            {
                check(error.Message.Contains(expected, StringComparison.OrdinalIgnoreCase),
                    "Expected diagnostic containing '" + expected + "', got: " + error.Message);
                return;
            }
            throw new Exception("Expected explicit InvalidDataException: " + expected);
        }

        test("Prefab assembly selection accepts an empty library", () =>
            check(PrefabAssemblySelection.Create(new Dictionary<string, string>()).Count == 0,
                "An empty library must not invent outputs."));

        test("Prefab assembly index-to-batch integration imports the assembly, not MA library dependencies", () => InTemp(root =>
        {
            var index = Build(root,
                (Assembly, "Variant", Instance(Outfit, 10) + Instance(Avatar, 20)),
                (Avatar, "Avatar", Plain + Instance(Model, 30)),
                (Outfit, "Outfit", Plain + Modular + Instance(Model, 40)),
                (Model, "Model", Plain));
            var assetsBefore = index.Assets.ToArray();
            var prefabsBefore = index.Prefabs.ToArray();
            var selected = PrefabAssemblySelection.Create(index.Prefabs);
            var invoked = new List<string>();
            var failures = new List<string>();
            var result = SourceImportBatch.RunAsync(selected, (source, _) =>
            {
                invoked.Add(source.Key);
                if (source.Key == Outfit)
                    throw new InvalidDataException("Dependent MA output must not be imported independently.");
                check(index.Prefabs.ContainsKey(Avatar) && index.Prefabs.ContainsKey(Outfit) && index.Assets.ContainsKey(Model),
                    "Source reconstruction lost its dependency library.");
                return Task.CompletedTask;
            }, (source, _) =>
            {
                failures.Add(source.Key);
                return Task.CompletedTask;
            }, maxDegreeOfParallelism: 1).GetAwaiter().GetResult();
            check(selected.Select(source => source.Key).SequenceEqual(new[] { Assembly }), "Only the authored assembly is an output.");
            check(invoked.SequenceEqual(new[] { Assembly }) && failures.Count == 0 && result.Failed.Count == 0 && result.Succeeded.Count == 1,
                "A dependency-only MA source contaminated the batch outcome.");
            check(index.Assets.SequenceEqual(assetsBefore) && index.Prefabs.SequenceEqual(prefabsBefore),
                "Selection mutated the full asset/prefab library.");
        }));

        test("Prefab assembly selection preserves unrelated outputs and shared dependencies deterministically", () => InTemp(root =>
        {
            var index = Build(root,
                (Other, "Unrelated MA", Plain + Modular),
                (Avatar, "Second assembly", Instance(Model)),
                (Model, "Shared model", Plain),
                (Assembly, "First assembly", Instance(Model) + Instance(Model, 3)));
            var reverse = index.Prefabs.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value);
            var expected = new[] { Assembly, Avatar, Other };
            check(PrefabAssemblySelection.Create(index.Prefabs).Select(pair => pair.Key).SequenceEqual(expected),
                "Selection removed an unrelated root or mishandled a shared/repeated dependency.");
            check(PrefabAssemblySelection.Create(reverse).SequenceEqual(PrefabAssemblySelection.Create(index.Prefabs)),
                "Selection depends on dictionary enumeration order.");
        }));

        test("Prefab assembly selection uses class-1001 direct source references only", () => InTemp(root =>
        {
            string decoys = Plain +
                "  m_SourcePrefab: {fileID: 100100000, guid: " + Avatar + ", type: 3}\n" +
                "--- !u!114 &7\nMonoBehaviour:\n" +
                "  reference: {fileID: 100100000, guid: " + Avatar + ", type: 3}\n" +
                "  text: |\n    PrefabInstance:\n      m_SourcePrefab: {fileID: 100100000, guid: " + Avatar + ", type: 3}\n" +
                "--- !u!1001 &8\nPrefabInstance:\n" +
                "  m_SourcePrefab: {fileID: 0}\n" +
                "  m_Modification:\n    m_SourcePrefab: {fileID: 100100000, guid: " + Avatar + ", type: 3}\n" +
                "--- !u!1 &9\nPrefabInstance:\n  m_SourcePrefab: {fileID: 100100000, guid: " + Avatar + ", type: 3}\n";
            var index = Build(root, (Assembly, "Decoys", decoys), (Avatar, "Independent", Plain));
            check(PrefabAssemblySelection.Create(index.Prefabs).Count == 2,
                "A non-structural GUID reference suppressed an independent output.");
        }));

        test("Prefab assembly selection handles Unity directives, stripped documents and multiline sources", () => InTemp(root =>
        {
            string yaml = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n" + Plain +
                "--- !u!4 &-10 stripped\nTransform:\n  m_CorrespondingSourceObject: {fileID: 1, guid: " + Other + "}\n" +
                "--- !u!1001 &11\nPrefabInstance:\n  m_SourcePrefab:\n    fileID: 100100000\n    guid: " + Avatar + "\n    type: 3\n";
            var index = Build(root, (Assembly, "Variant", yaml), (Avatar, "Avatar", Plain), (Other, "Other", Plain));
            check(PrefabAssemblySelection.Create(index.Prefabs).Select(pair => pair.Key).SequenceEqual(new[] { Assembly, Other }),
                "Unity document formatting changed structural selection.");
        }));

        test("Prefab assembly selection matches indexed GUIDs case-insensitively", () => InTemp(root =>
        {
            var index = Build(root, (Assembly, "Variant", Instance(Missing.ToUpperInvariant())), (Missing, "Dependency", Plain));
            check(PrefabAssemblySelection.Create(index.Prefabs).Single().Key == Assembly,
                "GUID casing split one assembly into independent outputs.");
        }));

        test("Prefab assembly selection leaves missing and non-prefab references to runtime", () => InTemp(root =>
        {
            Build(root, (Assembly, "Variant", Instance(Missing) + Instance(Model, 3)), (Other, "Standalone", Plain));
            string fbx = Path.Combine(root, "Mesh.fbx");
            File.WriteAllText(fbx, "model payload");
            File.WriteAllText(fbx + ".meta", "guid: " + Model + "\n");
            var index = UnityPackageAssetIndex.Build(Directory.GetFiles(root));
            check(index.Assets.ContainsKey(Model) && !index.Prefabs.ContainsKey(Model), "Fixture did not index a model dependency.");
            var selected = PrefabAssemblySelection.Create(index.Prefabs);
            check(selected.Select(pair => pair.Key).SequenceEqual(new[] { Assembly, Other }),
                "Missing/external sources must not invent assembly edges.");
            var result = SourceImportBatch.RunAsync(selected, (source, _) =>
            {
                if (source.Key == Assembly) throw new InvalidDataException("Missing source at runtime.");
                return Task.CompletedTask;
            }, (_, _) => Task.CompletedTask, maxDegreeOfParallelism: 1).GetAwaiter().GetResult();
            check(result.Failed.Single().Key == Assembly && result.Succeeded.Single().Key == Other,
                "Selection hid a runtime missing-source failure or failed an unrelated output.");
        }));

        test("Prefab assembly selection never falls back to dependent MA outputs after assembly failure", () => InTemp(root =>
        {
            var index = Build(root, (Assembly, "Variant", Instance(Outfit)), (Outfit, "Outfit", Plain + Modular));
            int invocations = 0;
            var result = SourceImportBatch.RunAsync(PrefabAssemblySelection.Create(index.Prefabs), (_, _) =>
            {
                invocations++;
                throw new InvalidDataException("Assembly reconstruction failed.");
            }, (_, _) => Task.CompletedTask, maxDegreeOfParallelism: 1).GetAwaiter().GetResult();
            check(invocations == 1 && result.Succeeded.Count == 0 && result.Failed.Single().Key == Assembly,
                "Assembly failure caused a dependent MA fallback output.");
        }));

        test("Prefab assembly selection ignores conflicted indexed GUIDs without discarding their owner", () => InTemp(root =>
        {
            var index = Build(root, (Assembly, "Variant", Instance(Avatar)),
                (Avatar, "Conflicting one", Plain), (Avatar, "Conflicting two", Plain.Replace("Model", "Different")));
            check(index.ConflictedGuids.Contains(Avatar) && !index.Prefabs.ContainsKey(Avatar), "Fixture did not create a GUID conflict.");
            check(PrefabAssemblySelection.Create(index.Prefabs).Single().Key == Assembly,
                "A conflicted source reference removed the owning assembly.");
        }));

        test("Prefab assembly selection rejects self references and isolated cycles", () => InTemp(root =>
        {
            var self = Build(root, (Assembly, "Self", Instance(Assembly)));
            Reject(() => PrefabAssemblySelection.Create(self.Prefabs), "cycle");
            var cycle = Build(root, (Avatar, "Cycle one", Instance(Outfit)), (Outfit, "Cycle two", Instance(Avatar)));
            Reject(() => PrefabAssemblySelection.Create(cycle.Prefabs), "cycle");
        }));

        test("Prefab assembly selection rejects dependency cycles even with valid top-level roots", () => InTemp(root =>
        {
            var index = Build(root, (Assembly, "Variant", Instance(Avatar)),
                (Avatar, "Cycle one", Instance(Outfit)), (Outfit, "Cycle two", Instance(Avatar)), (Other, "Unrelated", Plain));
            Reject(() => PrefabAssemblySelection.Create(index.Prefabs), "cycle");
        }));

        test("Prefab assembly selection rejects unreadable indexed dependencies explicitly", () => InTemp(root =>
        {
            var index = Build(root, (Assembly, "Variant", Instance(Avatar)), (Avatar, "Deleted dependency", Plain));
            File.Delete(index.Prefabs[Avatar]);
            Reject(() => PrefabAssemblySelection.Create(index.Prefabs), "Cannot read indexed prefab");
        }));

        test("Prefab assembly selection rejects malformed YAML and structural source references", () => InTemp(root =>
        {
            var malformed = new[]
            {
                "",
                "not a mapping\n",
                "--- !u!1001 &1\nPrefabInstance: [\n",
                "--- !u!1001 &1\nPrefabInstance: {}\n",
                "--- !u!1001 &1\nPrefabInstance:\n  m_SourcePrefab: invalid\n",
                "--- !u!1001 &1\nPrefabInstance:\n  m_SourcePrefab: {fileID: invalid, guid: " + Avatar + "}\n",
                "--- !u!1001 &1\nPrefabInstance:\n  m_SourcePrefab: {fileID: 100100000}\n",
                "--- !u!1001 &1\nPrefabInstance:\n  m_SourcePrefab: {fileID: 100100000, guid: [invalid]}\n"
            };
            foreach (string yaml in malformed)
            {
                var index = Build(root, (Assembly, "Malformed", yaml), (Avatar, "Avatar", Plain));
                Reject(() => PrefabAssemblySelection.Create(index.Prefabs), "prefab");
                var dependency = Build(root, (Assembly, "Owner", Instance(Avatar)), (Avatar, "Malformed dependency", yaml));
                Reject(() => PrefabAssemblySelection.Create(dependency.Prefabs), "prefab");
            }
        }));

        test("Prefab assembly selection rejects ambiguous case-insensitive library identities", () => InTemp(root =>
        {
            var index = Build(root, (Missing, "Prefab", Plain));
            var ambiguous = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Missing] = index.Prefabs[Missing],
                [Missing.ToUpperInvariant()] = index.Prefabs[Missing]
            };
            Reject(() => PrefabAssemblySelection.Create(ambiguous), "collision");
            Reject(() => PrefabAssemblySelection.Create(new Dictionary<string, string> { [Assembly] = " " }), "empty");
        }));
    }

    private static UnityPackageAssetIndex Build(string root, params (string Guid, string Name, string Yaml)[] prefabs)
    {
        var files = new List<string>();
        foreach (var prefab in prefabs)
        {
            string path = Path.Combine(root, prefab.Name + ".prefab");
            File.WriteAllText(path, prefab.Yaml);
            File.WriteAllText(path + ".meta", "fileFormatVersion: 2\nguid: " + prefab.Guid + "\nPrefabImporter: {}\n");
            files.Add(path);
            files.Add(path + ".meta");
        }
        return UnityPackageAssetIndex.Build(files);
    }

    private static string Instance(string guid, long id = 2) =>
        "--- !u!1001 &" + id + "\nPrefabInstance:\n  m_SourcePrefab: {fileID: 100100000, guid: " + guid + ", type: 3}\n";
}
