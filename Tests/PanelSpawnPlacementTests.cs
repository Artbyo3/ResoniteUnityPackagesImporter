using System.Numerics;
using UnityPackageImporter.UI;

internal static class PanelSpawnPlacementTests
{
    internal static void Register(Action<string, Action> test)
    {
        test("Station placement removes head-height offset without changing horizontal location", () =>
        {
            var grounded = PanelSpawnPlacement.GroundStation(new Vector3(2f, 1.8f, 3f), Vector3.Zero, Vector3.UnitY);
            Check(grounded, new Vector3(2f, 0f, 3f));
        });
        test("Station placement uses the user's elevated floor rather than world zero", () =>
        {
            var grounded = PanelSpawnPlacement.GroundStation(new Vector3(2f, 13.8f, 3f), new Vector3(1f, 12f, 1f), Vector3.UnitY);
            Check(grounded, new Vector3(2f, 12f, 3f));
        });
        test("Station placement projects onto the user's tilted ground plane", () =>
        {
            var up = Vector3.Normalize(new Vector3(0f, 1f, 1f));
            var ground = new Vector3(3f, 4f, 5f);
            var expected = ground + new Vector3(2f, 0f, 0f);
            Check(PanelSpawnPlacement.GroundStation(expected + up * 1.8f, ground, up), expected);
        });
        test("Station placement leaves an already grounded spawn unchanged", () =>
        {
            var expected = new Vector3(-4f, -2f, 6f);
            Check(PanelSpawnPlacement.GroundStation(expected, new Vector3(0f, -2f, 0f), Vector3.UnitY), expected);
        });
    }

    private static void Check(Vector3 actual, Vector3 expected)
    {
        if (Vector3.Distance(actual, expected) > 0.00001f)
            throw new Exception($"Expected grounded station at {expected}, got {actual}.");
    }
}
