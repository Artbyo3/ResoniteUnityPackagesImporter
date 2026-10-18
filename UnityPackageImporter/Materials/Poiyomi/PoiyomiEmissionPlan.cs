using System.Collections.Generic;
using UnityPackageImporter.Models;

namespace UnityPackageImporter.Materials.Poiyomi;

internal static class PoiyomiEmissionPlan
{
    internal static TextureCompositionPlan Create(UnityMaterialData data)
    {
        var inputs = new List<TextureContribution>();
        var map = EmissionPlanInputs.Texture(data, "_EmissionMap", (int)data.Number("_EmissionMapUV"));
        var mask = EmissionPlanInputs.Texture(data, "_EmissionMask", (int)data.Number("_EmissionMaskUV"),
            TextureContributionKind.ScalarMask, (int)data.Number("_EmissionMaskChannel"), data.Enabled("_EmissionMaskInvert"));
        if (map != null) inputs.Add(map);
        if (mask != null) inputs.Add(mask);
        string unsupported = null;
        if ((map != null && EmissionPlanInputs.Moves(data, "_EmissionMapPan")) ||
            (mask != null && EmissionPlanInputs.Moves(data, "_EmissionMaskPan"))) unsupported = "animated emission texture coordinates";
        if (data.Number("_EmissionMask0GlobalMask") > 0) unsupported = "global emission-mask blending";
        if (data.Enabled("_EmissionBaseColorAsMap")) unsupported = "emission blending with the fully shaded base color";
        return new(inputs, unsupported);
    }
}
