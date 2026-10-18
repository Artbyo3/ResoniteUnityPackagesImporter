using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;
using Renderite.Shared;
using UnityPackageImporter.FrooxEngineRepresentation;
using UnityGameObject = UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes.GameObject;

namespace UnityPackageImporter.Models;

/// <summary>
/// Installs supported Modular Avatar attachments from the station. Installation
/// operates on a duplicate so the source remains available for recovery.
/// </summary>
internal static class ModularAvatarAttachmentInstaller
{
    private const string InstallerSlotName = "Modular Avatar Installer";

    private sealed class MergeSourceBinding
    {
        public ModularAvatarComponentDefinition Definition { get; init; }
        public Slot SourceSlot { get; init; }
        public int[] IndexPath { get; init; }
    }

    private sealed class BoneProxyBinding
    {
        public ModularAvatarComponentDefinition Definition { get; init; }
        public Slot SourceSlot { get; init; }
        public int[] IndexPath { get; init; }
        public float3 AuthoredGlobalPosition { get; init; }
        public floatQ AuthoredGlobalRotation { get; init; }
        public float3 AuthoredGlobalScale { get; init; }
    }

    private sealed class PreparedMerge
    {
        public MergeSourceBinding Binding { get; init; }
        public Slot SourceRoot { get; init; }
        public Slot TargetRoot { get; init; }
        public BoneAttachmentPlan Plan { get; init; }
        public Dictionary<string, Slot> SourceSlots { get; init; }
        public Dictionary<string, Slot> TargetSlots { get; init; }
    }

    internal static List<BipedRig> GetAvailableAvatarRigs(Slot contextSlot) => FindAvatarRigs(contextSlot);

