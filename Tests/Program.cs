using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using UnityPackageImporter.Extractor;
using UnityPackageImporter.Models;

if (args.Length == 2 && args[0] == "--engine-api") return EngineApiCheck.Run(args[1]);
if (args.Length == 3 && args[0] == "--packages") return PackageAudit.Run(args[1], args[2]);
if (args.Length == 3 && args[0] == "--dump-type") return EngineApiCheck.DumpType(args[1], args[2]);

var tests = new List<(string Name, Action Run)>();
void Test(string name, Action run) => tests.Add((name, run));
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
void Throws<T>(Action run) where T : Exception
{
    try { run(); }
    catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
void InTemp(Action<string> run)
{
    var root = Path.Combine(Path.GetTempPath(), "unitypackage-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try { run(root); }
    finally { Directory.Delete(root, true); }
}
string Archive(string root, params (string Path, string Content)[] files)
{
    var file = Path.Combine(root, Guid.NewGuid().ToString("N") + ".unitypackage");
    using var output = File.Create(file);
    using var gzip = new GZipStream(output, CompressionMode.Compress);
    using var writer = new TarWriter(gzip, TarEntryFormat.Ustar);
    foreach (var item in files)
    {
        using var data = new MemoryStream(Encoding.UTF8.GetBytes(item.Content));
        writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, item.Path) { DataStream = data });
    }
    return file;
}
string Package(string root, string assetPath = "Assets/模型/file2.png") => Archive(root,
    ("guid/asset", "payload"), ("guid/asset.meta", "fileFormatVersion: 2\nguid: guid\n"),
    ("guid/pathname", assetPath + "\n\0"));

Test("Extract payload, Unicode names, and metadata", () => InTemp(root =>
{
    var files = UnityPackageExtractor.Unpack(Package(root), Path.Combine(root, "cache"));
    Check(files.Count == 2, "Metadata must always be extracted.");
    Check(File.ReadAllText(files.Single(p => p.EndsWith(".png"))) == "payload", "Wrong payload.");
}));
Test("Keep numeric extensions intact", () => InTemp(root =>
{
    var files = UnityPackageExtractor.Unpack(Package(root, "Assets/audio.mp3"), Path.Combine(root, "cache"));
    Check(files.Any(p => p.EndsWith(".mp3")), "Extension was truncated.");
}));
Test("Cache reuse returns the same files without extracting again", () => InTemp(root =>
{
    var package = Package(root);
    var cache = Path.Combine(root, "cache");
    var first = UnityPackageExtractor.Unpack(package, cache);
    File.Delete(package);
    Check(first.SequenceEqual(UnityPackageExtractor.Unpack(package, cache)), "Cache was not reused.");
}));
Test("Rebuild an incomplete legacy cache", () => InTemp(root =>
{
    var cache = Path.Combine(root, "cache");
    Directory.CreateDirectory(Path.Combine(cache, "Assets"));
    File.WriteAllText(Path.Combine(cache, "Assets", "partial"), "partial");
    var files = UnityPackageExtractor.Unpack(Package(root), cache);
    Check(files.Count == 2 && !File.Exists(Path.Combine(cache, "Assets", "partial")), "Reused an incomplete cache.");
}));
Test("Rebuild cache when metadata is missing", () => InTemp(root =>
{
    var package = Package(root);
    var cache = Path.Combine(root, "cache");
    var files = UnityPackageExtractor.Unpack(package, cache);
    File.Delete(files.Single(p => p.EndsWith(".meta")));
    Check(UnityPackageExtractor.Unpack(package, cache).All(File.Exists), "Missing metadata was not restored.");
}));
foreach (var path in new[] { "../escape", "/absolute", "C:/absolute", "guid/../../escape", "guid\\..\\..\\escape", "guid/asset:stream", "guid/NUL" })
{
    Test("Reject archive path " + path, () => InTemp(root =>
    {
        var package = Archive(root, (path, "bad"));
        var cache = Path.Combine(root, "cache");
        Throws<InvalidDataException>(() => UnityPackageExtractor.Unpack(package, cache));
        Check(!Directory.Exists(cache), "Published a failed cache.");
        Check(!Directory.GetDirectories(root).Any(), "Staging directory leaked.");
    }));
}
foreach (var path in new[] { "Assets/../../escape", "C:/escape", "/escape", "Assets/file:stream", "Assets/CON.txt" })
    Test("Reject asset pathname " + path, () => InTemp(root =>
        Throws<InvalidDataException>(() => UnityPackageExtractor.Unpack(Package(root, path), Path.Combine(root, "cache")))));
Test("Reject symbolic links", () => InTemp(root =>
{
    var package = Path.Combine(root, "link.unitypackage");
    using (var file = File.Create(package))
    using (var gzip = new GZipStream(file, CompressionMode.Compress))
    using (var writer = new TarWriter(gzip, TarEntryFormat.Ustar))
        writer.WriteEntry(new UstarTarEntry(TarEntryType.SymbolicLink, "guid") { LinkName = "../outside" });
    Throws<InvalidDataException>(() => UnityPackageExtractor.Unpack(package, Path.Combine(root, "cache")));
}));
Test("Reject duplicate asset destinations", () => InTemp(root =>
{
    var package = Archive(root, ("a/asset", "a"), ("a/pathname", "Assets/Body.mat"),
        ("b/asset", "b"), ("b/pathname", "Assets/body.mat"));
    Throws<InvalidDataException>(() => UnityPackageExtractor.Unpack(package, Path.Combine(root, "cache")));
}));
Test("Reject duplicate archive entries", () => InTemp(root =>
{
    var package = Archive(root, ("guid/asset", "first"), ("guid/asset", "second"));
    Throws<InvalidDataException>(() => UnityPackageExtractor.Unpack(package, Path.Combine(root, "cache")));
}));
Test("Failed extraction leaves no partial cache or staging files", () => InTemp(root =>
{
    var package = Archive(root, ("good/asset", "payload"), ("good/pathname", "Assets/good.txt"),
        ("bad/asset", "bad"), ("bad/pathname", "Assets/../../outside.txt"));
    Throws<InvalidDataException>(() => UnityPackageExtractor.Unpack(package, Path.Combine(root, "cache")));
    Check(!Directory.GetDirectories(root).Any(), "A failed extraction left partial files.");
    Check(!File.Exists(Path.Combine(root, "outside.txt")), "Escaped cache root.");
}));
Test("Corrupt input fails without replacing an existing incomplete cache", () => InTemp(root =>
{
    var package = Path.Combine(root, "bad.unitypackage");
    File.WriteAllText(package, "not gzip");
    var cache = Path.Combine(root, "cache");
    Directory.CreateDirectory(cache);
    var original = Path.Combine(cache, "original.txt");
    File.WriteAllText(original, "original");
    Throws<InvalidDataException>(() => UnityPackageExtractor.Unpack(package, cache));
    Check(File.ReadAllText(original) == "original", "Existing cache changed on failure.");
    Check(Directory.GetDirectories(root).Length == 1, "Staging directory leaked.");
}));
Test("Rebuild a cache containing a truncated asset", () => InTemp(root =>
{
    var package = Package(root);
    var cache = Path.Combine(root, "cache");
    var files = UnityPackageExtractor.Unpack(package, cache);
    var asset = files.Single(p => p.EndsWith(".png"));
    File.WriteAllText(asset, "x");
    UnityPackageExtractor.Unpack(package, cache);
    Check(File.ReadAllText(asset) == "payload", "Damaged cache was reused.");
}));
Test("Enforce entry count limit", () => InTemp(root =>
    Throws<InvalidDataException>(() => UnityPackageExtractor.Unpack(Package(root), Path.Combine(root, "cache"), maxEntries: 1))));
