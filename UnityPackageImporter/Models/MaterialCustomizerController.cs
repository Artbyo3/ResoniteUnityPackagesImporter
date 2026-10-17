#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.CommonAvatar;
using FrooxEngine.Store;
using FrooxEngine.UIX;
using Renderite.Shared;
using UnityPackageImporter.UI;

namespace UnityPackageImporter.Models;

/// <summary>Builds per-slot material controls from the station's native scrollable popover rows.</summary>
internal sealed class MaterialCustomizerController
{
    private readonly StationTemplate ui;
    private readonly Func<UnityPrefabImportTask> getCurrent;
    private readonly Func<UnityProjectImporter> getImporter;
    private readonly Func<bool> isAlive;
    private readonly Func<bool> isBusy;
    private readonly Action<string> setStage;
    private readonly Action<bool> setBusy;
    private readonly Action<Func<Task>> schedule;
    private readonly Action rebuildPreview;
    private readonly List<Button> entryButtons = new();
    private readonly bool isNative;
    private readonly Slot mainRoot;
    private readonly Slot mainList;
    private readonly Slot mainRowTemplate;
    private readonly BooleanValueDriver<bool> mainVisible;
    private readonly Slot materialRoot;
    private readonly Slot materialList;
    private readonly Slot materialRowTemplate;
    private readonly BooleanValueDriver<bool> materialVisible;
    private readonly Slot searchFieldTemplate;
    private readonly Slot folderPickerRoot;
    private readonly Slot folderList;
    private readonly Slot folderChoiceRootTemplate;
    private readonly Slot folderChoiceTemplate;
    private readonly Button folderNavUp;
    private readonly Sync<bool>? folderCanGoUp;
    private readonly Sync<int>? folderBrowseIndex;
    private readonly Text breadcrumbPath;
    private readonly BooleanValueDriver<bool> folderPickerVisible;
    private readonly Text meshCountText;
    private readonly Text materialCountText;
    private readonly Text pickerContextText;
    private readonly Text pickerContextMaterialText;
    private readonly Slot? pickerContextSphere;
    private readonly Text? folderPickerTitle;
    private readonly Button meshSearchClear;
    private readonly Button materialSearchClear;
    private readonly Button foldersOpenButton;
    private readonly Button folderRootButton;
    private readonly Dictionary<(UnityPrefabImportTask Item, object Renderer, int Index), IAssetProvider<Material>> originals = new();

    private Slot mainSearchField;
    private Slot materialSearchField;
    private Slot mainSearchHint;
    private Slot materialSearchHint;
    private Text mainSearchText;
    private Text materialSearchText;
    private Slot mainDismissButton;
    private Text mainTitle;
    private Text materialTitle;
    private string mainQuery = "";
    private string materialQuery = "";
    private string currentFolder = "";
    private string browsedFolder = "";
    private string lastBrowsedFolder = "";
    private int lastBrowseDepthIndex;
    private MaterialSlotBinding selectedSlot;
    private MaterialRendererBinding expandedRenderer;
    private readonly HashSet<string> expandedRendererNames = new(StringComparer.OrdinalIgnoreCase);
    private UnityPrefabImportTask displayedItem;
    private IReadOnlyList<MaterialRendererBinding> renderers = Array.Empty<MaterialRendererBinding>();
    private PackageMaterialCatalog materialCatalog;
    private int mainSearchGeneration;
    private int materialSearchGeneration;
    private CancellationTokenSource materialRowsCts;
    private CancellationTokenSource? meshSpheresCts;
    private readonly ConcurrentDictionary<string, Uri> thumbnailUriCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<Uri?>> thumbnailTaskCache = new(StringComparer.OrdinalIgnoreCase);

    private sealed class FittedTextBinding
    {
        private readonly Slot? root;
        private readonly Text? directText;
        private readonly Text[] childTexts;

        public FittedTextBinding(Slot? root)
        {
            this.root = root;
            if (root == null || root.IsDestroyed)
            {
                childTexts = Array.Empty<Text>();
                return;
            }
            directText = root.GetComponent<Text>();
            childTexts = root.GetComponentsInChildren<Text>().Where(t => t != directText).ToArray();
        }

        public void SetText(string value)
        {
            if (root == null || root.IsDestroyed) return;
            if (childTexts.Length > 0)
            {
                if (directText != null && !directText.IsDestroyed)
                {
                    if (!directText.Content.IsDriven) directText.Content.Value = value;
                    directText.Enabled = false;
                    directText.InteractionTarget.Value = false;
                }
                foreach (var childText in childTexts)
                {
                    if (childText == null || childText.IsDestroyed) continue;
                    childText.ParseRichText.Value = true;
                    if (!childText.Content.IsDriven) childText.Content.Value = value;
                    childText.Enabled = true;
                    childText.InteractionTarget.Value = false;
                }
            }
            else if (directText != null && !directText.IsDestroyed)
            {
                directText.ParseRichText.Value = false;
                if (!directText.Content.IsDriven) directText.Content.Value = value;
                directText.Enabled = true;
                directText.InteractionTarget.Value = false;
            }
        }
    }

    private sealed class NativeMeshCardHolder
    {
        public MaterialRendererBinding Renderer;
        public Slot CardSlot;
        public Slot Header;
        public ValueMultiDriver<bool> MultiDriver;
        public BooleanValueDriver<float> HeightDriver;
        public SmoothValue<float> Smooth;
        public BooleanValueDriver<RectOrientation> ChevronDriver;
        public LayoutElement RevealLayout;
        public Slot SlotsContainer;
        public Slot SlotTemplate;
        public float TargetHeight;
        public bool IsPopulated;
        public readonly Dictionary<MaterialSlotBinding, (FittedTextBinding NameLabel, Slot? SphereSlot, Button RestoreBtn)> SlotRows = new();
    }

    private sealed class PooledMaterialCard
    {
        public required Slot Card;
        public FittedTextBinding? NameLabel;
        public Slot? OutlineSlot;
        public Slot? SphereSlot;
        public Image? SphereImage;
        public Func<Task>? CurrentAction;
    }

    private sealed class PooledFolderRow
    {
        public required Slot Row;
        public FittedTextBinding? Label;
        public Action? CurrentAction;
    }

    private readonly List<NativeMeshCardHolder> nativeCards = new();
    private readonly List<PooledMaterialCard> pooledMaterialCards = new();
    private readonly List<PooledFolderRow> pooledFolderRows = new();
    private FittedTextBinding? folderChoiceRootLabel;
    private UnityPrefabImportTask cachedMeshCardsItem;

