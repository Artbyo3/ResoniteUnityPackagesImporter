using System.Collections.Generic;
using System.IO;
using UnityPackageImporter.FrooxEngineRepresentation;
using YamlDotNet.Serialization;

namespace UnityPackageImporter.Models;

public class YamlToFrooxEngine
{
    public static Dictionary<ulong, IUnityObject> parseYaml(string FilePath)
    {
        var deserializer = new DeserializerBuilder()
            .WithNodeTypeResolver(new UnityNodeTypeResolver())
            .IgnoreUnmatchedProperties()
            .Build();
        // Unity's optional "stripped" header suffix is not part of the YAML tag.
        // Normalize it in memory so the extracted package cache remains immutable.
        string yaml = AvatarPackageIndex.NormalizeDocumentHeaders(File.ReadAllText(FilePath));
        var documents = UnityYamlDocumentParser.Parse<UnityEngineObjectWrapper>(yaml, deserializer);
        var objects = new Dictionary<ulong, IUnityObject>();
        foreach (var document in documents)
        {
            IUnityObject obj = document.Value.Result();
            obj.id = document.Key;
            objects.Add(document.Key, obj);
        }
        return objects;
    }
}
