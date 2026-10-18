#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

#if !REGRESSION_TESTS
using Elements.Assets;
using Elements.Core;
using FrooxEngine;
#endif

namespace UnityPackageImporter.Models;

/// <summary>
/// Pre-bakes 128x128 pseudo-3D material sphere thumbnails with specular, diffuse,
/// rim fresnel, MatCap, emission and anti-aliased edge cutout.
/// </summary>
internal static class MaterialThumbnailGenerator
{
    private const int ThumbnailSize = 128;
    private const float OrbRadius = 54.0f;
    private const float Center = (ThumbnailSize - 1) / 2.0f; // 63.5f

    private static readonly ConcurrentDictionary<string, (float r, float g, float b, float a)> BaseColorCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Uri> ImportedUriCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim ThumbnailThrottle = new(1, 1);

    public delegate (int width, int height, byte[] rgba)? TextureReader(string filePath, int maxDimension);

    private readonly struct Vec3(float x, float y, float z)
    {
        public readonly float X = x, Y = y, Z = z;

        public static Vec3 Normalize(float x, float y, float z)
        {
            float len = MathF.Sqrt(x * x + y * y + z * z);
            return len > 1e-6f ? new Vec3(x / len, y / len, z / len) : new Vec3(0, 0, 1);
        }
    }

    private static readonly Vec3 KeyLight = Vec3.Normalize(-0.45f, 0.65f, 0.60f);
    private static readonly Vec3 HalfDir = Vec3.Normalize(-0.45f, 0.65f, 1.60f);
    private static readonly Vec3 FillLight = Vec3.Normalize(0.50f, -0.40f, 0.35f);

    public static string GetThumbnailCacheDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ResoniteMaterialThumbs");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void SetCachedBaseColor(string materialGuid, (float r, float g, float b, float a) color)
    {
        if (!string.IsNullOrEmpty(materialGuid))
            BaseColorCache[materialGuid] = color;
    }

    public static (float r, float g, float b, float a) GetMaterialBaseColor(string materialGuid, IReadOnlyDictionary<string, string> assetIndex)
    {
        if (string.IsNullOrEmpty(materialGuid)) return (0.8f, 0.8f, 0.8f, 1.0f);
        if (BaseColorCache.TryGetValue(materialGuid, out var cached)) return cached;

        try
        {
            if (assetIndex.TryGetValue(materialGuid, out string? matPath) &&
                !string.IsNullOrEmpty(matPath) && File.Exists(matPath))
            {
                var data = UnityMaterialData.Parse(File.ReadAllText(matPath));
                var col = data.Color("_Color", data.Color("_BaseColor", (0.8f, 0.8f, 0.8f, 1.0f)));
                BaseColorCache[materialGuid] = col;
                return col;
            }
        }
        catch (Exception ex)
        {
#if !REGRESSION_TESTS
            UnityPackageImporter.Warn($"Failed to read material base color for '{materialGuid}': {ex.Message}");
#endif
        }

        return (0.8f, 0.8f, 0.8f, 1.0f);
    }

#if !REGRESSION_TESTS
    public static (float r, float g, float b, float a) GetMaterialBaseColor(string materialGuid, UnityProjectImporter importer) =>
        GetMaterialBaseColor(materialGuid, importer.AssetIDDict);

    public static Task<string?> GetOrCreateThumbnailAsync(
        string materialGuid,
        UnityProjectImporter importer,
        CancellationToken cancellationToken = default) =>
        GetOrCreateThumbnailAsync(materialGuid, importer.AssetIDDict, cancellationToken);

    public static (int width, int height, byte[] rgba)? LoadBitmapRgba(string filePath, int maxDimension = 128)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;
        try
        {
            var bitmap = Bitmap2D.Load(filePath, null, Elements.Assets.AlphaHandling.ForceRGBA, maxDimension, 1f);
            if (bitmap == null || bitmap.Size.x <= 0 || bitmap.Size.y <= 0) return null;

            int w = bitmap.Size.x;
            int h = bitmap.Size.y;
            byte[] bytes = new byte[w * h * 4];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var c = bitmap[x, y];
                    int idx = (y * w + x) * 4;
                    bytes[idx + 0] = (byte)(Math.Clamp(c.r, 0f, 1f) * 255f);
                    bytes[idx + 1] = (byte)(Math.Clamp(c.g, 0f, 1f) * 255f);
                    bytes[idx + 2] = (byte)(Math.Clamp(c.b, 0f, 1f) * 255f);
                    bytes[idx + 3] = (byte)(Math.Clamp(c.a, 0f, 1f) * 255f);
                }
            }

            return (w, h, bytes);
        }
        catch (Exception ex)
        {
            UnityPackageImporter.Warn($"Failed to load bitmap from '{filePath}': {ex.Message}");
            return null;
        }
    }
