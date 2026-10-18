#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace UnityPackageImporter.Materials;

internal enum TextureContributionKind { ColorWithAlpha, ScalarMask }

internal sealed record TextureContribution(string Guid, int Uv, float ScaleX, float ScaleY, float OffsetX, float OffsetY,
    TextureContributionKind Kind, int Channel = 0, bool Invert = false);

/// <summary>A static multiplication recipe in a single shared UV domain. Shader adapters describe; the baker evaluates.</summary>
internal sealed class TextureCompositionPlan
{
    internal IReadOnlyList<TextureContribution> Inputs { get; }
    internal string? UnsupportedReason { get; }
    internal int Uv => Inputs.Count == 0 ? 0 : Inputs[0].Uv;
    internal string Key { get; }

    internal TextureCompositionPlan(IEnumerable<TextureContribution> inputs, string? unsupported = null)
    {
        Inputs = Array.AsReadOnly(inputs.ToArray());
        UnsupportedReason = unsupported;
        if (Inputs.Any(i => i.Uv < 0 || i.Uv > 3)) UnsupportedReason ??= "view-dependent or generated UV coordinates";
        if (Inputs.Any(i => !float.IsFinite(i.ScaleX) || !float.IsFinite(i.ScaleY) || !float.IsFinite(i.OffsetX) || !float.IsFinite(i.OffsetY)))
            UnsupportedReason ??= "non-finite texture coordinates";
        if (Inputs.Any(i => i.Channel < 0 || i.Channel > 3)) UnsupportedReason ??= "unknown mask channel";
        if (Inputs.Count > 1 && Inputs.Skip(1).Any(i => i.Uv != Uv || i.ScaleX != Inputs[0].ScaleX || i.ScaleY != Inputs[0].ScaleY ||
                i.OffsetX != Inputs[0].OffsetX || i.OffsetY != Inputs[0].OffsetY))
            UnsupportedReason ??= "different UV sets or texture transforms";
        Key = "composition-v1:" + string.Join("|", Inputs.Select(i => string.Create(CultureInfo.InvariantCulture,
            $"{i.Guid.ToLowerInvariant()}:{i.Uv}:{i.ScaleX:R}:{i.ScaleY:R}:{i.OffsetX:R}:{i.OffsetY:R}:{i.Kind}:{i.Channel}:{i.Invert}")));
    }

    // Samples contain linear RGB and straight alpha. Output is linear RGB with alpha already included.
    internal (float r, float g, float b) Evaluate(Func<TextureContribution, (float r, float g, float b, float a)> sample)
    {
        if (UnsupportedReason != null) throw new NotSupportedException(UnsupportedReason);
        float r = 1, g = 1, b = 1;
        for (int index = 0; index < Inputs.Count; index++)
        {
            var input = Inputs[index];
            var c = sample(input);
            if (input.Kind == TextureContributionKind.ScalarMask)
            {
                float mask = input.Channel switch { 0 => c.r, 1 => c.g, 2 => c.b, _ => c.a };
                mask = Math.Clamp(input.Invert ? 1 - mask : mask, 0, 1);
                r *= mask; g *= mask; b *= mask;
            }
            else { r *= c.r * c.a; g *= c.g * c.a; b *= c.b * c.a; }
        }
        return (r, g, b);
    }
}
