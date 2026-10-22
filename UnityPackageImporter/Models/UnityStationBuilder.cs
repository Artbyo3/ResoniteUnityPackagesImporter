using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.CommonAvatar;
using FrooxEngine.UIX;
using UnityPackageImporter.UI;

namespace UnityPackageImporter.Models;

internal sealed class UnityStationHandle
{
    private readonly StationTemplate ui;
    private readonly ImportSession session;
    private readonly Dictionary<UnityPrefabImportTask, float> progress = new();
    private readonly HashSet<PreparedImportItem> created = new();
    private readonly HashSet<PreparedImportItem> failedCreation = new();
    private readonly Dictionary<PreparedImportItem, string> names = new();
    private readonly Dictionary<PreparedImportItem, string> presets = new();
    private readonly SelectableImportItems<PreparedImportItem> items = new();
    private Slot preview, rowTemplate;
    private BooleanValueDriver<float3> previewScaleDriver;
    private Slider<float> previewTurntableSlider;
    private BipedRig selectedAvatar;
    private UnityProjectImporter importer;
    private MaterialDependencySession dependencies;
    private readonly ReusableDecisionPrompt decisionPrompt;
    private MaterialCustomizerController materialCustomizer;
    private int lifecycle;
    private bool busy, ready;
    private Task closeTask;
    private readonly object closeGate = new();
    private string operationStage = "Package import";
    private string operationStageKey;
    public Slot RootSlot => ui.Root;
    public bool IsAlive => !ui.Host.IsDestroyed && !ui.Root.IsDestroyed && !session.Token.IsCancellationRequested;
    private PreparedImportItem Current => items.Current;
    private bool IsOutfit => Current?.Manifest?.IsModularAvatarAttachment == true;
    private bool IsAvatar => !IsOutfit && Current != null && (Current.Manifest?.IsAvatarPrefab == true ||
        Current.CurrentStructureRootSlot.GetComponentInChildren<BipedRig>() != null);

    internal UnityStationHandle(StationTemplate template, ImportSession importSession)
    {
        ui = template;
        session = importSession;
        session.BindUi(ui.Host, ui.Root);
        ui.Host.SetParent(session.Root, true);
        decisionPrompt = new ReusableDecisionPrompt(
            (driverKey, messageKey) => ui.SetLocalizedKey(driverKey, messageKey),
            (visible, waiting) =>
            {
                Get<BooleanValueDriver<bool>>("companionVisible").State.Value = visible;
                Get<ValueField<bool>>("companionWaiting").Value.Value = waiting;
            });
        Bind("close", CloseAsync, allowWhileBusy: true);
        Bind("previous", () => SelectAsync(items.SelectedIndex - 1));
        Bind("next", () => SelectAsync(items.SelectedIndex + 1));
        Bind("create", CreateAvatarAsync);
        Bind("install", InstallOutfitAsync);
        Bind("targetChange", ChooseAvatarAsync);
        Bind("targetField", ChooseAvatarAsync);
        Bind("companionImport", () => decisionPrompt.InvokePrimaryAsync());
        Bind("companionBack", () => decisionPrompt.InvokeDismissAsync());
        Bind("companionPlaceholder", () => decisionPrompt.InvokeSecondaryAsync());
        Bind("shaderAutomatic", () => ChangeShaderAsync("Automatic"));
        Bind("shaderToon", () => ChangeShaderAsync("XiexeToon"));
        Get<SmoothValue<float>>("arc").Speed.Value = 10f;
        Get<SmoothValue<float>>("arc").WriteBack.Value = false;
        Get<BooleanValueDriver<float2>>("advanced").State.Value = false;
        Get<BooleanValueDriver<bool>>("mode").State.Value = false;
        Get<BooleanValueDriver<bool>>("avatarPicker").State.Value = false;
        Slot previewSlot = Get<Slot>("preview");
        previewScaleDriver = previewSlot.GetComponent<BooleanValueDriver<float3>>();
        if (previewScaleDriver != null) previewScaleDriver.State.Value = false;
        Slot dragSurface = RootSlot.FindChild("Avatar Preview Drag Surface");
        if (dragSurface != null)
        {
            previewTurntableSlider = dragSurface.GetComponent<Slider<float>>();
        }
        ResetPreviewTurntable();
        Get<ValueField<bool>>("shaderVisible").Value.Value = false;
        Get<BooleanValueDriver<float3>>("tabletScale").State.Value = true;
        Get<Slot>("nameCard").ActiveSelf = false;
        SetImportingPanel(true);
        SetCompanionState(false, false);
        // The source importer has no Shape Changer handler yet. Expose its actual availability.
        ui.SetLocalizedKey("bodyShapeDescription", "bodyShapeDescriptionUnsupported");
        SetActions(false);
        rowTemplate = Get<Button>("avatarRow").Slot;
        rowTemplate.ActiveSelf = false;
        materialCustomizer = new MaterialCustomizerController(
            ui,
            () => Current,
            () => importer,
            () => IsAlive,
            () => busy,
            value => { operationStage = value; operationStageKey = null; },
            SetMaterialBusy,
            ScheduleMaterialAction,
            RebuildPreview);
        materialCustomizer.SetEntryPointsEnabled(false);
        ui.LocaleChanged += OnLocaleChanged;
        ResetOutcome();
        _ = ui.Root.World.RootSlot.StartGlobalTask(MonitorLifetimeAsync);
    }

