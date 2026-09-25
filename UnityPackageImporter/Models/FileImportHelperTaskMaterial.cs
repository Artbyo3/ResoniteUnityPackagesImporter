using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.Store;
using Renderite.Shared;

namespace UnityPackageImporter.Models;

public class FileImportHelperTaskMaterial
{
    private string file;
    private string myID;
    public Slot assetsRoot;
    public Slot matslot;
    public UnityProjectImporter importer;
    public IAssetProvider<Material> finalMaterial;
    public bool ismissing = false;

    public static string materialNameIdentifyEndingPrefab = " - Material";
    private static readonly HashSet<string> LilToonGuids = new(StringComparer.OrdinalIgnoreCase)
    {
        "efa77a80ca0344749b4f19fdd5891cbe", // lilToon main VRChat shader
        "9294844b15dca184d914a632279b24e1", // lilToon standard / opaque
        "3c79b10c7e0b2784aaa4c2f8dd17d55e", // lts_trans_o (transparent outline)
        "c462372f7fb70584eb2e3a09fb2215c0", // lts_cutout
        "2ee4f828a274151478546b5a37466dd2", // lts_trans
        "c1630c1e8555238479e000fbc9822a10", // lts_o (opaque outline)
        "850fc1e041d8e1346be7f5244dc049fa", // lts_twopass
        "4f82b88126eecde47bc9548f0bc2e54a", // lts_multi
        "51b2dee0ab07bd84d8147601ff89e511", // lilToon wings/effects variant
        "165365ab7100a044ca85fc8c33548a62", // lilToon cutout variant (face_alpha, tights, etc.)
    };

    private static readonly HashSet<string> LilToonCutoutGuids = new(StringComparer.OrdinalIgnoreCase)
    {
        "c462372f7fb70584eb2e3a09fb2215c0", // lts_cutout
        "165365ab7100a044ca85fc8c33548a62", // lilToon cutout variant
    };

    private static readonly HashSet<string> LilToonTransparentGuids = new(StringComparer.OrdinalIgnoreCase)
    {
        "3c79b10c7e0b2784aaa4c2f8dd17d55e", // lts_trans_o
        "2ee4f828a274151478546b5a37466dd2", // lts_trans
        "850fc1e041d8e1346be7f5244dc049fa", // lts_twopass
    };

    public FileImportHelperTaskMaterial(string myID, string file, UnityProjectImporter importer)
    {
        this.importer = importer;
        this.file = file;
        this.myID = myID;
        UnityPackageImporter.Msg("Importing material with ID: \"" + myID + "\" from file: " + file);
        assetsRoot = importer.importTaskAssetRoot;
        matslot = assetsRoot.FindChildOrAdd(Path.GetFileNameWithoutExtension(this.file) + " [" + myID + "]" + materialNameIdentifyEndingPrefab);
    }

    // To assign a material to a missing material during import if the fbx doesn't have a definition for it
    public FileImportHelperTaskMaterial(UnityProjectImporter importer)
    {
        this.importer = importer;
        UnityPackageImporter.Msg("Importing null/missing material fallback");
        assetsRoot = importer.importTaskAssetRoot;
        matslot = assetsRoot.FindChildOrAdd("Missing_Material_Fallback" + materialNameIdentifyEndingPrefab);
        var pbs = matslot.GetComponentOrAttach<FrooxEngine.PBS_Metallic>();
        pbs.EmissiveColor.Value = new Elements.Core.colorX(0f, 0f, 0f, 1f);
        pbs.AlbedoColor.Value = new Elements.Core.colorX(0.75f, 0.75f, 0.75f, 1f);
        pbs.Metallic.Value = 0f;
        pbs.Smoothness.Value = 0.3f;
        finalMaterial = pbs;
        ismissing = true;
    }

    public async Task<IAssetProvider<Material>> runImportFileMaterialsAsync()
    {
        await default(ToBackground);
        if (ismissing) return finalMaterial;
        finalMaterial = await importer.MaterialImports.GetOrAdd(myID, ImportFileMaterial);
        return finalMaterial;
    }

