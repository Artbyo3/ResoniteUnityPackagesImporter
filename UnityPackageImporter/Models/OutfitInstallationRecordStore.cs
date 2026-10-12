using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Elements.Core;
using FrooxEngine;
using Renderite.Shared;

namespace UnityPackageImporter.Models;

internal sealed class OwnedRoot
{
    public Slot Slot { get; init; }
    public bool ActiveWhenEnabled { get; init; }
    public string Role { get; init; }
}

internal sealed class OwnedComponent
{
    public Component Component { get; init; }
    public bool EnabledWhenOutfitEnabled { get; init; }
    public string Role { get; init; }
}

internal sealed class InstallationRecordHandle
{
    public Slot RecordRoot { get; init; }
    public Slot TargetAvatar { get; init; }
    public Slot InstalledRoot { get; init; }
    public ValueMultiDriver<bool> Enabled { get; init; }
    public int SchemaVersion { get; init; }
    public string PrefabGuid { get; init; }
    public string ContentHash { get; init; }
    public OutfitInstallMatch Match { get; init; }
    public bool IsHealthy => RecordRoot != null && !RecordRoot.IsDestroyed &&
                             TargetAvatar != null && !TargetAvatar.IsDestroyed &&
                             InstalledRoot != null && !InstalledRoot.IsDestroyed &&
                             Enabled != null && !Enabled.IsDestroyed &&
                             SchemaVersion == OutfitInstallIdentity.CurrentSchemaVersion;
}

/// <summary>
/// Manages persistent outfit installation records, ownership metadata, and toggle controls
/// attached to target avatar rigs in FrooxEngine.
/// </summary>
internal static class OutfitInstallationRecordStore
{
    public const string RecordsSlotName = "Unity Package Importer";
    public const string RecordMarker = "UnityPackageImporter.ModularAvatarOutfit";
    public const string RecordTypeFieldName = "Record Type";
    public const string SchemaFieldName = "Schema Version";
    public const string PrefabGuidFieldName = "Prefab GUID";
    public const string ContentHashFieldName = "Content Hash";
    public const string SourceNameFieldName = "Source Name";
    public const string AvatarSignatureFieldName = "Avatar Signature";
    public const string TargetAvatarFieldName = "Target Avatar";
    public const string InstalledRootFieldName = "Installed Root";
    public const string StateSlotName = "State";
    public const string EnabledSlotName = "Enabled";
    public const string OwnedObjectsSlotName = "Owned Objects";
    public const string OwnedComponentsSlotName = "Owned Components";

