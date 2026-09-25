using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FrooxEngine;

namespace UnityPackageImporter.Models;

/// <summary>
/// Keeps skinned bounds behavior consistent before and after Modular Avatar
/// bone remapping. Per-bone bounds substantially improved observed close-range
/// culling on Uruki; renderers without usable bones use static mesh bounds.
/// </summary>
internal static class SkinnedBoundsPolicy
{
    // Resonite's skinned bounds calculator needs the duplicated hierarchy to
    // survive several world updates before its renderers are activated. Three
    // updates is the engine's known-safe boundary for duplicated avatars.
    private const int HierarchyStabilizationUpdates = 3;

    public static void ApplyAfterBones(SkinnedMeshRenderer renderer, string context)
    {
        if (renderer == null || renderer.IsDestroyed) return;

        int validBones = 0;
        for (int i = 0; i < renderer.Bones.Count; i++)
        {
            var bone = renderer.Bones[i];
            if (bone != null && !bone.IsDestroyed) validBones++;
        }

        if (validBones == 0)
        {
            renderer.BoundsComputeMethod.Value = SkinnedBounds.Static;
            UnityPackageImporter.Warn(
                "Using static bounds for skinned renderer '" + renderer.Slot.Name +
                "' because it has no usable bones" + FormatContext(context) + ".");
            return;
        }

        renderer.BoundsComputeMethod.Value = SkinnedBounds.MediumPerBoneApproximate;
        if (validBones != renderer.Bones.Count)
        {
            UnityPackageImporter.Warn(
                "Skinned renderer '" + renderer.Slot.Name + "' has " +
                (renderer.Bones.Count - validBones) + " missing bone reference(s)" +
                FormatContext(context) + "; per-bone bounds will use the remaining bones.");
        }
    }

    /// <summary>
    /// Waits for duplication, reparenting and bone-reference changes to settle,
    /// then reapplies the bounds policy immediately before callers activate the
    /// renderers. Callers must keep these renderers disabled or beneath an
    /// inactive root until this method completes.
    /// </summary>
    public static async Task StabilizeBeforeEnableAsync(
        IEnumerable<SkinnedMeshRenderer> renderers,
        string context)
    {
        var liveRenderers = renderers?
            .Where(renderer => renderer != null && !renderer.IsDestroyed)
            .Distinct()
            .ToList() ?? new List<SkinnedMeshRenderer>();

        if (liveRenderers.Count == 0) return;

        await default(ToWorld);
        for (int i = 0; i < HierarchyStabilizationUpdates; i++)
            await default(NextUpdate);

        foreach (var renderer in liveRenderers)
            ApplyAfterBones(renderer, context);

        UnityPackageImporter.Msg(
            "Finalized bounds for " + liveRenderers.Count +
            " skinned renderer(s) after " + HierarchyStabilizationUpdates +
            " hierarchy update(s)" + FormatContext(context) + ".");
    }

    private static string FormatContext(string context) =>
        string.IsNullOrWhiteSpace(context) ? "" : " during " + context;
}
