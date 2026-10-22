using UnityPackageImporter.Models;

internal static class ImportFailureReportTests
{
    internal static void Register(Action<string, Action> test)
    {
        test("Failure report preserves typed stage and causes while withholding free text", () =>
        {
            var cause = new IOException(@"Could not read C:\Users\TestUser\AppData\Local\avatar.mat");
            var error = new InvalidOperationException("Material conversion failed", cause);
            var report = ImportFailureReport.Build("Clothing install", error, DateTimeOffset.Parse("2026-10-13T08:30:00Z"));

            Check(report.Contains("Stage: InstallOutfit", StringComparison.Ordinal), "The failed stage must be recorded.");
            Check(report.Contains("Error: InvalidOperationException", StringComparison.Ordinal), "The outer failure type must be recorded.");
            Check(report.Contains("Cause: IOException", StringComparison.Ordinal), "The inner failure type must be recorded.");
            Check(!report.Contains("Material conversion failed", StringComparison.Ordinal), "Unknown free text must not disclose asset names.");
            Check(!report.Contains(@"C:\Users\TestUser", StringComparison.OrdinalIgnoreCase), "The report must not include local account paths.");

            var forwardSlash = ImportFailureReport.Sanitize("Error at C:/Users/TestUser/AppData/Local/mesh.fbx");
            Check(forwardSlash == "Error at <local path>", "Forward slash Windows path must be sanitized.");

            var unixPath = ImportFailureReport.Sanitize("Failed opening /home/testuser/project/model.fbx");
            Check(unixPath == "Failed opening <local path>", "Unix home path must be sanitized.");

            var fileUri = ImportFailureReport.Sanitize("Loaded file:///C:/Users/testuser/package.unitypackage");
            Check(fileUri == "Loaded <local path>", "File URI path must be sanitized.");
        });
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
