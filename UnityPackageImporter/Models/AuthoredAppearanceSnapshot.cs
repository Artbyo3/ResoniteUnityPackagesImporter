#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FrooxEngine;
using UnityPackageImporter.FrooxEngineRepresentation;
using UnityGameObject = UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes.GameObject;
using UnityTransform = UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes.Transform;
using UnityPrefabInstance = UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes.PrefabInstance;

namespace UnityPackageImporter.Models;

internal sealed class AuthoredAppearanceSnapshot
{
    internal AuthoredAppearanceHierarchy Hierarchy { get; }
    private readonly IReadOnlyDictionary<long, Slot> slots;

    private AuthoredAppearanceSnapshot(AuthoredAppearanceHierarchy hierarchy, IReadOnlyDictionary<long, Slot> slots)
    {
        Hierarchy = hierarchy;
        this.slots = slots;
    }

    internal static AuthoredAppearanceSnapshot? For(IUnityStructureImporter importer) => importer switch
    {
        UnitySceneImportTask scene => scene.AppearanceSnapshot,
        UnityPrefabImportTask prefab => prefab.AppearanceSnapshot,
        _ => null
    };

    internal bool TryGetSlot(long identity, out Slot slot) => slots.TryGetValue(identity, out slot!);

    internal static AuthoredAppearanceSnapshot Capture(IUnityStructureImporter importer)
    {
        var stage = importer.CurrentStructureRootSlot;
        var sources = importer.existingIUnityObjects;
        var identities = new Dictionary<Slot, long>();
        var transformSlots = new Dictionary<long, Slot>();
        Slot? DirectSlot(IUnityObject source) => source switch
        {
            UnityGameObject game => game.frooxEngineSlot,
            UnityTransform transform => transform.parentHashedGameObj?.frooxEngineSlot ??
                (sources.TryGetValue(transform.m_GameObjectID, out var owner) && owner is UnityGameObject game ? game.frooxEngineSlot : null),
            _ => null
        };
        Slot? ResolveSlot(IUnityObject source)
        {
            var slot = DirectSlot(source);
            if (!string.IsNullOrEmpty(source.m_CorrespondingSourceObject?.guid))
            {
                if (source.m_PrefabInstance == null || !source.m_PrefabInstance.TryGetValue("fileID", out ulong instanceId) ||
                    !sources.TryGetValue(instanceId, out var entry) || entry is not UnityPrefabInstance instance ||
                    !instance.TryFindObject(source.m_CorrespondingSourceObject, out var resolved))
                    throw new InvalidDataException("Authored appearance alias has no exact owning model instance: " + source.id + ".");
                if (source is UnityGameObject && resolved is not UnityGameObject || source is UnityTransform && resolved is not UnityTransform)
                    throw new InvalidDataException("Authored appearance alias resolves to a different source component type: " + source.id + ".");
                var instanceSlot = DirectSlot(resolved);
                if (slot != instanceSlot)
                    throw new InvalidDataException("Authored appearance alias disagrees with its exact model instance: " + source.id + ".");
                slot = instanceSlot;
            }
            if (slot == null || slot.IsDestroyed || !PreparedImportItem.Contains(stage, slot)) return null;
            return slot;
        }
        foreach (var entry in sources)
        {
            if (entry.Value is not UnityGameObject) continue;
            var slot = ResolveSlot(entry.Value);
            if (slot == null) continue;
            long identity = unchecked((long)entry.Key);
            if (!identities.TryAdd(slot, identity))
                throw new InvalidDataException("Multiple source GameObjects claim the same appearance slot: " + identity + ".");
        }
        foreach (var entry in sources)
        {
            if (entry.Value is not UnityTransform) continue;
            var slot = ResolveSlot(entry.Value);
            if (slot != null) transformSlots.Add(unchecked((long)entry.Key), slot);
        }
        var scaffolds = sources.Values.OfType<UnityPrefabInstance>().Select(instance => instance.importask?.ModelSceneRoot)
            .OfType<Slot>().Where(slot => !slot.IsDestroyed && PreparedImportItem.Contains(stage, slot)).ToHashSet();
        var captured = AuthoredAppearanceGraphCapture<Slot>.Capture(stage, identities, transformSlots, scaffolds,
            sources.Keys.Select(value => unchecked((long)value)), slot => slot.Children, slot => slot.Name, slot => slot.ActiveSelf);
        return new AuthoredAppearanceSnapshot(captured.Hierarchy, captured.Targets);
    }
}
