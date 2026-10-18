using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;
using UnityPackageImporter.Models;
using UnityPackageImporter.UI;


namespace UnityPackageImporter;

public class UnityProjectImporter
{
    private readonly object dependencyLock = new();
    private readonly object materialCatalogLock = new();
    private readonly Dictionary<string, string> mutableAssetIndex;
    private readonly HashSet<string> conflictedGuids;
    private readonly List<MissingMaterialBinding> missingMaterialBindings = new();
    private readonly ConcurrentDictionary<IAssetProvider<Material>, string> materialGuids = new();
    private readonly Dictionary<string, List<string>> materialWarningsByGuid = new(StringComparer.OrdinalIgnoreCase);
    private Task<PackageMaterialCatalog> materialCatalogTask;

    public readonly Slot importTaskAssetRoot;
    public List<FileImportHelperTaskMaterial> TasksMaterials = new List<FileImportHelperTaskMaterial>();
    internal readonly AsyncImportCache<IAssetProvider<Material>> MaterialImports = new();
    internal readonly AsyncImportCache<StaticTexture2D> TextureImports = new();
    internal readonly PendingAssetAssignments<StaticTexture2D> MissingTextureAssignments = new();
    private readonly Dictionary<SkinnedMeshRenderer, IAssetProvider<Material>[]> automaticMaterials = new();
    public Dictionary<string, FileImportTaskScene> SharedImportedFBXScenes = new Dictionary<string, FileImportTaskScene>();
    internal ImportMeasurements Measurements { get; } = new();
    public ReadOnlyDictionary<string, string> AssetIDDict;
    public ReadOnlyDictionary<string, string> ListOfUnityScenes;
    public List<string> files;
    public Slot root;
    public World world;
    internal UnityStationHandle Station { get; set; }
    internal ImportSession Session { get; set; }
    public ReadOnlyDictionary<string, string> ListOfPrefabs;
    public IReadOnlyList<string> PackageNames { get; }
    public IReadOnlyCollection<string> ConflictedGuids => conflictedGuids;

    public UnityProjectImporter(
        IEnumerable<string> files,
        Dictionary<string, string> AssetIDDict,
        Dictionary<string, string> ListOfPrefabs,
        Dictionary<string, string> ListOfUnityScenes,
        Slot root,
        Slot assetsRoot,
        World world,
        IReadOnlyList<string> packageNames,
        IEnumerable<string> conflictedGuids = null)
    {
        this.files = files.ToList();
        this.importTaskAssetRoot = assetsRoot;
        this.root = root;
        this.PackageNames = packageNames?.ToArray() ?? Array.Empty<string>();
        this.conflictedGuids = new HashSet<string>(conflictedGuids ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        // Publish asset-index snapshots: a companion package must not mutate a
        // dictionary while background material importers are reading it.
        this.ListOfPrefabs = new ReadOnlyDictionary<string, string>(ListOfPrefabs);
        this.mutableAssetIndex = new Dictionary<string, string>(AssetIDDict, StringComparer.OrdinalIgnoreCase);
        this.AssetIDDict = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(this.mutableAssetIndex, StringComparer.OrdinalIgnoreCase));
        this.ListOfUnityScenes = new ReadOnlyDictionary<string, string>(ListOfUnityScenes);
        this.world = world;
    }

    internal void RegisterMaterialGuid(IAssetProvider<Material> material, string materialGuid)
    {
        if (material == null || string.IsNullOrWhiteSpace(materialGuid)) return;
        materialGuids[material] = materialGuid;
    }

    internal bool TryGetMaterialGuid(IAssetProvider<Material> material, out string materialGuid)
    {
        if (material != null && materialGuids.TryGetValue(material, out materialGuid) && !string.IsNullOrEmpty(materialGuid))
            return true;

        materialGuid = string.Empty;
        return false;
    }

