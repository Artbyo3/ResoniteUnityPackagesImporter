using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using HarmonyLib;
using ResoniteModLoader;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityPackageImporter.Extractor;
using UnityPackageImporter.Models;

namespace UnityPackageImporter;

public class UnityPackageImporter : ResoniteMod
{
    public override string Name => "UnityPackageImporter";
    public override string Author => "dfgHiatus, eia485, delta, Frozenreflex, benaclejames, 989onan";
    public override string Version => "0.0.0";
    public override string Link => "https://github.com/dfgHiatus/ResoniteUnityPackagesImporter";

    internal const string UNITY_PACKAGE_EXTENSION = ".unitypackage";
    internal const string UNITY_PREFAB_EXTENSION = ".prefab";
    internal const string UNITY_SCENE_EXTENSION = ".unity";
    internal const string UNITY_META_EXTENSION = ".meta";

    internal static ModConfiguration Config;
    internal static string cachePath = Path.Combine(
        Engine.Current.CachePath,
        "Cache",
        "DecompressedUnityPrefabs");

    [AutoRegisterConfigKey]
    internal readonly static ModConfigurationKey<bool> dumpPackageContents =
     new ModConfigurationKey<bool>("dumpPackageContents", "Import files inside of unity packages: import all files inside unity packages instead of just prefabs when \"importPrefab\" is enabled.", () => false);
    [AutoRegisterConfigKey]
    internal readonly static ModConfigurationKey<bool> importAsRawFiles =
     new ModConfigurationKey<bool>("importAsRawFiles", "Import Binaries: Import files as raw binaries", () => false);
    [AutoRegisterConfigKey]
    internal readonly static ModConfigurationKey<bool> ImportPrefab =
         new ModConfigurationKey<bool>("importPrefab", "Import Prefabs and Scenes: Import prefabs inside unity packages (DISABLES ALL UNLESS \"Import files inside of unity packages\" IS ENABLED)", () => true);
    [AutoRegisterConfigKey]
    internal readonly static ModConfigurationKey<bool> importText =
         new ModConfigurationKey<bool>("importText", "Import Text: Import text inside packages", () => true);
    [AutoRegisterConfigKey]
    internal readonly static ModConfigurationKey<bool> importTexture =
         new ModConfigurationKey<bool>("importTexture", "Import Textures: Import textures inside packages", () => true);
    [AutoRegisterConfigKey]
    internal readonly static ModConfigurationKey<bool> importDocument =
         new ModConfigurationKey<bool>("importDocument", "Import Documents: Import documents inside packages", () => true);
    [AutoRegisterConfigKey]
    internal readonly static ModConfigurationKey<bool> importMesh =
         new ModConfigurationKey<bool>("importMesh", "Import Meshes: Import meshes inside packages", () => true);
    [AutoRegisterConfigKey]
    internal readonly static ModConfigurationKey<bool> importPointCloud =
         new ModConfigurationKey<bool>("importPointCloud", "Import Point Clouds: Import point clouds inside packages", () => true);
    [AutoRegisterConfigKey]
    internal readonly static ModConfigurationKey<bool> importAudio =
         new ModConfigurationKey<bool>("importAudio", "Import Audio: Import audio files inside packages", () => true);
    [AutoRegisterConfigKey]
    internal static ModConfigurationKey<bool> importFont =
         new ModConfigurationKey<bool>("importFont", "Import Fonts: Import fonts inside packages", () => true);
    [AutoRegisterConfigKey]
    internal static ModConfigurationKey<bool> importVideo =
         new ModConfigurationKey<bool>("importVideo", "Import Videos: Import videos inside packages", () => true);

    public override void OnEngineInit()
    {
        ModelScaleHelper.WarnHandler = Warn;
        new Harmony("net.dfgHiatus.UnityPackageImporter").PatchAll();
        Config = GetConfiguration();
        Directory.CreateDirectory(cachePath);
        Engine.Current.RunPostInit(() => AssetPatch("unitypackage"));
    }

