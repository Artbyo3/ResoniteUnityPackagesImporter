using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FrooxEngine;

namespace UnityPackageImporter.Models;

internal sealed class MissingMaterialBinding
{
    public SkinnedMeshRenderer Renderer { get; init; }
    public int MaterialIndex { get; init; }
    public string MaterialGuid { get; init; }
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
    internal static MaterialDependencyRequest CaptureWaiting(World world)
    {
        lock (Sync)
        {
            if (world == null || !WaitingByWorld.TryGetValue(world, out var session)) return null;
            if (session.IsUsable && session.IsWaiting) return session.CaptureRequest();
            RemoveWorld(world);
            return null;
        }
    }
    private static void RemoveWorld(World world)
    {
        lock (Sync)
        {
            WaitingByWorld.Remove(world);
            world.WorldDestroyed -= RemoveWorld;
        }
    }
    internal static void BeginWaiting(MaterialDependencySession session)
    {
        if (!session.IsUsable) return;
        MaterialDependencySession replaced;
        lock (Sync)
        {
            WaitingByWorld.TryGetValue(session.World, out replaced);
            if (replaced == null) session.World.WorldDestroyed += RemoveWorld;
            WaitingByWorld[session.World] = session;
        }
        if (replaced != null && replaced != session) replaced.Back();
    }
    internal static void StopWaiting(MaterialDependencySession session)
    {
        lock (Sync)
            if (WaitingByWorld.TryGetValue(session.World, out var current) && current == session) RemoveWorld(session.World);
    }
}

// A dropped package belongs to the request that was waiting when it arrived.
// It must not migrate to another station while extraction is in progress.
internal sealed class MaterialDependencyRequest
{
    private readonly MaterialDependencySession owner;
    private readonly int generation;
    public CancellationToken Token => owner.Token;
    public IDisposable Enter() => owner.Enter();

    public MaterialDependencyRequest(MaterialDependencySession owner, int generation)
    {
        this.owner = owner;
        this.generation = generation;
    }

    public async Task<bool> ResolveAsync(IReadOnlyDictionary<string, string> assets)
    {
        Token.ThrowIfCancellationRequested();
        await default(ToWorld);
        Token.ThrowIfCancellationRequested();
        // Back, Continue, or another station taking over discards this request.
        // A stale drop must not unexpectedly open a new independent import.
        if (!owner.IsCurrentRequest(generation)) return true;
        if (!owner.CanUse(assets?.Keys))
        {
            owner.NoMatch();
            return false;
        }
        return await owner.ApplyAsync(assets, generation);
    }
}

// Only an explicit request arms the next package. Its extraction and resolution
// belong to the existing import session, without creating a second station.
internal sealed class MaterialDependencySession
{
    private readonly UnityProjectImporter importer;
    private readonly UnityStationHandle station;
    private bool applying;
    private readonly System.Threading.SemaphoreSlim applyLock = new(1, 1);
    private int generation;
    public World World => importer.world;
    public bool IsWaiting { get; private set; }
    public CancellationToken Token => importer.Session?.Token ?? default;
    public bool IsUsable => !Token.IsCancellationRequested && station.IsAlive && World != null && !World.RootSlot.IsDestroyed;
    public MaterialDependencySession(UnityProjectImporter project, UnityStationHandle handle) { importer = project; station = handle; }
    internal IDisposable Enter() => importer.Session?.Enter();
    internal MaterialDependencyRequest CaptureRequest() => new(this, generation);
    internal bool IsCurrentRequest(int request) => request == generation && IsUsable && IsWaiting;
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
    internal async Task<bool> ApplyAsync(IReadOnlyDictionary<string, string> assets, int request)
    {
        await applyLock.WaitAsync(Token);
        try
        {
            using var operation = importer.Session?.Enter();
            await default(ToWorld);
            importer.Session?.Check();
            if (!IsCurrentRequest(request)) return true;
            if (!CanUse(assets?.Keys)) return false;
            applying = true;
            station.SetCompanionState(true, true, "Connecting materials", "マテリアルを接続中");
            var result = await importer.ResolveMissingMaterialsAsync(assets);
            await default(ToWorld);
            importer.Session?.Check();
            if (!IsUsable) return true;
            station.RefreshMaterials();
            if (request != generation || !IsUsable) return true;
            if (result.Remaining.HasMissingAssets)
                station.SetCompanionState(true, true, "More materials are needed", "追加のマテリアルが必要です");
            else
            {
                IsWaiting = false;
                MaterialDependencyCoordinator.StopWaiting(this);
                station.SetCompanionState(true, true, "Materials ready", "マテリアルの準備ができました");
                await Task.Delay(1000, importer.Session?.Token ?? default);
                await default(ToWorld);
                importer.Session?.Check();
                if (request == generation && IsUsable) station.SetCompanionState(false, false);
            }
            return true;
        }
        finally { applying = false; applyLock.Release(); }
    }
}