    internal void RegisterMaterialWarning(string materialGuid, string warning)
    {
        if (string.IsNullOrWhiteSpace(materialGuid) || string.IsNullOrWhiteSpace(warning)) return;
        warning = ImportFailureReport.Sanitize(warning);
        lock (dependencyLock)
        {
            if (!materialWarningsByGuid.TryGetValue(materialGuid, out var list))
            {
                list = new List<string>();
                materialWarningsByGuid[materialGuid] = list;
            }
            if (!list.Contains(warning))
                list.Add(warning);
        }
    }

    internal IReadOnlyList<string> GetMaterialWarnings(string materialGuid)
    {
        if (string.IsNullOrWhiteSpace(materialGuid)) return Array.Empty<string>();
        lock (dependencyLock)
        {
            if (materialWarningsByGuid.TryGetValue(materialGuid, out var list))
                return list.ToArray();
            return Array.Empty<string>();
        }
    }

    internal IReadOnlyList<string> GetItemMaterialWarnings(Slot itemRoot)
    {
        if (itemRoot == null || itemRoot.IsDestroyed) return Array.Empty<string>();
        var result = new List<string>();
        var seenGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var renderers = itemRoot.GetComponentsInChildren<FrooxEngine.MeshRenderer>();
        foreach (var renderer in renderers)
        {
            if (renderer == null || renderer.IsDestroyed) continue;
            foreach (var mat in renderer.Materials)
            {
                if (mat != null && TryGetMaterialGuid(mat, out string guid) && seenGuids.Add(guid))
                {
                    lock (dependencyLock)
                    {
                        if (materialWarningsByGuid.TryGetValue(guid, out var warnings))
                        {
                            foreach (var w in warnings)
                            {
                                if (!result.Contains(w)) result.Add(w);
                            }
                        }
                    }
                }
            }
        }

        lock (dependencyLock)
        {
            foreach (var binding in missingMaterialBindings)
            {
                if (IsPendingMissingBinding(binding, itemRoot) && !string.IsNullOrEmpty(binding.MaterialGuid) && seenGuids.Add(binding.MaterialGuid))
                {
                    if (materialWarningsByGuid.TryGetValue(binding.MaterialGuid, out var warnings))
                    {
                        foreach (var w in warnings)
                        {
                            if (!result.Contains(w)) result.Add(w);
                        }
                    }
                }
            }
        }

        return result;
    }

    internal void RegisterMissingMaterial(
        FrooxEngine.SkinnedMeshRenderer renderer,
        int materialIndex,
        string materialGuid)
    {
        if (renderer == null || renderer.IsDestroyed || materialIndex < 0 || MaterialDependencyDiagnostics.IsBuiltinOrEmptyGuid(materialGuid)) return;
        lock (dependencyLock)
        {
            missingMaterialBindings.RemoveAll(binding =>
                ReferenceEquals(binding.Renderer, renderer) &&
                binding.MaterialIndex == materialIndex);

            missingMaterialBindings.Add(new MissingMaterialBinding
            {
                Renderer = renderer,
                MaterialIndex = materialIndex,
                MaterialGuid = materialGuid
            });
        }
    }

    internal void ClearMissingMaterial(
        FrooxEngine.SkinnedMeshRenderer renderer,
        int materialIndex)
    {
        if (renderer == null || materialIndex < 0) return;
        lock (dependencyLock)
        {
            missingMaterialBindings.RemoveAll(binding =>
                ReferenceEquals(binding.Renderer, renderer) &&
                binding.MaterialIndex == materialIndex);
        }
    }

    private bool IsPendingMissingBinding(MissingMaterialBinding binding, Slot itemRoot)
    {
        if (binding == null ||
            binding.Resolved ||
            binding.Renderer == null ||
            binding.Renderer.IsDestroyed ||
            binding.Renderer.Slot == null ||
            binding.Renderer.Slot.IsDestroyed ||
            (itemRoot != null && !binding.Renderer.Slot.IsChildOf(itemRoot, true)))
            return false;

        if (binding.MaterialIndex >= 0 && binding.MaterialIndex < binding.Renderer.Materials.Count)
        {
            var assigned = binding.Renderer.Materials[binding.MaterialIndex];
            if (assigned != null && TryGetMaterialGuid(assigned, out _))
                return false;
        }
        return true;
    }