    internal MaterialCustomizerController(
        StationTemplate ui,
        Func<UnityPrefabImportTask> getCurrent,
        Func<UnityProjectImporter> getImporter,
        Func<bool> isAlive,
        Func<bool> isBusy,
        Action<string> setStage,
        Action<bool> setBusy,
        Action<Func<Task>> schedule,
        Action rebuildPreview)
    {
        this.ui = ui;
        this.getCurrent = getCurrent;
        this.getImporter = getImporter;
        this.isAlive = isAlive;
        this.isBusy = isBusy;
        this.setStage = setStage;
        this.setBusy = setBusy;
        this.schedule = schedule;
        this.rebuildPreview = rebuildPreview;

        Slot customizerPreview = Descendants(ui.Root).FirstOrDefault(slot => slot.Name == "Material Customizer Preview");
        if (customizerPreview != null)
        {
            isNative = true;
            searchFieldTemplate = null;
            mainRoot = customizerPreview.Children.FirstOrDefault(slot => slot.Name == "Customize Materials")
                ?? throw new InvalidOperationException("Material Customizer Preview is missing Customize Materials.");
            materialRoot = customizerPreview.Children.FirstOrDefault(slot => slot.Name == "Choose Material")
                ?? throw new InvalidOperationException("Material Customizer Preview is missing Choose Material.");
            folderPickerRoot = customizerPreview.Children.FirstOrDefault(slot => slot.Name == "Folder Picker Popover");

            mainVisible = customizerPreview.GetComponents<BooleanValueDriver<bool>>()
                .FirstOrDefault(driver => driver.TargetField.Target == mainRoot.ActiveSelf_Field)
                ?? customizerPreview.GetComponents<BooleanValueDriver<bool>>().FirstOrDefault()
                ?? throw new InvalidOperationException("Missing visibility driver for Customize Materials.");
            mainVisible.State.Value = false;

            materialVisible = customizerPreview.GetComponents<BooleanValueDriver<bool>>()
                .FirstOrDefault(driver => driver.TargetField.Target == materialRoot.ActiveSelf_Field)
                ?? customizerPreview.GetComponents<BooleanValueDriver<bool>>().ElementAtOrDefault(1)
                ?? throw new InvalidOperationException("Missing visibility driver for Choose Material.");
            materialVisible.State.Value = false;

            mainList = Descendants(mainRoot).FirstOrDefault(slot => slot.Name == "meshes-content")
                ?? throw new InvalidOperationException("Customize Materials is missing meshes-content.");
            mainRowTemplate = mainList.Children.FirstOrDefault(slot => slot.Name == "BodyMain")
                ?? mainList.Children.FirstOrDefault()
                ?? throw new InvalidOperationException("meshes-content is missing BodyMain template.");
            materialList = Descendants(materialRoot).FirstOrDefault(slot => slot.Name == "materials-content")
                ?? throw new InvalidOperationException("Choose Material is missing materials-content.");
            materialRowTemplate = materialList.Children.FirstOrDefault(slot => slot.Name == "Skin_Default")
                ?? materialList.Children.FirstOrDefault()
                ?? throw new InvalidOperationException("materials-content is missing Skin_Default template.");

            foreach (var driver in customizerPreview.GetComponentsInChildren<BooleanValueDriver<bool>>())
            {
                if (driver == mainVisible || driver == materialVisible) continue;
                var targetSlot = driver.TargetField.Target?.FindNearestParent<Slot>();
                if (targetSlot != null &&
                    (targetSlot.Parent == mainList ||
                     targetSlot.Parent == materialList ||
                     (folderPickerRoot != null && targetSlot != folderPickerRoot && targetSlot.IsChildOf(folderPickerRoot))))
                {
                    driver.TargetField.Target = null;
                    driver.Enabled = false;
                }
            }

            foreach (Slot child in mainList.Children.ToArray())
            {
                if (child != mainRowTemplate) child.Destroy();
            }
            foreach (Slot child in materialList.Children.ToArray())
            {
                if (child != materialRowTemplate) child.Destroy();
            }

            StripMaterialCardFixtureDrivers(materialRowTemplate);
            mainRowTemplate.ActiveSelf = false;
            materialRowTemplate.ActiveSelf = false;

            Slot templateHeader = FindDescendant(mainRowTemplate, "mesh-0-header");
            if (templateHeader != null)
            {
                foreach (var eq in templateHeader.GetComponents<ValueEqualityDriver<int>>())
                {
                    eq.Target.Target = null;
                    eq.TargetValue.Target = null;
                    eq.Destroy();
                }
                foreach (var cycle in templateHeader.GetComponents<ButtonValueCycle<int>>())
                {
                    cycle.TargetValue.Target = null;
                    cycle.Destroy();
                }
            }

            Slot templateSlotsContainer = FindDescendant(mainRowTemplate, "Material Slots");
            if (templateSlotsContainer != null)
            {
                var children = templateSlotsContainer.Children.ToList();
                for (int i = 1; i < children.Count; i++)
                {
                    children[i].Destroy();
                }
                if (children.Count > 0)
                {
                    children[0].ActiveSelf = false;
                }
            }

            mainTitle = Descendants(mainRoot).FirstOrDefault(slot => slot.Name == "mesh-panel-title")?.GetComponent<Text>();
            materialTitle = Descendants(materialRoot).FirstOrDefault(slot => slot.Name == "picker-panel-title")?.GetComponent<Text>();
            meshCountText = Descendants(mainRoot).FirstOrDefault(slot => slot.Name == "mesh-count")?.GetComponent<Text>();
            materialCountText = Descendants(materialRoot).FirstOrDefault(slot => slot.Name == "material-count")?.GetComponent<Text>();
            pickerContextText = Descendants(materialRoot).FirstOrDefault(slot => slot.Name == "picker-context")?.GetComponent<Text>();
            pickerContextMaterialText = Descendants(materialRoot).FirstOrDefault(slot => slot.Name == "picker-context-material")?.GetComponent<Text>();
            pickerContextSphere = Descendants(materialRoot).FirstOrDefault(slot => slot.Name == "picker-context-sphere");
            folderPickerTitle = folderPickerRoot != null
                ? Descendants(folderPickerRoot).FirstOrDefault(slot => slot.Name == "folder-picker-panel-title")?.GetComponent<Text>()
                : null;

            mainDismissButton = Descendants(mainRoot).FirstOrDefault(slot => slot.Name == "mesh-panel-close");
            if (mainDismissButton != null)
            {
                var closeBtn = mainDismissButton.GetComponent<Button>();
                if (closeBtn != null) NativeButtonEvents.Pressed(closeBtn, (_, _) => CloseAll());
            }

            Slot matDismiss = Descendants(materialRoot).FirstOrDefault(slot => slot.Name == "picker-panel-close");
            if (matDismiss != null)
            {
                var closeBtn = matDismiss.GetComponent<Button>();
                if (closeBtn != null) NativeButtonEvents.Pressed(closeBtn, (_, _) => ClosePickerOnly());
            }

            mainSearchField = Descendants(mainRoot).FirstOrDefault(slot => slot.Name == "mesh-search-input");
            materialSearchField = Descendants(materialRoot).FirstOrDefault(slot => slot.Name == "material-search-input");

            meshSearchClear = Descendants(mainRoot).FirstOrDefault(slot => slot.Name == "mesh-search-clear")?.GetComponent<Button>();
            if (meshSearchClear != null)
            {
                UnityStationHandle.ReleaseFixtureActions(meshSearchClear);
                NativeButtonEvents.Pressed(meshSearchClear, (_, _) =>
                {
                    if (mainSearchText != null) mainSearchText.Content.Value = "";
                    UpdateSearchHint(mainSearchField, mainSearchHint);
                    RefreshMainRows("");
                });
            }

            materialSearchClear = Descendants(materialRoot).FirstOrDefault(slot => slot.Name == "material-search-clear")?.GetComponent<Button>();
            if (materialSearchClear != null)
            {
                UnityStationHandle.ReleaseFixtureActions(materialSearchClear);
                NativeButtonEvents.Pressed(materialSearchClear, (_, _) =>
                {
                    if (materialSearchText != null) materialSearchText.Content.Value = "";
                    UpdateSearchHint(materialSearchField, materialSearchHint);
                    RefreshMaterialRows("");
                });
            }

            foreach (var mux in customizerPreview.GetComponents<ValueMultiplexer<string>>())
            {
                mux.Target.Target = null;
                mux.Enabled = false;
            }
            foreach (var mux in customizerPreview.GetComponents<ValueMultiplexer<int>>())
            {
                mux.Target.Target = null;
                mux.Enabled = false;
            }
            foreach (var eq in customizerPreview.GetComponents<ValueEqualityDriver<int>>())
            {
                eq.Target.Target = null;
                eq.Enabled = false;
            }
            foreach (var vmd in customizerPreview.GetComponents<ValueMultiDriver<bool>>())
            {
                vmd.Drives.Clear();
                vmd.Enabled = false;
            }
            var browseDriver = customizerPreview.GetComponent<ValueMultiDriver<int>>();
            if (browseDriver != null)
            {
                browseDriver.Drives.Clear();
                folderBrowseIndex = browseDriver.Value;
                folderBrowseIndex.Value = 0;
            }
            Slot? folderNavTransition = Descendants(customizerPreview).FirstOrDefault(slot => slot.Name == "Folder Navigation Transition");
            folderCanGoUp = folderNavTransition?.GetComponent<ValueField<bool>>()?.Value;
            if (folderCanGoUp != null) folderCanGoUp.Value = false;

            foldersOpenButton = Descendants(materialRoot).FirstOrDefault(slot => slot.Name == "folders-open")?.GetComponent<Button>();
            if (foldersOpenButton != null)
            {
                var vs = foldersOpenButton.Slot.GetComponent<ButtonValueSet<bool>>();
                if (vs?.TargetValue.Target != null)
                {
                    folderPickerVisible = customizerPreview.GetComponents<BooleanValueDriver<bool>>()
                        .FirstOrDefault(d => d.State == vs.TargetValue.Target);
                }
                UnityStationHandle.ReleaseFixtureActions(foldersOpenButton);
                foldersOpenButton.RequireLockInToPress.Value = true;
                foldersOpenButton.PassThroughVerticalMovement.Value = true;
                foldersOpenButton.PassThroughHorizontalMovement.Value = true;
                NativeButtonEvents.Pressed(foldersOpenButton, (_, _) =>
                {
                    bool next = !(folderPickerVisible?.State.Value ?? folderPickerRoot?.ActiveSelf ?? false);
                    if (folderPickerVisible != null) folderPickerVisible.State.Value = next;
                    else if (folderPickerRoot != null) folderPickerRoot.ActiveSelf = next;
                    if (next)
                    {
                        browsedFolder = currentFolder;
                        RefreshFolderRows(animateTransition: false);
                    }
                });
            }

            if (folderPickerVisible == null && folderPickerRoot != null)
            {
                folderPickerVisible = customizerPreview.GetComponents<BooleanValueDriver<bool>>()
                    .FirstOrDefault(d => d.TargetField.Target == folderPickerRoot.ActiveSelf_Field)
                    ?? customizerPreview.GetComponents<BooleanValueDriver<bool>>().ElementAtOrDefault(2);
            }
            if (folderPickerVisible != null)
            {
                folderPickerVisible.TargetField.Target = null;
                folderPickerVisible.State.Value = false;
                if (folderPickerRoot != null) folderPickerRoot.ActiveSelf = true;
            }
            else if (folderPickerRoot != null) folderPickerRoot.ActiveSelf = false;

            if (folderPickerRoot != null)
            {
                folderList = Descendants(folderPickerRoot).FirstOrDefault(slot => slot.Name == "folder-picker-list-content");
                if (folderList != null)
                {
                    folderChoiceRootTemplate = folderList.Children.FirstOrDefault(slot => slot.Name == "folder-choice-0");
                    folderChoiceTemplate = folderList.Children.FirstOrDefault(slot => slot.Name == "folder-choice-1");
                    foreach (Slot child in folderList.Children.ToArray())
                    {
                        if (child != folderChoiceRootTemplate && child != folderChoiceTemplate)
                        {
                            child.Destroy();
                        }
                    }
                    if (folderChoiceRootTemplate != null)
                    {
                        folderChoiceRootTemplate.ActiveSelf = false;
                        var rootBtn = folderChoiceRootTemplate.GetComponent<Button>();
                        if (rootBtn != null)
                        {
                            UnityStationHandle.ReleaseFixtureActions(rootBtn);
                            foreach (var comp in folderChoiceRootTemplate.GetComponentsInChildren<Component>())
                            {
                                if (comp.GetType().Name.StartsWith("ButtonValueSet`", StringComparison.Ordinal))
                                    comp.Enabled = false;
                            }
                            rootBtn.RequireLockInToPress.Value = true;
                            rootBtn.PassThroughVerticalMovement.Value = true;
                            rootBtn.PassThroughHorizontalMovement.Value = true;
                            NativeButtonEvents.Pressed(rootBtn, (_, _) =>
                            {
                                if (!isAlive() || isBusy()) return;
                                currentFolder = browsedFolder;
                                if (folderPickerVisible != null) folderPickerVisible.State.Value = false;
                                else if (folderPickerRoot != null) folderPickerRoot.ActiveSelf = false;
                                UpdateBreadcrumb();
                                _ = rootBtn.World.RootSlot.StartGlobalTask(async () =>
                                {
                                    await Task.Delay(180);
                                    await default(ToWorld);
                                    if (isAlive() && !isBusy() && materialVisible.State.Value)
                                    {
                                        RefreshMaterialRows(materialQuery);
                                    }
                                });
                            });
                        }
                    }
                    if (folderChoiceTemplate != null)
                    {
                        folderChoiceTemplate.ActiveSelf = false;
                    }
                }
                Slot navUpSlot = Descendants(folderPickerRoot).FirstOrDefault(slot => slot.Name == "folder-nav-up");
                folderNavUp = navUpSlot?.GetComponent<Button>();
                if (folderNavUp != null)
                {
                    UnityStationHandle.ReleaseFixtureActions(folderNavUp);
                    foreach (var comp in navUpSlot.GetComponentsInChildren<Component>())
                    {
                        if (comp.GetType().Name.StartsWith("ButtonValueSet`", StringComparison.Ordinal))
                            comp.Enabled = false;
                    }
                    navUpSlot.ActiveSelf = true;
                    var le = navUpSlot.GetComponent<LayoutElement>();
                    if (le != null)
                    {
                        le.MinWidth.Value = 0f;
                        le.UseZeroMetrics.Value = true;
                    }
                    var labelSlot = FindDescendant(navUpSlot, "folder-nav-up-label");
                    var labelLe = labelSlot?.GetComponent<LayoutElement>();
                    if (labelLe != null)
                    {
                        labelLe.FlexibleHeight.Value = -1f;
                        labelLe.UseZeroMetrics.Value = true;
                    }
                    var labelText = labelSlot?.GetComponent<Text>();
                    if (folderNavTransition != null && folderCanGoUp != null && folderNavTransition.GetComponent<SmoothValue<float>>() == null)
                    {
                        if (le != null)
                        {
                            var smoothW = folderNavTransition.AttachComponent<SmoothValue<float>>();
                            smoothW.Speed.Value = 14f;
                            smoothW.Value.Target = le.PreferredWidth;
                            var bvdW = folderNavTransition.AttachComponent<BooleanValueDriver<float>>();
                            bvdW.FalseValue.Value = 0f;
                            bvdW.TrueValue.Value = 44f;
                            bvdW.TargetField.Target = smoothW.TargetValue;
                            var vcW = folderNavTransition.AttachComponent<ValueCopy<bool>>();
                            vcW.Source.Target = folderCanGoUp;
                            vcW.Target.Target = bvdW.State;
                        }
                        var smoothS = folderNavTransition.AttachComponent<SmoothValue<float3>>();
                        smoothS.Speed.Value = 14f;
                        smoothS.Value.Target = navUpSlot.Scale_Field;
                        var bvdS = folderNavTransition.AttachComponent<BooleanValueDriver<float3>>();
                        bvdS.FalseValue.Value = float3.Zero;
                        bvdS.TrueValue.Value = float3.One;
                        bvdS.TargetField.Target = smoothS.TargetValue;
                        var vcS = folderNavTransition.AttachComponent<ValueCopy<bool>>();
                        vcS.Source.Target = folderCanGoUp;
                        vcS.Target.Target = bvdS.State;
                        if (labelText != null)
                        {
                            var vcT = folderNavTransition.AttachComponent<ValueCopy<bool>>();
                            vcT.Source.Target = folderCanGoUp;
                            vcT.Target.Target = labelText.EnabledField;
                        }
                        var vcB = folderNavTransition.AttachComponent<ValueCopy<bool>>();
                        vcB.Source.Target = folderCanGoUp;
                        vcB.Target.Target = folderNavUp.EnabledField;
                    }

                    folderNavUp.RequireLockInToPress.Value = true;
                    folderNavUp.PassThroughVerticalMovement.Value = true;
                    folderNavUp.PassThroughHorizontalMovement.Value = true;
                    if (folderCanGoUp != null) folderCanGoUp.Value = false;
                    else folderNavUp.Enabled = false;
                    NativeButtonEvents.Pressed(folderNavUp, (_, _) =>
                    {
                        if (!isAlive() || isBusy()) return;
                        string normalized = NormalizeFolder(browsedFolder);
                        int lastSlash = normalized.LastIndexOf('/');
                        browsedFolder = lastSlash >= 0 ? normalized[..lastSlash] : "";
                        RefreshFolderRows(animateTransition: true);
                    });
                }
            }

            Slot breadcrumbSlot = Descendants(materialRoot).FirstOrDefault(slot => slot.Name == "picker-breadcrumb");
            if (breadcrumbSlot != null)
            {
                breadcrumbPath = Descendants(breadcrumbSlot).FirstOrDefault(slot => slot.Name == "folder-path")?.GetComponent<Text>();
                Slot rootBtnSlot = Descendants(breadcrumbSlot).FirstOrDefault(slot => slot.Name == "folder-root");
                if (rootBtnSlot != null)
                {
                    folderRootButton = rootBtnSlot.GetComponent<Button>() ?? rootBtnSlot.AttachComponent<Button>();
                    UnityStationHandle.ReleaseFixtureActions(folderRootButton);
                    folderRootButton.RequireLockInToPress.Value = true;
                    folderRootButton.PassThroughVerticalMovement.Value = true;
                    folderRootButton.PassThroughHorizontalMovement.Value = true;
                    NativeButtonEvents.Pressed(folderRootButton, (_, _) => schedule(() =>
                    {
                        currentFolder = "";
                        browsedFolder = "";
                        UpdateBreadcrumb();
                        if (folderPickerVisible?.State.Value == true || (folderPickerVisible == null && folderPickerRoot?.ActiveSelf == true))
                        {
                            RefreshFolderRows(animateTransition: true);
                        }
                        RefreshMaterialRows(materialQuery);
                        return Task.CompletedTask;
                    }));
                }
            }

            foreach (Slot btnSlot in Descendants(ui.Root).Where(slot => slot.Name == "Customize Action Button"))
            {
                var button = btnSlot.GetComponent<Button>();
                if (button != null)
                {
                    NativeButtonEvents.Pressed(button, (_, _) => schedule(OpenMainAsync));
                    entryButtons.Add(button);
                }
            }
        }
        else
        {
            isNative = false;
            folderPickerVisible = null;
            folderCanGoUp = null;
            folderBrowseIndex = null;
            meshCountText = null;
            materialCountText = null;
            pickerContextText = null;
            pickerContextMaterialText = null;
            pickerContextSphere = null;
            folderPickerTitle = null;
            meshSearchClear = null;
            materialSearchClear = null;
            foldersOpenButton = null;
            folderRootButton = null;
            folderPickerRoot = null;
            folderList = null;
            folderChoiceRootTemplate = null;
            folderChoiceTemplate = null;
            folderNavUp = null;
            breadcrumbPath = null;

            Slot avatarPicker = ui.Get<BooleanValueDriver<bool>>("avatarPicker").Slot;
            Slot pickerParent = avatarPicker.Parent;
            searchFieldTemplate = ui.Get<Text>("name").Slot.Parent;

            mainRoot = avatarPicker.Duplicate(pickerParent, false);
            mainRoot.Name = "Material Customizer Popover";
            mainVisible = FirstVisibilityDriver(mainRoot);
            mainVisible.State.Value = false;
            mainList = FindAvatarList(mainRoot);
            mainRowTemplate = KeepOnlyRowTemplate(mainList);
            mainTitle = FindHeaderTitle(mainRoot);
            DisableLocalizedOverrides(mainTitle.Slot);
            mainTitle.HorizontalAlign.Value = Elements.Assets.TextHorizontalAlignment.Left;
            mainDismissButton = AddBlueDismissButton(mainRoot, CloseAll);

            materialRoot = avatarPicker.Duplicate(pickerParent, false);
            materialRoot.Name = "Package Material Picker Popover";
            materialVisible = FirstVisibilityDriver(materialRoot);
            materialVisible.State.Value = false;
            materialList = FindAvatarList(materialRoot);
            materialRowTemplate = KeepOnlyRowTemplate(materialList);
            materialTitle = FindHeaderTitle(materialRoot);
            DisableLocalizedOverrides(materialTitle.Slot);
            materialTitle.HorizontalAlign.Value = Elements.Assets.TextHorizontalAlignment.Left;

            foreach (Slot advanced in Descendants(ui.Root).Where(slot => slot.Name == "Advanced Options Content"))
                AddAdvancedEntryPoint(advanced);
        }
    }