    private async Task<IAssetProvider<Material>> ImportFileMaterial()
    {
        await default(ToBackground);
        var lines = File.ReadLines(file).ToArray();

        // 1. Detect if this is a lilToon material (and not Poiyomi)
        bool isPoiyomi = lines.Any(l => l.Contains("POI_") || l.IndexOf("Poiyomi", StringComparison.OrdinalIgnoreCase) >= 0);
        bool isLilToon = false;
        if (!isPoiyomi)
        {
            foreach (var line in lines)
            {
                if (line.Contains("_lilToonVersion:"))
                {
                    isLilToon = true;
                    break;
                }
                if (line.StartsWith("  m_Shader:") && (line.IndexOf("lilToon", StringComparison.OrdinalIgnoreCase) >= 0 || LilToonGuids.Any(g => line.Contains(g))))
                {
                    isLilToon = true;
                    break;
                }
                if (line.Contains("- _BackfaceForceShadow:") ||
                    line.Contains("- _ShadowColorTex:") ||
                    line.Contains("- _Shadow2ndColorTex:") ||
                    (line.Contains("- _Shadow2ndColor:") && !line.Contains("_Shadow2ndColorTex:")))
                {
                    isLilToon = true;
                    break;
                }
            }
        }

        // 2. Parse YAML structures (textures, floats, colors)
        string currentSection = "";
        string currentTex = "";
        var textures = new Dictionary<string, (string guid, float sx, float sy, float ox, float oy)>();
        var floats = new Dictionary<string, float>();
        var colors = new Dictionary<string, (float r, float g, float b, float a)>();
        bool isCutout = false;
        bool isTransparent = false;

        string matNameLower = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
        if (matNameLower.Contains("alpha") || matNameLower.Contains("cutout") || matNameLower.Contains("option") ||
            matNameLower.Contains("tear") || matNameLower.Contains("blush") || matNameLower.Contains("eyeshadow"))
        {
            isCutout = true;
        }

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("RenderType: TransparentCutout") || line.Contains("TransparentCutout") || line.IndexOf("cutout", StringComparison.OrdinalIgnoreCase) >= 0)
                isCutout = true;
            if ((line.StartsWith("RenderType: Transparent") || line.IndexOf("transparent", StringComparison.OrdinalIgnoreCase) >= 0) && !line.Contains("TransparentCutout"))
                isTransparent = true;
            if (line.StartsWith("  m_Shader:"))
            {
                if (LilToonCutoutGuids.Any(g => line.Contains(g))) isCutout = true;
                if (LilToonTransparentGuids.Any(g => line.Contains(g))) isTransparent = true;
            }

            if (line.StartsWith("m_TexEnvs:")) { currentSection = "tex"; continue; }
            if (line.StartsWith("m_Floats:")) { currentSection = "floats"; continue; }
            if (line.StartsWith("m_Colors:")) { currentSection = "colors"; continue; }

