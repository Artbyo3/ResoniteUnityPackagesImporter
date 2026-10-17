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
            if (!parser.Accept<NodeEvent>(out var root) || root.Anchor.IsEmpty)
                throw new InvalidDataException("Unity YAML document must have a nonzero numeric root anchor.");

            ulong id;
            if (ulong.TryParse(root.Anchor.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedUlong) && parsedUlong != 0)
            {
                id = parsedUlong;
            }
            else if (long.TryParse(root.Anchor.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLong) && parsedLong != 0)
            {
                id = unchecked((ulong)parsedLong);
            }
            else
            {
                throw new InvalidDataException("Unity YAML document must have a nonzero numeric root anchor.");
            }

            // Deserialization errors must fail the import, rather than publish a partial object graph.
            T document = deserializer.Deserialize<T>(parser);
            parser.Consume<DocumentEnd>();
            if (document != null && !documents.TryAdd(id, document))
                throw new InvalidDataException("Unity YAML contains a duplicate document anchor: " + id + ".");
        }
        parser.Consume<StreamEnd>();
        return documents;
    }
}
