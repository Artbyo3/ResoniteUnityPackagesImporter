#nullable enable
using System;
using System.Linq;

namespace UnityPackageImporter.Models;

internal sealed record ImportReportPresentation(string Report, Uri IssueUrl, bool FullReportIncluded);

// Builds a reviewable draft, never submits an issue or opens a browser.
internal static class ImportIssueDraft
{
    internal const string NewIssueUrl = "https://github.com/Artbyo3/ResoniteUnityPackagesImporter/issues/new";
    internal const int MaximumUrlCharacters = 6000;

    internal static ImportReportPresentation Build(ImportReportData data, ReportSanitizer sanitizer)
    {
        string report = ImportFailureReport.Build(data, sanitizer);
        string title = "Import failed during " + data.Stage;
        string prefix = NewIssueUrl + "?title=" + Uri.EscapeDataString(title) + "&body=";
        string introduction = "Please describe what you were doing and review this report before submitting. Do not attach source packages.\n\n";
        int longest = 0, run = 0;
        foreach (char c in report) { run = c == '`' ? run + 1 : 0; longest = Math.Max(longest, run); }
        string fence = new('`', Math.Max(3, longest + 1));
        string complete = prefix + Uri.EscapeDataString(introduction + fence + "text\n" + report + "\n" + fence);
        if (complete.Length <= MaximumUrlCharacters)
            return new(report, new Uri(complete), true);

        // Do not silently truncate a diagnostic report to fit a browser URL.
        string summary = "The full diagnostic report is too long for this link. In Resonite, use Copy report, then paste it below before submitting.\n\n" +
            "Report format: " + ImportFailureReport.FormatVersion + "\nMod version: " + sanitizer.Clean(data.Environment.ModVersion) +
            "\nResonite version: " + sanitizer.Clean(data.Environment.ResoniteVersion) +
            "\nStage: " + data.Stage + "\nReport token: " + (data.ReportId == Guid.Empty ? "unavailable" : data.ReportId.ToString("N")) +
            (data.Result == null ? "\nResult snapshot: unavailable" :
                "\nStructure ready: " + data.Result.StructureReady + "\nIssues: " + string.Join(", ", data.Result.Issues.Take(32))) +
            (data.ModelFailures?.FirstOrDefault() is { } model ?
                "\nModel failure: " + model.Facts.Format + "; " + model.Error.Reason + "; " + ReportFailureDetails.Describe(model.Error.Reason) : "") +
            "\n\n<!-- Paste the copied report here. Review it for personal information first. -->";
        string compact = prefix + Uri.EscapeDataString(introduction + summary);
        if (compact.Length > MaximumUrlCharacters)
        {
            // Even unusual version strings cannot make the link unbounded.
            compact = prefix + Uri.EscapeDataString("Use Copy report in Resonite and paste the diagnostic report here. Review it before submitting.");
        }
        return new(report, new Uri(compact), false);
    }
}