            if (currentSection == "tex")
            {
                if (line.StartsWith("- _"))
                {
                    currentTex = line.Substring(2).TrimEnd(':').Trim();
                }
                else if (line.StartsWith("m_Texture:") && !string.IsNullOrEmpty(currentTex))
                {
                    int guidIdx = line.IndexOf("guid:");
                    if (guidIdx >= 0)
                    {
                        string guidPart = line.Substring(guidIdx + 5).Trim();
                        int endGuid = guidPart.IndexOfAny(new[] { ',', ' ', '}' });
                        string guid = (endGuid >= 0) ? guidPart.Substring(0, endGuid).Trim() : guidPart.Trim();

                        float sx = 1, sy = 1, ox = 0, oy = 0;
                        if (i + 1 < lines.Length && lines[i + 1].Trim().StartsWith("m_Scale:"))
                        {
                            ParseVec2(lines[i + 1], out sx, out sy);
                        }
                        if (i + 2 < lines.Length && lines[i + 2].Trim().StartsWith("m_Offset:"))
                        {
                            ParseVec2(lines[i + 2], out ox, out oy);
                        }
                        textures[currentTex] = (guid, sx, sy, ox, oy);
                    }
                }
            }
            else if (currentSection == "floats")
            {
                if (line.StartsWith("- _"))
                {
                    int colon = line.IndexOf(':');
                    if (colon > 0)
                    {
                        string name = line.Substring(2, colon - 2).Trim();
                        string valStr = line.Substring(colon + 1).Trim();
                        if (float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float fVal))
                        {
                            floats[name] = fVal;
                        }
                    }
                }
            }
            else if (currentSection == "colors")
            {
                if (line.StartsWith("- _"))
                {
                    int colon = line.IndexOf(':');
                    if (colon > 0)
                    {
                        string name = line.Substring(2, colon - 2).Trim();
                        string valStr = line.Substring(colon + 1).Trim();
                        if (ParseColor(valStr, out float r, out float g, out float b, out float a))
                        {
                            colors[name] = (r, g, b, a);
                        }
                    }
                }
            }
        }

        if (isLilToon)
        {
            try
            {
                UnityPackageImporter.Msg("Converting lilToon material: " + Path.GetFileNameWithoutExtension(file) + " to XiexeToonMaterial");
                await default(ToWorld);
                var toon = matslot.GetComponentOrAttach<XiexeToonMaterial>();
                finalMaterial = toon;
                await default(ToBackground);

                // Textures mapping
                string mainTexGuid = GetTexGuid(textures, "_MainTex") ?? GetTexGuid(textures, "_BaseMap") ?? GetTexGuid(textures, "_MainTexture");
                string main2ndTexGuid = GetTexGuid(textures, "_Main2ndTex");
                string main3rdTexGuid = GetTexGuid(textures, "_Main3rdTex");
                string alphaMaskGuid = GetTexGuid(textures, "_AlphaMask");

                bool hasMain2nd = main2ndTexGuid != null && importer.AssetIDDict.ContainsKey(main2ndTexGuid);
                bool hasMain3rd = main3rdTexGuid != null && importer.AssetIDDict.ContainsKey(main3rdTexGuid);
                bool hasAlphaMask = alphaMaskGuid != null && importer.AssetIDDict.ContainsKey(alphaMaskGuid) &&
                                   (floats.TryGetValue("_AlphaMaskMode", out float amm) ? amm > 0f : false);

                StaticTexture2D finalMainTexture = null;
                var mainTexData = GetTexData(textures, "_MainTex", "_BaseMap", "_MainTexture");

                if (mainTexGuid != null && importer.AssetIDDict.TryGetValue(mainTexGuid, out var mainDiskPath) && (hasMain2nd || hasMain3rd || hasAlphaMask))
                {
                    var baseCol = (colors.TryGetValue("_Color", out var cVal) || colors.TryGetValue("_BaseColor", out cVal))
                        ? (cVal.r, cVal.g, cVal.b, cVal.a) : (1f, 1f, 1f, 1f);

                    string m2Path = hasMain2nd ? importer.AssetIDDict[main2ndTexGuid] : null;
                    string m2MaskGuid = GetTexGuid(textures, "_Main2ndBlendMask");
                    string m2MaskPath = (m2MaskGuid != null && importer.AssetIDDict.TryGetValue(m2MaskGuid, out var m2m)) ? m2m : null;
                    int m2Mode = floats.TryGetValue("_Main2ndTexBlendMode", out float m2mFloat) ? (int)m2mFloat : 0;
                    var m2Col = (colors.TryGetValue("_Color2nd", out var c2Val) || colors.TryGetValue("_Main2ndColor", out c2Val))
                        ? (c2Val.r, c2Val.g, c2Val.b, c2Val.a) : (1f, 1f, 1f, 1f);

                    string m3Path = hasMain3rd ? importer.AssetIDDict[main3rdTexGuid] : null;
                    string m3MaskGuid = GetTexGuid(textures, "_Main3rdBlendMask");
                    string m3MaskPath = (m3MaskGuid != null && importer.AssetIDDict.TryGetValue(m3MaskGuid, out var m3m)) ? m3m : null;
                    int m3Mode = floats.TryGetValue("_Main3rdTexBlendMode", out float m3mFloat) ? (int)m3mFloat : 0;
                    var m3Col = (colors.TryGetValue("_Color3rd", out var c3Val) || colors.TryGetValue("_Main3rdColor", out c3Val))
                        ? (c3Val.r, c3Val.g, c3Val.b, c3Val.a) : (1f, 1f, 1f, 1f);

                    string aMaskPath = hasAlphaMask ? importer.AssetIDDict[alphaMaskGuid] : null;

                    string cacheDir = Path.Combine(Path.GetTempPath(), "ResoniteComposites");
                    string compFile = TextureCompositor.GetOrCreateCompositeMainTex(
                        cacheDir,
                        mainDiskPath, baseCol,
                        m2Path, m2MaskPath, m2Mode, m2Col,
                        m3Path, m3MaskPath, m3Mode, m3Col,
                        aMaskPath, out string compHash);

                    if (compFile != null)
                    {
                        finalMainTexture = await importer.CompositeImports.GetOrAdd(compHash, async () =>
                        {
                            return await ImportLocalTextureFile(compFile, Path.GetFileNameWithoutExtension(file) + " - CompositedMainTex [" + compHash.Substring(0, 6) + "]");
                        });
                    }
                }

                if (finalMainTexture == null && mainTexGuid != null)
                {
                    finalMainTexture = await ImportTexture(mainTexGuid);
                }

                if (finalMainTexture != null)
                {
                    await default(ToWorld);
                    toon.MainTexture.Target = finalMainTexture;
                    toon.MainTextureScale.Value = new float2(mainTexData.sx, mainTexData.sy);
                    toon.MainTextureOffset.Value = new float2(mainTexData.ox, mainTexData.oy);
                    await default(ToBackground);
                }

                string normalGuid = GetTexGuid(textures, "_BumpMap") ?? GetTexGuid(textures, "_Bump2ndMap");
                if (normalGuid != null)
                {
                    var texData = GetTexData(textures, "_BumpMap", "_Bump2ndMap");
                    var tex = await ImportTexture(normalGuid);
                    await default(ToWorld);
                    toon.NormalMap.Target = tex;
                    toon.NormalMapScale.Value = new float2(texData.sx, texData.sy);
                    toon.NormalMapOffset.Value = new float2(texData.ox, texData.oy);
                    await default(ToBackground);
                }

                string metallicGuid = GetTexGuid(textures, "_MetallicGlossMap");
                string smoothnessGuid = GetTexGuid(textures, "_SmoothnessTex");
                StaticTexture2D finalMetallicTexture = null;

                float mFloat = floats.TryGetValue("_Metallic", out float mf) ? mf : 0f;
                float sFloat = floats.TryGetValue("_Smoothness", out float sf) ? sf : 0.5f;

                if (metallicGuid != null && smoothnessGuid != null && metallicGuid != smoothnessGuid &&
                    importer.AssetIDDict.TryGetValue(metallicGuid, out var mDisk) &&
                    importer.AssetIDDict.TryGetValue(smoothnessGuid, out var sDisk))
                {
                    string cacheDir = Path.Combine(Path.GetTempPath(), "ResoniteComposites");
                    string packedFile = TextureCompositor.GetOrCreatePackedMetallicGlossMap(cacheDir, mDisk, mFloat, sDisk, sFloat, out string packHash);
                    if (packedFile != null)
                    {
                        finalMetallicTexture = await importer.CompositeImports.GetOrAdd(packHash, async () =>
                        {
                            return await ImportLocalTextureFile(packedFile, Path.GetFileNameWithoutExtension(file) + " - PackedMetallicSmoothness [" + packHash.Substring(0, 6) + "]");
                        });
                    }
                }
                else if (metallicGuid != null)
                {
                    finalMetallicTexture = await ImportTexture(metallicGuid);
                }

                if (finalMetallicTexture != null)
                {
                    await default(ToWorld);
                    toon.MetallicGlossMap.Target = finalMetallicTexture;
                    await default(ToBackground);
                }

                string emissionGuid = GetTexGuid(textures, "_EmissionMap") ?? GetTexGuid(textures, "_Emission2ndMap");
                if (emissionGuid != null)
                {
                    var tex = await ImportTexture(emissionGuid);
                    await default(ToWorld);
                    toon.EmissionMap.Target = tex;
                    await default(ToBackground);
                }

                string occlusionGuid = GetTexGuid(textures, "_OcclusionMap");
                if (occlusionGuid != null)
                {
                    var tex = await ImportTexture(occlusionGuid);
                    await default(ToWorld);
                    toon.OcclusionMap.Target = tex;
                    await default(ToBackground);
                }

                string outlineMaskGuid = GetTexGuid(textures, "_OutlineWidthMask") ?? GetTexGuid(textures, "_OutlineMask");
                if (outlineMaskGuid != null)
                {
                    var tex = await ImportTexture(outlineMaskGuid);
                    await default(ToWorld);
                    toon.OutlineMask.Target = tex;
                    await default(ToBackground);
                }

                string shadowMaskGuid = GetTexGuid(textures, "_ShadowBorderMask") ?? GetTexGuid(textures, "_ShadowStrengthMask");
                if (shadowMaskGuid != null)
                {
                    var maskTex = await ImportTexture(shadowMaskGuid);
                    var maskData = GetTexData(textures, "_ShadowBorderMask", "_ShadowStrengthMask");
                    await default(ToWorld);
                    toon.ShadowRampMask.Target = maskTex;
                    toon.ShadowRampMaskScale.Value = new float2(maskData.sx, maskData.sy);
                    toon.ShadowRampMaskOffset.Value = new float2(maskData.ox, maskData.oy);
                    await default(ToBackground);
                }

                string sssGuid = GetTexGuid(textures, "_BacklightColorTex") ?? GetTexGuid(textures, "_TranslucencyMap");
                if (sssGuid != null)
                {
                    var tex = await ImportTexture(sssGuid);
                    await default(ToWorld);
                    toon.ThicknessMap.Target = tex;
                    if (colors.TryGetValue("_BacklightColor", out var blCol))
                    {
                        toon.SubsurfaceColor.Value = new colorX(blCol.r, blCol.g, blCol.b, blCol.a);
                    }
                    if (floats.TryGetValue("_BacklightDirectivity", out float bld))
                    {
                        toon.SubsurfacePower.Value = bld;
                    }
                    if (floats.TryGetValue("_BacklightMainStrength", out float bls))
                    {
                        toon.SubsurfaceScale.Value = bls;
                    }
                    await default(ToBackground);
                }

                string matcapGuid = GetTexGuid(textures, "_MatCapTex") ?? GetTexGuid(textures, "_MatCap2ndTex");
                if (matcapGuid != null)
                {
                    var tex = await ImportTexture(matcapGuid);
                    await default(ToWorld);
                    toon.Matcap.Target = tex;
                    await default(ToBackground);
                }

                // Shadow Ramp: texture or procedural generation from LilToon shadow inputs
                string shadowTexGuid = GetTexGuid(textures, "_ShadowColorTex") ?? GetTexGuid(textures, "_Shadow2ndColorTex");
                StaticTexture2D rampTexture = null;
                if (shadowTexGuid != null)
                {
                    rampTexture = await ImportTexture(shadowTexGuid);
                }
                else
                {
                    bool hasShadow = colors.ContainsKey("_ShadowColor") || colors.ContainsKey("_Shadow2ndColor") || floats.ContainsKey("_ShadowBorder");
                    if (hasShadow)
                    {
                        var baseCol = (colors.TryGetValue("_Color", out var bc) || colors.TryGetValue("_BaseColor", out bc))
                            ? new RampColor(bc.r, bc.g, bc.b, 1f) : RampColor.White;

                        var sh1Col = colors.TryGetValue("_ShadowColor", out var sc1)
                            ? new RampColor(sc1.r, sc1.g, sc1.b, 1f)
                            : new RampColor(0.7f, 0.7f, 0.7f, 1f);

                        float border1 = floats.TryGetValue("_ShadowBorder", out float sb1) ? sb1 : 0.5f;
                        float blur1 = floats.TryGetValue("_ShadowBlur", out float sbl1) ? sbl1 : 0.1f;

                        var sh2Col = colors.TryGetValue("_Shadow2ndColor", out var sc2)
                            ? new RampColor(sc2.r, sc2.g, sc2.b, 1f)
                            : new RampColor(sh1Col.R * 0.75f, sh1Col.G * 0.75f, sh1Col.B * 0.75f, 1f);

                        float border2 = floats.TryGetValue("_Shadow2ndBorder", out float sb2) ? sb2 : border1 * 0.5f;
                        float blur2 = floats.TryGetValue("_Shadow2ndBlur", out float sbl2) ? sbl2 : blur1;

                        string hash = RampGenerator.GetRampHash(baseCol, sh1Col, border1, blur1, sh2Col, border2, blur2);
                        rampTexture = await importer.RampImports.GetOrAdd(hash, async () =>
                        {
                            try
                            {
                                string cacheDir = Path.Combine(Path.GetTempPath(), "ResoniteRamps");
                                string rampPath = RampGenerator.GetOrCreateRampFile(cacheDir, baseCol, sh1Col, border1, blur1, sh2Col, border2, blur2);
                                return await ImportLocalTextureFile(rampPath, Path.GetFileNameWithoutExtension(file) + " - ShadowRamp [" + hash.Substring(0, 6) + "]", clamp: true, mipmaps: false);
                            }
                            catch (Exception ex)
                            {
                                UnityPackageImporter.Warn("Failed to generate/import shadow ramp: " + ex.Message);
                                return null;
                            }
                        });
                    }
                }

                if (rampTexture != null)
                {
                    await default(ToWorld);
                    toon.ShadowRamp.Target = rampTexture;
                    toon.ShadowSharpness.Value = 1f;
                    await default(ToBackground);
                }

                // Floats mapping
                await default(ToWorld);
                if (floats.TryGetValue("_Metallic", out float metallic)) toon.Metallic.Value = metallic;
                if (floats.TryGetValue("_Smoothness", out float smoothness)) toon.Glossiness.Value = smoothness;
                if (floats.TryGetValue("_BumpScale", out float bumpScale)) toon.NormalScale.Value = bumpScale;
                if (floats.TryGetValue("_Cutoff", out float cutoff)) toon.AlphaClip.Value = cutoff;
                if (floats.TryGetValue("_ZWrite", out float zw)) toon.ZWrite.Value = (zw == 0f) ? ZWrite.Off : ZWrite.On;

                if (floats.TryGetValue("_OutlineWidth", out float outlineWidth) && outlineWidth > 0f)
                {
                    // In lilToon, _OutlineWidth is in millimeters (e.g. 0.05 = 0.05mm).
                    // In Resonite XiexeToonMaterial, OutlineWidth is in world units / meters (0.05mm = 0.00005m).
                    float mappedWidth = (outlineWidth > 0.001f) ? (outlineWidth / 1000f) : outlineWidth;
                    toon.OutlineWidth.Value = Math.Clamp(mappedWidth, 0.000005f, 0.005f);
                    bool hasOutlineTex = textures.ContainsKey("_OutlineTex") && GetTexGuid(textures, "_OutlineTex") != null;
                    bool isLit = (floats.TryGetValue("_OutlineLightingMix", out float mix) && mix > 0.5f) || hasOutlineTex;
                    toon.Outline.Value = isLit ? XiexeToonMaterial.OutlineStyle.Lit : XiexeToonMaterial.OutlineStyle.Emissive;
                    toon.OutlineAlbedoTint.Value = isLit;
                }
                else
                {
                    toon.Outline.Value = XiexeToonMaterial.OutlineStyle.None;
                }

                if (floats.TryGetValue("_Cull", out float cull))
                {
                    toon.Culling.Value = (cull == 0f) ? Culling.Off : ((cull == 1f) ? Culling.Front : Culling.Back);
                }

                // BlendMode
                if (hasAlphaMask)
                {
                    toon.BlendMode.Value = BlendMode.Cutout;
                    if (floats.TryGetValue("_Cutoff", out float cutVal) && cutVal > 0f)
                        toon.AlphaClip.Value = cutVal;
                    else
                        toon.AlphaClip.Value = 0.5f;
                }
                else if (floats.TryGetValue("_TransparentMode", out float transMode))
                {
                    if (transMode == 1f)
                    {
                        toon.BlendMode.Value = BlendMode.Cutout;
                        if (toon.AlphaClip.Value <= 0.01f) toon.AlphaClip.Value = 0.5f;
                    }
                    else if (transMode == 2f)
                    {
                        toon.BlendMode.Value = BlendMode.Alpha;
                    }
                    else
                    {
                        toon.BlendMode.Value = BlendMode.Opaque;
                    }
                }
                else if (isCutout || (floats.TryGetValue("_Cutoff", out float c) && c > 0f && c < 0.99f))
                {
                    toon.BlendMode.Value = BlendMode.Cutout;
                    if (toon.AlphaClip.Value <= 0.01f) toon.AlphaClip.Value = 0.5f;
                }
                else if (isTransparent)
                {
                    toon.BlendMode.Value = BlendMode.Alpha;
                }
                else
                {
                    toon.BlendMode.Value = BlendMode.Opaque;
                }

                if (floats.TryGetValue("_Saturation", out float sat))
                {
                    toon.Saturation.Value = sat;
                }

                // Colors mapping
                if (colors.TryGetValue("_Color", out var col) || colors.TryGetValue("_BaseColor", out col))
                {
                    toon.Color.Value = new colorX(col.r, col.g, col.b, col.a);
                }
                if (colors.TryGetValue("_EmissionColor", out var emCol) || colors.TryGetValue("_Emission2ndColor", out emCol))
                {
                    toon.EmissionColor.Value = new colorX(emCol.r, emCol.g, emCol.b, emCol.a);
                }
                if (colors.TryGetValue("_OutlineColor", out var outCol))
                {
                    toon.OutlineColor.Value = new colorX(outCol.r, outCol.g, outCol.b, outCol.a);
                }
                if (colors.TryGetValue("_MatCapColor", out var mcCol) || colors.TryGetValue("_MatCap2ndColor", out mcCol))
                {
                    toon.MatcapTint.Value = new colorX(mcCol.r, mcCol.g, mcCol.b, mcCol.a);
                }
                if (colors.TryGetValue("_ReflectionColor", out var reflCol))
                {
                    float reflLum = reflCol.r * 0.3f + reflCol.g * 0.59f + reflCol.b * 0.11f;
                    toon.SpecularIntensity.Value = reflLum;
                    toon.Reflectivity.Value = reflLum;
                }
                if (colors.TryGetValue("_RimColor", out var rimCol))
                {
                    toon.RimColor.Value = new colorX(rimCol.r, rimCol.g, rimCol.b, rimCol.a);
                    if (floats.TryGetValue("_RimBorder", out float rimBorder)) toon.RimThreshold.Value = rimBorder;
                    if (floats.TryGetValue("_RimBlur", out float rimBlur)) toon.RimSharpness.Value = 1f - Math.Clamp(rimBlur, 0f, 1f);
                    if (floats.TryGetValue("_RimFresnelPower", out float rimPower)) toon.RimRange.Value = rimPower;
                    toon.RimIntensity.Value = 1f;
                }
                await default(ToBackground);
                return finalMaterial;
            }
            catch (Exception ex)
            {
                UnityPackageImporter.Warn("LilToon conversion failed for " + Path.GetFileNameWithoutExtension(file) + ", falling back to PBS_Metallic: " + ex.Message);
            }
        }

        // Standard / PBS Metallic fallback
        {
            UnityPackageImporter.Msg("Importing Standard/PBS material: " + Path.GetFileNameWithoutExtension(file));
            await default(ToWorld);
            var pbs = matslot.GetComponentOrAttach<PBS_Metallic>();
            finalMaterial = pbs;
            await default(ToBackground);

            string fbMainTexGuid = GetTexGuid(textures, "_MainTex") ?? GetTexGuid(textures, "_BaseMap") ?? GetTexGuid(textures, "_MainTexture") ?? GetTexGuid(textures, "_1st_ShadeMap");
            if (fbMainTexGuid != null)
            {
                var texData = GetTexData(textures, "_MainTex", "_BaseMap", "_MainTexture", "_1st_ShadeMap");
                var tex = await ImportTexture(fbMainTexGuid);
                await default(ToWorld);
                pbs.AlbedoTexture.Target = tex;
                pbs.TextureScale.Value = new float2(texData.sx, texData.sy);
                pbs.TextureOffset.Value = new float2(texData.ox, texData.oy);
                await default(ToBackground);
            }

            string normalGuid = GetTexGuid(textures, "_BumpMap");
            if (normalGuid != null)
            {
                var tex = await ImportTexture(normalGuid);
                await default(ToWorld);
                pbs.NormalMap.Target = tex;
                await default(ToBackground);
            }

            string metallicGuid = GetTexGuid(textures, "_MetallicGlossMap");
            if (metallicGuid != null)
            {
                var tex = await ImportTexture(metallicGuid);
                await default(ToWorld);
                pbs.MetallicMap.Target = tex;
                await default(ToBackground);
            }

            string emissionGuid = GetTexGuid(textures, "_EmissionMap");
            if (emissionGuid != null)
            {
                var tex = await ImportTexture(emissionGuid);
                await default(ToWorld);
                pbs.EmissiveMap.Target = tex;
                await default(ToBackground);
            }

            string occlusionGuid = GetTexGuid(textures, "_OcclusionMap");
            if (occlusionGuid != null)
            {
                var tex = await ImportTexture(occlusionGuid);
                await default(ToWorld);
                pbs.OcclusionMap.Target = tex;
                await default(ToBackground);
            }

            await default(ToWorld);
            if (floats.TryGetValue("_Metallic", out float metallic)) pbs.Metallic.Value = metallic;
            if (floats.TryGetValue("_Smoothness", out float smoothness)) pbs.Smoothness.Value = smoothness;
            if (floats.TryGetValue("_BumpScale", out float bumpScale)) pbs.NormalScale.Value = bumpScale;

            if (colors.TryGetValue("_Color", out var col))
            {
                pbs.AlbedoColor.Value = new colorX(col.r, col.g, col.b, col.a);
            }
            if (colors.TryGetValue("_EmissionColor", out var emCol))
            {
                pbs.EmissiveColor.Value = new colorX(emCol.r, emCol.g, emCol.b, emCol.a);
            }
            await default(ToBackground);
        }
        return finalMaterial;
    }

    private static string GetTexGuid(Dictionary<string, (string guid, float sx, float sy, float ox, float oy)> textures, string name)
    {
        if (textures.TryGetValue(name, out var data) && !string.IsNullOrEmpty(data.guid))
        {
            return data.guid;
        }
        return null;
    }

    private static (float sx, float sy, float ox, float oy) GetTexData(Dictionary<string, (string guid, float sx, float sy, float ox, float oy)> textures, params string[] properties)
    {
        foreach (var property in properties)
        {
            if (textures.TryGetValue(property, out var data) && !string.IsNullOrEmpty(data.guid))
                return (data.sx, data.sy, data.ox, data.oy);
        }
        return (1, 1, 0, 0);
    }

    private static void ParseVec2(string line, out float x, out float y)
    {
        x = 1; y = 1;
        int xIdx = line.IndexOf("x:");
        int yIdx = line.IndexOf("y:");
        if (xIdx >= 0 && yIdx >= 0)
        {
            string xPart = line.Substring(xIdx + 2, yIdx - (xIdx + 2)).TrimEnd(new[] { ',', ' ' }).Trim();
            int endY = line.IndexOf('}', yIdx);
            string yPart = (endY >= 0 ? line.Substring(yIdx + 2, endY - (yIdx + 2)) : line.Substring(yIdx + 2)).Trim();
            float.TryParse(xPart, NumberStyles.Float, CultureInfo.InvariantCulture, out x);
            float.TryParse(yPart, NumberStyles.Float, CultureInfo.InvariantCulture, out y);
        }
    }

    private static bool ParseColor(string line, out float r, out float g, out float b, out float a)
    {
        r = g = b = a = 1;
        try
        {
            int rIdx = line.IndexOf("r:");
            int gIdx = line.IndexOf("g:");
            int bIdx = line.IndexOf("b:");
            int aIdx = line.IndexOf("a:");
            if (rIdx < 0 || gIdx < 0 || bIdx < 0 || aIdx < 0) return false;

            string rStr = line.Substring(rIdx + 2, gIdx - (rIdx + 2)).TrimEnd(new[] { ',', ' ' }).Trim();
            string gStr = line.Substring(gIdx + 2, bIdx - (gIdx + 2)).TrimEnd(new[] { ',', ' ' }).Trim();
            string bStr = line.Substring(bIdx + 2, aIdx - (bIdx + 2)).TrimEnd(new[] { ',', ' ' }).Trim();
            int endA = line.IndexOf('}', aIdx);
            string aStr = (endA >= 0 ? line.Substring(aIdx + 2, endA - (aIdx + 2)) : line.Substring(aIdx + 2)).Trim();

            float.TryParse(rStr, NumberStyles.Float, CultureInfo.InvariantCulture, out r);
            float.TryParse(gStr, NumberStyles.Float, CultureInfo.InvariantCulture, out g);
            float.TryParse(bStr, NumberStyles.Float, CultureInfo.InvariantCulture, out b);
            float.TryParse(aStr, NumberStyles.Float, CultureInfo.InvariantCulture, out a);
            return true;
        }
        catch { return false; }
    }

    public Task<StaticTexture2D> ImportTexture(string idtarget) =>
        importer.TextureImports.GetOrAdd(idtarget, () => ImportTextureCore(idtarget));

    private async Task<StaticTexture2D> ImportTextureCore(string idtarget)
    {
        await default(ToBackground);
        if (!importer.AssetIDDict.TryGetValue(idtarget, out var filePath))
        {
            UnityPackageImporter.Warn("Texture GUID not found in package: " + idtarget);
            return null;
        }
        var url = await importer.world.Engine.LocalDB.ImportLocalAssetAsync(filePath, LocalDB.ImportLocation.Copy);
        await default(ToWorld);
        var slot = assetsRoot.AddSlot(Path.GetFileName(filePath) + " [" + idtarget + "] - Texture");
        var texture = slot.AttachComponent<StaticTexture2D>();
        texture.URL.Value = url;
        return texture;
    }

    public async Task<StaticTexture2D> ImportLocalTextureFile(string filePath, string slotName, bool clamp = false, bool mipmaps = true)
    {
        await default(ToBackground);
        if (!File.Exists(filePath)) return null;
        try
        {
            var url = await importer.world.Engine.LocalDB.ImportLocalAssetAsync(filePath, LocalDB.ImportLocation.Copy);
            await default(ToWorld);
            var slot = assetsRoot.AddSlot(slotName);
            var texture = slot.AttachComponent<StaticTexture2D>();
            texture.URL.Value = url;
            if (clamp)
            {
                texture.WrapModeU.Value = TextureWrapMode.Clamp;
                texture.WrapModeV.Value = TextureWrapMode.Clamp;
            }
            if (!mipmaps)
            {
                texture.MipMaps.Value = false;
            }
            return texture;
        }
        catch (Exception ex)
        {
            UnityPackageImporter.Warn($"Failed to import local texture {filePath}: {ex.Message}");
            return null;
        }
    }
}