#endif

    public static async Task<string?> GetOrCreateThumbnailAsync(
        string materialGuid,
        IReadOnlyDictionary<string, string> assetIndex,
        CancellationToken cancellationToken = default,
        TextureReader? customTextureReader = null)
    {
        if (string.IsNullOrEmpty(materialGuid)) return null;
        if (!assetIndex.TryGetValue(materialGuid, out string? matPath) ||
            string.IsNullOrEmpty(matPath) || !File.Exists(matPath))
            return null;

        string cacheDir = GetThumbnailCacheDirectory();
        long lastWrite = File.GetLastWriteTimeUtc(matPath).Ticks;
        string cacheFileName = $"v2_{materialGuid}_{lastWrite}.png";
        string cacheFilePath = Path.Combine(cacheDir, cacheFileName);

        if (File.Exists(cacheFilePath) && new FileInfo(cacheFilePath).Length > 0)
        {
            return cacheFilePath;
        }

        await ThumbnailThrottle.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                string yaml = File.ReadAllText(matPath);
                var data = UnityMaterialData.Parse(yaml);

                TextureReader? reader = customTextureReader;
#if !REGRESSION_TESTS
                reader ??= LoadBitmapRgba;
#endif

                byte[] pngBytes = RenderOrbPng(data, assetIndex, reader, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                string tempFile = Path.Combine(cacheDir, $"{materialGuid}_{Guid.NewGuid():N}.tmp");
                File.WriteAllBytes(tempFile, pngBytes);

                try
                {
                    if (File.Exists(cacheFilePath)) File.Delete(cacheFilePath);
                    File.Move(tempFile, cacheFilePath);
                }
                catch (IOException)
                {
                    // Concurrent thumbnail generation may lock or replace the destination; clean up temp file.
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                }

                return cacheFilePath;
            }, cancellationToken);
        }
        finally
        {
            ThumbnailThrottle.Release();
        }
    }

    public static byte[] RenderOrbPng(
        UnityMaterialData data,
        IReadOnlyDictionary<string, string> assetIndex,
        TextureReader? textureReader,
        CancellationToken cancellationToken = default)
    {
        byte[] pixels = RenderOrbRgba(data, assetIndex, textureReader, cancellationToken);
        return RgbaPng.Encode(pixels, ThumbnailSize, ThumbnailSize);
    }

    public static byte[] RenderOrbRgba(
        UnityMaterialData data,
        IReadOnlyDictionary<string, string> assetIndex,
        TextureReader? textureReader,
        CancellationToken cancellationToken = default)
    {
        var baseColor = data.Color("_Color", data.Color("_BaseColor", (1.0f, 1.0f, 1.0f, 1.0f)));
        float smoothness = data.Number("_Smoothness", data.Number("_Glossiness", 0.5f));
        float metallic = Math.Clamp(data.Number("_Metallic", 0.0f), 0.0f, 1.0f);
        float cutoff = data.Number("_Cutoff", 0.5f);
        bool isCutout = data.RenderType != null && data.RenderType.Contains("Cutout", StringComparison.OrdinalIgnoreCase);

        // Main Texture
        (int width, int height, byte[] rgba)? mainTex = null;
        float sx = 1f, sy = 1f, ox = 0f, oy = 0f;
        string[] mainTexCandidates = { "_MainTex", "_BaseMap", "_BaseColorMap", "_MainTexture", "_MainColorTex", "_1st_ShadeMap" };
        string? matchedProp = null;
        string? mainTexGuid = null;
        foreach (var prop in mainTexCandidates)
        {
            string? g = data.Texture(prop);
            if (!string.IsNullOrEmpty(g))
            {
                matchedProp = prop;
                mainTexGuid = g;
                break;
            }
        }
        if (matchedProp != null && data.Textures.TryGetValue(matchedProp, out var texProps))
        {
            sx = texProps.sx;
            sy = texProps.sy;
            ox = texProps.ox;
            oy = texProps.oy;
        }

        if (mainTexGuid != null && textureReader != null && assetIndex.TryGetValue(mainTexGuid, out var texPath))
        {
            mainTex = textureReader(texPath, 128);
        }

        // MatCap
        (int width, int height, byte[] rgba)? matcapTex = null;
        var matcapColor = data.Color("_MatCapColor", (1f, 1f, 1f, 1f));
        float matcapBlend = data.Number("_MatCapBlend", 1f);
        bool useMatcap = data.Enabled("_UseMatCap") || data.Texture("_MatCapTex") != null;
        string? matcapGuid = data.Texture("_MatCapTex");
        if (useMatcap && matcapGuid != null && textureReader != null && assetIndex.TryGetValue(matcapGuid, out var matcapPath))
        {
            matcapTex = textureReader(matcapPath, 128);
        }

        // Emission
        (int width, int height, byte[] rgba)? emissionTex = null;
        var emissionColor = data.Color("_EmissionColor", (0f, 0f, 0f, 1f));
        bool useEmission = data.Enabled("_UseEmission") || (emissionColor.r > 0 || emissionColor.g > 0 || emissionColor.b > 0) || data.Texture("_EmissionMap") != null;
        string? emissionGuid = data.Texture("_EmissionMap");
        if (useEmission && emissionGuid != null && textureReader != null && assetIndex.TryGetValue(emissionGuid, out var emissionPath))
        {
            emissionTex = textureReader(emissionPath, 128);
        }

        byte[] pixels = new byte[ThumbnailSize * ThumbnailSize * 4];
        float edgeWidth = 1.0f / OrbRadius;

        for (int y = 0; y < ThumbnailSize; y++)
        {
            if ((y & 7) == 0) cancellationToken.ThrowIfCancellationRequested();

            for (int x = 0; x < ThumbnailSize; x++)
            {
                int pIdx = (y * ThumbnailSize + x) * 4;

                float dx = (x - Center) / OrbRadius;
                float dy = (y - Center) / OrbRadius;
                float distSq = dx * dx + dy * dy;
                float dist = MathF.Sqrt(distSq);

                if (dist > 1.0f + edgeWidth)
                {
                    pixels[pIdx + 3] = 0;
                    continue;
                }

                float edgeAlpha = dist > 1.0f - edgeWidth
                    ? Math.Clamp((1.0f + edgeWidth - dist) / (2.0f * edgeWidth), 0.0f, 1.0f)
                    : 1.0f;

                float dz = MathF.Sqrt(MathF.Max(0.0f, 1.0f - distSq));
                float nx = dx;
                float ny = -dy;
                float nz = dz;

                // Spherical UV
                float u = MathF.Asin(Math.Clamp(dx, -1.0f, 1.0f)) / MathF.PI + 0.5f;
                float v = MathF.Asin(Math.Clamp(-dy, -1.0f, 1.0f)) / MathF.PI + 0.5f;

                u = (u * sx + ox);
                v = (v * sy + oy);
                u = u - MathF.Floor(u);
                v = v - MathF.Floor(v);

                float albedoR = baseColor.r;
                float albedoG = baseColor.g;
                float albedoB = baseColor.b;
                float albedoA = baseColor.a;

                if (mainTex.HasValue)
                {
                    var tex = mainTex.Value;
                    int tx = Math.Clamp((int)(u * (tex.width - 1)), 0, tex.width - 1);
                    int ty = Math.Clamp((int)((1.0f - v) * (tex.height - 1)), 0, tex.height - 1);
                    int tOff = (ty * tex.width + tx) * 4;

                    albedoR *= tex.rgba[tOff + 0] / 255.0f;
                    albedoG *= tex.rgba[tOff + 1] / 255.0f;
                    albedoB *= tex.rgba[tOff + 2] / 255.0f;
                    albedoA *= tex.rgba[tOff + 3] / 255.0f;
                }

                if (isCutout && albedoA < cutoff)
                {
                    pixels[pIdx + 3] = 0;
                    continue;
                }

                // Diffuse Lighting (Half-Lambert)
                float nDotL = nx * KeyLight.X + ny * KeyLight.Y + nz * KeyLight.Z;
                float diffKey = Math.Clamp(nDotL * 0.55f + 0.45f, 0.0f, 1.0f);

                float nDotFill = nx * FillLight.X + ny * FillLight.Y + nz * FillLight.Z;
                float diffFill = Math.Clamp(nDotFill * 0.5f + 0.5f, 0.0f, 1.0f) * 0.20f;

                float totalDiffuse = Math.Clamp(diffKey * 0.70f + diffFill + 0.25f, 0.0f, 1.2f);

                // Specular
                float nDotH = MathF.Max(0.0f, nx * HalfDir.X + ny * HalfDir.Y + nz * HalfDir.Z);
                float specExponent = 12.0f + smoothness * 116.0f;
                float specIntensity = MathF.Pow(nDotH, specExponent) * (0.15f + 0.85f * smoothness);

                float specR = (1.0f - metallic) + metallic * albedoR;
                float specG = (1.0f - metallic) + metallic * albedoG;
                float specB = (1.0f - metallic) + metallic * albedoB;

                // Rim Fresnel
                float fresnel = 1.0f - nz;
                float rim = MathF.Pow(Math.Clamp(fresnel, 0.0f, 1.0f), 3.0f) * (0.30f + 0.20f * smoothness);

                // MatCap
                float matcapR = 0f, matcapG = 0f, matcapB = 0f;
                if (matcapTex.HasValue)
                {
                    var mTex = matcapTex.Value;
                    float mu = Math.Clamp(nx * 0.5f + 0.5f, 0.0f, 1.0f);
                    float mv = Math.Clamp(ny * 0.5f + 0.5f, 0.0f, 1.0f);
                    int mx = Math.Clamp((int)(mu * (mTex.width - 1)), 0, mTex.width - 1);
                    int my = Math.Clamp((int)((1.0f - mv) * (mTex.height - 1)), 0, mTex.height - 1);
                    int mOff = (my * mTex.width + mx) * 4;

                    matcapR = (mTex.rgba[mOff + 0] / 255.0f) * matcapColor.r * matcapBlend;
                    matcapG = (mTex.rgba[mOff + 1] / 255.0f) * matcapColor.g * matcapBlend;
                    matcapB = (mTex.rgba[mOff + 2] / 255.0f) * matcapColor.b * matcapBlend;
                }

                // Emission
                float emissR = emissionColor.r;
                float emissG = emissionColor.g;
                float emissB = emissionColor.b;
                if (emissionTex.HasValue)
                {
                    var eTex = emissionTex.Value;
                    int ex = Math.Clamp((int)(u * (eTex.width - 1)), 0, eTex.width - 1);
                    int ey = Math.Clamp((int)((1.0f - v) * (eTex.height - 1)), 0, eTex.height - 1);
                    int eOff = (ey * eTex.width + ex) * 4;

                    emissR *= eTex.rgba[eOff + 0] / 255.0f;
                    emissG *= eTex.rgba[eOff + 1] / 255.0f;
                    emissB *= eTex.rgba[eOff + 2] / 255.0f;
                }

                // Combine Color
                float litR = albedoR * totalDiffuse * (1.0f - metallic * 0.7f) + specIntensity * specR + rim * 0.8f + matcapR + emissR;
                float litG = albedoG * totalDiffuse * (1.0f - metallic * 0.7f) + specIntensity * specG + rim * 0.8f + matcapG + emissG;
                float litB = albedoB * totalDiffuse * (1.0f - metallic * 0.7f) + specIntensity * specB + rim * 0.8f + matcapB + emissB;
                float finalA = edgeAlpha * albedoA;

                pixels[pIdx + 0] = (byte)(Math.Clamp(litR, 0.0f, 1.0f) * 255.0f);
                pixels[pIdx + 1] = (byte)(Math.Clamp(litG, 0.0f, 1.0f) * 255.0f);
                pixels[pIdx + 2] = (byte)(Math.Clamp(litB, 0.0f, 1.0f) * 255.0f);
                pixels[pIdx + 3] = (byte)(Math.Clamp(finalA, 0.0f, 1.0f) * 255.0f);
            }
        }

        return pixels;
    }
}
