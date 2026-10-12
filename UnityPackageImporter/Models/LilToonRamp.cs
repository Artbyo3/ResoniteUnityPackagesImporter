// CPU adaptation of lilToon's ltspass_bakeramp.shader and the SDK's vertical
// shadow-mask expansion. MIT, lilxyzw and Yellow Dog Man Studios; see notices.
using System;
using System.IO;
using System.Security.Cryptography;

namespace UnityPackageImporter.Models;

internal static class LilToonRamp
{
    internal static (float r, float g, float b) Sample(UnityMaterialData data, float light, float mask)
    {
        var first = data.Color("_ShadowColor", (0.82f, 0.76f, 0.85f, 1));
        var second = data.Color("_Shadow2ndColor", (0.68f, 0.66f, 0.79f, 1));
        var third = data.Color("_Shadow3rdColor", (0, 0, 0, 0));
        var edge = data.Color("_ShadowBorderColor", (1, 0.1f, 0, 1));
        float lit = Toon(light, data.Number("_ShadowBorder", 0.5f), data.Number("_ShadowBlur", 0.1f));
        lit = Lerp(1, lit, data.Number("_ShadowStrength", 1));
        float secondAmount = second.a * (1 - Toon(light, data.Number("_Shadow2ndBorder", 0.15f), data.Number("_Shadow2ndBlur", 0.1f)));
        float thirdAmount = third.a * (1 - Toon(light, data.Number("_Shadow3rdBorder", 0.25f), data.Number("_Shadow3rdBlur", 0.1f)));
        float border = Toon(light, data.Number("_ShadowBorder", 0.5f), data.Number("_ShadowBlur", 0.1f), data.Number("_ShadowBorderRange", 0.08f));
        float Channel(float a, float b, float c, float borderColor)
        {
            float indirect = Lerp(Linear(a), Linear(b), secondAmount);
            indirect = Lerp(indirect, Linear(c), thirdAmount);
            indirect = Lerp(indirect, 1, border * Linear(borderColor));
            // The SDK expands its baked sRGB row from white at mask=0.
            return Lerp(1, Srgb(Lerp(indirect, 1, lit)), mask);
        }
        return (Channel(first.r, second.r, third.r, edge.r),
            Channel(first.g, second.g, third.g, edge.g), Channel(first.b, second.b, third.b, edge.b));
    }

    internal static byte[] Generate(UnityMaterialData data, int size = 256)
    {
        if (size < 2 || size > 1024) throw new ArgumentOutOfRangeException(nameof(size));
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // PNG rows run top to bottom; shader UV y runs bottom to top.
                var color = Sample(data, (x + 0.5f) / size, 1 - (float)y / (size - 1));
                int index = (y * size + x) * 4;
                pixels[index] = Byte(color.r);
                pixels[index + 1] = Byte(color.g);
                pixels[index + 2] = Byte(color.b);
                pixels[index + 3] = 255;
            }
        return RgbaPng.Encode(pixels, size, size);
    }

    internal static string GetOrCreateFile(string directory, UnityMaterialData data) => Save(directory, Generate(data));
    internal static string GetWhiteFile(string directory) => Save(directory, RgbaPng.Encode(new byte[] { 255, 255, 255, 255 }, 1, 1));

    private static string Save(string directory, byte[] bytes)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "liltoon-v1-" + Convert.ToHexString(SHA256.HashData(bytes)) + ".png");
        if (File.Exists(path)) return path;
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            try { File.Move(temporary, path); }
            catch (IOException) when (File.Exists(path)) { }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return path;
    }

    private static float Toon(float value, float border, float blur, float range = 0)
    {
        float min = Math.Clamp(border - blur * 0.5f - range, 0, 1);
        float max = Math.Clamp(border + blur * 0.5f, 0, 1);
        return max <= min ? (value < min ? 0 : 1) : Math.Clamp((value - min) / (max - min), 0, 1);
    }
    private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0, 1);
    private static float Linear(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    private static float Srgb(float c) => c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1 / 2.4f) - 0.055f;
    private static byte Byte(float c) => (byte)Math.Clamp((int)(c * 255 + 0.5f), 0, 255);
}
