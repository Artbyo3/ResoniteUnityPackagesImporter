using UnityPackageImporter.Models;

internal static class MaterialCustomizationTests
{
    private const string MaterialYaml = """
        %YAML 1.1
        %TAG !u! tag:unity3d.com,2011:
        --- !u!21 &2100000
        Material:
          m_Name: {0}
          m_Shader: {{fileID: 4800000, guid: shader, type: 3}}
          m_SavedProperties:
            m_Floats: []
            m_Colors: []
            m_TexEnvs: []
        """;

    internal static void Register(Action<string, Action> test)
    {
        test("Material catalog finds package materials independent of their folder names", () =>
        {
            var assets = new Dictionary<string, string>
            {
                ["skin-guid"] = @"C:\cache\Assets\Materials\Skin\Warm.mat",
                ["shirt-guid"] = @"C:\cache\Assets\Other\Blue Shirt.mat",
                ["texture-guid"] = @"C:\cache\Assets\Textures\Albedo.png",
            };
            var catalog = PackageMaterialCatalog.Build(assets, path => path.EndsWith("Warm.mat", StringComparison.Ordinal)
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture, MaterialYaml, "Warm Skin")
                : string.Format(System.Globalization.CultureInfo.InvariantCulture, MaterialYaml, "青いシャツ"));

            Check(catalog.Count == 2, "The catalog should include every readable .mat regardless of folder and omit non-material assets.");
            Check(catalog.Choices.Any(choice => choice.Guid == "skin-guid" && choice.Name == "Warm Skin" && choice.Folder == "Materials/Skin"),
                "The material name or nested folder path was not retained.");
            Check(catalog.Choices.Any(choice => choice.Guid == "shirt-guid" && choice.Name == "青いシャツ" && choice.Folder == "Other"),
                "Japanese material names and their actual package folder should be retained.");
        });

