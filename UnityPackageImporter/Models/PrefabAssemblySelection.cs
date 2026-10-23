#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace UnityPackageImporter.Models;

internal static class PrefabAssemblySelection
{
    private static readonly Regex UnityTag = new(@"(?m)^(---[ \t]+)!u!(\d+)(?=[ \t]+&)", RegexOptions.CultureInvariant);

    internal static IReadOnlyList<KeyValuePair<string, string>> Create(IReadOnlyDictionary<string, string> prefabs)
    {
        ArgumentNullException.ThrowIfNull(prefabs);
        var library = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prefab in prefabs)
        {
            if (string.IsNullOrWhiteSpace(prefab.Key) || string.IsNullOrWhiteSpace(prefab.Value) ||
                !library.TryAdd(prefab.Key, prefab.Value))
                throw new InvalidDataException("Prefab library contains an empty GUID/path or a case-insensitive GUID collision.");
        }

        var ordered = library.OrderBy(prefab => prefab.Key, StringComparer.OrdinalIgnoreCase).ToArray();
        var edges = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var incoming = library.Keys.ToDictionary(guid => guid, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var prefab in ordered)
        {
            var dependencies = ReadDependencies(prefab.Value, library);
            edges.Add(prefab.Key, dependencies);
            foreach (string dependency in dependencies)
                incoming[dependency]++;
        }

        var roots = ordered.Where(prefab => incoming[prefab.Key] == 0).ToArray();
        var pending = new Queue<string>(roots.Select(prefab => prefab.Key));
        int visited = 0;
        while (pending.TryDequeue(out string? guid))
        {
            visited++;
            foreach (string dependency in edges[guid])
                if (--incoming[dependency] == 0)
                    pending.Enqueue(dependency);
        }

        if (visited != library.Count)
        {
            string remaining = string.Join(", ", ordered.Where(prefab => incoming[prefab.Key] > 0)
                .Select(prefab => prefab.Key));
            throw new InvalidDataException("Prefab dependency cycle detected; unresolved GUIDs: " + remaining + ".");
        }

        return Array.AsReadOnly(roots);
    }

    private static HashSet<string> ReadDependencies(string path, IReadOnlyDictionary<string, string> library)
    {
        var stream = new YamlStream();
        try
        {
            string yaml = AvatarPackageIndex.NormalizeDocumentHeaders(File.ReadAllText(path));
            using var reader = new StringReader(UnityTag.Replace(yaml, "$1!<tag:unity3d.com,2011:$2>"));
            stream.Load(reader);
        }
        catch (YamlException error)
        {
            throw new InvalidDataException("Malformed prefab YAML: " + path, error);
        }
        catch (IOException error)
        {
            throw new InvalidDataException("Cannot read indexed prefab: " + path, error);
        }

        if (stream.Documents.Count == 0)
            throw new InvalidDataException("Prefab has no YAML documents: " + path);

        var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in stream.Documents)
        {
            if (document.RootNode is not YamlMappingNode root)
                throw new InvalidDataException("Prefab document is not a mapping: " + path);
            if (root.Tag.Value != "tag:unity3d.com,2011:1001") continue;
            if (UnityPrefabSchema.NormalizeDocument(root, 1001)) continue;

            if (root.Children.Count != 1 ||
                !root.Children.TryGetValue(new YamlScalarNode("PrefabInstance"), out var instanceNode) ||
                instanceNode is not YamlMappingNode instance ||
                !instance.Children.TryGetValue(new YamlScalarNode("m_SourcePrefab"), out var sourceNode) ||
                sourceNode is not YamlMappingNode source ||
                !source.Children.TryGetValue(new YamlScalarNode("fileID"), out var fileIdNode) ||
                fileIdNode is not YamlScalarNode fileId ||
                !long.TryParse(fileId.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long sourceFileId))
                throw new InvalidDataException("Malformed PrefabInstance source reference: " + path);

            if (sourceFileId == 0) continue;
            if (!source.Children.TryGetValue(new YamlScalarNode("guid"), out var guidNode) ||
                guidNode is not YamlScalarNode guid || string.IsNullOrWhiteSpace(guid.Value))
                throw new InvalidDataException("PrefabInstance source reference has no scalar GUID: " + path);

            if (library.ContainsKey(guid.Value)) dependencies.Add(guid.Value);
        }

        return dependencies;
    }
}