    private void OnLocaleChanged(string locale)
    {
        if (!IsAlive) return;
        session.Reports?.SetLocale(locale);
        if (ready && items.Count == 0 && created.Count > 0)
        {
            string completeTitle = "<b>" + Escape(LocalizationManager.GetStringForLocale(locale, "allProcessed")) + "</b>";
            Get<Text>("avatarTitle").Content.Value = completeTitle;
            Get<Text>("outfitTitle").Content.Value = completeTitle;
        }
        if (Current != null && presets.TryGetValue(Current, out var currentPreset))
        {
            Get<ValueField<string>>("shaderValue").Value.Value = FormatShaderPresetDisplay(currentPreset, locale);
        }
        materialCustomizer?.RefreshLocale();
    }
    private T Get<T>(string key) where T : class, IWorldElement => ui.Get<T>(key);
    private void ResetPreviewTurntable()
    {
        if (previewTurntableSlider != null && !previewTurntableSlider.IsDestroyed)
            previewTurntableSlider.Value.Value = 0.5f;
        Slot previewSlot = Get<Slot>("preview");
        if (previewSlot != null && !previewSlot.IsDestroyed && !previewSlot.Rotation_Field.IsDriven)
            previewSlot.LocalRotation = floatQ.Euler(0f, 180f, 0f);
    }
    private void Bind(string key, Func<Task> action, bool allowWhileBusy = false)
    {
        Button button = Get<Button>(key);
        NativeButtonEvents.Pressed(button, (_, _) =>
        {
            if (!IsAlive || busy && !allowWhileBusy) return;
            RootSlot.World.RootSlot.StartGlobalTask(async () =>
            {
                try
                {
                    if (allowWhileBusy) await action();
                    else using (session.Enter()) await action();
                }
                catch (OperationCanceledException) when (session.Token.IsCancellationRequested)
                {
                    // Action was cancelled by user; safe to ignore.
                }
                catch (Exception ex)
                {
                    await default(ToWorld);
                    UnityPackageImporter.Warn("Station action failed: " + ex);
                    CompleteFailure(ex);
                }
            });
        });
    }
    private void ResetOutcome()
    {
        Get<ValueField<bool>>("success").Value.Value = false;
        Get<ValueField<bool>>("failure").Value.Value = false;
    }
    public void UpdateProgress(float value, string stageKey, string file, int current = 0, int total = 0)
    {
        if (session.Reports != null) session.Reports.Stage = ImportFailureReport.Stage(stageKey);
        if (!IsAlive) return;
        operationStageKey = stageKey;
        operationStage = LocalizationManager.GetEnglish(stageKey);
        Get<Slot>("progressPedestal").ActiveSelf = true;
        Get<Slot>("progressCard").ActiveSelf = true;
        Get<BooleanValueDriver<float3>>("cardScale").State.Value = true;
        Get<BooleanValueDriver<float3>>("cardPosition").State.Value = true;
        Get<SmoothValue<float>>("arc").TargetValue.Value = MathX.Clamp01(value) * 360f;
        ui.SetLocalizedKey("phase", stageKey);
        ui.SetLocalized("loaded", current, total);
        Get<ValueField<int>>("processed").Value.Value = current;
        Get<ValueField<int>>("total").Value.Value = total;
        Get<Text>("progressFile").ParseRichText.Value = false;
        Get<Text>("progressFile").Content.Value = file ?? "";
    }
    internal void RegisterPrefabs(IEnumerable<UnityPrefabImportTask> tasks)
    { foreach (var task in tasks) progress[task] = 0f; }
    internal void ReportPrefab(UnityPrefabImportTask task, float value)
    {
        if (!IsAlive) return;
        progress[task] = Math.Max(progress.GetValueOrDefault(task), MathX.Clamp01(value));
        UpdateProgress(0.3f + 0.7f * progress.Values.Sum() / progress.Count,
            "buildingPrefabs",
            Path.GetFileName(task.ID.Value), progress.Values.Count(p => p >= 1f), progress.Count);
    }
    public void CompleteFailure(Exception error)
    {
        var actionStage = ImportFailureReport.Stage(operationStage);
        ImportReportService.Record(session.Reports, error, error, Current, importer,
            actionStage != ImportReportStage.Unknown ? actionStage : session.Reports?.Stage);
        if (!IsAlive) return;
        lifecycle++;
        materialCustomizer?.CloseAll();
        ResetOutcome();
        string reason = ImportFailureReport.Sanitize(error.Message);
        if (!string.IsNullOrEmpty(operationStageKey))
            ui.SetLocalizedKey("phase", operationStageKey);
        else
            ui.SetLocalizedRaw("phase", operationStage);
        if (Current != null)
        {
            Get<Text>("progressFile").ParseRichText.Value = false;
            Get<Text>("progressFile").Content.Value = names.TryGetValue(Current, out var curName)
                ? curName
                : Path.GetFileName(Current.ID.Value);
        }
        ui.SetLocalizedKey("failure", "failureReason", Escape(reason));
        Get<ValueField<bool>>("failure").Value.Value = true;
        Get<Slot>("progressCard").ActiveSelf = true;
        Get<BooleanValueDriver<float3>>("cardScale").State.Value = true;
        Get<BooleanValueDriver<float3>>("cardPosition").State.Value = true;
        Get<SmoothValue<float>>("arc").TargetValue.Value = 0f;
        Get<Slot>("progressPedestal").ActiveSelf = true;
        // Keep the tablet's Close action accessible when import cannot stage.
        Get<BooleanValueDriver<float3>>("tabletScale").State.Value = true;
        SetImportingPanel(false);
    }
    private void SetImportingPanel(bool visible) =>
        Get<BooleanValueDriver<bool>>("importingPanelVisible").State.Value = visible;
    public async Task CompleteSuccessAsync(bool isPartial = false)
    {
        if (!IsAlive) return;
        int generation = ++lifecycle;
        ResetOutcome();
        Get<Slot>("progressCard").ActiveSelf = true;
        if (isPartial)
        {
            ui.SetLocalizedKey("phase", "partialCompatibility");
        }
        Get<ValueField<bool>>("success").Value.Value = true;
        Get<SmoothValue<float>>("arc").TargetValue.Value = 360f;
        await Task.Delay(3500, session.Token);
        await default(ToWorld);
        if (!IsAlive || generation != lifecycle) return;
        Get<BooleanValueDriver<float3>>("cardScale").State.Value = false;
        Get<BooleanValueDriver<float3>>("cardPosition").State.Value = false;
        Get<SmoothValue<float>>("arc").Speed.Value = 3f;
        Get<SmoothValue<float>>("arc").TargetValue.Value = 0f;
        await Task.Delay(2000, session.Token);
        await default(ToWorld);
        if (IsAlive && generation == lifecycle)
        {
            // The canvas animation does not include the 3D logo. Retire the
            // entire disclosure hierarchy after its closing transition.
            Get<Slot>("progressCard").ActiveSelf = false;
            Get<SmoothValue<float>>("arc").Speed.Value = 10f;
        }
    }
    internal async Task StageAsync(UnityProjectImporter project, List<PreparedImportItem> tasks)
    {
        await default(ToWorld);
        if (!IsAlive) return;
        Get<Slot>("progressCard").ActiveSelf = false;
        importer = project;
        items.Reset(tasks.Where(task => task.CurrentStructureRootSlot != null && !task.CurrentStructureRootSlot.IsDestroyed));
        foreach (var item in items.Entries)
        {
            names[item] = item.DisplayName;
            presets[item] = "Automatic";
        }
        ready = true;
        if (items.Count == 0)
        {
            ShowEmptyPackage(project.PackageNames.FirstOrDefault() ?? "Package", project.ListOfUnityScenes.Count > 0);
            return;
        }
        Get<BooleanValueDriver<float3>>("tabletScale").State.Value = true;
        // Reset already selects the first entry; its saved name must not be
        // replaced by the template's example input on this initial display.
        await SelectAsync(0, saveCurrentName: false);
        if (project.GetMissingMaterialSummary(Current?.CurrentStructureRootSlot).HasMissingAssets)
        {
            dependencies = new MaterialDependencySession(project, this);
            SetCompanionState(true, false);
        }
    }
    internal void ShowEmptyPackage(string name, bool scenes = false)
    {
        ready = true;
        Get<Slot>("progressCard").ActiveSelf = false;
        Get<BooleanValueDriver<float3>>("tabletScale").State.Value = true;
        Get<Slot>("nameCard").ActiveSelf = false;
        Get<Checkbox>("protect").Slot.Parent.ActiveSelf = false;
        Get<Text>("avatarTitle").Content.Value = "<b>" + Escape(name) + "</b>";
        ui.SetLocalizedKey("avatarContext", scenes ? "scenePackage" : "assetsPackage");
        ui.SetLocalizedKey("createAvatar", scenes ? "scenesImported" : "noPrefabsFound");
        SetActions(false);
    }
    private async Task MonitorLifetimeAsync()
    {
        var world = RootSlot.World;
        while (true)
        {
            await Task.Delay(200);
            if (world.RootSlot.IsDestroyed) { _ = session.CancelAsync(); return; }
            await default(ToWorld);
            if (!IsAlive || session.Root.IsDestroyed) break;
        }
        dependencies?.Continue();
        await session.CleanupAsync();
    }
    private void SetActions(bool enabled)
    {
        var result = Current?.ReadResult();
        if (enabled && ready && Current != null && result?.StructureReady == false)
            ImportReportService.Record(session.Reports, Current, item: Current, project: importer, stage: ImportReportStage.Validation);
        bool canCreateAvatar = IsAvatar && result?.CanPerform(ImportOutputAction.CreateAvatar) == true;
        bool canKeepObject = !IsOutfit && result?.CanPerform(ImportOutputAction.KeepObject) == true;
        bool canPerformMainAction = canCreateAvatar || canKeepObject;

        ui.SetLocalizedKey("createAvatar", canCreateAvatar ? "createAvatar" : "keepInWorld");
        ui.SetLocalizedKey("avatarName", canCreateAvatar ? "avatarName" : "objectName");
        Get<Checkbox>("protect").Slot.Parent.ActiveSelf = canCreateAvatar;

        if (result != null)
        {
            var action = IsOutfit ? ImportOutputAction.InstallOutfit : canCreateAvatar ? ImportOutputAction.CreateAvatar : ImportOutputAction.KeepObject;
            ui.SetLocalizedKey("avatarContext", !result.CanPerform(action) ? "resultBlockedContext" :
                (!IsAvatar && !IsOutfit) ? "prefabContext" : "avatarContext", items.SelectedIndex + 1, items.Count);
        }
        Get<Button>("create").Enabled = enabled && Current != null && !created.Contains(Current) && !failedCreation.Contains(Current)
            && canPerformMainAction;
        Get<Button>("install").Enabled = enabled && Current != null && selectedAvatar != null &&
            result.CanPerform(ImportOutputAction.InstallOutfit) &&
            ModularAvatarAttachmentInstaller.IsOwnedAvatar(selectedAvatar, RootSlot.World.LocalUser);
        bool canNavigate = items.CanNavigate;
        foreach (string key in new[] { "previous", "next" })
        {
            Button arrow = Get<Button>(key);
            arrow.Enabled = enabled && canNavigate;
            if (arrow.Slot.Parent != null) arrow.Slot.Parent.ActiveSelf = canNavigate;
        }
        materialCustomizer?.SetEntryPointsEnabled(enabled && Current != null && !created.Contains(Current) && !failedCreation.Contains(Current));
        if (enabled) materialCustomizer?.OnBusyCleared();
    }
    private void SetMaterialBusy(bool value)
    {
        busy = value;
        if (IsAlive) SetActions(!value);
    }
    private void ScheduleMaterialAction(Func<Task> action)
    {
        if (!IsAlive || busy || action == null) return;
        RootSlot.World.RootSlot.StartGlobalTask(async () =>
        {
            try
            {
                using (session.Enter()) await action();
            }
            catch (OperationCanceledException) when (session.Token.IsCancellationRequested)
            {
                // Action was cancelled by user; safe to ignore.
            }
            catch (Exception ex)
            {
                await default(ToWorld);
                UnityPackageImporter.Warn("Material customization failed: " + ex);
                CompleteFailure(ex);
            }
        });
    }
    private async Task SelectAsync(int index, bool saveCurrentName = true)
    {
        await default(ToWorld);
        if (!IsAlive || !ready || busy) return;
        if (items.Count == 0)
        {
            ShowAllItemsCompleted();
            return;
        }
        if (saveCurrentName && Current != null) names[Current] = Get<Text>("name").Content.Value;
        lifecycle++;
        ResetOutcome();
        Get<Slot>("progressPedestal").ActiveSelf = true;
        Get<BooleanValueDriver<float3>>("cardScale").State.Value = false;
        Get<BooleanValueDriver<float3>>("cardPosition").State.Value = false;
        Get<Slot>("progressCard").ActiveSelf = false;
        materialCustomizer?.CloseAll();
        items.Select(index);
        ResetPreviewTurntable();
        Get<BooleanValueDriver<float3>>("tabletScale").State.Value = true;
        if (previewScaleDriver != null && !previewScaleDriver.IsDestroyed)
            previewScaleDriver.State.Value = true;
        Get<BooleanValueDriver<bool>>("mode").State.Value = IsOutfit;
        Get<Slot>("nameCard").ActiveSelf = true;
        SetImportingPanel(false);
        Get<ValueField<bool>>("shaderVisible").Value.Value = false;
        Get<ValueField<string>>("shaderValue").Value.Value = FormatShaderPresetDisplay(presets[Current], CurrentLocale);
        Get<Text>("name").Content.Value = names[Current];
        foreach (string key in new[] { "avatarTitle", "outfitTitle" }) Get<Text>(key).Content.Value = "<b>" + Escape(names[Current]) + "</b>";
        ui.SetLocalizedKey("avatarContext", (!IsAvatar && !IsOutfit) ? "prefabContext" : "avatarContext", items.SelectedIndex + 1, items.Count);
        ui.SetLocalizedKey("outfitContext", "outfitContext", items.SelectedIndex + 1, items.Count);
        ui.SetLocalizedKey("createAvatar", IsAvatar ? "createAvatar" : "keepInWorld");
        ui.SetLocalizedKey("avatarName", IsAvatar ? "avatarName" : "objectName");
        Get<Checkbox>("protect").Slot.Parent.ActiveSelf = IsAvatar;
        RebuildPreview();
        if (dependencies != null && !dependencies.IsWaiting && !dependencies.IsDismissed)
        {
            bool hasMissing = importer.GetMissingMaterialSummary(Current?.CurrentStructureRootSlot).HasMissingAssets;
            SetCompanionState(hasMissing, false);
        }
        else if (dependencies == null && importer != null && importer.GetMissingMaterialSummary(Current?.CurrentStructureRootSlot).HasMissingAssets)
        {
            dependencies = new MaterialDependencySession(importer, this);
            SetCompanionState(true, false);
        }
        SetActions(true);
    }
    private void ShowAllItemsCompleted()
    {
        if (preview != null && !preview.IsDestroyed) preview.Destroy();
        preview = null;
        SetImportingPanel(false);
        Get<Slot>("nameCard").ActiveSelf = false;
        Get<Checkbox>("protect").Slot.Parent.ActiveSelf = false;
        Get<ValueField<bool>>("shaderVisible").Value.Value = false;
        Get<Text>("name").Content.Value = string.Empty;
        string completeTitle = "<b>" + Escape(LocalizationManager.GetStringForLocale(CurrentLocale, "allProcessed")) + "</b>";
        Get<Text>("avatarTitle").Content.Value = completeTitle;
        Get<Text>("outfitTitle").Content.Value = completeTitle;
        ui.SetLocalizedKey("avatarContext", "allProcessed");
        ui.SetLocalizedKey("outfitContext", "allProcessed");
        SetCompanionState(false, false);
        SetActions(false);
    }
    private void RebuildPreview()
    {
        if (preview != null && !preview.IsDestroyed) preview.Destroy();
        if (Current == null || Current.CurrentStructureRootSlot.IsDestroyed || created.Contains(Current) || failedCreation.Contains(Current)) return;
        preview = session.Envelope(Current.CurrentStructureRootSlot).Duplicate(Get<Slot>("preview"), false);
        preview.SetIdentityTransform();
        preview.ActiveSelf = true;
        preview.PersistentSelf = false;
        foreach (var grab in preview.GetComponentsInChildren<Grabbable>()) grab.Enabled = false;
        preview.GetComponentOrAttach<GrabBlock>();
    }
    private Task ChooseAvatarAsync()
    {
        var parent = rowTemplate.Parent;
        foreach (var child in parent.Children.ToArray()) if (child != rowTemplate) child.Destroy();
        var candidates = ModularAvatarAttachmentInstaller.GetAvailableAvatarRigs(Current?.CurrentStructureRootSlot ?? RootSlot)
            .Where(rig => !rig.Slot.IsChildOf(session.Root, true)).ToArray();
        for (int i = 0; i < candidates.Length; i++)
        {
            BipedRig candidate = candidates[i];
            var row = rowTemplate.Duplicate(parent, false);
            row.Name = "Avatar " + i;
            row.ActiveSelf = true;
            var button = row.GetComponent<Button>();
            row.Children.Single(child => child.Name == "Labels").Children.Single(child => child.Name == "Title")
                .GetComponent<Text>().Content.Value = "<b>" + Escape(candidate.Slot.Name) + "</b>";
            var check = row.GetComponentInChildren<ValueEqualityDriver<int>>();
            if (check != null)
            {
                check.Reference.Value = i;
                check.TargetValue.Target.Value = Array.IndexOf(candidates, selectedAvatar);
            }
            NativeButtonEvents.Pressed(button, (_, _) =>
            {
                if (!IsAlive || busy) return;
                if (!ModularAvatarAttachmentInstaller.IsOwnedAvatar(candidate, RootSlot.World.LocalUser)) return;
                selectedAvatar = candidate;
                Get<Text>("targetName").Content.Value = "<b>" + Escape(candidate.Slot.Name) + "</b>";
                Get<BooleanValueDriver<bool>>("avatarPicker").State.Value = false;
                SetActions(true);
            });
        }
        Get<BooleanValueDriver<bool>>("avatarPicker").State.Value = true;
        ui.SetLocalizedKey("chooseAvatar", candidates.Length == 0 ? "noOwnedAvatars" : "chooseAvatar");
        return Task.CompletedTask;
    }
    private async Task CreateAvatarAsync()
    {
        if (Current == null || IsOutfit || created.Contains(Current) || failedCreation.Contains(Current)) return;
        var item = Current;
        bool sourceConsumed = false;
        var result = item.ReadResult();
        bool canCreateAvatar = IsAvatar && result.CanPerform(ImportOutputAction.CreateAvatar);
        bool canKeepObject = result.CanPerform(ImportOutputAction.KeepObject);
        if (!canCreateAvatar && !canKeepObject) return;
        operationStage = canCreateAvatar ? "Create avatar" : "Keep prefab in world";
        busy = true;
        SetActions(false);
        try
        {
            item.ReadResult().Require(canCreateAvatar ? ImportOutputAction.CreateAvatar : ImportOutputAction.KeepObject);
            var source = item.CurrentStructureRootSlot;
            var envelope = session.Envelope(source);
            bool avatar = canCreateAvatar;
            bool protect = avatar && Get<Checkbox>("protect").State.Value;
            if (Get<Checkbox>("expressions").State.Value) await item.BuildExpressionsAsync();
            await default(ToWorld);
            session.Check();
            string name = Get<Text>("name").Content.Value;
            envelope.Name = string.IsNullOrWhiteSpace(name) ? names[item] : name.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
            envelope.GlobalPosition = Get<Slot>("preview").GlobalPosition;
            envelope.GlobalRotation = RootSlot.GlobalRotation * floatQ.Euler(0f, 180f, 0f);
            if (avatar) await NativeAvatarCreation.CreateAsync(item, envelope, protect, session.Token);
            await default(ToWorld);
            session.Check();
            if (Get<Checkbox>("avatarReceipt").State.Value) SaveReceipt(envelope, item);
            session.Commit(item);
            created.Add(item);
            names[item] = Get<Text>("name").Content.Value;
            sourceConsumed = true;
            if (!items.Consume(item))
                UnityPackageImporter.Warn("A committed import item was not present in the station's selectable list.");

            materialCustomizer?.CloseAll();
            Get<BooleanValueDriver<float3>>("tabletScale").State.Value = false;
            if (previewScaleDriver != null && !previewScaleDriver.IsDestroyed)
                previewScaleDriver.State.Value = false;

            if (items.Count > 0)
            {
                await Task.Delay(260, session.Token);
                await default(ToWorld);
            }

            if (preview != null && !preview.IsDestroyed) preview.Destroy();
            preview = null;
        }
        catch
        {
            // Roll back the failed creation preview, then rethrow.
            await default(ToWorld);
            // Native creation can have partially configured a rig. Never release or
            // finalize that same partial object a second time; cleanup still owns it.
            failedCreation.Add(item);
            if (preview != null && !preview.IsDestroyed) preview.Destroy();
            throw;
        }
        finally
        {
            await default(ToWorld);
            busy = false;
            if (IsAlive)
            {
                if (sourceConsumed)
                {
                    if (items.Count > 0)
                    {
                        await SelectAsync(items.SelectedIndex, saveCurrentName: false);
                    }
                    else
                    {
                        _ = RootSlot.World.RootSlot.StartGlobalTask(CloseAsync);
                    }
                }
                else SetActions(true);
            }
        }
    }
    private async Task InstallOutfitAsync()
    {
        if (Current == null || !IsOutfit || selectedAvatar == null) return;
        var item = Current;
        var target = selectedAvatar;
        bool receipt = Get<Checkbox>("outfitReceipt").State.Value;
        bool menu = Get<Checkbox>("outfitMenu").State.Value;
        operationStage = "Install clothing";
        busy = true;
        SetActions(false);
        try
        {
            item.ReadResult().Require(ImportOutputAction.InstallOutfit);
            var (installedResult, error) = await ModularAvatarAttachmentInstaller.TryInstallDirectlyDetailedAsync(
                item, target,
                menu, session.Token, assetFiles: importer.AssetIDDict);
            await default(ToWorld);
            if (installedResult == null)
            {
                string message = string.IsNullOrEmpty(error)
                    ? "The outfit could not be installed safely on the selected avatar."
                    : "The outfit could not be installed safely on the selected avatar: " + error;
                throw new InvalidOperationException(message);
            }
            if (receipt && IsAlive && !target.IsDestroyed) SaveReceipt(target.Slot, item, installedResult);
        }
        finally { await default(ToWorld); busy = false; if (IsAlive) SetActions(true); }
    }
    private async Task ChangeShaderAsync(string preset)
    {
        if (Current == null || created.Contains(Current)) return;
        operationStage = "Change material preset";
        busy = true;
        SetActions(false);
        Get<ValueField<bool>>("shaderVisible").Value.Value = false;
        try
        {
            await importer.ApplyMaterialPresetAsync(Current.CurrentStructureRootSlot, preset);
            await default(ToWorld);
            session.Check();
            presets[Current] = preset;
            Get<ValueField<string>>("shaderValue").Value.Value = FormatShaderPresetDisplay(preset, CurrentLocale);
            RebuildPreview();
        }
        finally { await default(ToWorld); busy = false; if (IsAlive) SetActions(true); }
    }
    internal void SetCompanionState(bool visible, bool waiting, string titleKey = null)
    {
        if (!IsAlive) return;
        if (visible)
        {
            if (dependencies == null && importer != null)
                dependencies = new MaterialDependencySession(importer, this);
            decisionPrompt.Show(
                new DecisionPromptKeys(
                    titleKey ?? "waiting",
                    "companionDescription",
                    "companionMissing",
                    "importCompanion",
                    "placeholderMaterials",
                    "back"),
                () => { dependencies?.BeginWaiting(); return Task.CompletedTask; },
                () => { dependencies?.Continue(); return Task.CompletedTask; },
                () => { dependencies?.Back(); return Task.CompletedTask; });
            decisionPrompt.SetState(true, waiting);
        }
        else decisionPrompt.SetState(false, waiting);
    }
    internal void RefreshMaterials() { if (IsAlive) RebuildPreview(); }
    public Task CloseAsync()
    {
        lock (closeGate)
        {
            return closeTask ??= CloseCoreAsync();
        }
    }

