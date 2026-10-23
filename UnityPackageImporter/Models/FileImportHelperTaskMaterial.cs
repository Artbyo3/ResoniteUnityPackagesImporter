using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.Store;
using Renderite.Shared;
using UnityPackageImporter.Materials;
using UnityPackageImporter.Materials.Poiyomi;

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
    internal string MaterialGuid => myID;
    internal string SourceFile => file;
    private readonly bool forceToon;
    private readonly bool retainImportedMaterial;

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

    public FileImportHelperTaskMaterial(string myID, string file, UnityProjectImporter importer, bool forceToon = false)
    {
        importer.Session?.Check();
        this.importer = importer;
        this.file = file;
        this.myID = myID;
        this.forceToon = forceToon;
        importer.TasksMaterials.Add(this);
        UnityPackageImporter.Msg("Importing material with ID: \"" + myID + "\" from file: " + file);
        assetsRoot = importer.importTaskAssetRoot;
        matslot = assetsRoot.FindChildOrAdd(Path.GetFileNameWithoutExtension(this.file) + " [" + myID + (forceToon ? "-toon" : "") + "]" + materialNameIdentifyEndingPrefab);
    }

    // To assign a material to a missing material during import if the fbx doesn't have a definition for it
    public FileImportHelperTaskMaterial(UnityProjectImporter importer)
    {
        importer.Session?.Check();
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

    internal FileImportHelperTaskMaterial(UnityProjectImporter importer, IAssetProvider<Material> importedMaterial)
    {
        importer.Session?.Check();
        this.importer = importer;
        finalMaterial = importedMaterial ?? throw new ArgumentNullException(nameof(importedMaterial));
        retainImportedMaterial = true;
    }

    public async Task<IAssetProvider<Material>> runImportFileMaterialsAsync()
    {
        await default(ToBackground);
        importer.Session?.Check();
        if (ismissing || retainImportedMaterial) return finalMaterial;
        finalMaterial = await importer.MaterialImports.GetOrAdd(myID + (forceToon ? ":toon" : ":automatic"), ImportFileMaterial);
        if (finalMaterial != null && !string.IsNullOrEmpty(myID))
        {
            importer.RegisterMaterialGuid(finalMaterial, myID);
        }
        return finalMaterial;
    }

    private async Task<IAssetProvider<Material>> ImportFileMaterial()
    {
        await default(ToBackground);
        importer.Session?.Check();
        var data = UnityMaterialData.Parse(File.ReadAllText(file));
        var textures = data.Textures;
        var floats = data.Numbers;
        var colors = data.Colors;
        string shaderName = ResolveShaderName(data);
        bool isPoiyomi = PoiyomiSettings.Matches(data, shaderName);
        if (isPoiyomi)
            return await PoiyomiMaterialConverter.ConvertAsync(this, data, forceToon);
        bool isLilToon = !isPoiyomi && ((data.ShaderGuid != null && LilToonGuids.Contains(data.ShaderGuid)) ||
            (shaderName?.Contains("lilToon", StringComparison.OrdinalIgnoreCase) ?? false) ||
            floats.ContainsKey("_lilToonVersion") || floats.ContainsKey("_BackfaceForceShadow") ||
            textures.ContainsKey("_ShadowColorTex") || colors.ContainsKey("_Shadow2ndColor"));
        if (isLilToon || forceToon)
        {
            return await LilToonMaterialConverter.ConvertAsync(this, data, shaderName, generic: !isLilToon);
        }

        // Standard / PBS Metallic fallback
        {
            UnityPackageImporter.Msg("Importing Standard/PBS material: " + Path.GetFileNameWithoutExtension(file));
            await default(ToWorld);
            importer.Session?.Check();
            var pbs = matslot.GetComponentOrAttach<PBS_Metallic>();
            finalMaterial = pbs;
            await default(ToBackground);
            importer.Session?.Check();

            string fbMainTexGuid = GetTexGuid(textures, "_MainTex") ?? GetTexGuid(textures, "_BaseMap") ?? GetTexGuid(textures, "_MainTexture") ?? GetTexGuid(textures, "_1st_ShadeMap");
            if (fbMainTexGuid != null)
            {
                var texData = GetTexData(textures, "_MainTex", "_BaseMap", "_MainTexture", "_1st_ShadeMap");
                await AssignTextureAsync(fbMainTexGuid, pbs.AlbedoTexture);
                await default(ToWorld);
                importer.Session?.Check();
                pbs.TextureScale.Value = new float2(texData.sx, texData.sy);
                pbs.TextureOffset.Value = new float2(texData.ox, texData.oy);
                await default(ToBackground);
                importer.Session?.Check();
            }

            string normalGuid = GetTexGuid(textures, "_BumpMap");
            if (normalGuid != null)
            {
                await AssignTextureAsync(normalGuid, pbs.NormalMap, normalMap: true, requiredForCompletion: false);
                await default(ToBackground);
                importer.Session?.Check();
            }

            string metallicGuid = GetTexGuid(textures, "_MetallicGlossMap");
            if (metallicGuid != null)
            {
                await AssignTextureAsync(metallicGuid, pbs.MetallicMap, requiredForCompletion: false);
                await default(ToBackground);
                importer.Session?.Check();
            }

            string emissionGuid = GetTexGuid(textures, "_EmissionMap");
            if (emissionGuid != null)
            {
                await AssignTextureAsync(emissionGuid, pbs.EmissiveMap, requiredForCompletion: false);
                await default(ToBackground);
                importer.Session?.Check();
            }

            string occlusionGuid = GetTexGuid(textures, "_OcclusionMap");
            if (occlusionGuid != null)
            {
                await AssignTextureAsync(occlusionGuid, pbs.OcclusionMap, requiredForCompletion: false);
                await default(ToBackground);
                importer.Session?.Check();
            }

            await default(ToWorld);
            importer.Session?.Check();
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
            importer.Session?.Check();
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

    private string ResolveShaderName(UnityMaterialData data)
    {
        if (data.ShaderGuid == null) return null;
        if (importer.AssetIDDict.TryGetValue(data.ShaderGuid, out var shaderPath) &&
            Path.GetExtension(shaderPath).Equals(".shader", StringComparison.OrdinalIgnoreCase))
        {
            // Read only the declaration, never execute shader code.
            foreach (var line in File.ReadLines(shaderPath).Take(128))
            {
                var match = System.Text.RegularExpressions.Regex.Match(line, "^\\s*Shader\\s+\"([^\"]+)\"");
                if (match.Success) return match.Groups[1].Value;
            }
        }
        if (LilToonCutoutGuids.Contains(data.ShaderGuid)) return "Hidden/lilToonCutout";
        if (data.ShaderGuid.Equals("3c79b10c7e0b2784aaa4c2f8dd17d55e", StringComparison.OrdinalIgnoreCase))
            return "Hidden/lilToonTransparentOutline";
        if (LilToonTransparentGuids.Contains(data.ShaderGuid)) return "Hidden/lilToonTransparent";
        if (data.ShaderGuid == "4f82b88126eecde47bc9548f0bc2e54a") return "Hidden/lilToonMulti";
        if (data.ShaderGuid == "c1630c1e8555238479e000fbc9822a10") return "Hidden/lilToonOutline";
        if (LilToonGuids.Contains(data.ShaderGuid)) return "lilToon";
        return null;
    }

    internal async Task AssignTextureAsync(string guid, AssetRef<ITexture2D> target,
        bool multiplyRgbByAlpha = false, bool normalMap = false, bool requiredForCompletion = true,
        TexturePixelTransform transform = null, bool clamp = false, bool mipmaps = true)
    {
        if (MaterialDependencyDiagnostics.IsBuiltinOrEmptyGuid(guid)) return;
        var texture = await ImportTextureVariant(guid, multiplyRgbByAlpha, normalMap, transform, clamp, mipmaps);
        await default(ToWorld);
        importer.Session?.Check();
        // The asset index may have changed while the first attempt was
        // completing. Do not leave a newly resolvable field waiting for another
        // companion package merely because it missed the resolution snapshot.
        if (texture == null && importer.AssetIDDict.ContainsKey(guid))
        {
            texture = await ImportTextureVariant(guid, multiplyRgbByAlpha, normalMap, transform, clamp, mipmaps);
            await default(ToWorld);
            importer.Session?.Check();
        }
        target.Target = texture;
        if (texture != null) return;

        importer.MissingTextureAssignments.Register(target, guid,
            () => ImportTextureVariant(guid, multiplyRgbByAlpha, normalMap, transform, clamp, mipmaps), async (restored, cancellation) =>
        {
            await default(ToWorld);
            cancellation.ThrowIfCancellationRequested();
            importer.Session?.Check();
            if (matslot.IsDestroyed || target.IsDisposed) return true;
            // A user may have assigned a replacement in the meantime.
            if (target.Target != null) return true;
            target.Target = restored;
            return true;
        }, required: requiredForCompletion);
    }

    public async Task<StaticTexture2D> ImportTexture(string idtarget, bool multiplyRgbByAlpha = false, bool normalMap = false)
        => await ImportTextureVariant(idtarget, multiplyRgbByAlpha, normalMap, null, false, true);

    private async Task<StaticTexture2D> ImportTextureVariant(string idtarget, bool multiplyRgbByAlpha, bool normalMap,
        TexturePixelTransform transform, bool clamp, bool mipmaps)
    {
        importer.Session?.Check();
        string key = idtarget.ToLowerInvariant() + (multiplyRgbByAlpha ? ":alpha-rgb" : "") + (normalMap ? ":normal" : "") +
            (transform == null ? "" : ":" + transform.Key) + (clamp ? ":clamp" : "") + (mipmaps ? "" : ":no-mips");
        var texture = await importer.TextureImports.GetOrAdd(key,
            () => ImportTextureCore(idtarget, multiplyRgbByAlpha, normalMap, transform, clamp, mipmaps));
        importer.Session?.Check();
        if (texture == null && importer.AssetIDDict.ContainsKey(idtarget))
        {
            // A missing attempt can finish after companion invalidation. Retry
            // that key once now that its source is available, retaining any
            // successful or still-running attempt started by another caller.
            importer.TextureImports.RetryUnsuccessful(key);
            texture = await importer.TextureImports.GetOrAdd(key,
                () => ImportTextureCore(idtarget, multiplyRgbByAlpha, normalMap, transform, clamp, mipmaps));
            importer.Session?.Check();
        }
        return texture;
    }

    private async Task<StaticTexture2D> ImportTextureCore(string idtarget, bool multiplyRgbByAlpha, bool normalMap,
        TexturePixelTransform transform, bool clamp, bool mipmaps)
    {
        await default(ToBackground);
        importer.Session?.Check();
        if (!importer.AssetIDDict.TryGetValue(idtarget, out var filePath))
        {
            UnityPackageImporter.Warn("Texture GUID not found in package: " + idtarget);
            return null;
        }
        string temporary = null;
        Uri url;
        try
        {
            if (multiplyRgbByAlpha || transform != null)
            {
                temporary = Path.Combine(Path.GetTempPath(), "importer-alpha-" + Guid.NewGuid().ToString("N") + ".png");
                MaterialTextureProcessor.ProcessFile(filePath, temporary, multiplyRgbByAlpha, transform,
                    () => importer.Session?.Check());
            }
            importer.Session?.Check();
            url = await importer.world.Engine.LocalDB.ImportLocalAssetAsync(temporary ?? filePath, LocalDB.ImportLocation.Copy);
        }
        finally { if (temporary != null && File.Exists(temporary)) File.Delete(temporary); }
        await default(ToWorld);
        importer.Session?.Check();
        var slot = assetsRoot.AddSlot(Path.GetFileName(filePath) + " [" + idtarget + "] - Texture");
        var texture = slot.AttachComponent<StaticTexture2D>();
        texture.IsNormalMap.Value = normalMap;
        if (clamp)
        {
            texture.WrapModeU.Value = TextureWrapMode.Clamp;
            texture.WrapModeV.Value = TextureWrapMode.Clamp;
        }
        texture.MipMaps.Value = mipmaps;
        texture.URL.Value = url;
        return texture;
    }

    internal async Task<StaticTexture2D> ImportProcessedLocalTextureFile(string filePath, string slotName, TexturePixelTransform transform)
    {
        await default(ToBackground);
        importer.Session?.Check();
        string temporary = Path.Combine(Path.GetTempPath(), "importer-ramp-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            MaterialTextureProcessor.ProcessFile(filePath, temporary, transform: transform,
                checkCancellation: () => importer.Session?.Check());
            return await ImportLocalTextureFile(temporary, slotName, clamp: true, mipmaps: false);
        }
        finally
        {
            await default(ToBackground);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public async Task<StaticTexture2D> ImportLocalTextureFile(string filePath, string slotName, bool clamp = false, bool mipmaps = true)
    {
        await default(ToBackground);
        importer.Session?.Check();
        if (!File.Exists(filePath)) return null;
        try
        {
            var url = await importer.world.Engine.LocalDB.ImportLocalAssetAsync(filePath, LocalDB.ImportLocation.Copy);
            await default(ToWorld);
            importer.Session?.Check();
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            UnityPackageImporter.Warn($"Failed to import local texture {filePath}: {ex.Message}");
            return null;
        }
    }
}
