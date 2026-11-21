#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FrooxEngine;
using FrooxEngine.CommonAvatar;
using UnityPackageImporter.FrooxEngineRepresentation;
using UnityGameObject = UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes.GameObject;
using UnitySkin = UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes.SkinnedMeshRenderer;

namespace UnityPackageImporter.Models;

internal static class AvatarWardrobeRuntime
{
    internal static async Task<bool> BuildAsync(AvatarWardrobePlan wardrobe, Slot avatarRoot,
        IReadOnlyDictionary<ulong, IUnityObject> objects, UnityProjectImporter project,
        CancellationToken token, AuthoredAppearanceSnapshot? authoredSnapshot = null)
    {
        ArgumentNullException.ThrowIfNull(wardrobe);
        if (!wardrobe.CanBuild) return false;
        var graph = wardrobe.SharedState!;
        var parameters = graph.Parameters.CreateState();
        var initialWrites = graph.Evaluate(parameters, token);
        await default(ToWorld);
        token.ThrowIfCancellationRequested();
        if (avatarRoot.IsDestroyed || avatarRoot.GetComponentInParents<AvatarRoot>() == null)
            throw new InvalidOperationException("Wardrobe controls require completed native avatar setup.");

        var pending = new List<(Action Apply, Action Restore, Func<bool> Valid)>();
        var destinations = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var materialTasks = new Dictionary<(string Guid, long Id), FileImportHelperTaskMaterial>();
        var owned = new OwnedAttachmentTransaction<Slot>();
        RootContextMenuItem? rootLink = null;
        bool bindingsCommitted = false;
        try
        {
            async Task<FileImportHelperTaskMaterial> ResolveMaterialAsync(InitialAppearanceMaterial? material)
            {
                if (material == null || material.FileId == 0 || !Regex.IsMatch(material.Guid ?? "", @"\A[0-9a-fA-F]{32}\z") ||
                    !project.AssetIDDict.TryGetValue(material.Guid, out string? path) ||
                    !string.Equals(Path.GetExtension(path), ".mat", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                    throw new InvalidOperationException("Wardrobe material reference cannot be resolved from exact package assets.");
                string normalizedGuid = material.Guid.ToLowerInvariant();
                if (materialTasks.TryGetValue((normalizedGuid, material.FileId), out var existing))
                    return existing;
                await default(ToBackground);
                string yaml = await File.ReadAllTextAsync(path, token);
                if (!ModularAvatarInitialAppearance.IsExactMaterialAsset(yaml, material.FileId))
                    throw new InvalidOperationException("Wardrobe material fileID does not match an exact Material asset.");
                UnityMaterialData.Parse(yaml);
                await default(ToWorld);
                token.ThrowIfCancellationRequested();
                if (!materialTasks.TryGetValue((normalizedGuid, material.FileId), out existing))
                {
                    existing = new FileImportHelperTaskMaterial(material.Guid, path, project);
                    var provider = await existing.runImportFileMaterialsAsync();
                    await default(ToWorld);
                    token.ThrowIfCancellationRequested();
                    if (provider is not Component component || component.IsDestroyed ||
                        !PreparedImportItem.Contains(project.importTaskAssetRoot, component.Slot))
                        throw new InvalidOperationException("Wardrobe material failed to produce an owned Resonite Material provider.");
                    materialTasks.Add((normalizedGuid, material.FileId), existing);
                }
                return existing;
            }

            var controlsRoot = avatarRoot.AddSlot("Wardrobe controls");
            owned.Track(controlsRoot);
            var source = controlsRoot.AttachComponent<ContextMenuItemSource>();
            source.Label.Value = "Wardrobe";
            var rootItems = controlsRoot.AddSlot("Items");
            controlsRoot.AttachComponent<ContextMenuSubmenu>().ItemsRoot.Target = rootItems;
            var groups = new Dictionary<string, Slot>(StringComparer.Ordinal) { [""] = rootItems };
            var keys = new HashSet<(InitialAppearanceEffect, long, string)>();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var control in wardrobe.Controls)
            {
                token.ThrowIfCancellationRequested();
                if (!graph.IndependentControls!.TryGetValue(control.Id, out var parameter))
                    throw new InvalidOperationException("Wardrobe control has no exact shared parameter.");
                bool defaultOn = parameters[parameter] == 1;
                if (!identities.Add(control.Id) || !paths.Add(control.Path) || groups.ContainsKey(control.Path) || control.Bindings.Count == 0)
                    throw new InvalidOperationException("Wardrobe contains duplicate or empty control bindings.");
                string[] parts = control.Path.Split('/');
                if (parts.Any(part => string.IsNullOrWhiteSpace(part) || part is "." or ".."))
                    throw new InvalidOperationException("Wardrobe has an invalid control path.");
                Slot parent = rootItems;
                string groupPath = "";
                foreach (string part in parts.SkipLast(1))
                {
                    groupPath = groupPath.Length == 0 ? part : groupPath + "/" + part;
                    if (paths.Contains(groupPath))
                        throw new InvalidOperationException("Wardrobe toggle path collides with a submenu.");
                    if (!groups.TryGetValue(groupPath, out var items))
                    {
                        var group = parent.AddSlot(part);
                        group.AttachComponent<ContextMenuItemSource>().Label.Value = part;
                        items = group.AddSlot("Items");
                        group.AttachComponent<ContextMenuSubmenu>().ItemsRoot.Target = items;
                        groups.Add(groupPath, items);
                    }
                    parent = items;
                }
                var itemSlot = parent.AddSlot(parts[^1]);
                var item = itemSlot.AttachComponent<ContextMenuItemSource>();
                item.Label.Value = parts[^1];
                item.ButtonEnabled.Value = true;
                var state = itemSlot.AttachComponent<ValueMultiDriver<bool>>();
                state.Value.Value = defaultOn;
                var authoredDefault = itemSlot.AddSlot("Authored default").AttachComponent<ValueField<bool>>();
                authoredDefault.Value.Value = defaultOn;
                itemSlot.AttachComponent<ButtonToggle>().TargetValue.Target = state.Value;
                if (!string.IsNullOrEmpty(control.IconGuid))
                    await ContextMenuBuilder.AttachMenuIconAsync(itemSlot, item, control.IconGuid, project);
                await default(ToWorld);
                token.ThrowIfCancellationRequested();
                foreach (var binding in control.Bindings)
                {
                    if (!initialWrites.TryGetValue(AvatarPropertyKey.Of(binding.On), out var initial) ||
                        initial != (defaultOn ? binding.On : binding.Off))
                        throw new InvalidOperationException("Wardrobe native projection does not match its shared initial state.");
                    var off = binding.Off;
                    var on = binding.On;
                    if (off.Effect != on.Effect || off.TargetId != on.TargetId || off.Property != on.Property ||
                        !keys.Add((on.Effect, on.TargetId, on.Property)))
                        throw new InvalidOperationException("Wardrobe bindings have ambiguous field ownership.");
                    bool hasSource = objects.TryGetValue(unchecked((ulong)on.TargetId), out var entry);
                    if (!hasSource && on.Effect != InitialAppearanceEffect.Active)
                        throw new InvalidOperationException("Wardrobe destination no longer exists.");
                    if (on.Effect == InitialAppearanceEffect.Active)
                    {
                        Slot? target = entry is UnityGameObject game ? game.frooxEngineSlot : null;
                        if (authoredSnapshot != null)
                        {
                            if (!authoredSnapshot.TryGetSlot(on.TargetId, out var captured) || hasSource && target != captured)
                                throw new InvalidOperationException("Wardrobe visibility target disagrees with its captured source instance.");
                            target = captured;
                        }
                        if (target == null || target.IsDestroyed || target == avatarRoot || !PreparedImportItem.Contains(avatarRoot, target))
                            throw new InvalidOperationException("Wardrobe visibility target is not an owned child object.");
                        var field = target.ActiveSelf_Field;
                        if (!destinations.Add(field) || field.IsDriven || field.IsLinked)
                            throw new InvalidOperationException("Wardrobe visibility target is already driven.");
                        bool before = target.ActiveSelf;
                        BooleanValueDriver<bool>? driver = null;
                        pending.Add((() =>
                        {
                            driver = itemSlot.AttachComponent<BooleanValueDriver<bool>>();
                            driver.FalseValue.Value = off.Active;
                            driver.TrueValue.Value = on.Active;
                            driver.State.Value = defaultOn;
                            state.Drives.Add().ForceLink(driver.State);
                            driver.TargetField.ForceLink(field);
                        }, () =>
                        {
                            if (driver != null && !driver.IsDestroyed) driver.Destroy();
                            field.Value = before;
                        }, () => !target.IsDestroyed && PreparedImportItem.Contains(avatarRoot, target) &&
                            target.ActiveSelf == before && !field.IsLinked && !field.IsDriven));
                    }
                    else if (on.Effect == InitialAppearanceEffect.Shape)
                    {
                        if (entry is not UnitySkin skin || skin.IsStaticMesh || skin.createdMeshRenderer == null ||
                            skin.createdMeshRenderer.IsDestroyed || skin.m_GameObject == null ||
                            !skin.m_GameObject.TryGetValue("fileID", out ulong ownerId) ||
                            !objects.TryGetValue(ownerId, out var owner) || owner is not UnityGameObject ownerGame ||
                            !PreparedImportItem.Contains(avatarRoot, ownerGame.frooxEngineSlot) ||
                            !PreparedImportItem.Contains(ownerGame.frooxEngineSlot, skin.createdMeshRenderer.Slot) ||
                            !float.IsFinite(off.Weight) || !float.IsFinite(on.Weight))
                            throw new InvalidOperationException("Wardrobe blendshape target is not an owned skinned renderer.");
                        var renderer = skin.createdMeshRenderer;
                        int index = renderer.BlendShapeIndex(on.Property);
                        if (index < 0 || index >= renderer.BlendShapeWeights.Count)
                            throw new InvalidOperationException("Wardrobe blendshape has no exact native destination.");
                        var field = renderer.BlendShapeWeights.GetElement(index);
                        if (!destinations.Add(field) || field.IsDriven || field.IsLinked || !float.IsFinite(field.Value))
                            throw new InvalidOperationException("Wardrobe blendshape target is already driven or invalid.");
                        float before = field.Value;
                        BooleanValueDriver<float>? driver = null;
                        pending.Add((() =>
                        {
                            driver = itemSlot.AttachComponent<BooleanValueDriver<float>>();
                            driver.FalseValue.Value = off.Weight / 100f;
                            driver.TrueValue.Value = on.Weight / 100f;
                            driver.State.Value = defaultOn;
                            state.Drives.Add().ForceLink(driver.State);
                            driver.TargetField.ForceLink(field);
                        }, () =>
                        {
                            if (driver != null && !driver.IsDestroyed) driver.Destroy();
                            field.Value = before;
                        }, () => !renderer.IsDestroyed && PreparedImportItem.Contains(avatarRoot, renderer.Slot) &&
                            renderer.BlendShapeIndex(on.Property) == index && index < renderer.BlendShapeWeights.Count &&
                            ReferenceEquals(renderer.BlendShapeWeights.GetElement(index), field) &&
                            field.Value == before && !field.IsLinked && !field.IsDriven));
                    }
                    else if (on.Effect == InitialAppearanceEffect.Material)
                    {
                        if (entry is not UnitySkin skin || skin.createdMeshRenderer == null ||
                            skin.createdMeshRenderer.IsDestroyed || skin.m_GameObject == null ||
                            !skin.m_GameObject.TryGetValue("fileID", out ulong ownerId) ||
                            !objects.TryGetValue(ownerId, out var owner) || owner is not UnityGameObject ownerGame ||
                            !PreparedImportItem.Contains(avatarRoot, ownerGame.frooxEngineSlot) ||
                            !PreparedImportItem.Contains(ownerGame.frooxEngineSlot, skin.createdMeshRenderer.Slot) ||
                            !int.TryParse(on.Property, NumberStyles.None, CultureInfo.InvariantCulture, out int index) ||
                            index < 0 || index >= skin.createdMeshRenderer.Materials.Count)
                            throw new InvalidOperationException("Wardrobe material target is not an owned renderer material slot.");
                        var offTask = await ResolveMaterialAsync(off.Material);
                        var onTask = await ResolveMaterialAsync(on.Material);
                        var renderer = skin.createdMeshRenderer;
                        if (renderer.IsDestroyed || index >= renderer.Materials.Count)
                            throw new InvalidOperationException("Wardrobe material slot no longer exists.");
                        var field = renderer.Materials.GetElement(index);
                        if (!destinations.Add(field) || field.IsDriven || field.IsLinked)
                            throw new InvalidOperationException("Wardrobe material target is already driven.");
                        var before = field.Target;
                        BooleanReferenceDriver<IAssetProvider<Material>>? driver = null;
                        pending.Add((() =>
                        {
                            driver = itemSlot.AttachComponent<BooleanReferenceDriver<IAssetProvider<Material>>>();
                            driver.FalseTarget.Target = offTask.finalMaterial;
                            driver.TrueTarget.Target = onTask.finalMaterial;
                            driver.State.Value = defaultOn;
                            state.Drives.Add().ForceLink(driver.State);
                            driver.TargetReference.ForceLink(field);
                        }, () =>
                        {
                            if (driver != null && !driver.IsDestroyed) driver.Destroy();
                            field.Target = before;
                        }, () => !renderer.IsDestroyed && PreparedImportItem.Contains(avatarRoot, renderer.Slot) &&
                            index < renderer.Materials.Count && ReferenceEquals(renderer.Materials.GetElement(index), field) &&
                            ReferenceEquals(field.Target, before) && !field.IsLinked && !field.IsDriven &&
                            offTask.finalMaterial is Component offComp && !offComp.IsDestroyed &&
                            onTask.finalMaterial is Component onComp && !onComp.IsDestroyed));
                    }
                    else throw new InvalidOperationException("Wardrobe effect has no native runtime adapter.");
                }
            }
            InitialAppearanceTransaction.Apply(pending, token);
            bindingsCommitted = true;
            token.ThrowIfCancellationRequested();
            rootLink = avatarRoot.AttachComponent<RootContextMenuItem>();
            rootLink.Item.Target = source;
            owned.Commit();
            return true;
        }
        catch (Exception failure)
        {
            var failures = new List<Exception> { failure };
            if (rootLink != null && !rootLink.IsDestroyed)
                try { rootLink.Destroy(); }
                catch (Exception cleanup) { failures.Add(cleanup); }
            failures.AddRange(owned.RollBack(slot => { if (!slot.IsDestroyed) slot.Destroy(); }));
            if (bindingsCommitted)
                for (int index = pending.Count - 1; index >= 0; index--)
                    try { pending[index].Restore(); }
                    catch (Exception restore) { failures.Add(restore); }
            if (failures.Count > 1) throw new AggregateException(failures);
            throw;
        }
    }
}