    internal static async Task<(ImportResult result, string error)> TryInstallDirectlyDetailedAsync(
        PreparedImportItem item,
        BipedRig targetRig,
        bool installOutfitMenu = true,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string> assetFiles = null)
    {
        await default(ToWorld);
        cancellationToken.ThrowIfCancellationRequested();
        if (item == null) return (null, "The prepared outfit is unavailable.");
        var attachmentRoot = item.CurrentStructureRootSlot;
        var manifest = item.Manifest;
        var unityObjects = item.existingIUnityObjects;
        var prefabGuid = item.ID.Key;
        var prefabFile = item.ID.Value;
        if (attachmentRoot == null || manifest == null || unityObjects == null || targetRig == null)
            return (null, "Attachment parameters are missing or invalid.");

        try
        {
            item.ReadResult().Require(ImportOutputAction.InstallOutfit);
            var assetSnapshot = assetFiles?.ToDictionary(entry => entry.Key, entry => entry.Value,
                StringComparer.OrdinalIgnoreCase);
            await default(ToBackground);
            var identity = OutfitInstallIdentity.FromPrefab(prefabGuid, prefabFile, assetSnapshot, cancellationToken);
            await default(ToWorld);
            cancellationToken.ThrowIfCancellationRequested();
            if (attachmentRoot.IsDestroyed || targetRig.IsDestroyed)
                return (null, "Target avatar or outfit is no longer available.");
            using var installationLease = await AttachmentInstallationGate.EnterAsync(targetRig.Slot, cancellationToken);
            await default(ToWorld);
            cancellationToken.ThrowIfCancellationRequested();
            if (attachmentRoot.IsDestroyed || targetRig.IsDestroyed)
                return (null, "Target avatar or outfit is no longer available.");
            var sourceResult = item.ReadResult();
            sourceResult.Require(ImportOutputAction.InstallOutfit);
            var mergeBindings = ResolveMergeSources(attachmentRoot, manifest, unityObjects);
            var boneProxyBindings = ResolveBoneProxies(attachmentRoot, manifest, unityObjects);
            if (mergeBindings.Count != manifest.ModularAvatarComponents.Count(c => c.Kind == ModularAvatarComponentKind.MergeArmature) ||
                boneProxyBindings.Count != manifest.ModularAvatarComponents.Count(c => c.Kind == ModularAvatarComponentKind.BoneProxy))
                return (null, "Not all required outfit attachments could be resolved. Installation was not started.");

            if (mergeBindings.Count == 0 && boneProxyBindings.Count == 0)
                return (null, "No Merge Armature or Bone Proxy components could be found in the outfit.");

            string currentError = GetTargetError(mergeBindings, boneProxyBindings, targetRig);
            if (currentError != null)
            {
                UnityPackageImporter.Warn("Could not install on " + targetRig.Slot.Name + ": " + currentError);
                return (null, currentError);
            }

            var installed = await InstallCopyAsync(
                attachmentRoot,
                mergeBindings,
                boneProxyBindings,
                targetRig,
                identity,
                cancellationToken);

            await default(ToWorld);
            if (installed == null || !installed.IsHealthy)
                return (null, "Installation copy produced an incomplete or unhealthy result.");
            // The installation is committed. Finish its optional controls even if the
            // station closes now; do not report a completed outfit as cancelled.
            bool optionalControlsFailed = false;
            if (installOutfitMenu && !installed.RecordRoot.Children.Any(child => child.Name == "Outfit Controls"))
            {
                Slot controls = null;
                RootContextMenuItem contextItem = null;
                try
                {
                    controls = installed.RecordRoot.AddSlot("Outfit Controls");
                    var source = controls.AttachComponent<ContextMenuItemSource>();
                    OutfitInstallationRecordStore.BindOutfitToggle(source, installed, installed.RecordRoot.Name);
                    contextItem = targetRig.Slot.AttachComponent<RootContextMenuItem>();
                    contextItem.Item.Target = source;
                }
                catch (Exception ex)
                {
                    optionalControlsFailed = true;
                    if (contextItem != null && !contextItem.IsDestroyed) contextItem.Destroy();
                    if (controls != null && !controls.IsDestroyed) controls.Destroy();
                    UnityPackageImporter.Warn("Outfit installed, but its optional menu could not be created: " + ex.Message);
                }
            }
            return (sourceResult.ForInstalledCopy(optionalControlsFailed), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await default(ToWorld);
            UnityPackageImporter.Error("Failed to install attachment '" + attachmentRoot.Name + "' directly: " + ex);
            return (null, ex.Message);
        }
    }

    private static List<MergeSourceBinding> ResolveMergeSources(
        Slot attachmentRoot,
        AvatarPackageManifest manifest,
        Dictionary<ulong, IUnityObject> unityObjects)
    {
        var result = new List<MergeSourceBinding>();
        foreach (var definition in manifest.ModularAvatarComponents
                     .Where(component => component.Kind == ModularAvatarComponentKind.MergeArmature))
        {
            Slot initialSlot = null;
            if (definition.GameObjectFileId != 0)
            {
                if ((unityObjects.TryGetValue((ulong)definition.GameObjectFileId, out var unityObject) ||
                     unityObjects.TryGetValue(unchecked((ulong)definition.GameObjectFileId), out unityObject)) &&
                    unityObject is UnityGameObject gameObject &&
                    gameObject.frooxEngineSlot != null)
                {
                    initialSlot = gameObject.frooxEngineSlot;
                }
            }

            if (initialSlot == null && !string.IsNullOrEmpty(definition.GameObjectName))
            {
                initialSlot = EnumerateSelfAndChildren(attachmentRoot)
                    .FirstOrDefault(s => s.Name.Equals(definition.GameObjectName, StringComparison.Ordinal))
                    ?? EnumerateSelfAndChildren(attachmentRoot)
                    .FirstOrDefault(s => s.Name.Equals(definition.GameObjectName, StringComparison.OrdinalIgnoreCase));
            }

            if (initialSlot == null)
            {
                initialSlot = attachmentRoot;
            }

            Slot sourceSlot = initialSlot;
            // If the MergeArmature is placed on the attachment root or an outfit container,
            // try to resolve to its child Armature if present!
            if (sourceSlot == attachmentRoot || !IsLikelyArmature(sourceSlot))
            {
                var candidateArmature = sourceSlot.Children.FirstOrDefault(IsLikelyArmature)
                    ?? EnumerateSelfAndChildren(sourceSlot).FirstOrDefault(s => s != sourceSlot && IsLikelyArmature(s));
                if (candidateArmature != null)
                {
                    sourceSlot = candidateArmature;
                }
            }

            var path = GetIndexPath(attachmentRoot, sourceSlot);
            if (path == null)
            {
                throw new InvalidOperationException(
                    "Merge Armature source '" + sourceSlot.Name +
                    "' is outside attachment root '" + attachmentRoot.Name + "'.");
            }

            result.Add(new MergeSourceBinding
            {
                Definition = definition,
                SourceSlot = sourceSlot,
                IndexPath = path
            });
        }

        return result.OrderBy(binding => binding.IndexPath.Length).ToList();
    }

    private static List<BoneProxyBinding> ResolveBoneProxies(
        Slot attachmentRoot,
        AvatarPackageManifest manifest,
        Dictionary<ulong, IUnityObject> unityObjects)
    {
        var result = new List<BoneProxyBinding>();
        foreach (var definition in manifest.ModularAvatarComponents
                     .Where(component => component.Kind == ModularAvatarComponentKind.BoneProxy))
        {
            Slot proxySlot = null;
            if (definition.GameObjectFileId != 0)
            {
                if ((unityObjects.TryGetValue((ulong)definition.GameObjectFileId, out var unityObject) ||
                     unityObjects.TryGetValue(unchecked((ulong)definition.GameObjectFileId), out unityObject)) &&
                    unityObject is UnityGameObject gameObject &&
                    gameObject.frooxEngineSlot != null)
                {
                    proxySlot = gameObject.frooxEngineSlot;
                }
            }

            if (proxySlot == null && !string.IsNullOrEmpty(definition.GameObjectName))
            {
                proxySlot = EnumerateSelfAndChildren(attachmentRoot)
                    .FirstOrDefault(s => s.Name.Equals(definition.GameObjectName, StringComparison.Ordinal))
                    ?? EnumerateSelfAndChildren(attachmentRoot)
                    .FirstOrDefault(s => s.Name.Equals(definition.GameObjectName, StringComparison.OrdinalIgnoreCase));
            }

            if (proxySlot == null)
            {
                UnityPackageImporter.Warn(
                    "Could not resolve Bone Proxy GameObject " + definition.GameObjectFileId +
                    " in '" + attachmentRoot.Name + "'. Skipping proxy.");
                continue;
            }

            var path = GetIndexPath(attachmentRoot, proxySlot);
            if (path == null)
            {
                UnityPackageImporter.Warn(
                    "Bone Proxy source '" + proxySlot.Name +
                    "' is outside attachment root '" + attachmentRoot.Name + "'. Skipping proxy.");
                continue;
            }

            result.Add(new BoneProxyBinding
            {
                Definition = definition,
                SourceSlot = proxySlot,
                IndexPath = path
            });
        }

        return result.OrderBy(binding => binding.IndexPath.Length).ToList();
    }

    internal static bool IsOwnedAvatar(BipedRig rig, User localUser)
    {
        if (rig == null || rig.IsDestroyed || rig.Slot == null || rig.Slot.IsDestroyed || localUser == null) return false;
        var protections = rig.Slot.GetComponentsInParents<FrooxEngine.CommonAvatar.SimpleAvatarProtection>();
        return AvatarOwnershipValidator.EvaluateOwnership(
            isRigValid: true,
            isRigDestroyed: rig.IsDestroyed || rig.Slot.IsDestroyed,
            isTemporaryStaging: ImportSession.IsTemporary(rig.Slot),
            isSameWorld: rig.World == localUser.World,
            localUser: localUser,
            activeUser: rig.Slot.ActiveUser,
            allocatingUser: rig.Slot.GetAllocatingUser(),
            protectionUsers: protections.Select(p => p.User.Target).ToList());
    }

    private static List<BipedRig> FindAvatarRigs(Slot attachmentRoot)
    {
        return attachmentRoot.World.RootSlot.GetAllChildren(false)
            .Select(slot => slot.GetComponent<BipedRig>())
            .Where(rig => rig != null && !rig.IsDestroyed && rig.Slot.IsActive && rig.Bones.Count > 0)
            .Where(rig => IsOwnedAvatar(rig, attachmentRoot.World.LocalUser))
            .Where(rig => rig.Slot != attachmentRoot && !rig.Slot.IsChildOf(attachmentRoot, true))
            .GroupBy(rig => rig.Slot)
            .Select(group => group.First())
            .OrderBy(rig => rig.Slot.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string GetTargetError(
        IReadOnlyList<MergeSourceBinding> mergeBindings,
        IReadOnlyList<BoneProxyBinding> boneProxyBindings,
        BipedRig targetRig)
    {
        if (targetRig == null || targetRig.IsDestroyed || targetRig.Slot == null || targetRig.Slot.IsDestroyed)
            return "Avatar is no longer available";

        if (!IsOwnedAvatar(targetRig, targetRig.World.LocalUser)) return "This avatar is not owned by the local user";
        try
        {
            return ValidateTarget(mergeBindings, boneProxyBindings, targetRig);
        }
        catch (Exception ex)
        {
            UnityPackageImporter.Warn(
                "Could not validate Modular Avatar target '" + targetRig.Slot.Name + "': " + ex);
            return "Avatar validation failed";
        }
    }

    private static string ValidateTarget(
        IReadOnlyList<MergeSourceBinding> mergeBindings,
        IReadOnlyList<BoneProxyBinding> boneProxyBindings,
        BipedRig targetRig)
    {
        var nestedRoots = mergeBindings.Select(binding => binding.SourceSlot).ToHashSet();
        var excludedTargets = OutfitInstallationRecordStore.GetInstalledOutfitRoots(targetRig.Slot);
        foreach (var binding in mergeBindings)
        {
            var targetRoot = ResolveTargetRoot(targetRig.Slot, binding, excludedTargets, targetRig);
            if (targetRoot == null)
                return "Target armature path is missing, ambiguous, or belongs to another installed outfit";

            var sourceNodes = new Dictionary<string, Slot>();
            var targetNodes = new Dictionary<string, Slot>();
            var source = BuildHierarchy(binding.SourceSlot, nestedRoots, sourceNodes, binding.SourceSlot);
            var target = BuildHierarchy(targetRoot, new HashSet<Slot>(), targetNodes, null, excludedTargets);
            var plan = ModularAvatarMergePlanner.CreatePlan(
                source,
                target,
                binding.Definition.Prefix,
                binding.Definition.Suffix);
            if (!plan.CanApply)
                return plan.Conflicts[0].Message;
        }

        foreach (var binding in boneProxyBindings)
        {
            if (ResolveBoneProxyTarget(targetRig, binding.Definition, excludedTargets) == null)
            {
                if (mergeBindings.Count > 0)
                {
                    UnityPackageImporter.Warn("Optional Bone Proxy target was not found for '" + binding.SourceSlot.Name + "'. Outfit will still merge onto armature.");
                }
                else
                {
                    return "Bone Proxy target was not found for " + binding.SourceSlot.Name;
                }
            }
        }

        return null;
    }

    private static async Task<InstallationRecordHandle> InstallCopyAsync(
        Slot attachmentRoot,
        IReadOnlyList<MergeSourceBinding> mergeBindings,
        IReadOnlyList<BoneProxyBinding> boneProxyBindings,
        BipedRig targetRig,
        OutfitInstallIdentity identity,
        CancellationToken cancellationToken = default)
    {
        await default(ToWorld);
        cancellationToken.ThrowIfCancellationRequested();
        if (attachmentRoot.IsDestroyed || !IsOwnedAvatar(targetRig, attachmentRoot.World.LocalUser))
            throw new InvalidOperationException("The attachment or target avatar no longer exists.");

        var existingRecord = OutfitInstallationRecordStore.FindInstallationRecord(targetRig.Slot, identity);
        if (existingRecord?.Match == OutfitInstallMatch.Current)
        {
            if (!existingRecord.IsHealthy)
                throw new InvalidOperationException(
                    "This outfit has an incomplete installation record. " +
                    "The existing avatar state was left untouched.");
            cancellationToken.ThrowIfCancellationRequested();
            return existingRecord;
        }
        if (existingRecord?.Match == OutfitInstallMatch.UpdateAvailable)
            throw new InvalidOperationException(
                "A different revision of this outfit is already installed. " +
                "The existing installation was left untouched.");

        string targetError = GetTargetError(mergeBindings, boneProxyBindings, targetRig);
        if (targetError != null) throw new InvalidOperationException(targetError);

        Slot installedRoot = null;
        Slot recordRoot = null;
        var transaction = new OwnedAttachmentTransaction<Slot>();
        var ownedRoots = new List<OwnedRoot>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            installedRoot = attachmentRoot.Duplicate(targetRig.Slot, false);
            transaction.Track(installedRoot);
            installedRoot.Name = attachmentRoot.Name + " [Installed on " + targetRig.Slot.Name + "]";
            installedRoot.SetIdentityTransform();
            bool installedRootActive = installedRoot.ActiveSelf;
            var authoredActivity = EnumerateSelfAndChildren(installedRoot).ToDictionary(slot => slot,
                slot => IsActiveWithinOutfit(slot, installedRoot));
            installedRoot.ActiveSelf = false;
            ownedRoots.Add(new OwnedRoot
            {
                Slot = installedRoot,
                ActiveWhenEnabled = installedRootActive,
                Role = "Outfit Root"
            });

            var clonedBindings = mergeBindings.Select(binding => new MergeSourceBinding
            {
                Definition = binding.Definition,
                SourceSlot = ResolveIndexPath(installedRoot, binding.IndexPath)
                    ?? throw new InvalidOperationException(
                        "The duplicated Merge Armature source could not be resolved for '" +
                        binding.SourceSlot.Name + "'."),
                IndexPath = binding.IndexPath
            }).ToList();
            var clonedBoneProxies = boneProxyBindings.Select(binding =>
            {
                var source = ResolveIndexPath(installedRoot, binding.IndexPath)
                    ?? throw new InvalidOperationException(
                        "The duplicated Bone Proxy source could not be resolved for '" +
                        binding.SourceSlot.Name + "'.");
                // Preserve the authored pose in the target avatar's placement before
                // merges or an earlier proxy can change this source's hierarchy.
                return new BoneProxyBinding
                {
                    Definition = binding.Definition,
                    SourceSlot = source,
                    IndexPath = binding.IndexPath,
                    AuthoredGlobalPosition = source.GlobalPosition,
                    AuthoredGlobalRotation = source.GlobalRotation,
                    AuthoredGlobalScale = source.GlobalScale
                };
            }).ToList();

            // Resolve index paths before removing any siblings from the duplicate.
            var copiedFootTag = installedRoot.Children.FirstOrDefault(child => child.Name == "Foot Level Tag");
            copiedFootTag?.Destroy();
            RemoveCopiedInstaller(installedRoot);

            var nestedRoots = clonedBindings.Select(binding => binding.SourceSlot).ToHashSet();
            var excludedTargets = OutfitInstallationRecordStore.GetInstalledOutfitRoots(targetRig.Slot);
            excludedTargets.Add(installedRoot);
            var prepared = new List<PreparedMerge>();
            foreach (var binding in clonedBindings)
            {
                var targetRoot = ResolveTargetRoot(targetRig.Slot, binding, excludedTargets, targetRig)
                    ?? throw new InvalidOperationException(
                        "Target armature path '" + binding.Definition.MergeTargetPath +
                        "' is missing, ambiguous, or belongs to another installed outfit.");

                var sourceSlots = new Dictionary<string, Slot>();
                var targetSlots = new Dictionary<string, Slot>();
                var source = BuildHierarchy(binding.SourceSlot, nestedRoots, sourceSlots, binding.SourceSlot);
                var target = BuildHierarchy(targetRoot, new HashSet<Slot>(), targetSlots, null, excludedTargets);
                var plan = ModularAvatarMergePlanner.CreatePlan(
                    source,
                    target,
                    binding.Definition.Prefix,
                    binding.Definition.Suffix);
                if (!plan.CanApply)
                    throw new InvalidOperationException(plan.Conflicts[0].Message);

                prepared.Add(new PreparedMerge
                {
                    Binding = binding,
                    SourceRoot = binding.SourceSlot,
                    TargetRoot = targetRoot,
                    Plan = plan,
                    SourceSlots = sourceSlots,
                    TargetSlots = targetSlots
                });
            }

            var boneMap = new Dictionary<Slot, Slot>();
            foreach (var merge in prepared)
            {
                foreach (var mapping in merge.Plan.Mappings)
                {
                    var source = merge.SourceSlots[mapping.SourceId];
                    var target = merge.TargetSlots[mapping.TargetId];
                    if (boneMap.TryGetValue(source, out var existing) && existing != target)
                        throw new InvalidOperationException(
                            "Bone '" + source.Name + "' maps to more than one target.");
                    boneMap[source] = target;
                }
            }

            var renderers = EnumerateSelfAndChildren(installedRoot)
                .SelectMany(slot => slot.Components.OfType<SkinnedMeshRenderer>())
                .ToList();
            var ownedComponents = EnumerateSelfAndChildren(installedRoot)
                .SelectMany(slot => slot.Components)
                .Where(component => component is MeshRenderer || component is SkinnedMeshRenderer)
                .Select(component => new OwnedComponent
                {
                    Component = component,
                    EnabledWhenOutfitEnabled = component.Enabled,
                    Role = component is SkinnedMeshRenderer
                        ? "Skinned Mesh Renderer"
                        : "Mesh Renderer"
                })
                .ToList();
            foreach (var renderer in renderers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int i = 0; i < renderer.Bones.Count; i++)
                {
                    var bone = renderer.Bones[i];
                    if (bone != null && boneMap.TryGetValue(bone, out var target))
                        renderer.Bones[i] = target;
                }

                SkinnedBoundsPolicy.ApplyAfterBones(renderer, "Modular Avatar installation");
            }

            foreach (var merge in prepared)
            {
                foreach (var retained in merge.Plan.RetainedRoots)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var source = merge.SourceSlots[retained.SourceId];
                    if (boneMap.ContainsKey(source)) continue;
                    var targetParent = merge.TargetSlots[retained.TargetParentId];
                    bool activeWhenEnabled = authoredActivity[source];
                    transaction.Track(source);
                    source.ActiveSelf = false;
                    source.SetParent(targetParent, false);
                    ownedRoots.Add(new OwnedRoot
                    {
                        Slot = source,
                        ActiveWhenEnabled = activeWhenEnabled,
                        Role = "Retained Bone Root"
                    });
                }
            }

            foreach (var proxy in clonedBoneProxies)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = ResolveBoneProxyTarget(targetRig, proxy.Definition, excludedTargets);
                if (target == null)
                {
                    UnityPackageImporter.Warn("Bone Proxy target was not found for '" + proxy.SourceSlot.Name + "'. Leaving in outfit hierarchy.");
                    continue;
                }
                bool activeWhenEnabled = authoredActivity[proxy.SourceSlot];
                transaction.Track(proxy.SourceSlot);
                ApplyBoneProxy(proxy.SourceSlot, target, proxy.Definition,
                    proxy.AuthoredGlobalPosition, proxy.AuthoredGlobalRotation, proxy.AuthoredGlobalScale);
                proxy.SourceSlot.ActiveSelf = false;
                if (proxy.SourceSlot != installedRoot)
                {
                    ownedRoots.Add(new OwnedRoot
                    {
                        Slot = proxy.SourceSlot,
                        ActiveWhenEnabled = activeWhenEnabled,
                        Role = "Bone Proxy Root"
                    });
                }
            }

            await SkinnedBoundsPolicy.StabilizeBeforeEnableAsync(
                renderers,
                "Modular Avatar activation", cancellationToken);

            await default(ToWorld);
            cancellationToken.ThrowIfCancellationRequested();
            if (attachmentRoot.IsDestroyed || installedRoot.IsDestroyed ||
                !IsOwnedAvatar(targetRig, attachmentRoot.World.LocalUser))
                throw new InvalidOperationException("The attachment or target avatar is no longer available.");
            var record = OutfitInstallationRecordStore.CreateInstallationRecord(
                targetRig,
                attachmentRoot.Name,
                installedRoot,
                identity,
                ownedRoots,
                ownedComponents,
                transaction,
                out recordRoot);
            ImportSession.PreserveAssetsForOutput(attachmentRoot);
            transaction.Commit();
            return record;
        }
        catch
        {
            await default(ToWorld);
            foreach (var cleanupError in transaction.RollBack(slot =>
                         { if (!slot.IsDestroyed) slot.Destroy(); }))
                UnityPackageImporter.Warn("Could not fully roll back outfit installation: " + cleanupError.Message);
            throw;
        }
    }

    private static void RemoveCopiedInstaller(Slot installedRoot)
    {
        var copiedMenu = installedRoot.Children.FirstOrDefault(child => child.Name == InstallerSlotName);
        foreach (var component in installedRoot.Components.ToArray())
        {
            if (component is RootContextMenuItem rootItem &&
                rootItem.Item.Target != null &&
                rootItem.Item.Target.Slot == copiedMenu)
            {
                rootItem.Destroy();
            }
        }
        copiedMenu?.Destroy();
    }

    private static Slot ResolveBoneProxyTarget(
        BipedRig targetRig,
        ModularAvatarComponentDefinition definition,
        HashSet<Slot> excludedRoots)
    {
        if (targetRig == null || targetRig.IsDestroyed) return null;

        string subPath = definition.BoneProxySubPath?.Trim() ?? "";
        Slot target;
        if (subPath == "$$AVATAR")
        {
            target = targetRig.Slot;
            subPath = "";
        }
        else if (definition.BoneReference == 55) // UnityEngine.HumanBodyBones.LastBone
        {
            if (string.IsNullOrWhiteSpace(subPath)) return null;
            target = targetRig.Slot;
        }
        else
        {
            BodyNode bodyNode = MapUnityHumanoidBone(definition.BoneReference);
            if (bodyNode == BodyNode.NONE) return null;
            target = targetRig.TryGetBone(bodyNode);
            if (target == null || target.IsDestroyed)
            {
                target = ResolveHumanoidBoneFallback(targetRig, bodyNode);
            }
        }

        if (target == null || target.IsDestroyed) return null;
        foreach (string part in SplitPath(subPath))
        {
            target = target.Children.FirstOrDefault(child =>
                child.Name.Equals(part, StringComparison.Ordinal))
                ?? target.Children.FirstOrDefault(child =>
                child.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
            if (target == null || target.IsDestroyed) return null;
        }
        if (excludedRoots.Any(root => target == root || target.IsChildOf(root, true))) return null;
        return target;
    }

    private static Slot ResolveHumanoidBoneFallback(BipedRig rig, BodyNode node)
    {
        if (rig == null || rig.IsDestroyed) return null;
        return node switch
        {
            BodyNode.UpperChest => rig.TryGetBone(BodyNode.Chest) ?? rig.TryGetBone(BodyNode.Spine),
            BodyNode.Chest => rig.TryGetBone(BodyNode.Spine),
            BodyNode.LeftToes => rig.TryGetBone(BodyNode.LeftFoot),
            BodyNode.RightToes => rig.TryGetBone(BodyNode.RightFoot),
            BodyNode.LeftEye or BodyNode.RightEye or BodyNode.Jaw => rig.TryGetBone(BodyNode.Head),
            BodyNode.LeftThumb_Distal or BodyNode.LeftThumb_Tip => rig.TryGetBone(BodyNode.LeftThumb_Proximal),
            BodyNode.RightThumb_Distal or BodyNode.RightThumb_Tip => rig.TryGetBone(BodyNode.RightThumb_Proximal),
            _ => null
        };
    }

    private static void ApplyBoneProxy(
        Slot source,
        Slot target,
        ModularAvatarComponentDefinition definition,
        float3 originalPosition,
        floatQ originalRotation,
        float3 originalScale)
    {
        if (source == null || source.IsDestroyed || target == null || target.IsDestroyed)
            throw new InvalidOperationException("The Bone Proxy source or target no longer exists.");
        if (target == source || target.IsChildOf(source, true))
            throw new InvalidOperationException("A Bone Proxy cannot be parented underneath itself.");

        switch (definition.BoneProxyAttachmentMode)
        {
            case 2: // AsChildKeepWorldPose
                source.SetParent(target, false);
                source.GlobalPosition = originalPosition;
                source.GlobalRotation = originalRotation;
                break;
            case 3: // AsChildKeepRotation
                source.SetParent(target, false);
                source.GlobalPosition = target.GlobalPosition;
                source.GlobalRotation = originalRotation;
                break;
            case 4: // AsChildKeepPosition
                source.SetParent(target, false);
                source.GlobalPosition = originalPosition;
                source.GlobalRotation = target.GlobalRotation;
                break;
            default: // Unset and AsChildAtRoot
                source.SetParent(target, false);
                source.LocalPosition = new float3(0f, 0f, 0f);
                source.LocalRotation = new floatQ(0f, 0f, 0f, 1f);
                break;
        }

        if (definition.BoneProxyMatchScale)
            source.LocalScale = new float3(1f, 1f, 1f);
        else
            source.GlobalScale = originalScale;
    }

    internal static BodyNode MapUnityHumanoidBone(int boneReference)
    {
        // UnityEngine.HumanBodyBones numeric values are serialized into prefab
        // YAML. Keep this mapping explicit so importing does not require Unity.
        return boneReference switch
        {
            0 => BodyNode.Hips,
            1 => BodyNode.LeftUpperLeg,
            2 => BodyNode.RightUpperLeg,
            3 => BodyNode.LeftLowerLeg,
            4 => BodyNode.RightLowerLeg,
            5 => BodyNode.LeftFoot,
            6 => BodyNode.RightFoot,
            7 => BodyNode.Spine,
            8 => BodyNode.Chest,
            9 => BodyNode.Neck,
            10 => BodyNode.Head,
            11 => BodyNode.LeftShoulder,
            12 => BodyNode.RightShoulder,
            13 => BodyNode.LeftUpperArm,
            14 => BodyNode.RightUpperArm,
            15 => BodyNode.LeftLowerArm,
            16 => BodyNode.RightLowerArm,
            17 => BodyNode.LeftHand,
            18 => BodyNode.RightHand,
            19 => BodyNode.LeftToes,
            20 => BodyNode.RightToes,
            21 => BodyNode.LeftEye,
            22 => BodyNode.RightEye,
            23 => BodyNode.Jaw,
            24 => BodyNode.LeftThumb_Proximal,
            25 => BodyNode.LeftThumb_Distal,
            26 => BodyNode.LeftThumb_Tip,
            27 => BodyNode.LeftIndexFinger_Proximal,
            28 => BodyNode.LeftIndexFinger_Intermediate,
            29 => BodyNode.LeftIndexFinger_Distal,
            30 => BodyNode.LeftMiddleFinger_Proximal,
            31 => BodyNode.LeftMiddleFinger_Intermediate,
            32 => BodyNode.LeftMiddleFinger_Distal,
            33 => BodyNode.LeftRingFinger_Proximal,
            34 => BodyNode.LeftRingFinger_Intermediate,
            35 => BodyNode.LeftRingFinger_Distal,
            36 => BodyNode.LeftPinky_Proximal,
            37 => BodyNode.LeftPinky_Intermediate,
            38 => BodyNode.LeftPinky_Distal,
            39 => BodyNode.RightThumb_Proximal,
            40 => BodyNode.RightThumb_Distal,
            41 => BodyNode.RightThumb_Tip,
            42 => BodyNode.RightIndexFinger_Proximal,
            43 => BodyNode.RightIndexFinger_Intermediate,
            44 => BodyNode.RightIndexFinger_Distal,
            45 => BodyNode.RightMiddleFinger_Proximal,
            46 => BodyNode.RightMiddleFinger_Intermediate,
            47 => BodyNode.RightMiddleFinger_Distal,
            48 => BodyNode.RightRingFinger_Proximal,
            49 => BodyNode.RightRingFinger_Intermediate,
            50 => BodyNode.RightRingFinger_Distal,
            51 => BodyNode.RightPinky_Proximal,
            52 => BodyNode.RightPinky_Intermediate,
            53 => BodyNode.RightPinky_Distal,
            54 => BodyNode.UpperChest,
            _ => BodyNode.NONE
        };
    }

    private static bool IsLikelyArmature(Slot s)
    {
        if (s == null || s.IsDestroyed) return false;
        if (s.Name.Equals("Armature", StringComparison.OrdinalIgnoreCase)) return true;
        return s.Children.Any(c => c.Name.Equals("Hips", StringComparison.OrdinalIgnoreCase) ||
                                   c.Name.Equals("Pelvis", StringComparison.OrdinalIgnoreCase));
    }

    private static Slot ResolveTargetRoot(Slot avatarRoot, MergeSourceBinding binding, HashSet<Slot> excludedRoots, BipedRig targetRig = null)
    {
        var candidates = new Dictionary<string, Slot>();
        BuildHierarchy(avatarRoot, new HashSet<Slot>(), candidates, null, excludedRoots);
        var target = ModularAvatarMergePlanner.ResolveTarget(avatarRoot, candidates.Values,
            binding.Definition.MergeTargetPath, binding.SourceSlot.Name, binding.Definition.Prefix, binding.Definition.Suffix,
            slot => slot.Name, slot => RelativePath(slot, avatarRoot));
        if (target != null) return target;

        var hipsBone = targetRig?.TryGetBone(BodyNode.Hips);
        var rigArmature = hipsBone?.Parent != null && hipsBone.Parent != avatarRoot
            ? hipsBone.Parent
            : null;

        if (rigArmature != null && (excludedRoots == null || !excludedRoots.Contains(rigArmature)))
        {
            string targetPath = binding.Definition.MergeTargetPath?.Trim() ?? "";
            string targetLeaf = targetPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();

            if (IsLikelyArmature(binding.SourceSlot) ||
                string.IsNullOrEmpty(targetPath) ||
                (targetLeaf != null && rigArmature.Name.Equals(targetLeaf, StringComparison.OrdinalIgnoreCase)) ||
                rigArmature.Name.Equals("Armature", StringComparison.OrdinalIgnoreCase))
            {
                return rigArmature;
            }
        }

        string explicitTargetLeaf = binding.Definition.MergeTargetPath?.Trim().Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (!string.IsNullOrEmpty(explicitTargetLeaf))
        {
            var matchingCandidate = candidates.Values.FirstOrDefault(c =>
                c.Name.Equals(explicitTargetLeaf, StringComparison.OrdinalIgnoreCase) &&
                (excludedRoots == null || !excludedRoots.Contains(c)));
            if (matchingCandidate != null) return matchingCandidate;
        }

        return null;
    }

    private static MergeHierarchyNode BuildHierarchy(
        Slot slot,
        HashSet<Slot> nestedMergeRoots,
        Dictionary<string, Slot> nodes,
        Slot currentMergeRoot,
        HashSet<Slot> excludedRoots = null) =>
        BoneAttachmentHierarchy.Snapshot(slot, node => node.Children,
            node => (node.ReferenceID.ToString(), node.Name), nodes,
            excludedRoots, currentMergeRoot == null ? null : nestedMergeRoots);

    private static IEnumerable<Slot> EnumerateSelfAndChildren(Slot root)
    {
        yield return root;
        foreach (var child in root.GetAllChildren(false)) yield return child;
    }

    private static int[] GetIndexPath(Slot root, Slot descendant)
    {
        var path = new List<int>();
        var current = descendant;
        while (current != null && current != root)
        {
            path.Add(current.ChildIndex);
            current = current.Parent;
        }

        if (current != root) return null;
        path.Reverse();
        return path.ToArray();
    }

    private static Slot ResolveIndexPath(Slot root, IReadOnlyList<int> path)
    {
        var current = root;
        foreach (int index in path)
        {
            if (index < 0 || index >= current.ChildrenCount) return null;
            current = current[index];
        }
        return current;
    }

    private static string[] SplitPath(string path) =>
        (path ?? "")
        .Replace('\\', '/')
        .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

    private static bool IsActiveWithinOutfit(Slot slot, Slot outfitRoot)
    {
        for (var current = slot; current != null; current = current.Parent)
        {
            if (!current.ActiveSelf) return false;
            if (current == outfitRoot) return true;
        }
        return false;
    }

    private static string RelativePath(Slot candidate, Slot root)
    {
        var names = new List<string>();
        var current = candidate;
        while (current != null && current != root)
        {
            names.Add(current.Name);
            current = current.Parent;
        }
        names.Reverse();
        return string.Join("/", names);
    }
}
