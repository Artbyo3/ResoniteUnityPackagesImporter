using System.IO.Compression;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elements.Core;

if (args.Length >= 3 && args[0] == "--validate")
    return NativePackageAudit.Run(args[1], args[2..]);
if (args.Length == 2 && args[0] == "--test-validation")
    return NativePackageAudit.Test(args[1]);
if (args.Length == 7 && args[0] == "--restore-providers")
    return NativeProviderRecovery.Run(args[1], args[2], args[3], args[4], args[5], args[6]);
if (args.Length == 4 && args[0] == "--strip-unreferenced-shaders")
{
    AssemblyLoadContext.Default.Resolving += (context, name) =>
    {
        string path = Path.Combine(args[3], name.Name + ".dll");
        return File.Exists(path) ? context.LoadFromAssemblyPath(Path.GetFullPath(path)) : null;
    };
    return NativeTemplateCleanup.StripUnreferencedShaders(args[1], args[2]);
}
if (args.Length >= 3 && args[0] == "--inspect")
{
    Inspector.Run(args[1], args[2], args.Length > 3 ? int.Parse(args[3]) : 10);
    return 0;
}
if (args.Length >= 3 && args[0] == "--tree")
{
    Inspector.Tree(args[1], args[2], args.Length > 3 ? int.Parse(args[3]) : 10);
    return 0;
}
if (args.Length >= 3 && args[0] == "--find-type")
{
    Inspector.FindType(args[1], args[2]);
    return 0;
}
if (args.Length >= 3 && args[0] == "--find-id")
{
    Inspector.FindId(args[1], args[2]);
    return 0;
}
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

    public static int StripUnreferencedShaders(string packagePath, string backupDirectory)
    {
        try
        {
            byte[] originalPackage = File.ReadAllBytes(packagePath);
            using var packageStream = new MemoryStream(originalPackage);
            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read);
            var records = archive.Entries.Where(entry => entry.FullName.EndsWith(".record", StringComparison.Ordinal)).ToArray();
            if (records.Length != 1 || records[0].FullName != "R-Main.record")
                throw new InvalidDataException("Shader cleanup requires a single-item native package.");
            using var recordStream = records[0].Open();
            var record = JsonNode.Parse(recordStream)!.AsObject();
            string assetPath = "Assets/" + record["assetUri"]!.GetValue<string>().Split('/').Last();
            using var source = archive.GetEntry(assetPath)!.Open();
            byte[] header = new byte[9];
            source.ReadExactly(header);
            if (!header.SequenceEqual(new byte[] { 70, 114, 68, 84, 0, 0, 0, 0, 3 }))
                throw new InvalidDataException("Unsupported native package format.");
            using var decompressor = new BrotliStream(source, CompressionMode.Decompress);
            using var bson = new MemoryStream();
            decompressor.CopyTo(bson);
            bson.Position = 0;
            var tree = DataTreeConverter.FromRawBSON(bson);
            var root = Dict(tree["Object"]);
            string[] types = List(tree["Types"]).Children.Select(Text).ToArray();

            var referencedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in Values(tree))
            {
                if (key != "ID" && !key.EndsWith("-ID", StringComparison.Ordinal) && value.Value is string candidate && Guid.TryParse(candidate, out _))
                {
                    referencedTargets.Add(candidate);
                }
            }

            int removedShaders = 0;
            foreach (var slot in Slots(root))
            {
                foreach (var component in Components(slot).Children.Cast<DataTreeDictionary>().ToArray())
                {
                    if (TypeName(component, types) != "FrooxEngine.StaticShader") continue;
                    string id = Id(Dict(component["Data"]));
                    if (!referencedTargets.Contains(id))
                    {
                        Components(slot).Children.Remove(component);
                        removedShaders++;
                    }
                }
            }

            if (removedShaders == 0)
            {
                Console.WriteLine("No unreferenced StaticShader components found; package unchanged.");
                return 0;
            }

            using var rawOutput = new MemoryStream();
            DataTreeConverter.ToRawBSON(tree, rawOutput);
            using var nativeOutput = new MemoryStream();
            nativeOutput.Write(header);
            using (var compressor = new BrotliStream(nativeOutput, CompressionLevel.Optimal, leaveOpen: true))
                compressor.Write(rawOutput.ToArray());
            byte[] nativeBytes = nativeOutput.ToArray();
            string newMainHash = Hash(nativeBytes);
            string newAssetPath = "Assets/" + newMainHash;
            record["assetUri"] = "packdb:///" + newMainHash;

            var referencedAssetHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { newMainHash };
            foreach (var (_, value) in Values(tree))
            {
                string text = Text(value).TrimStart('@');
                if (text.StartsWith("packdb:///", StringComparison.OrdinalIgnoreCase))
                {
                    referencedAssetHashes.Add(text.Split('/').Last());
                }
            }

            if (record["assetManifest"] is JsonArray manifestArray)
            {
                for (int i = manifestArray.Count - 1; i >= 0; i--)
                {
                    string h = manifestArray[i]!["hash"]!.GetValue<string>();
                    if (!referencedAssetHashes.Contains(h))
                    {
                        manifestArray.RemoveAt(i);
                    }
                }
            }

            using var output = new MemoryStream();
            using (var cleaned = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.StartsWith("Assets/", StringComparison.Ordinal) && entry.FullName != assetPath)
                    {
                        string h = entry.FullName["Assets/".Length..];
                        if (!referencedAssetHashes.Contains(h)) continue;
                    }
                    else if (entry.FullName.StartsWith("Variants/", StringComparison.Ordinal))
                    {
                        string[] parts = entry.FullName.Split('/');
                        if (parts.Length >= 2 && !referencedAssetHashes.Contains(parts[1])) continue;
                    }
                    else if (entry.FullName.StartsWith("Metadata/", StringComparison.Ordinal))
                    {
                        string file = entry.FullName["Metadata/".Length..];
                        string h = file.Split('.')[0];
                        if (!referencedAssetHashes.Contains(h)) continue;
                    }

                    var next = cleaned.CreateEntry(entry.FullName == assetPath ? newAssetPath : entry.FullName, CompressionLevel.NoCompression);
                    next.LastWriteTime = entry.LastWriteTime;
                    using var destination = next.Open();
                    if (entry.FullName == assetPath) destination.Write(nativeBytes);
                    else if (entry.FullName == "R-Main.record") destination.Write(System.Text.Encoding.UTF8.GetBytes(record.ToJsonString()));
                    else { using var input = entry.Open(); input.CopyTo(destination); }
                }
            }

            Directory.CreateDirectory(backupDirectory);
            string backup = Path.Combine(backupDirectory, Path.GetFileNameWithoutExtension(packagePath) + "-before-shader-cleanup-" + Hash(originalPackage) + ".ResonitePackage");
            File.WriteAllBytes(backup, originalPackage);
            if (!File.ReadAllBytes(backup).SequenceEqual(originalPackage)) throw new IOException("Backup verification failed.");
            File.WriteAllBytes(packagePath + ".tmp", output.ToArray());
            File.Move(packagePath + ".tmp", packagePath, overwrite: true);
            Console.WriteLine($"Removed {removedShaders} unreferenced StaticShader component(s) and their unused shader blobs from {Path.GetFileName(packagePath)}.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("StaticShader cleanup failed: " + error.Message);
            return 1;
        }
    }
}

