using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.CommonAvatar;
using FrooxEngine.FinalIK;
using Renderite.Shared;
using UnityGameObject = UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes.GameObject;

namespace UnityPackageImporter.Models;

/// <summary>
/// Supplies Unity calibration data to the engine's avatar creator. The caller owns
/// the uncommitted envelope until this method succeeds, including after failures.
/// </summary>
internal static class NativeAvatarCreation
{
    internal static async Task<Slot> CreateAsync(UnityPrefabImportTask item, Slot envelope,
        bool protectAvatar, CancellationToken cancellationToken = default)
    {
        await default(ToWorld);
        cancellationToken.ThrowIfCancellationRequested();
        if (item?.CurrentStructureRootSlot == null || item.CurrentStructureRootSlot.IsDestroyed || envelope == null || envelope.IsDestroyed)
            throw new InvalidOperationException("The imported avatar is no longer available.");

        Slot source = item.CurrentStructureRootSlot;
        if (source != envelope && !source.IsChildOf(envelope))
            throw new InvalidOperationException("The avatar is outside its import session.");
        var rigs = source.GetComponentsInChildren<BipedRig>().Where(rig => rig.IsBiped).ToArray();
        if (rigs.Length != 1)
            throw new InvalidOperationException(rigs.Length == 0
                ? "This import has no complete humanoid rig. Use the native Avatar Creator to configure it."
                : "This import contains multiple humanoid rigs. Choose a prefab with a single avatar.");

        BipedRig rig = rigs[0];
        if (envelope.GetComponentInChildren<AvatarRoot>() != null)
            throw new InvalidOperationException("This import already contains native avatar setup and cannot be created again.");

        // Match native readiness before its non-cancellable finalization begins.
        // This keeps cancellation responsive during unfinished asset downloads.
        var meshes = envelope.GetComponentsInChildren<SkinnedMeshRenderer>();
        while (meshes.Any(mesh => !mesh.IsDestroyed && mesh.Mesh.Target != null && !mesh.Mesh.IsAssetAvailable))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (envelope.IsDestroyed) throw new OperationCanceledException("The import was removed.");
            await default(NextUpdate);
        }
        cancellationToken.ThrowIfCancellationRequested();

        // Capture the authored viewpoint before changing object-root boundaries.
        var viewpoint = ResolveViewpoint(item, rig);
        VRIK ik = rig.Slot.GetComponent<VRIK>();
        if (ik == null)
            throw new InvalidOperationException("The humanoid rig has no initialized IK. Use the native Avatar Creator to configure it.");

