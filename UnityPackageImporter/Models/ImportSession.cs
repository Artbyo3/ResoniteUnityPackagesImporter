using System;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using Elements.Core;
using FrooxEngine;

namespace UnityPackageImporter.Models;

// A global, hidden staging hierarchy. Parenting under LocalUserSpace is NOT local-only.
// Completed outputs remain global objects; no reference-ID promotion is assumed.
internal sealed class ImportSession
{
    private static readonly ConditionalWeakTable<Slot, ImportSession> Sessions = new();
    private readonly ImportLifetime lifetime = new();
    private Task cleanup;
    private bool retainedAssets;
    private readonly World world;
    private readonly TaskCompletionSource worldGone = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Slot uiHost, uiRoot;
    public Slot Root { get; }
    public Slot Staging { get; }
    public Slot Assets { get; }
    public CancellationToken Token => lifetime.Token;

    public ImportSession(Slot root)
    {
        Root = root;
        world = root.World;
        root.Name = "Unity Package Import Session";
        root.PersistentSelf = false;
        Staging = root.AddSlot("Unfinished Imports");
        Staging.ActiveSelf = false;
        Assets = root.AddSlot("Import Assets");
        Sessions.Add(root, this);
        ((IDestroyable)Root).Destroyed += OnDestroyed;
        world.WorldDestroyed += OnWorldDestroyed;
    }

    public void BindUi(Slot host, Slot root)
    {
        uiHost = host;
        uiRoot = root;
        ((IDestroyable)host).Destroyed += OnDestroyed;
        ((IDestroyable)root).Destroyed += OnDestroyed;
    }
    private void OnDestroyed(IDestroyable destroyed) => _ = lifetime.CancelAndDrainAsync();
    private void OnWorldDestroyed(World destroyed)
    {
        _ = lifetime.CancelAndDrainAsync();
        worldGone.TrySetResult();
        Unsubscribe();
    }

    public IDisposable Enter() => lifetime.Enter();
    public void Check() => Token.ThrowIfCancellationRequested();
    public Task CancelAsync() => lifetime.CancelAndDrainAsync();

    public static bool IsTemporary(Slot slot)
        => Find(slot) != null;

    private static ImportSession Find(Slot slot)
    {
        for (var cursor = slot; cursor != null; cursor = cursor.Parent)
            if (Sessions.TryGetValue(cursor, out var session)) return session;
        return null;
    }

    internal static void PreserveAssetsForOutput(Slot source) => Find(source)?.RetainAssets();

    private void RetainAssets()
    {
        // Committed outputs must survive even a manual destruction of the entire
        // session root. Preserve provider identity so caches and later variants
        // continue sharing assets. Cleanup prunes unused providers afterwards.
        if (retainedAssets) return;
        Assets.SetParent(world.AssetsSlot, true);
        Assets.PersistentSelf = true;
        retainedAssets = true;
    }

    public Slot CreateStructure(string name, float3 position, floatQ rotation)
    {
        Check();
        var envelope = Staging.AddSlot(name + " - Import");
        envelope.GlobalPosition = position;
        envelope.GlobalRotation = rotation;
        return envelope.AddSlot(name);
    }

    public Slot Envelope(Slot source)
    {
        if (source == null || source.IsDestroyed) throw new InvalidOperationException("Imported source no longer exists.");
        var current = source;
        while (current.Parent != Staging && current.Parent != null) current = current.Parent;
        if (current.Parent != Staging) throw new InvalidOperationException("Imported source is not owned by this station.");
        return current;
    }

    public Slot Commit(Slot source)
    {
        Check();
        var envelope = Envelope(source);
        RetainAssets();
        envelope.SetParent(Root.World.RootSlot, true);
        envelope.PersistentSelf = true;
        return envelope;
    }

    // Called on the world context. Reentrant close / external deletion share one cleanup.
    public Task CleanupAsync() => cleanup ??= CleanupCoreAsync();

    private async Task CleanupCoreAsync()
    {
        await Task.WhenAny(lifetime.CancelAndDrainAsync(), worldGone.Task);
        if (world.IsDisposed || world.RootSlot.IsDestroyed) { Unsubscribe(); return; }
        await default(ToWorld);
        Unsubscribe();
        if (!Root.IsDestroyed && !world.RootSlot.IsDestroyed)
            Root.DestroyPreservingAssets();
        if (retainedAssets && !Assets.IsDestroyed && !world.RootSlot.IsDestroyed)
            Assets.DestroyPreservingAssets();
    }

    private void Unsubscribe()
    {
        Sessions.Remove(Root);
        ((IDestroyable)Root).Destroyed -= OnDestroyed;
        if (uiHost != null) ((IDestroyable)uiHost).Destroyed -= OnDestroyed;
        if (uiRoot != null) ((IDestroyable)uiRoot).Destroyed -= OnDestroyed;
        world.WorldDestroyed -= OnWorldDestroyed;
    }
}
