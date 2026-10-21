#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UnityPackageImporter.Models;

internal sealed record PackageMaterialChoice(
    string Guid,
    string Name,
    string Folder,
    string PackagePath,
    (float r, float g, float b, float a) BaseColor = default);

/// <summary>Indexes serialized Unity materials for an explicit, per-slot picker.</summary>
internal sealed class PackageMaterialCatalog
{
    private readonly PackageMaterialChoice[] choices;

    public IReadOnlyList<PackageMaterialChoice> Choices => choices;
    public int Count => choices.Length;

    private PackageMaterialCatalog(IEnumerable<PackageMaterialChoice> choices)
    {
        this.choices = choices
            .OrderBy(choice => choice.Folder, StringComparer.OrdinalIgnoreCase)
            .ThenBy(choice => choice.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(choice => choice.Guid, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static PackageMaterialCatalog Build(
        IEnumerable<KeyValuePair<string, string>>? assetIndex,
        Func<string, string>? readMaterial = null)
    {
        readMaterial ??= File.ReadAllText;
        var result = new List<PackageMaterialChoice>();
        foreach (var asset in assetIndex ?? Array.Empty<KeyValuePair<string, string>>())
        {
            if (string.IsNullOrWhiteSpace(asset.Key) || string.IsNullOrWhiteSpace(asset.Value) ||
                !Path.GetExtension(asset.Value).Equals(".mat", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var data = UnityMaterialData.Parse(readMaterial(asset.Value));
                string name = string.IsNullOrWhiteSpace(data.Name)
                    ? Path.GetFileNameWithoutExtension(asset.Value)
                    : data.Name!.Trim();
                string packagePath = GetPackagePath(asset.Value);
                string folder = FolderOf(packagePath);
                var baseCol = data.Color("_Color", data.Color("_BaseColor", (0.8f, 0.8f, 0.8f, 1.0f)));
                result.Add(new PackageMaterialChoice(asset.Key, name, folder, packagePath, baseCol));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                               YamlDotNet.Core.YamlException or InvalidDataException)
            {
                // An unreadable optional candidate should not fail the avatar import.
            }
        }

        return new PackageMaterialCatalog(result);
    }

    public IReadOnlyList<PackageMaterialChoice> Search(string? query = null, string? folder = null)
    {
        string normalizedFolder = NormalizeFolder(folder);
        IEnumerable<PackageMaterialChoice> result = choices;
        if (normalizedFolder.Length > 0)
        {
            string prefix = normalizedFolder + "/";
            result = result.Where(choice => choice.Folder.Equals(normalizedFolder, StringComparison.OrdinalIgnoreCase) ||
                                           choice.Folder.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        string? normalizedQuery = query?.Trim();
        if (!string.IsNullOrEmpty(normalizedQuery))
            result = result.Where(choice => choice.Name.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
                                            choice.PackagePath.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase));
        return result.ToArray();
    }

    public IReadOnlyList<string> ChildFolders(string? parentFolder = null)
    {
        string parent = NormalizeFolder(parentFolder);
        string prefix = parent.Length == 0 ? "" : parent + "/";
        return choices
            .Select(choice => choice.Folder)
            .Where(folder => folder.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && folder.Length > prefix.Length)
            .Select(folder => folder[prefix.Length..].Split('/')[0])
            .Where(segment => segment.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(segment => segment, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetPackagePath(string path)
    {
        string[] segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        int assets = Array.FindIndex(segments, segment => segment.Equals("Assets", StringComparison.OrdinalIgnoreCase));
        if (assets >= 0)
        {
            var relative = segments.Skip(assets + 1).ToArray();
            if (relative.Length > 0)
                return string.Join("/", relative);
        }

        // Keep a useful package-relative display path when an asset is outside Assets.
        return segments.Length <= 2 ? string.Join("/", segments) : string.Join("/", segments[^2..]);
    }

    private static string FolderOf(string packagePath)
    {
        int slash = packagePath.LastIndexOf('/');
        return slash < 0 ? "" : packagePath[..slash];
    }

    /// <summary>
    /// Orders material picker choices so the slot's current material is always the first card,
    /// deduplicating catalog entries. If the current material is not in the catalog (e.g. runtime/missing),
    /// a runtime-material choice without a GUID is placed first.
    /// </summary>
    public static IReadOnlyList<PackageMaterialChoice> OrderPickerChoices(
        string? currentGuid,
        string currentName,
        (float r, float g, float b, float a) currentColor,
        IEnumerable<PackageMaterialChoice> catalogChoices,
        IEnumerable<PackageMaterialChoice>? allCatalogChoices = null)
    {
        var result = new List<PackageMaterialChoice>();
        var seenGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        PackageMaterialChoice? matching = null;
        if (!string.IsNullOrEmpty(currentGuid))
        {
            matching = catalogChoices.FirstOrDefault(c => c.Guid.Equals(currentGuid, StringComparison.OrdinalIgnoreCase))
                ?? allCatalogChoices?.FirstOrDefault(c => c.Guid.Equals(currentGuid, StringComparison.OrdinalIgnoreCase));
        }

        if (matching != null)
        {
            result.Add(matching);
            seenGuids.Add(matching.Guid);
        }
        else
        {
            // Runtime-material choice (no GUID: no thumbnail lookup)
            result.Add(new PackageMaterialChoice("", currentName, "", "", currentColor));
        }

        foreach (var choice in catalogChoices)
        {
            if (string.IsNullOrEmpty(choice.Guid) || seenGuids.Add(choice.Guid))
            {
                result.Add(choice);
            }
        }

        return result;
    }

    private static string NormalizeFolder(string? folder)
    {
        return (folder ?? "").Replace('\\', '/').Trim('/').Replace("//", "/");
    }
}