    internal MaterialDependencySummary GetMissingMaterialSummary(Slot itemRoot = null)
    {
        List<MissingMaterialBinding> pending;
        lock (dependencyLock)
            pending = missingMaterialBindings.Where(binding => IsPendingMissingBinding(binding, itemRoot)).ToList();

        Func<object, bool> textureTargetFilter = null;
        if (itemRoot != null && !itemRoot.IsDestroyed)
        {
            var usedMaterialSlots = new HashSet<Slot>();
            foreach (var renderer in itemRoot.GetComponentsInChildren<FrooxEngine.MeshRenderer>())
            {
                if (renderer == null || renderer.IsDestroyed) continue;
                foreach (var mat in renderer.Materials)
                {
                    if (mat is Component comp && comp.Slot != null && !comp.Slot.IsDestroyed)
                        usedMaterialSlots.Add(comp.Slot);
                }
            }
            textureTargetFilter = target =>
                target is IWorldElement element &&
                element.FindNearestParent<Slot>() is Slot matSlot &&
                usedMaterialSlots.Contains(matSlot);
        }

        return MaterialDependencyDiagnostics.Analyze(
            pending.GroupBy(binding => binding.Renderer)
                .Select(group => group.Select(binding => binding.MaterialGuid)),
            Array.Empty<string>(), MissingTextureAssignments.GetMissingAssetGuids(textureTargetFilter));
    }

