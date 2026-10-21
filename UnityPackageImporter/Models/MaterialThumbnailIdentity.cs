#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UnityPackageImporter.Models;

internal sealed record MaterialThumbnailSource(string Guid, string Yaml, string Key, IReadOnlyDictionary<string, string> AssetIndex)
{
    public static Task<MaterialThumbnailSource?> CaptureAsync(string materialGuid,
        IReadOnlyDictionary<string, string> assetIndex, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(materialGuid) || !assetIndex.TryGetValue(materialGuid, out var path) || !File.Exists(path))
            return null;
        string yaml = File.ReadAllText(path);
        string key = MaterialThumbnailIdentity.Create(materialGuid, yaml, assetIndex);
        cancellationToken.ThrowIfCancellationRequested();
        return new MaterialThumbnailSource(materialGuid, yaml, key, assetIndex);
    }, cancellationToken);
}

internal static class MaterialThumbnailIdentity
{
    public static string MaterialKey(string materialGuid, string yaml) =>
        $"v3_{materialGuid}_{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(yaml)))}";

    public static string Create(string materialGuid, string yaml, IReadOnlyDictionary<string, string> assetIndex)
    {
        // Length-prefixed fields make the identity unambiguous. Include every referenced
        // texture so renderer changes cannot accidentally omit a dependency from the key.
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(yaml);
            foreach (var texture in UnityMaterialData.Parse(yaml).Textures.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (string.IsNullOrEmpty(texture.Value.guid)) continue;
                writer.Write(texture.Key);
                writer.Write(texture.Value.guid!);
                bool available = assetIndex.TryGetValue(texture.Value.guid!, out var path) && File.Exists(path);
                writer.Write(available);
                if (available)
                {
                    using var source = File.OpenRead(path!);
                    writer.Write(SHA256.HashData(source));
                }
            }
        }
        return $"v3_{materialGuid}_{Convert.ToHexString(SHA256.HashData(buffer.ToArray()))}";
    }
}