    public static string[] DecomposeUnityPackage(string file)
    {
        var dir = Path.Combine(cachePath, "v2", Utils.GenerateMD5(file));
        var files = UnityPackageExtractor.Unpack(file, dir);
        Msg($"Extracted or reused {files.Count} files from {Path.GetFileName(file)}");
        return files.ToArray();
    }
    private void AssetPatch(string extension)
    {
        // Revised implementation using reflection to handle API changes
        // same fix as the svg import mod.
        try
        {
            Debug($"Attempting to add {extension} support to import system");

            // Get ImportExtension type via reflection since it's now a struct inside AssetHelper
            var assHelperType = typeof(AssetHelper);
            var importExtType = assHelperType.GetNestedType("ImportExtension",
                System.Reflection.BindingFlags.NonPublic);

            if (importExtType == null)
            {
                Error("ImportExtension type not found. This mod is toast.");
                return;
            }

            // Create an ImportExtension instance with reflection
            // Constructor args: (string ext, bool autoImport)
            var importExt = System.Activator.CreateInstance(importExtType,
                new object[] { extension, true });

            // Get the associatedExtensions field via reflection
            var extensionsField = assHelperType.GetField("associatedExtensions",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            if (extensionsField == null)
            {
                Error("Could not find associatedExtensions field");
                return;
            }

            // Get the dictionary and add our extension to the Special asset class
            var extensions = extensionsField.GetValue(null);
            var dictType = extensions.GetType();
            var specialValue = dictType.GetMethod("get_Item").Invoke(extensions, new object[] { AssetClass.Special });

            if (specialValue == null)
            {
                Error("Couldn't get Special asset class list");
                return;
            }

            // Add our ImportExtension to the list
            specialValue.GetType().GetMethod("Add").Invoke(specialValue, new[] { importExt });

            Debug("{extension} import extension added successfully");
        }
        catch (System.Exception ex)
        {
            Error($"Failed to add {extension} to special import formats: {ex}");
        }
    }

    private static async Task Scanfiles(List<string> hasUnityPackage, Slot slot, World world)
    {
        List<Task> imports = new List<Task>();
        await default(ToBackground);
        var scanthesefiles = new List<string>();
        foreach (string unitypackage in hasUnityPackage)
        {
            scanthesefiles.AddRange(DecomposeUnityPackage(unitypackage));
        }

        Msg("CALLING FindPrefabsAndMetas for " + hasUnityPackage.Count + " package(s) as one dependency-aware import");
        List<string> notprefabsandmetas = (await FindPrefabsAndMetas(
            scanthesefiles,
            slot,
            imports,
            world,
            hasUnityPackage.Select(Path.GetFileName).ToArray())).ToList();
        if (Config.GetValue(dumpPackageContents))
        {
            if (Config.GetValue(ImportPrefab))
            {
                //get all files that don't have metas
                BatchFolderImporter.BatchImport(slot, scanthesefiles.FindAll(i => !Path.GetExtension(i).ToLower().Equals(UNITY_META_EXTENSION)), Config.GetValue(importAsRawFiles));
            }
            else
            {
                //bring in no prefabs or metas
                BatchFolderImporter.BatchImport(slot, notprefabsandmetas, Config.GetValue(importAsRawFiles));
            }
        }

        await default(ToWorld);
        await Task.WhenAll(imports);
        await default(ToBackground);
        Msg("FINISHED ALL IMPORTS AND DONE WITH ALL TASKS!!");
    }

    private static async Task<IEnumerable<string>> FindPrefabsAndMetas(
        IEnumerable<string> files,
        Slot importSlotContainment,
        List<Task> imports,
        World world,
        IReadOnlyList<string> packageNames)
    {
        Msg("Start Finding Prefabs and Metas");
        var fileList = files.ToList();
        foreach (var file in fileList)
            Msg("A file being imported is \""+file+"\"");
        var assetIndex = UnityPackageAssetIndex.Build(fileList);
        foreach (string conflict in assetIndex.Conflicts) Warn(conflict);

        var AssetIDDict = assetIndex.Assets;
        var ListOfPrefabs = assetIndex.Prefabs;
        var ListOfMetas = assetIndex.Metas;
        var ListOfUnityScenes = assetIndex.Scenes;

        Msg("Creating importer object");

        await MaterialDependencyCoordinator.TryResolveWaitingAsync(
            world,
            AssetIDDict,
            packageNames);

        if (Config.GetValue(ImportPrefab) && (ListOfPrefabs.Count > 0 || ListOfUnityScenes.Count > 0))
        {
            await default(ToWorld);
            imports.Add(new UnityProjectImporter(
                files,
                AssetIDDict,
                ListOfPrefabs,
                ListOfMetas,
                ListOfUnityScenes,
                importSlotContainment,
                world.AssetsSlot.AddSlot("UnityPackageImport - Assets"),
                world,
                packageNames).StartImports());
            await default(ToBackground);
        }

        Msg("end Finding Prefabs and Metas");
        return assetIndex.OtherFiles.ToArray();
    }

    [HarmonyPatch(typeof(UniversalImporter),
        "ImportTask",
        typeof(AssetClass),
        typeof(IEnumerable<string>),
        typeof(World),
        typeof(float3),
        typeof(floatQ),
        typeof(float3),
        typeof(bool))]
    public partial class UniversalImporterPatch
    {
        public static bool Prefix(ref IEnumerable<string> files, ref World world, ref Task __result)
        {
            var hasUnityPackage = new List<string>();
            var notUnityPackage = new List<string>();

            Msg("Run UnityPackageImporter patch.");
            foreach (var file in files)
            {
                if (Path.GetExtension(file).ToLower() == UNITY_PACKAGE_EXTENSION)
                    hasUnityPackage.Add(file);
                else
                    notUnityPackage.Add(file);
            }

            World curworld = world.RootSlot.World;
            if (hasUnityPackage.Count > 0)
            {
                Msg("Start import of unity packages.");
                var slot = world.AddSlot("Unity Package Import");
                // We want scenes to position themselves at 0,0,0.
                // There is an edge case where the thing this is parented under would be moving, but that's just a skill issue on the user's part. - @989onan
                slot.GlobalPosition = new float3(0, 0, 0);
                // Let in-game user managers not freak out that we're doing stuff in root. - @989onan
                slot.SetParent(world.LocalUserSpace, true);
                slot.StartGlobalTask(async () => await Scanfiles(hasUnityPackage, slot, curworld));
            }

            // Once we have removed the prefabs, we let the original stuff go through so we have the files normally
            // Idk if we really need this if the stuff above is going to eventually just import prefabs and textures already set up... - @989onan

            files = notUnityPackage;
            if (notUnityPackage.Count == 0)
            {
                __result = Task.CompletedTask;
                return false; // We have only unity packages, so don't run the rest and make some random model import dialogue
            }
            return true;
        }
    }


    // Unused, should we keep? - @989onan
    private static bool ShouldImportFile(string file)
    {
        var extension = Path.GetExtension(file).ToLower();
        var assetClass = AssetHelper.ClassifyExtension(Path.GetExtension(file));
        return (Config.GetValue(importText) && assetClass == AssetClass.Text)
            || (Config.GetValue(importTexture) && assetClass == AssetClass.Texture)
            || (Config.GetValue(importDocument) && assetClass == AssetClass.Document)
            || (Config.GetValue(importPointCloud) && assetClass == AssetClass.PointCloud)
            || (Config.GetValue(importAudio) && assetClass == AssetClass.Audio)
            || (Config.GetValue(importFont) && assetClass == AssetClass.Font)
            || (Config.GetValue(importVideo) && assetClass == AssetClass.Video)
            /* Handle an edge case where assimp will try to import .xml files as 3D models */
            || (Config.GetValue(importMesh) && assetClass == AssetClass.Model && extension != ".xml")
            /* Handle recursive unity package imports */
            || extension == UNITY_PACKAGE_EXTENSION;
    }
}
