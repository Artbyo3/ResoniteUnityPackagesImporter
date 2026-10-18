// Adapted from Resonite.UnitySDK's Poiyomi converters (MIT).
// Copyright (c) 2026 Yellow Dog Man Studios. See THIRD_PARTY_NOTICES.md.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityPackageImporter.Models;

namespace UnityPackageImporter.Materials.Poiyomi;

internal enum PoiyomiBlend { Opaque, Cutout, Alpha, Transparent, Additive, Multiply }
internal enum PoiyomiLighting { TextureRamp, MultilayerMath, Wrapped, Skin, ShadeMap, Flat, Realistic, Cloth, SDF }

/// <summary>Shader-specific decisions, independent of Unity and FrooxEngine.</summary>
internal static class PoiyomiSettings
{
    internal static bool Matches(UnityMaterialData data, string shaderName) =>
        (shaderName?.Contains(".poiyomi/", StringComparison.OrdinalIgnoreCase) ?? false) ||
        (shaderName?.Contains("Poiyomi", StringComparison.OrdinalIgnoreCase) ?? false) ||
        data.Keywords.Any(k => k.StartsWith("POI_", StringComparison.OrdinalIgnoreCase)) ||
        data.Numbers.Keys.Any(k => k.StartsWith("_Poi", StringComparison.OrdinalIgnoreCase)) ||
        new[] { "_ShadingEnabled", "_LightingMode", "_MochieBRDF", "_EnableEmission" }.All(data.Numbers.ContainsKey);

    internal static PoiyomiLighting Lighting(UnityMaterialData data) => (PoiyomiLighting)(int)data.Number("_LightingMode");
    internal static bool UsesPbs(UnityMaterialData data) => data.Number("_ShadingEnabled", 1) != 0 &&
        Lighting(data) is PoiyomiLighting.Wrapped or PoiyomiLighting.Realistic or PoiyomiLighting.Cloth or PoiyomiLighting.SDF;

    internal static PoiyomiBlend Blend(UnityMaterialData data)
    {
        if (data.Enabled("_AlphaForceOpaque")) return PoiyomiBlend.Opaque;
        if (data.Number("_DstBlend") == 10) return PoiyomiBlend.Transparent;
        if (data.Number("_DstBlend") == 1) return PoiyomiBlend.Additive;
        if (data.Number("_SrcBlend", 1) == 2) return PoiyomiBlend.Multiply;
        if (data.Enabled("_AlphaToCoverage") || data.Enabled("_AlphaDithering") || data.Enabled("_AlphaDistanceFade"))
            return PoiyomiBlend.Alpha;
        // Unity materials can omit shader defaults. In that case prefer their explicit render tag.
        if (!data.Numbers.ContainsKey("_SrcBlend") && !data.Numbers.ContainsKey("_DstBlend"))
            return data.RenderType switch { "Transparent" => PoiyomiBlend.Transparent, "TransparentCutout" => PoiyomiBlend.Cutout, _ => PoiyomiBlend.Opaque };
        return PoiyomiBlend.Cutout;
    }

    internal static (float r, float g, float b, float a) Emission(UnityMaterialData data)
    {
        if (!data.Enabled("_EnableEmission")) return (0, 0, 0, 1);
        var c = data.Color("_EmissionColor", (1, 1, 1, 1));
        float strength = data.Number("_EmissionStrength", 1);
        return (c.r * strength, c.g * strength, c.b * strength, c.a);
    }

    internal static TexturePixelTransform MetallicTransform(UnityMaterialData data, bool pbs)
    {
        int Channel(string suffix, int fallback) => Math.Clamp((int)data.Number("_MochieMetallicMaps" + suffix + "Channel", fallback), 0, 4);
        int Encode(int channel, string invert) => data.Enabled(invert) ? channel + 5 : channel;
        int metallic = Encode(Channel("Metallic", 0), "_MochieMetallicMapInvert");
        int smooth = Encode(Channel("Roughness", 3), "_MochieRoughnessMapInvert");
        return pbs ? TexturePixelTransform.Swizzle(metallic, 9, 9, smooth) :
            TexturePixelTransform.Swizzle(metallic, smooth,
                Encode(Channel("ReflectionMask", 4), "_MochieReflectionMaskInvert"),
                Encode(Channel("SpecularMask", 4), "_MochieSpecularMaskInvert"));
    }

    internal static IEnumerable<string> Warnings(UnityMaterialData data, bool pbs)
    {
        if (!data.Numbers.ContainsKey("_LightingMode") || !data.Numbers.ContainsKey("_ShadingEnabled"))
            yield return "Poiyomi lighting settings are incomplete; using default lighting. Shader defaults cannot be recovered from a missing or stripped shader.";
        if (!Enum.IsDefined(typeof(PoiyomiLighting), Lighting(data)))
            yield return "This Poiyomi lighting mode is unknown; using basic toon lighting.";
        if (pbs && (data.Enabled("_EnableOutlines") || data.Enabled("_EnableRimLighting") || data.Enabled("_MatcapEnable") || data.Enabled("_SubsurfaceScattering")))
            yield return "The SDK-compatible PBS path cannot reproduce toon outlines, rim lighting, MatCap or subsurface effects.";
        if (data.Enabled("_AlphaDithering") || data.Enabled("_AlphaDistanceFade") || data.Enabled("_AlphaToCoverage"))
            yield return "Advanced Poiyomi alpha effects are approximated with alpha blending.";
        if (data.Enabled("_MainColorAdjustToggle"))
            yield return "Main color adjustment is limited to toon saturation; hue, masks and other color adjustments are not baked.";
        if (data.Enabled("_DetailEnabled") && !pbs)
            yield return "Detail layers are not supported by the toon conversion.";
        if (data.Enabled("_VertexManipulationsEnabled"))
            yield return "Vertex manipulation is not reproduced; the PBS path only approximates height displacement.";
        if (data.Enabled("_EnableEmission1") || data.Enabled("_EnableEmission2") || data.Enabled("_EnableEmission3"))
            yield return "Additional emission layers are not converted.";
        if (data.Enabled("_ScrollingEmission") || data.Enabled("_EmissionScrollingEnabled") || data.Enabled("_EmissionBlinkingEnabled") || data.Enabled("_EnableGITDEmission"))
            yield return "Dynamic emission scrolling, blinking and lighting-dependent strength are not reproduced by static texture composition.";
        if (data.Enabled("_EnableDissolve") || data.Enabled("_DissolveEnabled") || data.Enabled("_EnableAudioLink"))
            yield return "Dissolve and audio-driven shader behavior are not converted.";
        if (data.Enabled("_SubsurfaceScattering") && data.Number("_SSSSpread", 1) <= 0)
            yield return "Invalid subsurface spread was clamped to keep the resulting material finite.";
    }
}
