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

        if (!plan.CanBuild || (plan.BreathingBindings.Count == 0 && plan.BlinkingBindings.Count == 0))
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
    }
}
