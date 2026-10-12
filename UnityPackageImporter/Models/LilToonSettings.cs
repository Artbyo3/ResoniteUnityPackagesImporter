// Conversion rules adapted from Resonite.UnitySDK's LilToonXiexeConverter.
// Copyright (c) 2026 Yellow Dog Man Studios. MIT; see THIRD_PARTY_NOTICES.md.
#nullable enable
using System;

namespace UnityPackageImporter.Models;

internal enum ToonBlend { Opaque, Cutout, Alpha, Additive }

/// <summary>Runtime-compatible SDK rules. No engine state or Unity editor dependencies.</summary>
internal static class LilToonSettings
{
    public static ToonBlend Blend(UnityMaterialData data, string? shaderName)
    {
        string leaf = shaderName?[(shaderName.LastIndexOf('/') + 1)..] ?? "";
        if (leaf.Contains("Multi", StringComparison.OrdinalIgnoreCase))
            return (int)data.Number("_TransparentMode") switch
            {
                1 or 5 => ToonBlend.Cutout,
                2 or 3 or 4 => ToonBlend.Alpha,
                6 => ToonBlend.Additive,
                _ => ToonBlend.Opaque
            };
        if (leaf.Contains("Refraction", StringComparison.OrdinalIgnoreCase)) return ToonBlend.Alpha;
        if (leaf.Contains("Cutout", StringComparison.OrdinalIgnoreCase)) return ToonBlend.Cutout;
        if (leaf.Contains("Transparent", StringComparison.OrdinalIgnoreCase) ||
            leaf.Contains("Overlay", StringComparison.OrdinalIgnoreCase) ||
            leaf.Contains("Fur", StringComparison.OrdinalIgnoreCase) ||
            (shaderName?.Contains("/[Optional] FurOnly/", StringComparison.OrdinalIgnoreCase) ?? false))
            return ToonBlend.Alpha;
        if (leaf.Contains("Gem", StringComparison.OrdinalIgnoreCase)) return ToonBlend.Additive;
        // Missing shader source: use its serialized RenderType, never the filename,
        // presence of an alpha mask, or an opaque shader's saved cutoff property.
        if (string.IsNullOrEmpty(shaderName))
            return data.RenderType switch
            {
                "TransparentCutout" => ToonBlend.Cutout,
                "Transparent" => ToonBlend.Alpha,
                _ => ToonBlend.Opaque
            };
        return ToonBlend.Opaque;
    }

    public static string? NormalProperty(UnityMaterialData data) =>
        data.Enabled("_UseBumpMap") ? "_BumpMap" :
        data.Enabled("_UseBump2ndMap") ? "_Bump2ndMap" : null;

    public static bool MatcapSupported(UnityMaterialData data) =>
        data.Enabled("_UseMatCap") && data.Number("_MatCapBlendMode") == 1 && data.Texture("_MatCapBlendMask") == null;

    public static bool OutlineEnabled(UnityMaterialData data, string? shaderName) =>
        data.Enabled("_UseOutline") || (shaderName?.Contains("Outline", StringComparison.OrdinalIgnoreCase) ?? false);

    public static float EmissionStrength(UnityMaterialData data) =>
        Math.Clamp(data.Number("_EmissionBlend", 1), 0, 1) *
        (1 - 0.625f * Math.Clamp(data.Number("_EmissionFluorescence"), 0, 1));

    public static bool NeedsMainBake(UnityMaterialData data) =>
        data.Enabled("_UseMain2ndTex") || data.Enabled("_UseMain3rdTex") ||
        data.Enabled("_MainGradationStrength") ||
        data.Color("_MainTexHSVG", (0, 1, 1, 1)) != (0f, 1f, 1f, 1f) ||
        (data.Enabled("_AlphaMaskMode") && data.Texture("_AlphaMask") != null);
}
