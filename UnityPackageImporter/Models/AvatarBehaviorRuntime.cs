#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.CommonAvatar;
using Renderite.Shared;
using UnityPackageImporter.FrooxEngineRepresentation;
using UnityGameObject = UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes.GameObject;
using UnitySkin = UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes.SkinnedMeshRenderer;

namespace UnityPackageImporter.Models;

public static class AvatarBehaviorRuntime
{
    public static async Task BuildAsync(
        AvatarBehaviorPlan plan,
        Slot avatarRoot,
        IReadOnlyDictionary<ulong, IUnityObject> unityObjects,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(avatarRoot);
        token.ThrowIfCancellationRequested();

        if (!plan.CanBuild || (plan.BreathingBindings.Count == 0 && plan.BlinkingBindings.Count == 0 &&
            plan.TalkingBindings.Count == 0 && plan.VisemeBindings.Count == 0 && !plan.BlinkSuppressed &&
            plan.MeshSettingsBindings.Count == 0 && plan.VisibleInFirstPersonObjectIds.Count == 0 &&
            !plan.FloorAdjustmentY.HasValue))
            return;

        await default(ToWorld);
        token.ThrowIfCancellationRequested();

        if (avatarRoot.IsDestroyed || avatarRoot.GetComponentInParents<AvatarRoot>() == null)
            return;

        // 1. Process Blinking
        if (plan.BlinkingBindings.Count > 0)
        {
            var eyeLinearDrivers = avatarRoot.GetComponentsInChildren<EyeLinearDriver>();
            EyeLinearDriver? linearDriver = eyeLinearDrivers.Count > 0 ? eyeLinearDrivers[0] : null;

            foreach (var blink in plan.BlinkingBindings)
            {
                token.ThrowIfCancellationRequested();
                UnitySkin? skin = null;
                if (blink.TargetRendererFileId != 0 &&
                    unityObjects.TryGetValue(unchecked((ulong)blink.TargetRendererFileId), out var obj) &&
                    obj is UnitySkin uSkin)
                {
                    skin = uSkin;
                }
                else
                {
                    foreach (var uObj in unityObjects.Values)
                    {
                        if (uObj is UnitySkin candidate && candidate.createdMeshRenderer != null &&
                            !candidate.createdMeshRenderer.IsDestroyed &&
                            PreparedImportItem.Contains(avatarRoot, candidate.createdMeshRenderer.Slot) &&
                            candidate.createdMeshRenderer.BlendShapeIndex(blink.TargetBlendShape) >= 0)
                        {
                            skin = candidate;
                            break;
                        }
                    }
                }

                if (skin?.createdMeshRenderer != null && !skin.createdMeshRenderer.IsDestroyed)
                {
                    int shapeIdx = skin.createdMeshRenderer.BlendShapeIndex(blink.TargetBlendShape);
                    if (shapeIdx >= 0 && shapeIdx < skin.createdMeshRenderer.BlendShapeWeights.Count)
                    {
                        var field = skin.createdMeshRenderer.BlendShapeWeights.GetElement(shapeIdx);
                        if (linearDriver != null && linearDriver.EyeManager.Target == null)
                        {
                            var manager = avatarRoot.GetComponentInChildren<EyeManager>();
                            if (manager != null) linearDriver.EyeManager.Target = manager;
                        }
                    }
                }
            }
        }

        // 2. Process Breathing
        if (plan.BreathingBindings.Count > 0)
        {
            Slot? breathingRoot = null;
            foreach (var breathing in plan.BreathingBindings)
            {
                token.ThrowIfCancellationRequested();
                if (breathing.Mode == AvatarBreathingTargetMode.BlendShape)
                {
                    UnitySkin? skin = null;
                    if (breathing.TargetRendererFileId != 0 &&
                        unityObjects.TryGetValue(unchecked((ulong)breathing.TargetRendererFileId), out var obj) &&
                        obj is UnitySkin uSkin)
                    {
                        skin = uSkin;
                    }
                    else
                    {
                        foreach (var uObj in unityObjects.Values)
                        {
                            if (uObj is UnitySkin candidate && candidate.createdMeshRenderer != null &&
                                !candidate.createdMeshRenderer.IsDestroyed &&
                                PreparedImportItem.Contains(avatarRoot, candidate.createdMeshRenderer.Slot) &&
                                candidate.createdMeshRenderer.BlendShapeIndex(breathing.TargetBlendShape) >= 0)
                            {
                                skin = candidate;
                                break;
                            }
                        }
                    }

                    if (skin?.createdMeshRenderer != null && !skin.createdMeshRenderer.IsDestroyed)
                    {
                        int shapeIdx = skin.createdMeshRenderer.BlendShapeIndex(breathing.TargetBlendShape);
                        if (shapeIdx >= 0 && shapeIdx < skin.createdMeshRenderer.BlendShapeWeights.Count)
                        {
                            var field = skin.createdMeshRenderer.BlendShapeWeights.GetElement(shapeIdx);
                            if (!field.IsDriven && !field.IsLinked)
                            {
                                breathingRoot ??= avatarRoot.AddSlot("Breathing Driver");
                                var driver = breathingRoot.AttachComponent<Wiggler<float>>();
                                driver.Speed.Value = 1f / Math.Max(0.5f, breathing.PeriodSeconds);
                                driver.Offset.Value = (breathing.MinWeight + breathing.MaxWeight) * 0.5f;
                                driver.Magnitude.Value = (breathing.MaxWeight - breathing.MinWeight) * 0.5f;
                                driver.Target.ForceLink(field);
                            }
                        }
                    }
                }
                else if (breathing.Mode == AvatarBreathingTargetMode.BoneScale)
                {
                    Slot? boneSlot = null;
                    if (breathing.TargetObjectFileId != 0 &&
                        unityObjects.TryGetValue(unchecked((ulong)breathing.TargetObjectFileId), out var obj) &&
                        obj is UnityGameObject uGo && uGo.frooxEngineSlot != null && !uGo.frooxEngineSlot.IsDestroyed)
                    {
                        boneSlot = uGo.frooxEngineSlot;
                    }

                    if (boneSlot != null && PreparedImportItem.Contains(avatarRoot, boneSlot))
                    {
                        breathingRoot ??= avatarRoot.AddSlot("Breathing Driver");
                        var driver = breathingRoot.AttachComponent<Wiggler<float3>>();
                        driver.Speed.Value = 1f / Math.Max(0.5f, breathing.PeriodSeconds);
                        driver.Offset.Value = new float3(1.025f, 1.025f, 1.025f);
                        driver.Magnitude.Value = new float3(0.025f, 0.025f, 0.025f);
                        driver.Target.ForceLink(boneSlot.Scale_Field);
                    }
                }
            }
        }

        // 3. Process Talking
        if (plan.TalkingBindings.Count > 0)
        {
            Slot? talkingRoot = null;
            foreach (var talking in plan.TalkingBindings)
            {
                token.ThrowIfCancellationRequested();
                if (!string.IsNullOrEmpty(talking.TargetBlendShape))
                {
                    UnitySkin? skin = null;
                    if (talking.TargetRendererFileId != 0 &&
                        unityObjects.TryGetValue(unchecked((ulong)talking.TargetRendererFileId), out var obj) &&
                        obj is UnitySkin uSkin)
                    {
                        skin = uSkin;
                    }
                    else
                    {
                        foreach (var uObj in unityObjects.Values)
                        {
                            if (uObj is UnitySkin candidate && candidate.createdMeshRenderer != null &&
                                !candidate.createdMeshRenderer.IsDestroyed &&
                                PreparedImportItem.Contains(avatarRoot, candidate.createdMeshRenderer.Slot) &&
                                candidate.createdMeshRenderer.BlendShapeIndex(talking.TargetBlendShape) >= 0)
                            {
                                skin = candidate;
                                break;
                            }
                        }
                    }

                    if (skin?.createdMeshRenderer != null && !skin.createdMeshRenderer.IsDestroyed)
                    {
                        int shapeIdx = skin.createdMeshRenderer.BlendShapeIndex(talking.TargetBlendShape);
                        if (shapeIdx >= 0 && shapeIdx < skin.createdMeshRenderer.BlendShapeWeights.Count)
                        {
                            var field = skin.createdMeshRenderer.BlendShapeWeights.GetElement(shapeIdx);
                            if (!field.IsDriven && !field.IsLinked)
                            {
                                talkingRoot ??= avatarRoot.AddSlot("Talking Driver");
                                var driver = talkingRoot.AttachComponent<ValueCopy<float>>();
                                driver.Target.ForceLink(field);
                            }
                        }
                    }
                }
                else if (talking.TargetObjectFileId != 0)
                {
                    if (unityObjects.TryGetValue(unchecked((ulong)talking.TargetObjectFileId), out var obj) &&
                        obj is UnityGameObject uGo && uGo.frooxEngineSlot != null && !uGo.frooxEngineSlot.IsDestroyed &&
                        PreparedImportItem.Contains(avatarRoot, uGo.frooxEngineSlot))
                    {
                        talkingRoot ??= avatarRoot.AddSlot("Talking Driver");
                        var driver = talkingRoot.AttachComponent<ValueCopy<bool>>();
                        driver.Target.ForceLink(uGo.frooxEngineSlot.ActiveSelf_Field);
                    }
                }
            }
        }

        // 4. Process Visemes
        if (plan.VisemeBindings.Count > 0)
        {
            foreach (var viseme in plan.VisemeBindings)
            {
                token.ThrowIfCancellationRequested();
                UnitySkin? skin = null;
                if (viseme.TargetRendererFileId != 0 &&
                    unityObjects.TryGetValue(unchecked((ulong)viseme.TargetRendererFileId), out var obj) &&
                    obj is UnitySkin uSkin)
                {
                    skin = uSkin;
                }
                else
                {
                    foreach (var uObj in unityObjects.Values)
                    {
                        if (uObj is UnitySkin candidate && candidate.createdMeshRenderer != null &&
                            !candidate.createdMeshRenderer.IsDestroyed &&
                            PreparedImportItem.Contains(avatarRoot, candidate.createdMeshRenderer.Slot) &&
                            candidate.createdMeshRenderer.BlendShapeIndex(viseme.TargetBlendShape) >= 0)
                        {
                            skin = candidate;
                            break;
                        }
                    }
                }

                if (skin?.createdMeshRenderer != null && !skin.createdMeshRenderer.IsDestroyed)
                {
                    int shapeIdx = skin.createdMeshRenderer.BlendShapeIndex(viseme.TargetBlendShape);
                    if (shapeIdx >= 0 && shapeIdx < skin.createdMeshRenderer.BlendShapeWeights.Count)
                    {
                        var field = skin.createdMeshRenderer.BlendShapeWeights.GetElement(shapeIdx);
                        if (!field.IsDriven && !field.IsLinked)
                        {
                            var directDrivers = skin.createdMeshRenderer.Slot.GetComponents<DirectVisemeDriver>();
                            var directDriver = directDrivers.Count > 0 ? directDrivers[0] : skin.createdMeshRenderer.Slot.AttachComponent<DirectVisemeDriver>();
                            if (viseme.Channel >= 0 && viseme.Channel <= (int)Viseme.Laughter)
                            {
                                var drive = directDriver[(Viseme)viseme.Channel];
                                if (!drive.IsLinkGranted)
                                    drive.ForceLink(field);
                            }
                        }
                    }
                }
            }
        }

        // 5. Process MeshSettings (bounds & probe anchor)
        if (plan.MeshSettingsBindings.Count > 0)
        {
            foreach (var meshSetting in plan.MeshSettingsBindings)
            {
                token.ThrowIfCancellationRequested();
                UnitySkin? skin = null;
                if (meshSetting.TargetRendererFileId != 0 &&
                    unityObjects.TryGetValue(unchecked((ulong)meshSetting.TargetRendererFileId), out var obj) &&
                    obj is UnitySkin uSkin)
                {
                    skin = uSkin;
                }

                if (skin?.createdMeshRenderer != null && !skin.createdMeshRenderer.IsDestroyed)
                {
                    if (meshSetting.SetBounds)
                    {
                        skin.createdMeshRenderer.BoundsComputeMethod.Value = SkinnedBounds.Static;
                    }
                }
            }
        }

        // 6. Process VisibleInFirstPerson
        if (plan.VisibleInFirstPersonObjectIds.Count > 0)
        {
            foreach (long goId in plan.VisibleInFirstPersonObjectIds)
            {
                token.ThrowIfCancellationRequested();
                if (unityObjects.TryGetValue(unchecked((ulong)goId), out var obj) &&
                    obj is UnityGameObject uGo && uGo.frooxEngineSlot != null && !uGo.frooxEngineSlot.IsDestroyed &&
                    PreparedImportItem.Contains(avatarRoot, uGo.frooxEngineSlot))
                {
                    uGo.frooxEngineSlot.Tag = "VisibleInFirstPerson";
                }
            }
        }

        // 7. Process FloorAdjuster
        if (plan.FloorAdjustmentY.HasValue && Math.Abs(plan.FloorAdjustmentY.Value) > 0.0001f)
        {
            var bipedRig = avatarRoot.GetComponentInChildren<BipedRig>();
            if (bipedRig != null)
            {
                var hips = bipedRig.TryGetBone(BodyNode.Hips);
                if (hips != null)
                {
                    hips.LocalPosition += new float3(0f, plan.FloorAdjustmentY.Value, 0f);
                }
            }
        }
    }
}
