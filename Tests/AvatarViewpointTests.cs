using System.Globalization;
using UnityPackageImporter.Models;

internal static class AvatarViewpointTests
{
    internal static void Register(Action<string, Action> test)
    {
        test("Avatar viewpoint preserves authored coordinates independently of culture", () =>
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                var avatar = Parse("  ViewPosition: {x: -0.02, y: 1.356539, z: 4.469751e-2}");
                if (avatar.ViewPosition is not UnityPosition position ||
                    position.X != -0.02f || position.Y != 1.356539f || position.Z != 0.04469751f)
                    throw new Exception("The descriptor viewpoint was lost, normalized, or parsed with the current locale.");
                if (avatar.GameObjectFileId != 100)
                    throw new Exception("The viewpoint lost its source GameObject reference.");
            }
            finally { CultureInfo.CurrentCulture = previous; }
        });
        test("Missing avatar viewpoint stays absent instead of becoming the origin", () =>
        {
            if (Parse("").ViewPosition != null || Parse("  ViewPosition: {x: 0, y: 1}").ViewPosition != null)
                throw new Exception("Missing or incomplete viewpoint must require a fallback.");
        });
        test("Nonfinite avatar viewpoints cannot reach native calibration", () =>
        {
            foreach (string invalid in new[] { "NaN", "Infinity", "-Infinity", "1e50", "not-a-number" })
                if (Parse("  ViewPosition: {x: 0, y: " + invalid + ", z: 0}").ViewPosition != null)
                    throw new Exception("Invalid coordinate was accepted: " + invalid);
        });
        test("Avatar viewpoints remain associated with their own descriptors", () =>
        {
            string yaml = Descriptor(100, 101, "  ViewPosition: {x: 0, y: 1.2, z: 0.04}") +
                Descriptor(200, 201, "  ViewPosition: {x: 0, y: 2.1, z: -0.01}");
            var avatars = AvatarPackageIndex.ParseText(yaml).Avatars;
            if (avatars.Count != 2 || avatars[0].ViewPosition?.Y != 1.2f || avatars[1].ViewPosition?.Y != 2.1f)
                throw new Exception("Descriptors were cross-wired.");
        });
    }

    private static VrcAvatarDefinition Parse(string viewpoint) =>
        AvatarPackageIndex.ParseText(Descriptor(100, 101, viewpoint)).Avatars.Single();

    private static string Descriptor(int root, int component, string viewpoint) =>
        $"--- !u!1 &{root}\nGameObject:\n  m_Name: Test Avatar\n--- !u!114 &{component}\nMonoBehaviour:\n" +
        $"  m_GameObject: {{fileID: {root}}}\n  m_Script: {{fileID: 11500000, guid: {AvatarPackageIndex.VrcAvatarDescriptorScriptGuid}, type: 3}}\n" +
        viewpoint + "\n";
}
