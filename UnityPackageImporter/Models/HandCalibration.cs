#nullable enable
using System;
using System.Numerics;

namespace UnityPackageImporter.Models;



internal static class HandCalibration
{
    internal static Quaternion GetReferenceRotation(Quaternion handRotation,
        Vector3 wristToPalm, Vector3 palmToThumb, bool isLeftHand)
    {
        if (!IsFinite(handRotation) || !IsUsable(wristToPalm) || !IsUsable(palmToThumb) ||
            !float.IsFinite(handRotation.LengthSquared()) || handRotation.LengthSquared() < 0.000001f)
            throw new InvalidOperationException("The hand calibration axes or rotation are incomplete. Use the native Avatar Creator to align the hands.");

        Quaternion handBasis = GetBasis(wristToPalm, palmToThumb);





        Quaternion referenceBasis = GetBasis(Vector3.UnitZ,
            isLeftHand ? Vector3.UnitX : -Vector3.UnitX);




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
