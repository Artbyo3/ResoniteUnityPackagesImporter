using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Elements.Core;

// Recovery consumes an explicit plan whose provider fields were compared with
// the read-only live reference. Export-local GUIDs alone are never evidence.
internal static class NativeProviderRecovery
{
    private static DataTreeDictionary Dict(DataTreeNode n) => (DataTreeDictionary)n;
    private static DataTreeList List(DataTreeNode n) => (DataTreeList)n;
    private static string Text(DataTreeNode n) => Convert.ToString(((DataTreeValue)n).Value)!;
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static IEnumerable<DataTreeDictionary> Slots(DataTreeDictionary root)
    {
        yield return root;
        foreach (var child in List(root["Children"]).Children.Cast<DataTreeDictionary>())
            foreach (var slot in Slots(child)) yield return slot;
    }
    private static IEnumerable<(string Key, DataTreeValue Value)> Values(DataTreeNode node)
    {
        if (node is DataTreeDictionary dict)
            foreach (var pair in dict.Children)
            {
                if (pair.Value is DataTreeValue value) yield return (pair.Key, value);
                else foreach (var child in Values(pair.Value)) yield return child;
            }
        else if (node is DataTreeList list)
            foreach (var item in list.Children)
                foreach (var child in Values(item)) yield return child;
    }
    private static DataTreeDictionary Read(ZipArchive zip, out JsonObject record, out string assetName, out byte[] header)
    {
        using var recordStream = zip.GetEntry("R-Main.record")!.Open();
        record = JsonNode.Parse(recordStream)!.AsObject();
        assetName = "Assets/" + record["assetUri"]!.GetValue<string>().Split('/').Last();
        using var source = zip.GetEntry(assetName)!.Open();
        header = new byte[9]; source.ReadExactly(header);
        if (!header.SequenceEqual(new byte[] { 70, 114, 68, 84, 0, 0, 0, 0, 3 })) throw new InvalidDataException("Unsupported native object encoding.");
        using var decompressor = new BrotliStream(source, CompressionMode.Decompress);
        using var bson = new MemoryStream(); decompressor.CopyTo(bson); bson.Position = 0;
        return DataTreeConverter.FromRawBSON(bson);
    }
    private static void Remap(DataTreeNode node, Dictionary<string, string> ids)
    {
        if (node is DataTreeDictionary dict)
            foreach (string key in dict.Children.Keys.ToArray())
            {
                var child = dict.Children[key];
                if (child is DataTreeValue value && value.Value is string old && ids.TryGetValue(old, out string? replacement))
                    dict.Children[key] = new DataTreeValue(replacement);
                else Remap(child, ids);
            }
        else if (node is DataTreeList list)
            foreach (var child in list.Children) Remap(child, ids);
    }

