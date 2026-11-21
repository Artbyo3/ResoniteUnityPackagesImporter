#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace UnityPackageImporter.Models;

public sealed record AvatarEyeLookSettings(
    bool HasSettings,
    int EyelidType,
    UnityAssetReference? EyelidsMesh,
    IReadOnlyList<int> EyelidBlendShapeIndices,
    UnityAssetReference? LeftEyeTransform,
    UnityAssetReference? RightEyeTransform,
    float Confidence = 1f,
    float Excitement = 0f,
    string Error = "")
{
    internal static AvatarEyeLookSettings Parse(string yaml)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(yaml))
                return new(false, 0, null, Array.Empty<int>(), null, null);

            string normalized = Regex.Replace(AvatarPackageIndex.NormalizeDocumentHeaders(yaml), @"(?m)^(---[ \t]+)!u!(\d+)", "$1!<tag:unity3d.com,2011:$2>");
            var stream = new YamlStream();
            stream.Load(new StringReader(normalized));
            if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode root)
                throw new InvalidDataException("Eye look requires one exact descriptor record.");

            YamlMappingNode? body = Get(root, "MonoBehaviour") as YamlMappingNode ?? root;

            var eyeLookNode = Get(body, "customEyeLookSettings");
            if (eyeLookNode is not YamlMappingNode settings)
            {
                return new(false, 0, null, Array.Empty<int>(), null, null);
            }

            int eyelidType = 0;
            var eyelidTypeNode = Get(settings, "eyelidType");
            if (eyelidTypeNode is YamlScalarNode typeScalar &&
                int.TryParse(typeScalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedType))
            {
                eyelidType = parsedType;
            }

            UnityAssetReference? meshRef = ParseAssetRef(Get(settings, "eyelidsMesh"));
            UnityAssetReference? leftEye = ParseAssetRef(Get(settings, "leftEye"));
            UnityAssetReference? rightEye = ParseAssetRef(Get(settings, "rightEye"));

            var blendshapeIndices = new List<int>();
            var blendshapesNode = Get(settings, "eyelidsBlendshapes");
            if (blendshapesNode is YamlSequenceNode sequence)
            {
                foreach (var child in sequence.Children)
                {
                    if (child is YamlScalarNode idxScalar &&
                        int.TryParse(idxScalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx))
                    {
                        blendshapeIndices.Add(idx);
                    }
                }
            }

            float confidence = 1f;
            float excitement = 0f;
            var movementNode = Get(settings, "eyeMovement") as YamlMappingNode;
            if (movementNode != null)
            {
                if (Get(movementNode, "confidence") is YamlScalarNode confScalar &&
                    float.TryParse(confScalar.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float confVal))
                {
                    confidence = Math.Clamp(confVal, 0f, 1f);
                }
                if (Get(movementNode, "excitement") is YamlScalarNode excScalar &&
                    float.TryParse(excScalar.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float excVal))
                {
                    excitement = Math.Clamp(excVal, 0f, 1f);
                }
            }

            return new(
                HasSettings: true,
                EyelidType: eyelidType,
                EyelidsMesh: meshRef,
                EyelidBlendShapeIndices: blendshapeIndices.AsReadOnly(),
                LeftEyeTransform: leftEye,
                RightEyeTransform: rightEye,
                Confidence: confidence,
                Excitement: excitement);
        }
        catch (Exception error) when (error is YamlException or InvalidDataException)
        {
            return new(false, 0, null, Array.Empty<int>(), null, null, Error: error.Message);
        }
    }

    private static UnityAssetReference? ParseAssetRef(YamlNode? node)
    {
        if (node is not YamlMappingNode mapping) return null;
        var fileNode = Get(mapping, "fileID");
        if (fileNode is not YamlScalarNode fileScalar ||
            !long.TryParse(fileScalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long fileId) ||
            fileId == 0)
            return null;

        string guid = (Get(mapping, "guid") as YamlScalarNode)?.Value ?? "";
        return new UnityAssetReference { FileId = fileId, Guid = guid };
    }

    private static YamlNode? Get(YamlMappingNode? node, string key) =>
        node != null && node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;
}
