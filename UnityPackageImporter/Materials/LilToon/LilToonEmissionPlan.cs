using System.Collections.Generic;
using UnityPackageImporter.Models;

namespace UnityPackageImporter.Materials.LilToon;

internal static class LilToonEmissionPlan
{
    internal static TextureCompositionPlan Create(UnityMaterialData data)
    {
        var inputs = new List<TextureContribution>();
        var map = EmissionPlanInputs.Texture(data, "_EmissionMap", (int)data.Number("_EmissionMap_UVMode"));
        // lilToon multiplies all RGBA channels of the blend mask. Its mask samples transformed main UV0.
        var mask = EmissionPlanInputs.Texture(data, "_EmissionBlendMask", 0);
        if (mask != null)
        {
            var main = data.Textures.TryGetValue("_MainTex", out var t) ? t : (guid: (string)null, sx: 1f, sy: 1f, ox: 0f, oy: 0f);
            mask = mask with { ScaleX = mask.ScaleX * main.sx, ScaleY = mask.ScaleY * main.sy,
                OffsetX = main.ox * mask.ScaleX + mask.OffsetX, OffsetY = main.oy * mask.ScaleY + mask.OffsetY };
        }
        if (map != null) inputs.Add(map);
        if (mask != null) inputs.Add(mask);
        string unsupported = null;
        if ((map != null && EmissionPlanInputs.Moves(data, "_EmissionMap_ScrollRotate")) ||
            (mask != null && (EmissionPlanInputs.Moves(data, "_EmissionBlendMask_ScrollRotate") ||
                EmissionPlanInputs.Moves(data, "_MainTex_ScrollRotate")))) unsupported = "animated emission texture coordinates";
        if (data.Enabled("_EmissionMainStrength")) unsupported = "emission blending with the fully shaded base color";
        if (data.Enabled("_EmissionUseGrad") || data.Enabled("_EmissionParallaxDepth")) unsupported = "dynamic emission gradients or parallax";
        if (data.Color("_EmissionBlink", (0, 0, 0, 0)).g != 0) unsupported = "animated emission blinking";
        if (data.Numbers.TryGetValue("_EmissionBlendMode", out float blend) && blend != 1) unsupported = "non-additive emission blending";
        return new(inputs, unsupported);
    }
}
