# Local import reports

The importer writes an English text report when an import or station action fails,
or when a staged item has a structurally blocked result. Closing a station or
cancelling an import does not create a failure report. Ordinary compatibility
warnings alone do not create one. Nothing is uploaded automatically.

Reports are stored in `reports/UnityPackageImporter`, beside the existing
`DecompressedUnityPrefabs` cache folder. Only the newest 20 reports are retained.
The failure card still shows its existing error text. A separate error report panel
opens in private userspace, using the prepared native UI template. No report text
is added to the shared world or saved in a persistent world slot.

## Error report panel

The panel displays the same filtered report that is saved locally. Copy report
uses the native clipboard action. Open GitHub issue uses the native hyperlink
confirmation and opens a draft in the repository's issue editor; it never submits
an issue automatically. Review the draft before submitting.

After supplying report data and the issue URL, the controller sets the template's
`actionsReady.Value` input. Native drivers own the enabled states of Copy report,
Open GitHub issue, and the hyperlink; the controller does not write those driven fields.

Short reports are prefilled in full. For reports too long for the link's 6,000
character budget, the draft contains a compact summary and asks you to paste the
full report using Copy report. The panel and clipboard keep the complete report.

One import reuses one panel. Late report completion cannot replace a newer report
or reopen a panel dismissed while those reports were being built. A later, distinct
failure can open it again. Close is owned by the native template and does not
close the station or cancel an import. The panel can outlive its source station.

UI labels use the existing translations and update when the locale changes. The
diagnostic report and generated issue body remain English. The template owns its
appearance, scrolling, hover feedback and Close behavior. Its shared assets remain
deduplicated with the station bundle.

If private userspace or the panel is unavailable, the local file and existing
failure card still provide the report. Public extraction calls that have no UI
session continue to save a local report without spawning a panel.

## Contents and privacy

Report format 2 includes versions, OS family, UTC date, locale, a random token for
each report, bounded exception types and meaningful caller names, size ranges and
category counts, and a result snapshot when available. Exact event times stay in
local file metadata. Result snapshots include readiness, issues, failed object
types/classes and operation states/codes.
Warnings use structured categories and diagnostic codes, rather than raw log
lines. Source failures and warnings are labelled as session-wide; they can concern
other items from the same import.

Package, asset and object names are excluded by default. `IncludeNamesInReports`
in the mod configuration enables explicitly collected names. Personal-data
redaction remains active in either mode: local paths, account and machine names,
home folders, email addresses and URLs are removed from all output. Unknown
exception messages are classified into fixed technical explanations where the
cause is recognized. Unknown raw messages stay withheld because they may contain
arbitrary asset names. Input-format errors retain their actionable English text.

Content hashes, original asset GUIDs, original object IDs and exact file sizes are
excluded. Objects and models receive report-local labels so references can still
be followed. Source scopes distinguish file-local IDs without revealing paths.
File sizes use ranges instead of exact fingerprints. Reports have independent
tokens, so the import's session ID is not exposed across reports.

Model failures identify the anonymous model, import phase, format/header version,
native library version, configured post-processing steps and classified cause.
Async stack frames retain logical caller/class names and omit runtime throw/await
machinery, parameters and source locations. Header inspection is bounded and only
typed facts enter the report. Parser offsets and arbitrary data values are omitted.

Reports contain no package contents, full stack traces or raw mod logs. Opting into
names deliberately identifies content; leave that option off when sharing an
anonymous report. Technical characteristics can still correlate similar failures,
so review the report before sharing it.

Package facts read only the input header; reporting no longer hashes whole files.
Missing, changed, locked or unreadable input files are marked
explicitly. Missing native snapshots and cached extraction entry counts remain
unknown. Detail samples, report size and reports per session are bounded. Saving
a report is best effort and does not replace the original failure.

## Input errors

The extractor checks the input header before opening gzip data. ZIP, 7z and RAR
files must be extracted first; import the contained `.unitypackage`. Web pages,
text, empty files, uncompressed tar and unknown formats receive specific guidance.
Malformed or truncated gzip/tar data is reported as corrupted or incomplete.
Extraction safety limits, access errors and cancellation retain their own errors.

## Developer checks

The engine-independent formatter, context, file store and input classifier are
linked into the regression tests. Fixtures are small synthetic archives and typed
data. To print the sample report:

```sh
dotnet run --project Tests/Importer.RegressionTests.csproj -c Release -- --sample-import-report
dotnet run --project Tests/Importer.RegressionTests.csproj -c Release -- --sample-model-report
```

The report service captures native values on the world context, then reads bounded
file facts and builds immutable reports off-thread. The panel receives them on its
private world's context. No browser or clipboard action runs until a user clicks.
