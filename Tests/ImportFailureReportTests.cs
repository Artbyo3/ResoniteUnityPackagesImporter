using UnityPackageImporter.Models;

internal static class ImportFailureReportTests
{
    internal static void Register(Action<string, Action> test)
    {
        test("Failure report includes stage and inner cause while removing local paths", () =>
        {
            var cause = new IOException(@"Could not read C:\Users\TestUser\AppData\Local\avatar.mat");
            var error = new InvalidOperationException("Material conversion failed", cause);
            var report = ImportFailureReport.Build("Clothing install", error, DateTimeOffset.Parse("2026-10-13T08:30:00Z"));

            Check(report.Contains("Stage: Clothing install", StringComparison.Ordinal), "The failed stage must be recorded.");
            Check(report.Contains("InvalidOperationException: Material conversion failed", StringComparison.Ordinal), "The outer failure must be recorded.");
            Check(report.Contains("IOException: Could not read <local path>", StringComparison.Ordinal), "The inner cause should be useful without leaking a local path.");
            Check(!report.Contains(@"C:\Users\TestUser", StringComparison.OrdinalIgnoreCase), "The report must not include local account paths.");
        });
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