    public static InstallationRecordHandle CreateInstallationRecord(
        BipedRig targetRig,
        string sourceName,
        Slot installedRoot,
        OutfitInstallIdentity identity,
        IReadOnlyList<OwnedRoot> ownedRoots,
        IReadOnlyList<OwnedComponent> ownedComponents,
        OwnedAttachmentTransaction<Slot> transaction,
        out Slot recordRoot)
    {
        var recordsRoot = targetRig.Slot.Children.FirstOrDefault(child => child.Name == RecordsSlotName);
        if (recordsRoot == null)
        {
            recordsRoot = targetRig.Slot.AddSlot(RecordsSlotName);
            transaction.Track(recordsRoot);
            recordsRoot.OrderOffset = 2000;
        }

        string displayName = sourceName.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
            ? sourceName[..^7]
            : sourceName;
        string shortId = identity.PrefabGuid.Length > 8
            ? identity.PrefabGuid[..8]
            : identity.PrefabGuid;
        recordRoot = recordsRoot.AddSlot("[Installing] " + displayName + " " + shortId);
        transaction.Track(recordRoot);

        var metadata = recordRoot.AddSlot("Metadata");
        AddValueField(metadata, RecordTypeFieldName, RecordMarker);
        AddValueField(metadata, SchemaFieldName, OutfitInstallIdentity.CurrentSchemaVersion);
        AddValueField(metadata, PrefabGuidFieldName, identity.PrefabGuid);
        AddValueField(metadata, ContentHashFieldName, identity.ContentHash);
        AddValueField(metadata, SourceNameFieldName, displayName);
        AddValueField(metadata, AvatarSignatureFieldName, BuildAvatarSignature(targetRig));

        var references = recordRoot.AddSlot("References");
        AddReferenceField(references, TargetAvatarFieldName, targetRig.Slot);
        AddReferenceField(references, InstalledRootFieldName, installedRoot);

        var ownedObjects = references.AddSlot(OwnedObjectsSlotName);
        var ownedComponentReferences = references.AddSlot(OwnedComponentsSlotName);
        var effectiveOwnedRoots = ownedRoots
            .Where(owned => owned?.Slot != null && !owned.Slot.IsDestroyed)
            .GroupBy(owned => owned.Slot)
            .Select(group => group.First())
            .Where(owned => !ownedRoots.Any(other =>
                other?.Slot != null &&
                other.Slot != owned.Slot &&
                !other.Slot.IsDestroyed &&
                owned.Slot.IsChildOf(other.Slot, true)))
            .ToList();

        var state = recordRoot.AddSlot(StateSlotName);
        var enabledSlot = state.AddSlot(EnabledSlotName);
        var enabled = enabledSlot.AttachComponent<ValueMultiDriver<bool>>();
        enabled.Value.Value = false;

        for (int i = 0; i < effectiveOwnedRoots.Count; i++)
        {
            var owned = effectiveOwnedRoots[i];
            var referenceSlot = ownedObjects.AddSlot((i + 1).ToString("D2") + " " + owned.Role);
            AddReferenceField(referenceSlot, "Object", owned.Slot);
            AddValueField(referenceSlot, "Active When Enabled", owned.ActiveWhenEnabled);

            var activeDriver = enabledSlot.AttachComponent<BooleanValueDriver<bool>>();
            activeDriver.FalseValue.Value = false;
            activeDriver.TrueValue.Value = owned.ActiveWhenEnabled;
            activeDriver.State.Value = false;
            activeDriver.TargetField.ForceLink(owned.Slot.ActiveSelf_Field);
            enabled.Drives.Add().ForceLink(activeDriver.State);
        }

        var effectiveOwnedComponents = ownedComponents
            .Where(owned => owned?.Component != null && !owned.Component.IsDestroyed)
            .GroupBy(owned => owned.Component)
            .Select(group => group.First())
            .ToList();
        for (int i = 0; i < effectiveOwnedComponents.Count; i++)
        {
            var owned = effectiveOwnedComponents[i];
            var referenceSlot = ownedComponentReferences.AddSlot(
                (i + 1).ToString("D2") + " " + owned.Role);
            AddComponentReferenceField(referenceSlot, "Component", owned.Component);
            AddValueField(referenceSlot, "Enabled With Outfit", owned.EnabledWhenOutfitEnabled);

            var componentDriver = enabledSlot.AttachComponent<BooleanValueDriver<bool>>();
            componentDriver.FalseValue.Value = false;
            componentDriver.TrueValue.Value = owned.EnabledWhenOutfitEnabled;
            componentDriver.State.Value = false;
            componentDriver.TargetField.ForceLink(owned.Component.EnabledField);
            enabled.Drives.Add().ForceLink(componentDriver.State);
        }

        if (effectiveOwnedRoots.Count == 0)
            throw new InvalidOperationException("The installation produced no owned outfit objects.");

        enabled.Value.Value = true;
        recordRoot.Name = displayName + " • " + shortId;
        return new InstallationRecordHandle
        {
            RecordRoot = recordRoot,
            TargetAvatar = targetRig.Slot,
            InstalledRoot = installedRoot,
            Enabled = enabled,
            SchemaVersion = OutfitInstallIdentity.CurrentSchemaVersion,
            PrefabGuid = identity.PrefabGuid,
            ContentHash = identity.ContentHash,
            Match = OutfitInstallMatch.Current
        };
    }

    public static InstallationRecordHandle FindInstallationRecord(
        Slot avatarRoot,
        OutfitInstallIdentity identity)
    {
        if (avatarRoot == null || avatarRoot.IsDestroyed || identity == null) return null;

        foreach (var candidate in EnumerateSelfAndChildren(avatarRoot))
        {
            var metadata = candidate.Children.FirstOrDefault(child => child.Name == "Metadata");
            if (metadata == null ||
                !string.Equals(ReadDirectStringField(metadata, RecordTypeFieldName), RecordMarker,
                    StringComparison.Ordinal))
                continue;

            string prefabGuid = ReadDirectStringField(metadata, PrefabGuidFieldName);
            string contentHash = ReadDirectStringField(metadata, ContentHashFieldName);
            int schemaVersion = ReadDirectValueField(metadata, SchemaFieldName, 0);
            var match = identity.Compare(prefabGuid, contentHash);
            if (match == OutfitInstallMatch.None) continue;

            var references = candidate.Children.FirstOrDefault(child => child.Name == "References");
            var state = candidate.Children.FirstOrDefault(child => child.Name == StateSlotName);
            var enabledSlot = state?.Children.FirstOrDefault(child => child.Name == EnabledSlotName);
            var recordedAvatar = references == null ? null : ReadDirectSlotReference(references, TargetAvatarFieldName);
            if (recordedAvatar != null && recordedAvatar != avatarRoot) continue;

            return new InstallationRecordHandle
            {
                RecordRoot = candidate,
                TargetAvatar = references == null
                    ? null
                    : ReadDirectSlotReference(references, TargetAvatarFieldName),
                InstalledRoot = references == null
                    ? null
                    : ReadDirectSlotReference(references, InstalledRootFieldName),
                Enabled = enabledSlot?.GetComponent<ValueMultiDriver<bool>>(),
                SchemaVersion = schemaVersion,
                PrefabGuid = prefabGuid,
                ContentHash = contentHash,
                Match = match
            };
        }

        return null;
    }

