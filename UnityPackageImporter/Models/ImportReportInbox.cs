#nullable enable
namespace UnityPackageImporter.Models;

// One import, one panel. Closing acknowledges even reports still being hashed.
internal sealed class ImportReportInbox
{
    private readonly object gate = new();
    private long requested, received, dismissed;
    private ImportReportPresentation? current;
    internal long BeginReport() { lock (gate) return ++requested; }
    internal ImportReportPresentation? Current { get { lock (gate) return current; } }

    internal bool Complete(long revision, ImportReportPresentation report)
    {
        lock (gate)
        {
            if (revision <= dismissed || revision <= received || revision > requested) return false;
            received = revision;
            current = report;
            return true;
        }
    }

    internal void Dismiss()
    {
        lock (gate) { dismissed = requested; current = null; }
    }
}
