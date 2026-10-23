using System.Globalization;
using UnityPackageImporter.Models;
using YamlDotNet.RepresentationModel;

internal static class ComposedInitialAppearanceTests
{
    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        test("Composed prefab defaults flow from MA Shape Set into clothing Blendshape Sync", () => Run(check, false));
        test("Unrelated malformed library cannot change composed initial appearance", () => Run(check, true));
    }

    private static void Run(Action<bool, string> check, bool includeUnrelated)
    {
        string directory = Path.Combine(Path.GetTempPath(), "importer-appearance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Object(1, "Avatar", "[{component: {fileID: 11}}, {component: {fileID: 100}}]") +
                Transform(11, 1, 0, "[{fileID: 12}, {fileID: 13}]") +
                Object(2, "Body", "[{component: {fileID: 12}}, {component: {fileID: 20}}]") + Transform(12, 2, 11, "[]") +
                Object(3, "Clothes", "[{component: {fileID: 13}}, {component: {fileID: 30}}, {component: {fileID: 101}}]") + Transform(13, 3, 11, "[]") +
                Skin(20, 2) + Skin(30, 3) +
                $"--- !u!114 &100\nMonoBehaviour:\n  m_GameObject: {{fileID: 1}}\n  m_Script: {{fileID: 11500000, guid: {ModularAvatarInitialAppearance.ShapeChangerGuid}, type: 3}}\n  m_inverted: 0\n  m_shapes:\n  - Object:\n      referencePath: Body\n      targetObject: {{fileID: 2}}\n    ShapeName: Size\n    ChangeType: 1\n    Value: 73\n" +
                $"--- !u!114 &101\nMonoBehaviour:\n  m_GameObject: {{fileID: 3}}\n  m_Script: {{fileID: 11500000, guid: {AvatarPackageIndex.BlendshapeSyncScriptGuid}, type: 3}}\n  Bindings:\n  - ReferenceMesh:\n      referencePath: Body\n      targetObject: {{fileID: 2}}\n    Blendshape: Size\n    LocalBlendshape: Fit\n";
            string path = Path.Combine(directory, "assembly.prefab");
            File.WriteAllText(path, source);
            var library = new Dictionary<string, string> { ["assembly"] = path };
            if (includeUnrelated) library.Add("unrelated", Path.Combine(directory, "does-not-exist.prefab"));
            string composed = UnityPrefabComposer.Compose("--- !u!1001 &500\nPrefabInstance:\n  m_Modification:\n    m_TransformParent: {fileID: 0}\n    m_Modifications: []\n  m_SourcePrefab: {fileID: 100100000, guid: assembly, type: 3}\n", library);
            var docs = BlendshapeInitialAppearance.ReadDocuments(composed).ToDictionary(pair => pair.Key,
                pair => (YamlMappingNode)pair.Value.Children.Values.Single());
            long Named(string name) => docs.Single(pair => pair.Value.Children.TryGetValue("m_Name", out var node) && ((YamlScalarNode)node).Value == name).Key;
            long Owner(YamlMappingNode body) => long.Parse(((YamlScalarNode)((YamlMappingNode)body["m_GameObject"])["fileID"]).Value!, CultureInfo.InvariantCulture);
            long root = Named("Avatar"), bodyId = Named("Body"), clothesId = Named("Clothes");
            long bodySkin = docs.Single(pair => pair.Value.Children.ContainsKey("m_BlendShapeWeights") && Owner(pair.Value) == bodyId).Key;
            long clothesSkin = docs.Single(pair => pair.Value.Children.ContainsKey("m_BlendShapeWeights") && Owner(pair.Value) == clothesId).Key;
            var snapshots = new Dictionary<long, InitialAppearanceRenderer>
            {
                [bodySkin] = new(bodyId, new Dictionary<string, float> { ["Size"] = 12 }, Array.Empty<InitialAppearanceMaterial>()),
                [clothesSkin] = new(clothesId, new Dictionary<string, float> { ["Fit"] = 0 }, Array.Empty<InitialAppearanceMaterial>())
            };
            var manifest = AvatarPackageIndex.ParseText(composed, "synthetic-assembly.prefab");
            var plan = AvatarBuildPlanner.CreatePlan(manifest, "synthetic");
            check(!plan.HasErrors, "Composed assembly failed structural planning.");
            var defaults = ModularAvatarInitialAppearance.Evaluate(composed, new InitialAppearanceContext
            {
                AvatarRootId = root, Renderers = snapshots, ExternalAnimationAndParameterContextVerifiedAbsent = true
            });
            check(defaults.CanApply, string.Join("; ", defaults.Diagnostics.Select(item => item.Code + ": " + item.Message)));
            check(defaults.Renderers[bodySkin].Shapes["Size"] == 73, "Authored body shape default was not evaluated.");
            var objects = new Dictionary<long, BlendshapeAppearanceObject>
            {
                [root] = new(root, root, "Avatar", ""), [bodyId] = new(bodyId, root, "Body", "Body"), [clothesId] = new(clothesId, root, "Clothes", "Clothes")
            };
            var renderers = defaults.Renderers.ToDictionary(pair => pair.Key, pair => new BlendshapeAppearanceRenderer(pair.Key, pair.Value.GameObjectId, pair.Value.Shapes));
            var linked = BlendshapeInitialAppearance.Evaluate(composed, objects, renderers);
            check(!linked.HasErrors && linked.Writes.Single().RendererId == clothesSkin && linked.Writes[0].Shape == "Fit" && linked.Writes[0].UnityWeight == 73,
                "Clothing sync did not consume evaluated body appearance after composition.");
            check(snapshots[bodySkin].Shapes["Size"] == 12 && snapshots[clothesSkin].Shapes["Fit"] == 0, "Pipeline mutated source snapshots.");
            check(File.ReadAllText(path) == source, "Composition or appearance evaluation mutated the shared source prefab.");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static string Object(long id, string name, string components) => $"--- !u!1 &{id}\nGameObject:\n  m_Name: {name}\n  m_IsActive: 1\n  m_Component: {components}\n";
    private static string Transform(long id, long owner, long parent, string children) => $"--- !u!4 &{id}\nTransform:\n  m_GameObject: {{fileID: {owner}}}\n  m_Father: {{fileID: {parent}}}\n  m_Children: {children}\n";
    private static string Skin(long id, long owner) => $"--- !u!137 &{id}\nSkinnedMeshRenderer:\n  m_GameObject: {{fileID: {owner}}}\n  m_BlendShapeWeights: [0]\n";
}
