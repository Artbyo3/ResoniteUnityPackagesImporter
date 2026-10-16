#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using YamlDotNet.RepresentationModel;

namespace UnityPackageImporter.Models;

internal sealed class ParsedModelMeta
{
    public float GlobalScale { get; set; } = 1f;
    public bool UseFileUnits { get; set; } = true;
    public bool UseFileScale { get; set; } = true;
    public Dictionary<long, string> FileIdToRecycleName { get; } = new();
    public Dictionary<string, ExternalObjectRef> ExternalObjects { get; } = new(StringComparer.Ordinal);
    public List<(string BoneName, string HumanName)> HumanBones { get; } = new();
}

internal readonly record struct ExternalObjectRef(long FileId, string Guid, int Type);

/// <summary>
/// Structured YAML parser for Unity <c>.fbx.meta</c> (<c>ModelImporter</c>) files using
/// <see cref="YamlStream"/> instead of fragile line-by-line indentation state machines.
/// </summary>
internal static class ModelMetaParser
{
    internal static ParsedModelMeta ParseFile(string metaFilePath)
    {
        if (string.IsNullOrEmpty(metaFilePath) || !File.Exists(metaFilePath))
            return new ParsedModelMeta();

        return Parse(File.ReadAllText(metaFilePath));
    }

    internal static ParsedModelMeta Parse(string yamlContent)
    {
        var result = new ParsedModelMeta();
        if (string.IsNullOrWhiteSpace(yamlContent))
            return result;

        try
        {
            var yaml = new YamlStream();
            using var reader = new StringReader(yamlContent);
            yaml.Load(reader);
            if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root)
                return result;

            YamlMappingNode? importerNode = GetMapping(root, "ModelImporter") ?? root;
            ReadScaleProperties(importerNode, result);

            if (GetMapping(importerNode, "meshes") is YamlMappingNode meshesNode)
            {
                ReadScaleProperties(meshesNode, result);
            }

            if (GetMapping(importerNode, "fileIDToRecycleName") is YamlMappingNode recycleMap)
            {
                foreach (var entry in recycleMap.Children)
                {
                    if (entry.Key is YamlScalarNode keyScalar &&
                        entry.Value is YamlScalarNode valScalar &&
                        long.TryParse(keyScalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long fileId) &&
                        fileId != 0)
                    {
                        result.FileIdToRecycleName[fileId] = valScalar.Value ?? string.Empty;
                    }
                }
            }

            if (GetNode(importerNode, "externalObjects") is YamlSequenceNode externalSeq)
            {
                foreach (var item in externalSeq.Children)
                {
                    if (item is not YamlMappingNode itemMap) continue;
                    var firstMap = GetMapping(itemMap, "first");
                    var secondMap = GetMapping(itemMap, "second");
                    if (firstMap == null || secondMap == null) continue;

                    string? name = GetScalar(firstMap, "name");
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    long fileId = 0;
                    if (GetScalar(secondMap, "fileID") is string fidStr)
                        long.TryParse(fidStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out fileId);

                    string guid = GetScalar(secondMap, "guid") ?? string.Empty;
                    int type = 0;
                    if (GetScalar(secondMap, "type") is string typeStr)
                        int.TryParse(typeStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out type);

                    result.ExternalObjects[name] = new ExternalObjectRef(fileId, guid, type);
                }
            }
            else if (GetMapping(importerNode, "externalObjects") is YamlMappingNode externalMap)
            {
                // Support compact dictionary-style externalObjects if emitted by custom Unity tools
                foreach (var entry in externalMap.Children)
                {
                    if (entry.Key is YamlMappingNode firstMap && entry.Value is YamlMappingNode secondMap)
                    {
                        string? name = GetScalar(firstMap, "name");
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        long fileId = 0;
                        if (GetScalar(secondMap, "fileID") is string fidStr)
                            long.TryParse(fidStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out fileId);

                        string guid = GetScalar(secondMap, "guid") ?? string.Empty;
                        int type = 0;
                        if (GetScalar(secondMap, "type") is string typeStr)
                            int.TryParse(typeStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out type);

                        result.ExternalObjects[name] = new ExternalObjectRef(fileId, guid, type);
                    }
                }
            }

            if (GetMapping(importerNode, "humanDescription") is YamlMappingNode humanDesc &&
                GetNode(humanDesc, "human") is YamlSequenceNode humanSeq)
            {
                foreach (var item in humanSeq.Children)
                {
                    if (item is not YamlMappingNode boneMap) continue;
                    string? boneName = GetScalar(boneMap, "boneName");
                    string? humanName = GetScalar(boneMap, "humanName");
                    if (!string.IsNullOrWhiteSpace(boneName) && !string.IsNullOrWhiteSpace(humanName))
                    {
                        result.HumanBones.Add((boneName.Trim(), humanName.Trim()));
                    }
                }
            }
        }
        catch (Exception)
        {
            // Fall back to line-based recovery if a non-standard .meta file contains invalid YAML tokens
            ParseFallbackLines(yamlContent, result);
        }