Test("Enforce decompressed byte limit", () => InTemp(root =>
    Throws<InvalidDataException>(() => UnityPackageExtractor.Unpack(Package(root), Path.Combine(root, "cache"), maxBytes: 512))));
Test("Honor cancellation without publishing a cache", () => InTemp(root =>
{
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    Throws<OperationCanceledException>(() => UnityPackageExtractor.Unpack(Package(root), Path.Combine(root, "cache"), cancellationToken: cancellation.Token));
    Check(!Directory.Exists(Path.Combine(root, "cache")), "Published canceled cache.");
}));
Test("Concurrent cache imports return complete files", () => InTemp(root =>
{
    var package = Package(root);
    var results = Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        UnityPackageExtractor.Unpack(package, Path.Combine(root, "cache"))))).GetAwaiter().GetResult();
    Check(results.All(files => files.Count == 2 && files.All(File.Exists)), "Concurrent extraction was incomplete.");
}));
Test("Async cache executes one import and shares completion", () =>
{
    var cache = new AsyncImportCache<int>();
    var completion = new TaskCompletionSource<int>();
    int calls = 0;
    var results = Enumerable.Range(0, 32).Select(_ => Task.Run(() => cache.GetOrAdd("guid", () =>
    {
        Interlocked.Increment(ref calls);
        return completion.Task;
    }))).ToArray();
    completion.SetResult(42);
    Check(Task.WhenAll(results).GetAwaiter().GetResult().All(value => value == 42) && calls == 1, "Import was duplicated.");
});
Test("Async cache separates asset GUIDs", () =>
{
    var cache = new AsyncImportCache<int>();
    Check(cache.GetOrAdd("a", () => Task.FromResult(1)).Result == 1 &&
          cache.GetOrAdd("b", () => Task.FromResult(2)).Result == 2, "Assets were aliased.");
});
Test("Async cache shares failures instead of hanging", () =>
{
    var cache = new AsyncImportCache<int>();
    var first = cache.GetOrAdd("guid", () => throw new InvalidDataException("bad asset"));
    var second = cache.GetOrAdd("guid", () => Task.FromResult(42));
    Check(ReferenceEquals(first, second), "Failure was not shared.");
    Throws<InvalidDataException>(() => second.GetAwaiter().GetResult());
});

Test("Build stable outfit install identity from Unity prefab", () => InTemp(root =>
{
    var prefab = Path.Combine(root, "Outfit.prefab");
    File.WriteAllText(prefab, "revision one");
    var identity = OutfitInstallIdentity.FromPrefab("AA-BB-CC", prefab);
    Check(identity.PrefabGuid == "aabbcc", "Prefab GUID was not normalized.");
    Check(identity.ContentHash.Length == 64, "Prefab content hash is not SHA-256.");
    Check(identity.Compare("aabbcc", identity.ContentHash.ToLowerInvariant()) == OutfitInstallMatch.Current,
        "The same prefab revision was not recognized.");
}));

Test("Distinguish outfit updates from unrelated prefabs", () => InTemp(root =>
{
    var prefab = Path.Combine(root, "Outfit.prefab");
    File.WriteAllText(prefab, "revision two");
    var identity = OutfitInstallIdentity.FromPrefab("outfit-guid", prefab);
    Check(identity.Compare("outfit-guid", new string('0', 64)) == OutfitInstallMatch.UpdateAvailable,
        "A changed prefab revision was not reported as an update.");
    Check(identity.Compare("another-guid", identity.ContentHash) == OutfitInstallMatch.None,
        "An unrelated prefab matched the installation record.");
}));

Test("Report material references omitted from a Unity package", () =>
{
    var summary = MaterialDependencyDiagnostics.Analyze(
        new[]
        {
            new[] { "included", "missing-a", "missing-a", "" },
            new[] { "included" },
            new[] { "missing-b" }
        },
        new[] { "included" });

    Check(summary.MissingAssetCount == 2, "Missing material assets were not deduplicated.");
    Check(summary.AffectedRendererCount == 2, "Affected renderer count is wrong.");
    Check(summary.MissingReferenceCount == 3, "Missing material reference count is wrong.");
    Check(summary.MissingAssetGuids.SequenceEqual(new[] { "missing-a", "missing-b" }),
        "Missing material GUIDs were not stable and sorted.");
});

Test("Ignore empty and available material references", () =>
{
    var summary = MaterialDependencyDiagnostics.Analyze(
        new[] { new[] { null!, "", "AVAILABLE" } },
        new[] { "available" });
    Check(!summary.HasMissingAssets && summary.AffectedRendererCount == 0,
        "Valid material references were reported as missing.");
});

Test("Build one GUID index across split Unity packages", () => InTemp(root =>
{
    var clothing = Path.Combine(root, "clothing");
    var materials = Path.Combine(root, "materials");
    Directory.CreateDirectory(clothing);
    Directory.CreateDirectory(materials);
    var prefab = Path.Combine(clothing, "Outfit.prefab");
    var material = Path.Combine(materials, "Blue.mat");
    File.WriteAllText(prefab, "Prefab");
    File.WriteAllText(prefab + ".meta", "fileFormatVersion: 2\nguid: prefab-guid\n");
    File.WriteAllText(material, "Material");
    File.WriteAllText(material + ".meta", "fileFormatVersion: 2\nguid: material-guid\n");

    var index = UnityPackageAssetIndex.Build(new[]
    {
        prefab, prefab + ".meta", material, material + ".meta"
    });

    Check(index.Assets["prefab-guid"] == prefab, "Clothing prefab was not indexed.");
    Check(index.Assets["material-guid"] == material, "Separate material package was not indexed.");
    Check(index.Prefabs.Count == 1 && index.Conflicts.Count == 0,
        "Split packages produced the wrong prefab index.");
}));

Test("Keep GUID conflicts deterministic across Unity packages", () => InTemp(root =>
{
    var first = Path.Combine(root, "First.mat");
    var second = Path.Combine(root, "Second.mat");
    File.WriteAllText(first, "first");
    File.WriteAllText(second, "second");
    File.WriteAllText(first + ".meta", "guid: shared-guid\n");
    File.WriteAllText(second + ".meta", "guid: shared-guid\n");

    var index = UnityPackageAssetIndex.Build(new[] { first + ".meta", second + ".meta" });
    Check(index.Assets["shared-guid"] == first, "A later package silently replaced the first GUID.");
    Check(index.Conflicts.Count == 1, "Conflicting package assets were not reported.");
}));

