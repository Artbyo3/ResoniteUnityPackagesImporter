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
    private readonly Dictionary<UnityPrefabImportTask, float> progress = new();
    private readonly HashSet<UnityPrefabImportTask> created = new();
    private readonly Dictionary<UnityPrefabImportTask, string> names = new();
    private readonly Dictionary<UnityPrefabImportTask, string> presets = new();
    private List<UnityPrefabImportTask> items = new();
    private Slot preview, rowTemplate;
    private BipedRig selectedAvatar;
    private UnityProjectImporter importer;
    private MaterialDependencySession dependencies;
    private int selected = -1, lifecycle;
    private bool busy, ready, failed;
    public Slot RootSlot => ui.Root;
    public bool IsAlive => !ui.Host.IsDestroyed && !ui.Root.IsDestroyed;
    private UnityPrefabImportTask Current => items.Count == 0 || selected < 0 ? null : items[selected];
    private bool IsOutfit => Current?.Manifest?.IsModularAvatarAttachment == true;
    private bool IsAvatar => !IsOutfit && Current != null && (Current.Manifest?.IsAvatarPrefab == true ||
        Current.CurrentStructureRootSlot.GetComponentInChildren<BipedRig>() != null);

    internal UnityStationHandle(StationTemplate template)
    {
        ui = template;
        Bind("close", CloseAsync);
        Bind("previous", () => SelectAsync(selected - 1));
        Bind("next", () => SelectAsync(selected + 1));
        Bind("create", CreateAvatarAsync);
        Bind("install", InstallOutfitAsync);
        Bind("targetChange", ChooseAvatarAsync, false);
        Bind("targetField", ChooseAvatarAsync, false);
        Bind("companionImport", () => { dependencies?.BeginWaiting(); return Task.CompletedTask; });
        Bind("companionBack", () => { dependencies?.Back(); return Task.CompletedTask; });
        Bind("companionPlaceholder", () => { dependencies?.Continue(); return Task.CompletedTask; });
        Bind("shaderAutomatic", () => ChangeShaderAsync("Automatic"));
        Bind("shaderToon", () => ChangeShaderAsync("XiexeToon"));
        foreach (var copy in ui.Root.GetComponentsInChildren<ValueCopy<bool>>())
            if (copy.Target.Target == Get<BooleanValueDriver<float3>>("cardScale").State)
            {
                copy.Enabled = false;
                copy.Target.Target = null; // Disabled drivers still hold their field drive.
            }
        Get<SmoothValue<float>>("arc").Speed.Value = 10f;
        Get<SmoothValue<float>>("arc").WriteBack.Value = false;
        Get<BooleanValueDriver<float2>>("advanced").State.Value = false;
        Get<BooleanValueDriver<bool>>("mode").State.Value = false;
        Get<BooleanValueDriver<bool>>("avatarPicker").State.Value = false;
        Get<ValueField<bool>>("shaderVisible").Value.Value = false;
        SetCompanionState(false, false);
        // The source importer has no Shape Changer handler yet. Expose its actual availability.
        Get<Checkbox>("bodyShape").State.Value = false;
        Get<Checkbox>("bodyShape").Slot.GetComponent<Button>().Enabled = false;
        ui.SetLocalized("bodyShapeDescription", "Not supported by this importer yet", "このインポーターではまだ未対応です");
        SetActions(false);
        rowTemplate = Get<Button>("avatarRow").Slot;
        foreach (var row in rowTemplate.Parent.Children.ToArray()) if (row != rowTemplate) row.Destroy();
        rowTemplate.ActiveSelf = false;
        ResetOutcome();
    }
    private T Get<T>(string key) where T : class, IWorldElement => ui.Get<T>(key);
    private void Bind(string key, Func<Task> action, bool replace = true)
    {
        Button button = Get<Button>(key);
        if (replace) ReleaseFixtureActions(button);
        NativeButtonEvents.Pressed(button, (_, _) =>
        {
            if (!IsAlive || busy) return;
            RootSlot.StartGlobalTask(async () =>
            {
                try { await action(); }
                catch (Exception ex)
                {
                    await default(ToWorld);
                    UnityPackageImporter.Warn("Station action failed: " + ex);
                    CompleteFailure(ex.Message);
                }
            });
        });
    }
    internal static void ReleaseFixtureActions(Button button)
    {
        foreach (var component in button.Slot.Components.ToArray())
            if (component is ButtonDestroy || component is ButtonToggle ||
                component.GetType().Name.StartsWith("ButtonValueSet`", StringComparison.Ordinal)) component.Enabled = false;
    }
    private void ResetOutcome()
    {
        Get<ValueField<bool>>("success").Value.Value = false;
        Get<ValueField<bool>>("failure").Value.Value = false;
    }
    public void UpdateProgress(float value, string english, string japanese, string file, int current = 0, int total = 0)
    {
        if (!IsAlive) return;
        Get<BooleanValueDriver<float3>>("cardScale").State.Value = true;
        Get<BooleanValueDriver<float3>>("cardPosition").State.Value = true;
        Get<SmoothValue<float>>("arc").TargetValue.Value = MathX.Clamp01(value) * 360f;
        ui.SetLocalized("ready", english, japanese);
        ui.SetLocalized("loaded", $"Loaded {current} of {total}", $"読み込み済み {current} / {total}");
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
        UpdateProgress(0.3f + 0.7f * progress.Values.Sum() / progress.Count, "Building prefabs", "プレハブを構築中",
            Path.GetFileName(task.ID.Value), progress.Values.Count(p => p >= 1f), progress.Count);
    }
    public void CompleteFailure(string reason)
    {
        if (!IsAlive) return;
        lifecycle++;
        failed = true;
        ResetOutcome();
        ui.SetLocalized("failure", "Import failed: " + Escape(reason), "インポート失敗: " + Escape(reason));
        Get<ValueField<bool>>("failure").Value.Value = true;
        Get<BooleanValueDriver<float3>>("cardScale").State.Value = true;
        Get<BooleanValueDriver<float3>>("cardPosition").State.Value = true;
    }
    public async Task CompleteSuccessAsync()
    {
        if (!IsAlive) return;
        int generation = ++lifecycle;
        ResetOutcome();
        Get<ValueField<bool>>("success").Value.Value = true;
        Get<SmoothValue<float>>("arc").TargetValue.Value = 360f;
        await Task.Delay(3500);
        await default(ToWorld);
        if (!IsAlive || generation != lifecycle) return;
        Get<BooleanValueDriver<float3>>("cardScale").State.Value = false;
        Get<BooleanValueDriver<float3>>("cardPosition").State.Value = false;
        Get<SmoothValue<float>>("arc").Speed.Value = 3f;
        Get<SmoothValue<float>>("arc").TargetValue.Value = 0f;
        await Task.Delay(2000);
        await default(ToWorld);
        if (IsAlive && generation == lifecycle) Get<SmoothValue<float>>("arc").Speed.Value = 10f;
    }
    internal async Task StageAsync(UnityProjectImporter project, List<UnityPrefabImportTask> tasks)
    {
        await default(ToWorld);
        if (!IsAlive) return;
        importer = project;
        items = tasks.Where(task => task.CurrentStructureRootSlot != null && !task.CurrentStructureRootSlot.IsDestroyed).ToList();
        foreach (var item in items)
        {
            names[item] = Path.GetFileNameWithoutExtension(item.ID.Value);
            presets[item] = "Automatic";
            item.CurrentStructureRootSlot.ActiveSelf = false;
        }
        ready = true;
        _ = RootSlot.World.RootSlot.StartGlobalTask(MonitorLifetimeAsync);
        if (items.Count == 0)
        {
            ShowEmptyPackage(project.PackageNames.FirstOrDefault() ?? "Package", project.ListOfUnityScenes.Count > 0);
            return;
        }
        await SelectAsync(0);
        if (project.GetMissingMaterialSummary().HasMissingAssets)
        {
            dependencies = new MaterialDependencySession(project, this);
            SetCompanionState(true, false);
        }
    }
    internal void ShowEmptyPackage(string name, bool scenes = false)
    {
        ready = true;
        Get<Slot>("nameCard").ActiveSelf = false;
        Get<Checkbox>("protect").Slot.Parent.ActiveSelf = false;
        Get<Text>("avatarTitle").Content.Value = "<b>" + Escape(name) + "</b>";
        ui.SetLocalized("avatarContext", scenes ? "SCENE PACKAGE" : "ASSETS PACKAGE", scenes ? "シーンパッケージ" : "アセットパッケージ");
        ui.SetLocalized("createAvatar", scenes ? "<b>Scenes Imported</b>" : "<b>No Prefabs Found</b>", scenes ? "<b>シーンをインポートしました</b>" : "<b>プレハブが見つかりません</b>");
        SetActions(false);
    }
    private async Task MonitorLifetimeAsync()
    {
        var world = RootSlot.World;
        while (IsAlive && !world.RootSlot.IsDestroyed) await Task.Delay(1000);
        await default(ToWorld);
        if (world.RootSlot.IsDestroyed) return;
        dependencies?.Continue();
        foreach (var item in items)
            if (!item.CurrentStructureRootSlot.IsDestroyed) item.CurrentStructureRootSlot.ActiveSelf = true;
    }
    private void SetActions(bool enabled)
    {
        Get<Button>("create").Enabled = enabled && Current != null && !created.Contains(Current);
        Get<Button>("install").Enabled = enabled && Current != null && selectedAvatar != null &&
            ModularAvatarAttachmentInstaller.IsOwnedAvatar(selectedAvatar, RootSlot.World.LocalUser);
        Get<Button>("previous").Enabled = enabled && items.Count > 1;
        Get<Button>("next").Enabled = enabled && items.Count > 1;
    }
    private async Task SelectAsync(int index)
    {
        await default(ToWorld);
        if (!IsAlive || !ready || busy || items.Count == 0) return;
        if (Current != null) names[Current] = Get<Text>("name").Content.Value;
        selected = (index % items.Count + items.Count) % items.Count;
        Get<BooleanValueDriver<bool>>("mode").State.Value = IsOutfit;
        Get<Slot>("nameCard").ActiveSelf = true;
        Get<ValueField<bool>>("shaderVisible").Value.Value = false;
        Get<ValueField<string>>("shaderValue").Value.Value = presets[Current];
        Get<Text>("name").Content.Value = names[Current];
        foreach (string key in new[] { "avatarTitle", "outfitTitle" }) Get<Text>(key).Content.Value = "<b>" + Escape(names[Current]) + "</b>";
        ui.SetLocalized("avatarContext", $"AVATAR PREFAB ({selected + 1} of {items.Count})", $"アバタープレハブ（{selected + 1} / {items.Count}）");
        if (!IsAvatar && !IsOutfit)
            ui.SetLocalized("avatarContext", $"PREFAB ({selected + 1} of {items.Count})", $"プレハブ（{selected + 1} / {items.Count}）");
        ui.SetLocalized("createAvatar", IsAvatar ? "<b>Create Avatar</b>" : "<b>Keep in World</b>", IsAvatar ? "<b>アバターを作成</b>" : "<b>ワールドに配置</b>");
        ui.SetLocalized("avatarName", IsAvatar ? "<b>Avatar Name</b>" : "<b>Object Name</b>", IsAvatar ? "<b>アバター名</b>" : "<b>オブジェクト名</b>");
        Get<Checkbox>("protect").Slot.Parent.ActiveSelf = IsAvatar;
        ui.SetLocalized("outfitContext", $"OUTFIT PREFAB ({selected + 1} of {items.Count})", $"衣装プレハブ（{selected + 1} / {items.Count}）");
        ui.SetLocalized(IsOutfit ? "outfitMetricsBody" : "avatarMetricsBody", Escape(BuildReceipt(Current)), Escape(BuildReceipt(Current, true)));
        RebuildPreview();
        SetActions(true);
    }
    private void RebuildPreview()
    {
        if (preview != null && !preview.IsDestroyed) preview.Destroy();
        if (Current == null || Current.CurrentStructureRootSlot.IsDestroyed) return;
        preview = Current.CurrentStructureRootSlot.Duplicate(Get<Slot>("preview"), false);
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
            .Where(rig => !rig.Slot.IsChildOf(RootSlot, true)).ToArray();
        for (int i = 0; i < candidates.Length; i++)
        {
            BipedRig candidate = candidates[i];
            var row = rowTemplate.Duplicate(parent, false);
            row.Name = "Avatar " + i;
            row.ActiveSelf = true;
            var button = row.GetComponent<Button>();
            ReleaseFixtureActions(button);
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
                if (!ModularAvatarAttachmentInstaller.IsOwnedAvatar(candidate, RootSlot.World.LocalUser)) return;
                selectedAvatar = candidate;
                Get<Text>("targetName").Content.Value = "<b>" + Escape(candidate.Slot.Name) + "</b>";
                Get<BooleanValueDriver<bool>>("avatarPicker").State.Value = false;
                SetActions(true);
            });
        }
        Get<BooleanValueDriver<bool>>("avatarPicker").State.Value = true;
        ui.SetLocalized("chooseAvatar", candidates.Length == 0 ? "<b>No owned avatars available</b>" : "<b>Choose an Avatar</b>",
            candidates.Length == 0 ? "<b>自分のアバターが見つかりません</b>" : "<b>アバターを選択</b>");
        return Task.CompletedTask;
    }
    private async Task CreateAvatarAsync()
    {
        if (Current == null || IsOutfit || created.Contains(Current)) return;
        var item = Current;
        busy = true;
        SetActions(false);
        try
        {
            var source = item.CurrentStructureRootSlot;
            if (Get<Checkbox>("expressions").State.Value) await item.BuildExpressionsAsync();
            await default(ToWorld);
            string name = Get<Text>("name").Content.Value;
            source.Name = string.IsNullOrWhiteSpace(name) ? names[item] : name.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
            if (IsAvatar && Get<Checkbox>("protect").State.Value) source.GetComponentOrAttach<SimpleAvatarProtection>().User.Target = source.World.LocalUser;
            source.GlobalPosition = Get<Slot>("preview").GlobalPosition;
            source.GlobalRotation = Get<Slot>("preview").GlobalRotation;
            source.ActiveSelf = true;
            source.PersistentSelf = true;
            created.Add(item);
            if (Get<Checkbox>("avatarReceipt").State.Value) SaveReceipt(source, item);
            if (preview != null && !preview.IsDestroyed) preview.Destroy();
        }
        finally { await default(ToWorld); busy = false; if (IsAlive) SetActions(true); }
    }
    private async Task InstallOutfitAsync()
    {
        if (Current == null || !IsOutfit || selectedAvatar == null) return;
        busy = true;
        SetActions(false);
        try
        {
            bool success = await ModularAvatarAttachmentInstaller.TryInstallDirectlyAsync(Current.CurrentStructureRootSlot,
                Current.Manifest, Current.existingIUnityObjects, selectedAvatar, Current.ID.Key, Current.ID.Value,
                Get<Checkbox>("outfitMenu").State.Value);
            await default(ToWorld);
            if (!success) throw new InvalidOperationException("The outfit could not be installed safely on the selected avatar.");
            if (Get<Checkbox>("outfitReceipt").State.Value) SaveReceipt(selectedAvatar.Slot, Current);
        }
        finally { await default(ToWorld); busy = false; if (IsAlive) SetActions(true); }
    }
    private async Task ChangeShaderAsync(string preset)
    {
        if (Current == null || created.Contains(Current)) return;
        busy = true;
        SetActions(false);
        Get<ValueField<bool>>("shaderVisible").Value.Value = false;
        try
        {
            await importer.ApplyMaterialPresetAsync(Current.CurrentStructureRootSlot, preset);
            await default(ToWorld);
            presets[Current] = preset;
            Get<ValueField<string>>("shaderValue").Value.Value = preset;
            RebuildPreview();
        }
        finally { await default(ToWorld); busy = false; if (IsAlive) SetActions(true); }
    }
    internal void SetCompanionState(bool visible, bool waiting, string english = null, string japanese = null)
    {
        if (!IsAlive) return;
        Get<BooleanValueDriver<bool>>("companionVisible").State.Value = visible;
        Get<ValueField<bool>>("companionWaiting").Value.Value = waiting;
        if (english != null) ui.SetLocalized("waiting", "<b>" + Escape(english) + "</b>", "<b>" + Escape(japanese ?? english) + "</b>");
    }
    internal void RefreshMaterials() { if (IsAlive) RebuildPreview(); }
    private async Task CloseAsync()
    {
        lifecycle++;
        dependencies?.Continue();
        Get<SmoothValue<float>>("arc").TargetValue.Value = 0f;
        Get<SmoothValue<float>>("arc").Speed.Value = 3f;
        Get<BooleanValueDriver<float3>>("cardScale").State.Value = false;
        Get<BooleanValueDriver<float3>>("cardPosition").State.Value = false;
        if (!ready && !failed) return;
        await Task.Delay(2000);
        await default(ToWorld);
        foreach (var item in items) if (!item.CurrentStructureRootSlot.IsDestroyed) item.CurrentStructureRootSlot.ActiveSelf = true;
        if (!ui.Host.IsDestroyed) ui.Host.Destroy();
    }
    private string BuildReceipt(UnityPrefabImportTask item, bool japanese = false) =>
        (japanese ? "プレハブ: " : "Prefab: ") + Path.GetFileName(item.ID.Value) +
        (japanese ? "\nインポートしたオブジェクト: " : "\nImported objects: ") + (item.existingIUnityObjects?.Count ?? 0) +
        (japanese ? "\nメッシュレンダラー: " : "\nMesh renderers: ") + item.CurrentStructureRootSlot.GetComponentsInChildren<SkinnedMeshRenderer>().Count +
        (japanese ? "\n不足しているマテリアル: " : "\nMissing materials: ") + importer.GetMissingMaterialSummary().MissingAssetCount +
        (japanese ? "\n検出した Modular Avatar コンポーネント: " : "\nModular Avatar components detected: ") + (item.Manifest?.ModularAvatarComponents.Count ?? 0) +
        (japanese ? "\nUI テンプレート: " : "\nUI template: ") + ui.Version +
        (japanese ? "\nShape Changer と Blendshape Sync の変換: 未対応" : "\nShape Changer and Blendshape Sync translation: not supported");
    private void SaveReceipt(Slot output, UnityPrefabImportTask item) =>
        output.AddSlot("Unity Import Receipt").AttachComponent<ValueField<string>>().Value.Value = BuildReceipt(item, ui.Locale("ready").First().State.Value);
    private static string Escape(string value) => (value ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}

internal static class UnityStationBuilder
{
    public static async Task<UnityStationHandle> BuildStationAsync(World world, float3 position, floatQ rotation)
    {
        var template = await StationTemplateLoader.LoadAsync("station", world, position, rotation);
        try { return new UnityStationHandle(template); }
        catch { if (!template.Host.IsDestroyed) template.Host.Destroy(); throw; }
    }
}
