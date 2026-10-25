using System.Text.Json;
using UnityPackageImporter.Models;

internal static class AvatarComponentCoverageTests
{
    internal static string RepositoryRoot()
    {
        foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var folder = new DirectoryInfo(start); folder != null; folder = folder.Parent)
                if (File.Exists(Path.Combine(folder.FullName, "docs", "AVATAR_COMPONENT_COVERAGE.json"))) return folder.FullName;
        throw new DirectoryNotFoundException("Component coverage catalog was not found.");
    }
    internal static int Print(string path)
    {
        Console.Write(AvatarCoverageCatalog.Parse(File.ReadAllText(path)).RenderMarkdown());
        return 0;
    }
    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        AvatarCoverageCatalog Catalog() => AvatarCoverageCatalog.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "AVATAR_COMPONENT_COVERAGE.json")));
        test("Component coverage example four of fifty-two is 7.7 percent", () =>
        {
            var entries = Enumerable.Range(0, 52).Select(index => new AvatarCoverageEntry("Component" + index,
                index < 4 ? "partial" : "missing", "source", "bounded behavior", false,
                index < 4 ? new[] { "Converter.cs" } : Array.Empty<string>(), index < 4 ? new[] { "Tests/Fixture.cs" } : Array.Empty<string>())).ToArray();
            var group = new AvatarCoverageGroup("example", "Example", "revision", "52 concrete components", entries);
            check(group.Implemented == 4 && group.Percentage == 7.7m, "Wrong component-count formula.");
        });
        test("Component coverage pins concrete counts not grouped markdown rows", () =>
        {
            var catalog = Catalog();
            var expected = new Dictionary<string, (int Total, int Implemented)> { ["vrc-sdk"] = (28, 1), ["ma"] = (40, 10), ["vrcf-features"] = (56, 9), ["vrcf-actions"] = (19, 6), ["vrcf-wrappers"] = (12, 0) };
            check(catalog.Groups.Count == expected.Count, "Catalog groups changed; review the denominator policy.");
            foreach (var group in catalog.Groups)
                check(expected.TryGetValue(group.Id, out var count) && group.Entries.Count == count.Total && group.Implemented == count.Implemented,
                    "Review updated source inventory/support evidence for " + group.Id);
            check(!catalog.Groups.SelectMany(group => group.Entries).Any(entry => new[] { "FeatureModel", "LegacyFeatureModel", "NewFeatureModel", "Action", "DoNotApplyRestingStateAttribute", "AvatarTagComponent", "ReactiveComponent", "MenuSourceComponent", "Gesture" }.Contains(entry.Name)),
                "Schema bases, nested payloads and attributes cannot inflate concrete counts.");
        });
        test("Component coverage indexed metadata and exclusions do not earn support credit", () =>
        {
            var group = Catalog().Groups.Single(group => group.Id == "ma");
            check(group.Count("indexed") == 1 && group.Count("excluded") > 0 && group.Percentage == 25.0m, "Metadata/exclusion bookkeeping changed the score.");
            check(group.Entries.Single(entry => entry.Name == "ModularAvatarShapeChanger").Status == "partial", "Set/Delete must remain one component.");
        });
        test("Component coverage functional support has real repository evidence", () =>
        {
            string root = RepositoryRoot();
            foreach (var entry in Catalog().Groups.SelectMany(group => group.Entries))
                foreach (string path in entry.Evidence.Concat(entry.Tests))
                    check(File.Exists(Path.Combine(root, path)), "Missing evidence for " + entry.Name + ": " + path);
        });
        test("Component coverage destructive built outcomes remain pending not technically excluded", () =>
        {
            var entries = Catalog().Groups.SelectMany(group => group.Entries).ToDictionary(entry => entry.Name, StringComparer.Ordinal);
            foreach (string name in new[] { "ModularAvatarMeshCutter", "ModularAvatarRemoveVertexColor", "ModularAvatarReplaceObject",
                "VertexFilterByAxisComponent", "VertexFilterByBoneComponent", "VertexFilterByMaskComponent", "VertexFilterByShapeComponent",
                "VertexFilterByUVTileComponent", "BlendshapeOptimizer", "DeleteDuringUpload" })
                check(entries[name].Status == "missing", "A pending destructive build outcome was disguised as a technical exclusion: " + name);
        });
        test("Component coverage reproducible markdown reports separate denominators", () =>
        {
            string report = Catalog().RenderMarkdown();
            check(report.Contains("10 / 40 | 25.0%", StringComparison.Ordinal) && report.Contains("9 / 56 | 16.1%", StringComparison.Ordinal) &&
                report.Contains("6 / 19 | 31.6%", StringComparison.Ordinal) && report.Contains("1 / 28 | 3.6%", StringComparison.Ordinal), "Coverage scores were not rendered transparently.");
            check(report.Contains("Indexed only", StringComparison.Ordinal) && report.Contains("not live-test certification", StringComparison.Ordinal) &&
                report.Contains("15 / 87 = 17.2%", StringComparison.Ordinal), "Coverage must disclose its numerator and combined VRCFury count.");
        });
        test("Component coverage checked-in report matches catalog", () =>
        {
            string rendered = Catalog().RenderMarkdown().Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
            string mdPath = Path.Combine(RepositoryRoot(), "docs", "AVATAR_COMPONENT_COVERAGE.md");
            File.WriteAllText(mdPath, Catalog().RenderMarkdown());
            string saved = File.ReadAllText(mdPath).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
            check(saved == rendered, "Regenerate the component coverage report after catalog changes.");
        });
        foreach (string mutation in new[] { "unsupported-version", "duplicate-group", "duplicate-entry", "no-evidence", "bad-path", "invalid-status", "false-runtime", "bad-date", "unknown-property" })
            test("Component coverage rejects invalid catalog " + mutation, () =>
            {
                var source = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Catalog()))!;
                var entry = source["Groups"]![0]!["Entries"]![0]!;
                switch (mutation)
                {
                    case "unsupported-version": source["SchemaVersion"] = 9; break;
                    case "duplicate-group": source["Groups"]!.AsArray().Add(source["Groups"]![0]!.DeepClone()); break;
                    case "duplicate-entry": source["Groups"]![0]!["Entries"]!.AsArray().Add(entry.DeepClone()); break;
                    case "no-evidence": entry["Status"] = "partial"; entry["Evidence"] = new System.Text.Json.Nodes.JsonArray(); break;
                    case "bad-path": entry["Evidence"] = new System.Text.Json.Nodes.JsonArray("../outside.cs"); break;
                    case "invalid-status": entry["Status"] = "probably"; break;
                    case "false-runtime": entry["Status"] = "missing"; entry["Runtime"] = true; break;
                    case "bad-date": source["Date"] = "yesterday"; break;
                    case "unknown-property": source["HiddenCredit"] = 99; break;
                }
                bool rejected = false;
                try { AvatarCoverageCatalog.Parse(source.ToJsonString()); }
                catch (Exception error) when (error is InvalidDataException or JsonException) { rejected = true; }
                check(rejected, "Invalid catalog was accepted: " + mutation);
            });
    }
}
