using System;
using System.Collections.Generic;

namespace UnityPackageImporter.Models;

internal sealed class MaterialDependencySummary
{
    public int MissingAssetCount { get; init; }
    public int AffectedRendererCount { get; init; }
    public int MissingReferenceCount { get; init; }
    public IReadOnlyList<string> MissingAssetGuids { get; init; } = Array.Empty<string>();

    public bool HasMissingAssets => MissingAssetCount > 0;
}

internal static class MaterialDependencyDiagnostics
{
    public static MaterialDependencySummary Analyze(
        IEnumerable<IEnumerable<string>> rendererMaterialGuids,
        IEnumerable<string> availableAssetGuids)
    {
        var available = new HashSet<string>(
            availableAssetGuids ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int affectedRenderers = 0;
        int missingReferences = 0;

        if (rendererMaterialGuids != null)
        {
            foreach (var rendererGuids in rendererMaterialGuids)
            {
                bool rendererAffected = false;
                if (rendererGuids != null)
                {
                    foreach (string guid in rendererGuids)
                    {
                        if (string.IsNullOrWhiteSpace(guid) || available.Contains(guid))
                            continue;

                        missing.Add(guid);
                        missingReferences++;
                        rendererAffected = true;
                    }
                }

                if (rendererAffected) affectedRenderers++;
            }
        }

        var sortedMissing = new List<string>(missing);
        sortedMissing.Sort(StringComparer.OrdinalIgnoreCase);
        return new MaterialDependencySummary
        {
            MissingAssetCount = sortedMissing.Count,
            AffectedRendererCount = affectedRenderers,
            MissingReferenceCount = missingReferences,
            MissingAssetGuids = sortedMissing
        };
    }
}
