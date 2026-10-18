#if TEXTURE_PIPELINE_TESTS
using System.Reflection;
using System.Runtime.Loader;
using Elements.Assets;
using UnityPackageImporter.Materials;
using UnityPackageImporter.Models;

if (args.Length != 1 && args.Length != 3) throw new ArgumentException("Pass the Resonite directory, optionally followed by a local mask and output path for a private replay.");
string gameDirectory = Path.GetFullPath(args[0]);
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    string candidate = Path.Combine(gameDirectory, name.Name + ".dll");
    return File.Exists(candidate) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate) : null;
};
Environment.SetEnvironmentVariable("PATH", gameDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));
return NativeTexturePipelineChecks.Run(args.Length == 3 ? args[1] : null, args.Length == 3 ? args[2] : null);

internal static class NativeTexturePipelineChecks
{
    internal static int Run(string replayMask = null, string replayOutput = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "importer-native-textures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        int passed = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        string Source(int width, int height)
        {
            byte[] pixels = new byte[width * height * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            { pixels[i] = 128; pixels[i + 1] = 64; pixels[i + 2] = 192; pixels[i + 3] = 128; }
            string path = Path.Combine(root, $"source-{width}-{height}.png");
            File.WriteAllBytes(path, RgbaPng.Encode(pixels, width, height));
            return path;
        }
        try
        {
            // The previous positional arguments pass a negative size limit and a zero scale.
            // This reproduces the failure using the actual native codec, independently of any avatar.
            string legacySource = Source(512, 4);
            bool reproduced = false;
            try
            {
                var legacy = Bitmap2D.Load(legacySource, null, AlphaHandling.ForceRGBA, -1, 0f);
                reproduced = legacy.Size.x != 512 || legacy.Size.y != 4;
            }
            catch { reproduced = true; }
            Check(reproduced, "Legacy decoder failure was not reproduced.");
            Console.WriteLine("PASS reproduced legacy dimension loss with the actual codec"); passed++;

            foreach (var (width, height) in new[] { (1, 1), (512, 4), (4, 512), (128, 64), (64, 128), (257, 131) })
            {
                string input = Source(width, height);
                string output = Path.Combine(root, $"processed-{width}-{height}.png");
                MaterialTextureProcessor.ProcessFile(input, output, multiplyRgbByAlpha: true);
                var bitmap = Bitmap2D.Load(output, generateMipmaps: false, alphaHandling: AlphaHandling.ForceRGBA);
                Check(bitmap.Size.x == width && bitmap.Size.y == height, "Processing resized the source image.");
                var pixel = bitmap[0, 0];
                Check(Math.Abs(pixel.r - 64f / 255) < 0.01f && Math.Abs(pixel.a - 128f / 255) < 0.01f, "Alpha multiplication or alpha preservation failed.");
                Console.WriteLine($"PASS processed {width}x{height} retains dimensions, pixel values and alpha"); passed++;
            }
            string packed = Path.Combine(root, "packed.png");
            MaterialTextureProcessor.ProcessFile(Source(128, 64), packed, transform: TexturePixelTransform.Swizzle(2, 9, 4, 6));
            var swizzled = Bitmap2D.Load(packed, generateMipmaps: false, alphaHandling: AlphaHandling.ForceRGBA)[0, 0];
            Check(Math.Abs(swizzled.r - 192f / 255) < 0.01f && swizzled.g < 0.01f && swizzled.b > 0.99f && Math.Abs(swizzled.a - 191f / 255) < 0.01f,
                "Native encode/decode changed packed material channels.");
            Console.WriteLine("PASS packed-channel processing survives native encode/decode"); passed++;
            string emission = Path.Combine(root, "emission.png"), mask = Path.Combine(root, "mask.png");
            File.WriteAllBytes(emission, RgbaPng.Encode(new byte[]
            { 255, 0, 0, 128, 0, 255, 0, 255, 0, 0, 255, 64, 255, 255, 255, 255 }, 2, 2));
            File.WriteAllBytes(mask, RgbaPng.Encode(new byte[]
            { 0, 0, 0, 255, 64, 0, 0, 255, 255, 0, 0, 255, 128, 0, 0, 255, 192, 0, 0, 255, 32, 0, 0, 255 }, 3, 2));
            File.WriteAllText(mask + ".meta", "TextureImporter:\n  mipmaps: {sRGBTexture: 0}\n  textureSettings: {filterMode: 1, wrapU: 0, wrapV: 0}\n");
            var composition = new TextureCompositionPlan(new[]
            {
                new TextureContribution("color", 0, 1, 1, 0, 0, TextureContributionKind.ColorWithAlpha),
                new TextureContribution("mask", 0, 1, 1, 0, 0, TextureContributionKind.ScalarMask)
            });
            string baked = Path.Combine(root, "composed.png");
            TextureCompositionBaker.Bake(composition, new Dictionary<string, string> { ["color"] = emission, ["mask"] = mask }, baked);
            var inputs = new Dictionary<string, TextureCompositionBaker.Source>
            { ["color"] = TextureCompositionBaker.ReadSource(emission), ["mask"] = TextureCompositionBaker.ReadSource(mask) };
            var combined = TextureCompositionBaker.ReadSource(baked);
            for (int y = 0; y < 2; y++) for (int x = 0; x < 3; x++)
            {
                float u = (x + 0.5f) / 3, v = (y + 0.5f) / 2;
                var expected = composition.Evaluate(i => TextureCompositionBaker.Sample(inputs[i.Guid], u, v));
                var actual = TextureCompositionBaker.Sample(combined, u, v);
                Check(Math.Abs(expected.r - actual.r) < 0.015f && Math.Abs(expected.g - actual.g) < 0.015f && Math.Abs(expected.b - actual.b) < 0.015f,
                    $"Composition changed UV orientation, channels, alpha or color space at ({x},{y}): {actual} vs {expected}.");
            }
            Console.WriteLine("PASS emission composition preserves UV orientation, mixed source sizes, linear masks and alpha"); passed++;
            File.WriteAllText(mask + ".meta", "TextureImporter:\n  mipmaps: {sRGBTexture: 0}\n  alphaSource: 0\n  textureSettings: {filterMode: 0, wrapU: 1, wrapV: 1}\n");
            var metadataSource = TextureCompositionBaker.ReadSource(mask);
            Check(!metadataSource.Srgb && metadataSource.Point && metadataSource.IgnoreAlpha && metadataSource.WrapU == 1,
                "Source metadata was not reflected in sampling.");
            Console.WriteLine("PASS source metadata controls linear sampling, filtering, wrapping and discarded alpha"); passed++;
            if (replayMask != null)
            {
                var replayPlan = new TextureCompositionPlan(new[]
                { new TextureContribution("source-mask", 0, 1, 1, 0, 0, TextureContributionKind.ScalarMask) });
                TextureCompositionBaker.Bake(replayPlan, new Dictionary<string, string> { ["source-mask"] = replayMask }, replayOutput);
                var original = TextureCompositionBaker.ReadSource(replayMask);
                var result = TextureCompositionBaker.ReadSource(replayOutput);
                bool darkFound = false, litFound = false;
                for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                {
                    float u = (x + 0.5f) / 64, v = (y + 0.5f) / 64;
                    float expected = TextureCompositionBaker.Sample(original, u, v).r;
                    float actual = TextureCompositionBaker.Sample(result, u, v).r;
                    Check(Math.Abs(expected - actual) < 0.02f, "Private mask replay changed the source mask.");
                    darkFound |= actual < 0.01f; litFound |= actual > 0.5f;
                }
                Check(darkFound && litFound, "Private source replay did not preserve both dark and glowing areas.");
                Console.WriteLine("PASS private source mask replay preserves dark and glowing regions"); passed++;
            }
            string cancelled = Path.Combine(root, "cancelled.png");
            bool propagated = false;
            try { MaterialTextureProcessor.ProcessFile(Source(64, 64), cancelled, checkCancellation: () => throw new OperationCanceledException()); }
            catch (OperationCanceledException) { propagated = true; }
            Check(propagated && !File.Exists(cancelled), "Cancellation published a processed image.");
            Console.WriteLine("PASS cancellation leaves no processed image"); passed++;
            Console.WriteLine($"{passed}/{passed} native texture checks passed.");
            return 0;
        }
        finally
        {
            string cleanup = Path.GetFullPath(root);
            string temporaryRoot = Path.GetFullPath(Path.GetTempPath());
            if (!cleanup.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(cleanup).StartsWith("importer-native-textures-", StringComparison.Ordinal))
                throw new InvalidOperationException("Test cleanup escaped its temporary directory.");
            Directory.Delete(cleanup, recursive: true);
        }
    }
}
#endif
