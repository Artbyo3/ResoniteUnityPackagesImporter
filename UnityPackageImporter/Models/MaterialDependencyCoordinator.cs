using System.Collections.Generic;
using System.Threading.Tasks;
using FrooxEngine;

namespace UnityPackageImporter.Models;

internal sealed class MissingMaterialBinding
{
    public SkinnedMeshRenderer Renderer { get; init; }
    public int MaterialIndex { get; init; }
    public string MaterialGuid { get; init; }
    public string RendererName { get; init; }
    public bool Resolved { get; set; }
}

internal sealed class MaterialResolutionResult
{
    public MaterialResolutionResult(MaterialDependencySummary remaining, int restoredReferenceCount)
    {
        Remaining = remaining;
        RestoredReferenceCount = restoredReferenceCount;
    }

    public MaterialDependencySummary Remaining { get; }
    public int RestoredReferenceCount { get; }
}

internal static class MaterialDependencyCoordinator
{
    private static readonly object Sync = new();
    private static readonly Dictionary<World, MaterialDependencySession> WaitingByWorld = new();
    internal static bool HasWaiting(World world)
    {
        lock (Sync) return world != null && WaitingByWorld.TryGetValue(world, out var session) && session.IsUsable && session.IsWaiting;
    }
    internal static void BeginWaiting(MaterialDependencySession session)
    {
        if (!session.IsUsable) return;
        MaterialDependencySession replaced;
        lock (Sync)
        {
            WaitingByWorld.TryGetValue(session.World, out replaced);
            WaitingByWorld[session.World] = session;
        }
        if (replaced != null && replaced != session) replaced.Back();
    }
    internal static void StopWaiting(MaterialDependencySession session)
    {
        lock (Sync)
            if (WaitingByWorld.TryGetValue(session.World, out var current) && current == session) WaitingByWorld.Remove(session.World);
    }
    public static async Task<bool> TryResolveWaitingAsync(World world, IReadOnlyDictionary<string, string> assets)
    {
        MaterialDependencySession session;
        lock (Sync)
        {
            if (world == null || !WaitingByWorld.TryGetValue(world, out session)) return false;
            if (!session.IsUsable) { WaitingByWorld.Remove(world); return false; }
        }
        if (!session.CanUse(assets?.Keys))
        {
            await default(ToWorld);
            session.NoMatch();
            return false;
        }
        await session.ApplyAsync(assets);
        return true;
    }
}

// UI lifetime and extraction lifetime are independent. Only an explicit request arms the next package.
internal sealed class MaterialDependencySession
{
    private readonly UnityProjectImporter importer;
    private readonly UnityStationHandle station;
    private bool applying;
    private readonly System.Threading.SemaphoreSlim applyLock = new(1, 1);
    private int generation;
    public World World => importer.world;
    public bool IsWaiting { get; private set; }
    public bool IsUsable => station.IsAlive && World != null && !World.RootSlot.IsDestroyed;
    public MaterialDependencySession(UnityProjectImporter project, UnityStationHandle handle) { importer = project; station = handle; }
    public bool CanUse(IEnumerable<string> ids) => IsUsable && IsWaiting && importer.CanResolveAnyMaterial(ids);
    public void BeginWaiting()
    {
        if (applying || !IsUsable) return;
        generation++;
        IsWaiting = true;
        MaterialDependencyCoordinator.BeginWaiting(this);
        station.SetCompanionState(true, true, "Waiting for package", "パッケージを待機中");
    }
    public void Back()
    {
        generation++;
        IsWaiting = false;
        MaterialDependencyCoordinator.StopWaiting(this);
        station.SetCompanionState(true, false);
    }
    public void Continue()
    {
        generation++;
        IsWaiting = false;
        MaterialDependencyCoordinator.StopWaiting(this);
        station.SetCompanionState(false, false);
    }
    public void NoMatch() => station.SetCompanionState(true, true, "This package has no matching materials", "このパッケージに対応するマテリアルがありません");
    public async Task ApplyAsync(IReadOnlyDictionary<string, string> assets)
    {
        await applyLock.WaitAsync();
        if (!CanUse(assets?.Keys)) { applyLock.Release(); return; }
        applying = true;
        int request = generation;
        try
        {
            await default(ToWorld);
            station.SetCompanionState(true, true, "Connecting materials", "マテリアルを接続中");
            var result = await importer.ResolveMissingMaterialsAsync(assets);
            await default(ToWorld);
            station.RefreshMaterials();
            if (request != generation || !IsUsable) return;
            if (result.Remaining.HasMissingAssets)
                station.SetCompanionState(true, true, "More materials are needed", "追加のマテリアルが必要です");
            else
            {
                IsWaiting = false;
                MaterialDependencyCoordinator.StopWaiting(this);
                station.SetCompanionState(true, true, "Materials ready", "マテリアルの準備ができました");
                await Task.Delay(1000);
                await default(ToWorld);
                if (request == generation) station.SetCompanionState(false, false);
            }
        }
        finally { applying = false; applyLock.Release(); }
    }
}
