using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.UIX;
using Renderite.Shared;
using UnityPackageImporter.FrooxEngineRepresentation;
using UnityGameObject = UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes.GameObject;
using UnitySkinnedMeshRenderer = UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes.SkinnedMeshRenderer;

namespace UnityPackageImporter.Models;

/// <summary>
/// Adds a local, session-backed context menu to imported Modular Avatar
/// attachments. Installation always operates on a duplicate so the imported
/// source remains available for inspection and recovery.
/// </summary>
internal static class ModularAvatarAttachmentInstaller
{
    private const string InstallerSlotName = "Modular Avatar Installer";
    private const string RecordsSlotName = "Unity Package Importer";
    private const string RecordMarker = "UnityPackageImporter.ModularAvatarOutfit";
    private const string RecordTypeFieldName = "Record Type";
    private const string SchemaFieldName = "Schema Version";
    private const string PrefabGuidFieldName = "Prefab GUID";
    private const string ContentHashFieldName = "Content Hash";
    private const string SourceNameFieldName = "Source Name";
    private const string AvatarSignatureFieldName = "Avatar Signature";
    private const string TargetAvatarFieldName = "Target Avatar";
    private const string InstalledRootFieldName = "Installed Root";
    private const string StateSlotName = "State";
    private const string EnabledSlotName = "Enabled";
    private const string OwnedObjectsSlotName = "Owned Objects";
    private const string OwnedComponentsSlotName = "Owned Components";

    private sealed class MergeSourceBinding
    {
        public ModularAvatarComponentDefinition Definition { get; init; }
        public Slot SourceSlot { get; init; }
        public int[] IndexPath { get; init; }
    }

    private sealed class BoneProxyBinding
    {
        public ModularAvatarComponentDefinition Definition { get; init; }
        public Slot SourceSlot { get; init; }
        public int[] IndexPath { get; init; }
    }

    private sealed class PreparedMerge
    {
        public MergeSourceBinding Binding { get; init; }
        public Slot SourceRoot { get; init; }
        public Slot TargetRoot { get; init; }
        public ModularAvatarMergePlan Plan { get; init; }
        public Dictionary<string, Slot> SourceSlots { get; init; }
        public Dictionary<string, Slot> TargetSlots { get; init; }
    }

    private sealed class OwnedRoot
    {
        public Slot Slot { get; init; }
        public bool ActiveWhenEnabled { get; init; }
        public string Role { get; init; }
    }

    private sealed class OwnedComponent
    {
        public Component Component { get; init; }
        public bool EnabledWhenOutfitEnabled { get; init; }
        public string Role { get; init; }
    }

    private sealed class InstallationRecordHandle
    {
        public Slot RecordRoot { get; init; }
        public Slot TargetAvatar { get; init; }
        public Slot InstalledRoot { get; init; }
        public ValueMultiDriver<bool> Enabled { get; init; }
        public int SchemaVersion { get; init; }
        public string PrefabGuid { get; init; }
        public string ContentHash { get; init; }
        public OutfitInstallMatch Match { get; init; }
        public bool IsHealthy => RecordRoot != null && !RecordRoot.IsDestroyed &&
                                 TargetAvatar != null && !TargetAvatar.IsDestroyed &&
                                 InstalledRoot != null && !InstalledRoot.IsDestroyed &&
                                 Enabled != null && !Enabled.IsDestroyed &&
                                 SchemaVersion == OutfitInstallIdentity.CurrentSchemaVersion;
    }

    public static async Task AttachAsync(
        Slot attachmentRoot,
        AvatarPackageManifest manifest,
        Dictionary<ulong, IUnityObject> unityObjects,
        IReadOnlyDictionary<string, string> availableAssets,
        string prefabGuid,
        string prefabFile)
    {
        if (attachmentRoot == null || manifest == null || unityObjects == null) return;

        var identity = OutfitInstallIdentity.FromPrefab(prefabGuid, prefabFile);

        var mergeBindings = ResolveMergeSources(attachmentRoot, manifest, unityObjects);
        if (mergeBindings.Count == 0)
        {
            UnityPackageImporter.Warn(
                "Modular Avatar attachment '" + attachmentRoot.Name +
                "' has no resolvable Merge Armature component; no installer menu was added.");
            return;
        }
        var boneProxyBindings = ResolveBoneProxies(attachmentRoot, manifest, unityObjects);

        await default(ToWorld);

        var existingInstaller = attachmentRoot.Children.FirstOrDefault(child => child.Name == InstallerSlotName);
        existingInstaller?.Destroy();

        var installerSlot = attachmentRoot.AddSlot(InstallerSlotName);
        installerSlot.OrderOffset = 2000;

        var source = installerSlot.AttachComponent<ContextMenuItemSource>();
        source.Label.Value = "Install Modular Avatar Clothing";
        source.Color.Value = new colorX(0.38f, 0.75f, 0.55f, 1f);
        source.CloseMenuOnPress.Value = false;

        var rootItem = attachmentRoot.AttachComponent<RootContextMenuItem>();
        rootItem.Item.Target = source;

        var submenu = installerSlot.AttachComponent<ContextMenuSubmenu>();
        var itemsRoot = installerSlot.AddSlot("Avatars");
        submenu.ItemsRoot.Target = itemsRoot;

        void RefreshCandidates()
        {
            if (attachmentRoot.IsDestroyed || itemsRoot.IsDestroyed) return;
            itemsRoot.DestroyChildren();

            var candidates = FindAvatarRigs(attachmentRoot);
            if (candidates.Count == 0)
            {
                AddStatusItem(itemsRoot, "No configured avatars found");
                return;
            }

            foreach (var rig in candidates)
            {
                string error = GetTargetError(mergeBindings, boneProxyBindings, rig);
                var itemSlot = itemsRoot.AddSlot(rig.Slot.Name);
                var item = itemSlot.AttachComponent<ContextMenuItemSource>();

                var existingRecord = FindInstallationRecord(rig.Slot, identity);
                if (existingRecord?.Match == OutfitInstallMatch.Current)
                {
                    if (existingRecord.IsHealthy)
                    {
                        BindOutfitToggle(item, existingRecord, rig.Slot.Name);
                    }
                    else
                    {
                        item.Label.Value = rig.Slot.Name + " (installation needs repair)";
                        item.Color.Value = new colorX(0.85f, 0.55f, 0.25f, 1f);
                        item.ButtonEnabled.Value = false;
                    }
                    continue;
                }
                if (existingRecord?.Match == OutfitInstallMatch.UpdateAvailable)
                {
                    item.Label.Value = rig.Slot.Name + " (update available)";
                    item.Color.Value = new colorX(0.85f, 0.65f, 0.28f, 1f);
                    item.ButtonEnabled.Value = false;
                    continue;
                }

                item.Label.Value = error == null
                    ? "Install copy on " + rig.Slot.Name
                    : rig.Slot.Name + " (incompatible)";
                item.Color.Value = error == null
                    ? new colorX(0.45f, 0.82f, 0.62f, 1f)
                    : new colorX(0.75f, 0.38f, 0.38f, 1f);
                item.ButtonEnabled.Value = error == null;
                item.CloseMenuOnPress.Value = true;

                if (error == null)
                {
                    var selectedRig = rig;
                    bool installing = false;
                    AddLocalPressedHandler(item, (_, _) => attachmentRoot.StartGlobalTask(async () =>
                    {
                        await default(ToWorld);
                        if (installing || item.IsDestroyed) return;
                        installing = true;
                        item.ButtonEnabled.Value = false;
                        item.Label.Value = "Installing on " + selectedRig.Slot.Name + "...";
                        try
                        {
                            string currentError = GetTargetError(mergeBindings, boneProxyBindings, selectedRig);
                            if (currentError != null) throw new InvalidOperationException(currentError);
                            var installed = await InstallCopyAsync(
                                attachmentRoot,
                                mergeBindings,
                                boneProxyBindings,
                                selectedRig,
                                identity);
                            source.Label.Value = "Installed copy on " + selectedRig.Slot.Name;
                            if (!item.IsDestroyed)
                                BindOutfitToggle(item, installed, selectedRig.Slot.Name);
                            UnityPackageImporter.Msg(
                                "Installed Modular Avatar attachment '" + attachmentRoot.Name +
                                "' on '" + selectedRig.Slot.Name + "' as '" + installed.InstalledRoot.Name + "'.");
                        }
                        catch (Exception ex)
                        {
                            installing = false;
                            if (!item.IsDestroyed)
                            {
                                item.Label.Value = "Retry on " + selectedRig.Slot.Name;
                                item.ButtonEnabled.Value = true;
                            }
                            source.Label.Value = "Install failed - see log";
                            UnityPackageImporter.Error(
                                "Failed to install Modular Avatar attachment '" + attachmentRoot.Name +
                                "' on '" + selectedRig.Slot.Name + "': " + ex);
                        }
                    }));
                }
            }
        }

        AddLocalPressedHandler(source, (_, _) => RefreshCandidates());
        RefreshCandidates();
        var materialDependencies = MaterialDependencyDiagnostics.Analyze(
            unityObjects.Values
                .OfType<UnitySkinnedMeshRenderer>()
                .Select(renderer => (renderer.m_Materials ?? new List<SourceObj>())
                    .Select(material => material?.guid)),
            availableAssets?.Keys ?? Array.Empty<string>());
        SpawnReportPanel(
            attachmentRoot,
            manifest,
            unityObjects,
            mergeBindings,
            boneProxyBindings,
            materialDependencies,
            identity);

        await default(ToBackground);
        UnityPackageImporter.Msg(
            "Added Modular Avatar installer menu to '" + attachmentRoot.Name +
            "' for " + mergeBindings.Count + " Merge Armature and " +
            boneProxyBindings.Count + " Bone Proxy component(s).");
    }

