using UnityPackageImporter.Models;

internal static class ThumbnailCacheTests
{
    private static string Material(string color = "1", string textureProperty = "_MainTex") => $$"""
        Material:
          m_SavedProperties:
            m_Colors:
              - _Color: {r: {{color}}, g: 0, b: 0, a: 1}
            m_TexEnvs:
              - {{textureProperty}}:
                  m_Texture: {fileID: 2800000, guid: texture, type: 3}
        """;

    internal static void Register(Action<string, Action> test)
    {
        test("Thumbnail identity changes for the same GUID with different material YAML", () =>
        {
            var assets = new Dictionary<string, string>();
            string first = MaterialThumbnailIdentity.Create("same-guid", Material("1"), assets);
            string second = MaterialThumbnailIdentity.Create("same-guid", Material("0.2"), assets);
            Check(first != second && first.StartsWith("v3_same-guid_"), "Material content and readable GUID must be part of identity.");
        });

        test("Thumbnail identity matches identical content across package paths and timestamps", () => InTemp(root =>
        {
            string first = Path.Combine(root, "first.png");
            string second = Path.Combine(root, "second.png");
            File.WriteAllBytes(first, [1, 2, 3]);
            File.Copy(first, second);
            File.SetLastWriteTimeUtc(second, DateTime.UtcNow.AddDays(-2));
            var one = new Dictionary<string, string> { ["texture"] = first };
            var two = new Dictionary<string, string> { ["texture"] = second };
            Check(MaterialThumbnailIdentity.Create("guid", Material(), one) == MaterialThumbnailIdentity.Create("guid", Material(), two),
                "Package paths or timestamps changed a content identity.");
        }));

        foreach (string property in new[] { "_MainTex", "_BaseMap", "_MatCapTex", "_EmissionMap" })
            test($"Thumbnail identity tracks {property} bytes even with unchanged size and timestamp", () => InTemp(root =>
            {
                string texture = Path.Combine(root, "texture.png");
                File.WriteAllBytes(texture, [1, 2, 3]);
                var stamp = File.GetLastWriteTimeUtc(texture);
                var assets = new Dictionary<string, string> { ["texture"] = texture };
                string yaml = Material(textureProperty: property);
                string before = MaterialThumbnailIdentity.Create("guid", yaml, assets);
                File.WriteAllBytes(texture, [3, 2, 1]);
                File.SetLastWriteTimeUtc(texture, stamp);
                Check(before != MaterialThumbnailIdentity.Create("guid", yaml, assets), "Texture edit reused a stale identity.");
            }));

        test("Thumbnail identity changes when a missing companion texture becomes available", () => InTemp(root =>
        {
            var assets = new Dictionary<string, string>();
            string missing = MaterialThumbnailIdentity.Create("guid", Material(), assets);
            string path = Path.Combine(root, "texture.png");
            File.WriteAllBytes(path, [1]);
            assets["texture"] = path;
            Check(missing != MaterialThumbnailIdentity.Create("guid", Material(), assets), "Missing texture identity survived resolution.");
        }));

        test("Thumbnail base colors are content keyed and scoped to the import session", () =>
        {
            var first = new MaterialThumbnailColors();
            var second = new MaterialThumbnailColors();
            Check(first.Get("guid", Material("1")).r == 1f, "Initial color was incorrect.");
            Check(first.Get("guid", Material("0.2")).r == 0.2f, "Changed material reused old color.");
            Check(second.Count == 0, "Another session inherited cached colors.");
            second.Get("guid", Material("0.4"));
            first.Clear();
            Check(first.Count == 0 && second.Count == 1, "Session cleanup affected another session.");
        });

        test("Thumbnail shared tasks separate source identities and share identical content", () =>
        {
            var cache = new ThumbnailTaskCache();
            var assets = new Dictionary<string, string>();
            string first = MaterialThumbnailIdentity.Create("guid", Material("1"), assets);
            string second = MaterialThumbnailIdentity.Create("guid", Material("0"), assets);
            int calls = 0;
            Task<Uri?> Generate(string key)
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult<Uri?>(new Uri("resdb:///" + key));
            }
            var a = cache.GetOrLoadThumbnailAsync(first, Generate).GetAwaiter().GetResult();
            var b = cache.GetOrLoadThumbnailAsync(second, Generate).GetAwaiter().GetResult();
            var again = cache.GetOrLoadThumbnailAsync(first, Generate).GetAwaiter().GetResult();
            Check(a != b && a == again && calls == 2, "Shared tasks ignored source identity or duplicated work.");
        });