    private async Task CloseCoreAsync()
    {
        lifecycle++;
        try
        {
            materialCustomizer?.CloseAll();
            materialCustomizer?.ClearThumbnailCaches();
        }
        catch (Exception error)
        {
            UnityPackageImporter.Warn($"Failed to close material customizer during station close: {error.Message}");
        }

        try
        {
            dependencies?.Continue();
        }
        catch (Exception error)
        {
            UnityPackageImporter.Warn($"Failed to continue dependencies during station close: {error.Message}");
        }

        try
        {
            if (session.TryEnter(out var lease))
            {
                using (lease)
                {
                    _ = session.CancelAsync();
                    await default(ToWorld);
                    if (preview != null && !preview.IsDestroyed) preview.Destroy();
                    preview = null;

                    if (IsAlive)
                    {
                        var arc = Get<SmoothValue<float>>("arc");
                        arc.TargetValue.Value = 0f;
                        arc.Speed.Value = 3f;
                        Get<BooleanValueDriver<float3>>("cardScale").State.Value = false;
                        Get<BooleanValueDriver<float3>>("cardPosition").State.Value = false;
                        Get<BooleanValueDriver<float3>>("tabletScale").State.Value = false;
                        if (previewScaleDriver != null && !previewScaleDriver.IsDestroyed)
                            previewScaleDriver.State.Value = false;
                    }

                    await Task.Delay(1000).ConfigureAwait(false);
                    await default(ToWorld);
                }
            }
            else
            {
                _ = session.CancelAsync();
                await default(ToWorld);
                if (preview != null && !preview.IsDestroyed) preview.Destroy();
                preview = null;
            }
        }
        catch (Exception error)
        {
            UnityPackageImporter.Warn($"Failed to run station close transition: {error.Message}");
        }
        finally
        {
            try
            {
                await session.CleanupAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                UnityPackageImporter.Warn($"Failed to clean up import session: {error.Message}");
            }

            try
            {
                await default(ToWorld);
                materialCustomizer?.Dispose();
                if (ui.Root != null && !ui.Root.IsDestroyed)
                    ui.Root.Destroy();
                if (ui.Host != null && !ui.Host.IsDestroyed)
                    ui.Host.Destroy();
            }
            catch (Exception error)
            {
                UnityPackageImporter.Warn($"Failed to tear down station UI: {error.Message}");
            }
        }
    }
    private static string FormatShaderPresetDisplay(string preset, string locale)
    {
        if (string.Equals(preset, "XiexeToon", StringComparison.OrdinalIgnoreCase))
            return "Xiexe Toon";
        string automaticStr = LocalizationManager.GetStringForLocale(locale, "automatic");
        return automaticStr.Replace("<b>", "", StringComparison.OrdinalIgnoreCase)
                           .Replace("</b>", "", StringComparison.OrdinalIgnoreCase)
                           .Trim();
    }
    private string BuildReceipt(PreparedImportItem item, string locale = null, ImportResult outputResult = null)
    {
        string loc = locale ?? CurrentLocale;
        var result = outputResult ?? item.ReadResult();
        var lines = new List<string>
        {
            LocalizationManager.GetStringForLocale(loc, "receiptPrefab", Path.GetFileName(item.ID.Value)),
            LocalizationManager.GetStringForLocale(loc, "receiptImportedObjects", item.existingIUnityObjects?.Count ?? 0),
            LocalizationManager.GetStringForLocale(loc, "receiptMeshRenderers", item.CurrentStructureRootSlot.GetComponentsInChildren<SkinnedMeshRenderer>().Count),
            LocalizationManager.GetStringForLocale(loc, "receiptMissingMaterials", result.MissingMaterialAssets),
            LocalizationManager.GetStringForLocale(loc, "receiptModularAvatar", item.Manifest?.ModularAvatarComponents.Count ?? 0)
        };
        if (item.Manifest?.HasVrcFuryComponents == true)
        {
            lines.Add(LocalizationManager.GetStringForLocale(loc, "receiptVrcFury", item.Manifest.VrcFuryComponents.Count));
        }
        lines.Add(LocalizationManager.GetStringForLocale(loc, result.StructureReady ? "resultStructureReady" : "resultStructureBlocked"));
        lines.Add(LocalizationManager.GetStringForLocale(loc, "resultOperations", result.CompletedOperationCount, result.OperationCount));
        foreach (var issue in result.Issues)
            lines.Add(LocalizationManager.GetStringForLocale(loc, "result" + issue));
        if (result.MaterialWarnings.Count > 0)
        {
            lines.Add(LocalizationManager.GetStringForLocale(loc, "receiptMaterialWarningsHeading", result.MaterialWarnings.Count));
            foreach (var warning in result.MaterialWarnings)
                lines.Add("• " + warning);
        }
        lines.Add(LocalizationManager.GetStringForLocale(loc, "receiptUiTemplate", ui.Version));
        lines.Add(LocalizationManager.GetStringForLocale(loc, "receiptShapeChanger"));
        return string.Join("\n", lines);
    }
    private string CurrentLocale => LocalizationManager.DetectLocaleCode();

    private void SaveReceipt(Slot output, PreparedImportItem item, ImportResult outputResult = null) =>
        output.AddSlot("Unity Import Receipt").AttachComponent<ValueField<string>>().Value.Value = BuildReceipt(item, outputResult: outputResult);
    private static string Escape(string value) => (value ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}

internal static class UnityStationBuilder
{
    public static async Task<UnityStationHandle> BuildStationAsync(World world, float3 position, floatQ rotation, ImportSession session)
    {
        // Preserve yaw while removing gaze pitch and roll from the floor pedestal.
        float3 forward = rotation * float3.Forward;
        float yaw = forward.x * forward.x + forward.z * forward.z < 0.000001f ? 0f
            : MathF.Atan2(forward.x, forward.z) * (180f / MathF.PI);
        var template = await StationTemplateLoader.LoadAsync("station", world, position, floatQ.Euler(0f, yaw, 0f));
        try { return new UnityStationHandle(template, session); }
        catch { if (!template.Host.IsDestroyed) template.Host.Destroy(); throw; }
    }
}
