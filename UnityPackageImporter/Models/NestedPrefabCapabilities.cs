#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using YamlDotNet.RepresentationModel;

namespace UnityPackageImporter.Models;

internal sealed record NestedPrefabCapabilities(bool CanDefer, IReadOnlyCollection<string> PrefabGuids)
{
    private static readonly Regex Header = new(@"^--- !u!(?<class>[0-9]+) &-?[0-9]+(?: stripped)?[ \t]*\r?$", RegexOptions.Multiline);

    // Missing dependencies and unrecognizable files cannot establish mesh-free capability.
    internal static NestedPrefabCapabilities Scan(string sourceGuid, Func<string, string?> readPrefab)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(sourceGuid);
        while (pending.Count > 0)
        {
            string guid = pending.Pop();
            if (!visited.Add(guid)) continue;
            string? yaml = readPrefab(guid);
            if (yaml == null) return new(false, visited.ToArray());
            var documents = Header.Matches(yaml);
            if (documents.Count == 0) return new(false, visited.ToArray());
            for (int i = 0; i < documents.Count; i++)
            {
                string classId = documents[i].Groups["class"].Value;
                if (classId is "33" or "23" or "137") return new(false, visited.ToArray());
                int start = documents[i].Index + documents[i].Length;
                int end = i + 1 < documents.Count ? documents[i + 1].Index : yaml.Length;
                var stream = new YamlStream();
                using var reader = new StringReader(yaml.Substring(start, end - start));
                stream.Load(reader);
                if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode root)
                    return new(false, visited.ToArray());
                if (classId != "1001") continue;
                if (!root.Children.TryGetValue(new YamlScalarNode("PrefabInstance"), out var body) ||
                    body is not YamlMappingNode instance ||
                    !instance.Children.TryGetValue(new YamlScalarNode("m_SourcePrefab"), out var source) ||
                    source is not YamlMappingNode reference ||
                    !reference.Children.TryGetValue(new YamlScalarNode("guid"), out var child) ||
                    child is not YamlScalarNode scalar || string.IsNullOrWhiteSpace(scalar.Value))
                    return new(false, visited.ToArray());
                pending.Push(scalar.Value);
            }
        }
        return new(true, visited.ToArray());
    }
}

internal sealed class NestedPrefabDeferrals
{
    private readonly HashSet<ulong> instances = new();
    private readonly Dictionary<ulong, string> missing = new();
    internal IEnumerable<ulong> MissingInstanceIds => missing.Keys;
    internal void RegisterMissing(ulong instanceId, string guid) => missing.Add(instanceId, guid);
    internal bool TryGetMissingSource(ulong instanceId, out string? guid) => missing.TryGetValue(instanceId, out guid);
    internal static string MissingSourceWarning(ulong instanceId, string guid) =>
        $"Referenced prefab GUID {guid} for instance {instanceId} is missing from the package; it was not imported.";
    private readonly HashSet<string> sourcePaths = new(StringComparer.OrdinalIgnoreCase);
    internal IEnumerable<ulong> InstanceIds => instances;

    internal void Register(ulong instanceId, IEnumerable<string> paths)
    {
        instances.Add(instanceId);
        sourcePaths.UnionWith(paths);
    }

    internal bool ContainsInstance(ulong id) => instances.Contains(id) || missing.ContainsKey(id);
    internal bool ShouldSkipObject(ulong instanceId, bool hasSourceObject) => hasSourceObject && ContainsInstance(instanceId);
    internal bool ContainsSource(string path) => sourcePaths.Contains(path);

    internal Task InstantiateAsync(ulong instanceId, bool hasSourceObject, Func<Task> instantiate, Action skipped)
    {
        if (!ShouldSkipObject(instanceId, hasSourceObject)) return instantiate();
        skipped();
        return Task.CompletedTask;
    }
}
