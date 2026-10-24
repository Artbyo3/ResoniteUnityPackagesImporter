#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace UnityPackageImporter.Models;

internal sealed record AvatarCoverageEntry(string Name, string Status, string Source, string Reason,
    bool Runtime, IReadOnlyList<string> Evidence, IReadOnlyList<string> Tests);
internal sealed record AvatarCoverageGroup(string Id, string Name, string Revision, string Boundary,
    IReadOnlyList<AvatarCoverageEntry> Entries)
{
    internal int Count(string status) => Entries.Count(entry => entry.Status == status);
    internal int Implemented => Count("supported") + Count("partial");
    internal decimal Percentage => Entries.Count == 0 ? 0 : Math.Round(100m * Implemented / Entries.Count, 1, MidpointRounding.AwayFromZero);
}
internal sealed record AvatarCoverageCatalog(int SchemaVersion, string Date, IReadOnlyList<AvatarCoverageGroup> Groups)
{
    private static readonly HashSet<string> Statuses = new(StringComparer.Ordinal) { "supported", "partial", "indexed", "missing", "excluded" };

    internal static AvatarCoverageCatalog Parse(string json)
    {
        var catalog = JsonSerializer.Deserialize<AvatarCoverageCatalog>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow });
        if (catalog == null || catalog.SchemaVersion != 1 || catalog.Groups == null || catalog.Groups.Count == 0 ||
            !DateOnly.TryParseExact(catalog.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new InvalidDataException("Unsupported or incomplete component coverage catalog.");
        var groups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in catalog.Groups)
        {
            if (group == null || string.IsNullOrWhiteSpace(group.Id) || !groups.Add(group.Id) || string.IsNullOrWhiteSpace(group.Name) ||
                string.IsNullOrWhiteSpace(group.Revision) || string.IsNullOrWhiteSpace(group.Boundary) || group.Entries == null || group.Entries.Count == 0)
                throw new InvalidDataException("Coverage groups require unique IDs, pinned sources, boundaries and concrete entries.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in group.Entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Name) || !names.Add(entry.Name) || !Statuses.Contains(entry.Status ?? "") ||
                    string.IsNullOrWhiteSpace(entry.Source) || string.IsNullOrWhiteSpace(entry.Reason) || entry.Evidence == null || entry.Tests == null)
                    throw new InvalidDataException("Coverage entries require unique concrete names, explicit status, source and limits.");
                bool implemented = entry.Status is "supported" or "partial";
                if ((implemented && (entry.Evidence.Count == 0 || entry.Tests.Count == 0)) || (entry.Runtime && !implemented))
                    throw new InvalidDataException("Implemented/runtime support requires conversion evidence and regression tests.");
                foreach (string path in entry.Evidence.Concat(entry.Tests))
                    if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(':') || path.Contains('\\') ||
                        path.Split('/').Any(segment => segment is ".." or "." or ""))
                        throw new InvalidDataException("Coverage evidence must use repository-relative paths.");
            }
        }
        return catalog;
    }

    internal string RenderMarkdown()
    {
        var output = new StringBuilder();
        output.AppendLine("# Avatar component support coverage").AppendLine();
        output.AppendLine("Generated from docs/AVATAR_COMPONENT_COVERAGE.json. Formula: (supported + partial) / all catalog entries x 100.");
        output.AppendLine("Partial receives one component credit only because a documented functional subset exists; it is not full fidelity. Indexed-only and excluded entries receive no credit. Exclusions remain in the denominator. Runtime is reported separately; this is not live-test certification.").AppendLine();
        output.AppendLine("| Catalog | Implemented / total | Coverage | Full | Partial | Indexed only | Missing | Excluded | Runtime subsets |");
        output.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var group in Groups)
            output.AppendLine("| " + Escape(group.Name) + " | " + group.Implemented + " / " + group.Entries.Count + " | " +
                group.Percentage.ToString("0.0", CultureInfo.InvariantCulture) + "% | " + group.Count("supported") + " | " + group.Count("partial") +
                " | " + group.Count("indexed") + " | " + group.Count("missing") + " | " + group.Count("excluded") + " | " + group.Entries.Count(entry => entry.Runtime) + " |");
        var fury = Groups.Where(group => group.Id.StartsWith("vrcf-", StringComparison.Ordinal)).ToArray();
        int furyTotal = fury.Sum(group => group.Entries.Count);
        if (furyTotal > 0)
            output.AppendLine().AppendLine("VRCFury combined concrete authoring-type coverage: " + fury.Sum(group => group.Implemented) + " / " + furyTotal + " = " +
                Math.Round(100m * fury.Sum(group => group.Implemented) / furyTotal, 1, MidpointRounding.AwayFromZero).ToString("0.0", CultureInfo.InvariantCulture) + "% (feature models + action models + wrapper components). The breakdown stays visible rather than changing denominators silently.");
        foreach (var group in Groups)
        {
            output.AppendLine().AppendLine("## " + Escape(group.Name)).AppendLine();
            output.AppendLine("Source revision: " + group.Revision + ". " + group.Boundary).AppendLine();
            output.AppendLine("| Concrete type | Status | Runtime subset | Implemented behavior / remaining gap | Code evidence | Test evidence | Upstream source |");
            output.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
            foreach (var entry in group.Entries)
                output.AppendLine("| " + Escape(entry.Name) + " | " + entry.Status + " | " + (entry.Runtime ? "yes" : "no") + " | " +
                    Escape(entry.Reason) + " | " + string.Join(", ", entry.Evidence.Select(Escape)) + " | " +
                    string.Join(", ", entry.Tests.Select(Escape)) + " | " + Escape(entry.Source) + " |");
        }
        return output.ToString();
    }
    private static string Escape(string value) => value.Replace("|", "&#124;", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
