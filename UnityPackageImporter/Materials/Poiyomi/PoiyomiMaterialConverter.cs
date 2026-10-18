// Runtime adaptation of Resonite.UnitySDK's Poiyomi converters.
// Copyright (c) 2026 Yellow Dog Man Studios. MIT; see THIRD_PARTY_NOTICES.md.
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;
using Renderite.Shared;
using UnityPackageImporter.Models;

namespace UnityPackageImporter.Materials.Poiyomi;

/// <summary>Maps serialized Poiyomi settings to native materials without executing package shaders.</summary>
internal sealed class PoiyomiMaterialConverter
{
    private readonly FileImportHelperTaskMaterial owner;
    private readonly UnityMaterialData data;
    private static string RampDirectory => Path.Combine(Path.GetTempPath(), "ResoniteRamps");
    private PoiyomiMaterialConverter(FileImportHelperTaskMaterial owner, UnityMaterialData data)
    { this.owner = owner; this.data = data; }

    internal static async Task<IAssetProvider<Material>> ConvertAsync(FileImportHelperTaskMaterial owner, UnityMaterialData data, bool forceToon)
    {
        var converter = new PoiyomiMaterialConverter(owner, data);
        bool pbs = !forceToon && PoiyomiSettings.UsesPbs(data);
        foreach (var warning in PoiyomiSettings.Warnings(data, pbs))
        {
            string msg = Path.GetFileName(owner.SourceFile) + ": " + warning;
            UnityPackageImporter.Warn(msg);
            owner.importer?.RegisterMaterialWarning(owner.MaterialGuid, msg);
        }
        return pbs ? await converter.Pbs() : await converter.Toon();
    }

    private float N(string key, float fallback = 0) => data.Number(key, fallback);
    private bool On(string key) => data.Enabled(key);
    private colorX C(string key, (float r, float g, float b, float a) fallback, bool linear = false) => Color(data.Color(key, fallback), linear);
    private static colorX Color((float r, float g, float b, float a) c, bool linear = false) =>
        new(c.r, c.g, c.b, c.a, linear ? ColorProfile.Linear : ColorProfile.sRGB);
    private BlendMode Blend => PoiyomiSettings.Blend(data) switch
    {
        PoiyomiBlend.Opaque => BlendMode.Opaque, PoiyomiBlend.Alpha => BlendMode.Alpha,
        PoiyomiBlend.Transparent => BlendMode.Transparent, PoiyomiBlend.Additive => BlendMode.Additive,
        PoiyomiBlend.Multiply => BlendMode.Multiply, _ => BlendMode.Cutout
    };

    private async Task Texture(string property, AssetRef<ITexture2D> target, Sync<float2> scale = null,
        Sync<float2> offset = null, bool normal = false, bool alphaRgb = false,
        TexturePixelTransform transform = null, bool ramp = false)
    {
        var guid = data.Texture(property);
        if (guid != null)
            await owner.AssignTextureAsync(guid, target, multiplyRgbByAlpha: alphaRgb, normalMap: normal,
                requiredForCompletion: property == "_MainTex", transform: transform, clamp: ramp, mipmaps: !ramp);
        await default(ToWorld);
        owner.importer.Session?.Check();
        if (data.Textures.TryGetValue(property, out var uv))
        {
            if (scale != null) scale.Value = new float2(uv.sx, uv.sy);
            if (offset != null) offset.Value = new float2(uv.ox, uv.oy);
        }
    }

