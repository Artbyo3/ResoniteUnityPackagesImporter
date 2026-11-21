using UnityPackageImporter.Models;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

internal static class UnityYamlDocumentParserTests
{
    public static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        IDeserializer Deserializer() => new DeserializerBuilder()
            .WithTypeConverter(new UnityFileIdConverter())
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

        test("Unity YAML preserves signed fileIDs in reference dictionaries", () =>
        {
            const long signedId = -5487658955361336164;
            var documents = UnityYamlDocumentParser.Parse<Document>("--- &-5487658955361336164\nObject: {Name: Root}\n--- &7299590933040088953\nObject:\n  Target: {fileID: -5487658955361336164}\n", Deserializer());
            var child = documents[7299590933040088953].Object;
            check(child.Target["fileID"] == unchecked((ulong)signedId),
                "Negative references must retain the same bitwise identity as document anchors.");
            check(documents[child.Target["fileID"]].Object.Name == "Root", "Signed cross-object reference did not resolve.");
        });

        test("Signed reference regression reproduces the unconfigured deserializer failure", () =>
        {
            bool rejected = false;
            try
            {
                UnityYamlDocumentParser.Parse<Document>("--- &101\nObject: {Target: {fileID: -5487658955361336164}}\n",
                    new DeserializerBuilder().Build());
            }
            catch (YamlException) { rejected = true; }
            check(rejected, "Baseline no longer reproduces the signed-reference failure; review the compatibility adapter.");
        });

        foreach (string target in new[] { "{fileID: 1, fileID: 2}", "{fileID: 1, type: -1}", "{fileID: {nested: 1}}" })
            test("Unity reference conversion rejects corrupt mapping " + target, () =>
            {
                bool rejected = false;
                try { UnityYamlDocumentParser.Parse<Document>("--- &101\nObject: {Target: " + target + "}\n", Deserializer()); }
                catch (YamlException) { rejected = true; }
                check(rejected, "Reference conversion must not accept duplicates, negative non-identities or nested scalar values.");
            });

        test("Unity reference converter does not reinterpret unrelated unsigned fields", () =>
        {
            bool rejected = false;
            try { UnityYamlDocumentParser.Parse<Document>("--- &101\nObject: {Reference: -1}\n", Deserializer()); }
            catch (YamlException) { rejected = true; }
            check(rejected, "Signed compatibility must be confined to fileID mappings, not all unsigned data.");
        });

        foreach (string identity in new[] { "0", "9223372036854775807", "-9223372036854775808", "18446744073709551615" })
            test("Unity YAML accepts fileID boundary " + identity, () =>
            {
                ulong expected = identity.StartsWith('-') ? unchecked((ulong)long.Parse(identity)) : ulong.Parse(identity);
                var document = UnityYamlDocumentParser.Parse<Document>("--- &101\nObject: {Target: {fileID: " + identity + "}}\n", Deserializer());
                check(document[101].Object.Target["fileID"] == expected, "Boundary identity changed.");
            });

        foreach (string identity in new[] { "-9223372036854775809", "18446744073709551616", "1.0", "invalid", "null" })
            test("Unity YAML rejects invalid fileID " + identity, () =>
            {
                bool rejected = false;
                try { UnityYamlDocumentParser.Parse<Document>("--- &101\nObject: {Target: {fileID: " + identity + "}}\n", Deserializer()); }
                catch (YamlException) { rejected = true; }
                check(rejected, "Invalid identity must not truncate, default or alias another object.");
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
        public Dictionary<string, ulong> Target { get; set; } = new();
        public int Value { get; set; }
    }
}