internal static class Inspector
{
    private static DataTreeDictionary Dict(DataTreeNode node) => (DataTreeDictionary)node;
    private static DataTreeList List(DataTreeNode node) => (DataTreeList)node;
    private static string Text(DataTreeNode node) => Convert.ToString(((DataTreeValue)node).Value, System.Globalization.CultureInfo.InvariantCulture)!;
    private static string Name(DataTreeDictionary slot) => Text(Dict(slot["Name"])["Data"]);
    private static DataTreeList Components(DataTreeDictionary slot) => List(Dict(slot["Components"])["Data"]);

    private static DataTreeDictionary LoadPackage(string path, out string[] types)
    {
        using var zip = ZipFile.OpenRead(path);
        var recordEntry = zip.Entries.First(e => e.FullName.EndsWith(".record", StringComparison.Ordinal));
        using var recStream = recordEntry.Open();
        var record = JsonNode.Parse(recStream)!.AsObject();
        string assetFile = record["assetUri"]!.GetValue<string>().Split('/').Last();
        var entry = zip.Entries.First(e => e.FullName.EndsWith(assetFile, StringComparison.Ordinal));
        using var stream = entry.Open();
        byte[] header = new byte[9];
        stream.ReadExactly(header);
        using var brotli = new BrotliStream(stream, CompressionMode.Decompress);
        using var ms = new MemoryStream();
        brotli.CopyTo(ms);
        ms.Position = 0;
        var tree = DataTreeConverter.FromRawBSON(ms);
        types = List(tree["Types"]).Children.Select(Text).ToArray();
        return Dict(tree["Object"]);
    }