        Slot calibration = envelope.AddSlot("Avatar creation calibration", false);
        calibration.PersistentSelf = false;
        try
        {
            Slot head = calibration.AddSlot("Headset", false);
            head.GlobalPosition = viewpoint.Position;
            head.GlobalRotation = viewpoint.Rotation;
            Slot left = CreateHandReference(calibration, rig[BodyNode.LeftHand],
                ik.Solver.leftArm.WristToPalmAxis.Value, ik.Solver.leftArm.PalmToThumbAxis.Value, "Left controller");
            Slot right = CreateHandReference(calibration, rig[BodyNode.RightHand],
                ik.Solver.rightArm.WristToPalmAxis.Value, ik.Solver.rightArm.PalmToThumbAxis.Value, "Right controller");

            // The FBX's nested ObjectRoot would exclude sibling clothing/meshes.
            // Let native creation own the complete imported prefab and its wrapper.
            for (Slot cursor = rig.Slot; cursor != envelope; cursor = cursor.Parent)
            {
                if (cursor == null) throw new InvalidOperationException("The humanoid rig left its import session.");
                foreach (var marker in cursor.GetComponents<ObjectRoot>().ToArray()) marker.Destroy();
            }
            envelope.GetComponentOrAttach<ObjectRoot>();
            if (rig.Slot.GetObjectRoot(false) != envelope)
                throw new InvalidOperationException("The native avatar root could not be bound to the complete imported prefab.");

            cancellationToken.ThrowIfCancellationRequested();
            // No foot/pelvis tracker calibration was authored by Unity. Null uses
            // the engine's default calibration instead of treating joints as trackers.
            // Await native completion even if cancellation arrives: aborting the await
            // could leave the engine mutating an envelope that cleanup has destroyed.
            await AvatarCreator.CreateBipedAvatarAsync(rig, head, left, right,
                leftFootReference: null, rightFootReference: null, hipsReference: null,
                setupEyes: true, setupProtection: protectAvatar,
                setupVolumeMeter: false, setupFaceTracking: true);
            await default(ToWorld);
            cancellationToken.ThrowIfCancellationRequested();
            if (envelope.IsDestroyed || envelope.GetComponent<AvatarRoot>() == null ||
                envelope.GetComponent<VRIKAvatar>() == null || envelope.GetComponent<AvatarGroup>() == null)
                throw new InvalidOperationException("Native avatar creation did not finish. The import has not been released.");

            // Native protection is attached to the root and renderers. Assign the
            // importing user consistently before this object can leave staging.
            if (protectAvatar)
                foreach (var protection in envelope.GetComponentsInChildren<SimpleAvatarProtection>())
                    protection.User.Target = envelope.World.LocalUser;
            return envelope;
        }
        finally
        {
            await default(ToWorld);
            if (!calibration.IsDestroyed) calibration.Destroy();
        }
    }

    private static (float3 Position, floatQ Rotation) ResolveViewpoint(UnityPrefabImportTask item, BipedRig rig)
    {
        var descriptors = item.Manifest?.Avatars
            .Select(definition => (Definition: definition, Root: ResolveDescriptorRoot(item, definition)))
            // The importer can attach BipedRig either to the FBX child or to a
            // prefab wrapper above the descriptor. Bone ownership identifies the
            // descriptor correctly in both layouts.
            .Where(pair => pair.Root != null && Contains(pair.Root, rig[BodyNode.Head]) &&
                Contains(pair.Root, rig[BodyNode.Hips]))
            .ToArray();
        if (descriptors?.Length == 1 && descriptors[0].Definition.ViewPosition is UnityPosition point)
        {
            Slot root = descriptors[0].Root;
            float3 local = new(point.X, point.Y, point.Z);
            float3 position = root.LocalPointToGlobal(local);
            if (!IsFinite(position)) throw new InvalidOperationException("The authored avatar viewpoint has an invalid transform.");
            return (position, root.GlobalRotation);
        }

        // A paired-eye midpoint is an anatomical fallback; the head joint itself
        // is not an HMD viewpoint and is deliberately not used as one.
        Slot leftEye = rig.TryGetBone(BodyNode.LeftEye);
        Slot rightEye = rig.TryGetBone(BodyNode.RightEye);
        if (leftEye == null || rightEye == null)
            throw new InvalidOperationException("This avatar has no usable authored viewpoint or paired eye bones. Use the native Avatar Creator to calibrate its view.");
        UnityPackageImporter.Warn("No unambiguous authored avatar viewpoint was available; using the midpoint of the eye bones.");
        return ((leftEye.GlobalPosition + rightEye.GlobalPosition) * 0.5f, rig.Slot.GlobalRotation);
    }

    private static bool Contains(Slot parent, Slot child) => child != null && (parent == child || child.IsChildOf(parent));

    private static Slot ResolveDescriptorRoot(UnityPrefabImportTask item, VrcAvatarDefinition definition) =>
        item.existingIUnityObjects != null &&
        item.existingIUnityObjects.TryGetValue(unchecked((ulong)definition.GameObjectFileId), out var value) &&
        value is UnityGameObject gameObject && gameObject.frooxEngineSlot is { IsDestroyed: false } slot
            ? slot : null;

    private static Slot CreateHandReference(Slot parent, Slot hand, float3 palmAxis, float3 thumbAxis, string name)
    {
        if (hand == null || !IsFinite(palmAxis) || !IsFinite(thumbAxis) ||
            palmAxis.SqrMagnitude < 0.000001f || thumbAxis.SqrMagnitude < 0.000001f ||
            MathX.Cross(palmAxis, thumbAxis).SqrMagnitude < 0.000001f)
            throw new InvalidOperationException("The hand calibration axes are incomplete. Use the native Avatar Creator to align the hands.");

        // Matches AvatarCreator.AlignHands in the installed engine: convert its
        // detected palm/thumb basis to controller forward/up, then rotate 180°.
        floatQ correction = floatQ.FromToRotation(floatQ.LookRotation(palmAxis, thumbAxis),
            floatQ.LookRotation(float3.Forward, float3.Up));
        Slot reference = parent.AddSlot(name, false);
        reference.GlobalPosition = hand.GlobalPosition;
        reference.GlobalRotation = hand.GlobalRotation * correction * floatQ.AxisAngle(float3.Forward, 180f);
        // These are the engine AvatarCreator's default tool anchor positions.
        // Plain calibration slots let native SetupAnchors create the real anchors
        // without spawning the creator's editor widgets and display geometry.
        reference.AddSlot("Tooltip", false).LocalPosition = new float3(0f, 0f, 0.15f);
        reference.AddSlot("Grabber", false).LocalPosition = new float3(0f, -0.02f, 0.075f);
        reference.AddSlot("Shelf", false).LocalPosition = new float3(0f, 0.03f, 0.03f);
        return reference;
    }

    private static bool IsFinite(float3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
}
