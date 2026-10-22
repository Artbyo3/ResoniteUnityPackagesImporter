using UnityPackageImporter.Models;

internal static class ImportReportPanelTests
{
    internal static void Register(Action<string, Action> test)
    {
        test("Issue draft uses the fixed repository and encodes the complete safe report", () =>
        {
            var data = StructuredImportReportTests.Sample();
            var draft = ImportIssueDraft.Build(data, new ReportSanitizer());
            Check(draft.IssueUrl.GetLeftPart(UriPartial.Path) == ImportIssueDraft.NewIssueUrl, "Issue target changed.");
            Check(draft.FullReportIncluded && Body(draft).Contains(draft.Report), "Short report was not prefilled.");
            Check(Body(draft).Contains("before submitting") && !draft.IssueUrl.Query.Contains("submit="), "Draft must remain reviewable.");
            Check(draft.Report == ImportFailureReport.Build(data, new ReportSanitizer()), "Displayed/copied report must equal the saved report.");
        });
        test("Long issue draft stays bounded and requests the full copied report", () =>
        {
            var sample = StructuredImportReportTests.Sample();
            var data = sample with { IncludeNames = true, Result = sample.Result! with {
                OperationDetails = Enumerable.Repeat(sample.Result!.OperationDetails[0] with { Name = new string('x', 400) }, 24).ToArray() } };
            var draft = ImportIssueDraft.Build(data, new ReportSanitizer());
            Check(!draft.FullReportIncluded && draft.IssueUrl.AbsoluteUri.Length <= ImportIssueDraft.MaximumUrlCharacters, "Long URL was not bounded.");
            Check(Body(draft).Contains("Copy report") && Body(draft).Contains("paste it below") && Body(draft).Contains("ReconstructionFailed"), "Long-report guidance or typed summary missing.");
            Check(draft.Report.Length > 6000, "Full clipboard report was silently shortened.");
        });
        test("Issue payload and panel retain personal-data redaction with opt-in names", () =>
        {
            var sample = StructuredImportReportTests.Sample();
            var data = sample with { IncludeNames = true, Result = sample.Result! with { Name = "sample-person sample-host sample@example.invalid https://example.invalid/private" } };
            var draft = ImportIssueDraft.Build(data, new ReportSanitizer("sample-person", "sample-host"));
            foreach (string secret in new[] { "sample-person", "sample-host", "sample@example.invalid", "https://example.invalid/private" })
                Check(!draft.Report.Contains(secret) && !Body(draft).Contains(secret), "A draft bypassed report redaction.");
        });
        test("Unicode and issue-query characters remain data rather than URL parameters", () =>
        {
            var sample = StructuredImportReportTests.Sample();
            var draft = ImportIssueDraft.Build(sample with { IncludeNames = true,
                Result = sample.Result! with { Name = "synthetic &title=other#fragment 日本語" } }, new ReportSanitizer());
            Check(Body(draft).Contains("&title=other#fragment 日本語"), "Unicode/query characters were corrupted.");
            Check(draft.IssueUrl.Fragment == "" && draft.IssueUrl.Query.Split('&').Length == 2, "Report text escaped into URL parameters.");
        });
        test("Report names cannot close the issue's diagnostic code fence", () =>
        {
            var sample = StructuredImportReportTests.Sample();
            var draft = ImportIssueDraft.Build(sample with { IncludeNames = true, Result = sample.Result! with { Name = "````synthetic" } }, new ReportSanitizer());
            Check(draft.FullReportIncluded && Body(draft).Contains("`````text\n"), "Diagnostic fence did not preserve text as data.");
        });
        test("Unusual version strings cannot create an oversized issue link", () =>
        {
            var sample = StructuredImportReportTests.Sample();
            var data = sample with { IncludeNames = true, Environment = sample.Environment with {
                ModVersion = new string('界', 500), ResoniteVersion = new string('界', 500) } };
            var draft = ImportIssueDraft.Build(data, new ReportSanitizer());
            Check(!draft.FullReportIncluded && draft.IssueUrl.AbsoluteUri.Length <= ImportIssueDraft.MaximumUrlCharacters && Body(draft).Contains("Copy report"), "Minimal draft fallback failed.");
        });
        test("Early failures still produce an actionable issue draft without a result", () =>
        {
            var data = StructuredImportReportTests.Sample() with { Result = null, Stage = ImportReportStage.Extraction };
            var draft = ImportIssueDraft.Build(data, new ReportSanitizer());
            Check(draft.Report.Contains("Result snapshot\nUnavailable") && Uri.UnescapeDataString(draft.IssueUrl.Query).Contains("Extraction"), "Early failure draft missing.");
        });
        test("Out-of-order report completion cannot replace a newer panel report", () =>
        {
            var inbox = new ImportReportInbox(); var first = inbox.BeginReport(); var second = inbox.BeginReport();
            var oldReport = Draft("old"); var newReport = Draft("new");
            Check(inbox.Complete(second, newReport) && !inbox.Complete(first, oldReport) && inbox.Current == newReport, "Stale report replaced the current one.");
        });
        test("Closing a panel suppresses even in-flight reports from the same failure batch", () =>
        {
            var inbox = new ImportReportInbox(); long first = inbox.BeginReport(); long pending = inbox.BeginReport();
            inbox.Complete(first, Draft("first")); inbox.Dismiss();
            Check(!inbox.Complete(pending, Draft("late")) && inbox.Current == null, "Closing reopened a pending report.");
            long next = inbox.BeginReport();
            Check(inbox.Complete(next, Draft("next")) && inbox.Current!.Report == "next", "New failures must remain reportable.");
        });
        test("Report panel state is isolated between simultaneous imports", () =>
        {
            var first = new ImportReportInbox(); var second = new ImportReportInbox();
            first.Complete(first.BeginReport(), Draft("first")); second.Complete(second.BeginReport(), Draft("second")); first.Dismiss();
            Check(first.Current == null && second.Current!.Report == "second", "A close crossed import sessions.");
        });
        test("Concurrent completion converges on the newest requested report", () =>
        {
            var inbox = new ImportReportInbox(); var revisions = Enumerable.Range(0, 40).Select(_ => inbox.BeginReport()).ToArray();
            Parallel.ForEach(revisions, revision => inbox.Complete(revision, Draft(revision.ToString())));
            Check(inbox.Current!.Report == revisions.Max().ToString(), "Concurrent reports did not converge.");
        });
        test("Duplicate and unrequested panel completions are ignored", () =>
        {
            var inbox = new ImportReportInbox(); long revision = inbox.BeginReport();
            Check(!inbox.Complete(revision + 1, Draft("unrequested")) && inbox.Complete(revision, Draft("first")) &&
                !inbox.Complete(revision, Draft("duplicate")) && inbox.Current!.Report == "first", "Invalid revision accepted.");
        });
    }

    private static ImportReportPresentation Draft(string text) => new(text, new Uri(ImportIssueDraft.NewIssueUrl), true);
    private static string Body(ImportReportPresentation draft) => Uri.UnescapeDataString(draft.IssueUrl.Query.Split("&body=", 2)[1]);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