    private static string NodeString(DataTreeNode node)
    {
        if (node is DataTreeValue v) return Convert.ToString(v.Value, System.Globalization.CultureInfo.InvariantCulture)!;
        if (node is DataTreeList l) return "[" + string.Join(", ", l.Children.Select(NodeString)) + "]";
        if (node is DataTreeDictionary d)
        {
            if (d.Children.TryGetValue("Data", out var inner)) return NodeString(inner);
            return "{" + string.Join(", ", d.Children.Select(kv => kv.Key + ":" + NodeString(kv.Value))) + "}";
        }
        return node?.ToString() ?? "";
    }

    private static void DumpTree(DataTreeDictionary slot, string[] types, string indent = "", int maxDepth = 10, int currentDepth = 0)
    {
        string name = Name(slot);
        var comps = Components(slot).Children.Cast<DataTreeDictionary>().Select(c => {
            int tIndex = Convert.ToInt32(((DataTreeValue)c["Type"]).Value);
            return types[tIndex].Replace("[FrooxEngine]", "");
        }).ToList();

        Console.WriteLine(indent + "Slot: " + name + " (" + comps.Count + " comps: " + string.Join(", ", comps) + ")");
        if (currentDepth < maxDepth)
        {
            foreach (var child in List(slot["Children"]).Children.Cast<DataTreeDictionary>())
            {
                DumpTree(child, types, indent + "  ", maxDepth, currentDepth + 1);
            }
        }
    }

    private static void DumpSlot(DataTreeDictionary slot, string[] types, string indent = "", int maxDepth = 10, int currentDepth = 0)
    {
        string name = Name(slot);
        var comps = Components(slot).Children.Cast<DataTreeDictionary>().Select(c => {
            int tIndex = Convert.ToInt32(((DataTreeValue)c["Type"]).Value);
            return types[tIndex].Replace("[FrooxEngine]", "");
        }).ToList();

        Console.WriteLine(indent + "Slot: " + name + " (" + comps.Count + " comps: " + string.Join(", ", comps) + ")");
        foreach (var comp in Components(slot).Children.Cast<DataTreeDictionary>())
        {
            int tIndex = Convert.ToInt32(((DataTreeValue)comp["Type"]).Value);
            string tName = types[tIndex].Replace("[FrooxEngine]", "");
            var data = Dict(comp["Data"]);
            foreach (var kvp in data.Children)
            {
                Console.WriteLine(indent + "   " + tName + " -> " + kvp.Key + " = " + NodeString(kvp.Value));
            }
        }

        if (currentDepth < maxDepth)
        {
            foreach (var child in List(slot["Children"]).Children.Cast<DataTreeDictionary>())
            {
                DumpSlot(child, types, indent + "  ", maxDepth, currentDepth + 1);
            }
        }
    }

    private static DataTreeDictionary? ResolvePath(DataTreeDictionary slot, string[] parts, int index = 0)
    {
        if (index >= parts.Length) return slot;
        string part = parts[index];
        foreach (var child in List(slot["Children"]).Children.Cast<DataTreeDictionary>())
        {
            if (Name(child) == part)
            {
                var found = ResolvePath(child, parts, index + 1);
                if (found != null) return found;
            }
        }
        return null;
    }

    private static DataTreeDictionary? FindChild(DataTreeDictionary slot, string targetName)
    {
        if (targetName.Contains('/'))
        {
            var parts = targetName.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return ResolvePath(slot, parts);
        }
        if (Name(slot) == targetName) return slot;
        foreach (var child in List(slot["Children"]).Children.Cast<DataTreeDictionary>())
        {
            var res = FindChild(child, targetName);
            if (res != null) return res;
        }
        return null;
    }

    private static DataTreeDictionary? FindComponent(DataTreeDictionary slot, string id, out DataTreeDictionary? owningSlot)
    {
        foreach (var comp in Components(slot).Children.Cast<DataTreeDictionary>())
        {
            if (comp.Children.TryGetValue("Data", out var dataNode) && dataNode is DataTreeDictionary d)
            {
                if (d.Children.TryGetValue("ID", out var idNode) && Text(idNode) == id)
                {
                    owningSlot = slot;
                    return comp;
                }
            }
        }
        foreach (var child in List(slot["Children"]).Children.Cast<DataTreeDictionary>())
        {
            var res = FindComponent(child, id, out owningSlot);
            if (res != null) return res;
        }
        owningSlot = null;
        return null;
    }

