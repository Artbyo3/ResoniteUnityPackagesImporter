using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FrooxEngine;
using UnityPackageImporter.FrooxEngineRepresentation;
using UnityPackageImporter.UI;
using UnityTypes = UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes;

namespace UnityPackageImporter.Models;


internal static class ImportReportService
{
    private static readonly ConditionalWeakTable<ImportReportContext, ImportErrorReportPanel> Panels = new();
    private static readonly Lazy<string> LoadedBuildHash = new(() => ImportActionDiagnostics.ReadBuildIdentity(() =>
    {
        using var file = File.OpenRead(typeof(ImportReportService).Assembly.Location);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file));
    }, type => UnityPackageImporter.Warn("Loaded mod hash unavailable for diagnostics: " + type)));
    internal static string LoadedBuildIdentity => LoadedBuildHash.Value;

    internal static async Task<string?> SaveAppearanceReplayAsync(InitialAppearanceReplay replay, CancellationToken token)
    {
        string? path = null;
        string directory = Path.Combine(Path.GetDirectoryName(UnityPackageImporter.cachePath), "diagnostics", "UnityPackageImporter", "appearance-replays");
        await ImportReportStore.SaveBestEffortAsync(async () =>
        {
            path = await AppearanceReplayStore.WriteAsync(directory, replay, token).ConfigureAwait(false);
        }, type => UnityPackageImporter.Warn("Could not save local appearance replay: " + type), token).ConfigureAwait(false);
        return path;
    }

    internal static string CaptureActionGateEvidence(ImportReportContext context, PreparedImportItem item,
        ImportResult result, ImportActionGate actionGate) => ImportActionDiagnostics.LocalEvidence(actionGate, result,
            item.ConversionPlan, LoadedBuildHash.Value, context.SessionId, item.DisplayName, item.SourcePath, item.FailedObjectIds);

    internal static void SaveActionGateEvidence(string evidence)
    {
        string directory = Path.Combine(Path.GetDirectoryName(UnityPackageImporter.cachePath), "diagnostics", "UnityPackageImporter", "action-gates");
        _ = Task.Run(() => ImportReportStore.SaveBestEffortAsync(async () =>
        {
            await ImportReportStore.WriteAsync(directory, evidence, DateTimeOffset.UtcNow, retainedReports: 64).ConfigureAwait(false);
        }, type => UnityPackageImporter.Warn("Could not save local action-gate evidence: " + type)));
    }


    internal static void EnablePanel(ImportReportContext context)
    {
        var privateWorld = Userspace.UserspaceWorld;
        if (privateWorld != null && !privateWorld.IsDestroyed)
            Panels.Add(context, new ImportErrorReportPanel(privateWorld));
    }

    internal static ImportReportContext Create(IEnumerable<string> files, string locale = null) => new(
        new(UnityPackageImporter.ReportModVersion, Engine.VersionNumber, EngineCompatibilityCheck.LastVerifiedResoniteVersion,
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ReportOsFamily.Windows :
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? ReportOsFamily.Linux :
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? ReportOsFamily.MacOS : ReportOsFamily.Other,
            locale ?? CultureInfo.CurrentUICulture.Name, LoadedBuildHash.Value), files,
        UnityPackageImporter.Config?.GetValue(UnityPackageImporter.IncludeNamesInReports) == true);

    internal static ReportObjectFailure Object(IUnityObject obj, ImportOwnership ownership = ImportOwnership.Unknown, Exception error = null, bool includeNames = false, string sourceKey = null)
    {
        if (!Enum.TryParse(obj.GetType().Name, out ReportObjectType kind)) kind = ReportObjectType.Other;
        string name = includeNames ? obj switch
        {
            UnityTypes.GameObject go => go.m_Name,
            UnityTypes.Transform transform => transform.parentHashedGameObj?.m_Name,
            _ => null
        } : null;
        return new(kind, YamlToFrooxEngine.ClassId(obj), unchecked((long)obj.id), name, ownership,
            Exceptions: error != null ? ImportFailureReport.CaptureExceptions(error, out _) : null, SourceKey: sourceKey);
    }

    internal static void ObjectFailed(ImportReportContext context, IUnityObject obj, Exception error, string sourceKey)
    {
        if (context == null) return;
        try { context.ObjectFailed(Object(obj, error: error, includeNames: context.IncludeNames, sourceKey: sourceKey)); }
        catch (Exception reportingError)
        {
            UnityPackageImporter.Warn("Could not capture local object failure diagnostics: " + reportingError.GetType().Name);
        }
    }

    internal static void ModelFailed(ImportReportContext context, string assetKey, string path, Exception error, string processing, ReportModelPhase phase)
    {
        if (context == null) return;

        string version = "unknown";
        try { version = Assimp.Unmanaged.AssimpLibrary.Instance.GetVersionAsVersion().ToString(); }
        catch (Exception metadataError)
        {
            UnityPackageImporter.Warn("Model library version unavailable for diagnostics: " + metadataError.GetType().Name);
        }
        try
        {
            var steps = processing.Split(',').Select(value => Enum.TryParse<ReportModelProcessing>(value.Trim(), out var step)
                ? (ReportModelProcessing?)step : null).Where(value => value.HasValue).Select(value => value.Value).ToArray();
            var captured = new ReportException(error.GetType().Name, Extractor.PackageInputFormat.Failure(error),
                ReportFailureDetails.Methods(error), ReportFailureDetails.Classify(error));
            context.ModelFailed(new(assetKey, ReportModelFactsReader.Read(path), version, steps, captured, phase));
        }
        catch (Exception reportingError)
        {
            UnityPackageImporter.Warn("Could not capture anonymous model diagnostics: " + reportingError.GetType().Name);
        }
    }

    internal static void Record(ImportReportContext context, object identity, Exception error = null,
        PreparedImportItem item = null, UnityProjectImporter project = null, ImportReportStage? stage = null,
        ImportActionGate actionGate = null)
    {
        try { RecordCore(context, identity, error, item, project, stage, actionGate); }
        catch (Exception reportingError)
        {
            UnityPackageImporter.Warn("Could not schedule the local import report: " + reportingError.GetType().Name);
        }
    }

    private static void RecordCore(ImportReportContext context, object identity, Exception error,
        PreparedImportItem item, UnityProjectImporter project, ImportReportStage? stage, ImportActionGate actionGate)
    {
        if (context == null || !context.TryBeginReport(identity)) return;
        ReportResult result = null;
        try
        {
            if (item != null)
            {
                result = ImportResultAdapter.CaptureReport(item, project, context.IncludeNames);
                if (actionGate != null) result = result with { MainActionGate = actionGate };
            }
        }
        catch (Exception captureError)
        {

            context.Warning(ReportWarningCategory.Reporting, ReportDiagnosticCode.SnapshotUnavailable);
            UnityPackageImporter.Warn("Import report snapshot unavailable: " + captureError.GetType().Name);
        }
        ImportReportData snapshot = context.Capture(error, result, stage);
        if (item != null) UnityPackageImporter.Msg(ImportFailureReport.LocalItemSummary(snapshot, item.DisplayName ?? "<unnamed>"));
        var readPackages = context.CapturePackageReader();
        string directory = Path.Combine(Path.GetDirectoryName(UnityPackageImporter.cachePath), "reports", "UnityPackageImporter");
        var sanitizer = ReportSanitizer.ForCurrentUser();
        Panels.TryGetValue(context, out var panel);
        long revision = panel?.BeginReport() ?? 0;
        _ = Task.Run(() => ImportReportStore.SaveBestEffortAsync(async () =>
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var packages = await readPackages(budget.Token).ConfigureAwait(false);
            var report = ImportIssueDraft.Build(snapshot with { Packages = packages }, sanitizer);
            panel?.Publish(revision, report);
            await ImportReportStore.WriteAsync(directory, report.Report, snapshot.Time).ConfigureAwait(false);
            UnityPackageImporter.Msg(ImportFailureReport.LocalLogSummary(snapshot));
        }, type => UnityPackageImporter.Warn("Could not save the local import report: " + type)));
    }
}
