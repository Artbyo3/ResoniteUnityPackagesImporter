using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.UIX;
using Renderite.Shared;

namespace UnityPackageImporter.Models;

internal sealed class MissingMaterialBinding
{
    public SkinnedMeshRenderer Renderer { get; init; }
    public int MaterialIndex { get; init; }
    public string MaterialGuid { get; init; }
    public string RendererName { get; init; }
    public bool Resolved { get; set; }
}

internal sealed class MaterialResolutionResult
{
    public MaterialResolutionResult(MaterialDependencySummary remaining, int restoredReferenceCount)
    {
        Remaining = remaining;
        RestoredReferenceCount = restoredReferenceCount;
    }

    public MaterialDependencySummary Remaining { get; }
    public int RestoredReferenceCount { get; }
}

internal static class MaterialDependencyCoordinator
{
    private static readonly object Sync = new();
    private static readonly Dictionary<World, MaterialDependencySession> WaitingByWorld = new();

    public static async Task ShowIfNeededAsync(UnityProjectImporter importer)
    {
        var summary = importer?.GetMissingMaterialSummary();
        if (summary == null || !summary.HasMissingAssets) return;

        UnityPackageImporter.Warn(
            "Import is missing " + summary.MissingAssetCount + " material asset(s) across " +
            summary.AffectedRendererCount + " renderer(s). Showing the dependency report.");

        await default(ToWorld);
        if (importer.world == null || importer.world.RootSlot == null || importer.world.RootSlot.IsDestroyed)
            return;

        var session = new MaterialDependencySession(importer);
        session.SpawnPanel();
        await default(ToBackground);
    }

    public static async Task<bool> TryResolveWaitingAsync(
        World world,
        IReadOnlyDictionary<string, string> dependencyAssets,
        IReadOnlyList<string> packageNames)
    {
        MaterialDependencySession session;
        lock (Sync)
        {
            if (world == null || !WaitingByWorld.TryGetValue(world, out session)) return false;
            if (!session.IsUsable)
            {
                WaitingByWorld.Remove(world);
                return false;
            }
        }

        if (!session.CanUse(dependencyAssets?.Keys))
        {
            await session.ShowNoMatchAsync(packageNames);
            return false;
        }

        lock (Sync)
        {
            if (WaitingByWorld.TryGetValue(world, out var current) && ReferenceEquals(current, session))
                WaitingByWorld.Remove(world);
        }

        await session.ApplyAsync(dependencyAssets, packageNames);
        if (session.IsWaiting && session.IsUsable)
        {
            lock (Sync) WaitingByWorld[world] = session;
        }
        return true;
    }

    internal static void BeginWaiting(MaterialDependencySession session)
    {
        if (session == null || !session.IsUsable) return;
        MaterialDependencySession replaced = null;
        lock (Sync)
        {
            if (WaitingByWorld.TryGetValue(session.World, out var current) && !ReferenceEquals(current, session))
                replaced = current;
            WaitingByWorld[session.World] = session;
        }
        replaced?.ShowReplaced();
    }

    internal static void StopWaiting(MaterialDependencySession session)
    {
        if (session == null) return;
        lock (Sync)
        {
            if (WaitingByWorld.TryGetValue(session.World, out var current) && ReferenceEquals(current, session))
                WaitingByWorld.Remove(session.World);
        }
    }
}

internal sealed class MaterialDependencySession
{
    private readonly UnityProjectImporter importer;
    private Slot panelRoot;
    private Text statusText;
    private Text messageText;
    private Text detailsText;
    private Button actionButton;
    private Button secondaryButton;
    private bool waiting;
    private bool applying;
    private bool complete;

    public MaterialDependencySession(UnityProjectImporter importer)
    {
        this.importer = importer;
    }

    public World World => importer.world;
    public bool IsWaiting => waiting;
    public bool IsUsable => panelRoot != null && !panelRoot.IsDestroyed &&
                            importer.world != null && importer.world.RootSlot != null &&
                            !importer.world.RootSlot.IsDestroyed;

    public bool CanUse(IEnumerable<string> assetGuids) =>
        IsUsable && importer.CanResolveAnyMaterial(assetGuids);

