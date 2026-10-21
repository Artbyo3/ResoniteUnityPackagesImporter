#nullable enable
using System;
using System.Collections.Concurrent;

namespace UnityPackageImporter.Models;

// Owned by one ImportSession, never shared across packages or worlds.
internal sealed class MaterialThumbnailColors
{
    private readonly ConcurrentDictionary<string, (float r, float g, float b, float a)> colors = new(StringComparer.Ordinal);

    public (float r, float g, float b, float a) Get(string materialGuid, string yaml) =>
        colors.GetOrAdd(MaterialThumbnailIdentity.MaterialKey(materialGuid, yaml), _ => Read(yaml));

    internal static (float r, float g, float b, float a) Read(string yaml)
    {
        var data = UnityMaterialData.Parse(yaml);
        return data.Color("_Color", data.Color("_BaseColor", (0.8f, 0.8f, 0.8f, 1.0f)));
    }

    public void Clear() => colors.Clear();
    internal int Count => colors.Count;
}
