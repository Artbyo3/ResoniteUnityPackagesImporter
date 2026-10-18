#nullable enable
using System;
using System.Numerics;

namespace UnityPackageImporter.Models;

// Pure calibration math, shared with the offline regression suite. All inputs
// must describe the same hand: the IK solver's hand slot and its local axes.
internal static class HandCalibration
{
    internal static Quaternion GetReferenceRotation(Quaternion handRotation,
        Vector3 wristToPalm, Vector3 palmToThumb, bool isLeftHand)
    {
        if (!IsFinite(handRotation) || !IsUsable(wristToPalm) || !IsUsable(palmToThumb) ||
            !float.IsFinite(handRotation.LengthSquared()) || handRotation.LengthSquared() < 0.000001f)
            throw new InvalidOperationException("The hand calibration axes or rotation are incomplete. Use the native Avatar Creator to align the hands.");

        Quaternion handBasis = GetBasis(wristToPalm, palmToThumb);
        // AvatarPoseNode follows Resonite's hand pose, whose fingers point +Z
        // and whose thumb points +X on the left hand and -X on the right.
        // HandSimulator's native rest pose is LookRotation(Down, outward side):
        // these mirrored thumb axes then point forward, toward natural palms.
        // The VRIK default thumb-up guess is not this anatomical reference frame.
        Quaternion referenceBasis = GetBasis(Vector3.UnitZ,
            isLeftHand ? Vector3.UnitX : -Vector3.UnitX);

        // SetReference stores inverse(referenceWorld) * handWorld. Convert
        // between the model's local anatomical basis and the native hand basis,
        // rather than equating either one with the model's bone axes.
        return Quaternion.Normalize(handRotation * handBasis * Quaternion.Inverse(referenceBasis));
    }

    private static Quaternion GetBasis(Vector3 palm, Vector3 thumb)
    {
        Vector3 forward = Vector3.Normalize(palm);
        Vector3 right = Vector3.Cross(Vector3.Normalize(thumb), forward);
        if (!IsUsable(right))
            throw new InvalidOperationException("The hand calibration axes are parallel. Use the native Avatar Creator to align the hands.");
        right = Vector3.Normalize(right);
        Vector3 up = Vector3.Cross(forward, right);

        // Rows map a common anatomical basis into the supplied hand frame.
        return Quaternion.CreateFromRotationMatrix(new Matrix4x4(
            right.X, right.Y, right.Z, 0,
            up.X, up.Y, up.Z, 0,
            forward.X, forward.Y, forward.Z, 0,
            0, 0, 0, 1));
    }

    private static bool IsUsable(Vector3 value) => float.IsFinite(value.X) &&
        float.IsFinite(value.Y) && float.IsFinite(value.Z) &&
        float.IsFinite(value.LengthSquared()) && value.LengthSquared() >= 0.000001f;

    private static bool IsFinite(Quaternion value) => float.IsFinite(value.X) &&
        float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);
}
