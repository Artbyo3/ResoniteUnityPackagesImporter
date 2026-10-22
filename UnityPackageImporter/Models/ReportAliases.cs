#nullable enable
using System.Collections.Generic;
using System.Globalization;

namespace UnityPackageImporter.Models;

// Recreated for every report; original IDs/paths are keys only and never output.
internal sealed class ReportAliases
{
    private readonly Dictionary<(string, long), string> objects = new();
    private readonly Dictionary<string, string> models = new(System.StringComparer.Ordinal);
    internal string Object(long id, string? source = null)
    {
        if (id == 0) return "none";
        var key = (source ?? "", id);
        if (!objects.TryGetValue(key, out string? label))
            objects.Add(key, label = "Object " + (objects.Count + 1).ToString(CultureInfo.InvariantCulture));
        return label;
    }
    internal string Model(string source)
    {
        if (!models.TryGetValue(source, out string? label))
            models.Add(source, label = "Model " + (models.Count + 1).ToString(CultureInfo.InvariantCulture));
        return label;
    }

    internal static string Size(long? bytes)
    {
        if (!bytes.HasValue || bytes < 0) return "unknown";
        if (bytes == 0) return "empty";
        if (bytes < 1024) return "under 1 KiB";
        long lower = 1024;
        while (lower <= long.MaxValue / 2 && bytes >= lower * 2) lower *= 2;
        long unit = lower >= 1048576 ? 1048576 : 1024;
        string suffix = unit == 1048576 ? " MiB" : " KiB";
        return lower > long.MaxValue / 2 ? "at least " + (lower / unit).ToString(CultureInfo.InvariantCulture) + suffix :
            (lower / unit).ToString(CultureInfo.InvariantCulture) + "–" + (lower * 2 / unit).ToString(CultureInfo.InvariantCulture) + suffix;
    }
}
