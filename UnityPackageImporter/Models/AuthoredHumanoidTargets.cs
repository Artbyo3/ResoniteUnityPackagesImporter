#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace UnityPackageImporter.Models;

internal static class AuthoredHumanoidTargets
{
    private static readonly Regex Header = new(@"^--- !u!(?<class>\d+) &(?<id>-?\d+)(?: stripped)?[ \t]*\r?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    internal static bool ReadAvatarGuid(string yaml, long rootId, out string? guid)
    {
        guid = null;
        var headers = Header.Matches(yaml).Cast<Match>().ToArray();
        var animators = new List<(long Id, YamlMappingNode Body)>();
        YamlMappingNode? root = null;
        for (int index = 0; index < headers.Length; index++)
        {
            var header = headers[index];
            long id = long.Parse(header.Groups["id"].Value, CultureInfo.InvariantCulture);
            string classId = header.Groups["class"].Value;
            if (classId != "95" && (classId != "1" || id != rootId)) continue;
            int start = header.Index + header.Length;
            int end = index + 1 < headers.Length ? headers[index + 1].Index : yaml.Length;
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml.Substring(start, end - start)));
            if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode wrapper) continue;
            if (classId == "1") root = Get(wrapper, "GameObject") as YamlMappingNode;
            else if (Get(wrapper, "Animator") is YamlMappingNode animator)
                animators.Add((id, animator));
        }
        var components = root == null ? null : Get(root, "m_Component") as YamlSequenceNode;
        var owners = animators.Where(animator =>
            (Get(animator.Body, "m_GameObject") is YamlMappingNode owner && Number(owner, "fileID") == rootId) ||
            (components != null && components.Children.Any(item => item is YamlMappingNode component &&
                Get(component, "component") is YamlMappingNode reference && Number(reference, "fileID") == animator.Id))).ToArray();
        if (owners.Length == 0) return false;
        if (owners.Length != 1 || components == null ||
            headers.GroupBy(header => long.Parse(header.Groups["id"].Value, CultureInfo.InvariantCulture)).Any(group => group.Count() != 1)) return true;
        var selected = owners[0];
        if (Get(selected.Body, "m_GameObject") is not YamlMappingNode localOwner || !Local(localOwner) || Number(localOwner, "fileID") != rootId ||
            components.Children.Count(item => item is YamlMappingNode component &&
                Get(component, "component") is YamlMappingNode reference && Local(reference) && Number(reference, "fileID") == selected.Id) != 1 ||
            Get(selected.Body, "m_Avatar") is not YamlMappingNode avatar || Number(avatar, "fileID") != 9000000 ||
            Number(avatar, "type") != 3) return true;
        string candidate = Text(avatar, "guid");
        if (candidate.Length == 32 && System.Guid.TryParseExact(candidate, "N", out var parsed) && parsed != System.Guid.Empty)
            guid = candidate;
        return true;
    }

    internal static IReadOnlyDictionary<string, T?> Resolve<T>(T avatarRoot, string avatarGuid, ParsedModelMeta metadata,
        Func<T, IEnumerable<T>> children, Func<T, string> nameOf) where T : class
    {
        var result = new Dictionary<string, T?>(StringComparer.Ordinal);
        if (!metadata.IsAuthoredHumanoid || !string.Equals(metadata.Guid, avatarGuid, StringComparison.OrdinalIgnoreCase) ||
            metadata.Skeleton.Count == 0 || metadata.HumanBones.Count == 0) return result;
        var skeleton = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in metadata.Skeleton)
            if (string.IsNullOrEmpty(entry.Name) || entry.Name.Contains('/') || entry.Name.Contains('\\') ||
                !skeleton.TryAdd(entry.Name, entry.ParentName)) return result;
        var roots = skeleton.Where(entry => entry.Value.Length == 0).Select(entry => entry.Key).ToArray();
        if (roots.Length != 1) return result;
        var paths = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (string boneName in skeleton.Keys)
        {
            var path = new List<string>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            string current = boneName;
            while (current != roots[0])
            {
                if (!visited.Add(current) || !skeleton.TryGetValue(current, out string? parent) || parent.Length == 0)
                    return result;
                path.Add(current);
                current = parent;
            }
            path.Reverse();
            paths.Add(boneName, path.ToArray());
        }
        var humanNames = new HashSet<string>(StringComparer.Ordinal);
        var boneNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var bone in metadata.HumanBones)
            if (!humanNames.Add(bone.HumanName) || !boneNames.Add(bone.BoneName) || !paths.ContainsKey(bone.BoneName)) return result;
        foreach (var bone in metadata.HumanBones)
        {
            T? target = avatarRoot;
            foreach (string segment in paths[bone.BoneName])
            {
                if (target == null) break;
                var matches = children(target).Where(child => string.Equals(nameOf(child), segment, StringComparison.Ordinal)).Distinct().ToArray();
                target = matches.Length == 1 ? matches[0] : null;
                if (target == null) break;
            }
            result.Add(bone.HumanName, target);
        }
        return result;
    }

    private static YamlNode? Get(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;
    private static string Text(YamlMappingNode node, string key) => (Get(node, key) as YamlScalarNode)?.Value ?? "";
    private static long Number(YamlMappingNode node, string key) =>
        long.TryParse(Text(node, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out long number) ? number : 0;
    private static bool Local(YamlMappingNode reference)
    {
        var guid = Get(reference, "guid");
        return guid == null || guid is YamlScalarNode scalar && (string.IsNullOrEmpty(scalar.Value) || scalar.Value == "0");
    }
}