    internal void SetEntryPointsEnabled(bool enabled)
    {
        foreach (Button button in entryButtons)
            if (!button.IsDestroyed) button.Enabled = enabled;
    }

    internal void CloseAll()
    {
        if (!isAlive()) return;
        mainSearchGeneration++;
        materialSearchGeneration++;
        materialRowsCts?.Cancel();
        meshSpheresCts?.Cancel();
        if (folderPickerVisible != null) folderPickerVisible.State.Value = false;
        else if (folderPickerRoot != null && !folderPickerRoot.IsDestroyed) folderPickerRoot.ActiveSelf = false;
        if (materialVisible != null && !materialVisible.IsDestroyed) materialVisible.State.Value = false;
        if (mainVisible != null && !mainVisible.IsDestroyed) mainVisible.State.Value = false;
    }

    private void ClosePickerOnly()
    {
        materialRowsCts?.Cancel();
        if (folderPickerVisible != null && !folderPickerVisible.IsDestroyed) folderPickerVisible.State.Value = false;
        else if (folderPickerRoot != null && !folderPickerRoot.IsDestroyed) folderPickerRoot.ActiveSelf = false;
        if (materialVisible != null && !materialVisible.IsDestroyed) materialVisible.State.Value = false;
        materialSearchGeneration++;
        selectedSlot = null;
    }

    private void AddAdvancedEntryPoint(Slot advanced)
    {
        Slot template = advanced.Children.SingleOrDefault(slot => slot.Name == "Materials & Shading Card");
        if (template == null) return;

        Slot card = template.Duplicate(advanced, false);
        card.Name = "Runtime Customize Materials Card";
        DisableLocalizedOverrides(card);
        Slot existingPresetField = FindDescendant(card, "Field element");
        if (existingPresetField != null) existingPresetField.ActiveSelf = false;
        SetText(FindDescendant(card, "Labels").Children.Single(slot => slot.Name == "Title").GetComponent<Text>(),
            "customizeMaterials");
        SetText(FindDescendant(card, "Labels").Children.Single(slot => slot.Name == "Subtitle").GetComponent<Text>(),
            "customizeMaterialsSubtitle");

        Slot buttonSlot = FindDescendant(card, "Button element");
        var button = buttonSlot.GetComponent<Button>();
        foreach (Button other in card.GetComponentsInChildren<Button>())
        {
            if (ReferenceEquals(other, button)) continue;
            UnityStationHandle.ReleaseFixtureActions(other);
            other.Slot.ActiveSelf = false;
        }
        UnityStationHandle.ReleaseFixtureActions(button);
        SetText(buttonSlot.GetComponentInChildren<Text>(), "open");
        NativeButtonEvents.Pressed(button, (_, _) => schedule(OpenMainAsync));
        entryButtons.Add(button);
    }

    private async Task OpenMainAsync()
    {
        if (!isAlive() || isBusy()) return;
        setStage("Open material customizer");
        UnityPrefabImportTask item = getCurrent();
        UnityProjectImporter importer = getImporter();
        if (item == null || item.CurrentStructureRootSlot == null || item.CurrentStructureRootSlot.IsDestroyed || importer == null)
            return;

        if (isNative && displayedItem == item && cachedMeshCardsItem == item && nativeCards.Count > 0)
        {
            ClosePickerOnly();
            mainVisible.State.Value = true;
            ApplyNativeMeshFilter(mainQuery);
            StartSearchMonitor(mainRoot, mainSearchField, isMain: true);
            return;
        }

        PackageMaterialCatalog catalog = await importer.GetPackageMaterialCatalogAsync();
        await default(ToWorld);
        if (!isAlive() || isBusy() || item != getCurrent()) return;

        displayedItem = item;
        materialCatalog = catalog;
        renderers = BuildRendererBindings(item, importer, catalog);
        expandedRenderer = null;
        expandedRendererNames.Clear();
        mainQuery = "";

        if (isNative)
        {
            mainSearchText = SearchText(mainSearchField);
            mainSearchHint = Descendants(mainSearchField).FirstOrDefault(slot => slot.Name == "mesh-search-placeholder");
            if (mainSearchText != null) mainSearchText.Content.Value = "";
            UpdateSearchHint(mainSearchField, mainSearchHint);
            SetText(mainTitle, "customizeMaterials");
            if (meshCountText != null)
            {
                meshCountText.Slot.ActiveSelf = true;
                meshCountText.Content.Value = L("meshesCount", renderers.Count);
            }
            ClosePickerOnly();
            mainVisible.State.Value = true;
            RefreshMainRows("");
            StartSearchMonitor(mainRoot, mainSearchField, isMain: true);
        }
        else
        {
            mainSearchField = EnsureSearchField(mainList, "Renderer Search");
            mainSearchText = SearchText(mainSearchField);
            mainSearchHint = EnsureSearchHint(mainSearchField, "searchMeshes");
            mainSearchText.Content.Value = "";
            UpdateSearchHint(mainSearchField, mainSearchHint);
            SetText(mainTitle, "customizeMaterialsMeshes", renderers.Count);
            ClosePickerOnly();
            mainVisible.State.Value = true;
            RefreshMainRows("");
            StartSearchMonitor(mainRoot, mainSearchField, isMain: true);
            if (mainDismissButton != null && !mainDismissButton.IsDestroyed) mainDismissButton.ActiveSelf = true;
        }
    }

