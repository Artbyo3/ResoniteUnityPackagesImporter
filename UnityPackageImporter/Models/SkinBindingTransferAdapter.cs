using System;
using System.Linq;
using Elements.Core;
using FrooxEngine;

namespace UnityPackageImporter.Models;

internal static class SkinBindingTransferAdapter
{
    internal static void Preserve(Slot source, Slot target)
    {
        var previousParent = source.Parent;
        var previousTransform = source.TRS;
        var basis = new[] { float3.Zero, new float3(1, 0, 0), new float3(0, 1, 0), new float3(0, 0, 1) };
        var original = basis.Select(point => source.LocalPointToGlobal(point)).ToArray();
        float tolerance = MathF.Max(1f, original.Skip(1).Max(point => (point - original[0]).Magnitude)) * 0.0001f;
        try
        {
            source.SetParent(target, true);
            if (basis.Where((point, index) => !SkinBindingTransfer.WithinTolerance(
                    (source.LocalPointToGlobal(point) - original[index]).Magnitude, tolerance)).Any())
                throw new InvalidOperationException("The target hierarchy cannot preserve the skin binding transform without distortion.");
        }
        catch
        {
            source.SetParent(previousParent, false);
            source.TRS = previousTransform;
            throw;
        }
    }
}