    private async Task<PBS_Metallic> Pbs()
    {
        await default(ToWorld);
        owner.importer.Session?.Check();
        var p = owner.matslot.GetComponentOrAttach<PBS_Metallic>();
        p.BlendMode.Value = Blend;
        p.AlphaCutoff.Value = N("_Cutoff", 0.5f);
        p.OffsetFactor.Value = N("_OffsetFactor");
        p.OffsetUnits.Value = N("_OffsetUnits");
        p.RenderQueue.Value = data.RenderQueue;
        p.AlbedoColor.Value = C("_Color", (1, 1, 1, 1));
        p.EmissiveColor.Value = Color(PoiyomiSettings.Emission(data), true);
        p.NormalScale.Value = N("_BumpScale", 1);
        p.Metallic.Value = On("_MochieBRDF") ? N("_MochieMetallicMultiplier", 1) : 0;
        p.Smoothness.Value = On("_MochieBRDF") ? N("_MochieRoughnessMultiplier", 1) : 0;
        await Texture("_MainTex", p.AlbedoTexture, p.TextureScale, p.TextureOffset);
        await Texture("_BumpMap", p.NormalMap, normal: true);
        await Texture("_LightingAOMaps", p.OcclusionMap);
        if (On("_EnableEmission"))
        {
            var emissionPlan = PoiyomiEmissionPlan.Create(data);
            var main = data.Textures.TryGetValue("_MainTex", out var uv) ? uv : (guid: (string)null, sx: 1f, sy: 1f, ox: 0f, oy: 0f);
            if (emissionPlan.Inputs.Any(i => i.Uv != 0 || i.ScaleX != main.sx || i.ScaleY != main.sy || i.OffsetX != main.ox || i.OffsetY != main.oy))
                emissionPlan = new TextureCompositionPlan(emissionPlan.Inputs, "PBS emission requires the same UV0 transform as the albedo");
            await EmissionTextureImporter.AssignAsync(owner, emissionPlan, p.EmissiveMap,
                ready => p.EmissiveColor.Value = ready ? Color(PoiyomiSettings.Emission(data), true) : colorX.Black);
        }
        if (On("_MochieBRDF")) await Texture("_MochieMetallicMaps", p.MetallicMap, transform: PoiyomiSettings.MetallicTransform(data, true));
        if (On("_DetailEnabled"))
        {
            await Texture("_DetailTex", p.DetailAlbedoTexture, p.DetailTextureScale, p.DetailTextureOffset);
            await Texture("_DetailNormalMap", p.DetailNormalMap, normal: true);
            await default(ToWorld);
            owner.importer.Session?.Check();
            p.DetailNormalScale.Value = N("_DetailNormalMapScale", 1);
        }
        if (On("_VertexManipulationsEnabled"))
        {
            await Texture("_VertexManipulationHeightMask", p.HeightMap);
            await default(ToWorld);
            owner.importer.Session?.Check();
            p.HeightScale.Value = -N("_VertexManipulationHeight");
        }
        return p;
    }

