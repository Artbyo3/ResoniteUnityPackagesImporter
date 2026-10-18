using System.Buffers.Binary;
using System.Globalization;
using UnityPackageImporter.Models;
using UnityPackageImporter.Materials;
using UnityPackageImporter.Materials.Poiyomi;

internal static class PoiyomiConversionTests
{
    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        void Near(float a, float b) => check(float.IsFinite(a) && Math.Abs(a - b) < 0.00001f, $"Expected {b}, got {a}.");
        test("Poiyomi recognizes unlocked and optimized shader names without taking lilToon materials", () =>
        {
            var d = new UnityMaterialData();
            foreach (var name in new[] { ".poiyomi/Poiyomi Toon", "Hidden/Locked/.poiyomi/Poiyomi Pro/Example", ".POIYOMI/Toon" })
                check(PoiyomiSettings.Matches(d, name), "Shader name not recognized.");
            d.Colors["_Shadow2ndColor"] = (1, 1, 1, 1);
            d.Numbers["_lilToonVersion"] = 42;
            check(!PoiyomiSettings.Matches(d, "lilToon"), "Shared toon property is not Poiyomi identity.");
            d.Keywords.Add("POI_TEST");
            check(PoiyomiSettings.Matches(d, null!), "Missing shader metadata fallback failed.");
        });
        test("Poiyomi saved property signature requires all discriminating fields", () =>
        {
            var d = new UnityMaterialData();
            d.Numbers["_LightingMode"] = 0;
            check(!PoiyomiSettings.Matches(d, null!), "One common property classified arbitrary shader.");
            foreach (var key in new[] { "_ShadingEnabled", "_MochieBRDF", "_EnableEmission" }) d.Numbers[key] = 0;
            check(PoiyomiSettings.Matches(d, null!), "Saved Poiyomi settings were not recognized.");
        });
        test("Poiyomi chooses PBS only for SDK physically based modes and honors disabled shading", () =>
        {
            var d = new UnityMaterialData();
            foreach (int mode in Enumerable.Range(0, 9))
            {
                d.Numbers["_LightingMode"] = mode;
                check(PoiyomiSettings.UsesPbs(d) == new[] { 2, 6, 7, 8 }.Contains(mode), "Wrong output family.");
                d.Numbers["_ShadingEnabled"] = 0;
                check(!PoiyomiSettings.UsesPbs(d), "Flat shading selected PBS.");
                d.Numbers["_ShadingEnabled"] = 1;
            }
        });
        test("Poiyomi alpha selection follows SDK precedence", () =>
        {
            var d = new UnityMaterialData();
            d.Numbers["_SrcBlend"] = 1; d.Numbers["_DstBlend"] = 0;
            check(PoiyomiSettings.Blend(d) == PoiyomiBlend.Cutout, "Cutout lost.");
            d.Numbers["_AlphaToCoverage"] = 1;
            check(PoiyomiSettings.Blend(d) == PoiyomiBlend.Alpha, "Alpha approximation lost.");
            d.Numbers["_SrcBlend"] = 2;
            check(PoiyomiSettings.Blend(d) == PoiyomiBlend.Multiply, "Multiplication lost.");
            d.Numbers["_DstBlend"] = 1;
            check(PoiyomiSettings.Blend(d) == PoiyomiBlend.Additive, "Additive precedence.");
            d.Numbers["_DstBlend"] = 10;
            check(PoiyomiSettings.Blend(d) == PoiyomiBlend.Transparent, "Transparent precedence.");
            d.Numbers["_AlphaForceOpaque"] = 1;
            check(PoiyomiSettings.Blend(d) == PoiyomiBlend.Opaque, "Forced opaque lost.");
        });
        test("Poiyomi absent blend properties preserve serialized render tags", () =>
        {
            var d = UnityMaterialData.Parse("Material:\n  stringTagMap: {RenderType: Transparent}\n  m_SavedProperties: {}\n");
            check(PoiyomiSettings.Blend(d) == PoiyomiBlend.Transparent, "Explicit transparent tag ignored.");
            check(PoiyomiSettings.Blend(new()) == PoiyomiBlend.Opaque, "Absent shader defaults guessed transparency.");
        });
        test("Poiyomi emission ignores stale disabled settings and preserves alpha with HDR strength", () =>
        {
            var d = new UnityMaterialData();
            d.Colors["_EmissionColor"] = (0.25f, 0.5f, 1, 0.2f);
            d.Numbers["_EmissionStrength"] = 4;
            Near(PoiyomiSettings.Emission(d).r, 0);
            d.Numbers["_EnableEmission"] = 1;
            var c = PoiyomiSettings.Emission(d);
            Near(c.r, 1); Near(c.g, 2); Near(c.b, 4); Near(c.a, 0.2f);
        });
        test("Poiyomi metallic packing uses different PBS and toon layouts", () =>
        {
            var d = new UnityMaterialData();
            d.Numbers["_MochieMetallicMapsMetallicChannel"] = 2;
            d.Numbers["_MochieMetallicMapsRoughnessChannel"] = 1;
            d.Numbers["_MochieMetallicMapsReflectionMaskChannel"] = 4;
            d.Numbers["_MochieMetallicMapsSpecularMaskChannel"] = 3;
            d.Numbers["_MochieRoughnessMapInvert"] = 1;
            var toon = PoiyomiSettings.MetallicTransform(d, false).Apply(0.1f, 0.2f, 0.3f, 0.4f);
            var pbs = PoiyomiSettings.MetallicTransform(d, true).Apply(0.1f, 0.2f, 0.3f, 0.4f);
            Near(toon.r, 0.3f); Near(toon.g, 0.8f); Near(toon.b, 1); Near(toon.a, 0.4f);
            Near(pbs.r, 0.3f); Near(pbs.g, 0); Near(pbs.b, 0); Near(pbs.a, 0.8f);
            check(PoiyomiSettings.MetallicTransform(d, false).Key != PoiyomiSettings.MetallicTransform(d, true).Key, "Different outputs share texture cache key.");
        });
        test("Texture channel transforms support every channel, constants and inversions", () =>
        {
            float[] expected = { 0.1f, 0.2f, 0.3f, 0.4f, 1, 0.9f, 0.8f, 0.7f, 0.6f, 0 };
            for (int i = 0; i < expected.Length; i++) Near(TexturePixelTransform.Swizzle(i, i, i, i).Apply(0.1f, 0.2f, 0.3f, 0.4f).r, expected[i]);
        });
        test("Poiyomi shadow tint preserves white for texture ramps and tints white for skin", () =>
        {
            var d = new UnityMaterialData();
            d.Colors["_LightingShadowColor"] = (0.5f, 0.25f, 0.75f, 1);
            var normal = TexturePixelTransform.ShadowTint(d, false);
            var skin = TexturePixelTransform.ShadowTint(d, true);
            Near(normal.Apply(1, 1, 1, 1).r, 1);
            Near(skin.Apply(1, 1, 1, 1).r, 0.5f);
            Near(normal.Apply(0.5f, 0.5f, 0.5f, 1).r, 0.375f);
            d.Numbers["_ShadowStrength"] = 0;
            Near(TexturePixelTransform.ShadowTint(d, true).Apply(0, 0, 0, 0).r, 1);
            Near(skin.Apply(1, 1, 1, 1).r, 0.5f); // Recipe captures settings, not a mutable material.
        });
        test("Texture recipe cache identity includes tint strength mode and is culture independent", () =>
        {
            var d = new UnityMaterialData();
            d.Colors["_LightingShadowColor"] = (0.25f, 0.5f, 0.75f, 1);
            var a = TexturePixelTransform.ShadowTint(d, false);
            check(a.Key != TexturePixelTransform.ShadowTint(d, true).Key, "Skin mode collision.");
            d.Numbers["_ShadowStrength"] = 0.5f;
            check(a.Key != TexturePixelTransform.ShadowTint(d, false).Key, "Strength collision.");
            var saved = CultureInfo.CurrentCulture;
            string key = a.Key;
            try { CultureInfo.CurrentCulture = new CultureInfo("fr-FR"); check(a.Key == key, "Culture-dependent cache."); }
            finally { CultureInfo.CurrentCulture = saved; }
        });
        test("Poiyomi tinting uses linear light while packed data inversion stays numeric", () =>
        {
            var d = new UnityMaterialData();
            d.Colors["_LightingShadowColor"] = (0.5f, 0.5f, 0.5f, 1);
            var t = TexturePixelTransform.ShadowTint(d, true);
            Near(t.ApplyStored(0.5f, 0.5f, 0.5f, 1).r, MathF.Pow(MathF.Pow(0.5f, 2.2f) * 0.5f, 1 / 2.2f));
            Near(TexturePixelTransform.Swizzle(5, 1, 2, 3).ApplyStored(0.25f, 0, 0, 1).r, 0.75f);
        });
        test("Poiyomi multilayer ramp respects shadow alpha and strength", () =>
        {
            var d = new UnityMaterialData();
            d.Numbers["_LightingMode"] = 1;
            d.Colors["_ShadowColor"] = (0.4f, 0.5f, 0.6f, 1);
            Near(PoiyomiRamp.Sample(d, 0).r, 0.4f); Near(PoiyomiRamp.Sample(d, 1).r, 1);
            d.Colors["_Shadow2ndColor"] = (0.2f, 0.3f, 0.4f, 0.5f);
            Near(PoiyomiRamp.Sample(d, 0).r, 0.3f);
            d.Numbers["_ShadowStrength"] = 0;
            Near(PoiyomiRamp.Sample(d, 0).r, 1);
        });
        test("Poiyomi ramp zero feather and full border range remain finite at boundaries", () =>
        {
            var d = new UnityMaterialData();
            d.Numbers["_BaseShade_Feather"] = 0; d.Numbers["_1st2nd_Shades_Feather"] = 0;
            d.Numbers["_ShadowBlur"] = 0; d.Numbers["_ShadowBorderRange"] = 1;
            d.Colors["_ShadowBorderColor"] = (1, 0, 0, 1);
            foreach (int mode in new[] { 1, 4 })
            {
                d.Numbers["_LightingMode"] = mode;
                foreach (float x in new[] { 0, 0.25f, 0.5f, 1 })
                { var c = PoiyomiRamp.Sample(d, x); check(float.IsFinite(c.r) && float.IsFinite(c.g) && float.IsFinite(c.b), "NaN ramp."); }
            }
        });
        test("Poiyomi procedural ramps retain the SDK texture dimensions and respond to changes", () =>
        {
            var d = new UnityMaterialData(); d.Numbers["_LightingMode"] = 4;
            byte[] first = PoiyomiRamp.Generate(d);
            check(BinaryPrimitives.ReadInt32BigEndian(first.AsSpan(16, 4)) == 512 && BinaryPrimitives.ReadInt32BigEndian(first.AsSpan(20, 4)) == 4, "Ramp dimensions.");
            d.Colors["_1st_ShadeColor"] = (1, 0, 0, 1);
            check(!first.SequenceEqual(PoiyomiRamp.Generate(d)), "New colors reused stale ramp.");
        });
        test("Poiyomi diagnostics distinguish unsupported effects and unknown lighting", () =>
        {
            var d = new UnityMaterialData(); d.Numbers["_LightingMode"] = 0; d.Numbers["_ShadingEnabled"] = 1;
            check(!PoiyomiSettings.Warnings(d, false).Any(), "Ordinary material warned.");
            d.Numbers["_EnableOutlines"] = 1;
            check(PoiyomiSettings.Warnings(d, true).Any(), "PBS outline loss is silent.");
            d.Numbers["_LightingMode"] = 999;
            check(PoiyomiSettings.Warnings(d, false).Any(x => x.Contains("unknown")), "Unknown lighting silently guessed.");
        });
    }
}