    private static void SpawnReportPanel(
        Slot attachmentRoot,
        AvatarPackageManifest manifest,
        Dictionary<ulong, IUnityObject> unityObjects,
        IReadOnlyList<MergeSourceBinding> mergeBindings,
        IReadOnlyList<BoneProxyBinding> boneProxyBindings,
        MaterialDependencySummary materialDependencies,
        OutfitInstallIdentity identity)
    {
        if (attachmentRoot == null || attachmentRoot.IsDestroyed) return;

        var panelRoot = attachmentRoot.World.AddSlot(
            "Unity Package Import Report - " + attachmentRoot.Name);
        var panelPosition = attachmentRoot.GlobalPosition + new float3(0f, 1.05f, -0.45f);

        var candidates = FindAvatarRigs(attachmentRoot)
            .Select(rig => (Rig: rig, Error: GetTargetError(mergeBindings, boneProxyBindings, rig)))
            .ToList();
        var compatibleCandidates = candidates
            .Where(candidate => candidate.Error == null ||
                                FindInstallationRecord(candidate.Rig.Slot, identity) != null)
            .Select(candidate => candidate.Rig)
            .ToList();
        var existingRecords = compatibleCandidates
            .Select(rig => FindInstallationRecord(rig.Slot, identity))
            .Where(record => record != null)
            .ToList();
        bool hasCurrentInstall = existingRecords.Any(record =>
            record.Match == OutfitInstallMatch.Current && record.IsHealthy);
        bool hasUpdateAvailable = existingRecords.Any(record =>
            record.Match == OutfitInstallMatch.UpdateAvailable);
        int targetRows = Math.Max(1, compatibleCandidates.Count);
        float compactHeight = 520f + Math.Max(0, targetRows - 1) * 70f;
        var compactSize = new float2(640f, compactHeight);
        var expandedSize = new float2(640f, compactHeight + 220f);

        LocaleString title = "Unity Import";
        var ui = RadiantUI_Panel.SetupPanel(
            panelRoot,
            title,
            compactSize,
            true,
            true);
        RadiantUI_Constants.SetupEditorStyle(ui, true);
        ui.Style.TextAlignment = Alignment.TopLeft;
        ui.Style.ButtonTextAlignment = Alignment.MiddleCenter;
        ui.VerticalLayout(14f, 18f, Alignment.TopLeft, true, false);

        int gameObjectCount = unityObjects.Values.Count(value => value is UnityGameObject);
        string detectedHandlers = string.Join(
            " • ",
            manifest.ModularAvatarComponents
                .GroupBy(component => component.Kind)
                .OrderBy(group => group.Key.ToString(), StringComparer.Ordinal)
                .Select(group => FormatComponentKind(group.Key) + " ×" + group.Count()));

        string displayName = attachmentRoot.Name.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
            ? attachmentRoot.Name[..^7]
            : attachmentRoot.Name;
        LocaleString heading =
            "<b>" + displayName + "</b><br>" +
            "<color=#AAB5C7>Modular Avatar clothing</color>";
        ui.Text(heading, 30f, false, Alignment.TopLeft, true);

        LocaleString summary = hasUpdateAvailable
            ? "<color=#FFCA70><b>Update available</b></color><br>" +
              "<color=#D7DCE5>Your installed outfit remains unchanged.</color>"
            : hasCurrentInstall
                ? "<color=#72DFA0><b>Outfit installed</b></color><br>" +
                  "<color=#D7DCE5>Move or rename it however you like.</color>"
                : "<color=#72DFA0><b>Ready to install</b></color><br>" +
                  "<color=#D7DCE5>This clothing can be installed safely.</color>";
        ui.Text(summary, 24f, false, Alignment.TopLeft, true);

        string readiness = boneProxyBindings.Count > 0
            ? "Armature, bone targets and bounds are ready."
            : "Armature, bones and bounds are ready.";
        LocaleString translated = "<color=#8C97AA>" + readiness + "</color>";
        ui.Text(translated, 17f, false, Alignment.TopLeft, true);

        LocaleString limitations =
            "<color=#FFCA70><b>Some features will be skipped</b></color><br>" +
            "<color=#AAB5C7>Blendshape links, menus and toggles.</color>";
        ui.Text(limitations, 17f, false, Alignment.TopLeft, true);

        LocaleString targetHeading = hasCurrentInstall
            ? "<b>Manage outfit</b>"
            : "<b>Choose an avatar</b>";
        ui.Text(targetHeading, 22f, false, Alignment.TopLeft, true);

        foreach (var rig in compatibleCandidates)
        {
            var selectedRig = rig;
            var existingRecord = FindInstallationRecord(selectedRig.Slot, identity);
            LocaleString buttonLabel = existingRecord?.Match switch
            {
                OutfitInstallMatch.Current when existingRecord.IsHealthy =>
                    "Outfit enabled • " + selectedRig.Slot.Name,
                OutfitInstallMatch.Current => "Installation needs repair • " + selectedRig.Slot.Name,
                OutfitInstallMatch.UpdateAvailable => "Update available • " + selectedRig.Slot.Name,
                _ => "Install on " + selectedRig.Slot.Name
            };
            var installButton = ui.Button(buttonLabel);
            if (existingRecord?.Match == OutfitInstallMatch.Current && existingRecord.IsHealthy)
            {
                BindOutfitToggle(installButton, existingRecord, selectedRig.Slot.Name);
                StyleReportButton(installButton, 56f, 20f);
                continue;
            }
            if (existingRecord != null)
            {
                installButton.Enabled = false;
                StyleReportButton(installButton, 56f, 20f);
                continue;
            }

            bool installing = false;
            AddLocalPressedHandler(installButton, (_, _) => attachmentRoot.StartGlobalTask(async () =>
            {
                await default(ToWorld);
                if (installing || installButton.IsDestroyed) return;
                installing = true;
                installButton.Enabled = false;
                installButton.LabelText = "Installing...";
                try
                {
                    string currentError = GetTargetError(mergeBindings, boneProxyBindings, selectedRig);
                    if (currentError != null) throw new InvalidOperationException(currentError);
                    var installed = await InstallCopyAsync(
                        attachmentRoot,
                        mergeBindings,
                        boneProxyBindings,
                        selectedRig,
                        identity);
                    if (!installButton.IsDestroyed)
                        BindOutfitToggle(installButton, installed, selectedRig.Slot.Name);
                    UnityPackageImporter.Msg(
                        "Installed Modular Avatar attachment '" + attachmentRoot.Name +
                        "' on '" + selectedRig.Slot.Name + "' as '" + installed.InstalledRoot.Name + "'.");
                }
                catch (Exception ex)
                {
                    if (!installButton.IsDestroyed)
                    {
                        installButton.LabelText = "Retry on " + selectedRig.Slot.Name;
                        installButton.Enabled = true;
                    }
                    installing = false;
                    UnityPackageImporter.Error(
                        "Failed to install Modular Avatar attachment '" + attachmentRoot.Name +
                        "' on '" + selectedRig.Slot.Name + "': " + ex);
                }
            }));
            StyleReportButton(installButton, 56f, 20f);
        }

        if (compatibleCandidates.Count == 0)
        {
            LocaleString noTargets = "<color=#AAB5C7>No compatible avatar found nearby.</color>";
            ui.Text(noTargets, 17f, false, Alignment.TopLeft, true);
        }

        LocaleString refreshLabel = "Search again";
        var refreshButton = ui.Button(refreshLabel);
        AddLocalPressedHandler(refreshButton, (_, _) => attachmentRoot.StartGlobalTask(async () =>
        {
            await default(ToWorld);
            if (attachmentRoot.IsDestroyed) return;
            // Build the replacement first so a transient error cannot destroy the
            // only usable report panel.
            SpawnReportPanel(
                attachmentRoot,
                manifest,
                unityObjects,
                mergeBindings,
                boneProxyBindings,
                materialDependencies,
                identity);
            if (!panelRoot.IsDestroyed) panelRoot.Destroy();
        }));
        StyleReportButton(refreshButton, 56f, 22f);

        ui.PushStyle();
        ui.Style.ButtonColor = new colorX(0.10f, 0.11f, 0.14f, 1f);
        ui.Style.HighlightColor = new colorX(0.28f, 0.31f, 0.39f, 1f);
        ui.Style.TextColor = new colorX(0.47f, 0.66f, 1f, 1f);
        ui.Style.PreferredHeight = 44f;
        LocaleString detailsLabel = "Show details";
        var detailsButton = ui.Button(detailsLabel);
        ui.PopStyle();
        detailsButton.Slot.Name = "Advanced Mode";
        StyleReportButton(detailsButton, 44f, 17f);
        detailsButton.Label.Color.Value = new colorX(0.47f, 0.66f, 1f, 1f);

        LocaleString detailText =
            "<b>Import details</b><br>" +
            "<color=#8C97AA>Parsed:</color> " + unityObjects.Count + " objects • " +
            gameObjectCount + " GameObjects<br>" +
            "<color=#8C97AA>Detected:</color> " +
            (string.IsNullOrWhiteSpace(detectedHandlers) ? "None" : detectedHandlers) + "<br><br>" +
            "<color=#72DFA0><b>Translated</b></color><br>" +
            "Bone mapping" + (boneProxyBindings.Count > 0 ? " • Bone Proxy placement" : "") +
            " • safe installation record • outfit Enabled • accurate bounds<br><br>" +
            "<color=#FFCA70><b>Skipped</b></color><br>" +
            (materialDependencies.HasMissingAssets
                ? "Missing materials ×" + materialDependencies.MissingAssetCount + " • "
                : "") +
            "Blendshape Sync • menus • parameters • toggles<br><br>" +
            "<color=#8C97AA>The imported source remains untouched.</color>";
        var advancedDetails = ui.Text(detailText, 16f, false, Alignment.TopLeft, true);
        advancedDetails.Slot.Name = "Advanced Details";
        var advancedLayout = advancedDetails.Slot.GetComponent<LayoutElement>() ??
                             advancedDetails.Slot.AttachComponent<LayoutElement>();
        advancedLayout.PreferredHeight.Value = 190f;
        advancedDetails.Slot.ActiveSelf = false;

        var smoothSize = panelRoot.AttachComponent<SmoothValue<float2>>();
        smoothSize.TargetValue.Value = compactSize;
        smoothSize.Speed.Value = 8f;
        smoothSize.WriteBack.Value = false;
        smoothSize.Value.Target = ui.Canvas.Size;

        var sizeDriver = detailsButton.Slot.AttachComponent<BooleanValueDriver<float2>>();
        sizeDriver.FalseValue.Value = compactSize;
        sizeDriver.TrueValue.Value = expandedSize;
        sizeDriver.State.Value = false;
        sizeDriver.TargetField.Target = smoothSize.TargetValue;

        var labelDriver = detailsButton.Slot.AttachComponent<BooleanValueDriver<string>>();
        labelDriver.FalseValue.Value = "Show details";
        labelDriver.TrueValue.Value = "Hide details";
        labelDriver.State.Value = false;
        labelDriver.TargetField.Target = detailsButton.Label.Content;

        var detailsDriver = detailsButton.Slot.AttachComponent<BooleanValueDriver<bool>>();
        detailsDriver.FalseValue.Value = false;
        detailsDriver.TrueValue.Value = true;
        detailsDriver.State.Value = false;
        detailsDriver.TargetField.Target = advancedDetails.Slot.ActiveSelf_Field;

        detailsButton.Slot.AttachComponent<ButtonToggle>().TargetValue.Target = sizeDriver.State;
        detailsButton.Slot.AttachComponent<ButtonToggle>().TargetValue.Target = labelDriver.State;
        detailsButton.Slot.AttachComponent<ButtonToggle>().TargetValue.Target = detailsDriver.State;

        ui.NestOut();
        panelRoot.GlobalPosition = panelPosition;
        panelRoot.GlobalRotation = attachmentRoot.GlobalRotation;
        panelRoot.GlobalScale = new float3(0.00065f, 0.00065f, 0.00065f);
    }

