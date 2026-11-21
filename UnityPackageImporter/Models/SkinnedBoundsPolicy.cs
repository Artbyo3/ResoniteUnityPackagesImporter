using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FrooxEngine;

namespace UnityPackageImporter.Models;






internal static class SkinnedBoundsPolicy
{



    private const int HierarchyStabilizationUpdates = 3;

    public static void ApplyAfterBones(SkinnedMeshRenderer renderer, string context)
    {
        if (renderer == null || renderer.IsDestroyed) return;



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
