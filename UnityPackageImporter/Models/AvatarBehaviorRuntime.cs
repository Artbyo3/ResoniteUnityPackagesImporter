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

internal static class AvatarBehaviorRuntime
{
    internal static async Task BuildAsync(
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
            !plan.FloorAdjustmentY.HasValue && plan.BoneConstraintBindings.Count == 0 && plan.WorldConstraintBindings.Count == 0 &&
            plan.MoveToBindings.Count == 0 && plan.WorldFixedBindings.Count == 0 && plan.WorldScaleBindings.Count == 0))
            return;

        await default(ToWorld);
        token.ThrowIfCancellationRequested();

        if (avatarRoot.IsDestroyed || avatarRoot.GetComponentInParents<AvatarRoot>() == null)
            return;


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
                                var driver = breathingRoot.AttachComponent<TimeSineDriver>();
                                driver.Speed.Value = breathing.AngularSpeed;
                                driver.Min.Value = breathing.MinWeight;
                                driver.Max.Value = breathing.MaxWeight;
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

                    if (boneSlot != null && PreparedImportItem.Contains(avatarRoot, boneSlot) &&
                        !boneSlot.Scale_Field.IsDriven && !boneSlot.Scale_Field.IsLinked)
                    {
                        breathingRoot ??= avatarRoot.AddSlot("Breathing Driver");
                        float3 baseline = boneSlot.LocalScale;
                        var scaleDriver = breathingRoot.AttachComponent<Float3Driver>();
                        Sync<float> DriveAxis(float baselineValue)
                        {
                            var value = breathingRoot.AttachComponent<ValueField<float>>();
                            value.Value.Value = baselineValue;
                            var driver = breathingRoot.AttachComponent<TimeSineDriver>();
                            driver.Speed.Value = breathing.AngularSpeed;
                            driver.Min.Value = baselineValue;
                            driver.Max.Value = baselineValue * 1.05f;
                            driver.Target.ForceLink(value.Value);
                            return value.Value;
                        }
                        scaleDriver.X.Target = DriveAxis(baseline.x);
                        scaleDriver.Y.Target = DriveAxis(baseline.y);
                        scaleDriver.Z.Target = DriveAxis(baseline.z);
                        scaleDriver.Target.ForceLink(boneSlot.Scale_Field);
                    }
                }
            }
        }


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

        if (plan.BoneConstraintBindings.Count > 0)
        {
            var bipedRig = avatarRoot.GetComponentInChildren<BipedRig>();
            foreach (var bc in plan.BoneConstraintBindings)
            {
                token.ThrowIfCancellationRequested();
                if (unityObjects.TryGetValue(unchecked((ulong)bc.TargetObjectFileId), out var obj) &&
                    obj is UnityGameObject uGo && uGo.frooxEngineSlot != null && !uGo.frooxEngineSlot.IsDestroyed &&
                    PreparedImportItem.Contains(avatarRoot, uGo.frooxEngineSlot))
                {
                    var bodyNode = (BodyNode)bc.HumanoidBone;
                    var targetBoneSlot = bipedRig?.TryGetBone(bodyNode);
                    if (targetBoneSlot != null && !targetBoneSlot.IsDestroyed && targetBoneSlot != uGo.frooxEngineSlot)
                    {
                        var copy = uGo.frooxEngineSlot.AttachComponent<CopyGlobalTransform>();
                        copy.Source.Target = targetBoneSlot;
                    }
                }
            }
        }


        if (plan.WorldConstraintBindings.Count > 0)
        {
            foreach (var wc in plan.WorldConstraintBindings)
            {
                token.ThrowIfCancellationRequested();
                if (unityObjects.TryGetValue(unchecked((ulong)wc.TargetObjectFileId), out var obj) &&
                    obj is UnityGameObject uGo && uGo.frooxEngineSlot != null && !uGo.frooxEngineSlot.IsDestroyed &&
                    PreparedImportItem.Contains(avatarRoot, uGo.frooxEngineSlot))
                {
                    var copy = uGo.frooxEngineSlot.AttachComponent<CopyGlobalTransform>();
                    copy.Source.Target = avatarRoot;
                }
            }
        }


        if (plan.MoveToBindings.Count > 0)
        {
            var bipedRig = avatarRoot.GetComponentInChildren<BipedRig>();
            foreach (var mt in plan.MoveToBindings)
            {
                token.ThrowIfCancellationRequested();
                if (unityObjects.TryGetValue(unchecked((ulong)mt.SourceObjectFileId), out var sObj) &&
                    sObj is UnityGameObject sourceUGo && sourceUGo.frooxEngineSlot != null && !sourceUGo.frooxEngineSlot.IsDestroyed &&
                    PreparedImportItem.Contains(avatarRoot, sourceUGo.frooxEngineSlot))
                {
                    Slot? destSlot = null;
                    if (mt.DestinationObjectFileId != 0 &&
                        unityObjects.TryGetValue(unchecked((ulong)mt.DestinationObjectFileId), out var dObj) &&
                        dObj is UnityGameObject destUGo && destUGo.frooxEngineSlot != null && !destUGo.frooxEngineSlot.IsDestroyed &&
                        PreparedImportItem.Contains(avatarRoot, destUGo.frooxEngineSlot))
                    {
                        destSlot = destUGo.frooxEngineSlot;
                    }
                    else if (mt.HumanoidBone >= 0 && bipedRig != null)
                    {
                        var bodyNode = (BodyNode)mt.HumanoidBone;
                        destSlot = bipedRig.TryGetBone(bodyNode);
                    }

                    if (destSlot != null && !destSlot.IsDestroyed && destSlot != sourceUGo.frooxEngineSlot &&
                        !sourceUGo.frooxEngineSlot.IsChildOf(destSlot))
                    {
                        sourceUGo.frooxEngineSlot.SetParent(destSlot, mt.PreserveWorldTransform);
                    }
                }
            }
        }


        if (plan.WorldFixedBindings.Count > 0)
        {
            foreach (var wf in plan.WorldFixedBindings)
            {
                token.ThrowIfCancellationRequested();
                if (unityObjects.TryGetValue(unchecked((ulong)wf.TargetObjectFileId), out var obj) &&
                    obj is UnityGameObject uGo && uGo.frooxEngineSlot != null && !uGo.frooxEngineSlot.IsDestroyed &&
                    PreparedImportItem.Contains(avatarRoot, uGo.frooxEngineSlot))
                {
                    var parentSlot = avatarRoot.Parent ?? avatarRoot;
                    uGo.frooxEngineSlot.SetParent(parentSlot, true);
                }
            }
        }


        if (plan.WorldScaleBindings.Count > 0)
        {
            foreach (var ws in plan.WorldScaleBindings)
            {
                token.ThrowIfCancellationRequested();
                if (unityObjects.TryGetValue(unchecked((ulong)ws.TargetObjectFileId), out var obj) &&
                    obj is UnityGameObject uGo && uGo.frooxEngineSlot != null && !uGo.frooxEngineSlot.IsDestroyed &&
                    PreparedImportItem.Contains(avatarRoot, uGo.frooxEngineSlot))
                {
                    var scaleCopy = uGo.frooxEngineSlot.AttachComponent<CopyGlobalScale>();
                    scaleCopy.Source.Target = avatarRoot.Parent ?? avatarRoot;
                }
            }
        }
    }
}
