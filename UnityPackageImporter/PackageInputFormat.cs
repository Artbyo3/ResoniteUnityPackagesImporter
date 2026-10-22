#nullable enable
using System;
using System.IO;
using System.Text;

namespace UnityPackageImporter.Extractor;

public enum PackageInputKind { Gzip, Zip, SevenZip, Rar, PlainTar, WebPage, Text, Empty, Unknown }
public enum PackageInputFailure { Zip, SevenZip, Rar, PlainTar, WebPage, Text, Empty, Unknown, Corrupted }

internal static class PackageInputFormat
{
    private const string FailureKey = "UnityPackageImporter.InputFailure";
    internal const int HeaderBytes = 512;

    internal static PackageInputKind Detect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return PackageInputKind.Empty;
        if (bytes.StartsWith(new byte[] { 0x1f, 0x8b })) return PackageInputKind.Gzip;
        if (bytes.StartsWith("PK\x03\x04"u8) || bytes.StartsWith("PK\x05\x06"u8) || bytes.StartsWith("PK\x07\x08"u8)) return PackageInputKind.Zip;
        if (bytes.StartsWith(new byte[] { 0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c })) return PackageInputKind.SevenZip;
        if (bytes.StartsWith(new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1a, 0x07 })) return PackageInputKind.Rar;
        if (IsTarHeader(bytes)) return PackageInputKind.PlainTar;
        string prefix = bytes.StartsWith(new byte[] { 0xff, 0xfe }) ? Encoding.Unicode.GetString(bytes[2..]) :
            bytes.StartsWith(new byte[] { 0xfe, 0xff }) ? Encoding.BigEndianUnicode.GetString(bytes[2..]) : Encoding.UTF8.GetString(bytes);
        prefix = prefix.TrimStart('\ufeff', ' ', '\t', '\r', '\n');
        if (prefix.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase) || prefix.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
            return PackageInputKind.WebPage;
        int printable = 0;
        foreach (byte value in bytes) if (value is >= 32 and <= 126 or 9 or 10 or 13) printable++;
        return printable >= bytes.Length * 0.9 ? PackageInputKind.Text : PackageInputKind.Unknown;
    }

    private static bool IsTarHeader(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 512) return false;
        int checksum = 0, expected = 0;
        bool digit = false;
        for (int i = 148; i < 156; i++)
        {
            byte value = bytes[i];
            if (value is >= (byte)'0' and <= (byte)'7') { expected = expected * 8 + value - '0'; digit = true; }
            else if (value != 0 && value != ' ') return false;
        }
        for (int i = 0; i < 512; i++) checksum += i is >= 148 and < 156 ? 32 : bytes[i];
        return digit && expected == checksum;
    }

    internal static PackageInputKind ReadHeader(Stream file)
    {
        byte[] header = new byte[HeaderBytes];
        int count = file.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        file.Position = 0;
        return Detect(header.AsSpan(0, count));
    }

    internal static InvalidDataException Error(PackageInputFailure failure, Exception? inner = null)
    {
        var error = new InvalidDataException(Message(failure), inner);
        error.Data[FailureKey] = failure;
        return error;
    }

    internal static PackageInputFailure? Failure(Exception error) => error.Data[FailureKey] is PackageInputFailure value ? value : null;

    internal static string Message(PackageInputFailure failure) => failure switch
    {
        PackageInputFailure.Zip => "The input is a ZIP archive. Extract it first and import the contained .unitypackage file.",
        PackageInputFailure.SevenZip => "The input is a 7z archive. Extract it first and import the contained .unitypackage file.",
        PackageInputFailure.Rar => "The input is a RAR archive. Extract it first and import the contained .unitypackage file.",
        PackageInputFailure.PlainTar => "The input is an uncompressed tar archive. Import the original gzip-compressed .unitypackage file.",
        PackageInputFailure.WebPage => "The input is a web page; the download probably failed. Download the actual .unitypackage file again.",
        PackageInputFailure.Text => "The input is a text file. Download the actual .unitypackage file again.",
        PackageInputFailure.Empty => "The input file is empty. Download the .unitypackage file again.",
        PackageInputFailure.Corrupted => "The package appears corrupted or incomplete. Download the .unitypackage file again.",
        _ => "The input format is unknown. Import an original gzip-compressed .unitypackage file."
    };

    internal static void RequireGzip(PackageInputKind kind)
    {
        if (kind == PackageInputKind.Gzip) return;
        throw Error(kind switch
        {
            PackageInputKind.Zip => PackageInputFailure.Zip, PackageInputKind.SevenZip => PackageInputFailure.SevenZip,
            PackageInputKind.Rar => PackageInputFailure.Rar, PackageInputKind.PlainTar => PackageInputFailure.PlainTar,
            PackageInputKind.WebPage => PackageInputFailure.WebPage, PackageInputKind.Text => PackageInputFailure.Text,
            PackageInputKind.Empty => PackageInputFailure.Empty, _ => PackageInputFailure.Unknown
        });
    }
}
