using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Elements.Assets;
using UnityPackageImporter.Models;
using YamlDotNet.RepresentationModel;

namespace UnityPackageImporter.Materials;

internal sealed class TextureCompositionBaker
{
    internal sealed record Source(Bitmap2D Bitmap, bool Srgb, bool Point, int WrapU, int WrapV, bool IgnoreAlpha = false);

    internal static Source ReadSource(string path)
    {
        bool srgb = true, point = false, ignoreAlpha = false;
        int wrapU = 0, wrapV = 0;
        string meta = path + ".meta";
        if (File.Exists(meta))
        {
            var yaml = new YamlStream();
            using var reader = File.OpenText(meta);
            yaml.Load(reader);
            var importer = Child(yaml.Documents[0].RootNode, "TextureImporter");
            var mip = Child(importer, "mipmaps");
            var settings = Child(importer, "textureSettings");
            srgb = Number(Child(mip, "sRGBTexture"), 1) != 0;
            ignoreAlpha = Number(Child(importer, "alphaSource"), 1) == 0;
            point = Number(Child(settings, "filterMode"), 1) == 0;
            int wrap = Number(Child(settings, "wrapMode"), 0);
            wrapU = Number(Child(settings, "wrapU"), wrap); wrapV = Number(Child(settings, "wrapV"), wrap);
            if (Number(Child(importer, "alphaSource"), 1) == 2 || Number(Child(importer, "grayScaleToAlpha"), 0) != 0)
                throw new NotSupportedException("Texture import alpha generation requires a separate conversion step.");
        }
        if (wrapU is < 0 or > 1 || wrapV is < 0 or > 1)
            throw new NotSupportedException("This composition supports repeat and clamp texture wrapping.");
        var bitmap = Bitmap2D.Load(path, generateMipmaps: false, alphaHandling: AlphaHandling.ForceRGBA);
        if (bitmap.Size.x <= 0 || bitmap.Size.y <= 0) throw new InvalidDataException("Invalid source texture dimensions.");
        return new(bitmap, srgb, point, wrapU, wrapV, ignoreAlpha);
    }

    internal static (int WrapU, int WrapV, bool Point) Bake(TextureCompositionPlan plan, IReadOnlyDictionary<string, string> paths,
        string destination, Action checkCancellation = null)
    {
        if (plan.UnsupportedReason != null) throw new NotSupportedException(plan.UnsupportedReason);
        var sources = new Dictionary<string, Source>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in plan.Inputs)
        {
            checkCancellation?.Invoke();
            if (!sources.ContainsKey(input.Guid)) sources.Add(input.Guid, ReadSource(paths[input.Guid]));
        }
        int width = sources.Count == 0 ? 1 : sources.Values.Max(s => s.Bitmap.Size.x);
        int height = sources.Count == 0 ? 1 : sources.Values.Max(s => s.Bitmap.Size.y);
        if ((long)width * height > 16777216) throw new NotSupportedException("Composed texture exceeds the 16-million-pixel bake limit.");
        int wrapU = sources.Count == 0 ? 0 : sources.Values.First().WrapU;
        int wrapV = sources.Count == 0 ? 0 : sources.Values.First().WrapV;
        if (sources.Values.Any(s => s.WrapU != wrapU || s.WrapV != wrapV))
            throw new NotSupportedException("Inputs use different texture wrapping modes.");
        var output = new byte[checked(width * height * 4)];
        float u = 0, v = 0;
        Func<TextureContribution, (float r, float g, float b, float a)> sample = input => Sample(sources[input.Guid], u, v);
        for (int y = 0; y < height; y++)
        {
            checkCancellation?.Invoke();
            v = (y + 0.5f) / height;
            for (int x = 0; x < width; x++)
            {
                u = (x + 0.5f) / width;
                var c = plan.Evaluate(sample);
                int i = (y * width + x) * 4;
                output[i] = Encode(c.r); output[i + 1] = Encode(c.g); output[i + 2] = Encode(c.b); output[i + 3] = 255;
            }
        }
        checkCancellation?.Invoke();
        File.WriteAllBytes(destination, RgbaPng.Encode(output, width, height));
        return (wrapU, wrapV, sources.Count != 0 && sources.Values.All(s => s.Point));
    }

    internal static (float r, float g, float b, float a) Sample(Source source, float u, float v)
    {
        var bitmap = source.Bitmap;
        (float r, float g, float b, float a) Pixel(int x, int y)
        {
            x = Coordinate(x, bitmap.Size.x, source.WrapU); y = Coordinate(y, bitmap.Size.y, source.WrapV);
            var c = bitmap[x, y];
            float alpha = source.IgnoreAlpha ? 1 : c.a;
            return source.Srgb ? (Decode(c.r), Decode(c.g), Decode(c.b), alpha) : (c.r, c.g, c.b, alpha);
        }
        float px = u * bitmap.Size.x - 0.5f, py = v * bitmap.Size.y - 0.5f;
        if (source.Point) return Pixel((int)MathF.Floor(px + 0.5f), (int)MathF.Floor(py + 0.5f));
        int ix = (int)MathF.Floor(px), iy = (int)MathF.Floor(py);
        float tx = px - ix, ty = py - iy;
        return Mix(Mix(Pixel(ix, iy), Pixel(ix + 1, iy), tx), Mix(Pixel(ix, iy + 1), Pixel(ix + 1, iy + 1), tx), ty);
    }
    private static int Coordinate(int i, int size, int wrap) => wrap == 1 ? Math.Clamp(i, 0, size - 1) : ((i % size) + size) % size;
    private static (float r, float g, float b, float a) Mix((float r, float g, float b, float a) a,
        (float r, float g, float b, float a) b, float t) =>
        (a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t);
    private static float Decode(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    private static byte Encode(float c)
    {
        if (!float.IsFinite(c) || c < 0 || c > 1.0001f) throw new NotSupportedException("HDR texture composition requires a floating-point output format.");
        c = c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1 / 2.4f) - 0.055f;
        return (byte)Math.Clamp((int)(c * 255 + 0.5f), 0, 255);
    }
    private static YamlNode Child(YamlNode node, string key) => node is YamlMappingNode map && map.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;
    private static int Number(YamlNode node, int fallback) => node is YamlScalarNode scalar && int.TryParse(scalar.Value, out int value) ? value : fallback;
}
