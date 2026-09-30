using System.IO.Compression;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elements.Core;

if (args.Length != 4)
{
    Console.Error.WriteLine("Usage: UITemplateTool PACKAGE CONTRACT BACKUP_DIRECTORY GAME_DIRECTORY");
    return 1;
}
AssemblyLoadContext.Default.Resolving += (context, name) =>
{
    string path = Path.Combine(args[3], name.Name + ".dll");
    return File.Exists(path) ? context.LoadFromAssemblyPath(Path.GetFullPath(path)) : null;
};
return NativeTemplateCleanup.Run(args[0], args[1], args[2]);

internal static class NativeTemplateCleanup
{
    private static readonly string[] RetiredPath = ["Contextual Tablet", "Canvas", "Vertical layout", "Main Area", "Advanced Mode Root"];
    private static DataTreeDictionary Dict(DataTreeNode node) => (DataTreeDictionary)node;
    private static DataTreeList List(DataTreeNode node) => (DataTreeList)node;
    private static string Text(DataTreeNode node) => Convert.ToString(((DataTreeValue)node).Value, System.Globalization.CultureInfo.InvariantCulture)!;
    private static string Id(DataTreeDictionary node) => Text(node["ID"]);
    private static string Name(DataTreeDictionary slot) => Text(Dict(slot["Name"])["Data"]);
    private static DataTreeList Components(DataTreeDictionary slot) => List(Dict(slot["Components"])["Data"]);
    private static IEnumerable<DataTreeDictionary> Slots(DataTreeDictionary root)
    {
        yield return root;
        foreach (var child in List(root["Children"]).Children.Cast<DataTreeDictionary>())
            foreach (var descendant in Slots(child)) yield return descendant;
    }
    private static IEnumerable<(string Key, DataTreeValue Value)> Values(DataTreeNode node)
    {
        if (node is DataTreeDictionary dict)
            foreach (var pair in dict.Children)
            {
                if (pair.Value is DataTreeValue value) yield return (pair.Key, value);
                else foreach (var item in Values(pair.Value)) yield return item;
            }
        else if (node is DataTreeList list)
            foreach (var child in list.Children)
                foreach (var item in Values(child)) yield return item;
    }
    private static void CollectIds(DataTreeNode node, HashSet<string> ids)
    {
        foreach (var (key, value) in Values(node))
            if (key == "ID" || key.EndsWith("-ID", StringComparison.Ordinal)) ids.Add(Text(value));
    }
    private static DataTreeDictionary Resolve(DataTreeDictionary root, IEnumerable<string> path)
    {
        foreach (string segment in path)
            root = List(root["Children"]).Children.Cast<DataTreeDictionary>().Single(child => Name(child) == segment);
        return root;
    }
    private static string TypeName(DataTreeDictionary component, string[] types) =>
        types[Convert.ToInt32(((DataTreeValue)component["Type"]).Value)].Replace("[FrooxEngine]", "", StringComparison.Ordinal);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static int Run(string packagePath, string contractPath, string backupDirectory)
    {
        try
        {
            byte[] originalPackage = File.ReadAllBytes(packagePath), originalContract = File.ReadAllBytes(contractPath);
            var contract = JsonNode.Parse(originalContract)!.AsObject();
            var bindings = contract["bindings"]!.AsObject();
            using var packageStream = new MemoryStream(originalPackage);
            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read);
            var records = archive.Entries.Where(entry => entry.FullName.EndsWith(".record", StringComparison.Ordinal)).ToArray();
            if (records.Length != 1 || records[0].FullName != "R-Main.record") throw new InvalidDataException("Cleanup requires a single-item native package.");
            using var recordStream = records[0].Open();
            var record = JsonNode.Parse(recordStream)!.AsObject();
            string assetPath = "Assets/" + record["assetUri"]!.GetValue<string>().Split('/').Last();
            using var source = archive.GetEntry(assetPath)!.Open();
            byte[] header = new byte[9];
            source.ReadExactly(header);
            if (!header.SequenceEqual(new byte[] { 70, 114, 68, 84, 0, 0, 0, 0, 3 })) throw new InvalidDataException("Unsupported native package format.");
            using var decompressor = new BrotliStream(source, CompressionMode.Decompress);
            using var bson = new MemoryStream();
            decompressor.CopyTo(bson);
            bson.Position = 0;
            var tree = DataTreeConverter.FromRawBSON(bson);
            var root = Dict(tree["Object"]);
            if (Name(root) != contract["rootName"]!.GetValue<string>()) throw new InvalidDataException("Unexpected template root.");
            DataTreeDictionary retired;
            try { retired = Resolve(root, RetiredPath); }
            catch (InvalidOperationException) { Console.WriteLine("No retired advanced layout found; package unchanged."); return 0; }
            if (Convert.ToBoolean(((DataTreeValue)Dict(retired["Active"])["Data"]).Value)) throw new InvalidDataException("Refusing to remove an active layout.");
            string[] types = List(tree["Types"]).Children.Select(Text).ToArray();
            var targets = new Dictionary<string, (DataTreeDictionary Slot, DataTreeDictionary? Component)>();
            foreach (var binding in bindings)
            {
                var spec = binding.Value!;
                var slot = Resolve(root, spec["path"]!.AsArray().Select(node => node!.GetValue<string>()));
                string type = spec["type"]!.GetValue<string>();
                var component = type == "Slot" ? null : Components(slot).Children.Cast<DataTreeDictionary>()
                    .Where(c => TypeName(c, types) == type).ElementAt(spec["index"]!.GetValue<int>());
                targets.Add(binding.Key, (slot, component));
            }
            var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectIds(retired, removed);
            var parent = Resolve(root, RetiredPath[..^1]);
            List(parent["Children"]).Children.Remove(retired);
            int removedDrivers = 0;
            bool changed;
            do
            {
                changed = false;
                foreach (var slot in Slots(root))
                    foreach (var component in Components(slot).Children.Cast<DataTreeDictionary>().ToArray())
                    {
                        var data = Dict(component["Data"]);
                        string type = TypeName(component, types);
                        string? targetMember = type.StartsWith("FrooxEngine.BooleanValueDriver<", StringComparison.Ordinal) ? "TargetField" :
                            type.StartsWith("FrooxEngine.ValueCopy<", StringComparison.Ordinal) ? "Target" : null;
                        if (targetMember == null || !data.Children.TryGetValue(targetMember, out var field) ||
                            !removed.Contains(Text(Dict(field)["Data"]))) continue;
                        CollectIds(component, removed);
                        Components(slot).Children.Remove(component);
                        changed = true;
                        removedDrivers++;
                    }
                foreach (var slot in Slots(root))
                    foreach (var component in Components(slot).Children.Cast<DataTreeDictionary>())
                    {
                        if (!TypeName(component, types).StartsWith("FrooxEngine.ValueMultiDriver<", StringComparison.Ordinal)) continue;
                        var data = Dict(component["Data"]);
                        if (!data.Children.TryGetValue("Drives", out var drives)) continue;
                        var entries = List(Dict(drives)["Data"]);
                        foreach (var entry in entries.Children.Cast<DataTreeDictionary>().ToArray())
                        {
                            if (!removed.Contains(Text(entry["Data"]))) continue;
                            CollectIds(entry, removed);
                            entries.Children.Remove(entry);
                            changed = true;
                        }
                    }
            } while (changed);
            // Any unexpected dependency aborts instead of leaving a broken graph.
            foreach (var (key, value) in Values(tree))
                if (key == "Data" && value.Value is string target && removed.Contains(target))
                    throw new InvalidDataException($"A retained component still references the retired layout ({target}; {removedDrivers} drivers selected).");
            foreach (var pair in targets)
            {
                string id = pair.Value.Component == null ? Id(pair.Value.Slot) : Id(Dict(pair.Value.Component["Data"]));
                if (removed.Contains(id)) { bindings.Remove(pair.Key); continue; }
                if (pair.Value.Component != null)
                {
                    string type = TypeName(pair.Value.Component, types);
                    var siblings = Components(pair.Value.Slot).Children.Cast<DataTreeDictionary>().Where(c => TypeName(c, types) == type).ToArray();
                    bindings[pair.Key]!["index"] = Array.IndexOf(siblings, pair.Value.Component);
                }
            }
            using var rawOutput = new MemoryStream();
            DataTreeConverter.ToRawBSON(tree, rawOutput);
            using var nativeOutput = new MemoryStream();
            nativeOutput.Write(header);
            using (var compressor = new BrotliStream(nativeOutput, CompressionLevel.Optimal, leaveOpen: true)) compressor.Write(rawOutput.ToArray());
            byte[] nativeBytes = nativeOutput.ToArray();
            string newAssetPath = "Assets/" + Hash(nativeBytes);
            record["assetUri"] = "packdb:///" + Hash(nativeBytes);
            using var output = new MemoryStream();
            using (var cleaned = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var entry in archive.Entries)
                {
                    var next = cleaned.CreateEntry(entry.FullName == assetPath ? newAssetPath : entry.FullName, CompressionLevel.NoCompression);
                    next.LastWriteTime = entry.LastWriteTime;
                    using var destination = next.Open();
                    if (entry.FullName == assetPath) destination.Write(nativeBytes);
                    else if (entry.FullName == "R-Main.record") destination.Write(System.Text.Encoding.UTF8.GetBytes(record.ToJsonString()));
                    else { using var input = entry.Open(); input.CopyTo(destination); }
                }
            }
            Directory.CreateDirectory(backupDirectory);
            string backup = Path.Combine(backupDirectory, "station-before-layout-cleanup-" + Hash(originalPackage) + ".ResonitePackage");
            File.WriteAllBytes(backup, originalPackage);
            File.WriteAllBytes(backup + ".bindings.json", originalContract);
            if (!File.ReadAllBytes(backup).SequenceEqual(originalPackage)) throw new IOException("Backup verification failed.");
            File.WriteAllBytes(packagePath + ".tmp", output.ToArray());
            File.WriteAllText(contractPath + ".tmp", contract.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
            File.Move(packagePath + ".tmp", packagePath, overwrite: true);
            File.Move(contractPath + ".tmp", contractPath, overwrite: true);
            Console.WriteLine($"Removed the inactive advanced layout and {removedDrivers} obsolete drivers; {bindings.Count} bindings retained. Original files backed up.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("Native template cleanup failed: " + error.Message); return 1; }
    }
}