    public static HashSet<Slot> GetInstalledOutfitRoots(Slot avatarRoot)
    {
        var roots = new HashSet<Slot>();
        foreach (var candidate in EnumerateSelfAndChildren(avatarRoot))
        {
            var metadata = candidate.Children.FirstOrDefault(child => child.Name == "Metadata");
            if (metadata == null || ReadDirectStringField(metadata, RecordTypeFieldName) != RecordMarker) continue;
            var references = candidate.Children.FirstOrDefault(child => child.Name == "References");
            if (references == null) continue;
            var recordedAvatar = ReadDirectSlotReference(references, TargetAvatarFieldName);
            if (recordedAvatar != null && recordedAvatar != avatarRoot) continue;
            var installedRoot = ReadDirectSlotReference(references, InstalledRootFieldName);
            if (installedRoot != null && !installedRoot.IsDestroyed) roots.Add(installedRoot);
            var objects = references.Children.FirstOrDefault(child => child.Name == OwnedObjectsSlotName);
            if (objects == null) continue;
            foreach (var entry in objects.Children)
            {
                var ownedRoot = ReadDirectSlotReference(entry, "Object");
                if (ownedRoot != null && !ownedRoot.IsDestroyed) roots.Add(ownedRoot);
            }
        }
        return roots;
    }

    public static void BindOutfitToggle(
        ContextMenuItemSource item,
        InstallationRecordHandle record,
        string avatarName)
    {
        if (item == null || item.IsDestroyed || record == null || !record.IsHealthy) return;

        item.ButtonEnabled.Value = true;
        item.CloseMenuOnPress.Value = true;
        item.Color.Value = new colorX(0.45f, 0.82f, 0.62f, 1f);
        item.Slot.AttachComponent<ButtonToggle>().TargetValue.Target = record.Enabled.Value;
        var labelDriver = item.Slot.AttachComponent<BooleanValueDriver<string>>();
        labelDriver.FalseValue.Value = "Enable outfit on " + avatarName;
        labelDriver.TrueValue.Value = "Disable outfit on " + avatarName;
        labelDriver.State.Value = record.Enabled.Value.Value;
        labelDriver.TargetField.Target = item.Label;
        CopyEnabledState(record, labelDriver.State, item.Slot);
    }

    public static void CopyEnabledState(
        InstallationRecordHandle record,
        IField<bool> target,
        Slot host)
    {
        var copy = host.AttachComponent<ValueCopy<bool>>();
        copy.Source.Target = record.Enabled.Value;
        copy.Target.Target = target;
        copy.WriteBack.Value = false;
    }

    public static void AddValueField<T>(Slot parent, string name, T value)
    {
        var fieldSlot = parent.AddSlot(name);
        fieldSlot.AttachComponent<ValueField<T>>().Value.Value = value;
    }

    public static void AddReferenceField(Slot parent, string name, Slot target)
    {
        var fieldSlot = parent.AddSlot(name);
        fieldSlot.AttachComponent<ReferenceField<Slot>>().Reference.Target = target;
    }

    public static void AddComponentReferenceField(Slot parent, string name, Component target)
    {
        var fieldSlot = parent.AddSlot(name);
        fieldSlot.AttachComponent<ReferenceField<Component>>().Reference.Target = target;
    }

    public static string ReadDirectStringField(Slot root, string name)
    {
        return ReadDirectValueField(root, name, "");
    }

    public static T ReadDirectValueField<T>(Slot root, string name, T fallback)
    {
        var field = root.Children.FirstOrDefault(child => child.Name == name)
            ?.GetComponent<ValueField<T>>();
        return field == null ? fallback : field.Value.Value;
    }

    public static Slot ReadDirectSlotReference(Slot root, string name)
    {
        return root.Children.FirstOrDefault(child => child.Name == name)
            ?.GetComponent<ReferenceField<Slot>>()?.Reference.Target;
    }

    public static string BuildAvatarSignature(BipedRig rig)
    {
        var names = new List<string> { rig.Slot.Name, rig.Bones.Count.ToString() };
        foreach (BodyNode bodyNode in Enum.GetValues(typeof(BodyNode)))
        {
            if (bodyNode == BodyNode.NONE) continue;
            var bone = rig.TryGetBone(bodyNode);
            names.Add(bodyNode + ":" + (bone == null || bone.IsDestroyed ? "" : bone.Name));
        }
        string signatureSource = string.Join("\n", names);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signatureSource)));
    }

    private static IEnumerable<Slot> EnumerateSelfAndChildren(Slot root)
    {
        yield return root;
        foreach (var child in root.GetAllChildren(false)) yield return child;
    }
}
