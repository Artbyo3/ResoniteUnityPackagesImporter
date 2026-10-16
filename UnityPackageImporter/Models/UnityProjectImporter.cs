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


namespace UnityPackageImporter;

public class UnityProjectImporter
{
    private readonly object dependencyLock = new();
    private readonly object materialCatalogLock = new();
    private readonly Dictionary<string, string> mutableAssetIndex;
    private readonly List<MissingMaterialBinding> missingMaterialBindings = new();
    private readonly ConcurrentDictionary<IAssetProvider<Material>, string> materialGuids = new();
    private Task<PackageMaterialCatalog> materialCatalogTask;

    public readonly Slot importTaskAssetRoot;
    public List<FileImportHelperTaskMaterial> TasksMaterials = new List<FileImportHelperTaskMaterial>();
    internal readonly AsyncImportCache<IAssetProvider<Material>> MaterialImports = new();
    internal readonly AsyncImportCache<StaticTexture2D> TextureImports = new();
    internal readonly PendingAssetAssignments<StaticTexture2D> MissingTextureAssignments = new();
    private readonly Dictionary<SkinnedMeshRenderer, IAssetProvider<Material>[]> automaticMaterials = new();
    public Dictionary<string, FileImportTaskScene> SharedImportedFBXScenes = new Dictionary<string, FileImportTaskScene>();
    public ReadOnlyDictionary<string, string> AssetIDDict;
    public ReadOnlyDictionary<string, string> ListOfUnityScenes;
    public List<string> files;
    public Slot root;
    public World world;
    internal UnityStationHandle Station { get; set; }
    internal ImportSession Session { get; set; }
    public ReadOnlyDictionary<string, string> ListOfPrefabs;
    public IReadOnlyList<string> PackageNames { get; }

    public UnityProjectImporter(
        IEnumerable<string> files,
        Dictionary<string, string> AssetIDDict,
        Dictionary<string, string> ListOfPrefabs,
        Dictionary<string, string> ListOfUnityScenes,
        Slot root,
        Slot assetsRoot,
        World world,
        IReadOnlyList<string> packageNames)
    {
        this.files = files.ToList();
        this.importTaskAssetRoot = assetsRoot;
        this.root = root;
        this.PackageNames = packageNames?.ToArray() ?? Array.Empty<string>();

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
            foreach (var renderer in itemRoot.GetComponentsInChildren<FrooxEngine.SkinnedMeshRenderer>())
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
                candidates.Contains(binding.MaterialGuid))) return true;
        return MissingTextureAssignments.CanResolveAny(candidates);
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
        IReadOnlyDictionary<string, string> dependencyAssets)
    {
        if (dependencyAssets == null)
            return new MaterialResolutionResult(GetMissingMaterialSummary(), 0);

        Session?.Check();
        lock (dependencyLock)
        {
            foreach (var asset in dependencyAssets)
                if (!mutableAssetIndex.ContainsKey(asset.Key))
                    mutableAssetIndex.Add(asset.Key, asset.Value);
            AssetIDDict = new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(mutableAssetIndex, StringComparer.OrdinalIgnoreCase));
        }
        lock (materialCatalogLock) materialCatalogTask = null;
        MaterialImports.RetryUnsuccessful();
        TextureImports.RetryUnsuccessful();

        List<MissingMaterialBinding> resolvable;
        lock (dependencyLock)
            resolvable = missingMaterialBindings
                .Where(binding => !binding.Resolved && mutableAssetIndex.ContainsKey(binding.MaterialGuid))
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
        Station?.UpdateProgress(0.1f, "Loading meshes and materials", "メッシュとマテリアルを読み込み中", "");
        var fbx_tasks = FillFBXFiles().ToArray();

        await Task.WhenAll(fbx_tasks.Select(task => task.RunnerWrapper()).ToArray());
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
        await Task.WhenAll(unityImportTasks.Select(task => task.StartImport()));
        await default(ToWorld);
        Session?.Check();
        if (stationHandle != null)
        {
            await stationHandle.CompleteSuccessAsync();
            Session?.Check();
            // Scene packages have no selection action: completion commits their scenes.
            if (!isClothingOrAvatar)
                foreach (var scene in unityImportTasks.OfType<UnitySceneImportTask>())
                    Session?.Commit(scene.CurrentStructureRootSlot);
            await stationHandle.StageAsync(this, unityImportTasks.OfType<UnityPrefabImportTask>().ToList());
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
            if(new string[]{"fbx"}.Contains(filename[filename.Length-1].ToLower())){

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
            if (new string[] { "fbx" }.Contains(filename[filename.Length - 1].ToLower()))
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
