#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;
using Renderite.Shared;
using UnityPackageImporter.FrooxEngineRepresentation;
using UnityGameObject = UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes.GameObject;

namespace UnityPackageImporter.Models;

public static class AvatarConstraintRuntime
{
    public static async Task BuildAsync(
        AvatarConstraintPlan plan,
        Slot avatarRoot,
        IReadOnlyDictionary<ulong, IUnityObject> unityObjects,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(avatarRoot);
        ArgumentNullException.ThrowIfNull(unityObjects);
        token.ThrowIfCancellationRequested();

        if (!plan.CanBuild || plan.Constraints.Count == 0) return;

        await default(ToWorld);
        token.ThrowIfCancellationRequested();

        Slot? ResolveSlot(long fileId)
        {
            if (fileId == 0) return null;
            if (unityObjects.TryGetValue(unchecked((ulong)fileId), out var obj))
            {
                if (obj is UnityGameObject go && go.frooxEngineSlot != null && !go.frooxEngineSlot.IsDestroyed)
                    return go.frooxEngineSlot;
                if (obj is FrooxEngineRepresentation.GameObjectTypes.Transform trans && trans.m_GameObjectID != 0 &&
                    unityObjects.TryGetValue(trans.m_GameObjectID, out var goObj) && goObj is UnityGameObject go2 &&
                    go2.frooxEngineSlot != null && !go2.frooxEngineSlot.IsDestroyed)
                    return go2.frooxEngineSlot;
            }
            return null;
        }

        foreach (var constraint in plan.Constraints)
        {
            token.ThrowIfCancellationRequested();
            if (!constraint.IsActive || constraint.Weight < 0.001f) continue;

            Slot? targetSlot = ResolveSlot(constraint.TargetTransformId) ?? ResolveSlot(constraint.GameObjectId);
            if (targetSlot == null || targetSlot.IsDestroyed) continue;

            // Find primary source
            var primarySourceDef = constraint.Sources.OrderByDescending(s => s.Weight).FirstOrDefault();
            if (primarySourceDef == null) continue;

            Slot? sourceSlot = ResolveSlot(primarySourceDef.SourceTransformId);
            if (sourceSlot == null || sourceSlot.IsDestroyed || sourceSlot == targetSlot) continue;

            switch (constraint.Type)
            {
                case AvatarConstraintType.Position:
                case AvatarConstraintType.Rotation:
                case AvatarConstraintType.Parent:
                {
                    var copy = targetSlot.AttachComponent<CopyGlobalTransform>();
                    copy.Source.Target = sourceSlot;
                    copy.DrivePosition = constraint.Type is AvatarConstraintType.Position or AvatarConstraintType.Parent;
                    copy.DriveRotation = constraint.Type is AvatarConstraintType.Rotation or AvatarConstraintType.Parent;
                    break;
                }
                case AvatarConstraintType.LookAt:
                case AvatarConstraintType.Aim:
                {
                    var copy = targetSlot.AttachComponent<CopyGlobalTransform>();
                    copy.Source.Target = sourceSlot;
                    copy.DrivePosition = false;
                    copy.DriveRotation = true;
                    break;
                }
                case AvatarConstraintType.Scale:
                {
                    break;
                }
            }
        }

        await default(ToWorld);
    }
}