    public static void Run(string packagePath, string slotName, int maxDepth = 10)
    {
        var pkg = LoadPackage(packagePath, out var types);
        if (slotName.StartsWith("id:", StringComparison.Ordinal))
        {
            string id = slotName[3..];
            var comp = FindComponent(pkg, id, out var owner);
            if (comp != null)
            {
                int tIndex = Convert.ToInt32(((DataTreeValue)comp["Type"]).Value);
                string tName = types[tIndex].Replace("[FrooxEngine]", "");
                Console.WriteLine($"=== Component {id} ({tName}) on slot '{Name(owner!)}' ===");
                var data = Dict(comp["Data"]);
                foreach (var kvp in data.Children)
                {
                    if (kvp.Value is DataTreeDictionary fieldDict)
                    {
                        var keys = string.Join(", ", fieldDict.Children.Select(p => p.Key + ":" + NodeString(p.Value)));
                        Console.WriteLine($"   {kvp.Key} = {{{keys}}}");
                    }
                    else
                    {
                        Console.WriteLine($"   {kvp.Key} = {NodeString(kvp.Value)}");
                    }
                }
            }
            else
            {
                Console.WriteLine("Could not find component with ID: " + id);
            }
            return;
        }
        var target = FindChild(pkg, slotName);
        if (target != null)
        {
            Console.WriteLine("=== Dump of " + slotName + " in " + packagePath + " ===");
            DumpSlot(target, types, "", maxDepth);
        }
        else
        {
            Console.WriteLine("Could not find slot: " + slotName);
        }
    }

    public static void Tree(string packagePath, string slotName, int maxDepth = 10)
    {
        var pkg = LoadPackage(packagePath, out var types);
        var target = FindChild(pkg, slotName);
        if (target != null)
        {
            Console.WriteLine("=== Tree of " + slotName + " in " + packagePath + " ===");
            DumpTree(target, types, "", maxDepth);
        }
        else
        {
            Console.WriteLine("Could not find slot: " + slotName);
        }
    }

    public static void FindType(string packagePath, string typeSubstr)
    {
        var pkg = LoadPackage(packagePath, out var types);
        void SearchSlot(DataTreeDictionary slot, string path)
        {
            string currentPath = string.IsNullOrEmpty(path) ? Name(slot) : path + "/" + Name(slot);
            foreach (var comp in Components(slot).Children.Cast<DataTreeDictionary>())
            {
                int tIndex = Convert.ToInt32(((DataTreeValue)comp["Type"]).Value);
                string tName = types[tIndex].Replace("[FrooxEngine]", "");
                if (tName.Contains(typeSubstr, StringComparison.OrdinalIgnoreCase))
                {
                    var data = Dict(comp["Data"]);
                    string compId = data.Children.TryGetValue("ID", out var idNode) ? Text(idNode) : "?";
                    Console.WriteLine($"Found {tName} (ID: {compId}) at: {currentPath}");
                    foreach (var kvp in data.Children)
                    {
                        Console.WriteLine($"   {kvp.Key} = {NodeString(kvp.Value)}");
                    }
                }
            }
            foreach (var child in List(slot["Children"]).Children.Cast<DataTreeDictionary>())
            {
                SearchSlot(child, currentPath);
            }
        }
        SearchSlot(pkg, "");
    }

    public static void FindId(string packagePath, string searchId)
    {
        var pkg = LoadPackage(packagePath, out var types);
        void Search(DataTreeNode node, string path)
        {
            if (node is DataTreeDictionary dict)
            {
                foreach (var kvp in dict.Children)
                {
                    if (kvp.Value is DataTreeValue val && Text(val) == searchId)
                    {
                        Console.WriteLine($"Found {searchId} at key '{kvp.Key}' along path: {path}");
                    }
                    Search(kvp.Value, path + "." + kvp.Key);
                }
            }
            else if (node is DataTreeList list)
            {
                for (int i = 0; i < list.Children.Count; i++)
                    Search(list.Children[i], path + "[" + i + "]");
            }
        }
        Search(pkg, "Root");
    }
}
