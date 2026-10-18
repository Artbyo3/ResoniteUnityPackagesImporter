#nullable enable
using UnityPackageImporter.Models;

namespace UnityPackageImporter.Materials;

internal static class EmissionPlanInputs
{
    internal static TextureContribution? Texture(UnityMaterialData data, string property, int uv,
        TextureContributionKind kind = TextureContributionKind.ColorWithAlpha, int channel = 0, bool invert = false)
    {
        if (data.Texture(property) is not string guid) return null;
        var t = data.Textures[property];
        return new(guid, uv, t.sx, t.sy, t.ox, t.oy, kind, channel, invert);
    }
    internal static bool Moves(UnityMaterialData data, string property)
    {
        var v = data.Color(property, (0, 0, 0, 0));
        return v.r != 0 || v.g != 0 || v.b != 0 || v.a != 0;
    }
}
