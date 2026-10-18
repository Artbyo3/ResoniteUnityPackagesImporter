#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace UnityPackageImporter.Models;

internal static class ImportFailureReport
{
    private static readonly Regex LocalPath = new(
        @"(?i)(?:[a-z]:[\\/]|\\\\|file:///(?:[a-z]:[\\/])?|/(?:Users|home|tmp|var)/)[^\r\n""'<>|]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static string Build(string stage, Exception error, DateTimeOffset? time = null)
    {
        ArgumentNullException.ThrowIfNull(error);
        var errors = new List<Exception>();
        for (Exception? current = error; current != null && errors.Count < 8; current = current.InnerException)
            errors.Add(current);

        string details = string.Join("\n", errors.Select((exception, index) =>
            $"{(index == 0 ? "Error" : $"Caused by {index}")}: {exception.GetType().Name}: {Sanitize(exception.Message)}"));

        return string.Join("\n", new[]
        {
            "Unity Package Import Failure Report",
            "Time (UTC): " + (time ?? DateTimeOffset.UtcNow).ToUniversalTime().ToString("O"),
            "Stage: " + Sanitize(string.IsNullOrWhiteSpace(stage) ? "Unknown" : stage),
            details,
            "This report contains the failure stage and exception chain. It excludes local file paths and package contents."
        });
    }

    internal static string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return LocalPath.Replace(value, "<local path>").Trim();
    }
}