        test("Thumbnail disk paths and base colors invalidate re-imported material and texture content", () => InTemp(root =>
        {
            string guid = Guid.NewGuid().ToString("N");
            string material = Path.Combine(root, "material.mat");
            string texture = Path.Combine(root, "texture.bin");
            File.WriteAllText(material, Material("1"));
            File.WriteAllBytes(texture, [255]);
            var assets = new Dictionary<string, string> { [guid] = material, ["texture"] = texture };
            var paths = new HashSet<string>();
            MaterialThumbnailGenerator.TextureReader reader = (path, _) => (1, 1, new byte[] { File.ReadAllBytes(path)[0], 0, 0, 255 });
            string Generate()
            {
                string path = MaterialThumbnailGenerator.GetOrCreateThumbnailAsync(guid, assets, customTextureReader: reader).GetAwaiter().GetResult()!;
                paths.Add(path);
                return path;
            }
            try
            {
                string original = Generate();
                byte[] originalPng = File.ReadAllBytes(original);
                var materialStamp = File.GetLastWriteTimeUtc(material);
                File.WriteAllText(material, Material("0.2"));
                File.SetLastWriteTimeUtc(material, materialStamp);
                string changedMaterial = Generate();
                Check(original != changedMaterial && !originalPng.SequenceEqual(File.ReadAllBytes(changedMaterial)),
                    "Changed material reused the previous disk thumbnail.");
                var textureStamp = File.GetLastWriteTimeUtc(texture);
                File.WriteAllBytes(texture, [0]);
                File.SetLastWriteTimeUtc(texture, textureStamp);
                string changedTexture = Generate();
                Check(changedTexture != changedMaterial && !File.ReadAllBytes(changedTexture).SequenceEqual(File.ReadAllBytes(changedMaterial)),
                    "Changed texture reused the previous disk thumbnail.");
                Check(Generate() == changedTexture, "Identical source failed to reuse the disk thumbnail.");
                var colors = new MaterialThumbnailColors();
                Check(MaterialThumbnailGenerator.GetMaterialBaseColor(guid, assets, colors).r == 0.2f, "Initial source color was incorrect.");
                File.WriteAllText(material, Material("0.7"));
                Check(MaterialThumbnailGenerator.GetMaterialBaseColor(guid, assets, colors).r == 0.7f, "Material edit reused a cached source color.");
            }
            finally
            {
                foreach (string path in paths) File.Delete(path);
            }
        }));

        test("Thumbnail generation rejects textures changed during rendering without publishing files", () => InTemp(root =>
        {
            string guid = Guid.NewGuid().ToString("N");
            string material = Path.Combine(root, "material.mat");
            string texture = Path.Combine(root, "texture.bin");
            File.WriteAllText(material, Material());
            File.WriteAllBytes(texture, [1]);
            var assets = new Dictionary<string, string> { [guid] = material, ["texture"] = texture };
            var source = MaterialThumbnailSource.CaptureAsync(guid, assets, CancellationToken.None).GetAwaiter().GetResult()!;
            bool failed = false;
            try
            {
                MaterialThumbnailGenerator.GetOrCreateThumbnailAsync(source, customTextureReader: (path, _) =>
                {
                    File.WriteAllBytes(path, [2]);
                    return (1, 1, new byte[] { 255, 0, 0, 255 });
                }).GetAwaiter().GetResult();
            }
            catch (IOException exception) when (exception.Message.Contains("changed during rendering", StringComparison.Ordinal))
            {
                failed = true;
            }
            Check(failed, "Changed textures were published under a stale source key.");
            Check(!Directory.EnumerateFiles(MaterialThumbnailGenerator.GetThumbnailCacheDirectory(), "*" + guid + "*").Any(),
                "Failed generation left a cached or temporary thumbnail.");
        }));

        test("Thumbnail disk cleanup removes only oldest entries to meet the exact byte limit", () => InTemp(root =>
        {
            var now = DateTime.UtcNow;
            string oldest = CacheFile(root, "v2_old.png", 4, now.AddDays(-3));
            string middle = CacheFile(root, "v3_middle.png", 4, now.AddDays(-2));
            string newest = CacheFile(root, "v3_new.png", 4, now.AddDays(-1));
            string other = CacheFile(root, "keep.txt", 100, now.AddDays(-4));
            ThumbnailDiskCache.Cleanup(root, 8, now.AddDays(-14), FailCleanup);
            Check(!File.Exists(oldest) && File.Exists(middle) && File.Exists(newest) && File.Exists(other),
                "Cleanup removed newer or unrelated files, or did not respect the limit.");
            ThumbnailDiskCache.Cleanup(root, 8, now.AddDays(-14), FailCleanup);
            Check(File.Exists(middle) && File.Exists(newest), "Cleanup removed files already within the limit.");
        }));

        test("Thumbnail disk cleanup expires old PNG and temporary files under the size limit", () => InTemp(root =>
        {
            var now = DateTime.UtcNow;
            string old = CacheFile(root, "v2_old.png", 4, now.AddDays(-15));
            string temp = CacheFile(root, "abandoned.tmp", 4, now.AddDays(-15));
            string fresh = CacheFile(root, "v3_fresh.png", 4, now);
            ThumbnailDiskCache.Cleanup(root, ThumbnailDiskCache.MaximumBytes, now - ThumbnailDiskCache.MaximumAge, FailCleanup);
            Check(!File.Exists(old) && !File.Exists(temp) && File.Exists(fresh), "Age policy removed the wrong files.");
        }));

        test("Thumbnail disk cleanup preserves files in use even when readers allow deletion", () => InTemp(root =>
        {
            var now = DateTime.UtcNow;
            string busy = CacheFile(root, "v3_busy.png", 4, now.AddDays(-20));
            string idle = CacheFile(root, "v3_idle.png", 4, now.AddDays(-1));
            var failures = new List<string>();
            using (var reader = new FileStream(busy, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                ThumbnailDiskCache.Cleanup(root, 4, now.AddDays(-14), failures.Add);
                Check(File.Exists(busy) && !File.Exists(idle) && failures.Count == 1, "Cleanup deleted an in-use file or miscounted retained bytes.");
            }
            ThumbnailDiskCache.Cleanup(root, 4, now.AddDays(-14), FailCleanup);
            Check(!File.Exists(busy), "Released expired file was not cleaned up.");
        }));
    }

    private static string CacheFile(string root, string name, int bytes, DateTime stamp)
    {
        string path = Path.Combine(root, name);
        File.WriteAllBytes(path, new byte[bytes]);
        File.SetLastWriteTimeUtc(path, stamp);
        return path;
    }

    private static void InTemp(Action<string> action)
    {
        string root = Path.Combine(Path.GetTempPath(), "thumbnail-cache-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { action(root); }
        finally { Directory.Delete(root, true); }
    }

    private static void FailCleanup(string message) => throw new Exception(message);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
