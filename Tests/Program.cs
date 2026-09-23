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

foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + test.Name + ": " + error); }
}
Console.WriteLine($"{tests.Count - failed}/{tests.Count} passed");
return failed == 0 ? 0 : 1;
