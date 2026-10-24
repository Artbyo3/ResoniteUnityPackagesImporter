#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace UnityPackageImporter.Models;

public sealed record AvatarVisemeSettings(int? Mode, UnityAssetReference? Renderer,
    IReadOnlyList<string> Shapes, string Error = "")
{
    internal static AvatarVisemeSettings Parse(string yaml)
    {
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode root ||
                Get(root, "MonoBehaviour") is not YamlMappingNode body)
                throw new InvalidDataException("Lip sync requires one exact descriptor record.");
            var modeNode = Get(body, "lipSync");
            if (modeNode == null) return new(null, null, Array.Empty<string>());
            if (modeNode is not YamlScalarNode scalar || !int.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int mode))
                throw new InvalidDataException("Descriptor lipSync must be an integer mode.");
            UnityAssetReference? renderer = null;
            var referenceNode = Get(body, "VisemeSkinnedMesh");
            if (referenceNode != null)
            {
                if (referenceNode is not YamlMappingNode reference || Get(reference, "fileID") is not YamlScalarNode fileNode ||
                    !long.TryParse(fileNode.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long fileId))
                    throw new InvalidDataException("Descriptor viseme renderer requires an exact fileID.");
                var guidNode = Get(reference, "guid");
                if (guidNode != null && guidNode is not YamlScalarNode)
                    throw new InvalidDataException("Descriptor renderer GUID must be scalar.");
                renderer = new UnityAssetReference { FileId = fileId, Guid = (guidNode as YamlScalarNode)?.Value ?? "" };
            }
            var names = new List<string>();
            var shapesNode = Get(body, "VisemeBlendShapes");
            if (shapesNode != null)
            {
                if (shapesNode is not YamlSequenceNode sequence)
                    throw new InvalidDataException("Descriptor viseme shapes must be an ordered sequence.");
                foreach (var name in sequence.Children)
                {
                    if (name is not YamlScalarNode nameNode || nameNode.Value?.Contains('\0') == true)
                        throw new InvalidDataException("Descriptor viseme shape names must be scalar.");
                    names.Add(nameNode.Value ?? "");
                }
            }
            return new(mode, renderer, names.AsReadOnly());
        }
        catch (Exception error) when (error is YamlException or InvalidDataException)
        {
            return new(null, null, Array.Empty<string>(), error.Message);
        }
    }

    private static YamlNode? Get(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;
}

internal sealed record AvatarVisemeBinding(int Channel, string Shape, int SourceIndex);

internal static class AvatarVisemePlanner
{
    internal static IReadOnlyList<AvatarVisemeBinding> Compile(AvatarVisemeSettings settings,
        IReadOnlyList<string> sourceShapes, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(sourceShapes);
        token.ThrowIfCancellationRequested();
        if (settings.Error.Length != 0) throw new InvalidDataException(settings.Error);
        if (settings.Mode != 3) throw new InvalidDataException("Only authored VisemeBlendShape mode has an exact native adapter; jaw and parameter-only modes remain pending.");
        if (settings.Renderer == null || settings.Renderer.FileId == 0 || settings.Renderer.HasGuid)
            throw new InvalidDataException("Visemes require an exact local skinned renderer reference.");
        if (settings.Shapes == null || settings.Shapes.Count != 15)
            throw new InvalidDataException("The descriptor must retain exactly 15 ordered viseme entries.");
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < sourceShapes.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(sourceShapes[index]) || !indices.TryAdd(sourceShapes[index], index))
                throw new InvalidDataException("The renderer requires unique exact source blendshape names.");
        }
        var result = new List<AvatarVisemeBinding>();
        var used = new HashSet<int>();
        for (int channel = 0; channel < settings.Shapes.Count; channel++)
        {
            token.ThrowIfCancellationRequested();
            string shape = settings.Shapes[channel];
            if (string.IsNullOrEmpty(shape)) continue;
            if (!indices.TryGetValue(shape, out int index))
                throw new InvalidDataException("Authored viseme shape is missing: " + shape);
            if (!used.Add(index))
                throw new InvalidDataException("Multiple viseme channels share one shape; a multi-source viseme adapter is required.");
            result.Add(new(channel, shape, index));
        }
        if (result.Count == 0) throw new InvalidDataException("The authored viseme mapping has no target shapes.");
        return result.AsReadOnly();
    }
}
