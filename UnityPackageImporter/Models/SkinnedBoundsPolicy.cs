using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FrooxEngine;

namespace UnityPackageImporter.Models;

/// <summary>
/// Keeps skinned bounds behavior consistent before and after Modular Avatar
/// bone remapping. Static mode caches bounds for the native bone hierarchies;
/// correct bone references are essential. It is not a substitute for valid rig data.
/// </summary>
internal static class SkinnedBoundsPolicy
{
    // The duplicated hierarchy still needs to settle before its renderers are
    // activated. Allow engine update queues to process the copied references.
    // This delay alone does not prove that a malformed rig has valid bounds.
    private const int HierarchyStabilizationUpdates = 3;

    public static void ApplyAfterBones(SkinnedMeshRenderer renderer, string context)
    {
        if (renderer == null || renderer.IsDestroyed) return;

        // Preserve the native static policy. Do not force enlarged explicit
        // bounds or realtime recomputation to conceal invalid skin references.
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
        string context,
        CancellationToken cancellationToken = default)
    {
        await default(ToWorld);
        cancellationToken.ThrowIfCancellationRequested();
        var liveRenderers = renderers?
            .Where(renderer => renderer != null && !renderer.IsDestroyed)
            .Distinct()
            .ToList() ?? new List<SkinnedMeshRenderer>();

        if (liveRenderers.Count == 0) return;

        for (int i = 0; i < HierarchyStabilizationUpdates; i++)
        {
            await default(NextUpdate);
            cancellationToken.ThrowIfCancellationRequested();
        }

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
