using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace UnityPackageImporter.Models;

internal static class RgbaPng
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


    internal static byte[] Encode(byte[] pixels, int width, int height)
    {
        if (width <= 0 || height <= 0 || pixels.Length != checked(width * height * 4))
            throw new ArgumentException("RGBA buffer dimensions do not match.");
        int rowBytes = 1 + width * 4;
        byte[] rawScanlines = new byte[height * rowBytes];
        for (int y = 0; y < height; y++)
            Buffer.BlockCopy(pixels, y * width * 4, rawScanlines, y * rowBytes + 1, width * 4);

        byte[] compressedData;
        using (var memStream = new MemoryStream())
        {
            using (var zlib = new ZLibStream(memStream, CompressionLevel.Optimal, true))
            {
                zlib.Write(rawScanlines, 0, rawScanlines.Length);
            }
            compressedData = memStream.ToArray();
        }


        using var pngStream = new MemoryStream();

        pngStream.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });


        byte[] ihdrData = new byte[13];
        WriteBigEndianInt32(ihdrData, 0, width);
        WriteBigEndianInt32(ihdrData, 4, height);
        ihdrData[8] = 8;
        ihdrData[9] = 6;
        ihdrData[10] = 0;
        ihdrData[11] = 0;
        ihdrData[12] = 0;
        WritePngChunk(pngStream, "IHDR", ihdrData);


        WritePngChunk(pngStream, "IDAT", compressedData);


        WritePngChunk(pngStream, "IEND", Array.Empty<byte>());

        return pngStream.ToArray();
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
