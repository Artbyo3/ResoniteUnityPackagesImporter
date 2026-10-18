using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;
using Renderite.Shared;
using UnityPackageImporter.Models;

namespace UnityPackageImporter.Materials;

internal static class EmissionTextureImporter
{
    internal static async Task AssignAsync(FileImportHelperTaskMaterial owner, TextureCompositionPlan plan,
        AssetRef<ITexture2D> target, Action<bool> setReady = null)
    {
        await default(ToWorld);
        owner.importer.Session?.Check();
        target.Target = null; // Null emission map is dark in Xiexe; never replace a missing mask with white.
        setReady?.Invoke(false);
        if (plan.UnsupportedReason != null)
        {
            string msg = $"{Path.GetFileName(owner.SourceFile)}: Static emission is not converted: {plan.UnsupportedReason}.";
            UnityPackageImporter.Warn(msg);
            owner.importer?.RegisterMaterialWarning(owner.MaterialGuid, msg);
            return;
        }
        async Task<StaticTexture2D> Load()
        {
            await default(ToBackground);
            owner.importer.Session?.Check();
            var index = owner.importer.AssetIDDict;
            if (plan.Inputs.Any(i => !index.ContainsKey(i.Guid))) return null;
            string key = plan.Key;
            owner.importer.TextureImports.RetryUnsuccessful(key);
            return await owner.importer.TextureImports.GetOrAdd(key, async () =>
            {
                string temporary = Path.Combine(Path.GetTempPath(), "importer-composition-" + Guid.NewGuid().ToString("N") + ".png");
                try
                {
                    var paths = plan.Inputs.Select(i => i.Guid).Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g, g => index[g], StringComparer.OrdinalIgnoreCase);
                    var wrap = TextureCompositionBaker.Bake(plan, paths, temporary, () => owner.importer.Session?.Check());
                    var texture = await owner.ImportLocalTextureFile(temporary, "Composed emission");
                    await default(ToWorld);
                    owner.importer.Session?.Check();
                    if (texture != null)
                    {
                        texture.WrapModeU.Value = wrap.WrapU == 1 ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                        texture.WrapModeV.Value = wrap.WrapV == 1 ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                        texture.PreferredProfile.Value = ColorProfile.sRGB;
                        texture.FilterMode.Value = wrap.Point ? TextureFilterMode.Point : TextureFilterMode.Bilinear;
                    }
                    return texture;
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    string msg = $"{Path.GetFileName(owner.SourceFile)}: Emission composition failed: {error.Message}";
                    UnityPackageImporter.Warn(msg);
                    owner.importer?.RegisterMaterialWarning(owner.MaterialGuid, msg);
                    return null;
                }
                finally
                {
                    await default(ToBackground);
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            });
        }
        bool initiallyMissing = plan.Inputs.Any(i => !owner.importer.AssetIDDict.ContainsKey(i.Guid));
        var result = await Load();
        await default(ToWorld);
        owner.importer.Session?.Check();
        if (owner.matslot.IsDestroyed || target.IsDisposed) return;
        target.Target = result;
        setReady?.Invoke(result != null);
        if (result != null) return;
        // Re-snapshot after async work so a companion racing this registration is not lost.
        var missing = plan.Inputs.Select(i => i.Guid).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(g => !owner.importer.AssetIDDict.ContainsKey(g)).ToArray();
        if (missing.Length == 0)
        {
            if (!initiallyMissing) return;
            result = await Load();
            await default(ToWorld); owner.importer.Session?.Check();
            if (!target.IsDisposed) { target.Target = result; setReady?.Invoke(result != null); }
            return;
        }
        owner.importer.MissingTextureAssignments.RegisterMany(target, missing, Load, async (restored, cancellation) =>
        {
            await default(ToWorld);
            cancellation.ThrowIfCancellationRequested(); owner.importer.Session?.Check();
            if (owner.matslot.IsDestroyed || target.IsDisposed) return true;
            if (target.Target == null) { target.Target = restored; setReady?.Invoke(true); }
            return true;
        });
    }
}