    public void SpawnPanel()
    {
        var summary = importer.GetMissingMaterialSummary();
        if (!summary.HasMissingAssets) return;

        World.LocalUser.GetPointInFrontOfUser(
            out float3 panelPosition,
            out floatQ panelRotation,
            null,
            null,
            0.7f,
            true);

        panelRoot = World.AddSlot("Unity Package Material Dependencies");
        var compactSize = new float2(640f, 430f);
        var expandedSize = new float2(640f, 690f);
        var ui = RadiantUI_Panel.SetupPanel(
            panelRoot,
            "Unity Import",
            compactSize,
            true,
            true);
        RadiantUI_Constants.SetupEditorStyle(ui, true);
        ui.Style.TextAlignment = Alignment.TopLeft;
        ui.Style.ButtonTextAlignment = Alignment.MiddleCenter;
        ui.VerticalLayout(14f, 18f, Alignment.TopLeft, true, false);

        string displayName = GetDisplayName(importer.PackageNames);
        ui.Text(
            "<b>" + EscapeRichText(displayName) + "</b><br>" +
            "<color=#AAB5C7>Unity package materials</color>",
            30f,
            false,
            Alignment.TopLeft,
            true);

        statusText = ui.Text(
            "<color=#FFCA70><b>Material package needed</b></color>",
            24f,
            false,
            Alignment.TopLeft,
            true);
        messageText = ui.Text(
            BuildMissingMessage(summary),
            18f,
            false,
            Alignment.TopLeft,
            true);

        ui.PushStyle();
        ui.Style.ButtonColor = new colorX(0.20f, 0.48f, 0.34f, 1f);
        ui.Style.HighlightColor = new colorX(0.28f, 0.62f, 0.44f, 1f);
        ui.Style.TextColor = new colorX(0.96f, 0.98f, 1f, 1f);
        actionButton = ui.Button("Add Material Package");
        ui.PopStyle();
        StyleButton(actionButton, 58f, 20f);
        AddLocalPressedHandler(actionButton, (_, _) => panelRoot.StartGlobalTask(HandleActionAsync));

        ui.PushStyle();
        ui.Style.ButtonColor = new colorX(0.10f, 0.11f, 0.14f, 1f);
        ui.Style.HighlightColor = new colorX(0.25f, 0.28f, 0.34f, 1f);
        ui.Style.TextColor = new colorX(0.68f, 0.72f, 0.80f, 1f);
        secondaryButton = ui.Button("Continue with grey materials");
        ui.PopStyle();
        StyleButton(secondaryButton, 46f, 17f);
        AddLocalPressedHandler(secondaryButton, (_, _) => panelRoot.StartGlobalTask(HandleSecondaryAsync));

        ui.PushStyle();
        ui.Style.ButtonColor = new colorX(0.08f, 0.09f, 0.12f, 1f);
        ui.Style.HighlightColor = new colorX(0.22f, 0.25f, 0.31f, 1f);
        ui.Style.TextColor = new colorX(0.47f, 0.66f, 1f, 1f);
        var detailsButton = ui.Button("Show details");
        ui.PopStyle();
        detailsButton.Slot.Name = "Advanced Mode";
        StyleButton(detailsButton, 42f, 16f);

        detailsText = ui.Text(
            BuildDetails(summary),
            15f,
            false,
            Alignment.TopLeft,
            true);
        detailsText.Slot.Name = "Advanced Details";
        var detailsLayout = detailsText.Slot.GetComponent<LayoutElement>() ??
                            detailsText.Slot.AttachComponent<LayoutElement>();
        detailsLayout.PreferredHeight.Value = 230f;
        detailsText.Slot.ActiveSelf = false;

        var smoothSize = panelRoot.AttachComponent<SmoothValue<float2>>();
        smoothSize.TargetValue.Value = compactSize;
        smoothSize.Speed.Value = 8f;
        smoothSize.WriteBack.Value = false;
        smoothSize.Value.Target = ui.Canvas.Size;

        var sizeDriver = detailsButton.Slot.AttachComponent<BooleanValueDriver<float2>>();
        sizeDriver.FalseValue.Value = compactSize;
        sizeDriver.TrueValue.Value = expandedSize;
        sizeDriver.TargetField.Target = smoothSize.TargetValue;

        var labelDriver = detailsButton.Slot.AttachComponent<BooleanValueDriver<string>>();
        labelDriver.FalseValue.Value = "Show details";
        labelDriver.TrueValue.Value = "Hide details";
        labelDriver.TargetField.Target = detailsButton.Label.Content;

        var detailsDriver = detailsButton.Slot.AttachComponent<BooleanValueDriver<bool>>();
        detailsDriver.FalseValue.Value = false;
        detailsDriver.TrueValue.Value = true;
        detailsDriver.TargetField.Target = detailsText.Slot.ActiveSelf_Field;

        detailsButton.Slot.AttachComponent<ButtonToggle>().TargetValue.Target = sizeDriver.State;
        detailsButton.Slot.AttachComponent<ButtonToggle>().TargetValue.Target = labelDriver.State;
        detailsButton.Slot.AttachComponent<ButtonToggle>().TargetValue.Target = detailsDriver.State;

        ui.NestOut();
        panelRoot.GlobalPosition = panelPosition;
        panelRoot.GlobalRotation = panelRotation;
        panelRoot.GlobalScale = new float3(0.00065f, 0.00065f, 0.00065f);
    }

