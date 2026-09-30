using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FrooxEngine;

namespace UnityPackageImporter.Models;

/// <summary>
/// Keeps skinned bounds behavior consistent before and after Modular Avatar
/// bone remapping. Completed native Resonite FBX imports use static mesh bounds
/// with no explicit override, so reconstructed renderers mirror that behavior.
/// </summary>
internal static class SkinnedBoundsPolicy
{
    // The duplicated hierarchy still needs to settle before its renderers are
    // activated. Three updates is the engine's known-safe boundary for cloned
    // skinned hierarchies and keeps bone references stable before activation.
    private const int HierarchyStabilizationUpdates = 3;

    public static void ApplyAfterBones(SkinnedMeshRenderer renderer, string context)
    {
        if (renderer == null || renderer.IsDestroyed) return;

        // Native model imports finish in Static mode and leave
        // ExplicitLocalBounds empty, allowing the mesh asset's bounds to be
        // used. MediumPerBoneApproximate fell back to SlowRealtimeAccurate on
        // 27 of 28 matching Uruki renderers and reproduced inconsistent
        // culling, even though every bone reference matched the native import.
        if (renderer.Mesh?.Target != null)
        {
            renderer.BoundsComputeMethod.Value = SkinnedBounds.Static;
        }
        else
        {
            renderer.BoundsComputeMethod.Value = SkinnedBounds.MediumPerBoneApproximate;
        }

        int validBones = 0;
        for (int i = 0; i < renderer.Bones.Count; i++)
        {
            var bone = renderer.Bones[i];
            if (bone != null && !bone.IsDestroyed) validBones++;
        }

        if (validBones == 0)
        {
            UnityPackageImporter.Warn(
                "Skinned renderer '" + renderer.Slot.Name +
                "' has no usable bone references" + FormatContext(context) +
                "; native-style static mesh bounds remain active.");
            return;
        }

        if (validBones != renderer.Bones.Count)
        {
            UnityPackageImporter.Warn(
                "Skinned renderer '" + renderer.Slot.Name + "' has " +
                (renderer.Bones.Count - validBones) + " missing bone reference(s)" +
                FormatContext(context) + "; native-style static mesh bounds remain active.");
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