    public static int Run(string gameDirectory, string target, string source, string planPath, string backupDirectory, string assetSlotName)
    {
        string temporary = target + ".recovered.tmp";
        bool ownsTemporary = false;
        try
        {
            if (File.Exists(temporary)) throw new IOException("Recovery temporary path already exists; preserve and inspect it before retrying.");
            byte[] targetBytes = File.ReadAllBytes(target), sourceBytes = File.ReadAllBytes(source);
            var plan = JsonNode.Parse(File.ReadAllBytes(planPath))!.AsObject();
            if (Hash(targetBytes) != plan["targetPackageSha256"]!.GetValue<string>() || Hash(sourceBytes) != plan["sourcePackageSha256"]!.GetValue<string>())
                throw new InvalidDataException("Recovery inputs differ from the verified plan.");
            using var targetStream = new MemoryStream(targetBytes); using var targetZip = new ZipArchive(targetStream, ZipArchiveMode.Read);
            using var sourceStream = new MemoryStream(sourceBytes); using var sourceZip = new ZipArchive(sourceStream, ZipArchiveMode.Read);
            var tree = Read(targetZip, out var record, out string oldAsset, out var header);
            var oldTree = Read(sourceZip, out _, out _, out _);
            var types = List(tree["Types"]).Children.Select(Text).ToArray();
            var oldTypes = List(oldTree["Types"]).Children.Select(Text).ToArray();
            var components = Slots(Dict(oldTree["Object"])).SelectMany(slot => List(Dict(slot["Components"])["Data"]).Children.Cast<DataTreeDictionary>())
                .Concat(List(oldTree["Assets"]).Children.Cast<DataTreeDictionary>())
                .ToDictionary(component => Text(Dict(component["Data"])["ID"]));
            var existing = Values(tree).Where(pair => pair.Key == "ID" || pair.Key.EndsWith("-ID", StringComparison.Ordinal)).Select(pair => Text(pair.Value)).ToHashSet();
            var mappings = plan["rootMappings"]!.AsObject().ToDictionary(pair => pair.Key, pair => pair.Value!.GetValue<string>());
            if (mappings.Values.Any(existing.Contains)) throw new InvalidDataException("Refusing to replace an existing object.");
            var selected = plan["providerIds"]!.AsArray().Select(id => components[id!.GetValue<string>()]).ToArray();
            string[] allowed = ["FrooxEngine.SpriteProvider", "FrooxEngine.UI_UnlitMaterial", "FrooxEngine.FontChain", "FrooxEngine.StaticFont", "FrooxEngine.StaticTexture2D"];
            foreach (var component in selected)
            {
                string type = oldTypes[Convert.ToInt32(((DataTreeValue)component["Type"]).Value)];
                if (!allowed.Contains(type.Replace("[FrooxEngine]", ""))) throw new InvalidDataException("Recovery plan contains a non-render provider: " + type);
                if (!types.Contains(type)) throw new InvalidDataException("Provider type is absent from target schema.");
                foreach (var (key, value) in Values(component))
                    if (key == "ID" || key.EndsWith("-ID", StringComparison.Ordinal))
                        mappings.TryAdd(Text(value), Guid.NewGuid().ToString());
            }
            var destination = Slots(Dict(tree["Object"])).Single(slot => Text(Dict(slot["Name"])["Data"]) == assetSlotName);
            foreach (var component in selected)
            {
                string type = oldTypes[Convert.ToInt32(((DataTreeValue)component["Type"]).Value)];
                component.Children["Type"] = new DataTreeValue(Array.IndexOf(types, type));
                Remap(component, mappings);
                var data = Dict(component["Data"]);
                if (!data.Children.TryGetValue("persistent", out var persistent)) throw new InvalidDataException("Expected explicit provider persistence field.");
                Dict(persistent).Children["Data"] = new DataTreeValue(true);
                List(Dict(destination["Components"])["Data"]).Children.Add(component);
            }
            using var bson = new MemoryStream(); DataTreeConverter.ToRawBSON(tree, bson);
            using var payload = new MemoryStream(); payload.Write(header);
            using (var compressor = new BrotliStream(payload, CompressionLevel.Optimal, leaveOpen: true)) compressor.Write(bson.ToArray());
            byte[] native = payload.ToArray(); string newHash = Hash(native);
            record["assetUri"] = "packdb:///" + newHash;
            if (record["assetManifest"] is JsonArray manifest)
                foreach (var asset in manifest.OfType<JsonObject>())
                    if (asset["hash"]!.GetValue<string>() == oldAsset["Assets/".Length..]) { asset["hash"] = newHash; asset["bytes"] = native.Length; }
            using (var output = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                ownsTemporary = true;
                foreach (var entry in targetZip.Entries)
                {
                    var next = output.CreateEntry(entry.FullName == oldAsset ? "Assets/" + newHash : entry.FullName, CompressionLevel.NoCompression);
                    next.LastWriteTime = entry.LastWriteTime;
                    using var stream = next.Open();
                    if (entry.FullName == oldAsset) stream.Write(native);
                    else if (entry.FullName == "R-Main.record") stream.Write(System.Text.Encoding.UTF8.GetBytes(record.ToJsonString()));
                    else { using var original = entry.Open(); original.CopyTo(stream); }
                }
            }
            if (NativePackageAudit.Run(gameDirectory, [temporary]) != 0) throw new InvalidDataException("Recovered package failed native reference validation.");
            Directory.CreateDirectory(backupDirectory);
            string backup = Path.Combine(backupDirectory, "station-before-provider-recovery-" + Hash(targetBytes) + ".ResonitePackage");
            if (!File.Exists(backup)) File.WriteAllBytes(backup, targetBytes);
            if (Hash(File.ReadAllBytes(backup)) != Hash(targetBytes)) throw new InvalidDataException("Recovery backup verification failed.");
            File.Move(temporary, target, overwrite: true);
            Console.WriteLine($"Restored {selected.Length} verified persistent render providers. Existing assets and UI controls preserved. Backup: {backup}");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("Provider recovery failed: " + error.Message); return 1; }
        finally { if (ownsTemporary && File.Exists(temporary)) File.Delete(temporary); }
    }
}