Test("Prefab bounds override creates null nested objects and dictionary entries", () =>
{
    var target = new OverrideFixture();
    Check(UnityPropertyPath.TrySet(target, "m_AABB.m_Center.x", "0.25", null, out var error), error);
    Check(target.m_AABB.m_Center["x"] == 0.25f, "Bounds override was lost.");
});
Test("Prefab vector override writes a boxed struct back to its owner", () =>
{
    var target = new OverrideFixture();
    Check(UnityPropertyPath.TrySet(target, "position.x", "3.5", null, out var error), error);
    Check(target.position.x == 3.5f, "Struct override was lost.");
});
Test("Prefab material array uses objectReference even with an empty scalar", () =>
{
    var target = new OverrideFixture();
    var reference = new TestReference { guid = "material-guid" };
    Check(UnityPropertyPath.TrySet(target, "m_Materials.Array.data[2]", "", reference, out var error), error);
    Check(target.m_Materials.Count == 3 && ReferenceEquals(target.m_Materials[2], reference), "Wrong reference or index.");
});
Test("Prefab array element properties are traversed", () =>
{
    var target = new OverrideFixture();
    Check(UnityPropertyPath.TrySet(target, "m_Materials.Array.data[1].guid", "new-guid", null, out var error), error);
    Check(target.m_Materials[1].guid == "new-guid", "Nested list override was not applied.");
});
Test("Prefab array size preserves values and supports shrinking", () =>
{
    var target = new OverrideFixture();
    Check(UnityPropertyPath.TrySet(target, "numbers.Array.size", "4", null, out var error), error);
    Check(target.numbers.SequenceEqual(new[] { 3, 4, 0, 0 }), "Array growth lost values.");
    Check(UnityPropertyPath.TrySet(target, "numbers.Array.size", "1", null, out error), error);
    Check(target.numbers.SequenceEqual(new[] { 3 }), "Array did not shrink.");
});
Test("Prefab invalid array value preserves the existing element", () =>
{
    var target = new OverrideFixture();
    Check(!UnityPropertyPath.TrySet(target, "numbers.Array.data[0]", "bad", null, out _), "Invalid value was accepted.");
    Check(target.numbers.SequenceEqual(new[] { 3, 4 }), "Failed override removed an element.");
    Check(!UnityPropertyPath.TrySet(target, "numbers.Array.data[999999999]", "1", null, out _), "Unbounded allocation allowed.");
});
Test("Prefab properties accept empty names and Unity boolean values", () =>
{
    var target = new OverrideFixture();
    Check(UnityPropertyPath.TrySet(target, "Name", "", null, out var error), error);
    Check(UnityPropertyPath.TrySet(target, "Enabled", "1", null, out error), error);
    Check(target.Name == "" && target.Enabled, "Property conversion was incorrect.");
});
Test("Prefab float parsing is independent of desktop locale", () =>
{
    var original = System.Globalization.CultureInfo.CurrentCulture;
    try
    {
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        var target = new OverrideFixture();
        Check(UnityPropertyPath.TrySet(target, "position.x", "1.25", null, out var error), error);
        Check(target.position.x == 1.25f, "Float used desktop locale.");
    }
    finally { System.Globalization.CultureInfo.CurrentCulture = original; }
});
Test("Unknown prefab properties are reported without throwing", () =>
{
    Check(!UnityPropertyPath.TrySet(new OverrideFixture(), "unknown.x", "1", null, out var error) &&
          !string.IsNullOrEmpty(error), "Missing property was not reported.");
});

Test("Blendshape defaults convert Unity percentages without clamping", () =>
{
    var actual = new float[5];
    BlendShapeDefaults.Apply(new[] { 0f, 50f, 100f, -25f, 150f }, actual.Length,
        (i, value) => actual[i] = value, warning => throw new Exception(warning));
    Check(actual.SequenceEqual(new[] { 0f, .5f, 1f, -.25f, 1.5f }), "Incorrect blendshape units.");
});
Test("Blendshape overrides reach renderer values and preserve untouched defaults", () =>
{
    var target = new OverrideFixture { m_BlendShapeWeights = new() { 20f, 30f, 40f } };
    Check(UnityPropertyPath.TrySet(target, "m_BlendShapeWeights.Array.data[1]", "75", null, out var error), error);
    var actual = new float[3];
    BlendShapeDefaults.Apply(target.m_BlendShapeWeights, actual.Length,
        (i, value) => actual[i] = value, warning => throw new Exception(warning));
    Check(actual.SequenceEqual(new[] { .2f, .75f, .4f }), "Sparse override lost defaults.");
});
Test("Blendshape invalid and surplus values do not overwrite renderer data", () =>
{
    var actual = new[] { .3f, .4f };
    var warnings = new List<string>();
    BlendShapeDefaults.Apply(new[] { float.NaN, 50f, 100f }, actual.Length,
        (i, value) => actual[i] = value, warnings.Add);
    Check(actual.SequenceEqual(new[] { .3f, .5f }) && warnings.Count == 2, "Unsafe weights were applied.");
    BlendShapeDefaults.Apply(null!, actual.Length, (_, _) => throw new Exception("Unexpected write"), warnings.Add);
});

int failed = 0;
Test("Named blendshape defaults survive removal and reordering", () =>
{
    var names = new[] { "Smile", "Empty", "HideSleeve" };
    var imported = new[] { "HideSleeve", "Smile" };
    var actual = new float[2];
    BlendShapeDefaults.ApplyNamed(new[] { 25f, 100f, 75f }, names,
        name => Array.IndexOf(imported, name), (i, value) => actual[i] = value,
        warning => throw new Exception(warning));
    Check(actual.SequenceEqual(new[] { .75f, .25f }), "Removed shape shifted the target weights.");
});
Test("Ambiguous original blendshape names do not write weights", () =>
{
    int warnings = 0;
    foreach (var names in new string[][] { null!, new[] { "Same", "Same" }, new[] { "OnlyOne" } })
        BlendShapeDefaults.ApplyNamed(new[] { 100f, 50f }, names, _ => 0,
            (_, _) => throw new Exception("Unsafe mapping wrote a weight"), _ => warnings++);
    Check(warnings == 3, "Missing diagnostic for unsafe mapping.");
});
Test("Sparse prefab override keeps source shape identity after stripping", () =>
{
    var target = new OverrideFixture { m_BlendShapeWeights = new() { 20f, 0f, 40f } };
    Check(UnityPropertyPath.TrySet(target, "m_BlendShapeWeights.Array.data[2]", "100", null, out var error), error);
    var names = new[] { "First", "Empty", "Last" };
    var imported = new[] { "First", "Last" };
    var actual = new float[2];
    BlendShapeDefaults.ApplyNamed(target.m_BlendShapeWeights, names, name => Array.IndexOf(imported, name),
        (i, value) => actual[i] = value, warning => throw new Exception(warning));
    Check(actual.SequenceEqual(new[] { .2f, 1f }), "Sparse override changed shape identity.");
});

