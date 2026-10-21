using Elements.Assets;
using FrooxEngine;
using HarmonyLib;
using ResoniteModLoader;
using System.IO;
using System.Threading;
using UnityPackageImporter.Models;

namespace UnityPackageImporter;

public class UnityPackageImporter : ResoniteMod
{
    public override string Name => "UnityPackageImporter";
    public override string Author => "dfgHiatus, eia485, delta, Frozenreflex, benaclejames, 989onan, Artbyo3";
    public override string Version => "0.0.4";
    public override string Link => "https://github.com/Artbyo3/ResoniteUnityPackagesImporter";

    internal const string UNITY_PACKAGE_EXTENSION = ".unitypackage";
    internal const string UNITY_META_EXTENSION = ".meta";

    internal static ModConfiguration Config;
    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<string> uiTemplateDevelopmentDirectory = new(
        "uiTemplateDevelopmentDirectory", "Development only: directory containing native UI packages and their binding contracts. Empty uses bundled UI.", () => "");
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

    public override void OnEngineInit()
    {
        ModelScaleHelper.WarnHandler = Warn;
        new Harmony("net.dfgHiatus.UnityPackageImporter").PatchAll();
        Config = GetConfiguration();
        Directory.CreateDirectory(cachePath);
        Engine.Current.RunPostInit(() =>
        {
            EngineCompatibilityCheck.Run(Engine.VersionNumber, FrooxInternalBridge.ReflectedMembers, Msg, Warn, Error);
            AssetPatch("unitypackage");
        });
    }

    // Kept as the existing public extraction entry point.
    public static string[] DecomposeUnityPackage(string file, CancellationToken cancellationToken = default)
        => PackageImportCoordinator.ExtractPackage(file, cancellationToken);

    private void AssetPatch(string extension)
    {
        // Revised implementation using reflection to handle API changes
        // same fix as the svg import mod.
        try
        {
            Debug($"Attempting to add {extension} support to import system");

            // Get ImportExtension type via reflection since it's now a struct inside AssetHelper
            var importExtType = FrooxInternalBridge.ImportExtensionType;

            if (importExtType == null)
            {
                Error(EngineCompatibilityCheck.MissingMemberMessage("AssetHelper.ImportExtension"));
                return;
            }

            // Create an ImportExtension instance with reflection
            // Constructor args: (string ext, bool autoImport)
            var constructor = FrooxInternalBridge.ImportExtensionConstructor;
            if (constructor == null)
            {
                Error(EngineCompatibilityCheck.MissingMemberMessage("AssetHelper.ImportExtension..ctor(string, bool)"));
                return;
            }
            var importExt = constructor.Invoke(new object[] { extension, true });

            // Get the associatedExtensions field via reflection
            var extensionsField = FrooxInternalBridge.AssociatedExtensionsField;

            if (extensionsField == null)
            {
                Error(EngineCompatibilityCheck.MissingMemberMessage("AssetHelper.associatedExtensions"));
                return;
            }

            // Get the dictionary and add our extension to the Special asset class
            var extensions = extensionsField.GetValue(null);
            var dictType = extensions.GetType();
            var specialValue = dictType.GetMethod("get_Item").Invoke(extensions, new object[] { AssetClass.Special });

            if (specialValue == null)
            {
                Error("AssetHelper.associatedExtensions has no Special asset class list. The feature will not work on this Resonite version.");
                return;
            }

            // Add our ImportExtension to the list
            specialValue.GetType().GetMethod("Add").Invoke(specialValue, new[] { importExt });

            Debug($"{extension} import extension added successfully");
        }
        catch (System.Exception ex)
        {
            Error($"Failed to add {extension} to special import formats: {ex}");
        }
    }
}
