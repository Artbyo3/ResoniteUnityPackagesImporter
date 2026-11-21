#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;
using Renderite.Shared;
using UnityPackageImporter.FrooxEngineRepresentation;
using UnityGameObject = UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes.GameObject;

namespace UnityPackageImporter.Models;

public static class AvatarPhysBoneRuntime
{
    public static async Task BuildAsync(
        AvatarPhysBonePlan plan,
        Slot avatarRoot,
        IReadOnlyDictionary<ulong, IUnityObject> unityObjects,
        UnityProjectImporter project,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(avatarRoot);
        ArgumentNullException.ThrowIfNull(unityObjects);
        ArgumentNullException.ThrowIfNull(project);
        token.ThrowIfCancellationRequested();

        if (!plan.CanBuild || (plan.Bones.Count == 0 && plan.Colliders.Count == 0)) return;

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


        var createdColliders = new Dictionary<long, IDynamicBoneCollider>();

        foreach (var colDef in plan.Colliders)
        {
            token.ThrowIfCancellationRequested();
            Slot? targetSlot = ResolveSlot(colDef.RootTransformId) ?? ResolveSlot(colDef.GameObjectId);
            if (targetSlot == null || targetSlot.IsDestroyed) continue;

            Slot colliderHolder = targetSlot;
            bool hasOffset = Math.Abs(colDef.Position.X) > 0.0001f ||
                             Math.Abs(colDef.Position.Y) > 0.0001f ||
                             Math.Abs(colDef.Position.Z) > 0.0001f;

            if (hasOffset)
            {
                colliderHolder = targetSlot.AddSlot("PhysBoneColliderOffset", false);
                colliderHolder.LocalPosition = new float3(colDef.Position.X, colDef.Position.Y, colDef.Position.Z);
                colliderHolder.LocalRotation = new floatQ(colDef.Rotation.X, colDef.Rotation.Y, colDef.Rotation.Z, colDef.Rotation.W);
            }

            var sphere = colliderHolder.AttachComponent<DynamicBoneSphereCollider>();
            sphere.Radius.Value = colDef.Radius;
            createdColliders[colDef.ComponentId] = sphere;
        }


        foreach (var boneDef in plan.Bones)
        {
            token.ThrowIfCancellationRequested();
            Slot? rootBoneSlot = ResolveSlot(boneDef.RootTransformId) ?? ResolveSlot(boneDef.GameObjectId);
            if (rootBoneSlot == null || rootBoneSlot.IsDestroyed) continue;


            var ignoredSlots = new HashSet<Slot>();
            foreach (long ignoreId in boneDef.IgnoreTransformIds)
            {
                Slot? ignSlot = ResolveSlot(ignoreId);
                if (ignSlot != null && !ignSlot.IsDestroyed)
                    ignoredSlots.Add(ignSlot);
            }

            var chain = rootBoneSlot.AttachComponent<DynamicBoneChain>();
            chain.Stiffness.Value = boneDef.Stiffness;
            chain.Elasticity.Value = boneDef.Pull;
            chain.Damping.Value = Math.Clamp(1.0f - boneDef.Spring, 0.01f, 1.0f);
            chain.Inertia.Value = Math.Clamp(1.0f - boneDef.Immobile, 0.0f, 1.0f);
            chain.Gravity.Value = new float3(0f, -boneDef.Gravity * 9.81f, 0f);
            chain.BaseBoneRadius.Value = boneDef.Radius;
            chain.IsGrabbable.Value = boneDef.AllowGrabbing;
            chain.SimulateTerminalBones.Value = true;
            chain.DynamicPlayerCollision.Value = boneDef.AllowCollision;
            chain.CollideWithOwnBody.Value = boneDef.AllowCollision;


            foreach (long colId in boneDef.ColliderIds)
            {
                if (createdColliders.TryGetValue(colId, out var colliderComp) && colliderComp is DynamicBoneSphereCollider sphereCol)
                {
                    chain.StaticColliders.Add(sphereCol);
                }
            }


            chain.SetupFromChildren(rootBoneSlot, false, slot => !ignoredSlots.Contains(slot));
        }

        await default(ToWorld);
    }
}
