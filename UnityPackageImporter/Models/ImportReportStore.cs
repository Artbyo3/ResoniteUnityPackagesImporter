#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UnityPackageImporter.Models;

internal static class ImportReportStore
{
    internal const int RetainedReports = 20;
    internal const string Prefix = "unity-import-report-";
    private static readonly SemaphoreSlim Gate = new(1, 1);

    internal static async Task<string> WriteAsync(string directory, string report, DateTimeOffset time)
    {
        byte[] bytes = new UTF8Encoding(false).GetBytes(report);
        if (bytes.Length > 128 * 1024) throw new InvalidDataException("Import report exceeds the storage limit.");
        await Gate.WaitAsync().ConfigureAwait(false);
        string? temporary = null;
        try
        {
            directory = Path.GetFullPath(directory);
            RejectLinks(directory);
            Directory.CreateDirectory(directory);
            string name = Prefix + time.ToUniversalTime().ToString("yyyyMMddTHHmmssfffffffZ", System.Globalization.CultureInfo.InvariantCulture)
                + "-" + Guid.NewGuid().ToString("N") + ".txt";
            string path = Path.Combine(directory, name);
            temporary = path + ".tmp";
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous))
                await stream.WriteAsync(bytes).ConfigureAwait(false);
            File.Move(temporary, path);
            temporary = null;
            foreach (string old in Directory.EnumerateFiles(directory, Prefix + "*.txt")
                .Where(IsReportFile).OrderByDescending(Path.GetFileName, StringComparer.Ordinal).Skip(RetainedReports))
            {
                RejectLinks(old);
                File.Delete(old);
            }
            return path;
        }
        finally
        {
            try { if (temporary != null && File.Exists(temporary)) File.Delete(temporary); }
            finally { Gate.Release(); }
        }
    }

    private static bool IsReportFile(string path) => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(path),
        @"\Aunity-import-report-[0-9]{8}T[0-9]{13}Z-[a-f0-9]{32}\.txt\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static void RejectLinks(string path)
    {
        for (string? current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The report directory contains a link or reparse point.");
    }

    // Reporting has its own observed failure boundary; it must not replace an import error.
    internal static async Task<bool> SaveBestEffortAsync(Func<Task> write, Action<string> failed)
    {
        try { await write().ConfigureAwait(false); return true; }
        catch (Exception error)
        {
            failed(error.GetType().Name);
            return false;
        }
    }
}
