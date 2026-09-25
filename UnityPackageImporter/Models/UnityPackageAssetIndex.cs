using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

#nullable disable

namespace UnityPackageImporter.Models;

internal sealed class UnityPackageAssetIndex
{
    public Dictionary<string, string> Assets { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Prefabs { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Metas { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Scenes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> OtherFiles { get; } = new();
    public List<string> Conflicts { get; } = new();

    public static UnityPackageAssetIndex Build(IEnumerable<string> files)
    {
        var result = new UnityPackageAssetIndex();
        foreach (string file in files ?? Array.Empty<string>())
        {
            string extension = Path.GetExtension(file).ToLowerInvariant();
            if (extension != ".prefab" && extension != ".meta" && extension != ".unity")
                result.OtherFiles.Add(file);
            if (extension != ".meta") continue;

            string assetPath = file[..^extension.Length];
            string guid = ReadGuid(file);
            if (string.IsNullOrWhiteSpace(guid))
                throw new InvalidDataException("Unity metadata has no GUID: " + file);

            if (result.Assets.TryGetValue(guid, out string existingAsset))
            {
                if (!Path.GetFullPath(existingAsset).Equals(
                        Path.GetFullPath(assetPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    result.Conflicts.Add(
                        "GUID '" + guid + "' maps to both '" + existingAsset +
                        "' and '" + assetPath + "'. The first asset was used.");
                }
                continue;
            }

            result.Assets.Add(guid, assetPath);
            result.Metas.Add(guid, file);
            string assetExtension = Path.GetExtension(assetPath).ToLowerInvariant();
            if (assetExtension == ".prefab") result.Prefabs.Add(guid, assetPath);
            else if (assetExtension == ".unity") result.Scenes.Add(guid, assetPath);
        }
        return result;
    }

    private static string ReadGuid(string metadataPath)
    {
        foreach (string line in File.ReadLines(metadataPath))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("guid:", StringComparison.OrdinalIgnoreCase)) continue;
            return trimmed[5..].Trim();
        }
        return null;
    }
}