    private async Task<XiexeToonMaterial> Toon()
    {
        await default(ToWorld);
        owner.importer.Session?.Check();
        var t = owner.matslot.GetComponentOrAttach<XiexeToonMaterial>();
        t.BlendMode.Value = Blend;
        t.ZWrite.Value = N("_ZWrite", 1) > 0 ? ZWrite.On : ZWrite.Off;
        t.AlphaClip.Value = N("_Cutoff", 0.5f);
        t.OffsetFactor.Value = N("_OffsetFactor");
        t.OffsetUnits.Value = N("_OffsetUnits");
        t.Culling.Value = (Culling)(int)N("_Cull", 2);
        t.ColorMask.Value = (ColorMask)(int)N("_ColorMask", 15);
        t.RenderQueue.Value = data.RenderQueue;
        t.Color.Value = C("_Color", (1, 1, 1, 1));
        t.Saturation.Value = On("_MainColorAdjustToggle") ? 1 + N("_Saturation") : 1;
        t.AlbedoUV.Value = (int)N("_MainTexUV");
        t.UseVertexColors.Value = On("_MainVertexColoringEnabled") && N("_MainVertexColoring") >= 0.5f;
        t.VertexColorInterpolationSpace.Value = On("_MainVertexColoringLinearSpace") ? ColorProfile.Linear : ColorProfile.sRGB;
        t.NormalScale.Value = N("_BumpScale", 1);
        t.NormalUV.Value = (int)N("_BumpMapUV");
        t.ShadowSharpness.Value = 0.5f;
        t.ShadowRampMaskScale.Value = new float2(1, 1);
        t.ShadowRim.Value = colorX.White;
        t.ShadowRimThreshold.Value = 1;
        t.ShadowRimRange.Value = 0.7f;
        t.ShadowRimSharpness.Value = 0.3f;
        t.EmissionColor.Value = Color(PoiyomiSettings.Emission(data), true);
        t.SpecularIntensity.Value = 0;
        t.Metallic.Value = 0;
        t.Glossiness.Value = 0;
        t.Reflectivity.Value = 1;
        t.RimIntensity.Value = 0;
        t.Outline.Value = XiexeToonMaterial.OutlineStyle.None;
        t.SubsurfaceColor.Value = colorX.Black;
        t.OcclusionColor.Value = colorX.Black;
        t.OcclusionUV.Value = (int)N("_LightingAOMapsUV");

        await Texture("_MainTex", t.MainTexture, t.MainTextureScale, t.MainTextureOffset);
        await Texture("_BumpMap", t.NormalMap, t.NormalMapScale, t.NormalMapOffset, normal: true);
        await Texture("_LightingAOMaps", t.OcclusionMap, t.OcclusionMapScale, t.OcclusionMapOffset);
        await Reflections(t);
        if (On("_EnableEmission"))
        {
            var emissionPlan = PoiyomiEmissionPlan.Create(data);
            await EmissionTextureImporter.AssignAsync(owner, emissionPlan, t.EmissionMap,
                ready => t.EmissionColor.Value = ready ? Color(PoiyomiSettings.Emission(data), true) : colorX.Black);
            await default(ToWorld);
            owner.importer.Session?.Check();
            t.EmissionUV.Value = emissionPlan.Uv;
            var input = emissionPlan.Inputs.Count == 0 ? null : emissionPlan.Inputs[0];
            t.EmissionMapScale.Value = input == null ? new float2(1, 1) : new float2(input.ScaleX, input.ScaleY);
            t.EmissionMapOffset.Value = input == null ? new float2(0, 0) : new float2(input.OffsetX, input.OffsetY);
        }
        if (On("_MatcapEnable"))
        {
            await Texture("_Matcap", t.Matcap, alphaRgb: true);
            await default(ToWorld);
            owner.importer.Session?.Check();
            var c = data.Color("_MatcapColor", (1, 1, 1, 1));
            float strength = N("_MatcapIntensity", 1) * c.a;
            t.MatcapTint.Value = Color((c.r * strength, c.g * strength, c.b * strength, c.a), true);
        }
        await default(ToWorld);
        owner.importer.Session?.Check();
        Rim(t);
        if (On("_EnableOutlines"))
        {
            t.Outline.Value = (On("_OutlineLit") && N("_OutlineShadowStrength") >= 0.5f) || N("_OutlineExpansionMode") == 1
                ? XiexeToonMaterial.OutlineStyle.Lit : XiexeToonMaterial.OutlineStyle.Emissive;
            t.OutlineWidth.Value = N("_LineWidth");
            t.OutlineColor.Value = C("_LineColor", (0, 0, 0, 1), true);
            t.OutlineAlbedoTint.Value = N("_OutlineTintMix") >= 0.5f;
            await Texture("_OutlineMask", t.OutlineMask);
        }
        if (On("_SubsurfaceScattering"))
        {
            await Texture("_SSSThicknessMap", t.ThicknessMap, t.ThicknessMapScale, t.ThicknessMapOffset);
            await default(ToWorld);
            owner.importer.Session?.Check();
            t.ThicknessUV.Value = (int)N("_SSSThicknessMapUV");
            t.SubsurfaceColor.Value = C("_SSSColor", (1, 1, 1, 1), true);
            t.SubsurfaceDistortion.Value = 3 * N("_SSSDistortion");
            t.SubsurfacePower.Value = 3 * N("_SSSStrength");
            t.SubsurfaceScale.Value = 3 / Math.Max(0.0001f, N("_SSSSpread", 1));
        }
        await Shadows(t);
        return t;
    }