Test("CalculateEffectiveScale handles meter avatars and centimeter accessories", () =>
{
    // Avatars authored in meters (UnitScaleFactor = 100)
    Check(Math.Abs(ModelScaleHelper.CalculateEffectiveScale(1f, true, true, 100f) - 1.0f) < 1e-6f, "Avatar scale should be 1.0");

    // Blender accessories / clothing exported with UnitScaleFactor = 1.0
    Check(Math.Abs(ModelScaleHelper.CalculateEffectiveScale(1f, true, true, 1.0f) - 0.01f) < 1e-6f, "Accessory scale should be 0.01");

    // Millimeter FBX
    Check(Math.Abs(ModelScaleHelper.CalculateEffectiveScale(1f, true, true, 0.1f) - 0.001f) < 1e-6f, "Millimeter FBX scale should be 0.001");

    // Custom globalScale
    Check(Math.Abs(ModelScaleHelper.CalculateEffectiveScale(2.5f, true, true, 1.0f) - 0.025f) < 1e-6f, "Custom globalScale should multiply correctly");

    // useFileScale disabled
    Check(Math.Abs(ModelScaleHelper.CalculateEffectiveScale(2.0f, true, false, 1.0f) - 2.0f) < 1e-6f, "Disabled useFileScale should return globalScale");

    // useFileUnits disabled (defaults to centimeters = 0.01)
    Check(Math.Abs(ModelScaleHelper.CalculateEffectiveScale(1.0f, false, true, 100f) - 0.01f) < 1e-6f, "Disabled useFileUnits should default to 0.01");
});