    private static bool IsActiveInHierarchy(Slot slot, Slot root)
    {
        if (slot == null || root == null || slot.IsDestroyed || root.IsDestroyed) return false;
        Slot current = slot;
        while (current != null)
        {
            if (!current.ActiveSelf) return false;
            if (current == root) return true;
            current = current.Parent;
        }
        return false;
    }

    private IReadOnlyList<MaterialRendererBinding> BuildRendererBindings(
        UnityPrefabImportTask item,
        UnityProjectImporter importer,
        PackageMaterialCatalog catalog)
    {
        var result = new List<MaterialRendererBinding>();
        Slot root = item.CurrentStructureRootSlot;
        if (root == null || root.IsDestroyed) return result;

        var seenRenderers = new HashSet<MeshRenderer>();

        var skinned = root.GetComponentsInChildren<SkinnedMeshRenderer>();
        foreach (var renderer in skinned)
        {
            if (renderer == null || renderer.IsDestroyed || renderer.Slot == null || renderer.Slot.IsDestroyed) continue;
            if (!IsActiveInHierarchy(renderer.Slot, root) || !renderer.Enabled || renderer.Materials.Count == 0) continue;
            if (!seenRenderers.Add(renderer)) continue;

            var slots = new List<MaterialSlotBinding>();
            for (int index = 0; index < renderer.Materials.Count; index++)
            {
                int capturedIndex = index;
                IAssetProvider<Material> current = renderer.Materials[index];
                var key = (item, (object)renderer, index);
                if (!originals.TryGetValue(key, out var original)) originals[key] = original = current;
                slots.Add(new MaterialSlotBinding(renderer.Slot.Name, index, MaterialName(current, importer, catalog),
                    () => renderer.Materials[capturedIndex], value => renderer.Materials[capturedIndex] = value, original));
            }
            if (slots.Count > 0) result.Add(new MaterialRendererBinding(RendererLabel(renderer.Slot), slots));
        }

        var staticRenderers = root.GetComponentsInChildren<MeshRenderer>();
        foreach (var renderer in staticRenderers)
        {
            if (renderer == null || renderer is SkinnedMeshRenderer || renderer.IsDestroyed || renderer.Slot == null || renderer.Slot.IsDestroyed) continue;
            if (!IsActiveInHierarchy(renderer.Slot, root) || !renderer.Enabled || renderer.Materials.Count == 0) continue;
            if (!seenRenderers.Add(renderer)) continue;

            var slots = new List<MaterialSlotBinding>();
            for (int index = 0; index < renderer.Materials.Count; index++)
            {
                int capturedIndex = index;
                IAssetProvider<Material> current = renderer.Materials[index];
                var key = (item, (object)renderer, index);
                if (!originals.TryGetValue(key, out var original)) originals[key] = original = current;
                slots.Add(new MaterialSlotBinding(renderer.Slot.Name, index, MaterialName(current, importer, catalog),
                    () => renderer.Materials[capturedIndex], value => renderer.Materials[capturedIndex] = value, original));
            }
            if (slots.Count > 0) result.Add(new MaterialRendererBinding(RendererLabel(renderer.Slot), slots));
        }

        // Safety fallback: if all renderers in this item hierarchy were inactive/disabled,
        // fall back to including all renderers with materials so the list is not empty.
        if (result.Count == 0)
        {
            foreach (var renderer in skinned)
            {
                if (renderer == null || renderer.IsDestroyed || renderer.Slot == null || renderer.Slot.IsDestroyed || renderer.Materials.Count == 0) continue;
                if (!seenRenderers.Add(renderer)) continue;

                var slots = new List<MaterialSlotBinding>();
                for (int index = 0; index < renderer.Materials.Count; index++)
                {
                    int capturedIndex = index;
                    IAssetProvider<Material> current = renderer.Materials[index];
                    var key = (item, (object)renderer, index);
                    if (!originals.TryGetValue(key, out var original)) originals[key] = original = current;
                    slots.Add(new MaterialSlotBinding(renderer.Slot.Name, index, MaterialName(current, importer, catalog),
                        () => renderer.Materials[capturedIndex], value => renderer.Materials[capturedIndex] = value, original));
                }
                if (slots.Count > 0) result.Add(new MaterialRendererBinding(RendererLabel(renderer.Slot), slots));
            }

            foreach (var renderer in staticRenderers)
            {
                if (renderer == null || renderer is SkinnedMeshRenderer || renderer.IsDestroyed || renderer.Slot == null || renderer.Slot.IsDestroyed || renderer.Materials.Count == 0) continue;
                if (!seenRenderers.Add(renderer)) continue;

                var slots = new List<MaterialSlotBinding>();
                for (int index = 0; index < renderer.Materials.Count; index++)
                {
                    int capturedIndex = index;
                    IAssetProvider<Material> current = renderer.Materials[index];
                    var key = (item, (object)renderer, index);
                    if (!originals.TryGetValue(key, out var original)) originals[key] = original = current;
                    slots.Add(new MaterialSlotBinding(renderer.Slot.Name, index, MaterialName(current, importer, catalog),
                        () => renderer.Materials[capturedIndex], value => renderer.Materials[capturedIndex] = value, original));
                }
                if (slots.Count > 0) result.Add(new MaterialRendererBinding(RendererLabel(renderer.Slot), slots));
            }
        }

        return result;
    }

    private static string RendererLabel(Slot slot) => slot.Name;

    private string MaterialName(IAssetProvider<Material> material, UnityProjectImporter importer, PackageMaterialCatalog catalog)
    {
        if (material == null) return L("missingMaterial");
        if (importer != null && importer.TryGetMaterialGuid(material, out string registeredGuid))
        {
            var registeredChoice = catalog?.Choices.FirstOrDefault(candidate => candidate.Guid.Equals(registeredGuid, StringComparison.OrdinalIgnoreCase));
            if (registeredChoice != null) return registeredChoice.Name;
        }
        var imported = importer?.TasksMaterials.FirstOrDefault(task => !task.ismissing && (task.finalMaterial == material || ReferenceEquals(task.finalMaterial, material)));
        if (!string.IsNullOrEmpty(imported?.MaterialGuid))
        {
            var choice = catalog?.Choices.FirstOrDefault(candidate => candidate.Guid.Equals(imported.MaterialGuid, StringComparison.OrdinalIgnoreCase));
            if (choice != null) return choice.Name;
        }

        Slot providerSlot = material.FindNearestParent<Slot>();
        if (providerSlot != null && !string.IsNullOrEmpty(providerSlot.Name))
        {
            int open = providerSlot.Name.IndexOf('[');
            int close = providerSlot.Name.IndexOf(']', open >= 0 ? open : 0);
            if (open >= 0 && close > open)
            {
                string raw = providerSlot.Name.Substring(open + 1, close - open - 1);
                string guid = raw.Replace("-toon", "", StringComparison.OrdinalIgnoreCase).Trim();
                var choice = catalog.Choices.FirstOrDefault(candidate => candidate.Guid.Equals(guid, StringComparison.OrdinalIgnoreCase));
                if (choice != null) return choice.Name;
                return providerSlot.Name.Substring(0, open).Trim();
            }
            return providerSlot.Name.Replace(" - Material", "", StringComparison.OrdinalIgnoreCase).Trim();
        }

        return L("importedMaterial");
    }

