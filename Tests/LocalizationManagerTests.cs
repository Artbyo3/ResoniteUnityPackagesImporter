using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using UnityPackageImporter.UI;

internal static class LocalizationManagerTests
{
    internal static void Register(Action<string, Action> test)
    {
        test("Every locale contains all English keys with matching placeholders and bold tags", () =>
        {
            var assembly = typeof(LocalizationManagerTests).Assembly;
            const string resourcePrefix = "UnityPackageImporter.Localization.";
            using var englishStream = assembly.GetManifestResourceStream(resourcePrefix + "en.json")!;
            using var english = JsonDocument.Parse(englishStream);
            var reference = english.RootElement.GetProperty("messages").EnumerateObject().ToArray();
            var errors = new List<string>();

            foreach (string resource in assembly.GetManifestResourceNames()
                         .Where(name => name.StartsWith(resourcePrefix, StringComparison.Ordinal) &&
                                        name.EndsWith(".json", StringComparison.Ordinal)).Order())
            {
                using var stream = assembly.GetManifestResourceStream(resource)!;
                using var document = JsonDocument.Parse(stream);
                string locale = document.RootElement.GetProperty("localeCode").GetString()!;
                var messages = document.RootElement.GetProperty("messages");
                var missing = reference.Where(entry => !messages.TryGetProperty(entry.Name, out _))
                    .Select(entry => entry.Name).ToArray();
                if (missing.Length > 0)
                    errors.Add($"{locale}: missing keys: {string.Join(", ", missing)}");

                foreach (var entry in reference)
                {
                    if (!messages.TryGetProperty(entry.Name, out var translated)) continue;
                    string expected = entry.Value.GetString()!;
                    string actual = translated.GetString()!;
                    // Sorting allows grammatical reordering while preserving each argument's multiplicity.
                    var expectedArguments = Regex.Matches(expected, @"\{[0-9]+\}").Select(match => match.Value).Order();
                    var actualArguments = Regex.Matches(actual, @"\{[0-9]+\}").Select(match => match.Value).Order();
                    if (!expectedArguments.SequenceEqual(actualArguments))
                        errors.Add($"{locale}: {entry.Name}: placeholders differ from English");
                    var expectedTags = Regex.Matches(expected, @"</?b>").Select(match => match.Value);
                    var actualTags = Regex.Matches(actual, @"</?b>").Select(match => match.Value);
                    if (!expectedTags.SequenceEqual(actualTags))
                        errors.Add($"{locale}: {entry.Name}: bold tags differ from English");
                }
            }

            Check(errors.Count == 0, string.Join(Environment.NewLine, errors));
        });

        test("LocalizationManager resolves English and Japanese", () =>
        {
            LocalizationManager.Initialize();

            string enReady = LocalizationManager.GetEnglish("ready");
            Check(enReady == "Ready to import", $"Expected 'Ready to import', got '{enReady}'");

            string jaReady = LocalizationManager.GetJapanese("ready");
            Check(jaReady == "インポートの準備完了", $"Expected 'インポートの準備完了', got '{jaReady}'");

            string enCustom = LocalizationManager.GetEnglish("customizeMaterials");
            Check(enCustom == "<b>Customize Materials</b>", $"Expected '<b>Customize Materials</b>', got '{enCustom}'");

            string jaCustom = LocalizationManager.GetJapanese("customizeMaterials");
            Check(jaCustom == "<b>マテリアルをカスタマイズ</b>", $"Expected '<b>マテリアルをカスタマイズ</b>', got '{jaCustom}'");
        });

        test("Error report UI messages resolve for every supported locale", () =>
        {
            foreach (string key in new[] { "errorReportTitle", "errorReportDescription", "errorReportLogHeading",
                         "errorReportIssueHeading", "errorReportIssueDescription", "errorReportOpenIssue",
                         "errorReportCopy", "errorReportPrivacyHeading", "errorReportPrivacyDescription" })
                foreach (string locale in LocalizationManager.SupportedLocales)
                    Check(LocalizationManager.TryGetStaticStringForLocale(locale, key, out string text) && !string.IsNullOrWhiteSpace(text),
                        $"Error-report UI must not expose missing keys or pending arguments ({locale}.{key}).");
        });

        test("LocalizationManager loads embedded dictionaries outside the repository for all 26 Resonite locales", () =>
        {
            string workingDirectory = Directory.GetCurrentDirectory();
            string isolatedDirectory = Path.Combine(Path.GetTempPath(), "station-locale-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(isolatedDirectory);
            try
            {
                Directory.SetCurrentDirectory(isolatedDirectory);
                LocalizationManager.Reload();
                Check(LocalizationManager.SupportedLocales.Count == 26, $"Expected 26 supported Resonite locales, got {LocalizationManager.SupportedLocales.Count}");
                foreach (string locale in LocalizationManager.SupportedLocales)
                {
                    Check(LocalizationManager.TryGetStaticStringForLocale(locale, "ready", out string ready) && !string.IsNullOrWhiteSpace(ready),
                        $"Locale '{locale}' must resolve 'ready' from embedded resources.");
                    Check(LocalizationManager.TryGetStaticStringForLocale(locale, "importUnity", out string importUnity) && !string.IsNullOrWhiteSpace(importUnity),
                        $"Locale '{locale}' must resolve 'importUnity' from embedded resources.");
                    Check(LocalizationManager.TryGetStaticStringForLocale(locale, "rawFile", out string rawFile) && !string.IsNullOrWhiteSpace(rawFile),
                        $"Locale '{locale}' must resolve 'rawFile' from embedded resources.");
                }
                Check(LocalizationManager.GetStringForLocale("zh-cn", "ready") != LocalizationManager.GetStringForLocale("zh-tw", "ready"),
                    "zh-cn and zh-tw must remain distinct and not collide.");
                Check(LocalizationManager.GetStringForLocale("es", "customizeMaterials") == "<b>Personalizar materiales</b>",
                    "Spanish embedded locale must resolve '<b>Personalizar materiales</b>'.");
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
            foreach (string locale in LocalizationManager.SupportedLocales)
            {
                Check(LocalizationManager.TryGetStaticStringForLocale(locale, "ready", out string ready) && !string.IsNullOrEmpty(ready),
                    "Static progress copy must be ready before staging: " + locale);
                foreach (string key in new[] { "avatarContext", "outfitContext", "loaded", "failureReason" })
                {
                    Check(!LocalizationManager.TryGetStaticStringForLocale(locale, key, out string pending) && pending == "",
                        $"Parameterized copy must remain empty until runtime supplies arguments ({locale}.{key})");
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
            Check(enSlot == "Material slot 1", $"Expected 'Material slot 1', got '{enSlot}'");

            string jaSlot = LocalizationManager.GetJapanese("slotPrefix", 1);
            Check(jaSlot == "マテリアルスロット 1", $"Expected 'マテリアルスロット 1', got '{jaSlot}'");
        });

        test("LocalizationManager handles locale normalization and dynamic locale registration", () =>
        {
            Check(LocalizationManager.NormalizeLocaleCode("ja-JP") == "ja", "NormalizeLocaleCode should trim country code when not a distinct Resonite locale");
            Check(LocalizationManager.NormalizeLocaleCode("EN_us") == "en", "NormalizeLocaleCode should map en_US to 'en'");
            Check(LocalizationManager.NormalizeLocaleCode("en-GB") == "en-gb", "NormalizeLocaleCode should preserve 'en-gb'");
            Check(LocalizationManager.NormalizeLocaleCode("pt-BR") == "pt-br", "NormalizeLocaleCode should preserve 'pt-br'");
            Check(LocalizationManager.NormalizeLocaleCode("pt-PT") == "pt-br", "NormalizeLocaleCode should map 'pt-PT' to 'pt-br'");
            Check(LocalizationManager.NormalizeLocaleCode("zh-CN") == "zh-cn", "NormalizeLocaleCode should preserve 'zh-cn'");
            Check(LocalizationManager.NormalizeLocaleCode("zh-TW") == "zh-tw", "NormalizeLocaleCode should preserve 'zh-tw'");
            Check(LocalizationManager.NormalizeLocaleCode("zh-HK") == "zh-tw", "NormalizeLocaleCode should map 'zh-HK' to 'zh-tw'");
            Check(LocalizationManager.NormalizeLocaleCode("nb-NO") == "no", "NormalizeLocaleCode should map 'nb-NO' to 'no'");
            Check(LocalizationManager.NormalizeLocaleCode(null) == "en", "Null should default to 'en'");

            string customJson = """
            {
              "localeCode": "xx",
              "authors": ["Test"],
              "messages": {
                "UI.Artbyo3.UnityPackageImporter.ready": "Custom Ready",
                "UI.Artbyo3.UnityPackageImporter.loaded": "Custom {0} of {1}"
              }
            }
            """;
            LocalizationManager.RegisterLocale(customJson);

            string xxReady = LocalizationManager.GetStringForLocale("xx", "ready");
            Check(xxReady == "Custom Ready", $"Expected 'Custom Ready', got '{xxReady}'");

            string xxLoaded = LocalizationManager.GetStringForLocale("xx-YY", "loaded", 2, 8);
            Check(xxLoaded == "Custom 2 of 8", $"Expected 'Custom 2 of 8', got '{xxLoaded}'");

            // Key not present in 'xx' should fall back to English
            string xxFallback = LocalizationManager.GetStringForLocale("xx", "customizeMaterials");
            Check(xxFallback == "<b>Customize Materials</b>", $"Expected '<b>Customize Materials</b>' as fallback, got '{xxFallback}'");
        });

        test("LocalizationManager concurrent reads, dynamic registration, and reload are thread-safe", () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var token = cts.Token;
            int readCount = 0;
            var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

            // Concurrent readers
            var readerTasks = Enumerable.Range(0, 12).Select(_ => Task.Run(() =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var en = LocalizationManager.GetStringForLocale("en", "ready");
                        Check(en == "Ready to import", "English 'ready' must always resolve correctly.");

                        var ja = LocalizationManager.GetStringForLocale("ja", "ready");
                        Check(ja == "インポートの準備完了", "Japanese 'ready' must always resolve correctly.");

                        var fallback = LocalizationManager.GetStringForLocale("non-existent-lang", "ready");
                        Check(fallback == "Ready to import", "Unknown locale must safely fall back to English.");

                        var norm = LocalizationManager.NormalizeLocaleCode("ja-JP");
                        Check(norm == "ja", "NormalizeLocaleCode must remain consistent under concurrent access.");

                        Interlocked.Increment(ref readCount);
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                        break;
                    }
                }
            })).ToArray();

            // Concurrent writers: dynamic registration & reload
            var writerTask = Task.Run(async () =>
            {
                int iteration = 0;
                while (!token.IsCancellationRequested && iteration < 15)
                {
                    iteration++;
                    try
                    {
                        string dynamicJson = $$"""
                        {
                          "localeCode": "dyn{{iteration}}",
                          "messages": {
                            "UI.Artbyo3.UnityPackageImporter.ready": "DynReady {{iteration}}"
                          }
                        }
                        """;
                        LocalizationManager.RegisterLocale(dynamicJson);
                        if (iteration % 5 == 0)
                        {
                            LocalizationManager.Reload();
                        }
                        await Task.Delay(10);
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                        break;
                    }
                }
            });

            Task.WaitAll(writerTask);
            cts.Cancel();
            Task.WaitAll(readerTasks);

            if (!exceptions.IsEmpty)
            {
                throw new AggregateException("Concurrent localization test failed with exceptions", exceptions);
            }

            Check(readCount > 100, $"Concurrent readers must successfully execute many reads, ran {readCount}.");
            // Final check after reloads and registrations: English and Japanese must remain intact
            Check(LocalizationManager.GetEnglish("ready") == "Ready to import", "English ready must be valid after concurrent run.");
            Check(LocalizationManager.GetJapanese("ready") == "インポートの準備完了", "Japanese ready must be valid after concurrent run.");
        });
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
