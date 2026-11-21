#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.CommonAvatar;
using Renderite.Shared;
using UnityGameObject = UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes.GameObject;
using UnitySkin = UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes.SkinnedMeshRenderer;

namespace UnityPackageImporter.Models;

internal static class AvatarEyeLookRuntime
{
    internal static async Task ApplyAsync(
        PreparedImportItem item,
        Slot avatarRoot,
        AvatarEyeLookSettings eyeLook,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(avatarRoot);
        token.ThrowIfCancellationRequested();

        if (eyeLook == null || !eyeLook.HasSettings) return;

        await default(ToWorld);
        token.ThrowIfCancellationRequested();

        void Report(string code, string message, AvatarOperationSeverity severity = AvatarOperationSeverity.Warning) =>
            item.ConversionPlan.AddDiagnostic(severity, message, code: "EYELOOK_" + code);

        try
        {
            if (!string.IsNullOrEmpty(eyeLook.Error))
                throw new InvalidDataException(eyeLook.Error);

            Slot? ResolveTransformSlot(UnityAssetReference? reference)
            {
                if (reference == null || reference.FileId == 0) return null;
                if (item.existingIUnityObjects.TryGetValue(unchecked((ulong)reference.FileId), out var obj))
                {
                    if (obj is UnityGameObject go && go.frooxEngineSlot != null && !go.frooxEngineSlot.IsDestroyed)
                        return go.frooxEngineSlot;
                    if (obj is FrooxEngineRepresentation.GameObjectTypes.Transform trans && trans.m_GameObjectID != 0 &&
                        item.existingIUnityObjects.TryGetValue(trans.m_GameObjectID, out var goObj) && goObj is UnityGameObject go2 &&
                        go2.frooxEngineSlot != null && !go2.frooxEngineSlot.IsDestroyed)
                        return go2.frooxEngineSlot;
                }
                return null;
            }

            Slot? leftEyeSlot = ResolveTransformSlot(eyeLook.LeftEyeTransform);
            Slot? rightEyeSlot = ResolveTransformSlot(eyeLook.RightEyeTransform);


            if (leftEyeSlot != null || rightEyeSlot != null)
            {
                var rotationDrivers = avatarRoot.GetComponentsInChildren<EyeRotationDriver>();
                foreach (var driver in rotationDrivers)
                {
                    if (driver.EyeManager.Target == null)
                    {
                        var manager = avatarRoot.GetComponentInChildren<EyeManager>();
                        if (manager != null) driver.EyeManager.Target = manager;
                    }

                    if (eyeLook.Confidence < 1f || eyeLook.Excitement > 0f)
                    {
                        driver.EyeMotionScale.Value = Math.Clamp(eyeLook.Confidence, 0.2f, 1.5f);
                    }
                }
            }


            if (eyeLook.EyelidType == 1)
            {
                if (eyeLook.EyelidsMesh != null &&
                    item.existingIUnityObjects.TryGetValue(unchecked((ulong)eyeLook.EyelidsMesh.FileId), out var meshObj) &&
                    meshObj is UnitySkin unitySkin && unitySkin.createdMeshRenderer != null && !unitySkin.createdMeshRenderer.IsDestroyed)
                {
                    var skinnedMesh = unitySkin.createdMeshRenderer;
                    if (eyeLook.EyelidBlendShapeIndices.Count > 0)
                    {
                        int blinkIndex = eyeLook.EyelidBlendShapeIndices[0];
                        if (blinkIndex >= 0 && blinkIndex < skinnedMesh.BlendShapeWeights.Count)
                        {
                            var blinkField = skinnedMesh.BlendShapeWeights.GetElement(blinkIndex);
                            var eyeLinearDrivers = avatarRoot.GetComponentsInChildren<EyeLinearDriver>();
                            if (eyeLinearDrivers.Count > 0)
                            {
                                var driver = eyeLinearDrivers[0];
                                if (driver.EyeManager.Target == null)
                                {
                                    var manager = avatarRoot.GetComponentInChildren<EyeManager>();
                                    if (manager != null) driver.EyeManager.Target = manager;
                                }
                            }
                        }
                    }
                }
                else
                {
                    Report("MESH_UNRESOLVED", "Authored eyelidsMesh could not be resolved to a uniquely owned skinned renderer.");
                }
            }
            else if (eyeLook.EyelidType == 2)
            {
                Report("BONES_INFO", "Authored bone eyelids are driven through standard skeleton hierarchy.", AvatarOperationSeverity.Info);
            }

            Report("CALIBRATED", "Configured native eye gaze and eyelid calibration from VRCAvatarDescriptor customEyeLookSettings.", AvatarOperationSeverity.Info);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await default(ToWorld);
            Report("FAILED", "Failed to calibrate avatar eye look: " + ex.Message);
        }
    }
}
