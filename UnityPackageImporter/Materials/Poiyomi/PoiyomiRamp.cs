// CPU adaptation of Resonite.UnitySDK's Poiyomi shadow ramp generators (MIT).
// Copyright (c) 2026 Yellow Dog Man Studios. See THIRD_PARTY_NOTICES.md.
using System;
using System.IO;
using System.Security.Cryptography;
using UnityPackageImporter.Models;

namespace UnityPackageImporter.Materials.Poiyomi;

internal static class PoiyomiRamp
{
    internal static (float r, float g, float b, float a) Sample(UnityMaterialData d, float light)
    {
        var pixel = (r: 1f, g: 1f, b: 1f, a: 1f);
        if (PoiyomiSettings.Lighting(d) == PoiyomiLighting.ShadeMap)
        {
            float first = Influence(light, d.Number("_BaseColor_Step", 0.5f), d.Number("_BaseShade_Feather", 0.1f));
            if (first > 0)
            {
                pixel = Mix(pixel, d.Color("_1st_ShadeColor", (0.5f, 0.5f, 0.5f, 1)), first);
                pixel = Mix(pixel, d.Color("_2nd_ShadeColor", (0.2f, 0.2f, 0.2f, 1)),
                    Influence(light, d.Number("_ShadeColor_Step", 0.25f), d.Number("_1st2nd_Shades_Feather", 0.1f)));
            }
        }
        else
        {
            float influence = 1;
            string[] suffixes = { "", "2nd", "3rd" };
            for (int i = 0; i < suffixes.Length && influence > 0; i++)
            {
                string prefix = "_Shadow" + suffixes[i];
                var c = d.Color(prefix + "Color", i == 0 ? (0.5f, 0.5f, 0.5f, 1) : (0, 0, 0, 0));
                float layer = Influence(light, d.Number(prefix + "Border", i == 0 ? 0.5f : 0.25f), d.Number(prefix + "Blur", 0.1f));
                if (i == 0) influence = layer;
                else layer *= influence;
                pixel = Mix(pixel, c, c.a * layer);
            }
            var border = d.Color("_ShadowBorderColor", (1, 1, 1, 0));
            float range = 1 - Math.Clamp(d.Number("_ShadowBorderRange", 0.1f), 0, 1);
            float center = d.Number("_ShadowBorder", 0.5f);
            float blur = Math.Max(0, d.Number("_ShadowBlur", 0.1f));
            float width = 2 * range * blur;
            // Explicit step avoids NaN at a zero-width boundary (including range == 0).
            float edge = width == 0 ? (light >= range * center ? 1 : 0) :
                Math.Clamp((light - range * (center - blur)) / width, 0, 1);
            pixel = Mix(pixel, border, border.a * influence * edge);
        }
        pixel = Mix((1, 1, 1, 1), pixel, d.Number("_ShadowStrength", 1));
        return (pixel.r, pixel.g, pixel.b, 1);
    }

    private static float Influence(float light, float border, float blur) => blur <= 0 ? (light <= border ? 1 : 0) :
        Math.Clamp((border + blur - light) / (2 * blur), 0, 1);
    private static (float r, float g, float b, float a) Mix((float r, float g, float b, float a) a,
        (float r, float g, float b, float a) b, float t)
    {
        t = Math.Clamp(t, 0, 1);
        return (a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t);
    }
    internal static byte[] Generate(UnityMaterialData data)
    {
        var pixels = new byte[512 * 4 * 4];
        for (int x = 0; x < 512; x++)
        {
            var c = Sample(data, x / 511f);
            for (int y = 0; y < 4; y++)
            {
                int i = (y * 512 + x) * 4;
                pixels[i] = Byte(c.r); pixels[i + 1] = Byte(c.g); pixels[i + 2] = Byte(c.b); pixels[i + 3] = 255;
            }
        }
        return RgbaPng.Encode(pixels, 512, 4);
    }
    private static byte Byte(float x) => (byte)Math.Clamp((int)(x * 255 + 0.5f), 0, 255);

    internal static string Save(string directory, byte[] bytes)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "poiyomi-v1-" + Convert.ToHexString(SHA256.HashData(bytes)) + ".png");
        if (File.Exists(path)) return path;
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            try { File.Move(temporary, path); }
            catch (IOException) when (File.Exists(path))
            {
                // Another thread already generated and moved the ramp texture into place.
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return path;
    }
}
