using System.Collections.Generic;
using System.Globalization;
using System.IO;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace UnityPackageImporter.Models;

/// <summary>Reads each Unity document's identity from its own root anchor.</summary>
public static class UnityYamlDocumentParser
{
    public static Dictionary<ulong, T> Parse<T>(string yaml, IDeserializer deserializer)
    {
        using var reader = new StringReader(yaml);
        var parser = new Parser(reader);
        parser.Consume<StreamStart>();
        var documents = new Dictionary<ulong, T>();
        while (parser.Accept<DocumentStart>(out _))
        {
            parser.Consume<DocumentStart>();
            if (!parser.Accept<NodeEvent>(out var root) ||
                root.Anchor.IsEmpty ||
                !ulong.TryParse(root.Anchor.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ||
                id == 0)
                throw new InvalidDataException("Unity YAML document must have a nonzero numeric root anchor.");

            // Deserialization errors must fail the import, rather than publish a partial object graph.
            T document = deserializer.Deserialize<T>(parser);
            parser.Consume<DocumentEnd>();
            if (!documents.TryAdd(id, document))
                throw new InvalidDataException("Unity YAML contains a duplicate document anchor: " + id + ".");
        }
        parser.Consume<StreamEnd>();
        return documents;
    }
}
