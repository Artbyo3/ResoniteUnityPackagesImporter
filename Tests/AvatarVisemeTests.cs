using UnityPackageImporter.Models;

internal static class AvatarVisemeTests
{
    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        string[] names = Enumerable.Range(0, 15).Select(index => "custom: mouth #" + index).ToArray();
        AvatarVisemeSettings Settings() => new(3, new UnityAssetReference { FileId = 137001 }, names.ToArray());
        void Reject(AvatarVisemeSettings settings, IReadOnlyList<string>? source = null)
        {
            try { AvatarVisemePlanner.Compile(settings, source ?? names); }
            catch (InvalidDataException) { return; }
            throw new Exception("Expected explicit unsupported-viseme rejection.");
        }
        string Descriptor(string fields) => "MonoBehaviour:\n" + fields;
        test("Visemes preserve all fifteen authored channel identities", () =>
        {
            var plan = AvatarVisemePlanner.Compile(Settings(), names.Reverse().ToArray());
            check(plan.Count == 15 && plan.Select(binding => binding.Channel).SequenceEqual(Enumerable.Range(0, 15)), "Channel ordering changed.");
            check(plan.All(binding => binding.Shape == names[binding.Channel] && binding.SourceIndex == 14 - binding.Channel), "Shape names were guessed or index order was assumed.");
        });
        test("Visemes preserve empty silence without inventing a laughter target", () =>
        {
            var shapes = names.ToArray(); shapes[0] = "";
            var plan = AvatarVisemePlanner.Compile(Settings() with { Shapes = shapes }, names);
            check(plan.Count == 14 && plan[0].Channel == 1 && plan.All(binding => binding.Channel < 15), "Empty silence or native laughter was mapped incorrectly.");
        });
        test("Visemes parse exact quoted and null scalar shape entries", () =>
        {
            var parsed = AvatarVisemeSettings.Parse(Descriptor("  lipSync: 3\n  VisemeSkinnedMesh: {fileID: -137001}\n  VisemeBlendShapes: ['custom: mouth #1', '', ]\n"));
            check(parsed.Mode == 3 && parsed.Renderer?.FileId == -137001 && parsed.Shapes.SequenceEqual(new[] { names[1], "" }) && parsed.Error == "", "Authored scalars were altered.");
        });
        test("Visemes retain missing mode instead of treating it as zero", () =>
        {
            var parsed = AvatarVisemeSettings.Parse(Descriptor("  nested: {lipSync: 3}\n"));
            check(parsed.Mode == null && parsed.Error == "", "Nested or absent mode became authored lip sync.");
        });
        test("Visemes index descriptor settings from the exact serialized component", () =>
        {
            string scene = "--- !u!1 &1\nGameObject:\n  m_Name: Avatar\n--- !u!114 &7\nMonoBehaviour:\n  m_GameObject: {fileID: 1}\n  m_Script: {fileID: " +
                AvatarPackageIndex.VrcAvatarDescriptorScriptFileId + ", guid: " + AvatarPackageIndex.VrcAvatarDescriptorScriptGuid + "}\n  lipSync: 3\n  VisemeSkinnedMesh: {fileID: 137001}\n  VisemeBlendShapes:\n" +
                string.Concat(names.Select(name => "  - '" + name + "'\n"));
            var settings = AvatarPackageIndex.ParseText(scene, "Avatar.prefab").Avatars.Single().Visemes;
            check(settings.Mode == 3 && settings.Renderer.FileId == 137001 && settings.Shapes.SequenceEqual(names), "Descriptor indexing lost ordered settings.");
        });
        foreach (int? mode in new int?[] { null, 0, 1, 2, 4, 5, -1 })
            test("Visemes explicitly gate unsupported mode " + mode, () => Reject(Settings() with { Mode = mode }));
        foreach (int count in new[] { 0, 1, 14, 16 })
            test("Visemes reject wrong ordered channel count " + count, () => Reject(Settings() with { Shapes = Enumerable.Repeat("", count).ToArray() }));
        test("Visemes reject all empty channels", () => Reject(Settings() with { Shapes = Enumerable.Repeat("", 15).ToArray() }));
        test("Visemes reject duplicate channel destinations rather than breaking links", () =>
        {
            var shapes = names.ToArray(); shapes[8] = shapes[4];
            Reject(Settings() with { Shapes = shapes });
        });
        test("Visemes reject missing exact names even when a heuristic alternative exists", () =>
        {
            var shapes = names.ToArray(); shapes[1] = "PP";
            Reject(Settings() with { Shapes = shapes });
        });
        test("Visemes keep shape identities case sensitive", () => Reject(Settings(), names.Select(name => name.ToUpperInvariant()).ToArray()));
        test("Visemes reject ambiguous mesh shape identities", () => Reject(Settings(), names.Concat(new[] { names[0] }).ToArray()));
        test("Visemes reject empty mesh shape identities", () => Reject(Settings(), names.Concat(new[] { "" }).ToArray()));
        foreach (var reference in new UnityAssetReference?[] { null, new() { FileId = 0 }, new() { FileId = 137001, Guid = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" } })
            test("Visemes reject absent null or external renderer " + reference?.Guid + reference?.FileId, () => Reject(Settings() with { Renderer = reference }));
        foreach (string fields in new[]
        {
            "  lipSync: [3]\n", "  lipSync: wrong\n", "  lipSync: 3\n  VisemeSkinnedMesh: wrong\n",
            "  lipSync: 3\n  VisemeSkinnedMesh: {fileID: wrong}\n", "  lipSync: 3\n  VisemeSkinnedMesh: {fileID: 1, guid: []}\n",
            "  lipSync: 3\n  VisemeBlendShapes: wrong\n", "  lipSync: 3\n  VisemeBlendShapes: [{name: wrong}]\n",
            "  lipSync: 3\n  lipSync: 4\n"
        })
            test("Visemes retain malformed descriptor diagnostic " + fields.Trim(), () =>
            {
                var settings = AvatarVisemeSettings.Parse(Descriptor(fields));
                check(settings.Error.Length != 0, "Malformed descriptor silently appeared valid.");
                Reject(settings);
            });
        test("Visemes propagate preflight cancellation", () =>
        {
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { AvatarVisemePlanner.Compile(Settings(), names, cancelled.Token); }
            catch (OperationCanceledException) { return; }
            throw new Exception("Cancellation was swallowed.");
        });
    }
}
