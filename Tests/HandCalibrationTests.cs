using System.Numerics;
using UnityPackageImporter.Models;

internal static class HandCalibrationTests
{
    internal static void Register(Action<string, Action> test)
    {
        test("Hand references preserve anatomical directions for either side and bone convention", () =>
        {
            foreach (bool left in new[] { true, false })
            foreach (Vector3 thumb in new[] { -Vector3.UnitX, Vector3.UnitX })
            foreach (Quaternion hand in HandRotations())
            {
                Quaternion reference = HandCalibration.GetReferenceRotation(hand, Vector3.UnitY, thumb, left);
                Near(Vector3.Transform(Vector3.UnitZ, reference), Vector3.Transform(Vector3.UnitY, hand));
                Near(Vector3.Transform(left ? Vector3.UnitX : -Vector3.UnitX, reference),
                    Vector3.Transform(thumb, hand));
            }
        });

        test("Saved wrist offsets map model anatomy into Resonite's mirrored hand frames", () =>
        {
            foreach (bool left in new[] { true, false })
            foreach (Vector3 thumb in new[] { -Vector3.UnitX, Vector3.UnitX })
            foreach (Quaternion hand in HandRotations())
            {
                Quaternion reference = HandCalibration.GetReferenceRotation(hand, Vector3.UnitY, thumb, left);
                // VRIKAvatar stores the wrist's rotation relative to its controller reference.
                Quaternion offset = Quaternion.Inverse(reference) * hand;
                Near(Vector3.Transform(Vector3.UnitY, offset), Vector3.UnitZ);
                Near(Vector3.Transform(thumb, offset), left ? Vector3.UnitX : -Vector3.UnitX);
            }
        });

        test("Hand calibration is independent of bone roll and solver reference space", () =>
        {
            Quaternion wrist = Quaternion.CreateFromYawPitchRoll(0.3f, -0.9f, 0.6f);
            Quaternion alignedFrame = Quaternion.CreateFromYawPitchRoll(-0.4f, 0.7f, 1.2f);
            foreach (bool left in new[] { true, false })
            foreach (Vector3 thumb in new[] { -Vector3.UnitX, Vector3.UnitX })
            {
                Quaternion original = HandCalibration.GetReferenceRotation(wrist, Vector3.UnitY, thumb, left);
                // An aligned solver slot changes the local coordinates, not the
                // anatomical directions in world space. Re-express both inputs.
                Quaternion alignedHand = wrist * alignedFrame;
                Quaternion inverseFrame = Quaternion.Inverse(alignedFrame);
                Quaternion aligned = HandCalibration.GetReferenceRotation(alignedHand,
                    Vector3.Transform(Vector3.UnitY, inverseFrame), Vector3.Transform(thumb, inverseFrame), left);
                SameRotation(aligned, original);
            }
        });

        test("Rotating the avatar or station rotates hand references by the same amount", () =>
        {
            Quaternion hand = Quaternion.CreateFromYawPitchRoll(-0.6f, 0.2f, 0.9f);
            Quaternion world = Quaternion.CreateFromYawPitchRoll(1.4f, -0.3f, -0.8f);
            Quaternion reference = HandCalibration.GetReferenceRotation(hand, Vector3.UnitY, Vector3.UnitX, true);
            Quaternion moved = HandCalibration.GetReferenceRotation(world * hand, Vector3.UnitY, Vector3.UnitX, true);
            SameRotation(moved, world * reference);
        });

        test("Hand calibration normalizes axes and removes thumb's forward component", () =>
        {
            Quaternion reference = HandCalibration.GetReferenceRotation(Quaternion.Identity,
                3 * Vector3.UnitY, new Vector3(-2, 0.4f, 0), true);
            Near(Vector3.Transform(Vector3.UnitZ, reference), Vector3.UnitY);
            Near(Vector3.Transform(Vector3.UnitX, reference), -Vector3.UnitX);
        });

        test("Native simulated resting hands keep fingers down and thumbs forward", () =>
        {
            foreach (bool left in new[] { true, false })
            {
                Vector3 thumb = left ? -Vector3.UnitX : Vector3.UnitX;
                Quaternion reference = HandCalibration.GetReferenceRotation(Quaternion.Identity, Vector3.UnitY, thumb, left);
                Quaternion wristOffset = Quaternion.Inverse(reference);
                // HandSimulator.Update uses LookRotation(Down, outward side).
                // These native pose fixtures have forward down and up outward.
                Quaternion simulatedHand = new(0.5f, left ? -0.5f : 0.5f,
                    left ? 0.5f : -0.5f, 0.5f);
                Quaternion wrist = simulatedHand * wristOffset;
                Near(Vector3.Transform(Vector3.UnitY, wrist), -Vector3.UnitY);
                Near(Vector3.Transform(thumb, wrist), Vector3.UnitZ);
            }
        });

        test("Incomplete or nonfinite hand calibration fails instead of guessing a rotation", () =>
        {
            foreach (Vector3 bad in new[] { Vector3.Zero, new Vector3(float.NaN, 0, 0),
                         new Vector3(float.PositiveInfinity, 0, 0), new Vector3(float.MaxValue, 0, 0) })
            {
                Reject(() => HandCalibration.GetReferenceRotation(Quaternion.Identity, bad, Vector3.UnitX, true));
                Reject(() => HandCalibration.GetReferenceRotation(Quaternion.Identity, Vector3.UnitY, bad, true));
            }
            Reject(() => HandCalibration.GetReferenceRotation(Quaternion.Identity, Vector3.UnitY, Vector3.UnitY, true));
            Reject(() => HandCalibration.GetReferenceRotation(Quaternion.Identity, Vector3.UnitY, -Vector3.UnitY, true));
            Reject(() => HandCalibration.GetReferenceRotation(new Quaternion(), Vector3.UnitY, Vector3.UnitX, true));
            Reject(() => HandCalibration.GetReferenceRotation(new Quaternion(0, float.NaN, 0, 1), Vector3.UnitY, Vector3.UnitX, true));
        });
    }

    private static Quaternion[] HandRotations() => new[] { Quaternion.Identity,
        Quaternion.CreateFromYawPitchRoll(0.8f, -0.3f, 1.1f),
        Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI) };

    private static void SameRotation(Quaternion actual, Quaternion expected)
    {
        Near(Vector3.Transform(Vector3.UnitZ, actual), Vector3.Transform(Vector3.UnitZ, expected));
        Near(Vector3.Transform(Vector3.UnitY, actual), Vector3.Transform(Vector3.UnitY, expected));
    }

    private static void Near(Vector3 actual, Vector3 expected)
    {
        if (!float.IsFinite(actual.LengthSquared()) || Vector3.Distance(actual, expected) > 0.0001f)
            throw new Exception($"Expected direction {expected}, got {actual}.");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new Exception("Invalid hand calibration was accepted.");
    }
}
