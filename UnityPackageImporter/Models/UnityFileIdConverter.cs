#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace UnityPackageImporter.Models;

internal sealed class UnityFileIdConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(Dictionary<string, ulong>);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        parser.Consume<MappingStart>();
        var references = new Dictionary<string, ulong>(StringComparer.Ordinal);
        while (!parser.TryConsume<MappingEnd>(out _))
        {
            var key = parser.Consume<Scalar>();
            var scalar = parser.Consume<Scalar>();
            ulong identity;
            if (ulong.TryParse(scalar.Value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong unsigned))
                identity = unsigned;
            else if (key.Value == "fileID" && long.TryParse(scalar.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long signed))
                identity = unchecked((ulong)signed);
            else
                throw new YamlException(scalar.Start, scalar.End, "Unity fileID must be a signed 64-bit identity or its unsigned representation.");
            if (!references.TryAdd(key.Value, identity))
                throw new YamlException(key.Start, key.End, "Unity reference contains a duplicate key.");
        }
        return references;
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
        => throw new NotSupportedException("Unity fileID conversion is read-only.");
}
