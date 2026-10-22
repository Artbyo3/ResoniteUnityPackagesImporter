using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;
using UnityPackageImporter.Extractor;
using UnityPackageImporter.UI;
using static global::UnityPackageImporter.UnityPackageImporter;

namespace UnityPackageImporter.Models;

// Owns package extraction and the prompt/station lifecycle. Individual project
// imports handle models and materials; the mod entry point only registers hooks.
internal static class PackageImportCoordinator
{
    internal static string[] ExtractPackage(string file, CancellationToken cancellationToken = default, ImportReportContext reports = null)
    {
        bool standalone = reports == null;
        reports ??= ImportReportService.Create(new[] { file });
        reports.Stage = ImportReportStage.Extraction;
        try { return ExtractPackageCore(file, cancellationToken, reports); }
        catch (System.Exception error) when (error is not System.OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The public extraction entry point can run without any station/session.
            if (standalone) ImportReportService.Record(reports, error, error);
            throw;
        }
    }

    private static string[] ExtractPackageCore(string file, CancellationToken cancellationToken, ImportReportContext reports)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dir = Path.Combine(cachePath, "v2", Utils.GenerateMD5(file));
        cancellationToken.ThrowIfCancellationRequested();
        var files = UnityPackageExtractor.Unpack(file, dir, cancellationToken: cancellationToken,
            entriesObserved: count => reports?.EntryRead(file, count));
        reports?.Extracted(file, files);
        UnityPackageImporter.Msg($"Extracted or reused {files.Count} files from {Path.GetFileName(file)}");
        return files.ToArray();
    }

    private static List<string> ExtractPackages(IEnumerable<string> packages, CancellationToken cancellation, ImportReportContext reports)
    {
        var extractedFiles = new List<string>();
        foreach (string unitypackage in packages)
        {
            cancellation.ThrowIfCancellationRequested();
            extractedFiles.AddRange(ExtractPackage(unitypackage, cancellation, reports));
        }
        cancellation.ThrowIfCancellationRequested();
        return extractedFiles;
    }

    private static async Task<bool> TryImportCompanionAsync(List<string> packages, World world, ImportReportContext reports)
    {
        await default(ToWorld);
        var request = MaterialDependencyCoordinator.CaptureWaiting(world);
        if (request == null) return false;
        try
        {
            // Include extraction in the owner's drain, not only the eventual
            // material assignments. No-match releases this lease before the
            // package is offered as an unrelated ordinary import.
            using var operation = request.Enter();
            await default(ToBackground);
            reports.Stage = ImportReportStage.Extraction;
            var extractedFiles = ExtractPackages(packages, request.Token, reports);
            var index = UnityPackageAssetIndex.Build(extractedFiles);
            foreach (string conflict in index.Conflicts) UnityPackageImporter.Warn(conflict);
            request.Token.ThrowIfCancellationRequested();
            return await request.ResolveAsync(index.Assets, index.ConflictedGuids);
        }
        catch (System.OperationCanceledException) when (request.Token.IsCancellationRequested)
        {
            // Closing the owner already schedules its cleanup. Consume the
            // cancelled drop; do not create a new station or clean a borrowed
            // session from inside its own outstanding operation.
            return true;
        }
    }

    private static async Task<bool> ExtractAndImportAsync(List<string> packages, Slot slot, World world, UnityStationHandle station, ImportSession session)
    {
        List<Task> imports = new List<Task>();
        await default(ToBackground);
        session.Reports.Stage = ImportReportStage.Extraction;
        var extractedFiles = ExtractPackages(packages, session.Token, session.Reports);
        UnityPackageImporter.Msg("CALLING FindPrefabsAndMetas for " + packages.Count + " package(s) as one dependency-aware import");
        List<string> otherFiles = (await StartProjectImportAsync(
            extractedFiles,
            slot,
            imports,
            world,
            packages.Select(Path.GetFileName).ToArray(), station, session)).ToList();
        await default(ToWorld);
        await Task.WhenAll(imports);
        await default(ToWorld);
        session?.Check();
        if (imports.Count == 0 && station != null)
        {
            await station.CompleteSuccessAsync();
            session?.Check();
            station.ShowEmptyPackage(Path.GetFileName(packages[0]));
        }
        if (Config.GetValue(dumpPackageContents))
        {
            await default(ToWorld);
            session?.Check();
            // Explicit content dumping remains the engine's ordinary, independent
            // import flow. BatchImport destroys its launcher: never give it staging.
            var launcher = world.AddSlot("Unity Package Contents", false);
            launcher.GlobalPosition = slot.GlobalPosition;
            var dump = Config.GetValue(ImportPrefab)
                ? extractedFiles.Where(file => !Path.GetExtension(file).Equals(UNITY_META_EXTENSION, System.StringComparison.OrdinalIgnoreCase))
                : otherFiles;
            BatchFolderImporter.BatchImport(launcher, dump, Config.GetValue(importAsRawFiles));
        }
        await default(ToBackground);
        UnityPackageImporter.Msg("FINISHED ALL IMPORTS AND DONE WITH ALL TASKS!!");
        return true;
    }

    private static async Task<IEnumerable<string>> StartProjectImportAsync(
        IEnumerable<string> files,
        Slot importRoot,
        List<Task> imports,
        World world,
        IReadOnlyList<string> packageNames,
        UnityStationHandle station,
        ImportSession session)
    {
        UnityPackageImporter.Msg("Start Finding Prefabs and Metas");
        var fileList = files.ToList();
        foreach (var file in fileList)
            UnityPackageImporter.Msg("A file being imported is \""+file+"\"");
        session.Reports.Stage = ImportReportStage.Indexing;
        var assetIndex = UnityPackageAssetIndex.Build(fileList);
        foreach (string conflict in assetIndex.Conflicts) UnityPackageImporter.Warn(conflict);

        var AssetIDDict = assetIndex.Assets;
        var ListOfPrefabs = assetIndex.Prefabs;
        var ListOfUnityScenes = assetIndex.Scenes;

        UnityPackageImporter.Msg("Creating importer object");

        if (Config.GetValue(ImportPrefab) && (ListOfPrefabs.Count > 0 || ListOfUnityScenes.Count > 0))
        {
            await default(ToWorld);
            imports.Add(new UnityProjectImporter(
                files,
                AssetIDDict,
                ListOfPrefabs,
                ListOfUnityScenes,
                session?.Staging ?? importRoot,
                session?.Assets ?? world.AssetsSlot.AddSlot("UnityPackageImport - Assets"),
                world,
                packageNames,
                assetIndex.ConflictedGuids) { Station = station, Session = session }.StartImports());
            await default(ToBackground);
        }

        UnityPackageImporter.Msg("end Finding Prefabs and Metas");
        return assetIndex.OtherFiles.ToArray();
    }

    internal static async Task ImportAsync(List<string> files, Slot assets, World world, float3 position, floatQ rotation)
    {
        await default(ToWorld);
        PreImportPrompt prompt = null;
        UnityStationHandle station = null;
        ImportSession session = null;
        var reports = ImportReportService.Create(files, LocalizationManager.DetectLocaleCode());
        bool raw = false;
        try
        {
            ImportReportService.EnablePanel(reports);
            if (await TryImportCompanionAsync(files, world, reports)) return;
            await default(ToWorld);
            reports.Stage = ImportReportStage.Prompt;
            prompt = await PreImportPrompt.SpawnAsync(world, position, rotation, files);
            var choice = await prompt.WaitAsync();
            await default(ToWorld);
            if (choice == PackageImportChoice.Cancel) return;
            if (choice == PackageImportChoice.Raw)
            {
                assets.GlobalPosition = position;
                assets.GlobalRotation = rotation;
                foreach (string file in files) await UniversalImporter.ImportRawFile(assets, new ImportItem(file));
                assets.PersistentSelf = true;
                raw = true;
                return;
            }
            session = new ImportSession(assets, reports);
            station = await UnityStationBuilder.BuildStationAsync(world, position, rotation, session);
            station.UpdateProgress(0f, "extractingPackage", Path.GetFileName(files[0]), 0, files.Count);
            prompt.Close();
            using (session.Enter())
                await ExtractAndImportAsync(files, session.Staging, world, station, session);
        }
        catch (System.OperationCanceledException) when (session?.Token.IsCancellationRequested == true)
        {
            // Import was cancelled by user; clean up session staging.
            await default(ToWorld);
            await session.CleanupAsync();
        }
        catch (System.Exception ex)
        {
            await default(ToWorld);
            station?.CompleteFailure(ex);
            ImportReportService.Record(reports, ex, ex);
            throw;
        }
        finally
        {
            await default(ToWorld);
            prompt?.Close();
            if (station == null && !raw)
            {
                if (session != null) await session.CleanupAsync();
                else if (!assets.IsDestroyed) assets.DestroyPreservingAssets();
            }
        }
    }
}
