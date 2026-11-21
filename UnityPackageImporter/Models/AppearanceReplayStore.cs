#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace UnityPackageImporter.Models;

internal static class AppearanceReplayStore
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly SemaphoreSlim Admission = new(4, 4);
    private static readonly Regex Name = new(@"\Aappearance-replay-[a-f0-9]{32}\.json\z", RegexOptions.CultureInvariant);

    internal static async Task<string> WriteAsync(string directory, InitialAppearanceReplay replay, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        replay.ValidateInputBudget();
        return await WithWriterAsync(async () =>
        {
            string? temporary = null;
            try
            {
                directory = Path.GetFullPath(directory);
                RejectLinks(directory);
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "appearance-replay-" + Guid.NewGuid().ToString("N") + ".json");
                temporary = path + ".tmp";
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
                {
                    await using var bounded = new AppearanceReplayByteStream(stream, InitialAppearanceReplay.MaximumBytes);
                    await JsonSerializer.SerializeAsync(bounded, replay, InitialAppearanceReplay.JsonOptions, token).ConfigureAwait(false);
                }
                token.ThrowIfCancellationRequested();
                File.Move(temporary, path);
                temporary = null;
                foreach (string old in Directory.EnumerateFiles(directory, "appearance-replay-*.json")
                    .Where(file => file != path && Name.IsMatch(Path.GetFileName(file))).OrderByDescending(File.GetLastWriteTimeUtc).ThenBy(file => file, StringComparer.Ordinal).Skip(7))
                {
                    RejectLinks(old);
                    File.Delete(old);
                }
                return path;
            }
            finally
            {
                if (temporary != null && File.Exists(temporary)) File.Delete(temporary);
            }
        }, token).ConfigureAwait(false);
    }

    internal static async Task<string> WithWriterAsync(Func<Task<string>> write, CancellationToken token)
    {
        if (!await Admission.WaitAsync(0, token).ConfigureAwait(false)) throw new IOException("Local replay capture admission is full.");
        bool acquired = false;
        try
        {
            await Gate.WaitAsync(token).ConfigureAwait(false);
            acquired = true;
            token.ThrowIfCancellationRequested();
            return await write().ConfigureAwait(false);
        }
        finally
        {
            if (acquired) Gate.Release();
            Admission.Release();
        }
    }

    private static void RejectLinks(string path)
    {
        for (string? current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The local replay directory contains a link or reparse point.");
    }
}
