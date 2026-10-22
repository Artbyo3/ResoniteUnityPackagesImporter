#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityPackageImporter.Extractor;

namespace UnityPackageImporter.Models;

internal static class ImportFailureReport
{
    private static readonly Regex LocalPath = new(
        @"(?i)(?:[a-z]:[\\/]|\\\\|file:///(?:[a-z]:[\\/])?|/(?:Users|home|tmp|var)/)[^\r\n""'<>|]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static string Build(string stage, Exception error, DateTimeOffset? time = null)
    {
        ArgumentNullException.ThrowIfNull(error);
        var exceptions = CaptureExceptions(error, out bool truncated);
        return Build(new ImportReportData(new("Unknown", "Unknown", "Unknown", ReportOsFamily.Other, "Unknown"),
            Guid.NewGuid(), time ?? DateTimeOffset.UtcNow, Stage(stage), exceptions, Array.Empty<ReportPackage>(), 0,
            null, new Dictionary<ReportWarningCategory, int>(), Array.Empty<ReportWarning>(), 0,
            Array.Empty<ReportObjectFailure>(), ExceptionsTruncated: truncated, ReportId: Guid.NewGuid()), ReportSanitizer.ForCurrentUser());
    }

    internal const int MaximumCharacters = 32000;
    internal const int FormatVersion = 2;
    internal const int MaximumExceptions = 8;

    internal static IReadOnlyList<ReportException> CaptureExceptions(Exception? error, out bool truncated)
    {
        var queue = new Queue<Exception>();
        var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        var results = new List<ReportException>();
        if (error != null) queue.Enqueue(error);
        while (queue.Count > 0 && results.Count < MaximumExceptions)
        {
            Exception current = queue.Dequeue();
            if (!seen.Add(current)) continue;
            results.Add(new(current.GetType().Name, PackageInputFormat.Failure(current), ReportFailureDetails.Methods(current), ReportFailureDetails.Classify(current)));
            if (current is AggregateException aggregate)
                foreach (Exception inner in aggregate.InnerExceptions.Take(MaximumExceptions + 1)) queue.Enqueue(inner);
            else if (current.InnerException != null) queue.Enqueue(current.InnerException);
        }
        truncated = queue.Count > 0;
        return results.AsReadOnly();
    }

    internal static ImportReportStage Stage(string? key) => key switch
    {
        "extractingPackage" => ImportReportStage.Extraction,
        "buildingPrefabs" => ImportReportStage.Reconstruction,
        "loadingModels" or "loadingMeshes" or "loadingMeshesAndMaterials" => ImportReportStage.Models,
        "loadingMaterials" or "translatingMaterials" => ImportReportStage.Materials,
        "Create avatar" => ImportReportStage.CreateAvatar,
        "Keep prefab in world" => ImportReportStage.KeepObject,
        "Install clothing" or "Clothing install" => ImportReportStage.InstallOutfit,
        "Change material preset" => ImportReportStage.MaterialCustomization,
        _ => ImportReportStage.Unknown
    };

    internal static string Build(ImportReportData data, ReportSanitizer sanitizer)
    {
        var text = new StringBuilder();
        var aliases = new ReportAliases();
        void Line(string value = "") => text.Append(sanitizer.Clean(value)).Append('\n');
        string Number(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "unknown";
        string Flag(bool? value) => value.HasValue ? (value.Value ? "yes" : "no") : "unknown";
        void Name(string? value) { if (data.IncludeNames && !string.IsNullOrEmpty(value)) Line("Name: " + value); }
        void ExceptionDetails(ReportException exception, string prefix)
        {
            Line(prefix + sanitizer.Token(exception.Type));
            Line("Message: " + (exception.InputFailure.HasValue ? PackageInputFormat.Message(exception.InputFailure.Value) :
                ReportFailureDetails.Describe(exception.Reason)));
            if (exception.Reason != ReportFailureReason.Unknown) Line("Reason: " + exception.Reason);
            foreach (string method in exception.Methods.Take(24)) Line("Method: " + sanitizer.Token(method));
        }
        void Object(ReportObjectFailure value)
        {
            Line($"{aliases.Object(value.Id, value.SourceKey)}: {value.Type}; Unity class {Number(value.UnityClass)}; ownership {value.Ownership}");
            Name(value.Name);
            foreach (var error in (value.Exceptions ?? Array.Empty<ReportException>()).Take(MaximumExceptions))
                ExceptionDetails(error, "Object error: ");
        }

        Line("Unity Package Import Report — report format " + FormatVersion);
        Line("Mod version: " + data.Environment.ModVersion);
        Line("Resonite version: " + data.Environment.ResoniteVersion);
        Line("Last verified version: " + data.Environment.VerifiedVersion);
        Line("Version matches last verified: " + Flag(data.Environment.ResoniteVersion == data.Environment.VerifiedVersion && data.Environment.ResoniteVersion != "Unknown"));
        Line("OS family: " + data.Environment.Os);
        Line("Date (UTC): " + data.Time.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Line("Report token: " + (data.ReportId == Guid.Empty ? "unavailable" : data.ReportId.ToString("N")));
        Line("Locale: " + data.Environment.Locale);
        Line("Names: " + (data.IncludeNames ? "included (opt-in)" : "excluded"));
        Line(); Line("Failure");
        Line("Stage: " + data.Stage);
        foreach (var (exception, index) in data.Exceptions.Take(MaximumExceptions).Select((value, index) => (value, index)))
        {
            ExceptionDetails(exception, index == 0 ? "Error: " : "Cause: ");
        }
        if (data.Exceptions.Count == 0) Line("No exception; an import result is blocked.");
        if (data.ExceptionsTruncated || data.Exceptions.Count > MaximumExceptions) Line("Additional exceptions omitted.");

        Line(); Line("Package facts"); Line("Input files: " + Number(data.PackageCount));
        foreach (var (package, index) in data.Packages.Take(16).Select((value, index) => (value, index)))
        {
            Line("Package " + Number(index + 1) + ": " + package.Status);
            Name(package.Name);
            Line("Size range: " + ReportAliases.Size(package.Bytes));
            Line("Detected format: " + (package.Format?.ToString() ?? "unknown"));
            Line("Archive entries observed: " + Number(package.Entries));
            Line("Extraction complete: " + Flag(package.ExtractionComplete));
            if (package.Assets == null) Line("Asset category counts: unavailable");
            else foreach (ReportAssetCategory category in Enum.GetValues<ReportAssetCategory>())
                Line(category + ": " + Number(package.Assets.GetValueOrDefault(category)));
        }
        if (data.PackageCount > 16) Line("Additional package facts omitted.");

        Line(); Line("Model import failures (session diagnostics)");
        Line("Count: " + Number(data.ModelFailureCount));
        foreach (var model in (data.ModelFailures ?? Array.Empty<ReportModelFailure>()).Take(32))
        {
            Line(aliases.Model(model.AssetKey) + ": " + model.Facts.Format);
            Line("Model phase: " + model.Phase);
            Line("Size range: " + ReportAliases.Size(model.Facts.Bytes));
            Line("Format version: " + Number(model.Facts.Version));
            if (model.Facts.UnrecognizedVersion) Line("The binary header contains an unrecognized format version.");
            Line("Model library version: " + model.BackendVersion);
            foreach (var step in model.Processing.Take(16)) Line("Post-process: " + step);
            ExceptionDetails(model.Error, "Model error: ");
        }
        if (data.ModelFailureCount > (data.ModelFailures?.Count ?? 0)) Line("Additional model-failure details omitted.");

        Line(); Line("Result snapshot");
        if (data.Result is not ReportResult result) Line("Unavailable (failure occurred before a result snapshot existed).");
        else
        {
            Name(result.Name);
            Line("Structure ready: " + Flag(result.StructureReady));
            Line("Native readiness: " + result.NativeReadiness);
            foreach (ImportResultIssue issue in result.Issues.Take(32)) Line("Issue: " + issue);
            Line("Failed objects: " + Number(result.FailedObjects));
            Line("Operations: " + Number(result.CompletedOperations) + " completed / " + Number(result.Operations));
            Line("Missing materials: " + Number(result.MissingMaterials));
            Line("Missing mesh references: " + Number(result.MissingMeshes));
            Line("Unsupported nested prefabs: " + Number(result.UnsupportedNestedPrefabs));
            Line("Missing nested prefabs: " + Number(result.MissingNestedPrefabs));
            Line("Unsupported features: " + Number(result.UnsupportedFeatures));
            Line("Material warnings: " + Number(result.MaterialWarnings));
            Line("Requires installation: " + Flag(result.RequiresInstallation));
            Line("Rig count: " + Number(result.Native.RigCount));
            Line("Complete biped rig count: " + Number(result.Native.CompleteRigCount));
            Line("IK present: " + Flag(result.Native.IkPresent));
            Line("IK initialized: " + Flag(result.Native.IkInitialized));
            Line("Left hand reference: " + Flag(result.Native.LeftHand));
            Line("Right hand reference: " + Flag(result.Native.RightHand));
            Line("Left eye reference: " + Flag(result.Native.LeftEye));
            Line("Right eye reference: " + Flag(result.Native.RightEye));
            Line("Viewpoint resolves: " + Flag(result.Native.Viewpoint));
            foreach (var failure in result.Objects.Take(32)) Object(failure);
            if (result.FailedObjects > result.Objects.Take(32).Count()) Line("Additional failed-object details omitted or unavailable.");
            Line("Plan diagnostics: " + Number(result.PlanDiagnosticCount));
            foreach (string code in (result.PlanDiagnosticCodes ?? Array.Empty<string>()).Take(24)) Line("Plan code: " + sanitizer.Token(code));
            foreach (var operation in result.OperationDetails.Take(24))
            {
                Line($"Operation: {operation.Kind}; state {operation.State}; ownership {operation.Ownership}; component {aliases.Object(operation.ComponentId, operation.SourceKey)}; object {aliases.Object(operation.GameObjectId, operation.SourceKey)}");
                if (operation.DeferredByPolicy) Line("Deferred by policy: yes (not an ownership or conversion failure).");
                Name(operation.Name);
                foreach (string code in operation.Codes.Take(8)) Line("Code: " + sanitizer.Token(code));
            }
            if (result.Operations > result.OperationDetails.Take(24).Count()) Line("Additional operation details omitted.");
        }

        Line(); Line("Session source failures (may include other items)");
        Line("Count: " + Number(data.SourceFailures));
        foreach (var failure in data.SourceFailureDetails.Take(32)) Object(failure);
        if (data.SourceFailures > data.SourceFailureDetails.Take(32).Count()) Line("Additional source-failure details omitted.");
        Line(); Line("Warning summary (session diagnostics and selected-result warnings)");
        foreach (ReportWarningCategory category in Enum.GetValues<ReportWarningCategory>()) Line(category + ": " + Number(data.WarningCounts.GetValueOrDefault(category)));
        foreach (ReportWarning warning in data.Warnings.Take(12))
        {
            Line(warning.Category + ": " + warning.Code + (warning.ObjectId.HasValue ? "; " + aliases.Object(warning.ObjectId.Value, warning.SourceKey) : ""));
            Name(warning.Name);
        }
        if (data.WarningCounts.Values.Sum(v => (long)v) > data.Warnings.Take(12).Count()) Line("Additional warning samples omitted.");
        Line(); Line("Content hashes and original asset/object IDs are excluded. Labels are local to this report. Package contents and raw log text are not included.");
        string output = text.ToString();
        return output.Length <= MaximumCharacters ? output : output[..(MaximumCharacters - 40)] + "\nReport truncated at size limit.\n";
    }

    internal static string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return LocalPath.Replace(value, "<local path>").Trim();
    }
}
