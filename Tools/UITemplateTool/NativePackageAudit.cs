using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Elements.Core;

// The native format stores references AND ordinary strings as BSON strings.
// Resolve the installed engine's declared field types before interpreting a GUID.
internal sealed class NativePackageAudit
{
    private readonly HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> assets;
    private readonly List<string> errors = [];
    private readonly Dictionary<Type, Dictionary<string, Type>> fields = [];
    private int references;

    private NativePackageAudit(IEnumerable<string> assets) => this.assets = assets.ToHashSet(StringComparer.Ordinal);
    private static object? Value(DataTreeNode node) => (node as DataTreeValue)?.Value;
    private static string Text(DataTreeNode node) => Convert.ToString(Value(node), System.Globalization.CultureInfo.InvariantCulture)!;
    private static DataTreeDictionary Dict(DataTreeNode node) => (DataTreeDictionary)node;
    private static DataTreeList List(DataTreeNode node) => (DataTreeList)node;

    private static void LoadEngine(string directory)
    {
        directory = Path.GetFullPath(directory);
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            string path = Path.Combine(directory, name.Name + ".dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        };
        AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(directory, "FrooxEngine.dll"));
    }

    private static Type ResolveType(string name) => NiceTypeParser.TryParse(name, part =>
    {
        if (part.StartsWith('['))
        {
            int end = part.IndexOf(']');
            var assembly = Assembly.Load(part[1..end]);
            return assembly.GetType(part[(end + 1)..], throwOnError: true)!;
        }
        return TypeHelper.FindType(part);
    }) ?? throw new InvalidDataException("Cannot resolve native component type: " + name);

    private static IEnumerable<(string Key, DataTreeValue Value)> Values(DataTreeNode node)
    {
        if (node is DataTreeDictionary dict)
            foreach (var pair in dict.Children)
                if (pair.Value is DataTreeValue value) yield return (pair.Key, value);
                else foreach (var child in Values(pair.Value)) yield return child;
        else if (node is DataTreeList list)
            foreach (var item in list.Children)
                foreach (var child in Values(item)) yield return child;
    }

    private Dictionary<string, Type> Fields(Type type)
    {
        if (fields.TryGetValue(type, out var result)) return result;
        result = [];
        for (Type? current = type; current != null; current = current.BaseType)
            foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                result.TryAdd(field.Name, field.FieldType);
        fields.Add(type, result);
        return result;
    }

    private static Type? GenericBase(Type type, string name)
    {
        for (Type? current = type; current != null; current = current.BaseType)
            if (current.IsGenericType && current.GetGenericTypeDefinition().FullName == name)
                return current.GetGenericArguments()[0];
        return null;
    }

    private void Reference(DataTreeNode node, string path)
    {
        object? raw = Value(node);
        if (raw == null) return;
        string target = Convert.ToString(raw)!;
        references++;
        if (!Guid.TryParse(target, out _) || !ids.Contains(target))
            errors.Add(path + " references missing object " + target);
    }

    private void ValidateNode(Type type, DataTreeNode node, string path)
    {
        if (node is not DataTreeDictionary dict) return;
        Type? scalar = GenericBase(type, "FrooxEngine.SyncField`1");
        if (scalar != null)
        {
            if (!dict.Children.TryGetValue("Data", out var data)) return;
            if (scalar.FullName == "Elements.Core.RefID") Reference(data, path);
            else if (scalar == typeof(Uri) && Value(data) is Uri uri)
                ValidateUri(uri.ToString(), path);
            else if (scalar == typeof(Uri) && Value(data) is string url)
                ValidateUri(url.TrimStart('@'), path);
            // Sync<string> content may legitimately look exactly like a GUID.
            return;
        }
        Type? element = GenericBase(type, "FrooxEngine.SyncElementList`1");
        if (element != null && dict.Children.TryGetValue("Data", out var entries) && entries is DataTreeList list)
        {
            for (int i = 0; i < list.Children.Count; i++) ValidateNode(element, list.Children[i], path + "[" + i + "]");
            return;
        }
        var declared = Fields(type);
        foreach (var pair in dict.Children)
        {
            if (pair.Key == "ID" || pair.Key.EndsWith("-ID", StringComparison.Ordinal)) continue;
            if (declared.TryGetValue(pair.Key, out var fieldType))
                ValidateNode(fieldType, pair.Value, path + "." + pair.Key);
            else
                // Never silently approve an unresolved GUID in an unknown schema.
                foreach (var (key, value) in Values(pair.Value))
                    if (key is "Data" or "Target" && Value(value) is string candidate && Guid.TryParse(candidate, out _) && !ids.Contains(candidate))
                        errors.Add(path + "." + pair.Key + " has an unresolved GUID in an unrecognized serialized field: " + candidate);
        }
    }

