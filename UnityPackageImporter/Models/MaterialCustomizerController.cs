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
internal sealed class MaterialCustomizerController : IDisposable
{
    private readonly StationTemplate ui;
    private readonly Func<PreparedImportItem> getCurrent;
    private readonly Func<UnityProjectImporter> getImporter;
    private readonly Func<bool> isAlive;
    private readonly Func<bool> isBusy;
    private readonly Action<string> setStage;
    private readonly Action<bool> setBusy;
    private readonly Action<Func<Task>> schedule;
    private readonly Action rebuildPreview;
    private readonly List<Button> entryButtons = new();
    private readonly Slot mainRoot;
    private readonly Slot mainList;
    private readonly Slot mainRowTemplate;
    private readonly BooleanValueDriver<bool> mainVisible;
    private readonly Slot materialRoot;
    private readonly Slot materialList;
    private readonly Slot materialRowTemplate;
    private readonly BooleanValueDriver<bool> materialVisible;
    private readonly Slot folderPickerRoot;
    private readonly Slot folderList;
    private readonly Slot folderChoiceRootTemplate;
    private readonly Slot folderChoiceTemplate;
    private readonly Button folderNavUp;
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
    private readonly Dictionary<(PreparedImportItem Item, object Renderer, int Index), IAssetProvider<Material>> originals = new();

    private Slot mainSearchField;
    private Slot materialSearchField;
    private Text mainSearchText;
    private Text materialSearchText;
    private Slot mainDismissButton;
    private Text mainTitle;
    private Text materialTitle;
    private string mainQuery = "";
    private string materialQuery = "";
    private string? pendingMaterialQuery;
    private string currentFolder = "";
    private string browsedFolder = "";
    private string lastBrowsedFolder = "";
    private int lastBrowseDepthIndex;
    private MaterialSlotBinding selectedSlot;
    private readonly HashSet<string> expandedRendererNames = new(StringComparer.OrdinalIgnoreCase);
    private PreparedImportItem displayedItem;
    private IReadOnlyList<MaterialRendererBinding> renderers = Array.Empty<MaterialRendererBinding>();
    private PackageMaterialCatalog materialCatalog;
    private CancellationTokenSource materialRowsCts;
    private CancellationTokenSource? meshSpheresCts;
    private readonly ThumbnailTaskCache thumbnailCache = new();

    private sealed class FittedTextBinding
    {
        private readonly Text? text;

        public FittedTextBinding(Slot? root)
        {
            if (root == null || root.IsDestroyed) return;
            text = root.GetComponent<Text>() ?? root.GetComponentInChildren<Text>();
        }

        public void SetText(string value)
        {
            if (text == null || text.IsDestroyed) return;
            text.ParseRichText.Value = true;
            text.Content.Value = value;
            text.InteractionTarget.Value = false;
        }

        public static void Set(Slot? root, string value)
        {
            if (root == null || root.IsDestroyed) return;
            var text = root.GetComponent<Text>() ?? root.GetComponentInChildren<Text>();
            if (text == null || text.IsDestroyed) return;
            text.ParseRichText.Value = true;
            text.Content.Value = value;
            text.InteractionTarget.Value = false;
        }
    }

    private sealed class NativeMeshCardHolder
    {
        public MaterialRendererBinding Renderer;
        public Slot CardSlot;
        public Slot Header;
        public ValueMultiDriver<bool> MultiDriver;
        public Slot SlotsContainer;
        public Slot SlotTemplate;
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
    private PreparedImportItem cachedMeshCardsItem;

