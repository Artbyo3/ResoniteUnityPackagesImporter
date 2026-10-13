using UnityPackageImporter.Models;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

internal static class UnityYamlDocumentParserTests
{
    public static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        IDeserializer Deserializer() => new DeserializerBuilder()
            .WithTagMapping("tag:unity3d.com,2011:1", typeof(Document))
            .IgnoreUnmatchedProperties()
            .Build();

        test("Unity YAML preserves each document anchor and cross-object references", () =>
        {
            var documents = UnityYamlDocumentParser.Parse<Document>("""
                %YAML 1.1
                %TAG !u! tag:unity3d.com,2011:
                --- !u!1 &101
                Object:
                  Name: First
                  Reference: 202
                  Value: 7
                --- !u!1 &202
                Object:
                  Name: Second
                  Reference: 101
                  Value: 9
                """, Deserializer());
            check(documents.Count == 2 && !documents.ContainsKey(0), "Document IDs must never lag behind the root anchors.");
            check(documents[101].Object.Name == "First" && documents[202].Object.Name == "Second", "Objects were attached to the wrong anchors.");
            check(documents[documents[101].Object.Reference].Object.Name == "Second", "Cross-object reference no longer resolves to its authored target.");
        });

        test("Unity YAML rejects corrupt object data instead of returning partial success", () =>
        {
            bool rejected = false;
            try
            {
                UnityYamlDocumentParser.Parse<Document>("--- &101\nObject: {Name: First, Value: 7}\n--- &202\nObject: {Name: Broken, Value: invalid}\n--- &303\nObject: {Name: Last, Value: 9}\n", Deserializer());
            }
            catch (YamlException) { rejected = true; }
            check(rejected, "A malformed required value must fail the import.");
        });

        test("Unity YAML rejects duplicate, missing, and zero object identities", () =>
        {
            foreach (var yaml in new[]
            {
                "--- &101\nObject: {Name: First}\n--- &101\nObject: {Name: Duplicate}\n",
                "---\nObject: {Name: Missing}\n",
                "--- &0\nObject: {Name: Zero}\n"
            })
            {
                bool rejected = false;
                try { UnityYamlDocumentParser.Parse<Document>(yaml, Deserializer()); }
                catch (InvalidDataException) { rejected = true; }
                check(rejected, "Invalid document identity must not be silently accepted.");
            }
        });
    }

    public sealed class Document
    {
        public ObjectData Object { get; set; } = new();
    }

    public sealed class ObjectData
    {
        public string Name { get; set; } = "";
        public ulong Reference { get; set; }
        public int Value { get; set; }
    }
}
