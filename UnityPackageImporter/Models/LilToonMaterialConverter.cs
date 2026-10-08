// Runtime adaptation of Resonite.UnitySDK LilToonXiexeConverter.
// Copyright (c) 2026 Yellow Dog Man Studios. MIT; see THIRD_PARTY_NOTICES.md.
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;
using Renderite.Shared;

namespace UnityPackageImporter.Models;

internal static class LilToonMaterialConverter
{
    internal static async Task<XiexeToonMaterial> ConvertAsync(
        FileImportHelperTaskMaterial owner, UnityMaterialData data, string shaderName, bool generic = false)
    {
        await default(ToWorld);
        owner.importer.Session?.Check();
        var toon = owner.matslot.GetComponentOrAttach<XiexeToonMaterial>();
        toon.Saturation.Value = 1;
        toon.Color.Value = Color(data.Color("_Color", data.Color("_BaseColor", (1, 1, 1, 1))), ColorProfile.sRGB);
        toon.BlendMode.Value = LilToonSettings.Blend(data, generic ? null : shaderName) switch
        {
            ToonBlend.Cutout => BlendMode.Cutout,
            ToonBlend.Alpha => BlendMode.Alpha,
            ToonBlend.Additive => BlendMode.Additive,
            _ => BlendMode.Opaque
        };
        toon.AlphaClip.Value = data.Number("_Cutoff", 0.5f);
        toon.ZWrite.Value = data.Number("_ZWrite", 1) > 0 ? ZWrite.On : ZWrite.Off;
        toon.Culling.Value = (Culling)(int)data.Number("_Cull", 2);
        toon.ColorMask.Value = (ColorMask)(int)data.Number("_ColorMask", 15);
        toon.RenderQueue.Value = data.RenderQueue;
        toon.ShadowRim.Value = colorX.White;
        toon.RimIntensity.Value = 0;
        toon.EmissionColor.Value = colorX.Black;
        toon.Outline.Value = XiexeToonMaterial.OutlineStyle.None;
        toon.ShadowRamp.Target = null;
        toon.MainTexture.Target = null;
        toon.NormalMap.Target = null;
        toon.EmissionMap.Target = null;
        toon.Matcap.Target = null;
        toon.OcclusionMap.Target = null;
        toon.OutlineMask.Target = null;
        toon.ShadowRampMask.Target = null;
        toon.MetallicGlossMap.Target = null;

        string main = new[] { "_MainTex", "_BaseMap", "_MainTexture", "_1st_ShadeMap" }
            .FirstOrDefault(property => data.Texture(property) != null);
        if (main != null) await Texture(main, toon.MainTexture, toon.MainTextureScale, toon.MainTextureOffset);
        if (!generic && LilToonSettings.NeedsMainBake(data))
            Warn("Main layers, color adjustment or alpha masking need Unity's lilToon baker; using the base texture and tint.");

        string normal = generic ? "_BumpMap" : LilToonSettings.NormalProperty(data);
        if (normal != null)
        {
            await Texture(normal, toon.NormalMap, toon.NormalMapScale, toon.NormalMapOffset);
            await default(ToWorld);
            toon.NormalScale.Value = data.Number(normal == "_Bump2ndMap" ? "_Bump2ndScale" : "_BumpScale", 1);
            toon.NormalUV.Value = normal == "_Bump2ndMap" ? (int)data.Number("_Bump2ndMap_UVMode") : 0;
        }

        if (generic || data.Enabled("_UseEmission"))
        {
            await Texture("_EmissionMap", toon.EmissionMap, toon.EmissionMapScale, toon.EmissionMapOffset);
            await default(ToWorld);
            var emission = data.Color("_EmissionColor", (0, 0, 0, 1));
            float strength = generic ? 1 : LilToonSettings.EmissionStrength(data) * emission.a;
            toon.EmissionColor.Value = Color((emission.r * strength, emission.g * strength, emission.b * strength, 1), ColorProfile.Linear);
            toon.EmissionUV.Value = (int)data.Number("_EmissionMap_UVMode");
            if (!generic && (data.Texture("_EmissionBlendMask") != null || data.Enabled("_EmissionMainStrength")))
                Warn("Emission masking/main-texture blending needs the Unity baker; using the emission texture.");
        }
        if (!generic && data.Enabled("_UseEmission2nd"))
            Warn("Second emission is not converted by the SDK-compatible path.");

        if (!generic && LilToonSettings.MatcapSupported(data))
        {
            await Texture("_MatCapTex", toon.Matcap);
            await default(ToWorld);
            var tint = data.Color("_MatCapColor", (1, 1, 1, 1));
            float strength = data.Number("_MatCapBlend", 1) * tint.a;
            toon.MatcapTint.Value = Color((tint.r * strength, tint.g * strength, tint.b * strength, 1), ColorProfile.sRGB);
        }
        else if (!generic && data.Enabled("_UseMatCap"))
            Warn("Only additive, unmasked MatCap is supported, matching the SDK.");

        if (!generic && LilToonSettings.OutlineEnabled(data, shaderName))
        {
            await Texture("_OutlineWidthMask", toon.OutlineMask);
            await default(ToWorld);
            toon.Outline.Value = data.Enabled("_OutlineEnableLighting") ? XiexeToonMaterial.OutlineStyle.Lit : XiexeToonMaterial.OutlineStyle.Emissive;
            toon.OutlineWidth.Value = data.Number("_OutlineWidth");
            toon.OutlineColor.Value = Color(data.Color("_OutlineColor", (0, 0, 0, 1)), ColorProfile.sRGB);
            toon.OutlineAlbedoTint.Value = data.Enabled("_OutlineLitApplyTex");
        }

        if (!generic && data.Enabled("_UseShadow"))
        {
            await Texture("_ShadowBorderMask", toon.OcclusionMap, toon.OcclusionMapScale, toon.OcclusionMapOffset);
            await default(ToWorld);
            var shadow = data.Color("_ShadowColor", (0.82f, 0.76f, 0.85f, 1));
            float strength = Math.Clamp(data.Number("_ShadowStrength", 1), 0, 1);
            toon.OcclusionColor.Value = Color((1 + (shadow.r - 1) * strength, 1 + (shadow.g - 1) * strength,
                1 + (shadow.b - 1) * strength, 1), ColorProfile.sRGB);
            await Texture("_ShadowStrengthMask", toon.ShadowRampMask, toon.ShadowRampMaskScale, toon.ShadowRampMaskOffset);
            await default(ToBackground);
            string path = LilToonRamp.GetOrCreateFile(Path.Combine(Path.GetTempPath(), "ResoniteRamps"), data);
            var ramp = await owner.ImportLocalTextureFile(path, "lilToon shadow ramp", clamp: true, mipmaps: false);
            var white = data.Texture("_ShadowStrengthMask") == null
                ? await owner.ImportLocalTextureFile(LilToonRamp.GetWhiteFile(Path.Combine(Path.GetTempPath(), "ResoniteRamps")),
                    "Unmasked lilToon shadows", clamp: true, mipmaps: false) : null;
            await default(ToWorld);
            owner.importer.Session?.Check();
            toon.ShadowRamp.Target = ramp;
            if (white != null) toon.ShadowRampMask.Target = white;
        }
        if (generic)
        {
            await Texture("_MetallicGlossMap", toon.MetallicGlossMap);
            await default(ToWorld);
            toon.Metallic.Value = data.Number("_Metallic");
            toon.Glossiness.Value = data.Number("_Smoothness", 0.5f);
        }
        await default(ToWorld);
        owner.importer.Session?.Check();
        return toon;

        void Warn(string message) => UnityPackageImporter.Warn(Path.GetFileName(owner.SourceFile) + ": " + message);
        async Task Texture(string property, AssetRef<ITexture2D> target, Sync<float2> scale = null, Sync<float2> offset = null)
        {
            var guid = data.Texture(property);
            if (guid == null) return;
            var texture = await owner.ImportTexture(guid, multiplyRgbByAlpha:
                !generic && (property == "_EmissionMap" || property == "_MatCapTex"),
                normalMap: property == "_BumpMap" || property == "_Bump2ndMap");
            await default(ToWorld);
            owner.importer.Session?.Check();
            target.Target = texture;
            var uv = data.Textures[property];
            if (scale != null) scale.Value = new float2(uv.sx, uv.sy);
            if (offset != null) offset.Value = new float2(uv.ox, uv.oy);
        }
    }

    private static colorX Color((float r, float g, float b, float a) c, ColorProfile profile) =>
        new(c.r, c.g, c.b, c.a, profile);
}
