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
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
