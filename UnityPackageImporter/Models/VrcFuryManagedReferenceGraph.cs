#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using YamlDotNet.RepresentationModel;

namespace UnityPackageImporter.Models;

public sealed class VrcFuryManagedReferenceGraph
{
    public IReadOnlyDictionary<long, YamlMappingNode> Records { get; }
    public IReadOnlyList<long> RootIds { get; }
    public IReadOnlyList<YamlMappingNode> ReachableFeatures { get; }
    public IReadOnlyList<(string Code, string Message)> Diagnostics { get; }
    public bool IsValid => Diagnostics.Count == 0;

    private VrcFuryManagedReferenceGraph(Dictionary<long, YamlMappingNode> records, List<long> roots,
        List<YamlMappingNode> features, List<(string Code, string Message)> diagnostics)
    {
        Records = new System.Collections.ObjectModel.ReadOnlyDictionary<long, YamlMappingNode>(records);
        RootIds = roots.AsReadOnly();
        ReachableFeatures = features.AsReadOnly();
        Diagnostics = diagnostics.AsReadOnly();
    }

    public static VrcFuryManagedReferenceGraph Read(YamlMappingNode body, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        cancellationToken.ThrowIfCancellationRequested();
        var records = new Dictionary<long, YamlMappingNode>();
        var roots = new List<long>();
        var features = new List<YamlMappingNode>();
        var diagnostics = new List<(string Code, string Message)>();
        var result = new VrcFuryManagedReferenceGraph(records, roots, features, diagnostics);
        var references = Get(body, "references");
        if (references != null && references is not YamlMappingNode)
            diagnostics.Add(("VRCFURY_INVALID_REFERENCE_TABLE", "Managed references must be a mapping."));
        var table = Get(references as YamlMappingNode, "RefIds");
        if (table != null && table is not YamlSequenceNode)
            diagnostics.Add(("VRCFURY_INVALID_REFERENCE_TABLE", "Managed RefIds must be a sequence."));
        if (table is YamlSequenceNode entries)
        {
            foreach (var node in entries.Children)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (node is not YamlMappingNode entry || !TryId(Get(entry, "rid"), out long id) || id < 0)
                    diagnostics.Add(("VRCFURY_INVALID_REFERENCE_ID", "Every managed record must have a nonnegative integer rid."));
                else if (!records.TryAdd(id, entry))
                    diagnostics.Add(("VRCFURY_DUPLICATE_REFERENCE_ID", $"Managed record rid {id} is duplicated."));
            }
        }

        FindReferences(Get(Get(body, "config") as YamlMappingNode, "features"), roots);
        FindReferences(Get(body, "content"), roots);
        if (!result.IsValid) return result;
        var pending = new Queue<long>(roots);
        var visited = new HashSet<long>();
        while (pending.TryDequeue(out long id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(id)) continue;
            if (!records.TryGetValue(id, out var entry))
            {
                diagnostics.Add(("VRCFURY_MISSING_REFERENCE", $"Reachable managed rid {id} has no record."));
                continue;
            }
            var type = Get(entry, "type") as YamlMappingNode;
            if (type == null || Get(type, "class") is not YamlScalarNode className || string.IsNullOrWhiteSpace(className.Value) ||
                Get(type, "ns") is not YamlScalarNode)
                diagnostics.Add(("VRCFURY_INVALID_REFERENCE_TYPE", $"Reachable managed rid {id} has no complete type."));
            else if ((Get(type, "ns") as YamlScalarNode)?.Value == "VF.Model.Feature")
                features.Add(entry);
            var nested = new List<long>();
            FindReferences(Get(entry, "data"), nested);
            foreach (long nestedId in nested) pending.Enqueue(nestedId);
        }
        if (!result.IsValid) features.Clear();
        return result;

        void FindReferences(YamlNode? root, List<long> output)
        {
            if (root == null) return;
            var nodes = new Stack<YamlNode>();
            var visitedNodes = new HashSet<YamlNode>(ReferenceEqualityComparer.Instance);
            nodes.Push(root);
            while (nodes.TryPop(out var node))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!visitedNodes.Add(node)) continue;
                if (node is YamlMappingNode mapping)
                {
                    var rid = Get(mapping, "rid");
                    if (rid != null)
                    {
                        if (!TryId(rid, out long id) || id < -2 || id == -1)
                            diagnostics.Add(("VRCFURY_INVALID_REFERENCE_ID", "Managed reference rid must be a nonnegative integer or Unity's null sentinel -2."));
                        else if (id != -2) output.Add(id);
                    }
                    var children = new List<YamlNode>(mapping.Children.Values);
                    for (int index = children.Count - 1; index >= 0; index--) nodes.Push(children[index]);
                }
                else if (node is YamlSequenceNode sequence)
                    for (int index = sequence.Children.Count - 1; index >= 0; index--) nodes.Push(sequence.Children[index]);
            }
        }
    }

    private static YamlNode? Get(YamlMappingNode? mapping, string key) =>
        mapping != null && mapping.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    private static bool TryId(YamlNode? node, out long id)
    {
        id = 0;
        return node is YamlScalarNode scalar && long.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
    }
}
