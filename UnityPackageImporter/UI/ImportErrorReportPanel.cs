using System;
using System.IO;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.UIX;
using UnityPackageImporter.Models;

namespace UnityPackageImporter.UI;

// The native template owns layout, scrolling, Close, clipboard and hyperlink interaction.
// This controller only supplies real report data in the local user's private world.
internal sealed class ImportErrorReportPanel
{
    private readonly World world;
    private readonly ImportReportInbox inbox = new();
    private StationTemplate template;
    private bool loading;

    internal ImportErrorReportPanel(World privateWorld) => world = privateWorld;
    internal long BeginReport() => inbox.BeginReport();

    internal void Publish(long revision, ImportReportPresentation report)
    {
        if (!inbox.Complete(revision, report)) return;
        try
        {
            world.RunSynchronously(() =>
            {
                if (world.IsDestroyed || inbox.Current == null) return;
                if (template?.Root is { IsDestroyed: false }) Apply(inbox.Current);
                else if (!loading)
                {
                    loading = true;
                    _ = world.RootSlot.StartGlobalTask(ShowAsync);
                }
            });
        }
        catch (Exception error)
        {
            // UI availability never prevents the independent local report-file write.
            UnityPackageImporter.Warn("Could not schedule the private import report panel: " + error.GetType().Name);
        }
    }

    private async Task ShowAsync()
    {
        StationTemplate loaded = null;
        try
        {
            if (world.IsDestroyed || world.LocalUser == null || inbox.Current == null) return;
            world.LocalUser.GetPointInFrontOfUser(out var position, out var rotation, float3.Backward, null, 0.9f, true);
            loaded = await StationTemplateLoader.LoadAsync("error-report", world, position, rotation, activate: false);
            await default(ToWorld);
            if (world.IsDestroyed || inbox.Current == null)
            {
                if (!loaded.Host.IsDestroyed) loaded.Host.Destroy();
                return;
            }
            template = loaded;
            var text = template.Get<Text>("report");
            if (text.Content.IsDriven || text.ParseRichText.Value)
                throw new InvalidDataException("The report binding must be an undriven plain-text source.");
            if (template.Get<ButtonClipboardCopyText>("copyReportAction").Source.Target != text.Content)
                throw new InvalidDataException("The report clipboard source must reference the report text.");
            // Close is the template's native ButtonActionTrigger. Observe its lifetime only.
            ((IDestroyable)template.Root).Destroyed += OnPanelDestroyed;
            template.LocaleChanged += RefreshLinkReason;
            RefreshLinkReason(LocalizationManager.DetectLocaleCode());
            Apply(inbox.Current);
            template.Host.ActiveSelf = true;
            UnityPackageImporter.Msg("Opened private import report panel.");
        }
        catch (Exception error)
        {
            await default(ToWorld);
            template = null;
            if (loaded?.Host is { IsDestroyed: false }) loaded.Host.Destroy();
            UnityPackageImporter.Warn("Could not open the private import report panel: " + error.GetType().Name);
        }
        finally { loading = false; }
    }

    private void Apply(ImportReportPresentation report)
    {
        template.Get<Text>("report").Content.Value = report.Report;
        template.Get<Hyperlink>("openIssueLink").URL.Value = report.IssueUrl;
        template.Get<ValueMultiDriver<bool>>("actionsReady").Value.Value = true;
    }

    private void RefreshLinkReason(string locale)
    {
        if (template?.Root is { IsDestroyed: false })
            template.Get<Hyperlink>("openIssueLink").Reason.Value =
                LocalizationManager.GetStringForLocale(locale, "errorReportIssueDescription");
    }

    private void OnPanelDestroyed(IDestroyable destroyed)
    {
        inbox.Dismiss();
        var host = template?.Host;
        template = null;
        if (!world.IsDestroyed && host is { IsDestroyed: false }) host.Destroy();
    }
}