    public async Task ShowNoMatchAsync(IReadOnlyList<string> packageNames)
    {
        await default(ToWorld);
        if (!IsUsable) return;
        statusText.Content.Value = "<color=#FFCA70><b>Materials not found</b></color>";
        messageText.Content.Value =
            "That package does not contain the materials this import needs.<br>" +
            "<color=#AAB5C7>Drop a different material package to keep searching.</color>";
        actionButton.LabelText = "Waiting for material package…";
        actionButton.Enabled = false;
        secondaryButton.LabelText = "Cancel";
        await default(ToBackground);
    }

    public void ShowReplaced()
    {
        waiting = false;
        if (!IsUsable) return;
        statusText.Content.Value = "<color=#FFCA70><b>Waiting was moved</b></color>";
        messageText.Content.Value =
            "Another import is now waiting for a material package.<br>" +
            "<color=#AAB5C7>Select Add Material Package here to switch back.</color>";
        actionButton.LabelText = "Add Material Package";
        actionButton.Enabled = true;
        secondaryButton.LabelText = "Continue with grey materials";
    }

    public async Task ApplyAsync(
        IReadOnlyDictionary<string, string> dependencyAssets,
        IReadOnlyList<string> packageNames)
    {
        applying = true;
        waiting = false;
        await default(ToWorld);
        if (!IsUsable) return;
        statusText.Content.Value = "<color=#7EA8FF><b>Connecting materials…</b></color>";
        messageText.Content.Value = "Updating the affected meshes in place.";
        actionButton.LabelText = "Connecting…";
        actionButton.Enabled = false;
        secondaryButton.Enabled = false;
        await default(ToBackground);

        var result = await importer.ResolveMissingMaterialsAsync(dependencyAssets);

        await default(ToWorld);
        applying = false;
        if (!IsUsable) return;
        detailsText.Content.Value = BuildDetails(result.Remaining);
        secondaryButton.Enabled = true;

        if (!result.Remaining.HasMissingAssets)
        {
            complete = true;
            statusText.Content.Value = "<color=#72DFA0><b>Materials connected</b></color>";
            messageText.Content.Value =
                "The missing materials were restored without importing the meshes again.";
            actionButton.LabelText = "Done";
            actionButton.Enabled = true;
            secondaryButton.Slot.ActiveSelf = false;
        }
        else
        {
            waiting = true;
            statusText.Content.Value = "<color=#FFCA70><b>More materials needed</b></color>";
            messageText.Content.Value =
                result.RestoredReferenceCount + " material assignment" +
                (result.RestoredReferenceCount == 1 ? " was" : "s were") +
                " restored. " + result.Remaining.MissingAssetCount +
                " material asset" + (result.Remaining.MissingAssetCount == 1 ? " is" : "s are") +
                " still missing.<br><color=#AAB5C7>Drop another material package to continue.</color>";
            actionButton.LabelText = "Waiting for another package…";
            actionButton.Enabled = false;
            secondaryButton.LabelText = "Continue with grey materials";
        }
        await default(ToBackground);
    }

