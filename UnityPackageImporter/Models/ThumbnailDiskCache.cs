#nullable enable
using System;
using System.IO;
using System.Linq;

namespace UnityPackageImporter.Models;

internal static class ThumbnailDiskCache
{
    internal const long MaximumBytes = 200L * 1024 * 1024;
    internal static readonly TimeSpan MaximumAge = TimeSpan.FromDays(14);
    private static readonly Lazy<string> DirectoryPath = new(Initialize);

    public static string Directory => DirectoryPath.Value;

    private static string Initialize()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ResoniteMaterialThumbs");
        System.IO.Directory.CreateDirectory(directory);
        Cleanup(directory, MaximumBytes, DateTime.UtcNow - MaximumAge, ReportFailure);
        return directory;
    }

    internal static void Cleanup(string directory, long maximumBytes, DateTime oldestAllowedUtc, Action<string> reportFailure)
    {
        if (maximumBytes < 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        var files = new DirectoryInfo(directory).EnumerateFiles()
            .Where(file => file.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                           file.Extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.LastWriteTimeUtc)
            .ThenBy(file => file.Name, StringComparer.Ordinal)
            .ToArray();
        long total = files.Sum(file => file.Length);
        foreach (var file in files)
        {
            if (file.LastWriteTimeUtc >= oldestAllowedUtc && total <= maximumBytes) continue;
            try
            {
                // Opening exclusively and deleting on close is atomic with respect to
                // other readers/writers, even ones that permit FileShare.Delete.
                // Cleanup runs before this process returns its first thumbnail path.
                using (var handle = new FileStream(file.FullName, FileMode.Open, FileAccess.Read,
                           FileShare.None, 1, FileOptions.DeleteOnClose))
                    total -= handle.Length;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A busy/inaccessible entry must survive; report the failed maintenance
                // operation and leave its bytes in the budget.
                reportFailure($"Thumbnail cache retained '{file.Name}': {exception.Message}");
            }
        }
    }

    private static void ReportFailure(string message)
    {
#if !REGRESSION_TESTS
        UnityPackageImporter.Warn(message);
#else
        System.Diagnostics.Trace.TraceWarning(message);
#endif
    }
}
