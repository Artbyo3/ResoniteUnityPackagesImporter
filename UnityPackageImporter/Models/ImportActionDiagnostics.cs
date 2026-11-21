#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace UnityPackageImporter.Models;

internal static class ImportActionDiagnostics
{
    internal static string ReadBuildIdentity(Func<string> read, Action<string> unavailable)
    {
        try { return read(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            unavailable(error.GetType().Name);
            return "unknown";
        }
    }

    internal static IReadOnlyList<AvatarOperationDiagnostic> Prioritize(IEnumerable<AvatarOperationDiagnostic> diagnostics) =>
        diagnostics.OrderByDescending(diagnostic => diagnostic.Severity).Take(64).ToArray();

    internal static string LocalEvidence(ImportActionGate gate, ImportResult result, AvatarBuildPlan? plan,
        string buildHash, Guid session, string itemName, string sourcePath, IEnumerable<ulong> failedIds)
    {
        string Field(string? value, int limit = 256) => (value ?? "").Replace('\r', ' ').Replace('\n', ' ')[..Math.Min((value ?? "").Length, limit)];
        var output = new StringBuilder("LOCAL-ONLY ACTION GATE EVIDENCE - contains source identities; do not publish.\n");
        output.AppendLine("Loaded DLL SHA256: " + Field(buildHash));
        output.AppendLine("Session: " + session.ToString("N"));
        output.AppendLine("Item: " + Field(itemName));
        output.AppendLine("Source: " + Field(sourcePath, 1024));
        output.AppendLine("Action: " + gate.Action + "; enabled: " + gate.Enabled);
        output.AppendLine("Button blockers: " + string.Join(", ", gate.Blockers));
        output.AppendLine("Structure ready: " + result.StructureReady + "; native readiness: " + result.NativeReadiness);
        output.AppendLine("Result issues: " + string.Join(", ", result.Issues));
        output.AppendLine("Operations completed: " + result.CompletedOperationCount + "/" + result.OperationCount);
        output.AppendLine("Failed source IDs (sample): " + string.Join(", ", failedIds.Distinct().Take(64).Select(id => id.ToString(CultureInfo.InvariantCulture))));
        var diagnostics = plan?.Diagnostics ?? new List<AvatarOperationDiagnostic>();
        foreach (var severity in Enum.GetValues<AvatarOperationSeverity>())
            output.AppendLine("Plan " + severity + " count: " + diagnostics.Count(diagnostic => diagnostic.Severity == severity));
        foreach (var diagnostic in diagnostics.Where(item => item.Code == "VRCFURY_INITIAL_SOURCE_GRAPH_RESOLVED").Take(8))
            output.AppendLine("Resolved model graph; source context: " + Field(diagnostic.SourceContext) + "; detail: " + Field(diagnostic.Message));
        foreach (var diagnostic in diagnostics.Where(item => item.Code is "APPEARANCE_REPLAY_SAVED" or "APPEARANCE_REPLAY_UNAVAILABLE").Take(8))
            output.AppendLine("Appearance replay; source context: " + Field(diagnostic.SourceContext) + "; detail: " + Field(diagnostic.Message, 512));
        foreach (var group in diagnostics.GroupBy(item => (item.Severity, item.Code)).OrderByDescending(group => group.Key.Severity).ThenBy(group => group.Key.Code, StringComparer.Ordinal).Take(64))
            output.AppendLine("Diagnostic group: " + group.Key.Severity + "; code: " + Field(group.Key.Code, 120) + "; count: " + group.Count() +
                "; sources: " + string.Join(", ", group.Select(item => item.SourceContext).Distinct().Take(8).Select(value => Field(value, 80))));
        foreach (var diagnostic in Prioritize(diagnostics))
            output.AppendLine("Plan " + diagnostic.Severity + "; code: " + Field(diagnostic.Code, 120) +
                "; source context: " + Field(diagnostic.SourceContext) + "; detail: " + Field(diagnostic.Message));
        if (diagnostics.Count > 64) output.AppendLine("Additional plan details omitted; severity counts include all diagnostics.");
        foreach (var operation in (plan?.Operations ?? new List<AvatarBuildOperation>()).Take(32))
        {
            output.AppendLine("Operation " + operation.Kind + "; state: " + operation.ExecutionState +
                "; valid: " + operation.IsValid + "; component: " + operation.Source.ComponentFileId + "; owner: " + operation.Source.GameObjectFileId);
            foreach (var diagnostic in Prioritize(operation.Diagnostics).Take(4))
                output.AppendLine("Operation " + diagnostic.Severity + "; code: " + Field(diagnostic.Code, 120) + "; detail: " + Field(diagnostic.Message));
        }
        string text = output.ToString();
        if (Encoding.UTF8.GetByteCount(text) <= 120 * 1024) return text;
        var bounded = new StringBuilder();
        int bytes = 0;
        foreach (string line in text.Split('\n'))
        {
            int next = Encoding.UTF8.GetByteCount(line) + 1;
            if (bytes + next > 120 * 1024) break;
            bounded.AppendLine(line);
            bytes += next;
        }
        bounded.AppendLine("Additional local details omitted to preserve the diagnostic byte budget.");
        return bounded.ToString();
    }
}