    private static void StyleReportButton(Button button, float height, float textSize)
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

        var background = button.Slot.GetComponent<Image>();
        if (background != null)
        {
            background.NineSliceSizing.Value = NineSliceSizing.FixedSize;
            background.PreserveAspect.Value = true;
        }
    }

    private static void BindOutfitToggle(
        Button button,
        InstallationRecordHandle record,
        string avatarName)
    {
        if (button == null || button.IsDestroyed || record == null || !record.IsHealthy) return;

        button.Enabled = true;
        button.Slot.AttachComponent<ButtonToggle>().TargetValue.Target = record.Enabled.Value;
        var labelDriver = button.Slot.AttachComponent<BooleanValueDriver<string>>();
        labelDriver.FalseValue.Value = "Outfit disabled • " + avatarName;
        labelDriver.TrueValue.Value = "Outfit enabled • " + avatarName;
        labelDriver.State.Value = record.Enabled.Value.Value;
        labelDriver.TargetField.Target = button.Label.Content;
        CopyEnabledState(record, labelDriver.State, button.Slot);
    }

    private static void BindOutfitToggle(
        ContextMenuItemSource item,
        InstallationRecordHandle record,
        string avatarName)
    {
        if (item == null || item.IsDestroyed || record == null || !record.IsHealthy) return;

        item.ButtonEnabled.Value = true;
        item.CloseMenuOnPress.Value = true;
        item.Color.Value = new colorX(0.45f, 0.82f, 0.62f, 1f);
        item.Slot.AttachComponent<ButtonToggle>().TargetValue.Target = record.Enabled.Value;
        var labelDriver = item.Slot.AttachComponent<BooleanValueDriver<string>>();
        labelDriver.FalseValue.Value = "Enable outfit on " + avatarName;
        labelDriver.TrueValue.Value = "Disable outfit on " + avatarName;
        labelDriver.State.Value = record.Enabled.Value.Value;
        labelDriver.TargetField.Target = item.Label;
        CopyEnabledState(record, labelDriver.State, item.Slot);
    }

    private static void CopyEnabledState(
        InstallationRecordHandle record,
        IField<bool> target,
        Slot host)
    {
        var copy = host.AttachComponent<ValueCopy<bool>>();
        copy.Source.Target = record.Enabled.Value;
        copy.Target.Target = target;
        copy.WriteBack.Value = false;
    }

    private static string FormatComponentKind(ModularAvatarComponentKind kind)
    {
        return kind switch
        {
            ModularAvatarComponentKind.MergeArmature => "Merge Armature",
            ModularAvatarComponentKind.OutfitRoot => "Outfit Root",
            ModularAvatarComponentKind.BoneProxy => "Bone Proxy",
            ModularAvatarComponentKind.BlendshapeSync => "Blendshape Sync",
            ModularAvatarComponentKind.MenuInstaller => "Menu Installer",
            ModularAvatarComponentKind.Parameters => "Parameters",
            ModularAvatarComponentKind.ObjectToggle => "Object Toggle",
            _ => kind.ToString()
        };
    }

    private static List<MergeSourceBinding> ResolveMergeSources(
        Slot attachmentRoot,
        AvatarPackageManifest manifest,
        Dictionary<ulong, IUnityObject> unityObjects)
    {
        var result = new List<MergeSourceBinding>();
        foreach (var definition in manifest.ModularAvatarComponents
                     .Where(component => component.Kind == ModularAvatarComponentKind.MergeArmature))
        {
            if (definition.GameObjectFileId <= 0 ||
                !unityObjects.TryGetValue((ulong)definition.GameObjectFileId, out var unityObject) ||
                unityObject is not UnityGameObject gameObject ||
                gameObject.frooxEngineSlot == null)
            {
                UnityPackageImporter.Warn(
                    "Could not resolve Merge Armature GameObject " + definition.GameObjectFileId +
                    " in '" + attachmentRoot.Name + "'.");
                continue;
            }

            var path = GetIndexPath(attachmentRoot, gameObject.frooxEngineSlot);
            if (path == null)
            {
                UnityPackageImporter.Warn(
                    "Merge Armature source '" + gameObject.frooxEngineSlot.Name +
                    "' is outside attachment root '" + attachmentRoot.Name + "'.");
                continue;
            }

            result.Add(new MergeSourceBinding
            {
                Definition = definition,
                SourceSlot = gameObject.frooxEngineSlot,
                IndexPath = path
            });
        }

        return result.OrderBy(binding => binding.IndexPath.Length).ToList();
    }

    private static List<BoneProxyBinding> ResolveBoneProxies(
        Slot attachmentRoot,
        AvatarPackageManifest manifest,
        Dictionary<ulong, IUnityObject> unityObjects)
    {
        var result = new List<BoneProxyBinding>();
        foreach (var definition in manifest.ModularAvatarComponents
                     .Where(component => component.Kind == ModularAvatarComponentKind.BoneProxy))
        {
            if (definition.GameObjectFileId <= 0 ||
                !unityObjects.TryGetValue((ulong)definition.GameObjectFileId, out var unityObject) ||
                unityObject is not UnityGameObject gameObject ||
                gameObject.frooxEngineSlot == null)
            {
                UnityPackageImporter.Warn(
                    "Could not resolve Bone Proxy GameObject " + definition.GameObjectFileId +
                    " in '" + attachmentRoot.Name + "'.");
                continue;
            }

            var path = GetIndexPath(attachmentRoot, gameObject.frooxEngineSlot);
            if (path == null)
            {
                UnityPackageImporter.Warn(
                    "Bone Proxy source '" + gameObject.frooxEngineSlot.Name +
                    "' is outside attachment root '" + attachmentRoot.Name + "'.");
                continue;
            }

            result.Add(new BoneProxyBinding
            {
                Definition = definition,
                SourceSlot = gameObject.frooxEngineSlot,
                IndexPath = path
            });
        }

        return result.OrderBy(binding => binding.IndexPath.Length).ToList();
    }

    private static List<BipedRig> FindAvatarRigs(Slot attachmentRoot)
    {
        return attachmentRoot.World.RootSlot.GetAllChildren(false)
            .Select(slot => slot.GetComponent<BipedRig>())
            .Where(rig => rig != null && !rig.IsDestroyed && rig.Bones.Count > 0)
            .Where(rig => rig.Slot != attachmentRoot && !rig.Slot.IsChildOf(attachmentRoot, true))
            .GroupBy(rig => rig.Slot)
            .Select(group => group.First())
            .OrderBy(rig => rig.Slot.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string GetTargetError(
        IReadOnlyList<MergeSourceBinding> mergeBindings,
        IReadOnlyList<BoneProxyBinding> boneProxyBindings,
        BipedRig targetRig)
    {
        if (targetRig == null || targetRig.IsDestroyed || targetRig.Slot == null || targetRig.Slot.IsDestroyed)
            return "Avatar is no longer available";

        try
        {
            return ValidateTarget(mergeBindings, boneProxyBindings, targetRig);
        }
        catch (Exception ex)
        {
            UnityPackageImporter.Warn(
                "Could not validate Modular Avatar target '" + targetRig.Slot.Name + "': " + ex);
            return "Avatar validation failed";
        }
    }

    private static string ValidateTarget(
        IReadOnlyList<MergeSourceBinding> mergeBindings,
        IReadOnlyList<BoneProxyBinding> boneProxyBindings,
        BipedRig targetRig)
    {
        var nestedRoots = mergeBindings.Select(binding => binding.SourceSlot).ToHashSet();
        foreach (var binding in mergeBindings)
        {
            var targetRoot = ResolveTargetRoot(targetRig.Slot, binding);
            if (targetRoot == null)
                return "Target armature path was not found";

            var sourceNodes = new Dictionary<string, Slot>();
            var targetNodes = new Dictionary<string, Slot>();
            var source = BuildHierarchy(binding.SourceSlot, nestedRoots, sourceNodes, binding.SourceSlot);
            var target = BuildHierarchy(targetRoot, new HashSet<Slot>(), targetNodes, null);
            var plan = ModularAvatarMergePlanner.CreatePlan(
                source,
                target,
                binding.Definition.Prefix,
                binding.Definition.Suffix);
            if (!plan.CanApply)
                return plan.Conflicts[0].Message;
        }

        foreach (var binding in boneProxyBindings)
        {
            if (ResolveBoneProxyTarget(targetRig, binding.Definition) == null)
            {
                return "Bone Proxy target was not found for " + binding.SourceSlot.Name;
            }
        }

        return null;
    }

    private static async Task<InstallationRecordHandle> InstallCopyAsync(
        Slot attachmentRoot,
        IReadOnlyList<MergeSourceBinding> mergeBindings,
        IReadOnlyList<BoneProxyBinding> boneProxyBindings,
        BipedRig targetRig,
        OutfitInstallIdentity identity)
    {
        if (attachmentRoot.IsDestroyed || targetRig == null || targetRig.IsDestroyed)
            throw new InvalidOperationException("The attachment or target avatar no longer exists.");

        var existingRecord = FindInstallationRecord(targetRig.Slot, identity);
        if (existingRecord?.Match == OutfitInstallMatch.Current)
        {
            if (!existingRecord.IsHealthy)
                throw new InvalidOperationException(
                    "This outfit has an incomplete installation record. " +
                    "The existing avatar state was left untouched.");
            return existingRecord;
        }
        if (existingRecord?.Match == OutfitInstallMatch.UpdateAvailable)
            throw new InvalidOperationException(
                "A different revision of this outfit is already installed. " +
                "The existing installation was left untouched.");

        Slot installedRoot = null;
        Slot recordRoot = null;
        var movedRetainedRoots = new List<Slot>();
        var movedBoneProxies = new List<Slot>();
        var ownedRoots = new List<OwnedRoot>();
        try
        {
            installedRoot = attachmentRoot.Duplicate(targetRig.Slot, false);
            installedRoot.Name = attachmentRoot.Name + " [Installed on " + targetRig.Slot.Name + "]";
            installedRoot.SetIdentityTransform();
            bool installedRootActive = installedRoot.ActiveSelf;
            installedRoot.ActiveSelf = false;
            ownedRoots.Add(new OwnedRoot
            {
                Slot = installedRoot,
                ActiveWhenEnabled = installedRootActive,
                Role = "Outfit Root"
            });

            RemoveCopiedInstaller(installedRoot);

            var clonedBindings = mergeBindings.Select(binding => new MergeSourceBinding
            {
                Definition = binding.Definition,
                SourceSlot = ResolveIndexPath(installedRoot, binding.IndexPath)
                    ?? throw new InvalidOperationException(
                        "The duplicated Merge Armature source could not be resolved for '" +
                        binding.SourceSlot.Name + "'."),
                IndexPath = binding.IndexPath
            }).ToList();
            var clonedBoneProxies = boneProxyBindings.Select(binding => new BoneProxyBinding
            {
                Definition = binding.Definition,
                SourceSlot = ResolveIndexPath(installedRoot, binding.IndexPath)
                    ?? throw new InvalidOperationException(
                        "The duplicated Bone Proxy source could not be resolved for '" +
                        binding.SourceSlot.Name + "'."),
                IndexPath = binding.IndexPath
            }).ToList();

            var nestedRoots = clonedBindings.Select(binding => binding.SourceSlot).ToHashSet();
            var prepared = new List<PreparedMerge>();
            foreach (var binding in clonedBindings)
            {
                var targetRoot = ResolveTargetRoot(targetRig.Slot, binding)
                    ?? throw new InvalidOperationException(
                        "Target armature path '" + binding.Definition.MergeTargetPath + "' was not found.");

                var sourceSlots = new Dictionary<string, Slot>();
                var targetSlots = new Dictionary<string, Slot>();
                var source = BuildHierarchy(binding.SourceSlot, nestedRoots, sourceSlots, binding.SourceSlot);
                var target = BuildHierarchy(targetRoot, new HashSet<Slot>(), targetSlots, null);
                var plan = ModularAvatarMergePlanner.CreatePlan(
                    source,
                    target,
                    binding.Definition.Prefix,
                    binding.Definition.Suffix);
                if (!plan.CanApply)
                    throw new InvalidOperationException(plan.Conflicts[0].Message);

                prepared.Add(new PreparedMerge
                {
                    Binding = binding,
                    SourceRoot = binding.SourceSlot,
                    TargetRoot = targetRoot,
                    Plan = plan,
                    SourceSlots = sourceSlots,
                    TargetSlots = targetSlots
                });
            }

            var boneMap = new Dictionary<Slot, Slot>();
            foreach (var merge in prepared)
            {
                foreach (var mapping in merge.Plan.Mappings)
                {
                    var source = merge.SourceSlots[mapping.SourceId];
                    var target = merge.TargetSlots[mapping.TargetId];
                    if (boneMap.TryGetValue(source, out var existing) && existing != target)
                        throw new InvalidOperationException(
                            "Bone '" + source.Name + "' maps to more than one target.");
                    boneMap[source] = target;
                }
            }

            var renderers = EnumerateSelfAndChildren(installedRoot)
                .Select(slot => slot.GetComponent<SkinnedMeshRenderer>())
                .Where(renderer => renderer != null)
                .ToList();
            var ownedComponents = EnumerateSelfAndChildren(installedRoot)
                .SelectMany(slot => slot.Components)
                .Where(component => component is MeshRenderer || component is SkinnedMeshRenderer)
                .Select(component => new OwnedComponent
                {
                    Component = component,
                    EnabledWhenOutfitEnabled = component.Enabled,
                    Role = component is SkinnedMeshRenderer
                        ? "Skinned Mesh Renderer"
                        : "Mesh Renderer"
                })
                .ToList();
            foreach (var renderer in renderers)
            {
                for (int i = 0; i < renderer.Bones.Count; i++)
                {
                    var bone = renderer.Bones[i];
                    if (bone != null && boneMap.TryGetValue(bone, out var target))
                        renderer.Bones[i] = target;
                }

                SkinnedBoundsPolicy.ApplyAfterBones(renderer, "Modular Avatar installation");
            }

            foreach (var merge in prepared)
            {
                foreach (var retained in merge.Plan.RetainedRoots)
                {
                    var source = merge.SourceSlots[retained.SourceId];
                    if (boneMap.ContainsKey(source)) continue;
                    var targetParent = merge.TargetSlots[retained.TargetParentId];
                    bool activeWhenEnabled = source.ActiveSelf;
                    source.ActiveSelf = false;
                    source.SetParent(targetParent, false);
                    movedRetainedRoots.Add(source);
                    ownedRoots.Add(new OwnedRoot
                    {
                        Slot = source,
                        ActiveWhenEnabled = activeWhenEnabled,
                        Role = "Retained Bone Root"
                    });
                }
            }

            foreach (var proxy in clonedBoneProxies)
            {
                var target = ResolveBoneProxyTarget(targetRig, proxy.Definition)
                    ?? throw new InvalidOperationException(
                        "Bone Proxy target was not found for '" + proxy.SourceSlot.Name + "'.");
                bool activeWhenEnabled = proxy.SourceSlot.ActiveSelf;
                proxy.SourceSlot.ActiveSelf = false;
                ApplyBoneProxy(proxy.SourceSlot, target, proxy.Definition);
                if (proxy.SourceSlot != installedRoot)
                {
                    movedBoneProxies.Add(proxy.SourceSlot);
                    ownedRoots.Add(new OwnedRoot
                    {
                        Slot = proxy.SourceSlot,
                        ActiveWhenEnabled = activeWhenEnabled,
                        Role = "Bone Proxy Root"
                    });
                }
            }

            await SkinnedBoundsPolicy.StabilizeBeforeEnableAsync(
                renderers,
                "Modular Avatar activation");

            var record = CreateInstallationRecord(
                targetRig,
                attachmentRoot.Name,
                installedRoot,
                identity,
                ownedRoots,
                ownedComponents,
                out recordRoot);
            return record;
        }
        catch
        {
            if (recordRoot != null && !recordRoot.IsDestroyed)
                recordRoot.Destroy();
            foreach (var moved in movedRetainedRoots.Where(slot => slot != null && !slot.IsDestroyed))
                moved.Destroy();
            foreach (var moved in movedBoneProxies.Where(slot => slot != null && !slot.IsDestroyed))
                moved.Destroy();
            if (installedRoot != null && !installedRoot.IsDestroyed)
                installedRoot.Destroy();
            throw;
        }
    }

    private static InstallationRecordHandle CreateInstallationRecord(
        BipedRig targetRig,
        string sourceName,
        Slot installedRoot,
        OutfitInstallIdentity identity,
        IReadOnlyList<OwnedRoot> ownedRoots,
        IReadOnlyList<OwnedComponent> ownedComponents,
        out Slot recordRoot)
    {
        var recordsRoot = targetRig.Slot.Children.FirstOrDefault(child => child.Name == RecordsSlotName);
        if (recordsRoot == null)
        {
            recordsRoot = targetRig.Slot.AddSlot(RecordsSlotName);
            recordsRoot.OrderOffset = 2000;
        }

        string displayName = sourceName.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
            ? sourceName[..^7]
            : sourceName;
        string shortId = identity.PrefabGuid.Length > 8
            ? identity.PrefabGuid[..8]
            : identity.PrefabGuid;
        recordRoot = recordsRoot.AddSlot("[Installing] " + displayName + " " + shortId);

        var metadata = recordRoot.AddSlot("Metadata");
        AddValueField(metadata, RecordTypeFieldName, RecordMarker);
        AddValueField(metadata, SchemaFieldName, OutfitInstallIdentity.CurrentSchemaVersion);
        AddValueField(metadata, PrefabGuidFieldName, identity.PrefabGuid);
        AddValueField(metadata, ContentHashFieldName, identity.ContentHash);
        AddValueField(metadata, SourceNameFieldName, displayName);
        AddValueField(metadata, AvatarSignatureFieldName, BuildAvatarSignature(targetRig));

        var references = recordRoot.AddSlot("References");
        AddReferenceField(references, TargetAvatarFieldName, targetRig.Slot);
        AddReferenceField(references, InstalledRootFieldName, installedRoot);

        var ownedObjects = references.AddSlot(OwnedObjectsSlotName);
        var ownedComponentReferences = references.AddSlot(OwnedComponentsSlotName);
        var effectiveOwnedRoots = ownedRoots
            .Where(owned => owned?.Slot != null && !owned.Slot.IsDestroyed)
            .GroupBy(owned => owned.Slot)
            .Select(group => group.First())
            .Where(owned => !ownedRoots.Any(other =>
                other?.Slot != null &&
                other.Slot != owned.Slot &&
                !other.Slot.IsDestroyed &&
                owned.Slot.IsChildOf(other.Slot, true)))
            .ToList();

        var state = recordRoot.AddSlot(StateSlotName);
        var enabledSlot = state.AddSlot(EnabledSlotName);
        var enabled = enabledSlot.AttachComponent<ValueMultiDriver<bool>>();
        enabled.Value.Value = false;

        for (int i = 0; i < effectiveOwnedRoots.Count; i++)
        {
            var owned = effectiveOwnedRoots[i];
            var referenceSlot = ownedObjects.AddSlot((i + 1).ToString("D2") + " " + owned.Role);
            AddReferenceField(referenceSlot, "Object", owned.Slot);
            AddValueField(referenceSlot, "Active When Enabled", owned.ActiveWhenEnabled);

            var activeDriver = enabledSlot.AttachComponent<BooleanValueDriver<bool>>();
            activeDriver.FalseValue.Value = false;
            activeDriver.TrueValue.Value = owned.ActiveWhenEnabled;
            activeDriver.State.Value = false;
            activeDriver.TargetField.Target = owned.Slot.ActiveSelf_Field;
            enabled.Drives.Add().ForceLink(activeDriver.State);
        }

        var effectiveOwnedComponents = ownedComponents
            .Where(owned => owned?.Component != null && !owned.Component.IsDestroyed)
            .GroupBy(owned => owned.Component)
            .Select(group => group.First())
            .ToList();
        for (int i = 0; i < effectiveOwnedComponents.Count; i++)
        {
            var owned = effectiveOwnedComponents[i];
            var referenceSlot = ownedComponentReferences.AddSlot(
                (i + 1).ToString("D2") + " " + owned.Role);
            AddComponentReferenceField(referenceSlot, "Component", owned.Component);
            AddValueField(referenceSlot, "Enabled With Outfit", owned.EnabledWhenOutfitEnabled);

            var componentDriver = enabledSlot.AttachComponent<BooleanValueDriver<bool>>();
            componentDriver.FalseValue.Value = false;
            componentDriver.TrueValue.Value = owned.EnabledWhenOutfitEnabled;
            componentDriver.State.Value = false;
            componentDriver.TargetField.Target = owned.Component.EnabledField;
            enabled.Drives.Add().ForceLink(componentDriver.State);
        }

        if (effectiveOwnedRoots.Count == 0)
            throw new InvalidOperationException("The installation produced no owned outfit objects.");

        enabled.Value.Value = true;
        recordRoot.Name = displayName + " • " + shortId;
        return new InstallationRecordHandle
        {
            RecordRoot = recordRoot,
            TargetAvatar = targetRig.Slot,
            InstalledRoot = installedRoot,
            Enabled = enabled,
            SchemaVersion = OutfitInstallIdentity.CurrentSchemaVersion,
            PrefabGuid = identity.PrefabGuid,
            ContentHash = identity.ContentHash,
            Match = OutfitInstallMatch.Current
        };
    }

    private static InstallationRecordHandle FindInstallationRecord(
        Slot avatarRoot,
        OutfitInstallIdentity identity)
    {
        if (avatarRoot == null || avatarRoot.IsDestroyed || identity == null) return null;

        foreach (var candidate in EnumerateSelfAndChildren(avatarRoot))
        {
            var metadata = candidate.Children.FirstOrDefault(child => child.Name == "Metadata");
            if (metadata == null ||
                !string.Equals(ReadDirectStringField(metadata, RecordTypeFieldName), RecordMarker,
                    StringComparison.Ordinal))
                continue;

            string prefabGuid = ReadDirectStringField(metadata, PrefabGuidFieldName);
            string contentHash = ReadDirectStringField(metadata, ContentHashFieldName);
            int schemaVersion = ReadDirectValueField(metadata, SchemaFieldName, 0);
            var match = identity.Compare(prefabGuid, contentHash);
            if (match == OutfitInstallMatch.None) continue;

            var references = candidate.Children.FirstOrDefault(child => child.Name == "References");
            var state = candidate.Children.FirstOrDefault(child => child.Name == StateSlotName);
            var enabledSlot = state?.Children.FirstOrDefault(child => child.Name == EnabledSlotName);

            return new InstallationRecordHandle
            {
                RecordRoot = candidate,
                TargetAvatar = references == null
                    ? null
                    : ReadDirectSlotReference(references, TargetAvatarFieldName),
                InstalledRoot = references == null
                    ? null
                    : ReadDirectSlotReference(references, InstalledRootFieldName),
                Enabled = enabledSlot?.GetComponent<ValueMultiDriver<bool>>(),
                SchemaVersion = schemaVersion,
                PrefabGuid = prefabGuid,
                ContentHash = contentHash,
                Match = match
            };
        }

        return null;
    }

    private static void AddValueField<T>(Slot parent, string name, T value)
    {
        var fieldSlot = parent.AddSlot(name);
        fieldSlot.AttachComponent<ValueField<T>>().Value.Value = value;
    }

    private static void AddReferenceField(Slot parent, string name, Slot target)
    {
        var fieldSlot = parent.AddSlot(name);
        fieldSlot.AttachComponent<ReferenceField<Slot>>().Reference.Target = target;
    }

    private static void AddComponentReferenceField(Slot parent, string name, Component target)
    {
        var fieldSlot = parent.AddSlot(name);
        fieldSlot.AttachComponent<ReferenceField<Component>>().Reference.Target = target;
    }

    private static string ReadDirectStringField(Slot root, string name)
    {
        return ReadDirectValueField(root, name, "");
    }

    private static T ReadDirectValueField<T>(Slot root, string name, T fallback)
    {
        var field = root.Children.FirstOrDefault(child => child.Name == name)
            ?.GetComponent<ValueField<T>>();
        return field == null ? fallback : field.Value.Value;
    }

    private static Slot ReadDirectSlotReference(Slot root, string name)
    {
        return root.Children.FirstOrDefault(child => child.Name == name)
            ?.GetComponent<ReferenceField<Slot>>()?.Reference.Target;
    }

    private static string BuildAvatarSignature(BipedRig rig)
    {
        var names = new List<string> { rig.Slot.Name, rig.Bones.Count.ToString() };
        foreach (BodyNode bodyNode in Enum.GetValues(typeof(BodyNode)))
        {
            if (bodyNode == BodyNode.NONE) continue;
            var bone = rig.TryGetBone(bodyNode);
            names.Add(bodyNode + ":" + (bone == null || bone.IsDestroyed ? "" : bone.Name));
        }
        string signatureSource = string.Join("\n", names);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signatureSource)));
    }

    private static void RemoveCopiedInstaller(Slot installedRoot)
    {
        var copiedMenu = installedRoot.Children.FirstOrDefault(child => child.Name == InstallerSlotName);
        foreach (var component in installedRoot.Components.ToArray())
        {
            if (component is RootContextMenuItem rootItem &&
                rootItem.Item.Target != null &&
                rootItem.Item.Target.Slot == copiedMenu)
            {
                rootItem.Destroy();
            }
        }
        copiedMenu?.Destroy();
    }

    private static void AddStatusItem(Slot itemsRoot, string label)
    {
        var slot = itemsRoot.AddSlot(label);
        var source = slot.AttachComponent<ContextMenuItemSource>();
        source.Label.Value = label;
        source.Color.Value = new colorX(0.6f, 0.6f, 0.6f, 1f);
        source.ButtonEnabled.Value = false;
    }

    private static void AddLocalPressedHandler(ContextMenuItemSource source, ButtonEventHandler handler)
    {
        // Publicizer exposes ContextMenuItemSource's event and its compiler backing
        // field with the same name. Reflection selects the event unambiguously.
        var eventInfo = typeof(ContextMenuItemSource).GetEvent(
            "LocalPressed",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (eventInfo == null)
            throw new MissingMemberException(typeof(ContextMenuItemSource).FullName, "LocalPressed");
        eventInfo.AddEventHandler(source, handler);
    }

    private static void AddLocalPressedHandler(Button button, ButtonEventHandler handler)
    {
        // Publicizer exposes both the local event and its backing field under the
        // same name, so ordinary event syntax is ambiguous at compile time.
        var eventInfo = typeof(Button).GetEvent(
            "LocalPressed",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (eventInfo == null)
            throw new MissingMemberException(typeof(Button).FullName, "LocalPressed");
        eventInfo.AddEventHandler(button, handler);
    }

    private static Slot ResolveBoneProxyTarget(
        BipedRig targetRig,
        ModularAvatarComponentDefinition definition)
    {
        if (targetRig == null || targetRig.IsDestroyed) return null;

        string subPath = definition.BoneProxySubPath?.Trim() ?? "";
        Slot target;
        if (subPath == "$$AVATAR")
        {
            target = targetRig.Slot;
            subPath = "";
        }
        else if (definition.BoneReference == 55) // UnityEngine.HumanBodyBones.LastBone
        {
            if (string.IsNullOrWhiteSpace(subPath)) return null;
            target = targetRig.Slot;
        }
        else
        {
            BodyNode bodyNode = MapUnityHumanoidBone(definition.BoneReference);
            if (bodyNode == BodyNode.NONE) return null;
            target = targetRig.TryGetBone(bodyNode);
        }

        if (target == null || target.IsDestroyed) return null;
        foreach (string part in SplitPath(subPath))
        {
            target = target.Children.FirstOrDefault(child =>
                child.Name.Equals(part, StringComparison.Ordinal));
            if (target == null || target.IsDestroyed) return null;
        }
        return target;
    }

    private static void ApplyBoneProxy(
        Slot source,
        Slot target,
        ModularAvatarComponentDefinition definition)
    {
        if (source == null || source.IsDestroyed || target == null || target.IsDestroyed)
            throw new InvalidOperationException("The Bone Proxy source or target no longer exists.");
        if (target == source || target.IsChildOf(source, true))
            throw new InvalidOperationException("A Bone Proxy cannot be parented underneath itself.");

        var originalPosition = source.GlobalPosition;
        var originalRotation = source.GlobalRotation;
        var originalScale = source.GlobalScale;

        switch (definition.BoneProxyAttachmentMode)
        {
            case 2: // AsChildKeepWorldPose
                source.SetParent(target, true);
                break;
            case 3: // AsChildKeepRotation
                source.SetParent(target, true);
                source.GlobalPosition = target.GlobalPosition;
                source.GlobalRotation = originalRotation;
                break;
            case 4: // AsChildKeepPosition
                source.SetParent(target, true);
                source.GlobalPosition = originalPosition;
                source.GlobalRotation = target.GlobalRotation;
                break;
            default: // Unset and AsChildAtRoot
                source.SetParent(target, false);
                source.LocalPosition = new float3(0f, 0f, 0f);
                source.LocalRotation = new floatQ(0f, 0f, 0f, 1f);
                break;
        }

        if (definition.BoneProxyMatchScale)
            source.LocalScale = new float3(1f, 1f, 1f);
        else
            source.GlobalScale = originalScale;
    }

    private static BodyNode MapUnityHumanoidBone(int boneReference)
    {
        // UnityEngine.HumanBodyBones numeric values are serialized into prefab
        // YAML. Keep this mapping explicit so importing does not require Unity.
        return boneReference switch
        {
            0 => BodyNode.Hips,
            1 => BodyNode.LeftUpperLeg,
            2 => BodyNode.RightUpperLeg,
            3 => BodyNode.LeftLowerLeg,
            4 => BodyNode.RightLowerLeg,
            5 => BodyNode.LeftFoot,
            6 => BodyNode.RightFoot,
            7 => BodyNode.Spine,
            8 => BodyNode.Chest,
            9 => BodyNode.Neck,
            10 => BodyNode.Head,
            11 => BodyNode.LeftShoulder,
            12 => BodyNode.RightShoulder,
            13 => BodyNode.LeftUpperArm,
            14 => BodyNode.RightUpperArm,
            15 => BodyNode.LeftLowerArm,
            16 => BodyNode.RightLowerArm,
            17 => BodyNode.LeftHand,
            18 => BodyNode.RightHand,
            19 => BodyNode.LeftToes,
            20 => BodyNode.RightToes,
            21 => BodyNode.LeftEye,
            22 => BodyNode.RightEye,
            23 => BodyNode.Jaw,
            24 => BodyNode.LeftThumb_Proximal,
            25 => BodyNode.LeftThumb_Distal,
            26 => BodyNode.LeftThumb_Tip,
            27 => BodyNode.LeftIndexFinger_Proximal,
            28 => BodyNode.LeftIndexFinger_Intermediate,
            29 => BodyNode.LeftIndexFinger_Distal,
            30 => BodyNode.LeftMiddleFinger_Proximal,
            31 => BodyNode.LeftMiddleFinger_Intermediate,
            32 => BodyNode.LeftMiddleFinger_Distal,
            33 => BodyNode.LeftRingFinger_Proximal,
            34 => BodyNode.LeftRingFinger_Intermediate,
            35 => BodyNode.LeftRingFinger_Distal,
            36 => BodyNode.LeftPinky_Proximal,
            37 => BodyNode.LeftPinky_Intermediate,
            38 => BodyNode.LeftPinky_Distal,
            39 => BodyNode.RightThumb_Proximal,
            40 => BodyNode.RightThumb_Distal,
            41 => BodyNode.RightThumb_Tip,
            42 => BodyNode.RightIndexFinger_Proximal,
            43 => BodyNode.RightIndexFinger_Intermediate,
            44 => BodyNode.RightIndexFinger_Distal,
            45 => BodyNode.RightMiddleFinger_Proximal,
            46 => BodyNode.RightMiddleFinger_Intermediate,
            47 => BodyNode.RightMiddleFinger_Distal,
            48 => BodyNode.RightRingFinger_Proximal,
            49 => BodyNode.RightRingFinger_Intermediate,
            50 => BodyNode.RightRingFinger_Distal,
            51 => BodyNode.RightPinky_Proximal,
            52 => BodyNode.RightPinky_Intermediate,
            53 => BodyNode.RightPinky_Distal,
            54 => BodyNode.UpperChest,
            _ => BodyNode.NONE
        };
    }

    private static Slot ResolveTargetRoot(Slot avatarRoot, MergeSourceBinding binding)
    {
        var parts = SplitPath(binding.Definition.MergeTargetPath);
        string targetName = parts.Length > 0 ? parts[^1] : null;
        if (string.IsNullOrEmpty(targetName) &&
            ModularAvatarMergePlanner.TryRemoveAffixes(
                binding.SourceSlot.Name,
                binding.Definition.Prefix,
                binding.Definition.Suffix,
                out var mappedName))
        {
            targetName = mappedName;
        }

        if (string.IsNullOrEmpty(targetName)) return null;

        var candidates = EnumerateSelfAndChildren(avatarRoot)
            .Where(slot => slot.Name.Equals(targetName, StringComparison.Ordinal))
            .ToList();
        if (parts.Length > 1)
        {
            var exact = candidates.Where(candidate => PathEndsWith(candidate, avatarRoot, parts)).ToList();
            if (exact.Count == 1) return exact[0];
            if (exact.Count > 1) return null;
        }

        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static MergeHierarchyNode BuildHierarchy(
        Slot slot,
        HashSet<Slot> nestedMergeRoots,
        Dictionary<string, Slot> nodes,
        Slot currentMergeRoot)
    {
        string id = slot.ReferenceID.ToString();
        nodes[id] = slot;
        bool startsNested = currentMergeRoot != null && slot != currentMergeRoot && nestedMergeRoots.Contains(slot);
        return new MergeHierarchyNode
        {
            Id = id,
            Name = slot.Name,
            StartsNestedMerge = startsNested,
            Children = startsNested
                ? Array.Empty<MergeHierarchyNode>()
                : slot.Children.Select(child =>
                    BuildHierarchy(child, nestedMergeRoots, nodes, currentMergeRoot)).ToArray()
        };
    }

    private static IEnumerable<Slot> EnumerateSelfAndChildren(Slot root)
    {
        yield return root;
        foreach (var child in root.GetAllChildren(false)) yield return child;
    }

    private static int[] GetIndexPath(Slot root, Slot descendant)
    {
        var path = new List<int>();
        var current = descendant;
        while (current != null && current != root)
        {
            path.Add(current.ChildIndex);
            current = current.Parent;
        }

        if (current != root) return null;
        path.Reverse();
        return path.ToArray();
    }

    private static Slot ResolveIndexPath(Slot root, IReadOnlyList<int> path)
    {
        var current = root;
        foreach (int index in path)
        {
            if (index < 0 || index >= current.ChildrenCount) return null;
            current = current[index];
        }
        return current;
    }

    private static string[] SplitPath(string path) =>
        (path ?? "")
        .Replace('\\', '/')
        .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

    private static bool PathEndsWith(Slot candidate, Slot root, IReadOnlyList<string> suffix)
    {
        var names = new List<string>();
        var current = candidate;
        while (current != null && current != root)
        {
            names.Add(current.Name);
            current = current.Parent;
        }
        names.Reverse();
        if (suffix.Count > names.Count) return false;
        int offset = names.Count - suffix.Count;
        for (int i = 0; i < suffix.Count; i++)
        {
            if (!names[offset + i].Equals(suffix[i], StringComparison.Ordinal)) return false;
        }
        return true;
    }
}
