#nullable enable
using System;

#if !REGRESSION_TESTS
using Elements.Core;
using FrooxEngine;
#endif

namespace UnityPackageImporter.Models;

/// <summary>
/// Reads base display color from known importer material types without reflection.
/// </summary>
internal static class MaterialColorReader
{
    public static readonly (float r, float g, float b, float a) NeutralTint = (0.5f, 0.5f, 0.5f, 1f);

#if !REGRESSION_TESTS
    public static colorX ReadMaterialColor(IAssetProvider<Material>? material)
    {
        if (material is PBS_Metallic pbs)
            return pbs.AlbedoColor.Value;
        if (material is XiexeToonMaterial toon)
            return toon.Color.Value;
        if (material is PBS_Specular spec)
            return spec.AlbedoColor.Value;
        if (material is UnlitMaterial unlit)
            return unlit.TintColor.Value;

        return new colorX(NeutralTint.r, NeutralTint.g, NeutralTint.b, NeutralTint.a);
    }

    public static (float r, float g, float b, float a) ReadMaterialColorRgba(IAssetProvider<Material>? material)
    {
        var c = ReadMaterialColor(material);
        return (c.r, c.g, c.b, c.a);
    }
#else
    public static (float r, float g, float b, float a) ReadMaterialColorRgba(object? material)
    {
        if (material is IPbsMetallicStub pbs)
            return pbs.AlbedoColor;
        if (material is IXiexeToonStub toon)
            return toon.Color;

        return NeutralTint;
    }

    public interface IPbsMetallicStub { (float r, float g, float b, float a) AlbedoColor { get; } }
    public interface IXiexeToonStub { (float r, float g, float b, float a) Color { get; } }

    public sealed class MockPbsMetallic : IPbsMetallicStub
    {
        public (float r, float g, float b, float a) AlbedoColor { get; set; }
    }

    public sealed class MockXiexeToon : IXiexeToonStub
    {
        public (float r, float g, float b, float a) Color { get; set; }
    }

    public sealed class MockUnknownMaterial { }
#endif
}