    private async Task Reflections(XiexeToonMaterial t)
    {
        await default(ToWorld);
        owner.importer.Session?.Check();
        if (On("_MochieBRDF"))
        {
            t.MetallicUV.Value = (int)N("_MochieMetallicMapsUV");
            t.Metallic.Value = N("_MochieMetallicMultiplier", 1);
            t.Glossiness.Value = N("_MochieRoughnessMultiplier", 1);
            t.Reflectivity.Value = N("_MochieReflectionStrength", 1);
            t.SpecularIntensity.Value = 100 * N("_MochieSpecularStrength", 1);
            t.SpecularArea.Value = 0.5f;
            await Texture("_MochieMetallicMaps", t.MetallicGlossMap, t.MetallicGlossMapScale, t.MetallicGlossMapOffset,
                transform: PoiyomiSettings.MetallicTransform(data, false));
        }
        else if (On("_StylizedSpecular") && N("_StylizedReflectionMode") == 0)
        {
            t.SpecularIntensity.Value = 100 * N("_StylizedSpecularStrength", 1);
            t.SpecularArea.Value = 0.5f;
        }
        else if (On("_StylizedSpecular") && N("_StylizedReflectionMode") == 1 && On("_UseReflection"))
        {
            t.Metallic.Value = N("_Metallic");
            t.Reflectivity.Value = N("_Reflectance", 1);
            t.SpecularIntensity.Value = On("_ApplySpecular") ? 100 * N("_Smoothness") : 0;
            t.SpecularArea.Value = 1 - N("_SpecularBorder", 0.5f);
            t.Glossiness.Value = On("_ApplyReflection") ? N("_Smoothness") : 0;
            await Texture("_MetallicGlossMap", t.MetallicGlossMap, t.MetallicGlossMapScale, t.MetallicGlossMapOffset);
        }
    }

    private void Rim(XiexeToonMaterial t)
    {
        if (!On("_EnableRimLighting")) return;
        t.RimAttenuationEffect.Value = 1;
        t.RimThreshold.Value = 0.1f;
        t.RimColor.Value = C(N("_RimStyle") < 2 ? "_RimLightColor" : "_RimColor", (1, 1, 1, 1), true);
        if (N("_RimStyle") == 0)
        {
            t.RimAlbedoTint.Value = N("_RimBaseColorMix");
            t.RimIntensity.Value = N("_RimBrightness", 1);
            t.RimRange.Value = 1 - N("_RimWidth", 0.5f) * N("_RimSharpness", 0.5f);
            t.RimSharpness.Value = 1 - N("_RimSharpness", 0.5f);
        }
        else if (N("_RimStyle") == 1)
        {
            t.RimAlbedoTint.Value = 0;
            t.RimIntensity.Value = 1;
            t.RimSharpness.Value = On("_RimLight_FeatherOff") ? 0 : 0.5f;
            t.RimRange.Value = On("_RimLight_FeatherOff") ? 0.2f * (1 - N("_RimLight_Power")) : 1 - 0.8f * N("_RimLight_Power");
        }
        else
        {
            t.RimAlbedoTint.Value = N("_RimMainStrength");
            t.RimIntensity.Value = 1;
            t.RimSharpness.Value = 0.1f;
            t.RimRange.Value = 0.7f;
        }
    }

    private async Task Shadows(XiexeToonMaterial t)
    {
        if (N("_ShadingEnabled", 1) == 0) return;
        switch (PoiyomiSettings.Lighting(data))
        {
            case PoiyomiLighting.TextureRamp:
                await Texture("_ToonRamp", t.ShadowRamp, transform: TexturePixelTransform.ShadowTint(data, false), ramp: true);
                break;
            case PoiyomiLighting.MultilayerMath:
            case PoiyomiLighting.ShadeMap:
                await default(ToBackground);
                owner.importer.Session?.Check();
                var ramp = await owner.ImportLocalTextureFile(PoiyomiRamp.Save(RampDirectory, PoiyomiRamp.Generate(data)), "Poiyomi shadow ramp", clamp: true, mipmaps: false);
                await default(ToWorld);
                owner.importer.Session?.Check();
                t.ShadowRamp.Target = ramp;
                break;
            case PoiyomiLighting.Skin:
                await default(ToBackground);
                owner.importer.Session?.Check();
                using (var source = typeof(PoiyomiMaterialConverter).Assembly.GetManifestResourceStream("UnityPackageImporter.Materials.PoiyomiToonSkinRamp.png"))
                {
                    if (source == null) throw new InvalidDataException("Bundled Poiyomi skin ramp is missing.");
                    using var bytes = new MemoryStream();
                    source.CopyTo(bytes);
                    string path = PoiyomiRamp.Save(RampDirectory, bytes.ToArray());
                    var skin = await owner.ImportProcessedLocalTextureFile(path, "Poiyomi skin shadow ramp", TexturePixelTransform.ShadowTint(data, true));
                    await default(ToWorld);
                    owner.importer.Session?.Check();
                    t.ShadowRamp.Target = skin;
                }
                break;
        }
    }
}
