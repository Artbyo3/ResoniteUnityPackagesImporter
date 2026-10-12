#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using YamlDotNet.RepresentationModel;

namespace UnityPackageImporter.Models;

/// <summary>Serialized material values, independent of shader conversion and engine objects.</summary>
internal sealed class UnityMaterialData
{
    public string? ShaderGuid { get; private set; }
    public string? RenderType { get; private set; }
    public int RenderQueue { get; private set; } = -1;
    public HashSet<string> Keywords { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, float> Numbers { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, (float r, float g, float b, float a)> Colors { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, (string? guid, float sx, float sy, float ox, float oy)> Textures { get; } = new(StringComparer.Ordinal);

    public float Number(string name, float fallback = 0) => Numbers.TryGetValue(name, out var value) ? value : fallback;
    public bool Enabled(string name) => Number(name) != 0;
    public (float r, float g, float b, float a) Color(string name, (float r, float g, float b, float a) fallback) =>
        Colors.TryGetValue(name, out var value) ? value : fallback;
    public string? Texture(string name) => Textures.TryGetValue(name, out var value) ? value.guid : null;

    public static UnityMaterialData Parse(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        if (stream.Documents.Count != 1 || Child(stream.Documents[0].RootNode, "Material") is not YamlMappingNode material)
            throw new InvalidDataException("Expected one Unity Material document.");
        var data = new UnityMaterialData
        {
            ShaderGuid = Text(Child(Child(material, "m_Shader"), "guid")),
            RenderType = Text(Child(Child(material, "stringTagMap"), "RenderType")),
            RenderQueue = (int)Number(Child(material, "m_CustomRenderQueue"), -1)
        };
        var saved = Child(material, "m_SavedProperties");
        foreach (var keyword in (Text(Child(material, "m_ShaderKeywords")) ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            data.Keywords.Add(keyword);
        if (Child(material, "m_ValidKeywords") is YamlSequenceNode keywords)
            foreach (var keyword in keywords)
                if (Text(keyword) is string value) data.Keywords.Add(value);
        foreach (string section in new[] { "m_Floats", "m_Ints" })
            foreach (var property in Properties(Child(saved, section)))
                data.Numbers[property.Key] = Number(property.Value, 0);
        foreach (var property in Properties(Child(saved, "m_Colors")))
        {
            var node = property.Value;
            // Unity also serializes vector properties in this section.
            data.Colors[property.Key] = (
                Number(Child(node, "r"), Number(Child(node, "x"), 0)),
                Number(Child(node, "g"), Number(Child(node, "y"), 0)),
                Number(Child(node, "b"), Number(Child(node, "z"), 0)),
                Number(Child(node, "a"), Number(Child(node, "w"), 0)));
        }
        foreach (var property in Properties(Child(saved, "m_TexEnvs")))
        {
            var node = property.Value;
            var texture = Child(node, "m_Texture");
            var guid = Text(Child(texture, "guid"));
            if (Number(Child(texture, "fileID"), 0) == 0 || string.IsNullOrEmpty(guid) || guid.Trim('0').Length == 0)
                guid = null;
            data.Textures[property.Key] = (guid,
                Number(Child(Child(node, "m_Scale"), "x"), 1),
                Number(Child(Child(node, "m_Scale"), "y"), 1),
                Number(Child(Child(node, "m_Offset"), "x"), 0),
                Number(Child(Child(node, "m_Offset"), "y"), 0));
        }
        return data;
    }

    private static IEnumerable<KeyValuePair<string, YamlNode>> Properties(YamlNode? node)
    {
        if (node is not YamlSequenceNode sequence) yield break;
        foreach (var item in sequence)
            if (item is YamlMappingNode mapping)
                foreach (var pair in mapping.Children)
                    yield return new(Text(pair.Key) ?? throw new InvalidDataException("Material property name is missing."), pair.Value);
    }

    private static YamlNode? Child(YamlNode? node, string key) =>
        node is YamlMappingNode map && map.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;
    private static string? Text(YamlNode? node) => (node as YamlScalarNode)?.Value;
    private static float Number(YamlNode? node, float fallback) =>
        float.TryParse(Text(node), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && float.IsFinite(value)
            ? value : fallback;
}
