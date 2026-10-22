using System.Numerics;

namespace UnityPackageImporter.UI;

internal static class PanelSpawnPlacement
{
    internal static Vector3 GroundStation(Vector3 position, Vector3 groundPosition, Vector3 userUp)
    {
        var normal = Vector3.Normalize(userUp);
        return position - normal * Vector3.Dot(position - groundPosition, normal);
    }
}
