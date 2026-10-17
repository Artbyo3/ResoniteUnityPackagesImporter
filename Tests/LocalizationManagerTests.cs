using System;
using System.IO;
using UnityPackageImporter.UI;

internal static class LocalizationManagerTests
{
    internal static void Register(Action<string, Action> test)
    {
        test("LocalizationManager resolves English and Japanese", () =>
        {
            LocalizationManager.Initialize();

            string enReady = LocalizationManager.GetEnglish("ready");
            Check(enReady == "Ready to import", $"Expected 'Ready to import', got '{enReady}'");

            string jaReady = LocalizationManager.GetJapanese("ready");
            Check(jaReady == "インポートの準備完了", $"Expected 'インポートの準備完了', got '{jaReady}'");

            string enCustom = LocalizationManager.GetEnglish("customizeMaterials");
            Check(enCustom == "Customize Materials", $"Expected 'Customize Materials', got '{enCustom}'");

            string jaCustom = LocalizationManager.GetJapanese("customizeMaterials");
            Check(jaCustom == "マテリアルをカスタマイズ", $"Expected 'マテリアルをカスタマイズ', got '{jaCustom}'");
        });

        test("LocalizationManager loads embedded dictionaries outside the repository", () =>
        {
            string workingDirectory = Directory.GetCurrentDirectory();
            string isolatedDirectory = Path.Combine(Path.GetTempPath(), "station-locale-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(isolatedDirectory);
            try
            {
                Directory.SetCurrentDirectory(isolatedDirectory);
                LocalizationManager.Reload();
                Check(LocalizationManager.GetEnglish("ready") == "Ready to import", "English must be available without development files.");
                Check(LocalizationManager.GetJapanese("ready") == "インポートの準備完了", "Japanese must be available without development files.");
            }
            finally
            {
                Directory.SetCurrentDirectory(workingDirectory);
                LocalizationManager.Reload();
                Directory.Delete(isolatedDirectory);
            }
        });

        test("Initial station copy never exposes pending format arguments or unknown keys", () =>
        {
            foreach (string locale in new[] { "en", "ja" })
            {
                Check(LocalizationManager.TryGetStaticStringForLocale(locale, "ready", out string ready) && !string.IsNullOrEmpty(ready),
                    "Static progress copy must be ready before staging.");
                foreach (string key in new[] { "avatarContext", "outfitContext", "loaded", "failureReason" })
                {
                    Check(!LocalizationManager.TryGetStaticStringForLocale(locale, key, out string pending) && pending == "",
                        "Parameterized copy must remain empty until runtime supplies arguments: " + key);
                }
            }
            Check(!LocalizationManager.TryGetStaticStringForLocale("en", "missingTestKey", out string missing) && missing == "",
                "A missing key must not become visible copy.");
            Check(LocalizationManager.TryGetStaticStringForLocale("unknown", "ready", out string fallback) && fallback == "Ready to import",
                "Static initialization must preserve English fallback.");
        });

        test("Initial localized copy distinguishes escaped braces from format arguments", () =>
        {
            LocalizationManager.RegisterLocale("""
            { "localeCode": "zz", "messages": {
                "literalBraces": "Use {{name}}",
                "formattedNumber": "Row {2,4:D2}",
                "invalidPattern": "Broken {"
            } }
            """);
            Check(LocalizationManager.TryGetStaticStringForLocale("zz", "literalBraces", out string literal) && literal == "Use {name}",
                "Escaped braces are ordinary visible text.");
            Check(!LocalizationManager.TryGetStaticStringForLocale("zz", "formattedNumber", out string pending) && pending == "",
                "Aligned or formatted arguments still require runtime data.");
            Check(!LocalizationManager.TryGetStaticStringForLocale("zz", "invalidPattern", out string invalid) && invalid == "",
                "Malformed patterns must not leak into initial UI.");
        });

        test("LocalizationManager formats arguments in localized message templates", () =>
        {
            LocalizationManager.Initialize();

            string enLoaded = LocalizationManager.GetEnglish("loaded", 3, 10);
            Check(enLoaded == "Loaded 3 of 10", $"Expected 'Loaded 3 of 10', got '{enLoaded}'");

            string jaLoaded = LocalizationManager.GetJapanese("loaded", 3, 10);
            Check(jaLoaded == "読み込み済み：3 / 10", $"Expected '読み込み済み：3 / 10', got '{jaLoaded}'");

            string enCtx = LocalizationManager.GetEnglish("avatarContext", 1, 4);
            Check(enCtx == "AVATAR PREFAB (1 of 4)", $"Expected 'AVATAR PREFAB (1 of 4)', got '{enCtx}'");

            string jaCtx = LocalizationManager.GetJapanese("avatarContext", 1, 4);
            Check(jaCtx == "アバタープレハブ（1 / 4）", $"Expected 'アバタープレハブ（1 / 4）', got '{jaCtx}'");

            string enOutfit = LocalizationManager.GetEnglish("outfitContext", 2, 3);
            Check(enOutfit == "OUTFIT PREFAB (2 of 3)", $"Unexpected outfit context: '{enOutfit}'");

            string jaOutfit = LocalizationManager.GetJapanese("outfitContext", 2, 3);
            Check(jaOutfit == "衣装プレハブ（2 / 3）", $"Unexpected outfit context: '{jaOutfit}'");

            string enFail = LocalizationManager.GetEnglish("failureReason", "Corrupt asset");
            Check(enFail == "Import failed: Corrupt asset", $"Expected 'Import failed: Corrupt asset', got '{enFail}'");

            string jaFail = LocalizationManager.GetJapanese("failureReason", "Corrupt asset");
            Check(jaFail == "インポート失敗: Corrupt asset", $"Expected 'インポート失敗: Corrupt asset', got '{jaFail}'");

            string enPkg = LocalizationManager.GetEnglish("packagesCount", 2);
            Check(enPkg == "2 packages", $"Expected '2 packages', got '{enPkg}'");

            string jaPkg = LocalizationManager.GetJapanese("packagesCount", 2);
            Check(jaPkg == "2 個のパッケージ", $"Expected '2 個のパッケージ', got '{jaPkg}'");

            string enSlot = LocalizationManager.GetEnglish("slotPrefix", 1);
            Check(enSlot == "Slot 1", $"Expected 'Slot 1', got '{enSlot}'");

            string jaSlot = LocalizationManager.GetJapanese("slotPrefix", 1);
            Check(jaSlot == "スロット 1", $"Expected 'スロット 1', got '{jaSlot}'");
        });

        test("LocalizationManager handles locale normalization and dynamic locale registration", () =>
        {
            Check(LocalizationManager.NormalizeLocaleCode("ja-JP") == "ja", "NormalizeLocaleCode should trim country code");
            Check(LocalizationManager.NormalizeLocaleCode("EN_us") == "en", "NormalizeLocaleCode should trim underscore variant and lowercase");
            Check(LocalizationManager.NormalizeLocaleCode(null) == "en", "Null should default to 'en'");

            string esJson = """
            {
              "localeCode": "es",
              "authors": ["Test"],
              "messages": {
                "UI.Artbyo3.UnityPackageImporter.ready": "Listo para importar",
                "UI.Artbyo3.UnityPackageImporter.loaded": "Cargado {0} de {1}"
              }
            }
            """;
            LocalizationManager.RegisterLocale(esJson);

            string esReady = LocalizationManager.GetStringForLocale("es", "ready");
            Check(esReady == "Listo para importar", $"Expected 'Listo para importar', got '{esReady}'");

            string esLoaded = LocalizationManager.GetStringForLocale("es-ES", "loaded", 2, 8);
            Check(esLoaded == "Cargado 2 de 8", $"Expected 'Cargado 2 de 8', got '{esLoaded}'");

            // Key not present in 'es' should fall back to English
            string esFallback = LocalizationManager.GetStringForLocale("es", "customizeMaterials");
            Check(esFallback == "Customize Materials", $"Expected 'Customize Materials' as fallback, got '{esFallback}'");
        });
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
