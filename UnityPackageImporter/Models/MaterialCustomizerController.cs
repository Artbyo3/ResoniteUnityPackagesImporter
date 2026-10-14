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
    private readonly Text breadcrumbPath;
    private readonly BooleanValueDriver<bool> folderPickerVisible;
    private readonly Text meshCountText;
    private readonly Text materialCountText;
    private readonly Text pickerContextText;
    private readonly Text pickerContextMaterialText;
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
    private MaterialSlotBinding selectedSlot;
    private MaterialRendererBinding expandedRenderer;
    private readonly HashSet<string> expandedRendererNames = new(StringComparer.OrdinalIgnoreCase);
    private UnityPrefabImportTask displayedItem;
    private IReadOnlyList<MaterialRendererBinding> renderers = Array.Empty<MaterialRendererBinding>();
    private PackageMaterialCatalog materialCatalog;
    private int mainSearchGeneration;
    private int materialSearchGeneration;
    private CancellationTokenSource materialRowsCts;
    private readonly ConcurrentDictionary<string, Uri> thumbnailUriCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<Uri?>> thumbnailTaskCache = new(StringComparer.OrdinalIgnoreCase);

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
        public readonly Dictionary<MaterialSlotBinding, (Slot NameSlot, Button RestoreBtn)> SlotRows = new();
    }

    private readonly List<NativeMeshCardHolder> nativeCards = new();
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
                if (driver.TargetField.Target == mainRowTemplate.ActiveSelf_Field ||
                    driver.TargetField.Target == materialRowTemplate.ActiveSelf_Field)
                {
                    driver.TargetField.Target = null;
                    driver.Enabled = false;
                }
            }
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
                    if (next) RefreshFolderRows();
                });
            }

            if (folderPickerVisible == null && folderPickerRoot != null)
            {
                folderPickerVisible = customizerPreview.GetComponents<BooleanValueDriver<bool>>()
                    .FirstOrDefault(d => d.TargetField.Target == folderPickerRoot.ActiveSelf_Field)
                    ?? customizerPreview.GetComponents<BooleanValueDriver<bool>>().ElementAtOrDefault(2);
            }
            if (folderPickerVisible != null) folderPickerVisible.State.Value = false;
            else if (folderPickerRoot != null) folderPickerRoot.ActiveSelf = false;

            if (folderPickerRoot != null)
            {
                folderList = Descendants(folderPickerRoot).FirstOrDefault(slot => slot.Name == "folder-picker-list-content");
                if (folderList != null)
                {
                    folderChoiceRootTemplate = folderList.Children.FirstOrDefault(slot => slot.Name == "folder-choice-0");
                    folderChoiceTemplate = folderList.Children.FirstOrDefault(slot => slot.Name == "folder-choice-1");
                    if (folderChoiceRootTemplate != null) folderChoiceRootTemplate.ActiveSelf = false;
                    if (folderChoiceTemplate != null) folderChoiceTemplate.ActiveSelf = false;
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
                    folderNavUp.RequireLockInToPress.Value = true;
                    folderNavUp.PassThroughVerticalMovement.Value = true;
                    folderNavUp.PassThroughHorizontalMovement.Value = true;
                    folderNavUp.Enabled = false;
                    folderNavUp.Slot.ActiveSelf = false;
                    NativeButtonEvents.Pressed(folderNavUp, (_, _) => schedule(() =>
                    {
                        string normalized = NormalizeFolder(currentFolder);
                        int lastSlash = normalized.LastIndexOf('/');
                        string parent = lastSlash >= 0 ? normalized[..lastSlash] : "";
                        currentFolder = parent;
                        UpdateBreadcrumb();
                        RefreshFolderRows();
                        RefreshMaterialRows(materialQuery);
                        return Task.CompletedTask;
                    }));
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
                        UpdateBreadcrumb();
                        if (folderPickerVisible?.State.Value == true || (folderPickerVisible == null && folderPickerRoot?.ActiveSelf == true))
                        {
                            RefreshFolderRows();
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
            meshCountText = null;
            materialCountText = null;
            pickerContextText = null;
            pickerContextMaterialText = null;
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
            "Customize Materials", "マテリアルをカスタマイズ");
        SetText(FindDescendant(card, "Labels").Children.Single(slot => slot.Name == "Subtitle").GetComponent<Text>(),
            "Choose materials for individual mesh slots", "メッシュスロットごとにマテリアルを選択");

        Slot buttonSlot = FindDescendant(card, "Button element");
        var button = buttonSlot.GetComponent<Button>();
        foreach (Button other in card.GetComponentsInChildren<Button>())
        {
            if (ReferenceEquals(other, button)) continue;
            UnityStationHandle.ReleaseFixtureActions(other);
            other.Slot.ActiveSelf = false;
        }
        UnityStationHandle.ReleaseFixtureActions(button);
        SetText(buttonSlot.GetComponentInChildren<Text>(), "Open", "開く");
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
            SetText(mainTitle, "Customize Materials", "マテリアルをカスタマイズ");
            if (meshCountText != null)
            {
                meshCountText.Slot.ActiveSelf = true;
                meshCountText.Content.Value = $"{renderers.Count} {L("meshes", "メッシュ")}";
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
            mainSearchHint = EnsureSearchHint(mainSearchField, "Search mesh names", "メッシュ名を検索");
            mainSearchText.Content.Value = "";
            UpdateSearchHint(mainSearchField, mainSearchHint);
            SetText(mainTitle, $"Customize Materials · {renderers.Count} meshes", $"マテリアルをカスタマイズ · {renderers.Count} メッシュ");
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
        if (material == null) return L("Missing material", "マテリアルなし");
        var imported = importer.TasksMaterials.FirstOrDefault(task => !task.ismissing && (task.finalMaterial == material || ReferenceEquals(task.finalMaterial, material)));
        if (!string.IsNullOrEmpty(imported?.MaterialGuid))
        {
            var choice = catalog.Choices.FirstOrDefault(candidate => candidate.Guid.Equals(imported.MaterialGuid, StringComparison.OrdinalIgnoreCase));
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

        return L("Imported material", "インポート済みマテリアル");
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
            SetText(mainTitle,
                string.IsNullOrWhiteSpace(mainQuery) ? $"Customize Materials · {renderers.Count} meshes" : $"Customize Materials · {matches.Length} of {renderers.Count} meshes",
                string.IsNullOrWhiteSpace(mainQuery) ? $"マテリアルをカスタマイズ · {renderers.Count} メッシュ" : $"マテリアルをカスタマイズ · {matches.Length} / {renderers.Count} メッシュ");

            if (matches.Length == 0)
            {
                AddRow(mainList, mainRowTemplate, L("No renderer names match that search", "検索条件に一致するメッシュがありません"), null);
                return;
            }

            foreach (var renderer in matches)
            {
                string marker = ReferenceEquals(expandedRenderer, renderer) ? "−" : "+";
                AddRow(mainList, mainRowTemplate, $"{marker}  {renderer.Name}  ·  {renderer.Slots.Count} {L("material slots", "マテリアルスロット")}",
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
                    AddRow(mainList, mainRowTemplate, $"    {L("Slot", "スロット")} {slot.Index + 1}  ·  {currentName}", () => OpenMaterialPickerAsync(slot));
                }
            }
        }
    }

    private void RebuildNativeMeshCards()
    {
        ClearNativeMeshCards();
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
                ? $"{nativeCards.Count} {L("meshes", "メッシュ")}"
                : $"{visibleCount} / {nativeCards.Count} {L("meshes", "メッシュ")}";
        }
    }

    private NativeMeshCardHolder CreateNativeMeshCard(MaterialRendererBinding renderer)
    {
        Slot card = mainRowTemplate.Duplicate(mainList, false);
        card.Name = $"Mesh Card · {renderer.Name}";
        card.ActiveSelf = true;

        foreach (var bvd in card.GetComponentsInChildren<BooleanValueDriver<bool>>())
        {
            if (bvd.TargetField.Target == card.ActiveSelf_Field)
            {
                bvd.TargetField.Target = null;
                bvd.Enabled = false;
            }
        }

        Slot titleSlot = FindDescendant(card, "mesh-0-title");
        SetFittedText(titleSlot, renderer.Name);

        Slot countSlot = FindDescendant(card, "mesh-0-slot-count");
        SetFittedText(countSlot, $"{renderer.Slots.Count} {L("slots", "スロット")}");

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
                foreach (var bvd in child.GetComponentsInChildren<BooleanValueDriver<bool>>())
                {
                    if (bvd.TargetField.Target == child.ActiveSelf_Field)
                    {
                        bvd.TargetField.Target = null;
                        bvd.Enabled = false;
                    }
                }
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
                if (comp.GetType().Name.StartsWith("ButtonValueSet`", StringComparison.Ordinal) ||
                    comp.GetType().Name.StartsWith("ButtonToggle", StringComparison.Ordinal))
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

        foreach (var slot in holder.Renderer.Slots)
        {
            Slot slotRow = holder.SlotTemplate.Duplicate(holder.SlotsContainer, false);
            slotRow.Name = $"Material Slot Row {slot.Index + 1}";
            slotRow.ActiveSelf = true;

            foreach (var bvd in slotRow.GetComponentsInChildren<BooleanValueDriver<bool>>())
            {
                if (bvd.TargetField.Target == slotRow.ActiveSelf_Field)
                {
                    bvd.TargetField.Target = null;
                    bvd.Enabled = false;
                }
            }

            Slot slotLabel = FindDescendant(slotRow, "slot-0-0-slot-label");
            SetFittedText(slotLabel, $"Slot {slot.Index + 1}");

            string currentName = MaterialName(slot.ReadCurrent(), getImporter(), materialCatalog);
            Slot nameSlot = FindDescendant(slotRow, "slot-0-0-name");
            SetFittedText(nameSlot, currentName);

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
                foreach (var bvd in restoreBtn.Slot.GetComponents<BooleanValueDriver<bool>>())
                {
                    bvd.TargetField.Target = null;
                    bvd.Enabled = false;
                }
                restoreBtn.Slot.ActiveSelf = isModified;
                NativeButtonEvents.Pressed(restoreBtn, (_, _) => schedule(() => RestoreOriginalAsync(slot)));
            }

            holder.SlotRows[slot] = (nameSlot, restoreBtn);
        }
    }

    private void UpdateSlotInNativeCards(MaterialSlotBinding slot)
    {
        if (slot == null) return;
        foreach (var holder in nativeCards)
        {
            if (holder.SlotRows.TryGetValue(slot, out var rowInfo))
            {
                string updatedName = MaterialName(slot.ReadCurrent(), getImporter(), materialCatalog);
                SetFittedText(rowInfo.NameSlot, updatedName);

                bool isModified = !ReferenceEquals(slot.ReadCurrent(), slot.Original);
                if (rowInfo.RestoreBtn != null)
                {
                    rowInfo.RestoreBtn.Slot.ActiveSelf = isModified;
                }
            }
        }
    }

    private async Task OpenMaterialPickerAsync(MaterialSlotBinding slot)
    {
        if (!isAlive() || isBusy() || materialCatalog == null) return;
        setStage("Browse package materials");
        selectedSlot = slot;
        string? initialGuid = slot.CurrentGuid(getImporter());
        var matchingChoice = !string.IsNullOrEmpty(initialGuid)
            ? materialCatalog.Choices.FirstOrDefault(c => c.Guid.Equals(initialGuid, StringComparison.OrdinalIgnoreCase))
            : null;

        if (matchingChoice == null && !string.IsNullOrEmpty(slot.InitialMaterialName))
        {
            string cleanName = slot.InitialMaterialName;
            int bracketIdx = cleanName.IndexOf('[');
            if (bracketIdx > 0) cleanName = cleanName[..bracketIdx].Trim();

            matchingChoice = materialCatalog.Choices.FirstOrDefault(c =>
                c.Name.Equals(cleanName, StringComparison.OrdinalIgnoreCase) ||
                c.Name.Equals(slot.InitialMaterialName, StringComparison.OrdinalIgnoreCase));
        }

        currentFolder = matchingChoice?.Folder ?? "";
        materialQuery = "";

        if (isNative)
        {
            materialSearchText = SearchText(materialSearchField);
            materialSearchHint = Descendants(materialSearchField).FirstOrDefault(s => s.Name == "material-search-placeholder");
            if (materialSearchText != null) materialSearchText.Content.Value = "";
            UpdateSearchHint(materialSearchField, materialSearchHint);
            SetText(materialTitle, "Choose Material", "マテリアルを選択");
            if (pickerContextText != null)
                pickerContextText.Content.Value = $"{slot.RendererName} · Slot {slot.Index + 1}";
            if (pickerContextMaterialText != null)
            {
                string curName = MaterialName(slot.ReadCurrent(), getImporter(), materialCatalog);
                pickerContextMaterialText.Content.Value = curName;
            }
            UpdateBreadcrumb();
            materialVisible.State.Value = true;
            RefreshMaterialRows("");
            StartSearchMonitor(materialRoot, materialSearchField, isMain: false);
        }
        else
        {
            materialSearchField = EnsureSearchField(materialList, "Material Search");
            materialSearchText = SearchText(materialSearchField);
            materialSearchHint = EnsureSearchHint(materialSearchField, "Search materials or folders", "マテリアルまたはフォルダーを検索");
            materialSearchText.Content.Value = "";
            UpdateSearchHint(materialSearchField, materialSearchHint);
            SetText(materialTitle, $"Choose Material · {slot.RendererName} · Slot {slot.Index + 1}",
                $"マテリアルを選択 · {slot.RendererName} · スロット {slot.Index + 1}");
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
        ClearRows(materialList, materialRowTemplate, isNative ? null : materialSearchField);

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
                    try { materialCountText.Content.Value = $"{choices.Count} {L("materials", "マテリアル")}"; } catch { }
                }
            }

            string currentGuid = selectedSlot?.CurrentGuid(getImporter()) ?? "";
            foreach (var choice in choices)
            {
                AddNativeMaterialCard(choice, currentGuid, cancellationToken);
            }
        }
        else
        {
            if (ReferenceEquals(selectedSlot.ReadCurrent(), selectedSlot.Original))
            {
                // No restore action is needed for an unchanged slot.
            }
            else
            {
                AddRow(materialList, materialRowTemplate, L("Restore Original Material", "元のマテリアルに戻す"), () => RestoreOriginalAsync(selectedSlot));
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
                    AddRow(materialList, materialRowTemplate, L("Folder", "フォルダー") + "  ·  " + child, () =>
                    {
                        currentFolder = destination;
                        RefreshMaterialRows(materialQuery);
                        return Task.CompletedTask;
                    });
                }
                if (!string.IsNullOrEmpty(folder))
                {
                    string parent = folder.Contains('/') ? folder[..folder.LastIndexOf('/')] : "";
                    AddRow(materialList, materialRowTemplate, L("Folder", "フォルダー") + "  ·  ..", () =>
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
                AddRow(materialList, materialRowTemplate, L("No matching materials", "一致するマテリアルがありません"), null);
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

    private void AddNativeMaterialCard(PackageMaterialChoice choice, string currentGuid, CancellationToken cancellationToken)
    {
        Slot card = materialRowTemplate.Duplicate(materialList, false);
        card.Name = $"Material Card · {choice.Name}";
        card.ActiveSelf = true;

        foreach (var bvd in card.GetComponentsInChildren<BooleanValueDriver<bool>>())
        {
            if (bvd.TargetField.Target == card.ActiveSelf_Field)
            {
                bvd.TargetField.Target = null;
                bvd.Enabled = false;
            }
        }

        string label = string.IsNullOrWhiteSpace(choice.Folder) || choice.Folder.Equals(currentFolder, StringComparison.OrdinalIgnoreCase)
            ? choice.Name
            : $"{choice.Name}  ·  {choice.Folder}";

        Slot nameSlot = FindDescendant(card, "material-0-name");
        SetFittedText(nameSlot, label);

        bool isCurrent = !string.IsNullOrEmpty(currentGuid) && choice.Guid.Equals(currentGuid, StringComparison.OrdinalIgnoreCase);
        Slot outline = FindDescendant(card, "material-0-outline");
        if (outline != null) outline.ActiveSelf = isCurrent;

        // Preview thumbnail sphere
        Slot sphereSlot = FindDescendant(card, "material-0-sphere")
            ?? FindDescendant(card, "material-0-preview");
        var image = sphereSlot?.GetComponent<Image>() ?? sphereSlot?.GetComponentInChildren<Image>();
        if (image != null && sphereSlot != null)
        {
            image.Sprite.Target = null;
            var baseCol = choice.BaseColor != default
                ? choice.BaseColor
                : MaterialThumbnailGenerator.GetMaterialBaseColor(choice.Guid, getImporter());
            image.Tint.Value = new colorX(baseCol.r, baseCol.g, baseCol.b, baseCol.a);

            ApplyMaterialThumbnail(card, sphereSlot, image, choice.Guid, cancellationToken);
        }

        var button = card.GetComponent<Button>();
        if (button != null)
        {
            UnityStationHandle.ReleaseFixtureActions(button);
            foreach (var comp in card.GetComponentsInChildren<Component>())
            {
                if (comp is ValueEqualityDriver<string> || comp.GetType().Name.StartsWith("ButtonValueSet`", StringComparison.Ordinal))
                    comp.Enabled = false;
            }
            button.RequireLockInToPress.Value = true;
            button.PassThroughVerticalMovement.Value = true;
            button.PassThroughHorizontalMovement.Value = true;
            NativeButtonEvents.Pressed(button, (_, _) => schedule(() => ApplyChoiceAsync(choice)));
        }
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

    private void RefreshFolderRows()
    {
        if (!isAlive() || folderList == null || folderChoiceTemplate == null) return;
        ClearRows(folderList, folderChoiceTemplate, folderChoiceRootTemplate);

        if (folderChoiceRootTemplate != null)
        {
            folderChoiceRootTemplate.ActiveSelf = true;
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

                Slot labelSlot = FindDescendant(folderChoiceRootTemplate, "folder-choice-0-label");
                string folderDisplayName = string.IsNullOrEmpty(currentFolder) ? "Root" : System.IO.Path.GetFileName(currentFolder);
                SetFittedText(labelSlot, L($"Use this folder ({folderDisplayName})", $"このフォルダーを使用 ({folderDisplayName})"));

                NativeButtonEvents.Pressed(rootBtn, (_, _) => schedule(() =>
                {
                    // "Use this folder" commits the current folder selection and closes the folder popover
                    if (folderPickerVisible != null) folderPickerVisible.State.Value = false;
                    else if (folderPickerRoot != null) folderPickerRoot.ActiveSelf = false;
                    UpdateBreadcrumb();
                    RefreshMaterialRows(materialQuery);
                    return Task.CompletedTask;
                }));
            }
        }

        var subfolders = materialCatalog?.ChildFolders(currentFolder) ?? Array.Empty<string>();
        foreach (string folderName in subfolders)
        {
            Slot row = folderChoiceTemplate.Duplicate(folderList, false);
            row.Name = $"Folder Choice · {folderName}";
            row.ActiveSelf = true;
            Slot labelSlot = FindDescendant(row, "folder-choice-1-label");
            SetFittedText(labelSlot, folderName);

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
                string destination = string.IsNullOrEmpty(currentFolder) ? folderName : currentFolder + "/" + folderName;
                NativeButtonEvents.Pressed(btn, (_, _) => schedule(() =>
                {
                    // Clicking a subfolder navigates into it without closing the popover
                    currentFolder = destination;
                    UpdateBreadcrumb();
                    RefreshFolderRows();
                    RefreshMaterialRows(materialQuery);
                    return Task.CompletedTask;
                }));
            }
        }

        if (folderNavUp != null)
        {
            bool canGoUp = !string.IsNullOrEmpty(currentFolder);
            folderNavUp.Enabled = canGoUp;
            folderNavUp.Slot.ActiveSelf = canGoUp;
        }
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

    private static void SetFittedText(Slot root, string value)
    {
        if (root == null || root.IsDestroyed) return;

        var directText = root.GetComponent<Text>();
        var childTexts = root.GetComponentsInChildren<Text>().Where(t => t != directText).ToList();

        if (childTexts.Count > 0)
        {
            if (directText != null)
            {
                directText.Content.Value = value;
                directText.Enabled = false;
                directText.InteractionTarget.Value = false;
            }
            foreach (var childText in childTexts)
            {
                childText.ParseRichText.Value = true;
                childText.Content.Value = value;
                childText.Enabled = true;
                childText.InteractionTarget.Value = false;
            }
        }
        else if (directText != null)
        {
            directText.ParseRichText.Value = false;
            directText.Content.Value = value;
            directText.Enabled = true;
            directText.InteractionTarget.Value = false;
        }
        else
        {
            foreach (var t in root.GetComponentsInChildren<Text>())
            {
                t.ParseRichText.Value = true;
                t.Content.Value = value;
                t.Enabled = true;
                t.InteractionTarget.Value = false;
            }
        }
    }

    private async Task ApplyChoiceAsync(PackageMaterialChoice choice)
    {
        if (!isAlive() || isBusy() || selectedSlot == null || displayedItem != getCurrent()) return;
        setStage("Replace mesh material");
        if (choice.Guid.Equals(selectedSlot.CurrentGuid(getImporter()), StringComparison.OrdinalIgnoreCase))
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
            selectedSlot.Assign(material);
            rebuildPreview();
            ClosePickerOnly();
            if (isNative)
            {
                UpdateSlotInNativeCards(selectedSlot);
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

    private Slot EnsureSearchHint(Slot field, string english, string japanese)
    {
        Slot existing = field.Children.SingleOrDefault(slot => slot.Name == "Runtime Search Hint");
        if (existing != null)
        {
            existing.GetComponent<Text>().Content.Value = L(english, japanese);
            return existing;
        }

        Text input = SearchText(field);
        Slot hint = input.Slot.Duplicate(field, false);
        hint.Name = "Runtime Search Hint";
        var hintText = hint.GetComponent<Text>();
        DisableLocalizedOverrides(hint);
        hintText.ParseRichText.Value = false;
        hintText.Content.Value = L(english, japanese);
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

    private void SetText(Text text, string english, string japanese)
    {
        if (text == null) return;
        text.ParseRichText.Value = false;
        bool japaneseSelected = ui.Locale("advanced").FirstOrDefault()?.State.Value == true;
        text.Content.Value = japaneseSelected ? japanese : english;
    }

    private string L(string english, string japanese) =>
        ui.Locale("advanced").FirstOrDefault()?.State.Value == true ? japanese : english;

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
