#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using YamlDotNet.RepresentationModel;

namespace UnityPackageImporter.Models;

internal static class UnityPrefabSchema
{
    private static YamlNode? Get(YamlMappingNode node, string key) => node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;
    private static string Text(YamlMappingNode node, string key) => (Get(node, key) as YamlScalarNode)?.Value ?? "";
    private static long Reference(YamlMappingNode node, string key)
    {
        if (Get(node, key) is not YamlMappingNode reference)
            throw new InvalidDataException("Malformed legacy prefab reference: " + key);
        return ReferenceId(reference);
    }
    private static long ReferenceId(YamlMappingNode reference) =>
        long.TryParse(Text(reference, "fileID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long id)
            ? id : throw new InvalidDataException("Malformed legacy prefab fileID.");
    private static void SetCompatible(YamlMappingNode body, string name, YamlMappingNode reference)
    {
        if (Get(body, name) is { } existing && (existing is not YamlMappingNode previous ||
            ReferenceId(previous) != ReferenceId(reference) ||
            !string.Equals(Text(previous, "guid"), Text(reference, "guid"), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Conflicting legacy and modern prefab reference: " + name);
        body.Children[new YamlScalarNode(name)] = reference;
    }

    internal static bool NormalizeDocument(YamlMappingNode root, int classId)
    {
        if (root.Children.Count != 1) throw new InvalidDataException("Prefab document must contain one Unity object.");
        if (classId == 1001)
        {
            if (Get(root, "PrefabInstance") is YamlMappingNode) return false;
            if (Get(root, "Prefab") is not YamlMappingNode legacy) throw new InvalidDataException("Unknown class 1001 prefab schema.");
            long parent = Reference(legacy, "m_ParentPrefab");
            string isDefinition = Text(legacy, "m_IsPrefabParent");
            if (parent == 0 && isDefinition == "1")
            {
                if (!string.IsNullOrEmpty(Text((YamlMappingNode)Get(legacy, "m_ParentPrefab")!, "guid")))
                    throw new InvalidDataException("Legacy prefab definition has an external parent reference.");
                if (Reference(legacy, "m_RootGameObject") == 0 ||
                    Get(legacy, "m_RootGameObject") is not YamlMappingNode rootReference || !string.IsNullOrEmpty(Text(rootReference, "guid")))
                    throw new InvalidDataException("Legacy prefab definition has no local root GameObject.");
                return true;
            }
            if (parent == 0 || isDefinition != "0" || Get(legacy, "m_ParentPrefab") is not YamlMappingNode source ||
                string.IsNullOrWhiteSpace(Text(source, "guid")))
                throw new InvalidDataException("Legacy prefab instance has no valid source reference.");
            SetCompatible(legacy, "m_SourcePrefab", source);
            root.Children.Remove(new YamlScalarNode("Prefab"));
            root.Add("PrefabInstance", legacy);
            return false;
        }
        if (root.Children.Values.Single() is not YamlMappingNode body || Get(body, "m_PrefabParentObject") == null)
            return false;
        if (Get(body, "m_PrefabParentObject") is not YamlMappingNode oldSource)
            throw new InvalidDataException("Malformed legacy prefab object source reference.");
        long sourceId = Reference(body, "m_PrefabParentObject");
        SetCompatible(body, "m_CorrespondingSourceObject", oldSource);
        if (sourceId == 0)
            SetCompatible(body, "m_PrefabInstance", new YamlMappingNode { { "fileID", new YamlScalarNode("0") } });
        else
        {
            if (string.IsNullOrWhiteSpace(Text(oldSource, "guid")) || Reference(body, "m_PrefabInternal") == 0 ||
                Get(body, "m_PrefabInternal") is not YamlMappingNode owner || !string.IsNullOrEmpty(Text(owner, "guid")))
                throw new InvalidDataException("Legacy prefab object has no source GUID or local owning instance.");
            SetCompatible(body, "m_PrefabInstance", owner);
        }
        return false;
    }
}