    internal bool CanResolveAnyMaterial(IEnumerable<string> assetGuids, Slot itemRoot = null)
    {
        var candidates = new HashSet<string>(assetGuids ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        lock (dependencyLock)
            if (missingMaterialBindings.Any(binding =>
                IsPendingMissingBinding(binding, itemRoot) &&
                candidates.Contains(binding.MaterialGuid) &&
                !conflictedGuids.Contains(binding.MaterialGuid))) return true;
        return MissingTextureAssignments.CanResolveAny(candidates.Where(c => !conflictedGuids.Contains(c)));
    }

    internal bool IsAssetAssignedOrCached(string guid)
    {
        if (string.IsNullOrWhiteSpace(guid)) return false;
        if (materialGuids.Values.Any(g => string.Equals(g, guid, StringComparison.OrdinalIgnoreCase)))
            return true;
        if (MaterialImports.Contains(guid + ":toon") || MaterialImports.Contains(guid + ":automatic"))
            return true;
        if (TextureImports.Contains(guid))
            return true;
        lock (dependencyLock)
        {
            if (missingMaterialBindings.Any(b => b.Resolved && string.Equals(b.MaterialGuid, guid, StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }

    internal Task<PackageMaterialCatalog> GetPackageMaterialCatalogAsync()
    {
        lock (materialCatalogLock)
            return materialCatalogTask ??= Task.Run(() => PackageMaterialCatalog.Build(AssetIDDict));
    }

    internal async Task<IAssetProvider<Material>> ImportPackageMaterialAsync(string materialGuid)
    {
        await default(ToWorld);
        Session?.Check();
        if (!AssetIDDict.TryGetValue(materialGuid, out string materialPath) ||
            !Path.GetExtension(materialPath).Equals(".mat", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The selected material is not available in the imported package.");

        var task = new FileImportHelperTaskMaterial(materialGuid, materialPath, this);
        await default(ToBackground);
        var material = await task.runImportFileMaterialsAsync();
        await default(ToWorld);
        Session?.Check();
        return material ?? throw new InvalidDataException("The selected material could not be converted.");
    }

    internal async Task ApplyMaterialPresetAsync(Slot root, string preset)
    {
        await default(ToWorld);
        Session?.Check();
        var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>();
        foreach (var renderer in renderers)
            if (!automaticMaterials.ContainsKey(renderer))
                automaticMaterials.Add(renderer, renderer.Materials.ToArray());
        if (preset == "Automatic")
        {
            foreach (var renderer in renderers)
                for (int i = 0; i < automaticMaterials[renderer].Length && i < renderer.Materials.Count; i++)
                    renderer.Materials[i] = automaticMaterials[renderer][i];
            return;
        }
        if (preset != "XiexeToon") throw new ArgumentException("Unknown generated shader preset.", nameof(preset));
        var replacements = new Dictionary<IAssetProvider<Material>, IAssetProvider<Material>>();
        foreach (var material in automaticMaterials.Where(pair => renderers.Contains(pair.Key)).SelectMany(pair => pair.Value).Where(value => value != null).Distinct())
        {
            if (!TryGetMaterialGuid(material, out string matGuid))
            {
                var fallbackSource = TasksMaterials.FirstOrDefault(task => !task.ismissing && !string.IsNullOrEmpty(task.MaterialGuid) && task.finalMaterial == material);
                matGuid = fallbackSource?.MaterialGuid;
            }
            if (string.IsNullOrEmpty(matGuid) || !AssetIDDict.TryGetValue(matGuid, out string sourceFile)) continue;
            var task = new FileImportHelperTaskMaterial(matGuid, sourceFile, this, true);
            var converted = await task.runImportFileMaterialsAsync();
            await default(ToWorld);
            Session?.Check();
            replacements[material] = converted;
        }
        foreach (var renderer in renderers)
            for (int i = 0; i < automaticMaterials[renderer].Length && i < renderer.Materials.Count; i++)
                if (automaticMaterials[renderer][i] != null && replacements.TryGetValue(automaticMaterials[renderer][i], out var material))
                    renderer.Materials[i] = material;
    }

    internal async Task<MaterialResolutionResult> ResolveMissingMaterialsAsync(
        IReadOnlyDictionary<string, string> dependencyAssets,
        IReadOnlyCollection<string> companionConflictedGuids = null)
    {
        if (dependencyAssets == null)
            return new MaterialResolutionResult(GetMissingMaterialSummary(), 0);

        Session?.Check();
        lock (dependencyLock)
        {
            UnityPackageAssetIndex.MergeCompanion(
                mutableAssetIndex,
                conflictedGuids,
                dependencyAssets,
                companionConflictedGuids,
                IsAssetAssignedOrCached,
                UnityPackageImporter.Warn);

            AssetIDDict = new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(mutableAssetIndex, StringComparer.OrdinalIgnoreCase));
        }
        lock (materialCatalogLock) materialCatalogTask = null;
        MaterialImports.RetryUnsuccessful();
        TextureImports.RetryUnsuccessful();

        List<MissingMaterialBinding> resolvable;
        lock (dependencyLock)
            resolvable = missingMaterialBindings
                .Where(binding => !binding.Resolved &&
                                  mutableAssetIndex.ContainsKey(binding.MaterialGuid) &&
                                  !conflictedGuids.Contains(binding.MaterialGuid))
                .ToList();

        int restored = 0;
        foreach (var materialGroup in resolvable.GroupBy(
                     binding => binding.MaterialGuid,
                     StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                string materialGuid = materialGroup.Key;
                string materialPath;
                lock (dependencyLock)
                    materialPath = mutableAssetIndex[materialGuid];

                // FileImportHelperTaskMaterial creates its asset slot in the constructor,
                // so it must be constructed while holding the world update lock.
                await default(ToWorld);
                Session?.Check();
                var materialTask = new FileImportHelperTaskMaterial(
                    materialGuid,
                    materialPath,
                    this);
                await default(ToBackground);
                var material = await materialTask.runImportFileMaterialsAsync();

                await default(ToWorld);
                Session?.Check();
                if (material == null) continue;
                foreach (var binding in materialGroup)
                {
                    if (binding.Renderer == null || binding.Renderer.IsDestroyed)
                    {
                        binding.Resolved = true;
                        continue;
                    }

                    while (binding.Renderer.Materials.Count <= binding.MaterialIndex)
                        binding.Renderer.Materials.Add();
                    binding.Renderer.Materials[binding.MaterialIndex] = material;
                    if (automaticMaterials.TryGetValue(binding.Renderer, out var automatic) && binding.MaterialIndex < automatic.Length)
                        automatic[binding.MaterialIndex] = material;
                    binding.Resolved = true;
                    restored++;
                }
                await default(ToBackground);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                UnityPackageImporter.Warn(
                    "Could not restore material '" + materialGroup.Key + "' on " +
                    materialGroup.Count() + " renderer assignment(s): " + ex);
                await default(ToBackground);
            }
        }

        restored += await MissingTextureAssignments.ResolveAsync(AssetIDDict.Keys,
            Session?.Token ?? default,
            (guid, exception) => UnityPackageImporter.Warn("Could not restore texture '" + guid + "': " + exception));

        return new MaterialResolutionResult(GetMissingMaterialSummary(), restored);
    }

    public async Task StartImports()
    {
        try
        {
            await ImportProject();
        }
        finally
        {
            await default(ToWorld);
            foreach (var task in SharedImportedFBXScenes.Values)
                task.DestroyTemplate();
            SharedImportedFBXScenes.Clear();
        }
    }

    private async Task ImportProject()
    {
        Session?.Check();
        await default(ToBackground);
        UnityPackageImporter.Msg("Start Project Importing for unitypackage");
        FrooxInternalBridge.ValidateModelImportApi();

        // I feel so smart making the wait all import fbx tasks code. - @989onan
        await default(ToWorld);
        Station?.UpdateProgress(0.1f, "loadingMeshesAndMaterials", "");
        var fbx_tasks = FillFBXFiles().ToArray();

        using (Measurements.Measure(ImportStage.ModelAssets, fbx_tasks.Length))
            await BoundedTaskScheduler.RunAsync(fbx_tasks, (task, _) => task.RunnerWrapper(), lifetime: Session?.Lifetime);
        Session?.Check();
        await default(ToBackground);
        // Now we have a full list of meta files and prefabs regarding this import file list from our prefix (where ever this is even if not a unity package folder) we now begin the hard part *drums* making the files go onto the model!
        List<IUnityStructureImporter> unityImportTasks = new List<IUnityStructureImporter>();
        // A bundled demonstration scene does not change the station's prefab
        // selection flow. Only scene-only packages publish scenes automatically.
        bool isClothingOrAvatar = this.ListOfPrefabs.Count > 0;

        int total = this.ListOfPrefabs.Count + this.ListOfUnityScenes.Count;
        int rowSize = MathX.Max(1, MathX.CeilToInt(MathX.Sqrt((float)total)));

        float3 GlobalPosition = new float3(0,0,0);
        floatQ GlobalRotation = new floatQ(0, 0, 0, 1);

        await default(ToWorld);
        this.world.LocalUser.GetPointInFrontOfUser(out GlobalPosition, out GlobalRotation, null, null, 0.7f, true);
        await default(ToBackground);
        int counter = 0;
        int prefabTotal = this.ListOfPrefabs.Count;

        // Cardinal 90° yaw snap logic for avatars/clothing:
        // Project forward gaze onto horizontal XZ plane and snap to nearest cardinal axis (0°, 90°, 180°, 270°)
        // so that spawned items align strictly to World X or World Z with zero diagonal tilt.
        float3 rawForward = GlobalRotation * float3.Forward;
        float fx = rawForward.x;
        float fz = rawForward.z;
        float lenSq = fx * fx + fz * fz;

        float snappedYaw;
        if (lenSq < 0.0001f)
        {
            snappedYaw = 0f;
        }
        else if (MathF.Abs(fz) >= MathF.Abs(fx))
        {
            snappedYaw = fz >= 0f ? 0f : 180f;
        }
        else
        {
            snappedYaw = fx >= 0f ? 90f : 270f;
        }

        floatQ snappedRotation = floatQ.Euler(0f, snappedYaw, 0f);
        floatQ taskRotation = isClothingOrAvatar ? snappedRotation : GlobalRotation;

        UnityStationHandle stationHandle = Station;

        foreach (KeyValuePair<string,string> Prefab in this.ListOfPrefabs)
        {
            UnityPackageImporter.Msg("create prefab import task obj for prefab \"" + Prefab.Value + "\"");
            await default(ToWorld);

            float3 spawnPosition;
            if (isClothingOrAvatar)
            {
                // Avatars & clothing: strictly lock into a single horizontal straight line aligned to the cardinal axis (1.15m spacing)
                float spacing = 1.15f;
                float3 right = snappedRotation * float3.Right;
                float3 lineOffset = right * ((counter - (prefabTotal - 1) * 0.5f) * spacing);
                spawnPosition = GlobalPosition + lineOffset;
            }
            else
            {
                // Worlds and scenes: standard grid offset
                spawnPosition = (GlobalRotation * UniversalImporter.GridOffset(ref counter, rowSize)) + GlobalPosition;
            }
            counter++;

            var task = new UnityPrefabImportTask(spawnPosition, Prefab, this, taskRotation);
            if (stationHandle != null)
                task.ReportProgress = async value => { await default(ToWorld); stationHandle.ReportPrefab(task, value); };
            unityImportTasks.Add(task);
            await default(ToBackground);
        }

        foreach(KeyValuePair<string, string> Scene in this.ListOfUnityScenes)
        {
            UnityPackageImporter.Msg("create scene import task obj for scene \""+ Scene.Value+ "\"");
            await default(ToWorld);
            unityImportTasks.Add(new UnitySceneImportTask((GlobalRotation * UniversalImporter.GridOffset(ref counter, rowSize)) + GlobalPosition, Scene, this));
            await default(ToBackground);
        }

        await default(ToWorld);
        stationHandle?.RegisterPrefabs(unityImportTasks.OfType<UnityPrefabImportTask>());
        SourceImportBatchResult<IUnityStructureImporter> outcomes;
        using (Measurements.Measure(ImportStage.SourceReconstruction, unityImportTasks.Count))
        {
            outcomes = await SourceImportBatch.RunAsync(unityImportTasks,
                (task, token) => task.StartImport(), async (task, ex) =>
            {
                await default(ToWorld);
                UnityPackageImporter.Warn($"Source reconstruction failed for '{task.ID.Value}': {ex.Message}");
                try
                {
                    if (task.CurrentStructureRootSlot != null && !task.CurrentStructureRootSlot.IsDestroyed)
                        task.CurrentStructureRootSlot.DestroyPreservingAssets();
                }
                catch (Exception cleanupEx)
                {
                    UnityPackageImporter.Warn($"Failed to clean up aborted task structure for '{task.ID.Value}': {cleanupEx.Message}");
                }
            }, lifetime: Session?.Lifetime);
        }
        foreach (var sample in Measurements.Snapshot())
            UnityPackageImporter.Msg(FormattableString.Invariant($"Import stage {sample.Stage}: {sample.ScheduledItems} scheduled inputs, {sample.Elapsed.TotalMilliseconds:F0} ms elapsed."));
        await default(ToWorld);
        Session?.Check();
        if (outcomes.Succeeded.Count == 0 && outcomes.Failed.Count > 0)
            throw new InvalidDataException("No prefab or scene could be reconstructed. Review the import errors before retrying.");
        if (stationHandle != null)
        {
            // A prepared item can exist before its source finishes. Only
            // completed sources may participate in selection or automatic commit.
            var prefabTasks = outcomes.Succeeded.OfType<UnityPrefabImportTask>().ToList();
            var sceneTasks = outcomes.Succeeded.OfType<UnitySceneImportTask>().ToList();
            bool isPartial = outcomes.Failed.Count > 0 || prefabTasks.Any(t => t.IsPartialCompatibility) || sceneTasks.Any(t => t.IsPartialCompatibility);
            var preparedItems = sceneTasks.SelectMany(scene => scene.PreparedItems ?? Enumerable.Empty<PreparedImportItem>())
                .Concat(prefabTasks.Select(prefab => prefab.PreparedItem))
                .Where(item => item?.CurrentStructureRootSlot is { IsDestroyed: false }).ToList();
            isPartial |= preparedItems.Any(item => item.ReadResult().IsPartial) ||
                sceneTasks.Any(scene => scene.ScenePreparation?.ReadResult().IsPartial == true);
            bool hasAvatarDefinitions = prefabTasks.Any(task => task.Manifest?.IsAvatarPrefab == true) ||
                sceneTasks.Any(task => task.Manifest?.IsAvatarPrefab == true);
            bool hasAvatarOrOutfitItems = preparedItems.Any(item =>
                item.Manifest.IsModularAvatarAttachment ||
                item.CurrentStructureRootSlot.GetComponentsInChildren<FrooxEngine.SkinnedMeshRenderer>().Count > 0);
            if (AvatarImportSelection.AutoCommitScenes(hasAvatarDefinitions, hasAvatarOrOutfitItems))
            {
                // Validate the entire automatic publication batch before releasing any scene.
                var autoScenes = sceneTasks.Where(s => s.ScenePreparation != null).ToList();
                foreach (var scene in autoScenes)
                    scene.ScenePreparation.ReadResult().Require(ImportOutputAction.KeepObject);
                foreach (var scene in autoScenes)
                    Session?.Commit(scene.ScenePreparation);
            }
            await stationHandle.CompleteSuccessAsync(isPartial);
            Session?.Check();
            await stationHandle.StageAsync(this, preparedItems);
        }

        await default(ToBackground);
        UnityPackageImporter.Msg("All finished!");
    }

    private IEnumerable<FileImportTaskScene> FillFBXFiles()
    {
        int total = 0;

        foreach (KeyValuePair<string,string> pair in AssetIDDict)
        {
            string[] filename = pair.Value.Split('.');
            if (string.Equals(filename[filename.Length - 1], "fbx", StringComparison.OrdinalIgnoreCase))
            {

                if (!SharedImportedFBXScenes.ContainsKey(pair.Key))
                {
                    total++;
                }
            }
        }

        int rowSize = MathX.Max(1, MathX.CeilToInt(MathX.Sqrt((float)total)));
        int counter = 0;
        this.world.LocalUser.GetPointInFrontOfUser(out float3 GlobalPosition, out floatQ GlobalRotation, null, null, 0.7f, true);
        foreach (KeyValuePair<string, string> pair in AssetIDDict)
        {
            string[] filename = pair.Value.Split('.');
            if (string.Equals(filename[filename.Length - 1], "fbx", StringComparison.OrdinalIgnoreCase))
            {
                if (!SharedImportedFBXScenes.ContainsKey(pair.Key))
                {
                    UnityPackageImporter.Debug("now importing \"" + pair.Value + "\" for later use by prefabs and scenes!");
                    FileImportTaskScene importtask = new FileImportTaskScene(this.root, pair.Key, this, this.AssetIDDict[pair.Key], (GlobalRotation * UniversalImporter.GridOffset(ref counter, rowSize)) + GlobalPosition);
                    this.SharedImportedFBXScenes.Add(pair.Key, importtask);
                    yield return importtask;

                }
            }
        }

        yield break;
    }
}
