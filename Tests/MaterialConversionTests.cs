using System.Buffers.Binary;
using System.IO.Compression;
using UnityPackageImporter.Models;

internal static class MaterialConversionTests
{
    public static void Register(Action<string, Action> test)
    {
        test("Material parser reads Unity YAML, integer switches, reordered UVs and null textures", () =>
        {
            var data = UnityMaterialData.Parse("""
                %YAML 1.1
                %TAG !u! tag:unity3d.com,2011:
                --- !u!21 &2100000
                Material:
                  m_Name: "Face transparent cutout"
                  m_Shader: {fileID: 4800000, guid: shader, type: 3}
                  stringTagMap: {RenderType: Opaque}
                  m_CustomRenderQueue: 2450
                  m_ShaderKeywords: "POI_OLD_KEY OTHER"
                  m_ValidKeywords: [POI_NEW_KEY]
                  m_SavedProperties:
                    m_Ints:
                    - _UseBumpMap: 1
                    m_TexEnvs:
                    - _MainTex:
                        m_Offset: {y: -0.25, x: 0.125}
                        m_Texture: {type: 3, guid: base, fileID: 2800000}
                        m_Scale: {y: 3, x: 2}
                    - _MatCapBlendMask:
                        m_Texture: {fileID: 0}
                    m_Colors:
                    - _Color: {a: 0.5, b: 0.3, g: 0.2, r: 0.1}
                    - _MainTexHSVG: {r: 0, g: 1, b: 1, a: 1}
                    m_Floats:
                    - _Cutoff: 0.5
                """);
            Check(data.Enabled("_UseBumpMap"), "Integer feature switch lost.");
            Check(data.ShaderGuid == "shader" && data.RenderQueue == 2450, "Material header lost.");
            Check(data.Keywords.SetEquals(new[] { "POI_OLD_KEY", "POI_NEW_KEY", "OTHER" }), "Legacy/modern shader keywords lost.");
            Check(data.Textures["_MainTex"] == ("base", 2f, 3f, 0.125f, -0.25f), "UV parsing depends on field order.");
            Check(data.Texture("_MatCapBlendMask") == null, "Empty mask must not disable MatCap.");
            Check(data.Colors["_Color"] == (0.1f, 0.2f, 0.3f, 0.5f), "Color fields parsed incorrectly.");
            Check(LilToonSettings.Blend(data, "lilToon") == ToonBlend.Opaque, "Filename/cutoff must not imply transparency.");
            Check(!LilToonSettings.NeedsMainBake(data), "Identity HSVG needs no bake.");
        });
        test("Material parser rejects non-material files", () => Reject<InvalidDataException>(() => UnityMaterialData.Parse("GameObject: {}")));
        test("lilToon Multi blend modes follow the SDK", () =>
        {
            var data = new UnityMaterialData();
            var expected = new[] { ToonBlend.Opaque, ToonBlend.Cutout, ToonBlend.Alpha, ToonBlend.Alpha, ToonBlend.Alpha, ToonBlend.Cutout, ToonBlend.Additive };
            for (int i = 0; i < expected.Length; i++)
            {
                data.Numbers["_TransparentMode"] = i;
                Check(LilToonSettings.Blend(data, "Hidden/lilToonMulti") == expected[i], "Wrong Multi mode: " + i);
            }
        });
        test("Shader variants take precedence over stale material mode values", () =>
        {
            var data = new UnityMaterialData();
            data.Numbers["_TransparentMode"] = 2;
            Check(LilToonSettings.Blend(data, "lilToon") == ToonBlend.Opaque, "Opaque shader read a Multi-only property.");
            foreach (var variant in new[] { "Refraction", "Fur", "Transparent", "Overlay" })
                Check(LilToonSettings.Blend(data, "Hidden/lilToon" + variant) == ToonBlend.Alpha, variant);
            Check(LilToonSettings.Blend(data, "Hidden/lilToonGem") == ToonBlend.Additive, "Gem mode.");
            Check(LilToonSettings.Blend(data, "Hidden/lilToonFurCutout") == ToonBlend.Cutout, "Cutout must win over Fur.");
        });
        test("Normal maps obey enable flags and first-map precedence", () =>
        {
            var data = new UnityMaterialData();
            data.Textures["_BumpMap"] = ("normal", 1, 1, 0, 0);
            Check(LilToonSettings.NormalProperty(data) == null, "Saved disabled normal map imported.");
            data.Numbers["_UseBump2ndMap"] = 1;
            Check(LilToonSettings.NormalProperty(data) == "_Bump2ndMap", "Second map fallback.");
            data.Numbers["_UseBumpMap"] = 1;
            Check(LilToonSettings.NormalProperty(data) == "_BumpMap", "First normal map precedence.");
        });
        test("MatCap permits only SDK-supported additive unmasked mode", () =>
        {
            var data = new UnityMaterialData();
            data.Numbers["_UseMatCap"] = 1;
            Check(!LilToonSettings.MatcapSupported(data), "Non-additive MatCap must not be approximated as additive.");
            data.Numbers["_MatCapBlendMode"] = 1;
            Check(LilToonSettings.MatcapSupported(data), "Additive MatCap rejected.");
            data.Textures["_MatCapBlendMask"] = ("mask", 1, 1, 0, 0);
            Check(!LilToonSettings.MatcapSupported(data), "Masked MatCap requires unsupported bake.");
        });
        test("Outline and emission rules match SDK scalar behavior", () =>
        {
            var data = new UnityMaterialData();
            data.Numbers["_OutlineWidth"] = 0.1f;
            Check(!LilToonSettings.OutlineEnabled(data, "lilToon"), "Width alone enabled outline.");
            Check(LilToonSettings.OutlineEnabled(data, "Hidden/lilToonOutline"), "Outline shader disabled.");
            data.Numbers["_EmissionBlend"] = 0.5f;
            data.Numbers["_EmissionFluorescence"] = 1;
            Check(Math.Abs(LilToonSettings.EmissionStrength(data) - 0.1875f) < 0.00001f, "Fluorescence scaling.");
        });
        test("Shadow ramp respects disabled layers, third layer and shadow strength", () =>
        {
            var data = new UnityMaterialData();
            data.Colors["_ShadowColor"] = (0.4f, 0.5f, 0.6f, 1);
            data.Colors["_Shadow2ndColor"] = (0, 0, 0, 0);
            var c = LilToonRamp.Sample(data, 0, 1);
            Near(c.r, 0.4f); Near(c.g, 0.5f);
            data.Colors["_Shadow3rdColor"] = (0.2f, 0.3f, 0.4f, 1);
            Near(LilToonRamp.Sample(data, 0, 1).r, 0.2f);
            Near(LilToonRamp.Sample(data, 0, 0).r, 1);
            Near(LilToonRamp.Sample(data, 1, 1).r, 1);
            data.Numbers["_ShadowStrength"] = 0;
            Near(LilToonRamp.Sample(data, 0, 1).r, 1);
        });
        test("Shadow PNG includes a white mask row and a shadow row in correct UV orientation", () =>
        {
            var data = new UnityMaterialData();
            data.Colors["_ShadowColor"] = (0.4f, 0.5f, 0.6f, 1);
            data.Colors["_Shadow2ndColor"] = (0, 0, 0, 0);
            byte[] png = LilToonRamp.Generate(data, 16);
            Check(png.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "PNG signature.");
            Check(BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)) == 16, "Width.");
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(33, 4));
            using var compressed = new MemoryStream(png, 41, length);
            using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
            using var raw = new MemoryStream();
            zlib.CopyTo(raw);
            var pixels = raw.ToArray();
            Check(pixels.Length == 16 * 65, "RGBA row size.");
            Check(Math.Abs(pixels[1] - 102) <= 1, "Top-left should be full shadow at UV y=1.");
            Check(pixels[15 * 65 + 1] == 255, "Bottom-left must be white at UV y=0.");
        });
        test("Shadow cache is content-addressed, concurrent-safe, and preserves I/O failures", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "importer-ramp-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var data = new UnityMaterialData();
                var paths = Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => LilToonRamp.GetOrCreateFile(root, data)))).GetAwaiter().GetResult();
                Check(paths.Distinct().Count() == 1 && File.Exists(paths[0]), "Concurrent cache publication.");
                Check(!Directory.GetFiles(root, "*.tmp").Any(), "Temporary files leaked.");
                var time = File.GetLastWriteTimeUtc(paths[0]);
                Check(LilToonRamp.GetOrCreateFile(root, data) == paths[0] && File.GetLastWriteTimeUtc(paths[0]) == time, "Cache rewritten.");
                data.Numbers["_ShadowStrength"] = 0;
                Check(LilToonRamp.GetOrCreateFile(root, data) != paths[0], "Different shadow strength reused old content.");
                Reject<IOException>(() => LilToonRamp.GetWhiteFile(paths[0]));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
        test("Skin mapping uses authored references and mesh order rather than hierarchy search", () =>
        {
            var head = new object(); var hip = new object();
            var mapped = SkinBoneMapping.Resolve(new[] { "Hips", "Head" }, new[] { ("Head", head), ("Hips", hip) });
            Check(mapped.SequenceEqual(new[] { hip, head }), "Assimp order was lost.");
        });
        test("Skin mapping rejects missing or ambiguous bones", () =>
        {
            Reject<InvalidDataException>(() => SkinBoneMapping.Resolve(new[] { "Head" }, new[] { ("Other", new object()) }));
            Reject<InvalidDataException>(() => SkinBoneMapping.Resolve(new[] { "Head" }, new[] { ("Head", new object()), ("Head", new object()) }));
        });
        test("Skin mapping resolves Unity suffix only when no exact match exists", () =>
        {
            var exact = new object(); var alias = new object();
            Check(SkinBoneMapping.Resolve(new[] { "Head" }, new[] { ("Head 1", alias) })[0] == alias, "Unity suffix not resolved.");
            Check(SkinBoneMapping.Resolve(new[] { "Head" }, new[] { ("Head 1", alias), ("Head", exact) })[0] == exact, "Alias overrode exact match.");
        });
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Near(float actual, float expected) => Check(Math.Abs(actual - expected) < 0.0001f, $"{actual} != {expected}");
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
}