        test("Material catalog search matches creator names and nested package paths", () =>
        {
            var catalog = PackageMaterialCatalog.Build(new Dictionary<string, string>
            {
                ["one"] = @"C:\cache\Assets\Colors\Skin\Warm.mat",
                ["two"] = @"C:\cache\Assets\Clothes\青いシャツ.mat",
            }, path => path.Contains("Warm", StringComparison.Ordinal)
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture, MaterialYaml, "Warm Skin")
                : string.Format(System.Globalization.CultureInfo.InvariantCulture, MaterialYaml, "青いシャツ"));

            Check(catalog.Search("warm skin").Single().Guid == "one", "Search should match material names without case sensitivity.");
            Check(catalog.Search("青いシャツ").Single().Guid == "two", "Search should match creator-authored Japanese names directly.");
            Check(catalog.Search("Skin").Single().Guid == "one", "Search should also match the package's nested folder path.");
            Check(catalog.Search("warm", "Colors").Single().Guid == "one", "Folder-scoped search did not include nested folders.");
        });

        test("Material catalog exposes browsable nested folders without semantic assumptions", () =>
        {
            var catalog = PackageMaterialCatalog.Build(new Dictionary<string, string>
            {
                ["one"] = @"C:\cache\Assets\Options\Skin\Light.mat",
                ["two"] = @"C:\cache\Assets\Options\Skin\Dark.mat",
                ["three"] = @"C:\cache\Assets\Options\Eyes\Blue.mat",
            }, path => string.Format(System.Globalization.CultureInfo.InvariantCulture, MaterialYaml, Path.GetFileNameWithoutExtension(path)));

            Check(catalog.ChildFolders().SequenceEqual(new[] { "Options" }), "Root folder browser should expose the real package hierarchy.");
            Check(catalog.ChildFolders("Options").SequenceEqual(new[] { "Eyes", "Skin" }), "Nested folder browser should deduplicate and sort folders.");
            Check(catalog.Search(folder: "Options/Skin").Count == 2, "Selecting a folder should include its materials.");

            var catalogWithRoot = PackageMaterialCatalog.Build(new Dictionary<string, string>
            {
                ["root"] = @"C:\cache\Assets\RootMaterial.mat",
                ["nested"] = @"C:\cache\Assets\Sub\SubMaterial.mat",
                ["nested-assets"] = @"C:\cache\Assets\Sub\Assets\DeepMaterial.mat",
            }, path => string.Format(System.Globalization.CultureInfo.InvariantCulture, MaterialYaml, Path.GetFileNameWithoutExtension(path)));

            Check(catalogWithRoot.Choices.First(c => c.Guid == "root").Folder == "", "Materials directly under Assets should be at root folder.");
            Check(catalogWithRoot.ChildFolders().SequenceEqual(new[] { "Sub" }), "Root folder browser should not expose Assets as a child folder.");
            Check(catalogWithRoot.Choices.First(c => c.Guid == "nested-assets").Folder == "Sub/Assets", "Nested folder named Assets under a subfolder should be preserved.");
            Check(catalogWithRoot.ChildFolders("Sub").SequenceEqual(new[] { "Assets" }), "Nested folder named Assets should show up under its parent folder.");

            var catalogWithDirectAssetsChild = PackageMaterialCatalog.Build(new Dictionary<string, string>
            {
                ["direct-assets"] = @"C:\cache\Assets\Assets\ChildInAssets.mat",
                ["other"] = @"C:\cache\Assets\Other\Material.mat",
            }, path => string.Format(System.Globalization.CultureInfo.InvariantCulture, MaterialYaml, Path.GetFileNameWithoutExtension(path)));

            Check(catalogWithDirectAssetsChild.Choices.First(c => c.Guid == "direct-assets").Folder == "Assets",
                "A child folder named Assets directly under the root Assets folder should be preserved as 'Assets'.");
            Check(catalogWithDirectAssetsChild.ChildFolders().SequenceEqual(new[] { "Assets", "Other" }),
                "Root child folders should include the user's Assets subfolder and Other.");
            Check(catalogWithDirectAssetsChild.Search(folder: "Assets").Count == 1,
                "Searching inside the Assets subfolder should return the material inside it without collapsing to root.");
        });

        test("Material thumbnail renderer bakes 128x128 PNG orb with anti-aliasing, diffuse, and specular", () =>
        {
            string yaml = """
                %YAML 1.1
                %TAG !u! tag:unity3d.com,2011:
                --- !u!21 &2100000
                Material:
                  m_Name: RedGlossy
                  m_Shader: {fileID: 4800000, guid: shader, type: 3}
                  m_SavedProperties:
                    m_Floats:
                      - _Smoothness: 0.85
                      - _Metallic: 0.1
                    m_Colors:
                      - _Color: {r: 0.9, g: 0.1, b: 0.1, a: 1.0}
                    m_TexEnvs: []
                """;
            var data = UnityMaterialData.Parse(yaml);
            var assets = new Dictionary<string, string>();
            byte[] png = MaterialThumbnailGenerator.RenderOrbPng(data, assets, null);

            Check(png.Length > 100, "Thumbnail PNG should have non-zero length.");
            Check(png[0] == 0x89 && png[1] == 0x50 && png[2] == 0x4E && png[3] == 0x47, "PNG signature must match.");

            byte[] rgba = MaterialThumbnailGenerator.RenderOrbRgba(data, assets, null);
            Check(rgba.Length == 128 * 128 * 4, "RGBA buffer should be 128x128x4 bytes.");

            // Corner (0, 0) should be transparent
            int cornerAlpha = rgba[3];
            Check(cornerAlpha == 0, $"Corner pixel should be fully transparent, got {cornerAlpha}.");

            // Center (63, 63) should be opaque red
            int centerIdx = (63 * 128 + 63) * 4;
            int centerR = rgba[centerIdx + 0];
            int centerA = rgba[centerIdx + 3];
            Check(centerA > 250, $"Center pixel should be fully opaque, got {centerA}.");
            Check(centerR > 100, $"Center pixel should have strong red component, got {centerR}.");

            // Key light specular highlight is near top-left of center (e.g. 50, 50)
            int highIdx = (50 * 128 + 50) * 4;
            int shadowIdx = (75 * 128 + 75) * 4;
            int highR = rgba[highIdx + 0];
            int shadowR = rgba[shadowIdx + 0];
            Check(highR > shadowR, $"Lit upper-left area ({highR}) should be brighter than shadow ({shadowR}).");
        });

        test("Material thumbnail renderer downsamples and maps texture onto spherical UVs", () =>
        {
            string yaml = """
                %YAML 1.1
                %TAG !u! tag:unity3d.com,2011:
                --- !u!21 &2100000
                Material:
                  m_Name: TexturedMat
                  m_Shader: {fileID: 4800000, guid: shader, type: 3}
                  m_SavedProperties:
                    m_Floats:
                      - _Smoothness: 0.5
                    m_Colors:
                      - _Color: {r: 1.0, g: 1.0, b: 1.0, a: 1.0}
                    m_TexEnvs:
                      - _MainTex:
                          m_Texture: {fileID: 2800000, guid: tex-guid, type: 3}
                          m_Scale: {x: 1, y: 1}
                          m_Offset: {x: 0, y: 0}
                """;
            var data = UnityMaterialData.Parse(yaml);
            var assets = new Dictionary<string, string> { ["tex-guid"] = @"C:\cache\blue_tex.png" };

            // Mock 2x2 cyan texture
            byte[] mockRgba = new byte[]
            {
                0, 255, 255, 255,   0, 255, 255, 255,
                0, 255, 255, 255,   0, 255, 255, 255
            };
            MaterialThumbnailGenerator.TextureReader mockReader = (path, maxDim) => (2, 2, mockRgba);

            byte[] rgba = MaterialThumbnailGenerator.RenderOrbRgba(data, assets, mockReader);
            int centerIdx = (63 * 128 + 63) * 4;
            int centerG = rgba[centerIdx + 1];
            int centerB = rgba[centerIdx + 2];
            Check(centerG > 50 && centerB > 50, "Texture cyan tint should be reflected in sphere center.");
        });

        test("Material thumbnail disk cache produces consistent cached file", () =>
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "ThumbTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string matFile = Path.Combine(tempDir, "TestMat.mat");
            try
            {
                File.WriteAllText(matFile, string.Format(System.Globalization.CultureInfo.InvariantCulture, MaterialYaml, "CachedMaterial"));
                var assets = new Dictionary<string, string> { ["mat-cache-guid"] = matFile };

                var task1 = MaterialThumbnailGenerator.GetOrCreateThumbnailAsync("mat-cache-guid", assets);
                string? file1 = task1.GetAwaiter().GetResult();
                Check(file1 != null && File.Exists(file1), "Thumbnail file should be created on disk.");

                var task2 = MaterialThumbnailGenerator.GetOrCreateThumbnailAsync("mat-cache-guid", assets);
                string? file2 = task2.GetAwaiter().GetResult();
                Check(file1 == file2, "Repeated call should return identical cached file path.");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        });

        test("ThumbnailTaskCache handles concurrent requests for the same material with single generation", () =>
        {
            var cache = new ThumbnailTaskCache();
            int generateCount = 0;
            var expectedUri = new Uri("resdb:///test-material-hash");

            var tasks = Enumerable.Range(0, 16).Select(_ => cache.GetOrLoadThumbnailAsync("guid-1", async _ =>
            {
                Interlocked.Increment(ref generateCount);
                await Task.Delay(50);
                return expectedUri;
            })).ToArray();

            Task.WaitAll(tasks);
            Check(generateCount == 1, $"Generation should run exactly once for concurrent requests, ran {generateCount} times.");
            Check(tasks.All(t => t.Result == expectedUri), "All concurrent requests should receive the identical URI.");
            Check(cache.TryGetCachedUri("guid-1", out var cached) && cached == expectedUri, "URI must be saved in cache.");
        });

        test("ThumbnailTaskCache caller cancellation does not poison cache and allows subsequent retrieval", () =>
        {
            var cache = new ThumbnailTaskCache();
            var expectedUri = new Uri("resdb:///test-cancel-recovery");
            var tcs = new TaskCompletionSource<Uri?>();

            using var cts = new CancellationTokenSource();
            var firstTask = cache.GetOrLoadThumbnailAsync("guid-cancel", _ => tcs.Task, cts.Token);
            cts.Cancel();

            bool threwCanceled = false;
            try
            {
                firstTask.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                threwCanceled = true;
            }
            Check(threwCanceled, "First caller should observe OperationCanceledException when callerToken is canceled.");

            // Complete the background task
            tcs.SetResult(expectedUri);

            // Second caller comes in after cancellation
            var secondTask = cache.GetOrLoadThumbnailAsync("guid-cancel", _ => Task.FromResult<Uri?>(null));
            var secondResult = secondTask.GetAwaiter().GetResult();
            Check(secondResult == expectedUri, "Subsequent caller should receive result once background task completes.");
        });

        test("ThumbnailTaskCache retries after fault and null without poisoning cache", () =>
        {
            var cache = new ThumbnailTaskCache();
            var expectedUri = new Uri("resdb:///test-fault-recovery");
            int attempts = 0;

            Func<string, Task<Uri?>> failingGenerator = _ =>
            {
                int attempt = Interlocked.Increment(ref attempts);
                if (attempt == 1) throw new InvalidOperationException("Simulated transient generator fault");
                if (attempt == 2) return Task.FromResult<Uri?>(null); // transient null return
                return Task.FromResult<Uri?>(expectedUri);
            };

            // Attempt 1: Throws
            bool threwFault = false;
            try
            {
                cache.GetOrLoadThumbnailAsync("guid-fault", failingGenerator).GetAwaiter().GetResult();
            }
            catch (InvalidOperationException)
            {
                threwFault = true;
            }
            Check(threwFault, "Attempt 1 should propagate exception.");

            // Attempt 2: Returns null
            var result2 = cache.GetOrLoadThumbnailAsync("guid-fault", failingGenerator).GetAwaiter().GetResult();
            Check(result2 == null, "Attempt 2 should return null without caching it.");

            // Attempt 3: Succeeds
            var result3 = cache.GetOrLoadThumbnailAsync("guid-fault", failingGenerator).GetAwaiter().GetResult();
            Check(result3 == expectedUri, "Attempt 3 should succeed after previous fault and null were safely evicted.");
            Check(attempts == 3, $"Should have invoked generator 3 times, got {attempts}.");
        });

        test("ThumbnailTaskCache simulates rapid folder switching and searching without deadlock or poisoning", () =>
        {
            var cache = new ThumbnailTaskCache();

            // Rapid switching across 10 folders/search queries
            for (int i = 0; i < 10; i++)
            {
                using var cts = new CancellationTokenSource();
                var guid = $"mat-{i % 3}";
                var task = cache.GetOrLoadThumbnailAsync(guid, async id =>
                {
                    await Task.Delay(20);
                    return new Uri($"resdb:///{id}");
                }, cts.Token);

                // Cancel rapidly
                cts.Cancel();
                try { task.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
            }

            // Stable navigation settles and requests all 3 materials
            var stable1 = cache.GetOrLoadThumbnailAsync("mat-0", id => Task.FromResult<Uri?>(new Uri($"resdb:///{id}"))).GetAwaiter().GetResult();
            var stable2 = cache.GetOrLoadThumbnailAsync("mat-1", id => Task.FromResult<Uri?>(new Uri($"resdb:///{id}"))).GetAwaiter().GetResult();
            var stable3 = cache.GetOrLoadThumbnailAsync("mat-2", id => Task.FromResult<Uri?>(new Uri($"resdb:///{id}"))).GetAwaiter().GetResult();

            Check(stable1 == new Uri("resdb:///mat-0"), "mat-0 must load correctly after rapid navigation.");
            Check(stable2 == new Uri("resdb:///mat-1"), "mat-1 must load correctly after rapid navigation.");
            Check(stable3 == new Uri("resdb:///mat-2"), "mat-2 must load correctly after rapid navigation.");

            cache.Clear();
            Check(cache.InFlightCount == 0 && cache.CachedUriCount == 0, "Cache clear should empty all entries.");
        });

        test("Empty catalog yields exactly one card for the slot's current material", () =>
        {
            var emptyChoices = Array.Empty<PackageMaterialChoice>();
            var ordered = PackageMaterialCatalog.OrderPickerChoices(
                currentGuid: null,
                currentName: "Missing Material",
                currentColor: (0.75f, 0.75f, 0.75f, 1f),
                catalogChoices: emptyChoices,
                allCatalogChoices: emptyChoices);

            Check(ordered.Count == 1, $"Empty catalog must yield exactly one card, got {ordered.Count}");
            Check(ordered[0].Name == "Missing Material", "Card name should match current material name");
            Check(string.IsNullOrEmpty(ordered[0].Guid), "Runtime card should have no GUID");
            Check(ordered[0].BaseColor == (0.75f, 0.75f, 0.75f, 1f), "Runtime card should retain material color");
        });

        test("With catalog entries the current material is first and not duplicated", () =>
        {
            var choice1 = new PackageMaterialChoice("guid-1", "Material One", "Materials", "Assets/Materials/Material One.mat");
            var choice2 = new PackageMaterialChoice("guid-2", "Material Two", "Materials", "Assets/Materials/Material Two.mat");
            var choice3 = new PackageMaterialChoice("guid-3", "Material Three", "Materials", "Assets/Materials/Material Three.mat");
            var catalog = new[] { choice1, choice2, choice3 };

            // Scenario A: current material matches choice2
            var orderedMatching = PackageMaterialCatalog.OrderPickerChoices(
                currentGuid: "guid-2",
                currentName: "Material Two",
                currentColor: (1f, 1f, 1f, 1f),
                catalogChoices: catalog,
                allCatalogChoices: catalog);

            Check(orderedMatching.Count == 3, $"Should have 3 cards, got {orderedMatching.Count}");
            Check(orderedMatching[0].Guid == "guid-2", "Current material must be first");
            Check(orderedMatching.Count(c => c.Guid == "guid-2") == 1, "Current material must not be duplicated");
            Check(orderedMatching[1].Guid == "guid-1", "Remaining choices follow");
            Check(orderedMatching[2].Guid == "guid-3", "Remaining choices follow");

            // Scenario B: current material is not in catalog (runtime/missing)
            var orderedNonMatching = PackageMaterialCatalog.OrderPickerChoices(
                currentGuid: "unknown-guid",
                currentName: "Custom Runtime Material",
                currentColor: (0.5f, 0.5f, 0.5f, 1f),
                catalogChoices: catalog,
                allCatalogChoices: catalog);

            Check(orderedNonMatching.Count == 4, $"Should have 4 cards (runtime + 3 catalog), got {orderedNonMatching.Count}");
            Check(orderedNonMatching[0].Name == "Custom Runtime Material", "Runtime material must be first");
            Check(string.IsNullOrEmpty(orderedNonMatching[0].Guid), "Runtime material should have empty GUID");
            Check(orderedNonMatching.Skip(1).Select(c => c.Guid).SequenceEqual(new[] { "guid-1", "guid-2", "guid-3" }),
                "Catalog choices follow without duplication");
        });

        test("MaterialColorReader reads typed color from known material and falls back to neutral tint", () =>
        {
            var pbs = new MaterialColorReader.MockPbsMetallic { AlbedoColor = (0.2f, 0.4f, 0.6f, 1f) };
            var readColor = MaterialColorReader.ReadMaterialColorRgba(pbs);
            Check(readColor == (0.2f, 0.4f, 0.6f, 1f), $"Expected typed color (0.2, 0.4, 0.6, 1), got {readColor}");

            var fallbackColor = MaterialColorReader.ReadMaterialColorRgba(new MaterialColorReader.MockUnknownMaterial());
            Check(fallbackColor == MaterialColorReader.NeutralTint, $"Expected neutral tint fallback, got {fallbackColor}");
        });
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
