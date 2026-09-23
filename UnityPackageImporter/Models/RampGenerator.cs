using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace UnityPackageImporter.Models;

public struct RampColor
{
    public float R;
    public float G;
    public float B;
    public float A;

    public RampColor(float r, float g, float b, float a = 1f)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    public static readonly RampColor White = new RampColor(1f, 1f, 1f, 1f);
}

public static class RampGenerator
{
    private static readonly uint[] CrcTable = InitializeCrcTable();

    private static uint[] InitializeCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? (0xedb88320u ^ (c >> 1)) : (c >> 1);
            }
            table[i] = c;
        }
        return table;
    }

    private static uint UpdateCrc(uint crc, byte[] buffer, int offset, int length)
    {
        uint c = crc ^ 0xffffffffu;
        for (int i = 0; i < length; i++)
        {
            c = CrcTable[(c ^ buffer[offset + i]) & 0xff] ^ (c >> 8);
        }
        return c ^ 0xffffffffu;
    }

    /// <summary>
    /// Computes a unique deterministic hash for caching generated ramp textures.
    /// </summary>
    public static string GetRampHash(RampColor baseColor, RampColor shadow1, float border1, float blur1, RampColor shadow2, float border2, float blur2)
    {
        string key = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{baseColor.R:F3},{baseColor.G:F3},{baseColor.B:F3}|{shadow1.R:F3},{shadow1.G:F3},{shadow1.B:F3}|{border1:F3}|{blur1:F3}|{shadow2.R:F3},{shadow2.G:F3},{shadow2.B:F3}|{border2:F3}|{blur2:F3}");

        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
        return BitConverter.ToString(hash, 0, 16).Replace("-", "").ToLowerInvariant();
    }

    /// <summary>
    /// Generates a valid PNG byte array representing the shadow ramp gradient.
    /// Default size: 256x4 (compact and fast to sample horizontally with clamp wrapping).
    /// </summary>
    public static byte[] GenerateShadowRampPng(
        RampColor baseColor,
        RampColor shadow1, float border1, float blur1,
        RampColor shadow2, float border2, float blur2,
        int width = 256, int height = 4)
    {
        if (width < 2) width = 2;
        if (height < 1) height = 1;

        // Generate 1 row of RGBA pixels
        byte[] rowPixels = new byte[width * 4];
        for (int x = 0; x < width; x++)
        {
            float u = (float)x / (width - 1);

            // Shadow 1 blend factor
            float half1 = blur1 * 0.5f;
            float w1;
            if (blur1 <= 0.001f)
            {
                w1 = u < border1 ? 0f : 1f;
            }
            else
            {
                w1 = Math.Clamp((u - (border1 - half1)) / blur1, 0f, 1f);
            }

            // Shadow 2 blend factor
            float half2 = blur2 * 0.5f;
            float w2;
            if (blur2 <= 0.001f)
            {
                w2 = u < border2 ? 0f : 1f;
            }
            else
            {
                w2 = Math.Clamp((u - (border2 - half2)) / blur2, 0f, 1f);
            }

            // Blend deep shadow (shadow2) into mid shadow (shadow1)
            float rSh = shadow2.R + (shadow1.R - shadow2.R) * w2;
            float gSh = shadow2.G + (shadow1.G - shadow2.G) * w2;
            float bSh = shadow2.B + (shadow1.B - shadow2.B) * w2;

            // Blend shadow into lit base color
            float r = rSh + (baseColor.R - rSh) * w1;
            float g = gSh + (baseColor.G - gSh) * w1;
            float b = bSh + (baseColor.B - bSh) * w1;

            int idx = x * 4;
            rowPixels[idx] = (byte)Math.Clamp((int)(r * 255f + 0.5f), 0, 255);
            rowPixels[idx + 1] = (byte)Math.Clamp((int)(g * 255f + 0.5f), 0, 255);
            rowPixels[idx + 2] = (byte)Math.Clamp((int)(b * 255f + 0.5f), 0, 255);
            rowPixels[idx + 3] = 255;
        }

        // Prepare uncompressed scanlines (filter 0 + RGBA per row)
        int rowBytes = 1 + width * 4;
        byte[] rawScanlines = new byte[height * rowBytes];
        for (int y = 0; y < height; y++)
        {
            int offset = y * rowBytes;
            rawScanlines[offset] = 0; // Filter byte: None
            Buffer.BlockCopy(rowPixels, 0, rawScanlines, offset + 1, width * 4);
        }

        // Compress scanlines with ZLib
        byte[] compressedData;
        using (var memStream = new MemoryStream())
        {
            using (var zlib = new ZLibStream(memStream, CompressionLevel.Optimal, true))
            {
                zlib.Write(rawScanlines, 0, rawScanlines.Length);
            }
            compressedData = memStream.ToArray();
        }

        // Build PNG
        using var pngStream = new MemoryStream();
        // PNG Signature
        pngStream.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        // IHDR chunk
        byte[] ihdrData = new byte[13];
        WriteBigEndianInt32(ihdrData, 0, width);
        WriteBigEndianInt32(ihdrData, 4, height);
        ihdrData[8] = 8; // Bit depth: 8
        ihdrData[9] = 6; // Color type: RGBA
        ihdrData[10] = 0; // Compression
        ihdrData[11] = 0; // Filter
        ihdrData[12] = 0; // Interlace
        WritePngChunk(pngStream, "IHDR", ihdrData);

        // IDAT chunk
        WritePngChunk(pngStream, "IDAT", compressedData);

        // IEND chunk
        WritePngChunk(pngStream, "IEND", Array.Empty<byte>());

        return pngStream.ToArray();
    }

    private static readonly object RampLock = new();

    /// <summary>
    /// Gets or creates a cached PNG file on disk for the specified shadow ramp parameters.
    /// Thread-safe and resilient against concurrent writes.
    /// </summary>
    public static string GetOrCreateRampFile(
        string cacheDirectory,
        RampColor baseColor,
        RampColor shadow1, float border1, float blur1,
        RampColor shadow2, float border2, float blur2)
    {
        string rampsDir = Path.Combine(cacheDirectory, "Ramps");
        string hash = GetRampHash(baseColor, shadow1, border1, blur1, shadow2, border2, blur2);
        string filePath = Path.Combine(rampsDir, $"ramp_{hash}.png");

        if (File.Exists(filePath)) return filePath;

        lock (RampLock)
        {
            if (File.Exists(filePath)) return filePath;

            Directory.CreateDirectory(rampsDir);
            byte[] pngBytes = GenerateShadowRampPng(baseColor, shadow1, border1, blur1, shadow2, border2, blur2);

            string tmpPath = Path.Combine(rampsDir, $"ramp_{hash}_{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllBytes(tmpPath, pngBytes);
                if (File.Exists(filePath))
                {
                    try { File.Delete(tmpPath); } catch { }
                    return filePath;
                }
                File.Move(tmpPath, filePath);
            }
            catch (IOException)
            {
                // Another thread or process created the file simultaneously
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
            }
        }

        return filePath;
    }

    private static void WritePngChunk(Stream stream, string type, byte[] data)
    {
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        byte[] lenBytes = new byte[4];
        WriteBigEndianInt32(lenBytes, 0, data.Length);
        stream.Write(lenBytes, 0, 4);
        stream.Write(typeBytes, 0, 4);
        if (data.Length > 0)
        {
            stream.Write(data, 0, data.Length);
        }

        uint crc = UpdateCrc(0, typeBytes, 0, 4);
        if (data.Length > 0)
        {
            crc = UpdateCrc(crc, data, 0, data.Length);
        }

        byte[] crcBytes = new byte[4];
        WriteBigEndianInt32(crcBytes, 0, (int)crc);
        stream.Write(crcBytes, 0, 4);
    }

    private static void WriteBigEndianInt32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)((value >> 24) & 0xff);
        buffer[offset + 1] = (byte)((value >> 16) & 0xff);
        buffer[offset + 2] = (byte)((value >> 8) & 0xff);
        buffer[offset + 3] = (byte)(value & 0xff);
    }
}
