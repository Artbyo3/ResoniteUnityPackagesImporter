using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using UnityPackageImporter.FrooxEngineRepresentation;
using YamlDotNet.Serialization;

namespace UnityPackageImporter.Models;

public class YamlToFrooxEngine
{
    private sealed record DocumentClass(int? Value);
    private static readonly ConditionalWeakTable<IUnityObject, DocumentClass> Classes = new();
    internal static int? ClassId(IUnityObject obj) => Classes.TryGetValue(obj, out var value) ? value.Value : null;
    public static Dictionary<ulong, IUnityObject> parseYaml(string FilePath)
    {
        var deserializer = new DeserializerBuilder()
            .WithNodeTypeResolver(new UnityNodeTypeResolver())
            .IgnoreUnmatchedProperties()
            .Build();
        // Unity's optional "stripped" header suffix is not part of the YAML tag.
        // Normalize it in memory so the extracted package cache remains immutable.
        string yaml = AvatarPackageIndex.NormalizeDocumentHeaders(File.ReadAllText(FilePath));
        var classes = new Dictionary<ulong, int?>();
        var documents = UnityYamlDocumentParser.Parse<UnityEngineObjectWrapper>(yaml, deserializer, (id, kind) => classes[id] = kind);
        var objects = new Dictionary<ulong, IUnityObject>();
        foreach (var document in documents)
        {
            IUnityObject obj = document.Value?.Result() ?? new FrooxEngineRepresentation.GameObjectTypes.NullType();
            obj.id = document.Key;
            Classes.Add(obj, new(classes[document.Key]));
            objects.Add(document.Key, obj);
        }
        return objects;
    }
}
