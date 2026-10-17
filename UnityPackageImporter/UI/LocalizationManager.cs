using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
#if !REGRESSION_TESTS
using Elements.Core;
using FrooxEngine;
#endif

namespace UnityPackageImporter.UI;

internal static class LocalizationManager
{
    private const string ResourcePrefix = "UnityPackageImporter.Localization.";
    private const string KeyPrefix = "UI.Artbyo3.UnityPackageImporter.";

    // localeCode -> (key -> messagePattern)
    private static readonly Dictionary<string, Dictionary<string, string>> Messages = new(StringComparer.OrdinalIgnoreCase);
    private static bool initialized;
    private static readonly object InitLock = new();

    static LocalizationManager()
    {
        Initialize();
    }

    public static void Initialize()
    {
        lock (InitLock)
        {
            if (initialized) return;
            LoadEmbeddedResources();
            LoadDevelopmentOverrides();
            initialized = true;
        }
    }

    public static void Reload()
    {
        lock (InitLock)
        {
            Messages.Clear();
            LoadEmbeddedResources();
            LoadDevelopmentOverrides();
            initialized = true;
        }
    }

    public static void RegisterLocale(string jsonContent)
    {
        if (string.IsNullOrWhiteSpace(jsonContent)) return;
        lock (InitLock)
        {
            using var doc = JsonDocument.Parse(jsonContent);
            ParseLocaleDocument(doc.RootElement);
        }
    }

    private static void LoadEmbeddedResources()
    {
        var assembly = typeof(LocalizationManager).Assembly;
        foreach (string name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(ResourcePrefix, StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                using var stream = assembly.GetManifestResourceStream(name);
                if (stream != null)
                {
                    using var doc = JsonDocument.Parse(stream);
                    ParseLocaleDocument(doc.RootElement);
                }
            }
            catch (Exception ex)
            {
                Warn("Failed to load embedded localization resource: " + name + " -> " + ex.Message);
            }
        }
    }

    private static void LoadDevelopmentOverrides()
    {
        try
        {
            var searchPaths = new List<string>();
#if !REGRESSION_TESTS
            string devDirectory = UnityPackageImporter.Config?.GetValue(UnityPackageImporter.uiTemplateDevelopmentDirectory);
            if (!string.IsNullOrWhiteSpace(devDirectory))
            {
                searchPaths.Add(Path.Combine(devDirectory, "localization", "station"));
                searchPaths.Add(Path.Combine(devDirectory, "localization"));
            }
#endif
            searchPaths.Add(Path.Combine(Directory.GetCurrentDirectory(), "localization", "station"));
            searchPaths.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "localization", "station"));

            foreach (var dir in searchPaths)
            {
                if (Directory.Exists(dir))
                {
                    foreach (var file in Directory.GetFiles(dir, "*.json"))
                    {
                        try
                        {
                            using var stream = File.OpenRead(file);
                            using var doc = JsonDocument.Parse(stream);
                            ParseLocaleDocument(doc.RootElement);
                        }
                        catch { }
                    }
                    break;
                }
            }
        }
        catch { }
    }

    private static void ParseLocaleDocument(JsonElement root)
    {
        if (!root.TryGetProperty("localeCode", out var codeProp)) return;
        string localeCode = codeProp.GetString();
        if (string.IsNullOrWhiteSpace(localeCode)) return;
        localeCode = NormalizeLocaleCode(localeCode);

        if (!Messages.TryGetValue(localeCode, out var dict))
        {
            dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Messages[localeCode] = dict;
        }

        if (root.TryGetProperty("messages", out var messagesProp) && messagesProp.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in messagesProp.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.GetString() ?? "";
            }
        }
    }

    public static string NormalizeLocaleCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "en";
        code = code.Trim().ToLowerInvariant();
        int dash = code.IndexOf('-');
        if (dash > 0) code = code.Substring(0, dash);
        int under = code.IndexOf('_');
        if (under > 0) code = code.Substring(0, under);
        return code;
    }

    public static string DetectLocaleCode()
    {
#if !REGRESSION_TESTS
        try
        {
            var settings = Userspace.UserspaceWorld?.RootSlot?.GetComponentInChildren<LocaleSettings>();
            if (settings != null)
            {
                string active = settings.ActiveLocaleCode;
                if (!string.IsNullOrWhiteSpace(active))
                {
                    string norm = NormalizeLocaleCode(active);
                    if (Messages.ContainsKey(norm)) return norm;
                }
                string primary = settings.PrimaryInterfaceLocaleCode?.Value;
                if (!string.IsNullOrWhiteSpace(primary))
                {
                    string norm = NormalizeLocaleCode(primary);
                    if (Messages.ContainsKey(norm)) return norm;
                }
            }
        }
        catch { }
#endif

        try
        {
            string culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            if (!string.IsNullOrWhiteSpace(culture))
            {
                string norm = NormalizeLocaleCode(culture);
                if (Messages.ContainsKey(norm)) return norm;
            }
        }
        catch { }

        return "en";
    }

    public static string GetString(string key, params object[] args) =>
        GetStringForLocale(DetectLocaleCode(), key, args);

    public static string GetStringForLocale(string localeCode, string key, params object[] args)
    {
        if (string.IsNullOrEmpty(key)) return "";
        if (!TryGetPattern(localeCode, key, out string template)) return key;

        if (args != null && args.Length > 0)
        {
            try
            {
                return string.Format(template, args);
            }
            catch (FormatException)
            {
                return template;
            }
        }
        return template;
    }

    // A freshly loaded template has no selection or progress arguments yet.
    // Do not show format tokens (or an unknown key) while that state is pending.
    public static bool TryGetStaticStringForLocale(string localeCode, string key, out string text)
    {
        text = "";
        if (string.IsNullOrEmpty(key) || !TryGetPattern(localeCode, key, out string template)) return false;
        try
        {
            var format = CompositeFormat.Parse(template);
            if (format.MinimumArgumentCount != 0) return false;
            text = string.Format(CultureInfo.CurrentCulture, template, Array.Empty<object>());
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool TryGetPattern(string localeCode, string key, out string template)
    {
        string normalized = NormalizeLocaleCode(localeCode);

        template = null;
        if (Messages.TryGetValue(normalized, out var dict))
        {
            dict.TryGetValue(KeyPrefix + key, out template);
            if (template == null) dict.TryGetValue(key, out template);
        }

        // Fallback to "en" if not found in requested locale
        if (template == null && !normalized.Equals("en", StringComparison.OrdinalIgnoreCase) && Messages.TryGetValue("en", out var enDict))
        {
            enDict.TryGetValue(KeyPrefix + key, out template);
            if (template == null) enDict.TryGetValue(key, out template);
        }

        return template != null;
    }

    public static string GetEnglish(string key, params object[] args) => GetStringForLocale("en", key, args);
    public static string GetJapanese(string key, params object[] args) => GetStringForLocale("ja", key, args);

    private static void Warn(string message)
    {
#if !REGRESSION_TESTS
        UnityPackageImporter.Warn(message);
#else
        Console.WriteLine("[LocalizationManager WARN] " + message);
#endif
    }
}
