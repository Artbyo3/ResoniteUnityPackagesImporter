using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Elements.Assets;
using Elements.Core;

namespace UnityPackageImporter.Models;

public static class TextureCompositor
{
    private static readonly object CompositeLock = new();

    public static string GetCompositeHash(
        string mainTexPath, (float r, float g, float b, float a) mainColor,
        string main2ndTexPath, string main2ndMaskPath, int main2ndBlendMode, (float r, float g, float b, float a) main2ndColor,
        string main3rdTexPath, string main3rdMaskPath, int main3rdBlendMode, (float r, float g, float b, float a) main3rdColor,
        string alphaMaskPath)
    {
        using var sha = SHA256.Create();
        var sb = new StringBuilder();
        AppendFileKey(sb, mainTexPath);
        AppendColorKey(sb, mainColor);

        AppendFileKey(sb, main2ndTexPath);
        AppendFileKey(sb, main2ndMaskPath);
        sb.Append("m2b:").Append(main2ndBlendMode).Append(";");
        AppendColorKey(sb, main2ndColor);

        AppendFileKey(sb, main3rdTexPath);
        AppendFileKey(sb, main3rdMaskPath);
        sb.Append("m3b:").Append(main3rdBlendMode).Append(";");
        AppendColorKey(sb, main3rdColor);

        AppendFileKey(sb, alphaMaskPath);

        byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
        byte[] hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void AppendFileKey(StringBuilder sb, string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            sb.Append("null;");
            return;
        }
        sb.Append(Path.GetFileName(path)).Append(":")
          .Append(new FileInfo(path).Length).Append(":")
          .Append(File.GetLastWriteTimeUtc(path).Ticks).Append(";");
    }

    private static void AppendColorKey(StringBuilder sb, (float r, float g, float b, float a) c)
    {
        sb.Append(c.r.ToString("F3")).Append(",")
          .Append(c.g.ToString("F3")).Append(",")
          .Append(c.b.ToString("F3")).Append(",")
          .Append(c.a.ToString("F3")).Append(";");
    }