Test("GetFbxUnitScaleFactor parses binary and ASCII FBX formats", () => InTemp(root =>
{
    // 1. Binary FBX test
    var binPath = Path.Combine(root, "test_binary.fbx");
    using (var ms = new MemoryStream())
    {
        ms.Write(Encoding.ASCII.GetBytes("Kaydara FBX Binary  \0\x1a\0"));
        ms.Write(new byte[100]); // padding
        ms.Write(Encoding.ASCII.GetBytes("UnitScaleFactor"));
        ms.Write(new byte[10]);
        ms.WriteByte((byte)'D');
        ms.Write(BitConverter.GetBytes(1.0)); // 1.0 cm
        ms.Write(new byte[100]);
        File.WriteAllBytes(binPath, ms.ToArray());
    }
    float parsedBin = ModelScaleHelper.GetFbxUnitScaleFactor(binPath);
    Check(Math.Abs(parsedBin - 1.0f) < 1e-5f, $"Binary UnitScaleFactor should be 1.0, got {parsedBin}");

    // 2. ASCII FBX test
    var asciiPath = Path.Combine(root, "test_ascii.fbx");
    File.WriteAllText(asciiPath, @"
        Properties70: {
            P: ""UnitScaleFactor"", ""double"", ""Number"", """",100
        }");
    float parsedAscii = ModelScaleHelper.GetFbxUnitScaleFactor(asciiPath);
    Check(Math.Abs(parsedAscii - 100.0f) < 1e-5f, $"ASCII UnitScaleFactor should be 100, got {parsedAscii}");
}));

Test("Model scale factor and unit scaling handles meter avatars and centimeter accessories", () => InTemp(root =>
{
    var meterFbx = Path.Combine(root, "meter_avatar.fbx");
    using (var ms = new MemoryStream())
    {
        ms.Write(Encoding.ASCII.GetBytes("Kaydara FBX Binary  \0\x1a\0"));
        ms.Write(new byte[50]);
        ms.Write(Encoding.ASCII.GetBytes("UnitScaleFactor"));
        ms.Write(new byte[10]);
        ms.WriteByte((byte)'D');
        ms.Write(BitConverter.GetBytes(100.0)); // 100.0 cm = 1.0 m
        ms.Write(new byte[50]);
        File.WriteAllBytes(meterFbx, ms.ToArray());
    }
    float meterScale = ModelScaleHelper.GetFbxUnitScaleFactor(meterFbx);
    Check(Math.Abs(meterScale - 100.0f) < 1e-5f, $"Meter avatar UnitScaleFactor should be 100.0, got {meterScale}");
    float meterEffective = ModelScaleHelper.CalculateEffectiveScale(1f, true, true, meterScale);
    Check(Math.Abs(meterEffective - 1.0f) < 1e-5f, $"Meter avatar effective scale should be 1.0, got {meterEffective}");

    var cmFbx = Path.Combine(root, "cm_accessory.fbx");
    using (var ms = new MemoryStream())
    {
        ms.Write(Encoding.ASCII.GetBytes("Kaydara FBX Binary  \0\x1a\0"));
        ms.Write(new byte[50]);
        ms.Write(Encoding.ASCII.GetBytes("UnitScaleFactor"));
        ms.Write(new byte[10]);
        ms.WriteByte((byte)'D');
        ms.Write(BitConverter.GetBytes(1.0)); // 1.0 cm
        ms.Write(new byte[50]);
        File.WriteAllBytes(cmFbx, ms.ToArray());
    }
    float cmScale = ModelScaleHelper.GetFbxUnitScaleFactor(cmFbx);
    Check(Math.Abs(cmScale - 1.0f) < 1e-5f, $"Centimeter accessory UnitScaleFactor should be 1.0, got {cmScale}");
    float cmEffective = ModelScaleHelper.CalculateEffectiveScale(1f, true, true, cmScale);
    Check(Math.Abs(cmEffective - 0.01f) < 1e-5f, $"Centimeter accessory effective scale should be 0.01, got {cmEffective}");
}));

Test("GenerateShadowRampPng produces valid PNG header and chunks", () =>
{
    var baseCol = RampColor.White;
    var sh1 = new RampColor(0.6f, 0.6f, 0.7f, 1f);
    var sh2 = new RampColor(0.3f, 0.3f, 0.4f, 1f);

    byte[] png = RampGenerator.GenerateShadowRampPng(baseCol, sh1, 0.5f, 0.1f, sh2, 0.25f, 0.1f, 256, 4);
    Check(png.Length > 50, "PNG output too small.");

    // Check PNG signature
    byte[] expectedSig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    for (int i = 0; i < 8; i++)
    {
        Check(png[i] == expectedSig[i], $"PNG signature mismatch at byte {i}");
    }

    // Check IHDR, IDAT, IEND chunk presence
    string ascii = Encoding.ASCII.GetString(png);
    Check(ascii.Contains("IHDR"), "Missing IHDR chunk");
    Check(ascii.Contains("IDAT"), "Missing IDAT chunk");
    Check(ascii.Contains("IEND"), "Missing IEND chunk");
});

Test("GenerateShadowRampPng gradient endpoints match deep shadow and base colors", () =>
{
    var baseCol = new RampColor(1.0f, 0.9f, 0.8f, 1f);
    var sh1 = new RampColor(0.5f, 0.4f, 0.3f, 1f);
    var sh2 = new RampColor(0.2f, 0.1f, 0.05f, 1f);

    // Hard borders (blur = 0) to test exact endpoints
    byte[] png = RampGenerator.GenerateShadowRampPng(baseCol, sh1, 0.6f, 0.0f, sh2, 0.3f, 0.0f, 256, 1);
    Check(png.Length > 0, "Generated PNG must not be empty");

    // Decompress IDAT to inspect raw pixel scanline
    // IDAT starts at offset 33 (8 sig + 4 len + 4 IHDR + 13 data + 4 crc)
    // Find IDAT chunk
    int idatIdx = -1;
    for (int i = 0; i < png.Length - 4; i++)
    {
        if (png[i] == 'I' && png[i + 1] == 'D' && png[i + 2] == 'A' && png[i + 3] == 'T')
        {
            idatIdx = i;
            break;
        }
    }
    Check(idatIdx >= 0, "Could not find IDAT chunk");
    int idatLen = (png[idatIdx - 4] << 24) | (png[idatIdx - 3] << 16) | (png[idatIdx - 2] << 8) | png[idatIdx - 1];

    byte[] decompressed;
    using (var ms = new MemoryStream(png, idatIdx + 4, idatLen))
    using (var zlib = new ZLibStream(ms, CompressionMode.Decompress))
    using (var outMs = new MemoryStream())
    {
        zlib.CopyTo(outMs);
        decompressed = outMs.ToArray();
    }

    // 1 row: 1 filter byte + 256 * 4 RGBA bytes = 1025 bytes
    Check(decompressed.Length == 1025, $"Expected 1025 decompressed bytes, got {decompressed.Length}");
    Check(decompressed[0] == 0, "Filter byte must be 0 (None)");

    // Pixel at x=0 (u=0): deep shadow
    int r0 = decompressed[1];
    int g0 = decompressed[2];
    int b0 = decompressed[3];
    int a0 = decompressed[4];
    Check(Math.Abs(r0 - (int)(sh2.R * 255f)) <= 1, $"Deep shadow R mismatch: {r0} vs {sh2.R * 255f}");
    Check(Math.Abs(g0 - (int)(sh2.G * 255f)) <= 1, $"Deep shadow G mismatch: {g0} vs {sh2.G * 255f}");
    Check(a0 == 255, "Alpha must be 255");

    // Pixel at x=255 (u=1.0): base color
    int rEnd = decompressed[1 + 255 * 4];
    int gEnd = decompressed[2 + 255 * 4];
    int bEnd = decompressed[3 + 255 * 4];
    Check(Math.Abs(rEnd - (int)(baseCol.R * 255f)) <= 1, $"Base lit R mismatch: {rEnd} vs {baseCol.R * 255f}");
    Check(Math.Abs(gEnd - (int)(baseCol.G * 255f)) <= 1, $"Base lit G mismatch: {gEnd} vs {baseCol.G * 255f}");
});

Test("RampGenerator hash determinism and file caching", () => InTemp(root =>
{
    var baseCol = RampColor.White;
    var sh1 = new RampColor(0.7f, 0.7f, 0.7f, 1f);
    var sh2 = new RampColor(0.4f, 0.4f, 0.4f, 1f);

    string hash1 = RampGenerator.GetRampHash(baseCol, sh1, 0.5f, 0.1f, sh2, 0.25f, 0.1f);
    string hash2 = RampGenerator.GetRampHash(baseCol, sh1, 0.5f, 0.1f, sh2, 0.25f, 0.1f);
    Check(hash1 == hash2, "Identical inputs must yield identical hash");

    string hashDiff = RampGenerator.GetRampHash(baseCol, new RampColor(0.8f, 0.7f, 0.7f, 1f), 0.5f, 0.1f, sh2, 0.25f, 0.1f);
    Check(hash1 != hashDiff, "Different inputs must yield different hash");

    string file1 = RampGenerator.GetOrCreateRampFile(root, baseCol, sh1, 0.5f, 0.1f, sh2, 0.25f, 0.1f);
    Check(File.Exists(file1), "Ramp file must be written to disk");

    var writeTime1 = File.GetLastWriteTimeUtc(file1);
    string file2 = RampGenerator.GetOrCreateRampFile(root, baseCol, sh1, 0.5f, 0.1f, sh2, 0.25f, 0.1f);
    Check(file1 == file2, "GetOrCreateRampFile must return same cached path");
    Check(File.GetLastWriteTimeUtc(file2) == writeTime1, "Cached file must not be rewritten");
}));

Test("LilToon multilayer makeup, alpha mask, and shadow mask detection in material data", () => InTemp(root =>
{
    var faceMat = Path.Combine(root, "face.mat");
    File.WriteAllText(faceMat, @"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!21 &2100000
Material:
  m_Shader: {fileID: 4800000, guid: efa77a80ca0344749b4f19fdd5891cbe, type: 3}
  m_SavedProperties:
    m_TexEnvs:
    - _Main2ndTex:
        m_Texture: {fileID: 2800000, guid: 536cf5586fab2e0489a3a4033a9c3e04, type: 3}
    - _AlphaMask:
        m_Texture: {fileID: 2800000, guid: c519efbbc07359845aed03c63121945e, type: 3}
    - _ShadowBorderMask:
        m_Texture: {fileID: 2800000, guid: 11111111111111111111111111111111, type: 3}
    m_Floats:
    - _AlphaMaskMode: 1
    m_Colors:
    - _BacklightColor: {r: 1, g: 0.8, b: 0.7, a: 1}
");
    var lines = File.ReadAllLines(faceMat);
    bool hasShaderGuid = lines.Any(l => l.Contains("efa77a80ca0344749b4f19fdd5891cbe"));
    Check(hasShaderGuid, "Material must use lilToon shader efa77a80ca0344749b4f19fdd5891cbe");

    bool hasMain2nd = lines.Any(l => l.Contains("_Main2ndTex:"));
    Check(hasMain2nd, "Material must define _Main2ndTex for makeup");

    bool hasMakeupGuid = lines.Any(l => l.Contains("536cf5586fab2e0489a3a4033a9c3e04"));
    Check(hasMakeupGuid, "Material must reference makeup texture GUID 536cf5586fab2e0489a3a4033a9c3e04");

    bool hasAlphaMaskMode = lines.Any(l => l.Contains("_AlphaMaskMode: 1"));
    Check(hasAlphaMaskMode, "Material must enable _AlphaMaskMode 1");

    bool hasAlphaMaskGuid = lines.Any(l => l.Contains("c519efbbc07359845aed03c63121945e"));
    Check(hasAlphaMaskGuid, "Material must reference alpha mask GUID c519efbbc07359845aed03c63121945e");

    bool hasShadowMask = lines.Any(l => l.Contains("_ShadowBorderMask:"));
    Check(hasShadowMask, "Material must define _ShadowBorderMask");

    bool hasBacklight = lines.Any(l => l.Contains("_BacklightColor:"));
    Check(hasBacklight, "Material must define _BacklightColor for subsurface scattering");
}));

Test("Parse VRCExpressionParameters YAML", () => InTemp(root =>
{
    var paramFile = Path.Combine(root, "Avatar_ExpressionParameters.asset");
    File.WriteAllText(paramFile, @"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_Name: Avatar_ExpressionParameters
  parameters:
  - name: Costume
    valueType: 0
    saved: 1
    defaultValue: 0
  - name: Stocking
    valueType: 2
    saved: 1
    defaultValue: 1
  - name: Shoes
    valueType: 2
    saved: 1
    defaultValue: 0
");
    var parameters = ExpressionMenuParser.ParseParameters(paramFile);
    Check(parameters.Count == 3, $"Expected 3 parameters, got {parameters.Count}");
    Check(parameters.ContainsKey("Costume"), "Must contain Costume parameter");
    Check(parameters["Costume"].ValueType == 0, "Costume must be Int (0)");
    Check(parameters["Costume"].DefaultValue == 0f, "Costume default must be 0");
    Check(parameters["Stocking"].ValueType == 2, "Stocking must be Bool (2)");
    Check(parameters["Stocking"].DefaultValue == 1f, "Stocking default must be 1");
    Check(parameters["Shoes"].DefaultValue == 0f, "Shoes default must be 0");
}));

Test("Index multiple VRChat avatar descriptors without cross-wiring assets", () =>
{
    string yaml = @"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1 &100
GameObject:
  m_Name: Avatar A
--- !u!114 &101
MonoBehaviour:
  m_GameObject: {fileID: 100}
  m_Script: {fileID: 542108242, guid: 67cc4cb7839cd3741b63733d5adf0442, type: 3}
  expressionsMenu: {fileID: 11400000, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 2}
  expressionParameters: {fileID: 11400000, guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb, type: 2}
  baseAnimationLayers:
  - isEnabled: 0
    type: 0
    animatorController: {fileID: 9100000, guid: 11111111111111111111111111111111, type: 2}
  - isEnabled: 1
    type: 5
    animatorController: {fileID: 9100000, guid: cccccccccccccccccccccccccccccccc,
      type: 2}
--- !u!1 &200 stripped
GameObject:
  m_Name: Avatar B
--- !u!114 &201
MonoBehaviour:
  m_GameObject: {fileID: 200}
  m_Script: {fileID: 542108242, guid: 67cc4cb7839cd3741b63733d5adf0442, type: 3}
  expressionsMenu: {fileID: 11400000, guid: dddddddddddddddddddddddddddddddd, type: 2}
  expressionParameters: {fileID: 11400000, guid: eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee, type: 2}
  baseAnimationLayers:
  - isEnabled: 1
    type: 5
    animatorController: {fileID: 9100000, guid: ffffffffffffffffffffffffffffffff, type: 2}
";

    var manifest = AvatarPackageIndex.ParseText(yaml, "multi-avatar.prefab");
    Check(manifest.Avatars.Count == 2, $"Expected two descriptors, got {manifest.Avatars.Count}");
    Check(manifest.IsAvatarPrefab, "Descriptor-bearing prefab must receive humanoid setup");
    Check(manifest.ShouldSetUpHumanoid, "Descriptor-bearing prefab must request humanoid setup");
    Check(manifest.Avatars[0].GameObjectName == "Avatar A", "First descriptor must remain attached to Avatar A");
    Check(manifest.Avatars[0].ExpressionsMenu?.Guid == "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "Avatar A menu was cross-wired");
    Check(manifest.Avatars[0].ExpressionParameters?.Guid == "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "Avatar A parameters were cross-wired");
    Check(manifest.Avatars[0].FxController?.Guid == "cccccccccccccccccccccccccccccccc", "Avatar A FX controller was not selected by layer type");
    Check(manifest.Avatars[1].GameObjectName == "Avatar B", "Stripped GameObject header must remain indexable");
    Check(manifest.Avatars[1].ExpressionsMenu?.Guid == "dddddddddddddddddddddddddddddddd", "Avatar B menu was cross-wired");
    Check(manifest.Avatars[1].FxController?.Guid == "ffffffffffffffffffffffffffffffff", "Avatar B FX controller was cross-wired");

    string normalized = AvatarPackageIndex.NormalizeDocumentHeaders("--- !u!1 &9 stripped\r\nGameObject:\r\n  m_Name: Test\r\n");
    Check(normalized == "--- !u!1 &9\r\nGameObject:\r\n  m_Name: Test\r\n", "Stripped normalization must preserve YAML line boundaries");
});

Test("Detect supported Modular Avatar component metadata", () =>
{
    string yaml = @"--- !u!1 &300
GameObject:
  m_Name: Outfit Armature
--- !u!114 &301
MonoBehaviour:
  m_GameObject: {fileID: 300}
  m_Script: {fileID: 11500000, guid: 2df373bf91cf30b4bbd495e11cb1a2ec, type: 3}
  mergeTarget:
    referencePath: Armature
    targetObject: {fileID: 42}
  prefix: Outfit_
  suffix: _MA
  LockMode: 2
  mangleNames: 1
--- !u!114 &302
MonoBehaviour:
  m_GameObject: {fileID: 300}
  m_Script: {fileID: 11500000, guid: 6fd7cab7d93b403280f2f9da978d8a4f, type: 3}
  Bindings:
  - ReferenceMesh:
      referencePath: Body
    Blendshape: Big
  - ReferenceMesh:
      referencePath: Face
    Blendshape: Smile
";

    var manifest = AvatarPackageIndex.ParseText(yaml, "outfit.prefab");
    Check(manifest.IsModularAvatarAttachment, "Outfit-only prefab must be classified as an MA attachment");
    Check(!manifest.IsAvatarPrefab, "MA attachment must not receive standalone humanoid setup");
    Check(!manifest.ShouldSetUpHumanoid, "Merge Armature attachment must skip standalone humanoid setup");
    Check(manifest.ModularAvatarComponents.Count == 2, "Expected Merge Armature and Blendshape Sync");
    var merge = manifest.ModularAvatarComponents.Single(component => component.Kind == ModularAvatarComponentKind.MergeArmature);
    Check(merge.GameObjectName == "Outfit Armature", "Merge Armature GameObject name was lost");
    Check(merge.MergeTargetPath == "Armature", "Merge target path was not parsed");
    Check(merge.MergeTargetObjectFileId == 42, "Merge target object was not parsed");
    Check(merge.Prefix == "Outfit_" && merge.Suffix == "_MA", "Prefix or suffix was not parsed");
    Check(merge.LockMode == 2 && merge.MangleNames, "Merge Armature lock settings were not parsed");
    var sync = manifest.ModularAvatarComponents.Single(component => component.Kind == ModularAvatarComponentKind.BlendshapeSync);
    Check(sync.BindingCount == 2, "Blendshape Sync binding count was not parsed");
});

Test("Do not classify Bone Proxy avatar variants as clothing", () =>
{
    string yaml = @"--- !u!1 &6886233627380799264 stripped
GameObject:
  m_Name: TEST08_Uruki
--- !u!114 &3776508462999175752
MonoBehaviour:
  m_GameObject: {fileID: 6886233627380799264}
  m_Script: {fileID: 11500000, guid: 42581d8044b64899834d3d515ab3a144, type: 3}
  boneReference: 10
  subPath: Face/Accessory
  attachmentMode: 2
  matchScale: 1
";

    var manifest = AvatarPackageIndex.ParseText(yaml, "TEST08_Uruki.prefab");
    Check(!manifest.IsModularAvatarAttachment, "Bone Proxy alone must not classify a prefab as clothing");
    Check(manifest.ShouldSetUpHumanoid, "Bone Proxy avatar variant must retain normal humanoid setup");
    var proxy = manifest.ModularAvatarComponents.Single();
    Check(proxy.Kind == ModularAvatarComponentKind.BoneProxy, "Expected a Bone Proxy component");
    Check(proxy.BoneReference == 10, "Bone Proxy humanoid target was not parsed");
    Check(proxy.BoneProxySubPath == "Face/Accessory", "Bone Proxy subpath was not parsed");
    Check(proxy.BoneProxyAttachmentMode == 2, "Bone Proxy attachment mode was not parsed");
    Check(proxy.BoneProxyMatchScale, "Bone Proxy match-scale option was not parsed");
});

Test("Plan Modular Avatar armature merges by exact hierarchy path", () =>
{
    var avatar = new MergeHierarchyNode
    {
        Id = "avatar-armature",
        Name = "Armature",
        Children = new[]
        {
            new MergeHierarchyNode
            {
                Id = "avatar-hips",
                Name = "Hips",
                Children = new[]
                {
                    new MergeHierarchyNode
                    {
                        Id = "avatar-spine",
                        Name = "Spine",
                        Children = new[]
                        {
                            new MergeHierarchyNode { Id = "avatar-chest", Name = "Chest" }
                        }
                    }
                }
            }
        }
    };

    var outfit = new MergeHierarchyNode
    {
        Id = "outfit-armature",
        Name = "Armature.Outfit",
        Children = new[]
        {
            new MergeHierarchyNode
            {
                Id = "outfit-hips",
                Name = "Outfit_Hips",
                Children = new[]
                {
                    new MergeHierarchyNode
                    {
                        Id = "outfit-spine",
                        Name = "Outfit_Spine",
                        Children = new[]
                        {
                            new MergeHierarchyNode { Id = "outfit-chest", Name = "Outfit_Chest" },
                            new MergeHierarchyNode { Id = "skirt-root", Name = "SkirtRoot" }
                        }
                    }
                }
            }
        }
    };

    var plan = ModularAvatarMergePlanner.CreatePlan(outfit, avatar, "Outfit_", "");
    Check(plan.CanApply, "A compatible armature plan must be applicable");
    Check(plan.Mappings.Count == 4, $"Expected root plus three bone mappings, got {plan.Mappings.Count}");
    Check(plan.Mappings.Any(mapping => mapping.SourceId == "outfit-chest" && mapping.TargetPath == "Armature/Hips/Spine/Chest"), "Chest must map through the exact parent chain");
    Check(plan.RetainedRoots.Count == 1 && plan.RetainedRoots[0].SourceId == "skirt-root", "Unique outfit bones must be retained");
    Check(plan.RetainedRoots[0].TargetParentId == "avatar-spine", "Unique bone must attach below its nearest mapped parent");
});

Test("Keep nested Merge Armatures separate in the merge plan", () =>
{
    var avatar = new MergeHierarchyNode
    {
        Id = "avatar-root",
        Name = "Armature",
        Children = new[] { new MergeHierarchyNode { Id = "avatar-hips", Name = "Hips" } }
    };
    var outfit = new MergeHierarchyNode
    {
        Id = "outfit-root",
        Name = "Armature.Outfit",
        Children = new[]
        {
            new MergeHierarchyNode { Id = "nested", Name = "Hips", StartsNestedMerge = true }
        }
    };

    var plan = ModularAvatarMergePlanner.CreatePlan(outfit, avatar);
    Check(plan.Mappings.Count == 1, "Nested merge root must not be consumed by its parent merge");
    Check(plan.RetainedRoots.Single().Reason.Contains("separately"), "Nested merge must be explicitly scheduled separately");
});

Test("Reject ambiguous Modular Avatar target bones", () =>
{
    var avatar = new MergeHierarchyNode
    {
        Id = "avatar-root",
        Name = "Armature",
        Children = new[]
        {
            new MergeHierarchyNode { Id = "hips-a", Name = "Hips" },
            new MergeHierarchyNode { Id = "hips-b", Name = "Hips" }
        }
    };
    var outfit = new MergeHierarchyNode
    {
        Id = "outfit-root",
        Name = "Armature.Outfit",
        Children = new[] { new MergeHierarchyNode { Id = "outfit-hips", Name = "Hips" } }
    };

    var plan = ModularAvatarMergePlanner.CreatePlan(outfit, avatar);
    Check(!plan.CanApply && plan.Conflicts.Count == 1, "Ambiguous direct target bones must block automatic installation");
    Check(plan.Mappings.Count == 1, "Ambiguous bones must not be mapped arbitrarily");
});

Test("Parse VRCExpressionsMenu and submenus YAML", () => InTemp(root =>
{
    var rootMenuFile = Path.Combine(root, "Root_Menu.asset");
    var subMenuFile = Path.Combine(root, "Clothing_Menu.asset");

    File.WriteAllText(rootMenuFile, @"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_Name: Root_Menu
  controls:
  - name: Clothing
    icon: {fileID: 2800000, guid: 11111111111111111111111111111111, type: 3}
    type: 103
    parameter:
      name:
    value: 1
    subMenu: {fileID: 11400000, guid: 22222222222222222222222222222222, type: 2}
");

    File.WriteAllText(subMenuFile, @"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_Name: Clothing_Menu
  controls:
  - name: Jacket
    icon: {fileID: 2800000, guid: 33333333333333333333333333333333, type: 3}
    type: 102
    parameter:
      name: Jacket_Toggle
    value: 1
  - name: Stocking
    type: 102
    parameter:
      name: Stocking
    value: 1
");

    var assetIdDict = new Dictionary<string, string>
    {
        { "rootguid000000000000000000000000", rootMenuFile },
        { "22222222222222222222222222222222", subMenuFile }
    };

    var (rootMenu, _) = ExpressionMenuParser.DiscoverMenuHierarchy(new[] { rootMenuFile, subMenuFile }, assetIdDict);
    Check(rootMenu != null, "Must discover root menu");
    Check(rootMenu.Controls.Count == 1, "Root menu must have 1 control");
    Check(rootMenu.Controls[0].Name == "Clothing", "Control name must be Clothing");
    Check(rootMenu.Controls[0].IsSubMenu, "Clothing must be a submenu");
    Check(rootMenu.Controls[0].SubMenu != null, "Submenu must be linked");
    Check(rootMenu.Controls[0].SubMenu.Controls.Count == 2, "Submenu must contain 2 items");
    Check(rootMenu.Controls[0].SubMenu.Controls[0].Name == "Jacket", "Item 1 must be Jacket");
    Check(rootMenu.Controls[0].SubMenu.Controls[0].ParameterName == "Jacket_Toggle", "Jacket parameter must match");
    Check(rootMenu.Controls[0].SubMenu.Controls[0].IsToggle, "Jacket must be a toggle");
}));

Test("Load only the menu and parameters referenced by one avatar", () => InTemp(root =>
{
    var wrongMenu = Path.Combine(root, "Wrong.asset");
    var selectedMenu = Path.Combine(root, "Selected.asset");
    var selectedSubMenu = Path.Combine(root, "SelectedSub.asset");
    var wrongParameters = Path.Combine(root, "WrongParameters.asset");
    var selectedParameters = Path.Combine(root, "SelectedParameters.asset");

    File.WriteAllText(wrongMenu, "MonoBehaviour:\n  m_Name: Wrong\n  controls:\n  - name: Wrong Toggle\n    type: 102\n    parameter:\n      name: Wrong\n");
    File.WriteAllText(selectedMenu, "MonoBehaviour:\n  m_Name: Selected\n  controls:\n  - name: Clothes\n    type: 103\n    parameter:\n      name: \n    subMenu: {fileID: 11400000, guid: 33333333333333333333333333333333, type: 2}\n");
    File.WriteAllText(selectedSubMenu, "MonoBehaviour:\n  m_Name: SelectedSub\n  controls:\n  - name: Jacket\n    type: 102\n    parameter:\n      name: Jacket\n");
    File.WriteAllText(wrongParameters, "MonoBehaviour:\n  parameters:\n  - name: Wrong\n    valueType: 2\n");
    File.WriteAllText(selectedParameters, "MonoBehaviour:\n  parameters:\n  - name: Jacket\n    valueType: 2\n    defaultValue: 1\n");

    var assets = new Dictionary<string, string>
    {
        ["11111111111111111111111111111111"] = wrongMenu,
        ["22222222222222222222222222222222"] = selectedMenu,
        ["33333333333333333333333333333333"] = selectedSubMenu,
        ["44444444444444444444444444444444"] = wrongParameters,
        ["55555555555555555555555555555555"] = selectedParameters
    };

    var (menu, parameters) = ExpressionMenuParser.LoadMenuHierarchy(
        "22222222222222222222222222222222",
        "55555555555555555555555555555555",
        assets);

    Check(menu?.Name == "Selected", "The descriptor's exact root menu was not selected");
    Check(menu.Controls.Single().SubMenu?.Name == "SelectedSub", "The referenced submenu was not linked");
    Check(parameters.Count == 1 && parameters.ContainsKey("Jacket"), "The descriptor's exact parameter asset was not selected");
    Check(!parameters.ContainsKey("Wrong"), "Parameters leaked from another avatar");
}));

Test("Parse AnimationClip m_IsActive curves for avatar state", () => InTemp(root =>
{
    var animFile = Path.Combine(root, "Jacket_OFF.anim");
    File.WriteAllText(animFile, @"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!74 &7400000
AnimationClip:
  m_Name: Jacket_OFF
  m_FloatCurves:
  - curve:
      serializedVersion: 2
      m_Curve:
      - serializedVersion: 3
        time: 0
        value: 0
    attribute: m_IsActive
    path: Avatar_Jacket_Mesh
    classID: 1
  - curve:
      serializedVersion: 2
      m_Curve:
      - serializedVersion: 3
        time: 0
        value: 100
    attribute: blendShape.Jacket_Shrink
    path: Body
    classID: 137
");
    var clip = AvatarStateReconstructor.ParseAnimationClip(animFile);
    Check(clip != null, "Clip must parse");
    Check(clip.Name == "Jacket_OFF", "Clip name must be Jacket_OFF");
    Check(clip.Curves.Count == 2, $"Expected 2 curves, got {clip.Curves.Count}");
    Check(clip.Curves[0].Path == "Avatar_Jacket_Mesh", "Curve 0 path must be Avatar_Jacket_Mesh");
    Check(clip.Curves[0].Attribute == "m_IsActive", "Curve 0 attribute must be m_IsActive");
    Check(clip.Curves[0].Value == 0f, "Curve 0 value must be 0");
    Check(clip.Curves[1].Attribute == "blendShape.Jacket_Shrink", "Curve 1 attribute must be blendShape");
}));

Test("ResolveSlotNamesForControl direct and synonym matching", () =>
{
    var urukiCandidateSlots = new List<string>
    {
        "Hair_Ahoge", "Hair_Back", "Hair_Base", "Hair_Front", "Hair_Side", "Hair_SideTails",
        "BodyVag", "BodyLP", "Body_ChinChinLP", "Face_Main", "BodyTail",
        "C_tights", "C_arm_belt", "C_underwear_top", "C_pouch", "C_shoes",
        "C_hairpin", "C_skirt", "C_earring", "C_warmers_bottom", "C_nipless",
        "C_bat", "C_underwear_bottom", "C_waist_Strap", "Face_Bandaid", "C_top",
        "C_warmers_top", "C_top_Sleeve"
    };

    var clips = new Dictionary<string, AnimClipData>();

    // Test direct normalized matches
    var skirtSlots = AvatarStateReconstructor.ResolveSlotNamesForControl("Skirt", "C_7", clips, urukiCandidateSlots);
    Check(skirtSlots.Contains("C_skirt"), "Skirt must resolve to C_skirt");

    var tailSlots = AvatarStateReconstructor.ResolveSlotNamesForControl("Tail", "C_1", clips, urukiCandidateSlots);
    Check(tailSlots.Contains("BodyTail"), "Tail must resolve to BodyTail");

    var armBeltSlots = AvatarStateReconstructor.ResolveSlotNamesForControl("Arm Belt", "C_2", clips, urukiCandidateSlots);
    Check(armBeltSlots.Contains("C_arm_belt"), "Arm Belt must resolve to C_arm_belt");

    var pouchSlots = AvatarStateReconstructor.ResolveSlotNamesForControl("Pouch", "C_3", clips, urukiCandidateSlots);
    Check(pouchSlots.Contains("C_pouch"), "Pouch must resolve to C_pouch");

    // Test token and synonym cluster matches
    var armWarmerSlots = AvatarStateReconstructor.ResolveSlotNamesForControl("Arm Warmer", "C_15", clips, urukiCandidateSlots);
    Check(armWarmerSlots.Contains("C_warmers_top"), "Arm Warmer must resolve to C_warmers_top");
    Check(!armWarmerSlots.Contains("C_warmers_bottom"), "Arm Warmer must not resolve to C_warmers_bottom");

    var legWarmerSlots = AvatarStateReconstructor.ResolveSlotNamesForControl("Leg Warmer", "C_16", clips, urukiCandidateSlots);
    Check(legWarmerSlots.Contains("C_warmers_bottom"), "Leg Warmer must resolve to C_warmers_bottom");
    Check(!legWarmerSlots.Contains("C_warmers_top"), "Leg Warmer must not resolve to C_warmers_top");

    var chestBeltSlots = AvatarStateReconstructor.ResolveSlotNamesForControl("Chest Belt", "C_17", clips, urukiCandidateSlots);
    Check(chestBeltSlots.Contains("C_waist_Strap"), "Chest Belt must resolve to C_waist_Strap");
});

Test("ResolveSlotNamesForControl fuzzy animation clip matching", () =>
{
    var candidateSlots = new List<string> { "C_warmers_top", "C_warmers_bottom", "Body" };
    var clip = new AnimClipData
    {
        Name = "C_Warmers_arm",
        Curves = new List<AnimToggleCurve>
        {
            new AnimToggleCurve { Path = "RootNode/C_warmers_top", Attribute = "m_IsActive", Value = 0f }
        }
    };
    var clips = new Dictionary<string, AnimClipData> { { clip.Name, clip } };

    var resolved = AvatarStateReconstructor.ResolveSlotNamesForControl("Arm Warmer", "C_15", clips, candidateSlots);
    Check(resolved.Count == 1 && resolved[0] == "C_warmers_top", "Arm Warmer must resolve to C_warmers_top via fuzzy clip match");
});

foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + test.Name + ": " + error); }
}
Console.WriteLine($"{tests.Count - failed}/{tests.Count} passed");
return failed == 0 ? 0 : 1;