    internal MaterialCustomizerController(
        StationTemplate ui,
        Func<PreparedImportItem> getCurrent,
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

        Slot customizerPreview = Descendants(ui.Root).FirstOrDefault(slot => slot.Name == "Material Customizer Preview")
            ?? throw new InvalidOperationException("Material Customizer Preview is missing.");

        mainRoot = customizerPreview.Children.FirstOrDefault(slot => slot.Name == "Customize Materials")
            ?? throw new InvalidOperationException("Material Customizer Preview is missing Customize Materials.");
        materialRoot = customizerPreview.Children.FirstOrDefault(slot => slot.Name == "Choose Material")
            ?? throw new InvalidOperationException("Material Customizer Preview is missing Choose Material.");
        folderPickerRoot = customizerPreview.Children.FirstOrDefault(slot => slot.Name == "Folder Picker Popover");

        mainVisible = ui.Get<BooleanValueDriver<bool>>("customizerMainVisible");
        mainVisible.State.Value = false;

        materialVisible = ui.Get<BooleanValueDriver<bool>>("customizerMaterialVisible");
        materialVisible.State.Value = false;

        folderPickerVisible = ui.Get<BooleanValueDriver<bool>>("customizerFolderPickerVisible");
        folderPickerVisible.State.Value = false;

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

        foreach (Slot child in mainList.Children.ToArray())
        {
            if (child != mainRowTemplate) child.Destroy();
        }
        foreach (Slot child in materialList.Children.ToArray())
        {
            if (child != materialRowTemplate) child.Destroy();
        }

        mainRowTemplate.ActiveSelf = false;
        materialRowTemplate.ActiveSelf = false;

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

        mainSearchText = SearchText(mainSearchField);
        if (mainSearchText != null)
        {
            ((IChangeable)mainSearchText.Content).Changed += OnMainSearchChanged;
        }

        materialSearchText = SearchText(materialSearchField);
        if (materialSearchText != null)
        {
            ((IChangeable)materialSearchText.Content).Changed += OnMaterialSearchChanged;
        }

        meshSearchClear = Descendants(mainRoot).FirstOrDefault(slot => slot.Name == "mesh-search-clear")?.GetComponent<Button>();
        if (meshSearchClear != null)
        {
            NativeButtonEvents.Pressed(meshSearchClear, (_, _) =>
            {
                if (mainSearchText != null) mainSearchText.Content.Value = "";
            });
        }

        materialSearchClear = Descendants(materialRoot).FirstOrDefault(slot => slot.Name == "material-search-clear")?.GetComponent<Button>();
        if (materialSearchClear != null)
        {
            NativeButtonEvents.Pressed(materialSearchClear, (_, _) =>
            {
                if (materialSearchText != null) materialSearchText.Content.Value = "";
            });
        }

        var browseDriver = customizerPreview.GetComponent<ValueMultiDriver<int>>();
        if (browseDriver != null)
        {
            folderBrowseIndex = browseDriver.Value;
            folderBrowseIndex.Value = 0;
        }

        foldersOpenButton = Descendants(materialRoot).FirstOrDefault(slot => slot.Name == "folders-open")?.GetComponent<Button>();
        if (foldersOpenButton != null)
        {
            foldersOpenButton.RequireLockInToPress.Value = false;
            foldersOpenButton.PassThroughVerticalMovement.Value = true;
            foldersOpenButton.PassThroughHorizontalMovement.Value = true;
            NativeButtonEvents.Pressed(foldersOpenButton, (_, _) =>
            {
                bool next = !folderPickerVisible.State.Value;
                folderPickerVisible.State.Value = next;
                if (next)
                {
                    browsedFolder = currentFolder;
                    RefreshFolderRows(animateTransition: false);
                }
            });
        }

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
                        rootBtn.RequireLockInToPress.Value = true;
                        rootBtn.PassThroughVerticalMovement.Value = true;
                        rootBtn.PassThroughHorizontalMovement.Value = true;
                        NativeButtonEvents.Pressed(rootBtn, (_, _) =>
                        {
                            if (!isAlive() || isBusy()) return;
                            currentFolder = browsedFolder;
                            folderPickerVisible.State.Value = false;
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
                folderNavUp.RequireLockInToPress.Value = false;
                folderNavUp.PassThroughVerticalMovement.Value = true;
                folderNavUp.PassThroughHorizontalMovement.Value = true;
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
                folderRootButton.RequireLockInToPress.Value = true;
                folderRootButton.PassThroughVerticalMovement.Value = true;
                folderRootButton.PassThroughHorizontalMovement.Value = true;
                NativeButtonEvents.Pressed(folderRootButton, (_, _) => schedule(() =>
                {
                    currentFolder = "";
                    browsedFolder = "";
                    UpdateBreadcrumb();
                    if (folderPickerVisible.State.Value)
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

        ApplyStaticLocalizedLabels();
    }

    private void ApplyStaticLocalizedLabels()
    {
        if (foldersOpenButton?.Slot != null && !foldersOpenButton.Slot.IsDestroyed)
        {
            Slot? labelSlot = FindDescendant(foldersOpenButton.Slot, "folders-open-label");
            if (labelSlot != null) FittedTextBinding.Set(labelSlot, L("folders"));
        }
        if (folderRootButton?.Slot != null && !folderRootButton.Slot.IsDestroyed)
        {
            FittedTextBinding.Set(folderRootButton.Slot, L("allFolders"));
        }
        if (folderNavUp?.Slot != null && !folderNavUp.Slot.IsDestroyed)
        {
            Slot? upLabelSlot = FindDescendant(folderNavUp.Slot, "folder-nav-up-label");
            if (upLabelSlot != null) FittedTextBinding.Set(upLabelSlot, L("up"));
        }
        if (mainDismissButton != null && !mainDismissButton.IsDestroyed)
        {
            Slot? closeLabelSlot = FindDescendant(mainDismissButton, "mesh-panel-close-label");
            if (closeLabelSlot != null) FittedTextBinding.Set(closeLabelSlot, L("closeBold"));
        }
        Slot? meshPlaceholder = Descendants(mainRoot).FirstOrDefault(slot => slot.Name == "mesh-search-placeholder");
        if (meshPlaceholder != null && !meshPlaceholder.IsDestroyed)
        {
            FittedTextBinding.Set(meshPlaceholder, L("searchMeshes"));
        }
        Slot? matPlaceholder = Descendants(materialRoot).FirstOrDefault(slot => slot.Name == "material-search-placeholder");
        if (matPlaceholder != null && !matPlaceholder.IsDestroyed)
        {
            FittedTextBinding.Set(matPlaceholder, L("searchMaterials"));
        }
        if (mainRowTemplate != null && !mainRowTemplate.IsDestroyed)
        {
            Slot? changeLabelSlot = FindDescendant(mainRowTemplate, "slot-0-0-change-label");
            if (changeLabelSlot != null) FittedTextBinding.Set(changeLabelSlot, L("changeBold"));
        }
    }

    internal void RefreshLocale()
    {
        if (!isAlive()) return;
        ApplyStaticLocalizedLabels();
        SetText(mainTitle, "customizeMaterials");
        SetText(materialTitle, "chooseMaterial");
        foreach (var holder in nativeCards)
        {
            if (holder.CardSlot == null || holder.CardSlot.IsDestroyed) continue;
            Slot? countSlot = FindDescendant(holder.CardSlot, "mesh-0-slot-count");
            if (countSlot != null) FittedTextBinding.Set(countSlot, L("slotsCount", holder.Renderer.Slots.Count));
            if (holder.IsPopulated && holder.SlotsContainer != null && !holder.SlotsContainer.IsDestroyed)
            {
                foreach (Slot slotRow in holder.SlotsContainer.Children)
                {
                    if (slotRow == holder.SlotTemplate || slotRow.IsDestroyed) continue;
                    Slot? changeLabelSlot = FindDescendant(slotRow, "slot-0-0-change-label");
                    if (changeLabelSlot != null) FittedTextBinding.Set(changeLabelSlot, L("changeBold"));
                }
                foreach (var pair in holder.SlotRows)
                {
                    var slot = pair.Key;
                    var rowInfo = pair.Value;
                    rowInfo.NameLabel.SetText(MaterialName(slot.ReadCurrent(), getImporter(), materialCatalog));
                }
                int idx = 0;
                foreach (Slot slotRow in holder.SlotsContainer.Children)
                {
                    if (slotRow == holder.SlotTemplate || slotRow.IsDestroyed) continue;
                    if (idx < holder.Renderer.Slots.Count)
                    {
                        Slot? slotLabel = FindDescendant(slotRow, "slot-0-0-slot-label");
                        if (slotLabel != null) FittedTextBinding.Set(slotLabel, L("slotPrefix", holder.Renderer.Slots[idx].Index + 1));
                        idx++;
                    }
                }
            }
        }
        if (mainVisible != null && !mainVisible.IsDestroyed && mainVisible.State.Value)
        {
            ApplyNativeMeshFilter(mainQuery);
        }
        if (materialVisible != null && !materialVisible.IsDestroyed && materialVisible.State.Value && selectedSlot != null)
        {
            if (pickerContextText != null && !pickerContextText.IsDestroyed)
                pickerContextText.Content.Value = $"{selectedSlot.RendererName} · {L("slotPrefix", selectedSlot.Index + 1)}";
            if (pickerContextMaterialText != null && !pickerContextMaterialText.IsDestroyed)
                pickerContextMaterialText.Content.Value = MaterialName(selectedSlot.ReadCurrent(), getImporter(), materialCatalog);
            RefreshMaterialRows(materialQuery);
        }
        if (folderPickerVisible.State.Value)
        {
            RefreshFolderRows(animateTransition: false);
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
        materialRowsCts?.Cancel();
        meshSpheresCts?.Cancel();
        if (folderPickerVisible != null && !folderPickerVisible.IsDestroyed) folderPickerVisible.State.Value = false;
        if (materialVisible != null && !materialVisible.IsDestroyed) materialVisible.State.Value = false;
        if (mainVisible != null && !mainVisible.IsDestroyed) mainVisible.State.Value = false;
    }

    internal void ClearThumbnailCaches()
    {
        thumbnailCache.Clear();
    }

    private void ClosePickerOnly()
    {
        materialRowsCts?.Cancel();
        if (folderPickerVisible != null && !folderPickerVisible.IsDestroyed) folderPickerVisible.State.Value = false;
        if (materialVisible != null && !materialVisible.IsDestroyed) materialVisible.State.Value = false;
        selectedSlot = null;
    }

    private async Task OpenMainAsync()
    {
        if (!isAlive() || isBusy()) return;
        setStage("Open material customizer");
        PreparedImportItem item = getCurrent();
        UnityProjectImporter importer = getImporter();
        if (item == null || item.CurrentStructureRootSlot == null || item.CurrentStructureRootSlot.IsDestroyed || importer == null)
            return;

        if (displayedItem == item && cachedMeshCardsItem == item && nativeCards.Count > 0)
        {
            ClosePickerOnly();
            mainVisible.State.Value = true;
            ApplyNativeMeshFilter(mainQuery);
            return;
        }

        PackageMaterialCatalog catalog = await importer.GetPackageMaterialCatalogAsync();
        await default(ToWorld);
        if (!isAlive() || isBusy() || item != getCurrent()) return;

        displayedItem = item;
        materialCatalog = catalog;
        renderers = BuildRendererBindings(item, importer, catalog);
        expandedRendererNames.Clear();
        mainQuery = "";

        if (mainSearchText != null) mainSearchText.Content.Value = "";
        SetText(mainTitle, "customizeMaterials");
        if (meshCountText != null)
        {
            meshCountText.Content.Value = L("meshesCount", renderers.Count);
        }
        ClosePickerOnly();
        mainVisible.State.Value = true;
        RefreshMainRows("");
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
        PreparedImportItem item,
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

        if (cachedMeshCardsItem != displayedItem || nativeCards.Count == 0)
        {
            RebuildNativeMeshCards();
        }
        ApplyNativeMeshFilter(mainQuery);
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
        FittedTextBinding.Set(titleSlot, renderer.Name);

        Slot countSlot = FindDescendant(card, "mesh-0-slot-count");
        FittedTextBinding.Set(countSlot, L("slotsCount", renderer.Slots.Count));

        Slot header = FindDescendant(card, "mesh-0-header");

        var multiDriver = header?.GetComponent<ValueMultiDriver<bool>>()
            ?? card.GetComponentsInChildren<ValueMultiDriver<bool>>().FirstOrDefault();

        var heightDriver = header?.GetComponents<BooleanValueDriver<float>>().FirstOrDefault()
            ?? card.GetComponentsInChildren<BooleanValueDriver<float>>().FirstOrDefault();

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

        var holder = new NativeMeshCardHolder
        {
            Renderer = renderer,
            CardSlot = card,
            Header = header,
            MultiDriver = multiDriver,
            SlotsContainer = slotsContainer,
            SlotTemplate = slotTemplate,
            IsPopulated = false
        };

        bool isExpanded = expandedRendererNames.Contains(renderer.Name);
        SetAccordionState(holder, isExpanded);

        var headerBtn = header?.GetComponent<Button>();
        if (headerBtn != null)
        {
            headerBtn.RequireLockInToPress.Value = false;
            headerBtn.PassThroughVerticalMovement.Value = true;
            headerBtn.PassThroughHorizontalMovement.Value = true;

            NativeButtonEvents.Pressed(headerBtn, (_, _) =>
            {
                bool willExpand = !expandedRendererNames.Contains(holder.Renderer.Name);
                SetAccordionState(holder, willExpand);
            });
        }

        return holder;
    }

    private void SetAccordionState(NativeMeshCardHolder holder, bool expand)
    {
        if (expand)
        {
            // Enforce single-card mutual exclusion so only one mesh card is expanded at a time.
            foreach (var other in nativeCards)
            {
                if (!ReferenceEquals(other, holder) && expandedRendererNames.Contains(other.Renderer.Name))
                {
                    SetAccordionState(other, false);
                }
            }
            EnsureSlotsPopulated(holder);
            expandedRendererNames.Add(holder.Renderer.Name);
        }
        else
        {
            expandedRendererNames.Remove(holder.Renderer.Name);
        }

        if (holder.MultiDriver != null)
        {
            holder.MultiDriver.Value.Value = expand;
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
            FittedTextBinding.Set(slotLabel, L("slotPrefix", slot.Index + 1));

            string currentName = MaterialName(slot.ReadCurrent(), getImporter(), materialCatalog);
            Slot nameSlot = FindDescendant(slotRow, "slot-0-0-name");
            var nameLabel = new FittedTextBinding(nameSlot);
            nameLabel.SetText(currentName);

            Slot? sphereSlot = FindDescendant(slotRow, "slot-0-0-sphere");
            UpdateSlotSphere(slotRow, sphereSlot, slot, sphereToken);

            Slot changeSlot = FindDescendant(slotRow, "slot-0-0-change");
            Slot? changeLabelSlot = FindDescendant(slotRow, "slot-0-0-change-label");
            if (changeLabelSlot != null) FittedTextBinding.Set(changeLabelSlot, L("changeBold"));
            var changeBtn = changeSlot?.GetComponent<Button>();
            if (changeBtn != null)
            {
                changeBtn.RequireLockInToPress.Value = false;
                changeBtn.PassThroughVerticalMovement.Value = true;
                changeBtn.PassThroughHorizontalMovement.Value = true;
                NativeButtonEvents.Pressed(changeBtn, (_, _) => schedule(() => OpenMaterialPickerAsync(slot)));
            }

            Slot restoreSlot = FindDescendant(slotRow, "slot-0-0-restore");
            var restoreBtn = restoreSlot?.GetComponent<Button>();
            bool isModified = IsSlotModified(slot);
            if (restoreBtn != null)
            {
                restoreBtn.RequireLockInToPress.Value = false;
                restoreBtn.PassThroughVerticalMovement.Value = true;
                restoreBtn.PassThroughHorizontalMovement.Value = true;
                restoreBtn.Slot.ActiveSelf = isModified;
                NativeButtonEvents.Pressed(restoreBtn, (_, _) => schedule(() => RestoreOriginalAsync(slot)));
            }

            holder.SlotRows[slot] = (nameLabel, sphereSlot, restoreBtn);
        }
    }

    private bool IsSlotModified(MaterialSlotBinding slot)
    {
        if (slot == null) return false;
        var current = slot.ReadCurrent();
        if (ReferenceEquals(current, slot.Original)) return false;
        if (current == null && slot.Original == null) return false;
        if (current == null || slot.Original == null) return true;

        var importer = getImporter();
        string currentGuid = slot.CurrentGuid(importer);
        if (!string.IsNullOrEmpty(currentGuid))
        {
            if (importer != null && importer.TryGetMaterialGuid(slot.Original, out string originalGuid) &&
                !string.IsNullOrEmpty(originalGuid))
            {
                return !currentGuid.Equals(originalGuid, StringComparison.OrdinalIgnoreCase);
            }
        }

        return true;
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

                bool isModified = IsSlotModified(slot);
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

        folderPickerVisible.State.Value = false;

        if (materialSearchText != null) materialSearchText.Content.Value = "";
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
        await Task.CompletedTask;
    }

    private static colorX ReadMaterialColor(IAssetProvider<Material>? material)
    {
        if (material == null) return new colorX(0.5f, 0.5f, 0.5f, 1f);
        try
        {
            var type = material.GetType();
            foreach (var name in new[] { "AlbedoColor", "TintColor", "Color", "BaseColor", "MainColor" })
            {
                var prop = type.GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (prop != null)
                {
                    var val = prop.GetValue(material);
                    if (val is IField<colorX> field) return field.Value;
                    if (val is colorX cx) return cx;
                }
                var fieldInfo = type.GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (fieldInfo != null)
                {
                    var val = fieldInfo.GetValue(material);
                    if (val is IField<colorX> field) return field.Value;
                    if (val is colorX cx) return cx;
                }
            }
        }
        catch
        {
            // Fall back to neutral tint
        }
        return new colorX(0.5f, 0.5f, 0.5f, 1f);
    }

    private void RefreshMaterialRows(string query)
    {
        if (!isAlive() || selectedSlot == null || !materialVisible.State.Value) return;
        materialRowsCts?.Cancel();
        materialRowsCts?.Dispose();
        materialRowsCts = new CancellationTokenSource();
        var cancellationToken = materialRowsCts.Token;

        materialQuery = query ?? "";

        IReadOnlyList<PackageMaterialChoice> catalogChoices;
        if (!string.IsNullOrWhiteSpace(materialQuery))
            catalogChoices = materialCatalog != null ? materialCatalog.Search(materialQuery) : Array.Empty<PackageMaterialChoice>();
        else if (materialCatalog != null)
        {
            var directChoices = materialCatalog.Search(folder: currentFolder)
                .Where(choice => choice.Folder.Equals(currentFolder, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (directChoices.Length > 0)
            {
                catalogChoices = directChoices;
            }
            else
            {
                var allInFolder = materialCatalog.Search(folder: currentFolder);
                catalogChoices = allInFolder.Count > 16 ? allInFolder.Take(16).ToArray() : allInFolder;
            }
        }
        else
        {
            catalogChoices = Array.Empty<PackageMaterialChoice>();
        }

        var currentMaterial = selectedSlot.ReadCurrent();
        string currentName = MaterialName(currentMaterial, getImporter(), materialCatalog);
        string currentGuid = ResolveSlotMaterialGuid(selectedSlot);
        colorX currentColorX = ReadMaterialColor(currentMaterial);
        (float r, float g, float b, float a) currentColor = (currentColorX.r, currentColorX.g, currentColorX.b, currentColorX.a);

        var choices = PackageMaterialCatalog.OrderPickerChoices(
            currentGuid,
            currentName,
            currentColor,
            catalogChoices,
            materialCatalog?.Choices);

        if (materialCountText != null)
        {
            materialCountText.Content.Value = L("materialsCount", choices.Count);
        }

        for (int i = 0; i < choices.Count; i++)
        {
            bool isFirst = i == 0;
            BindNativeMaterialCard(i, choices[i], currentGuid, cancellationToken, isExitCard: isFirst);
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

    private PooledMaterialCard GetOrCreatePooledMaterialCard(int index)
    {
        while (pooledMaterialCards.Count <= index)
        {
            Slot card = materialRowTemplate.Duplicate(materialList, false);

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

    private void BindNativeMaterialCard(
        int index,
        PackageMaterialChoice choice,
        string currentGuid,
        CancellationToken cancellationToken,
        bool isExitCard = false)
    {
        PooledMaterialCard pooled = GetOrCreatePooledMaterialCard(index);
        Slot card = pooled.Card;
        card.Name = $"Material Card · {choice.Name}";
        card.ActiveSelf = true;

        string label = string.IsNullOrWhiteSpace(choice.Folder) || choice.Folder.Equals(currentFolder, StringComparison.OrdinalIgnoreCase)
            ? choice.Name
            : $"{choice.Name}  ·  {choice.Folder}";

        pooled.NameLabel?.SetText(label);

        bool isCurrent = isExitCard || (!string.IsNullOrEmpty(currentGuid) && choice.Guid.Equals(currentGuid, StringComparison.OrdinalIgnoreCase));
        if (pooled.OutlineSlot != null)
        {
            pooled.OutlineSlot.ActiveSelf = isCurrent;
        }

        if (pooled.SphereImage != null && pooled.SphereSlot != null)
        {
            pooled.SphereImage.Sprite.Target = null;
            (float r, float g, float b, float a) baseCol = choice.BaseColor != default
                ? choice.BaseColor
                : (string.IsNullOrEmpty(choice.Guid) ? (r: 0.5f, g: 0.5f, b: 0.5f, a: 1f) : MaterialThumbnailGenerator.GetMaterialBaseColor(choice.Guid, getImporter()));
            pooled.SphereImage.Tint.Value = new colorX(baseCol.r, baseCol.g, baseCol.b, baseCol.a);

            if (!string.IsNullOrEmpty(choice.Guid))
            {
                ApplyMaterialThumbnail(card, pooled.SphereSlot, pooled.SphereImage, choice.Guid, cancellationToken);
            }
        }

        if (isExitCard)
        {
            pooled.CurrentAction = () =>
            {
                ClosePickerOnly();
                return Task.CompletedTask;
            };
        }
        else
        {
            var capturedChoice = choice;
            pooled.CurrentAction = () => ApplyChoiceAsync(capturedChoice);
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

                Uri? url = await thumbnailCache.GetOrLoadThumbnailAsync(materialGuid, async guid =>
                {
                    var currentImporter = getImporter();
                    if (currentImporter?.world == null || currentImporter.world.IsDestroyed) return null;

                    string? thumbPath = await MaterialThumbnailGenerator.GetOrCreateThumbnailAsync(guid, currentImporter, CancellationToken.None);
                    if (string.IsNullOrEmpty(thumbPath)) return null;

                    if (currentImporter.world == null || currentImporter.world.IsDestroyed) return null;
                    return await currentImporter.world.Engine.LocalDB.ImportLocalAssetAsync(thumbPath, LocalDB.ImportLocation.Copy);
                }, cancellationToken);

                if (url == null || cancellationToken.IsCancellationRequested) return;

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
            catch (OperationCanceledException)
            {
                // Thumbnail loading was cancelled; safe to ignore.
            }
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

        if (folderPickerTitle != null)
        {
            string titleText = !canGoUp
                ? "Assets"
                : System.IO.Path.GetFileName(normalizedBrowsed);
            folderPickerTitle.ParseRichText.Value = true;
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

        if (folderBrowseIndex != null && !folderBrowseIndex.IsRemoved)
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
        if (breadcrumbPath != null)
        {
            breadcrumbPath.Content.Value = string.IsNullOrEmpty(currentFolder) ? "" : "›  " + currentFolder;
        }
    }

    private static string NormalizeFolder(string folder)
    {
        return (folder ?? "").Replace('\\', '/').Trim('/').Replace("//", "/");
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
            UpdateSlotInNativeCards(targetSlot);
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
            UpdateSlotInNativeCards(slot);
        }
        finally
        {
            await default(ToWorld);
            if (isAlive()) setBusy(false);
        }
    }

    private void OnMainSearchChanged(IChangeable obj)
    {
        if (!isAlive() || mainSearchText == null) return;
        string query = mainSearchText.Content.Value ?? "";
        if (string.Equals(query, mainQuery, StringComparison.Ordinal)) return;
        RefreshMainRows(query);
    }

    private void OnMaterialSearchChanged(IChangeable obj)
    {
        if (!isAlive() || materialSearchText == null) return;
        string query = materialSearchText.Content.Value ?? "";
        if (string.Equals(query, materialQuery, StringComparison.Ordinal)) return;

        if (isBusy())
        {
            pendingMaterialQuery = query;
            return;
        }

        pendingMaterialQuery = null;
        RefreshMaterialRows(query);
    }

    internal void OnBusyCleared()
    {
        if (pendingMaterialQuery != null && isAlive() && materialVisible != null && materialVisible.State.Value && selectedSlot != null)
        {
            string query = pendingMaterialQuery;
            pendingMaterialQuery = null;
            RefreshMaterialRows(query);
        }
    }

    public void Dispose()
    {
        if (mainSearchText != null)
        {
            ((IChangeable)mainSearchText.Content).Changed -= OnMainSearchChanged;
        }
        if (materialSearchText != null)
        {
            ((IChangeable)materialSearchText.Content).Changed -= OnMaterialSearchChanged;
        }
        materialRowsCts?.Cancel();
        materialRowsCts?.Dispose();
        materialRowsCts = null;
        meshSpheresCts?.Cancel();
        meshSpheresCts?.Dispose();
        meshSpheresCts = null;
        ClearThumbnailCaches();
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
                text.Slot.Name != "material-search-placeholder")
            {
                return text;
            }
        }

        throw new InvalidOperationException("The native search field is not linked to a UIX.Text component.");
    }

    private string CurrentLocale => LocalizationManager.DetectLocaleCode();

    private void SetText(Text text, string key, params object[] args)
    {
        if (text == null) return;
        text.ParseRichText.Value = true;
        text.Content.Value = LocalizationManager.GetStringForLocale(CurrentLocale, key, args);
    }

    private string L(string key, params object[] args) =>
        LocalizationManager.GetStringForLocale(CurrentLocale, key, args);

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
