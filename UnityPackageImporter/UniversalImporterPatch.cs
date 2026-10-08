using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using HarmonyLib;
using UnityPackageImporter.Models;
using static global::UnityPackageImporter.UnityPackageImporter;

namespace UnityPackageImporter;

[HarmonyPatch(typeof(UniversalImporter),
    "ImportTask",
    typeof(AssetClass),
    typeof(IEnumerable<string>),
    typeof(World),
    typeof(float3),
    typeof(floatQ),
    typeof(float3),
    typeof(bool))]
internal static class UniversalImporterPatch
{
    public static bool Prefix(ref IEnumerable<string> files, World world, [HarmonyArgument(3)] float3 position,
        [HarmonyArgument(4)] floatQ rotation, ref Task __result, out Task __state)
    {
        __state = null;
        var hasUnityPackage = new List<string>();
        var notUnityPackage = new List<string>();

        UnityPackageImporter.Msg("Run UnityPackageImporter patch.");
        foreach (var file in files)
        {
            if (Path.GetExtension(file).ToLower() == UNITY_PACKAGE_EXTENSION)
                hasUnityPackage.Add(file);
            else
                notUnityPackage.Add(file);
        }

        if (hasUnityPackage.Count > 0)
        {
            UnityPackageImporter.Msg("Start import of unity packages.");
            var slot = world.AddSlot("Unity Package Import");
            // Identity session transform preserves authored scene coordinates and units.
            slot.PersistentSelf = false;
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            slot.StartGlobalTask(async () =>
            {
                try { await PackageImportCoordinator.ImportAsync(hasUnityPackage, slot, world, position, rotation); completion.TrySetResult(); }
                catch (System.Exception ex) { UnityPackageImporter.Error("Unity package import failed: " + ex); completion.TrySetException(ex); }
            });
            __state = completion.Task;
        }

        // Leave non-package files to the native importer and combine both tasks
        // in Postfix so mixed drops complete only after both import paths finish.

        files = notUnityPackage;
        if (notUnityPackage.Count == 0)
        {
            __result = __state ?? Task.CompletedTask;
            return false; // We have only unity packages, so don't run the rest and make some random model import dialogue
        }
        return true;
    }
    public static void Postfix(ref Task __result, Task __state)
    {
        if (__state != null && __result != __state) __result = Task.WhenAll(__result ?? Task.CompletedTask, __state);
    }
}