    private void RefreshMainRows(string query)
    {
        if (!isAlive() || displayedItem != getCurrent() || !mainVisible.State.Value) return;
        mainQuery = query ?? "";

        if (isNative)
        {
            if (cachedMeshCardsItem != displayedItem || nativeCards.Count == 0)
            {
                RebuildNativeMeshCards();
            }
            ApplyNativeMeshFilter(mainQuery);
        }
        else
        {
            ClearRows(mainList, mainRowTemplate, mainSearchField);
            var matches = renderers.Where(renderer => renderer.Name.Contains(mainQuery.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
            if (string.IsNullOrWhiteSpace(mainQuery))
                SetText(mainTitle, "customizeMaterialsMeshes", renderers.Count);
            else
                SetText(mainTitle, "customizeMaterialsSearch", mainQuery);

            if (matches.Length == 0)
            {
                AddRow(mainList, mainRowTemplate, L("noMatchingMeshes"), null);
                return;
            }

            foreach (var renderer in matches)
            {
                string marker = ReferenceEquals(expandedRenderer, renderer) ? "−" : "+";
                AddRow(mainList, mainRowTemplate, $"{marker}  {renderer.Name}  ·  {L("materialSlotsCount", renderer.Slots.Count)}",
                    () =>
                    {
                        expandedRenderer = ReferenceEquals(expandedRenderer, renderer) ? null : renderer;
                        RefreshMainRows(mainQuery);
                        return Task.CompletedTask;
                    });

                if (!ReferenceEquals(expandedRenderer, renderer)) continue;
                foreach (var slot in renderer.Slots)
                {
                    string currentName = MaterialName(slot.ReadCurrent(), getImporter(), materialCatalog);
                    AddRow(mainList, mainRowTemplate, $"    {L("slotPrefix", slot.Index + 1)}  ·  {currentName}", () => OpenMaterialPickerAsync(slot));
                }
            }
        }
    }

    private void RebuildNativeMeshCards()
    {
        ClearNativeMeshCards();
        meshSpheresCts?.Cancel();
        meshSpheresCts?.Dispose();
        meshSpheresCts = new CancellationTokenSource();
        cachedMeshCardsItem = displayedItem;

        foreach (var renderer in renderers)
        {
            var holder = CreateNativeMeshCard(renderer);
            if (holder != null)
            {
                nativeCards.Add(holder);
            }
        }
    }

    private void ClearNativeMeshCards()
    {
        meshSpheresCts?.Cancel();
        foreach (var holder in nativeCards)
        {
            if (holder.CardSlot != null && !holder.CardSlot.IsDestroyed)
            {
                holder.CardSlot.Destroy();
            }
        }
        nativeCards.Clear();
        cachedMeshCardsItem = null;
    }

    private void ApplyNativeMeshFilter(string query)
    {
        mainQuery = query ?? "";
        string trimmed = mainQuery.Trim();
        int visibleCount = 0;
        foreach (var holder in nativeCards)
        {
            bool match = string.IsNullOrEmpty(trimmed) || holder.Renderer.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase);
            holder.CardSlot.ActiveSelf = match;
            if (match) visibleCount++;
        }

        if (meshCountText != null)
        {
            meshCountText.Slot.ActiveSelf = true;
            meshCountText.Content.Value = string.IsNullOrWhiteSpace(mainQuery)
                ? L("meshesCount", nativeCards.Count)
                : L("meshesCountFiltered", visibleCount, nativeCards.Count);
        }
    }

    private NativeMeshCardHolder CreateNativeMeshCard(MaterialRendererBinding renderer)
    {
        Slot card = mainRowTemplate.Duplicate(mainList, false);
        card.Name = $"Mesh Card · {renderer.Name}";
        card.ActiveSelf = true;

        Slot titleSlot = FindDescendant(card, "mesh-0-title");
        SetFittedText(titleSlot, renderer.Name);

        Slot countSlot = FindDescendant(card, "mesh-0-slot-count");
        SetFittedText(countSlot, L("slotsCount", renderer.Slots.Count));

        Slot header = FindDescendant(card, "mesh-0-header");
        Slot reveal = FindDescendant(card, "Material Slots Reveal");
        var layout = reveal?.GetComponent<LayoutElement>();

        var multiDriver = header?.GetComponent<ValueMultiDriver<bool>>()
            ?? card.GetComponentsInChildren<ValueMultiDriver<bool>>().FirstOrDefault();

        var heightDriver = header?.GetComponents<BooleanValueDriver<float>>().FirstOrDefault()
            ?? card.GetComponentsInChildren<BooleanValueDriver<float>>().FirstOrDefault();

        var smooth = header?.GetComponents<SmoothValue<float>>().FirstOrDefault()
            ?? card.GetComponentsInChildren<SmoothValue<float>>().FirstOrDefault();

        var chevronDriver = header?.GetComponents<BooleanValueDriver<RectOrientation>>().FirstOrDefault()
            ?? card.GetComponentsInChildren<BooleanValueDriver<RectOrientation>>().FirstOrDefault();

        Slot slotsContainer = FindDescendant(card, "Material Slots");
        Slot slotTemplate = slotsContainer != null
            ? (slotsContainer.Children.FirstOrDefault(s => s.Name == "Material slot 1") ?? slotsContainer.Children.FirstOrDefault())
            : null;

        if (slotsContainer != null)
        {
            foreach (Slot child in slotsContainer.Children)
            {
                child.ActiveSelf = false;
            }
        }

        float slotHeight = slotTemplate?.GetComponent<LayoutElement>()?.PreferredHeight.Value ?? 48f;
        if (slotHeight <= 0) slotHeight = 48f;
        float spacing = slotsContainer?.GetComponent<VerticalLayout>()?.Spacing.Value ?? 4f;
        float targetHeight = Math.Max(slotHeight, renderer.Slots.Count * (slotHeight + spacing) + spacing);

        if (heightDriver != null)
        {
            heightDriver.TrueValue.Value = targetHeight;
            heightDriver.FalseValue.Value = 0.05f;
        }

        if (smooth != null)
        {
            smooth.TargetValue.Value = 0.05f;
        }

        if (header != null)
        {
            foreach (var eq in header.GetComponents<ValueEqualityDriver<int>>())
            {
                eq.Target.Target = null;
                eq.TargetValue.Target = null;
                eq.Destroy();
            }
            foreach (var cycle in header.GetComponents<ButtonValueCycle<int>>())
            {
                cycle.TargetValue.Target = null;
                cycle.Destroy();
            }
            foreach (var comp in header.GetComponents<Component>())
            {
                string typeName = comp.GetType().Name;
                if (typeName.StartsWith("ButtonValueSet`", StringComparison.Ordinal) ||
                    typeName.StartsWith("ButtonReferenceSet`", StringComparison.Ordinal) ||
                    typeName.StartsWith("ButtonToggle", StringComparison.Ordinal))
                {
                    comp.Enabled = false;
                }
            }
        }

        var holder = new NativeMeshCardHolder
        {
            Renderer = renderer,
            CardSlot = card,
            Header = header,
            MultiDriver = multiDriver,
            HeightDriver = heightDriver,
            Smooth = smooth,
            ChevronDriver = chevronDriver,
            RevealLayout = layout,
            SlotsContainer = slotsContainer,
            SlotTemplate = slotTemplate,
            TargetHeight = targetHeight,
            IsPopulated = false
        };

        bool isExpanded = expandedRendererNames.Contains(renderer.Name);
        SetAccordionState(holder, isExpanded, animate: false);

        var headerBtn = header?.GetComponent<Button>();
        if (headerBtn != null)
        {
            UnityStationHandle.ReleaseFixtureActions(headerBtn);
            headerBtn.RequireLockInToPress.Value = false;
            headerBtn.PassThroughVerticalMovement.Value = true;
            headerBtn.PassThroughHorizontalMovement.Value = true;

            NativeButtonEvents.Pressed(headerBtn, (_, _) =>
            {
                bool willExpand = !expandedRendererNames.Contains(holder.Renderer.Name);
                SetAccordionState(holder, willExpand, animate: true);
            });
        }

        return holder;
    }

    private void SetAccordionState(NativeMeshCardHolder holder, bool expand, bool animate)
    {
        if (expand)
        {
            // Enforce single-card mutual exclusion so only one mesh card is expanded at a time.
            foreach (var other in nativeCards)
            {
                if (!ReferenceEquals(other, holder) && expandedRendererNames.Contains(other.Renderer.Name))
                {
                    SetAccordionState(other, false, animate);
                }
            }
            EnsureSlotsPopulated(holder);
            expandedRendererNames.Add(holder.Renderer.Name);
        }
        else
        {
            expandedRendererNames.Remove(holder.Renderer.Name);
        }

        if (holder.MultiDriver != null && !holder.MultiDriver.Value.IsDriven)
        {
            try { holder.MultiDriver.Value.Value = expand; } catch { }
        }

        if (holder.HeightDriver != null && !holder.HeightDriver.State.IsDriven)
        {
            try { holder.HeightDriver.State.Value = expand; } catch { }
        }

        if (holder.ChevronDriver != null && !holder.ChevronDriver.State.IsDriven)
        {
            try { holder.ChevronDriver.State.Value = expand; } catch { }
        }

        if (holder.Smooth != null && !holder.Smooth.TargetValue.IsDriven)
        {
            float targetH = expand ? holder.TargetHeight : 0.05f;
            try { holder.Smooth.TargetValue.Value = targetH; } catch { }
        }

        if (holder.RevealLayout != null && !holder.RevealLayout.PreferredHeight.IsDriven)
        {
            holder.RevealLayout.PreferredHeight.Value = expand ? holder.TargetHeight : 0.05f;
        }
    }

    private void EnsureSlotsPopulated(NativeMeshCardHolder holder)
    {
        if (holder.IsPopulated || holder.SlotsContainer == null || holder.SlotTemplate == null) return;
        holder.IsPopulated = true;

        foreach (Slot child in holder.SlotsContainer.Children.ToArray())
        {
            if (child != holder.SlotTemplate) child.Destroy();
        }

        if (meshSpheresCts == null || meshSpheresCts.IsCancellationRequested)
        {
            meshSpheresCts?.Dispose();
            meshSpheresCts = new CancellationTokenSource();
        }
        var sphereToken = meshSpheresCts.Token;

        foreach (var slot in holder.Renderer.Slots)
        {
            Slot slotRow = holder.SlotTemplate.Duplicate(holder.SlotsContainer, false);
            slotRow.Name = $"Material Slot Row {slot.Index + 1}";
            slotRow.ActiveSelf = true;

            Slot slotLabel = FindDescendant(slotRow, "slot-0-0-slot-label");
            SetFittedText(slotLabel, L("slotPrefix", slot.Index + 1));

            string currentName = MaterialName(slot.ReadCurrent(), getImporter(), materialCatalog);
            Slot nameSlot = FindDescendant(slotRow, "slot-0-0-name");
            var nameLabel = new FittedTextBinding(nameSlot);
            nameLabel.SetText(currentName);

            Slot? sphereSlot = FindDescendant(slotRow, "slot-0-0-sphere");
            UpdateSlotSphere(slotRow, sphereSlot, slot, sphereToken);

            Slot changeSlot = FindDescendant(slotRow, "slot-0-0-change");
            var changeBtn = changeSlot?.GetComponent<Button>();
            if (changeBtn != null)
            {
                UnityStationHandle.ReleaseFixtureActions(changeBtn);
                NativeButtonEvents.Pressed(changeBtn, (_, _) => schedule(() => OpenMaterialPickerAsync(slot)));
            }

            Slot restoreSlot = FindDescendant(slotRow, "slot-0-0-restore");
            var restoreBtn = restoreSlot?.GetComponent<Button>();
            bool isModified = !ReferenceEquals(slot.ReadCurrent(), slot.Original);
            if (restoreBtn != null)
            {
                UnityStationHandle.ReleaseFixtureActions(restoreBtn);
                restoreBtn.Slot.ActiveSelf = isModified;
                NativeButtonEvents.Pressed(restoreBtn, (_, _) => schedule(() => RestoreOriginalAsync(slot)));
            }

            holder.SlotRows[slot] = (nameLabel, sphereSlot, restoreBtn);
        }
    }

    private void UpdateSlotInNativeCards(MaterialSlotBinding slot)
    {
        if (slot == null) return;
        if (meshSpheresCts == null || meshSpheresCts.IsCancellationRequested)
        {
            meshSpheresCts?.Dispose();
            meshSpheresCts = new CancellationTokenSource();
        }
        var sphereToken = meshSpheresCts.Token;

        foreach (var holder in nativeCards)
        {
            if (holder.SlotRows.TryGetValue(slot, out var rowInfo))
            {
                string updatedName = MaterialName(slot.ReadCurrent(), getImporter(), materialCatalog);
                rowInfo.NameLabel.SetText(updatedName);
                UpdateSlotSphere(holder.CardSlot, rowInfo.SphereSlot, slot, sphereToken);

                bool isModified = !ReferenceEquals(slot.ReadCurrent(), slot.Original);
                if (rowInfo.RestoreBtn != null && !rowInfo.RestoreBtn.Slot.IsDestroyed)
                {
                    rowInfo.RestoreBtn.Slot.ActiveSelf = isModified;
                }
            }
        }
    }

    private string ResolveSlotMaterialGuid(MaterialSlotBinding? slot)
    {
        if (slot == null) return "";
        var importer = getImporter();
        string? directGuid = slot.CurrentGuid(importer);
        if (!string.IsNullOrEmpty(directGuid)) return directGuid;

        if (materialCatalog == null) return "";

        string currentName = MaterialName(slot.ReadCurrent(), importer, materialCatalog);
        foreach (string candidateName in new[] { currentName, slot.InitialMaterialName })
        {
            if (string.IsNullOrWhiteSpace(candidateName)) continue;
            string cleanName = candidateName;
            int bracketIdx = cleanName.IndexOf('[');
            if (bracketIdx > 0) cleanName = cleanName[..bracketIdx].Trim();

            var match = materialCatalog.Choices.FirstOrDefault(c =>
                c.Name.Equals(cleanName, StringComparison.OrdinalIgnoreCase) ||
                c.Name.Equals(candidateName, StringComparison.OrdinalIgnoreCase));
            if (match != null && !string.IsNullOrEmpty(match.Guid))
                return match.Guid;
        }

        return "";
    }

    private void UpdateSlotSphere(Slot ownerSlot, Slot? sphereSlot, MaterialSlotBinding slot, CancellationToken cancellationToken)
    {
        if (sphereSlot == null || sphereSlot.IsDestroyed) return;
        var image = sphereSlot.GetComponent<Image>() ?? sphereSlot.GetComponentInChildren<Image>();
        if (image == null || image.IsDestroyed) return;

        image.Sprite.Target = null;
        string guid = ResolveSlotMaterialGuid(slot);
        if (!string.IsNullOrEmpty(guid))
        {
            var choice = materialCatalog?.Choices.FirstOrDefault(c => c.Guid.Equals(guid, StringComparison.OrdinalIgnoreCase));
            var baseCol = (choice != null && choice.BaseColor != default)
                ? choice.BaseColor
                : MaterialThumbnailGenerator.GetMaterialBaseColor(guid, getImporter());
            image.Tint.Value = new colorX(baseCol.r, baseCol.g, baseCol.b, baseCol.a);
            ApplyMaterialThumbnail(ownerSlot, image.Slot, image, guid, cancellationToken);
        }
    }

    private async Task OpenMaterialPickerAsync(MaterialSlotBinding slot)
    {
        if (!isAlive() || isBusy() || materialCatalog == null) return;
        setStage("Browse package materials");
        selectedSlot = slot;
        string resolvedGuid = ResolveSlotMaterialGuid(slot);
        var matchingChoice = !string.IsNullOrEmpty(resolvedGuid)
            ? materialCatalog.Choices.FirstOrDefault(c => c.Guid.Equals(resolvedGuid, StringComparison.OrdinalIgnoreCase))
            : null;

        currentFolder = matchingChoice?.Folder ?? "";
        browsedFolder = currentFolder;
        materialQuery = "";

        if (isNative)
        {
            if (folderPickerVisible != null && !folderPickerVisible.IsDestroyed) folderPickerVisible.State.Value = false;
            else if (folderPickerRoot != null && !folderPickerRoot.IsDestroyed) folderPickerRoot.ActiveSelf = false;

            materialSearchText = SearchText(materialSearchField);
            materialSearchHint = Descendants(materialSearchField).FirstOrDefault(s => s.Name == "material-search-placeholder");
            if (materialSearchText != null) materialSearchText.Content.Value = "";
            UpdateSearchHint(materialSearchField, materialSearchHint);
            SetText(materialTitle, "chooseMaterial");
            if (pickerContextText != null)
                pickerContextText.Content.Value = $"{slot.RendererName} · {L("slotPrefix", slot.Index + 1)}";
            if (pickerContextMaterialText != null)
            {
                string curName = MaterialName(slot.ReadCurrent(), getImporter(), materialCatalog);
                pickerContextMaterialText.Content.Value = curName;
            }
            UpdateBreadcrumb();
            materialVisible.State.Value = true;
            RefreshMaterialRows("");
            if (pickerContextSphere != null && materialRowsCts != null)
            {
                UpdateSlotSphere(materialRoot, pickerContextSphere, slot, materialRowsCts.Token);
            }
            StartSearchMonitor(materialRoot, materialSearchField, isMain: false);
        }
        else
        {
            materialSearchField = EnsureSearchField(materialList, "Material Search");
            materialSearchText = SearchText(materialSearchField);
            materialSearchHint = EnsureSearchHint(materialSearchField, "searchMaterials");
            materialSearchText.Content.Value = "";
            UpdateSearchHint(materialSearchField, materialSearchHint);
            SetText(materialTitle, "chooseMaterialSlot", slot.RendererName, slot.Index + 1);
            materialVisible.State.Value = true;
            RefreshMaterialRows("");
            StartSearchMonitor(materialRoot, materialSearchField, isMain: false);
        }
        await Task.CompletedTask;
    }

    private void RefreshMaterialRows(string query)
    {
        if (!isAlive() || selectedSlot == null || !materialVisible.State.Value) return;
        materialRowsCts?.Cancel();
        materialRowsCts?.Dispose();
        materialRowsCts = new CancellationTokenSource();
        var cancellationToken = materialRowsCts.Token;

        materialQuery = query ?? "";

        if (isNative)
        {
            IReadOnlyList<PackageMaterialChoice> choices;
            if (!string.IsNullOrWhiteSpace(materialQuery))
                choices = materialCatalog.Search(materialQuery);
            else
            {
                var directChoices = materialCatalog.Search(folder: currentFolder)
                    .Where(choice => choice.Folder.Equals(currentFolder, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (directChoices.Length > 0)
                {
                    choices = directChoices;
                }
                else
                {
                    var allInFolder = materialCatalog.Search(folder: currentFolder);
                    choices = allInFolder.Count > 16 ? allInFolder.Take(16).ToArray() : allInFolder;
                }
            }

            if (materialCountText != null)
            {
                materialCountText.Slot.ActiveSelf = true;
                if (!materialCountText.Content.IsDriven)
                {
                    try { materialCountText.Content.Value = L("materialsCount", choices.Count); } catch { }
                }
            }

            string currentGuid = ResolveSlotMaterialGuid(selectedSlot);
            for (int i = 0; i < choices.Count; i++)
            {
                BindNativeMaterialCard(i, choices[i], currentGuid, cancellationToken);
            }
            for (int i = choices.Count; i < pooledMaterialCards.Count; i++)
            {
                var extra = pooledMaterialCards[i];
                if (extra.Card != null && !extra.Card.IsDestroyed)
                {
                    extra.Card.ActiveSelf = false;
                    extra.CurrentAction = null;
                }
            }
        }
        else
        {
            ClearRows(materialList, materialRowTemplate, materialSearchField);
            if (ReferenceEquals(selectedSlot.ReadCurrent(), selectedSlot.Original))
            {
                // No restore action is needed for an unchanged slot.
            }
            else
            {
                AddRow(materialList, materialRowTemplate, L("restoreOriginal"), () => RestoreOriginalAsync(selectedSlot));
            }

            IReadOnlyList<PackageMaterialChoice> choices;
            if (!string.IsNullOrWhiteSpace(materialQuery))
                choices = materialCatalog.Search(materialQuery);
            else
            {
                string folder = currentFolder;
                foreach (string child in materialCatalog.ChildFolders(folder))
                {
                    string destination = string.IsNullOrEmpty(folder) ? child : folder + "/" + child;
                    AddRow(materialList, materialRowTemplate, L("folderPrefix") + "  ·  " + child, () =>
                    {
                        currentFolder = destination;
                        RefreshMaterialRows(materialQuery);
                        return Task.CompletedTask;
                    });
                }
                if (!string.IsNullOrEmpty(folder))
                {
                    string parent = folder.Contains('/') ? folder[..folder.LastIndexOf('/')] : "";
                    AddRow(materialList, materialRowTemplate, L("folderPrefix") + "  ·  ..", () =>
                    {
                        currentFolder = parent;
                        RefreshMaterialRows(materialQuery);
                        return Task.CompletedTask;
                    });
                }
                choices = materialCatalog.Search(folder: folder)
                    .Where(choice => choice.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }

            if (choices.Count == 0 && (string.IsNullOrWhiteSpace(materialQuery) || !materialCatalog.Search(materialQuery).Any()))
            {
                AddRow(materialList, materialRowTemplate, L("noMatchingMaterials"), null);
                return;
            }

            foreach (var choice in choices)
            {
                string label = string.IsNullOrWhiteSpace(choice.Folder)
                    ? choice.Name
                    : choice.Name + "  ·  " + choice.Folder;
                AddRow(materialList, materialRowTemplate, label, () => ApplyChoiceAsync(choice));
            }
        }
    }

    private PooledMaterialCard GetOrCreatePooledMaterialCard(int index)
    {
        while (pooledMaterialCards.Count <= index)
        {
            Slot card = materialRowTemplate.Duplicate(materialList, false);
            StripMaterialCardFixtureDrivers(card);

            Slot? nameSlot = FindDescendant(card, "material-0-name");
            Slot? outlineSlot = FindDescendant(card, "material-0-outline");

            Slot? sphereSlot = FindDescendant(card, "material-0-sphere")
                ?? FindDescendant(card, "material-0-preview");
            Image? sphereImage = sphereSlot?.GetComponent<Image>() ?? sphereSlot?.GetComponentInChildren<Image>();

            var pooled = new PooledMaterialCard
            {
                Card = card,
                NameLabel = new FittedTextBinding(nameSlot),
                OutlineSlot = outlineSlot,
                SphereSlot = sphereImage?.Slot ?? sphereSlot,
                SphereImage = sphereImage
            };

            var button = card.GetComponent<Button>();
            if (button != null)
            {
                UnityStationHandle.ReleaseFixtureActions(button);
                button.RequireLockInToPress.Value = true;
                button.PassThroughVerticalMovement.Value = true;
                button.PassThroughHorizontalMovement.Value = true;
                NativeButtonEvents.Pressed(button, (_, _) =>
                {
                    var action = pooled.CurrentAction;
                    if (action != null) schedule(action);
                });
            }

            pooledMaterialCards.Add(pooled);
        }

        return pooledMaterialCards[index];
    }

    private void BindNativeMaterialCard(int index, PackageMaterialChoice choice, string currentGuid, CancellationToken cancellationToken)
    {
        PooledMaterialCard pooled = GetOrCreatePooledMaterialCard(index);
        Slot card = pooled.Card;
        card.Name = $"Material Card · {choice.Name}";
        card.ActiveSelf = true;

        string label = string.IsNullOrWhiteSpace(choice.Folder) || choice.Folder.Equals(currentFolder, StringComparison.OrdinalIgnoreCase)
            ? choice.Name
            : $"{choice.Name}  ·  {choice.Folder}";

        pooled.NameLabel?.SetText(label);

        bool isCurrent = !string.IsNullOrEmpty(currentGuid) && choice.Guid.Equals(currentGuid, StringComparison.OrdinalIgnoreCase);
        if (pooled.OutlineSlot != null)
        {
            pooled.OutlineSlot.ActiveSelf = isCurrent;
        }

        if (pooled.SphereImage != null && pooled.SphereSlot != null)
        {
            pooled.SphereImage.Sprite.Target = null;
            var baseCol = choice.BaseColor != default
                ? choice.BaseColor
                : MaterialThumbnailGenerator.GetMaterialBaseColor(choice.Guid, getImporter());
            pooled.SphereImage.Tint.Value = new colorX(baseCol.r, baseCol.g, baseCol.b, baseCol.a);

            ApplyMaterialThumbnail(card, pooled.SphereSlot, pooled.SphereImage, choice.Guid, cancellationToken);
        }

        var capturedChoice = choice;
        pooled.CurrentAction = () => ApplyChoiceAsync(capturedChoice);
    }

    private void ApplyMaterialThumbnail(
        Slot card,
        Slot sphereSlot,
        Image image,
        string materialGuid,
        CancellationToken cancellationToken)
    {
        var importer = getImporter();
        if (importer == null || importer.world == null || importer.world.IsDestroyed || string.IsNullOrEmpty(materialGuid)) return;

        Task.Run(async () =>
        {
            try
            {
                // Yield briefly so UI layout and opening transitions finish rendering smoothly at full framerate
                await Task.Delay(100, cancellationToken);
                if (cancellationToken.IsCancellationRequested) return;

                if (!thumbnailUriCache.TryGetValue(materialGuid, out Uri url))
                {
                    Task<Uri?> task = thumbnailTaskCache.GetOrAdd(materialGuid, async guid =>
                    {
                        string? thumbPath = await MaterialThumbnailGenerator.GetOrCreateThumbnailAsync(guid, importer, cancellationToken);
                        if (string.IsNullOrEmpty(thumbPath) || cancellationToken.IsCancellationRequested) return null;

                        Uri importedUrl = await importer.world.Engine.LocalDB.ImportLocalAssetAsync(thumbPath, LocalDB.ImportLocation.Copy);
                        if (importedUrl != null) thumbnailUriCache[guid] = importedUrl;
                        return importedUrl;
                    });

                    url = await task;
                    if (url == null)
                    {
                        thumbnailTaskCache.TryRemove(materialGuid, out _);
                        return;
                    }
                }

                if (cancellationToken.IsCancellationRequested) return;

                importer.world.RunSynchronously(() =>
                {
                    if (cancellationToken.IsCancellationRequested || !isAlive() || card.IsDestroyed || sphereSlot.IsDestroyed || image.IsDestroyed) return;

                    var texture = sphereSlot.GetComponent<StaticTexture2D>() ?? sphereSlot.AttachComponent<StaticTexture2D>();
                    texture.URL.Value = url;
                    var spriteProvider = sphereSlot.GetComponent<SpriteProvider>() ?? sphereSlot.AttachComponent<SpriteProvider>();
                    spriteProvider.Texture.Target = texture;
                    image.Sprite.Target = spriteProvider;
                    image.Tint.Value = colorX.White;
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                UnityPackageImporter.Warn($"Could not load thumbnail for material {materialGuid}: {ex.Message}");
            }
        }, cancellationToken);
    }

    private PooledFolderRow GetOrCreatePooledFolderRow(int index)
    {
        while (pooledFolderRows.Count <= index)
        {
            Slot row = folderChoiceTemplate.Duplicate(folderList, false);
            Slot? labelSlot = FindDescendant(row, "folder-choice-1-label");

            var pooled = new PooledFolderRow
            {
                Row = row,
                Label = new FittedTextBinding(labelSlot)
            };

            var btn = row.GetComponent<Button>();
            if (btn != null)
            {
                UnityStationHandle.ReleaseFixtureActions(btn);
                foreach (var comp in row.GetComponentsInChildren<Component>())
                {
                    if (comp.GetType().Name.StartsWith("ButtonValueSet`", StringComparison.Ordinal))
                        comp.Enabled = false;
                }
                btn.RequireLockInToPress.Value = true;
                btn.PassThroughVerticalMovement.Value = true;
                btn.PassThroughHorizontalMovement.Value = true;
                NativeButtonEvents.Pressed(btn, (_, _) =>
                {
                    if (!isAlive() || isBusy()) return;
                    pooled.CurrentAction?.Invoke();
                });
            }

            pooledFolderRows.Add(pooled);
        }

        return pooledFolderRows[index];
    }

    private void RefreshFolderRows(bool animateTransition = false)
    {
        if (!isAlive() || folderList == null || folderChoiceTemplate == null) return;

        string normalizedBrowsed = NormalizeFolder(browsedFolder);
        bool canGoUp = !string.IsNullOrEmpty(normalizedBrowsed);

        if (folderPickerTitle != null && !folderPickerTitle.Content.IsDriven)
        {
            string titleText = !canGoUp
                ? L("folders")
                : System.IO.Path.GetFileName(normalizedBrowsed);
            folderPickerTitle.ParseRichText.Value = false;
            folderPickerTitle.Content.Value = titleText;
        }

        if (folderChoiceRootTemplate != null)
        {
            folderChoiceRootTemplate.ActiveSelf = true;
            folderChoiceRootLabel ??= new FittedTextBinding(FindDescendant(folderChoiceRootTemplate, "folder-choice-0-label"));
            folderChoiceRootLabel.SetText(L("useFolder"));
        }

        var subfolders = materialCatalog?.ChildFolders(browsedFolder) ?? Array.Empty<string>();
        for (int i = 0; i < subfolders.Count; i++)
        {
            string folderName = subfolders[i];
            PooledFolderRow pooled = GetOrCreatePooledFolderRow(i);
            pooled.Row.Name = $"Folder Choice · {folderName}";
            pooled.Row.ActiveSelf = true;
            pooled.Label?.SetText(folderName);

            string destination = string.IsNullOrEmpty(normalizedBrowsed) ? folderName : normalizedBrowsed + "/" + folderName;
            pooled.CurrentAction = () =>
            {
                // Navigating into a subfolder updates the folder popover and plays the FolderSlide transition
                browsedFolder = destination;
                RefreshFolderRows(animateTransition: true);
            };
        }

        for (int i = subfolders.Count; i < pooledFolderRows.Count; i++)
        {
            var extra = pooledFolderRows[i];
            if (extra.Row != null && !extra.Row.IsDestroyed)
            {
                extra.Row.ActiveSelf = false;
                extra.CurrentAction = null;
            }
        }

        if (folderCanGoUp != null && !folderCanGoUp.IsRemoved)
        {
            if (folderNavUp != null && !folderNavUp.Slot.IsDestroyed)
            {
                folderNavUp.Slot.ActiveSelf = true;
            }
            if (!folderCanGoUp.IsDriven)
            {
                folderCanGoUp.Value = canGoUp;
            }
        }
        else if (folderNavUp != null && !folderNavUp.Slot.IsDestroyed)
        {
            folderNavUp.Enabled = canGoUp;
            folderNavUp.Slot.ActiveSelf = canGoUp;
            var labelText = folderNavUp.Slot.GetComponentInChildren<Text>();
            if (labelText != null) labelText.Enabled = canGoUp;
        }

        if (folderBrowseIndex != null && !folderBrowseIndex.IsRemoved && !folderBrowseIndex.IsDriven)
        {
            int nextDepth = ComputeBrowseDepthIndex(normalizedBrowsed, animateTransition);
            folderBrowseIndex.Value = nextDepth;
            lastBrowseDepthIndex = nextDepth;
            lastBrowsedFolder = normalizedBrowsed;
        }
    }

    private int ComputeBrowseDepthIndex(string normalizedFolder, bool animateTransition)
    {
        if (string.IsNullOrEmpty(normalizedFolder)) return 0;

        int depth = 1;
        for (int i = 0; i < normalizedFolder.Length; i++)
        {
            if (normalizedFolder[i] == '/') depth++;
        }

        int clamped = Math.Clamp(depth, 1, 4);
        if (animateTransition && clamped == lastBrowseDepthIndex && !string.Equals(normalizedFolder, lastBrowsedFolder, StringComparison.OrdinalIgnoreCase))
        {
            return clamped == 4 ? 3 : clamped + 1;
        }
        return clamped;
    }

    private void UpdateBreadcrumb()
    {
        if (breadcrumbPath != null && !breadcrumbPath.Content.IsDriven)
        {
            breadcrumbPath.Content.Value = string.IsNullOrEmpty(currentFolder) ? "" : "›  " + currentFolder;
        }
    }

    private static string NormalizeFolder(string folder)
    {
        return (folder ?? "").Replace('\\', '/').Trim('/').Replace("//", "/");
    }


    private static void StripMaterialCardFixtureDrivers(Slot? card)
    {
        if (card == null || card.IsDestroyed) return;
        foreach (var eq in card.GetComponentsInChildren<ValueEqualityDriver<string>>())
        {
            eq.Target.Target = null;
            eq.TargetValue.Target = null;
            eq.Enabled = false;
        }
        foreach (var comp in card.GetComponentsInChildren<Component>())
        {
            string typeName = comp.GetType().Name;
            if (typeName.StartsWith("ButtonValueSet`", StringComparison.Ordinal) ||
                typeName.StartsWith("ButtonReferenceSet`", StringComparison.Ordinal))
            {
                comp.Enabled = false;
            }
        }
    }

    private static void SetFittedText(Slot root, string value)
    {
        if (root == null || root.IsDestroyed) return;

        var directText = root.GetComponent<Text>();
        var childTexts = root.GetComponentsInChildren<Text>().Where(t => t != directText).ToList();

        if (childTexts.Count > 0)
        {
            if (directText != null)
            {
                if (!directText.Content.IsDriven) directText.Content.Value = value;
                directText.Enabled = false;
                directText.InteractionTarget.Value = false;
            }
            foreach (var childText in childTexts)
            {
                childText.ParseRichText.Value = true;
                if (!childText.Content.IsDriven) childText.Content.Value = value;
                childText.Enabled = true;
                childText.InteractionTarget.Value = false;
            }
        }
        else if (directText != null)
        {
            directText.ParseRichText.Value = false;
            if (!directText.Content.IsDriven) directText.Content.Value = value;
            directText.Enabled = true;
            directText.InteractionTarget.Value = false;
        }
        else
        {
            foreach (var t in root.GetComponentsInChildren<Text>())
            {
                t.ParseRichText.Value = true;
                if (!t.Content.IsDriven) t.Content.Value = value;
                t.Enabled = true;
                t.InteractionTarget.Value = false;
            }
        }
    }

    private async Task ApplyChoiceAsync(PackageMaterialChoice choice)
    {
        if (!isAlive() || isBusy() || selectedSlot == null || displayedItem != getCurrent()) return;
        setStage("Replace mesh material");
        if (choice.Guid.Equals(ResolveSlotMaterialGuid(selectedSlot), StringComparison.OrdinalIgnoreCase))
        {
            ClosePickerOnly();
            return;
        }

        setBusy(true);
        try
        {
            var material = await getImporter().ImportPackageMaterialAsync(choice.Guid);
            await default(ToWorld);
            if (!isAlive() || displayedItem != getCurrent() || selectedSlot == null) return;
            var targetSlot = selectedSlot;
            targetSlot.Assign(material);
            rebuildPreview();
            ClosePickerOnly();
            if (isNative)
            {
                UpdateSlotInNativeCards(targetSlot);
            }
            else
            {
                RefreshMainRows(mainQuery);
            }
        }
        finally
        {
            await default(ToWorld);
            if (isAlive()) setBusy(false);
        }
    }

    private async Task RestoreOriginalAsync(MaterialSlotBinding slot)
    {
        if (!isAlive() || isBusy() || slot == null) return;
        setStage("Restore original mesh material");
        setBusy(true);
        try
        {
            slot.Assign(slot.Original);
            await default(ToWorld);
            if (!isAlive()) return;
            rebuildPreview();
            ClosePickerOnly();
            if (isNative)
            {
                UpdateSlotInNativeCards(slot);
            }
            else
            {
                RefreshMainRows(mainQuery);
            }
        }
        finally
        {
            await default(ToWorld);
            if (isAlive()) setBusy(false);
        }
    }

    private void StartSearchMonitor(Slot panel, Slot field, bool isMain)
    {
        int generation = isMain ? ++mainSearchGeneration : ++materialSearchGeneration;
        _ = panel.World.RootSlot.StartGlobalTask(async () =>
        {
            string previous = SearchText(field).Content.Value ?? "";
            while (isAlive() && !panel.IsDestroyed && !field.IsDestroyed)
            {
                await Task.Delay(180);
                await default(ToWorld);
                bool active = isMain
                    ? generation == mainSearchGeneration && mainVisible.State.Value
                    : generation == materialSearchGeneration && materialVisible.State.Value;
                if (!active || isBusy()) return;
                string next = SearchText(field).Content.Value ?? "";
                if (string.Equals(previous, next, StringComparison.Ordinal)) continue;
                previous = next;
                UpdateSearchHint(field, isMain ? mainSearchHint : materialSearchHint);
                if (isMain) RefreshMainRows(next);
                else RefreshMaterialRows(next);
            }
        });
    }

    private Slot EnsureSearchField(Slot list, string name)
    {
        var existing = list.Children.SingleOrDefault(slot => slot.Name == name);
        if (existing != null) return existing;
        Slot field = searchFieldTemplate.Duplicate(list, false);
        field.Name = name;
        field.ActiveSelf = true;
        SearchText(field).Content.Value = "";
        return field;
    }

    private Slot EnsureSearchHint(Slot field, string key)
    {
        Slot existing = field.Children.SingleOrDefault(slot => slot.Name == "Runtime Search Hint");
        if (existing != null)
        {
            existing.GetComponent<Text>().Content.Value = L(key);
            return existing;
        }

        Text input = SearchText(field);
        Slot hint = input.Slot.Duplicate(field, false);
        hint.Name = "Runtime Search Hint";
        var hintText = hint.GetComponent<Text>();
        DisableLocalizedOverrides(hint);
        hintText.ParseRichText.Value = false;
        hintText.Content.Value = L(key);
        hintText.Color.Value = new colorX(0.55f, 0.60f, 0.66f, 0.9f);
        hintText.InteractionTarget.Value = false;
        return hint;
    }

    private static void UpdateSearchHint(Slot field, Slot hint)
    {
        if (field == null || field.IsDestroyed || hint == null || hint.IsDestroyed) return;
        string value = SearchText(field)?.Content.Value;
        hint.ActiveSelf = string.IsNullOrEmpty(value);
    }

    private static Text SearchText(Slot field)
    {
        if (field == null || field.IsDestroyed) return null;

        var textEditor = field.GetComponent<TextEditor>() ?? field.GetComponentInChildren<TextEditor>();
        if (textEditor?.Text.Target is Text editorText) return editorText;

        var textField = field.GetComponent<TextField>() ?? field.GetComponentInChildren<TextField>();
        if (textField?.Editor.Target?.Text.Target is Text tfText) return tfText;

        foreach (var text in field.GetComponentsInChildren<Text>())
        {
            if (text.Slot.Name != "mesh-search-placeholder" &&
                text.Slot.Name != "material-search-placeholder" &&
                text.Slot.Name != "Runtime Search Hint")
            {
                return text;
            }
        }

        throw new InvalidOperationException("The native search field is not linked to a UIX.Text component.");
    }

    private void ClearRows(Slot list, Slot template, Slot search)
    {
        foreach (Slot child in list.Children.ToArray())
            if (child != template && child != search) child.Destroy();
        if (template != null && !template.IsDestroyed) template.ActiveSelf = false;
        if (search != null && !search.IsDestroyed) search.ActiveSelf = true;
    }

    private void AddRow(Slot list, Slot template, string title, Func<Task> action)
    {
        Slot row = template.Duplicate(list, false);
        row.Name = "Runtime Material Row";
        row.ActiveSelf = true;
        var button = row.GetComponent<Button>();
        UnityStationHandle.ReleaseFixtureActions(button);
        if (button != null)
        {
            button.RequireLockInToPress.Value = true;
            button.PassThroughVerticalMovement.Value = true;
            button.PassThroughHorizontalMovement.Value = true;
        }
        foreach (var equality in row.GetComponentsInChildren<ValueEqualityDriver<int>>())
            equality.Enabled = false;
        var selectedMark = FindDescendant(row, "Selected Checkmark");
        if (selectedMark != null) selectedMark.ActiveSelf = false;
        var titleText = FindDescendant(row, "Title")?.GetComponent<Text>() ?? throw new InvalidOperationException("The native list row is missing its title text.");
        titleText.ParseRichText.Value = false;
        titleText.Content.Value = title;
        if (action != null)
            NativeButtonEvents.Pressed(button, (_, _) => schedule(action));
        else button.Enabled = false;
    }

    private Slot AddBlueDismissButton(Slot popupRoot, Action dismiss)
    {
        Slot header = FindDescendant(popupRoot, "Header");
        Slot buttonSlot = ui.Get<Button>("close").Slot.Duplicate(header, false);
        buttonSlot.Name = "Runtime Blue Dismiss";
        var button = buttonSlot.GetComponent<Button>();
        UnityStationHandle.ReleaseFixtureActions(button);
        var text = buttonSlot.GetComponentInChildren<Text>();
        if (text != null) text.Content.Value = "×";
        var background = buttonSlot.GetComponent<Image>();
        if (background != null) background.Tint.Value = new colorX(0.14f, 0.64f, 0.98f, 1f);
        NativeButtonEvents.Pressed(button, (_, _) => dismiss());
        return buttonSlot;
    }

    private static void DisableLocalizedOverrides(Slot slot)
    {
        foreach (var driver in slot.GetComponentsInChildren<BooleanValueDriver<string>>())
        {
            driver.Enabled = false;
            driver.TargetField.Target = null;
        }
    }

    private string CurrentLocale => LocalizationManager.DetectLocaleCode();

    private void SetText(Text text, string key, params object[] args)
    {
        if (text == null) return;
        text.ParseRichText.Value = false;
        text.Content.Value = LocalizationManager.GetStringForLocale(CurrentLocale, key, args);
    }

    private string L(string key, params object[] args) =>
        LocalizationManager.GetStringForLocale(CurrentLocale, key, args);

    private static BooleanValueDriver<bool> FirstVisibilityDriver(Slot root) =>
        root.GetComponents<BooleanValueDriver<bool>>().FirstOrDefault()
        ?? throw new InvalidOperationException("The native material popover template is missing its visibility driver.");

    private static Slot KeepOnlyRowTemplate(Slot list)
    {
        Slot template = FindDescendant(list, "Avatar Option 01");
        foreach (Slot child in list.Children.ToArray())
            if (child != template) child.Destroy();
        template.Name = "Runtime Material Row Template";
        template.ActiveSelf = false;
        return template;
    }

    private static Slot FindAvatarList(Slot root) => FindPath(root,
        "Avatar Selection Popover Content", "Selection Surface", "Eligible Avatars", "Avatar List Viewport", "Scrollable Avatar List");

    private static Text FindHeaderTitle(Slot root) => FindPath(root, "Avatar Selection Popover Content", "Selection Surface", "Header", "Title")
        .GetComponent<Text>() ?? throw new InvalidOperationException("The native popover template is missing its title text.");

    private static Slot FindPath(Slot root, params string[] names)
    {
        Slot current = root;
        foreach (string name in names)
            current = current.Children.Single(slot => slot.Name == name);
        return current;
    }

    private static Slot FindDescendant(Slot root, string name)
    {
        if (root.Name == name) return root;
        foreach (Slot child in root.Children)
        {
            Slot result = FindDescendant(child, name);
            if (result != null) return result;
        }
        return null;
    }

    private static IEnumerable<Slot> Descendants(Slot root)
    {
        yield return root;
        foreach (Slot child in root.Children)
            foreach (Slot nested in Descendants(child)) yield return nested;
    }

    private sealed class MaterialRendererBinding
    {
        public string Name { get; }
        public IReadOnlyList<MaterialSlotBinding> Slots { get; }
        public MaterialRendererBinding(string name, IReadOnlyList<MaterialSlotBinding> slots) { Name = name; Slots = slots; }
    }

    private sealed class MaterialSlotBinding
    {
        public string RendererName { get; }
        public int Index { get; }
        public string InitialMaterialName { get; }
        public IAssetProvider<Material> Original { get; }
        private readonly Func<IAssetProvider<Material>> read;
        private readonly Action<IAssetProvider<Material>> write;
        public MaterialSlotBinding(string rendererName, int index, string materialName,
            Func<IAssetProvider<Material>> read, Action<IAssetProvider<Material>> write, IAssetProvider<Material> original)
        {
            RendererName = rendererName;
            Index = index;
            InitialMaterialName = materialName;
            this.read = read;
            this.write = write;
            Original = original;
        }
        public IAssetProvider<Material> ReadCurrent() => read();
        public void Assign(IAssetProvider<Material> material) => write(material);
        public string CurrentGuid(UnityProjectImporter importer)
        {
            var current = ReadCurrent();
            if (current == null) return "";

            if (importer != null && importer.TryGetMaterialGuid(current, out string registeredGuid))
                return registeredGuid;

            if (importer?.TasksMaterials != null)
            {
                var taskMatch = importer.TasksMaterials.FirstOrDefault(task =>
                    !task.ismissing && (task.finalMaterial == current || ReferenceEquals(task.finalMaterial, current)));
                if (!string.IsNullOrEmpty(taskMatch?.MaterialGuid))
                    return taskMatch.MaterialGuid;
            }

            Slot? providerSlot = current.FindNearestParent<Slot>();
            if (providerSlot != null && !string.IsNullOrEmpty(providerSlot.Name))
            {
                int open = providerSlot.Name.IndexOf('[');
                int close = providerSlot.Name.IndexOf(']', open >= 0 ? open : 0);
                if (open >= 0 && close > open)
                {
                    string raw = providerSlot.Name.Substring(open + 1, close - open - 1);
                    string guid = raw.Replace("-toon", "", StringComparison.OrdinalIgnoreCase).Trim();
                    if (!string.IsNullOrEmpty(guid)) return guid;
                }
            }

            return "";
        }
    }
}
