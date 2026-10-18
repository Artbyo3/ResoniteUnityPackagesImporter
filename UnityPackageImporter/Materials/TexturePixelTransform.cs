// Poiyomi swizzle and shadow-tint rules adapted from Resonite.UnitySDK (MIT).
// Copyright (c) 2026 Yellow Dog Man Studios. See THIRD_PARTY_NOTICES.md.
using System;
using System.Globalization;
using UnityPackageImporter.Models;

namespace UnityPackageImporter.Materials;

/// <summary>Immutable pixel processing recipe. Every output-affecting value participates in the cache key.</summary>
internal sealed record TexturePixelTransform
{
    private int R { get; init; } = 0;
    private int G { get; init; } = 1;
    private int B { get; init; } = 2;
    private int A { get; init; } = 3;
    private bool Tint { get; init; }
    private bool TintWhite { get; init; }
    private (float r, float g, float b, float a) Color { get; init; } = (1, 1, 1, 1);
    private float Strength { get; init; } = 1;
    internal string Key => string.Create(CultureInfo.InvariantCulture,
        $"pixel-v1:{R}:{G}:{B}:{A}:{Tint}:{TintWhite}:{Color.r:R}:{Color.g:R}:{Color.b:R}:{Color.a:R}:{Strength:R}");

    // 0..3 = RGBA, 4 = one, 5..9 = inverted RGBA/one (9 = zero).
    internal static TexturePixelTransform Swizzle(int r, int g, int b, int a)
    {
        foreach (int channel in new[] { r, g, b, a })
            if (channel < 0 || channel > 9) throw new ArgumentOutOfRangeException(nameof(channel));
        return new() { R = r, G = g, B = b, A = a };
    }
    internal static TexturePixelTransform ShadowTint(UnityMaterialData data, bool tintWhite) => new()
    {
        Tint = true, TintWhite = tintWhite, Color = data.Color("_LightingShadowColor", (1, 1, 1, 1)),
        Strength = Math.Clamp(data.Number("_ShadowStrength", 1), 0, 1)
    };
    internal (float r, float g, float b, float a) Apply(float r, float g, float b, float a)
    {
        float Channel(int channel)
        {
            float c = (channel % 5) switch { 0 => r, 1 => g, 2 => b, 3 => a, _ => 1 };
            return channel >= 5 ? 1 - c : c;
        }
        float Filter(float c, float tint) => Strength * c * (TintWhite ? tint : c + (1 - c) * tint) + 1 - Strength;
        return Tint ? (Filter(r, Color.r), Filter(g, Color.g), Filter(b, Color.b), Filter(a, Color.a)) :
            (Channel(R), Channel(G), Channel(B), Channel(A));
    }

    // Texture tinting operates on linear-light values, like the SDK's LDR processor.
    // Packed material channels are data: swizzle/invert their stored values directly.
    internal (float r, float g, float b, float a) ApplyStored(float r, float g, float b, float a)
    {
        if (!Tint) return Apply(r, g, b, a);
        var c = Apply(Decode(r), Decode(g), Decode(b), Decode(a));
        return (Encode(c.r), Encode(c.g), Encode(c.b), Encode(c.a));
    }
    private static float Decode(float c) => MathF.Pow(Math.Max(0, c), 2.2f);
    private static float Encode(float c) => MathF.Pow(Math.Max(0, c), 1 / 2.2f);
}