        return result;
    }

    private static void ReadScaleProperties(YamlMappingNode node, ParsedModelMeta result)
    {
        string? globalScaleStr = GetScalar(node, "globalScale") ?? GetScalar(node, "m_ScaleFactor");
        if (globalScaleStr != null &&
            float.TryParse(globalScaleStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float gs))
        {
            result.GlobalScale = gs;
        }

        string? useFileUnitsStr = GetScalar(node, "useFileUnits") ?? GetScalar(node, "m_UseFileUnits");
        if (useFileUnitsStr != null)
        {
            result.UseFileUnits = useFileUnitsStr == "1" || useFileUnitsStr.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        string? useFileScaleStr = GetScalar(node, "useFileScale") ?? GetScalar(node, "m_UseFileScale");
        if (useFileScaleStr != null)
        {
            result.UseFileScale = useFileScaleStr == "1" || useFileScaleStr.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static YamlNode? GetNode(YamlMappingNode map, string key)
    {
        return map.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? val) ? val : null;
    }

    private static YamlMappingNode? GetMapping(YamlMappingNode map, string key)
    {
        return GetNode(map, key) as YamlMappingNode;
    }

    private static string? GetScalar(YamlMappingNode map, string key)
    {
        return (GetNode(map, key) as YamlScalarNode)?.Value;
    }

    private static void ParseFallbackLines(string yamlContent, ParsedModelMeta result)
    {
        int section = -1;
        string boneName = string.Empty;
        string humanName = string.Empty;
        using var reader = new StringReader(yamlContent);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("globalScale:", StringComparison.Ordinal) || trimmed.StartsWith("m_ScaleFactor:", StringComparison.Ordinal))
            {
                if (float.TryParse(trimmed.Split(':', 2)[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float gs))
                    result.GlobalScale = gs;
                continue;
            }
            if (trimmed.StartsWith("useFileUnits:", StringComparison.Ordinal) || trimmed.StartsWith("m_UseFileUnits:", StringComparison.Ordinal))
            {
                string v = trimmed.Split(':', 2)[1].Trim();
                result.UseFileUnits = v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (trimmed.StartsWith("useFileScale:", StringComparison.Ordinal) || trimmed.StartsWith("m_UseFileScale:", StringComparison.Ordinal))
            {
                string v = trimmed.Split(':', 2)[1].Trim();
                result.UseFileScale = v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (line.StartsWith("  fileIDToRecycleName:", StringComparison.Ordinal)) { section = 1; continue; }
            if (line.StartsWith("  humanDescription:", StringComparison.Ordinal)) { section = 0; continue; }
            if (line.StartsWith("  ", StringComparison.Ordinal) && !line.StartsWith("    ", StringComparison.Ordinal) && !line.StartsWith("  -", StringComparison.Ordinal))
            {
                section = -1;
                continue;
            }
            if (section == 1 && line.StartsWith("    ", StringComparison.Ordinal) && line.Contains(':'))
            {
                string[] parts = line.Split(':', 2);
                if (long.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long fid) && fid != 0)
                    result.FileIdToRecycleName[fid] = parts[1].Trim();
            }
            else if (section == 0)
            {
                if (line.StartsWith("    - boneName:", StringComparison.Ordinal))
                    boneName = line.Split(':', 2)[1].Trim();
                else if (line.StartsWith("      humanName:", StringComparison.Ordinal))
                    humanName = line.Split(':', 2)[1].Trim();
                if (boneName.Length > 0 && humanName.Length > 0)
                {
                    result.HumanBones.Add((boneName, humanName));
                    boneName = string.Empty;
                    humanName = string.Empty;
                }
            }
        }
    }
}