    private void ValidateUri(string uri, string path)
    {
        if (uri.StartsWith("packdb:///", StringComparison.Ordinal))
        {
            if (!assets.Contains("Assets/" + uri[10..])) errors.Add(path + " has an unbundled package asset " + uri);
        }
        else if (uri.StartsWith("local:", StringComparison.OrdinalIgnoreCase) || uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            errors.Add(path + " retains a machine-local asset URI " + uri);
    }

    private void Slot(DataTreeDictionary slot, string[] types, string parent)
    {
        string path = parent + "/" + Text(Dict(slot["Name"])["Data"]);
        if (slot.Children.TryGetValue("ParentReference", out var parentReference) && parentReference is DataTreeDictionary parentField && parentField.Children.TryGetValue("Data", out var parentData))
            Reference(parentData, path + ".ParentReference");
        foreach (var component in List(Dict(slot["Components"])["Data"]).Children.Cast<DataTreeDictionary>())
        {
            string type = types[Convert.ToInt32(Value(component["Type"]))];
            ValidateNode(ResolveType(type), component["Data"], path + " (" + type + ")");
        }
        foreach (var child in List(slot["Children"]).Children.Cast<DataTreeDictionary>()) Slot(child, types, path);
    }

    private void Validate(DataTreeDictionary tree)
    {
        foreach (var (key, value) in Values(tree))
            if ((key == "ID" || key.EndsWith("-ID", StringComparison.Ordinal)) && Value(value) is string id && !ids.Add(id))
                errors.Add("Duplicate native object ID: " + id);
        string[] types = List(tree["Types"]).Children.Select(Text).ToArray();
        Slot(Dict(tree["Object"]), types, "");
        // Native exports may store shared dependencies outside the Object tree.
        if (tree.Children.TryGetValue("Assets", out var bundled))
            foreach (var component in List(bundled).Children.Cast<DataTreeDictionary>())
            {
                string type = types[Convert.ToInt32(Value(component["Type"]))];
                ValidateNode(ResolveType(type), component["Data"], "Assets (" + type + ")");
            }
        if (errors.Count != 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));
    }

    private static void Package(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        using var recordStream = zip.GetEntry("R-Main.record")!.Open();
        using var record = JsonDocument.Parse(recordStream);
        string uri = record.RootElement.GetProperty("assetUri").GetString()!;
        using var source = zip.GetEntry("Assets/" + uri.Split('/').Last())!.Open();
        byte[] header = new byte[9]; source.ReadExactly(header);
        if (!header.SequenceEqual(new byte[] { 70, 114, 68, 84, 0, 0, 0, 0, 3 })) throw new InvalidDataException("Unsupported native object encoding.");
        using var brotli = new BrotliStream(source, CompressionMode.Decompress);
        using var bson = new MemoryStream(); brotli.CopyTo(bson); bson.Position = 0;
        var audit = new NativePackageAudit(zip.Entries.Select(entry => entry.FullName));
        audit.Validate(DataTreeConverter.FromRawBSON(bson));
        Console.WriteLine($"PASS native closure {Path.GetFileName(path)}: {audit.references} typed references and package asset URLs.");
    }

    public static int Run(string gameDirectory, string[] paths)
    {
        try { LoadEngine(gameDirectory); foreach (string path in paths) Package(path); return 0; }
        catch (Exception error) { Console.Error.WriteLine("FAIL native UI closure: " + error.Message); return 1; }
    }

    public static int Test(string gameDirectory)
    {
        try
        {
            LoadEngine(gameDirectory);
            string missing = Guid.NewGuid().ToString();
            var field = new DataTreeDictionary(); field.Add("Data", missing);
            var audit = new NativePackageAudit([]);
            audit.ValidateNode(ResolveType("[FrooxEngine]FrooxEngine.Sync<string>"), field, "label.Content");
            if (audit.errors.Count != 0) throw new Exception("GUID-looking text was incorrectly treated as a reference.");
            audit.ValidateNode(ResolveType("[FrooxEngine]FrooxEngine.AssetRef<[FrooxEngine]FrooxEngine.Sprite>"), field, "image.Sprite");
            if (audit.errors.Count != 1) throw new Exception("Missing sprite reference was accepted.");
            audit.errors.Clear(); audit.ids.Add(missing);
            audit.ValidateNode(ResolveType("[FrooxEngine]FrooxEngine.AssetRef<[FrooxEngine]FrooxEngine.Sprite>"), field, "image.Sprite");
            if (audit.errors.Count != 0) throw new Exception("Internal reference was rejected.");
            var nullField = new DataTreeDictionary(); nullField.Children["Data"] = new DataTreeValue((string)null!);
            audit.ValidateNode(ResolveType("[FrooxEngine]FrooxEngine.AssetRef<[FrooxEngine]FrooxEngine.Sprite>"), nullField, "image.Material");
            if (audit.errors.Count != 0) throw new Exception("Optional null reference was rejected.");
            var items = new DataTreeList(); items.Children.Add(field);
            var listField = new DataTreeDictionary(); listField.Children["Data"] = items;
            audit.ids.Clear();
            audit.ValidateNode(ResolveType("[FrooxEngine]FrooxEngine.SyncAssetList<[FrooxEngine]FrooxEngine.Font>"), listField, "font.FallbackFonts");
            if (audit.errors.Count != 1) throw new Exception("Missing list element reference was accepted.");
            audit.errors.Clear();
            audit.ValidateUri("packdb:///missing", "texture.URL");
            audit.ValidateUri("local:///machine-only", "font.URL");
            if (audit.errors.Count != 2) throw new Exception("Unbundled/local URLs were accepted.");
            Console.WriteLine("PASS native closure regression checks (literal GUID text, missing/internal typed refs, unbundled/local URLs).");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL native closure regression checks: " + error); return 1; }
    }
}