    private static Bitmap2D LoadBitmap(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            return Bitmap2D.Load(path, false, AlphaHandling.ForceRGBA, -1, 0f);
        }
        catch (Exception ex)
        {
            UnityPackageImporter.Warn($"Failed to load texture for compositing {path}: {ex.Message}");
            return null;
        }
    }

    public static string GetOrCreateCompositeMainTex(
        string cacheDirectory,
        string mainTexPath, (float r, float g, float b, float a) mainColor,
        string main2ndTexPath, string main2ndMaskPath, int main2ndBlendMode, (float r, float g, float b, float a) main2ndColor,
        string main3rdTexPath, string main3rdMaskPath, int main3rdBlendMode, (float r, float g, float b, float a) main3rdColor,
        string alphaMaskPath, out string hash)
    {
        hash = GetCompositeHash(
            mainTexPath, mainColor,
            main2ndTexPath, main2ndMaskPath, main2ndBlendMode, main2ndColor,
            main3rdTexPath, main3rdMaskPath, main3rdBlendMode, main3rdColor,
            alphaMaskPath);

        string compositesDir = Path.Combine(cacheDirectory, "Composites");
        string finalPath = Path.Combine(compositesDir, "comp_" + hash + ".png");

        if (File.Exists(finalPath)) return finalPath;

        lock (CompositeLock)
        {
            if (File.Exists(finalPath)) return finalPath;
            Directory.CreateDirectory(compositesDir);

            string tmpPath = Path.Combine(compositesDir, "comp_" + hash + "_" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                var baseBmp = LoadBitmap(mainTexPath);
                if (baseBmp == null) return null;

                // 1. Base tint
                if (mainColor.r < 0.999f || mainColor.g < 0.999f || mainColor.b < 0.999f || mainColor.a < 0.999f)
                {
                    baseBmp.Multiply(new colorX(mainColor.r, mainColor.g, mainColor.b, mainColor.a));
                }

                // 2. 2nd Texture layer
                ApplyLayer(baseBmp, main2ndTexPath, main2ndMaskPath, main2ndBlendMode, main2ndColor);

                // 3. 3rd Texture layer
                ApplyLayer(baseBmp, main3rdTexPath, main3rdMaskPath, main3rdBlendMode, main3rdColor);

                // 4. Alpha Mask (Body Hider)
                if (!string.IsNullOrEmpty(alphaMaskPath) && File.Exists(alphaMaskPath))
                {
                    var alphaBmp = LoadBitmap(alphaMaskPath);
                    if (alphaBmp != null)
                    {
                        var rescaledAlpha = (alphaBmp.Size == baseBmp.Size) ? alphaBmp : alphaBmp.GetRescaled(baseBmp.Size);
                        baseBmp.ApplyMask(rescaledAlpha);
                    }
                }

                baseBmp.Save(tmpPath, -1, true);

                try
                {
                    File.Move(tmpPath, finalPath, overwrite: true);
                }
                catch (IOException)
                {
                    if (!File.Exists(finalPath)) throw;
                }

                return finalPath;
            }
            catch (Exception ex)
            {
                UnityPackageImporter.Warn("Failed to composite texture: " + ex.Message);
                return null;
            }
            finally
            {
                if (File.Exists(tmpPath))
                {
                    try { File.Delete(tmpPath); } catch { }
                }
            }
        }
    }

    private static void ApplyLayer(Bitmap2D baseBmp, string texPath, string maskPath, int blendMode, (float r, float g, float b, float a) tint)
    {
        if (string.IsNullOrEmpty(texPath) || !File.Exists(texPath)) return;

        var layerBmp = LoadBitmap(texPath);
        if (layerBmp == null) return;

        var workingLayer = (layerBmp.Size == baseBmp.Size) ? layerBmp : layerBmp.GetRescaled(baseBmp.Size);

        // Tint
        if (tint.r < 0.999f || tint.g < 0.999f || tint.b < 0.999f || tint.a < 0.999f)
        {
            workingLayer.Multiply(new colorX(tint.r, tint.g, tint.b, tint.a));
        }

        // Apply Mask if specified
        if (!string.IsNullOrEmpty(maskPath) && File.Exists(maskPath))
        {
            var maskBmp = LoadBitmap(maskPath);
            if (maskBmp != null)
            {
                var rescaledMask = (maskBmp.Size == workingLayer.Size) ? maskBmp : maskBmp.GetRescaled(workingLayer.Size);
                workingLayer.ApplyMask(rescaledMask);
            }
        }

        // Blend into base
        switch (blendMode)
        {
            case 1: // Add
                baseBmp.AdditiveBlend(workingLayer);
                break;
            case 2: // Screen
                baseBmp.CustomBlend(workingLayer, (c1, c2) => new color(
                    c1.r + c2.r - c1.r * c2.r,
                    c1.g + c2.g - c1.g * c2.g,
                    c1.b + c2.b - c1.b * c2.b,
                    Math.Max(c1.a, c2.a)));
                break;
            case 3: // Multiply
                baseBmp.MultiplyBlend(workingLayer);
                break;
            case 0: // Normal / AlphaBlend
            default:
                baseBmp.AlphaBlend(workingLayer);
                break;
        }
    }

    public static string GetOrCreatePackedMetallicGlossMap(
        string cacheDirectory,
        string metallicPath, float metallicFloat,
        string smoothnessPath, float smoothnessFloat,
        out string hash)
    {
        using var sha = SHA256.Create();
        var sb = new StringBuilder();
        AppendFileKey(sb, metallicPath);
        sb.Append("mVal:").Append(metallicFloat.ToString("F3")).Append(";");
        AppendFileKey(sb, smoothnessPath);
        sb.Append("sVal:").Append(smoothnessFloat.ToString("F3")).Append(";");

        byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
        hash = Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();

        string compositesDir = Path.Combine(cacheDirectory, "Composites");
        string finalPath = Path.Combine(compositesDir, "packed_" + hash + ".png");

        if (File.Exists(finalPath)) return finalPath;

        lock (CompositeLock)
        {
            if (File.Exists(finalPath)) return finalPath;
            Directory.CreateDirectory(compositesDir);

            string tmpPath = Path.Combine(compositesDir, "packed_" + hash + "_" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                Bitmap2D metallicBmp = (!string.IsNullOrEmpty(metallicPath) && File.Exists(metallicPath))
                    ? LoadBitmap(metallicPath) : null;
                Bitmap2D smoothnessBmp = (!string.IsNullOrEmpty(smoothnessPath) && File.Exists(smoothnessPath))
                    ? LoadBitmap(smoothnessPath) : null;

                if (metallicBmp == null && smoothnessBmp == null) return null;

                var targetSize = metallicBmp?.Size ?? smoothnessBmp.Size;
                var baseBmp = (metallicBmp != null)
                    ? ((metallicBmp.Size == targetSize) ? metallicBmp : metallicBmp.GetRescaled(targetSize))
                    : ((smoothnessBmp.Size == targetSize) ? smoothnessBmp : smoothnessBmp.GetRescaled(targetSize));

                var rescaledSmooth = (smoothnessBmp != null)
                    ? ((smoothnessBmp.Size == targetSize) ? smoothnessBmp : smoothnessBmp.GetRescaled(targetSize))
                    : null;

                int w = targetSize.x;
                int h = targetSize.y;

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        float m = (metallicBmp != null) ? baseBmp[x, y].r : metallicFloat;
                        float s = (rescaledSmooth != null) ? rescaledSmooth[x, y].r : smoothnessFloat;
                        baseBmp[x, y] = new color(m, m, m, s);
                    }
                }

                baseBmp.Save(tmpPath, -1, true);

                try
                {
                    File.Move(tmpPath, finalPath, overwrite: true);
                }
                catch (IOException)
                {
                    if (!File.Exists(finalPath)) throw;
                }

                return finalPath;
            }
            catch (Exception ex)
            {
                UnityPackageImporter.Warn("Failed to pack metallic/glossiness map: " + ex.Message);
                return null;
            }
            finally
            {
                if (File.Exists(tmpPath))
                {
                    try { File.Delete(tmpPath); } catch { }
                }
            }
        }
    }
}