    private async Task HandleActionAsync()
    {
        await default(ToWorld);
        if (!IsUsable || applying) return;
        if (complete)
        {
            MaterialDependencyCoordinator.StopWaiting(this);
            panelRoot.Destroy();
            return;
        }

        waiting = true;
        statusText.Content.Value = "<color=#7EA8FF><b>Waiting for material package</b></color>";
        messageText.Content.Value =
            "Drop the additional <b>.unitypackage</b> into Resonite.<br>" +
            "<color=#AAB5C7>The importer will connect matching materials automatically.</color>";
        actionButton.LabelText = "Waiting for package…";
        actionButton.Enabled = false;
        secondaryButton.LabelText = "Cancel";
        MaterialDependencyCoordinator.BeginWaiting(this);
        await default(ToBackground);
    }

    private async Task HandleSecondaryAsync()
    {
        await default(ToWorld);
        if (!IsUsable || applying) return;
        if (waiting)
        {
            waiting = false;
            MaterialDependencyCoordinator.StopWaiting(this);
            var summary = importer.GetMissingMaterialSummary();
            statusText.Content.Value = "<color=#FFCA70><b>Material package needed</b></color>";
            messageText.Content.Value = BuildMissingMessage(summary);
            actionButton.LabelText = "Add Material Package";
            actionButton.Enabled = true;
            secondaryButton.LabelText = "Continue with grey materials";
        }
        else
        {
            MaterialDependencyCoordinator.StopWaiting(this);
            panelRoot.Destroy();
        }
        await default(ToBackground);
    }

    private string BuildDetails(MaterialDependencySummary summary)
    {
        var guids = importer.GetMissingMaterialGuids();
        string guidLines = string.Join("<br>", guids.Take(10));
        if (guids.Count > 10)
            guidLines += "<br>…and " + (guids.Count - 10) + " more";

        return "<b>Import details</b><br>" +
               "<color=#8C97AA>Packages:</color> " +
               EscapeRichText(string.Join(", ", importer.PackageNames.Select(Path.GetFileName))) + "<br>" +
               "<color=#8C97AA>Missing material assets:</color> " + summary.MissingAssetCount + "<br>" +
               "<color=#8C97AA>Affected meshes:</color> " + summary.AffectedRendererCount + "<br><br>" +
               "<color=#8C97AA>Missing GUIDs</color><br>" + guidLines;
    }

    private static string BuildMissingMessage(MaterialDependencySummary summary) =>
        summary.MissingAssetCount + " material asset" +
        (summary.MissingAssetCount == 1 ? " is" : "s are") +
        " stored outside this package.<br><color=#AAB5C7>" +
        summary.AffectedRendererCount + " mesh" +
        (summary.AffectedRendererCount == 1 ? " is" : "es are") +
        " currently using a grey fallback.</color>";

    private static string GetDisplayName(IReadOnlyList<string> packageNames)
    {
        if (packageNames == null || packageNames.Count == 0) return "Unity package";
        if (packageNames.Count == 1) return Path.GetFileNameWithoutExtension(packageNames[0]);
        return packageNames.Count + " Unity packages";
    }

    private static string EscapeRichText(string value) =>
        (value ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static void StyleButton(Button button, float height, float textSize)
    {
        var layout = button.Slot.GetComponent<LayoutElement>() ??
                     button.Slot.AttachComponent<LayoutElement>();
        layout.PreferredHeight.Value = height;
        if (button.Label != null)
        {
            button.Label.Size.Value = textSize;
            button.Label.HorizontalAlign.Value = TextHorizontalAlignment.Center;
            button.Label.VerticalAlign.Value = TextVerticalAlignment.Middle;
        }
    }

    private static void AddLocalPressedHandler(Button button, ButtonEventHandler handler)
    {
        var eventInfo = typeof(Button).GetEvent(
            "LocalPressed",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (eventInfo == null)
            throw new MissingMemberException(typeof(Button).FullName, "LocalPressed");
        eventInfo.AddEventHandler(button, handler);
    }
}
