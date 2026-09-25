using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace UnityPackageImporter.Extractor;

// Independent of the engine so archives can be tested without launching Resonite.
public static class UnityPackageExtractor
{
    private const string CompletionFile = ".unitypackage-complete-v2";
    private static readonly object ExtractionLock = new();
    public const long DefaultMaxBytes = 8L * 1024 * 1024 * 1024;
    public const int DefaultMaxEntries = 100000;

    public static List<string> Unpack(string input, string outputDir,
        long maxBytes = DefaultMaxBytes, int maxEntries = DefaultMaxEntries,
        CancellationToken cancellationToken = default)
    {
        if (maxBytes <= 0 || maxEntries <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        // Serialize publication, including simultaneous imports of the same package.
        lock (ExtractionLock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            outputDir = Path.GetFullPath(outputDir);
            RejectReparsePoints(outputDir);
            if (TryReadCache(outputDir, out var cached)) return cached;

            var parent = Path.GetDirectoryName(outputDir)
                ?? throw new ArgumentException("A cache directory must have a parent.", nameof(outputDir));
            Directory.CreateDirectory(parent);
            var staging = Path.Combine(parent, ".unitypackage-" + Guid.NewGuid().ToString("N"));
            var raw = Path.Combine(staging, "raw");
            var content = Path.Combine(staging, "content");
            Directory.CreateDirectory(raw);
            Directory.CreateDirectory(content);
            try
            {
                ExtractArchive(input, raw, maxBytes, maxEntries, cancellationToken);
                var relativeFiles = new List<string>();
                var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var dir in Directory.GetDirectories(raw))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var asset = Path.Combine(dir, "asset");
                    var pathname = Path.Combine(dir, "pathname");
                    // Unity also serializes folders, which have no asset payload.
                    if (!File.Exists(asset)) continue;
                    if (!File.Exists(pathname)) throw new InvalidDataException("An asset has no pathname.");
                    if (new FileInfo(pathname).Length > 32768)
                        throw new InvalidDataException("Package asset pathname is too long.");
                    var relative = File.ReadAllText(pathname).TrimEnd('\0', '\r', '\n');
                    var destination = AssetPath(content, relative);
                    relative = Path.GetRelativePath(content, destination);
                    CopyAsset(asset, destination, relative, relativeFiles, destinations);
                    var meta = Path.Combine(dir, "asset.meta");
                    // Cache contents must not depend on current import options.
                    if (File.Exists(meta))
                        CopyAsset(meta, destination + ".meta", relative + ".meta", relativeFiles, destinations);
                }

                File.WriteAllLines(Path.Combine(content, CompletionFile), relativeFiles.Select(path =>
                    new FileInfo(Path.Combine(content, path)).Length + "\t" + path));
                cancellationToken.ThrowIfCancellationRequested();
                // Never publish a partially extracted cache.
                if (Directory.Exists(outputDir))
                {
                    RejectReparsePoints(outputDir);
                    Directory.Delete(outputDir, true);
                }
                Directory.Move(content, outputDir);
                return relativeFiles.Select(path => Path.Combine(outputDir, path)).ToList();
            }
            finally
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
            }
        }
    }

    private static bool TryReadCache(string root, out List<string> files)
    {
        files = new List<string>();
        var marker = Path.Combine(root, CompletionFile);
        if (!File.Exists(marker)) return false;
        RejectReparsePoints(marker);
        foreach (var line in File.ReadLines(marker))
        {
            var split = line.IndexOf('\t');
            if (split < 1 || !long.TryParse(line[..split], out var length)) return false;
            string path;
            try { path = AssetPath(root, line[(split + 1)..]); }
            catch (InvalidDataException) { return false; }
            RejectReparsePoints(path);
            if (!File.Exists(path) || new FileInfo(path).Length != length) return false;
            files.Add(path);
        }
        return true;
    }

    private static void CopyAsset(string source, string destination, string relative,
        List<string> files, HashSet<string> destinations)
    {
        if (!destinations.Add(destination))
            throw new InvalidDataException($"Duplicate asset destination: {relative}");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, false);
        files.Add(relative);
    }

    private static void ExtractArchive(string input, string raw, long maxBytes, int maxEntries,
        CancellationToken cancellationToken)
    {
        using var file = File.OpenRead(input);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var bounded = new BoundedReadStream(gzip, maxBytes, cancellationToken);
        using var reader = new TarReader(bounded);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int count = 0;
        while (reader.GetNextEntry() is { } entry)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++count > maxEntries) throw new InvalidDataException("Package has too many archive entries.");
            if (entry.EntryType == TarEntryType.Directory && (entry.Name == "./" || entry.Name == ".")) continue;
            var destination = SafePath(raw, entry.Name);
            if (!paths.Add(destination)) throw new InvalidDataException($"Duplicate archive entry: {entry.Name}");
            switch (entry.EntryType)
            {
                case TarEntryType.Directory:
                    Directory.CreateDirectory(destination);
                    break;
                case TarEntryType.RegularFile:
                case TarEntryType.V7RegularFile:
                    if (entry.Length > maxBytes) throw new InvalidDataException("Package entry exceeds the extraction limit.");
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write))
                        entry.DataStream?.CopyTo(output);
                    break;
                default:
                    throw new InvalidDataException($"Unsupported archive entry type: {entry.EntryType}");
            }
        }
    }

    private static string AssetPath(string root, string relative)
    {
        var normalized = relative.Replace('\\', '/');
        if (!normalized.StartsWith("Assets/", StringComparison.Ordinal) &&
            !normalized.StartsWith("Packages/", StringComparison.Ordinal))
            throw new InvalidDataException($"Asset path must be inside Assets or Packages: {relative}");
        return SafePath(root, normalized);
    }

    private static string SafePath(string root, string relative)
    {
        var normalized = relative.Replace('\\', '/');
        if (normalized.StartsWith('/') || Path.IsPathRooted(normalized))
            throw new InvalidDataException($"Absolute package path: {relative}");
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(p => p != ".").ToArray();
        if (parts.Length == 0 || parts.Any(p => p == ".." || p.EndsWith('.') || p.EndsWith(' ') ||
            p.Any(c => char.IsControl(c) || "<>:\"|?*".Contains(c)) || IsDeviceName(p)))
            throw new InvalidDataException($"Invalid package path: {relative}");
        var destination = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        if (!destination.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Package path escapes extraction directory: {relative}");
        return destination;
    }

    private static bool IsDeviceName(string part)
    {
        var name = part.Split('.')[0].ToUpperInvariant();
        return name is "CON" or "PRN" or "AUX" or "NUL" ||
            (name.Length == 4 && (name.StartsWith("COM") || name.StartsWith("LPT")) && char.IsDigit(name[3]));
    }

    private static void RejectReparsePoints(string path)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Cache path contains a link or reparse point: {current}");
    }

    // Bound decompressed bytes (including headers) and allow cancellation during large files.
    private sealed class BoundedReadStream(Stream inner, long limit, CancellationToken cancellationToken) : Stream
    {
        private long read;
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = inner.Read(buffer[..(int)Math.Min(buffer.Length, Math.Max(1, limit - read))]);
            read += count;
            if (read > limit) throw new InvalidDataException("Package exceeds the decompressed size limit.");
            return count;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => read; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
