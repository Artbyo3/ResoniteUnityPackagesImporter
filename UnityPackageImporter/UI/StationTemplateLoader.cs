using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.UIX;

namespace UnityPackageImporter.UI;

internal sealed class StationTemplate
{
    private readonly Dictionary<string, IWorldElement> bindings;
    public Slot Host { get; }
    public Slot Root { get; }
    public string Version { get; }
    public string Hash { get; }

    public StationTemplate(Slot host, Slot root, TemplateContract contract, string hash)
    {
        Host = host;
        Root = root;
        Version = contract.TemplateVersion;
        Hash = hash;
        bindings = new Dictionary<string, IWorldElement>(StringComparer.Ordinal);
        var existingMetadata = root.Children.SingleOrDefault(child => child.Name == "Importer UI Bindings");
        foreach (var pair in contract.Bindings)
        {
            var reference = existingMetadata?.Children.SingleOrDefault(child => child.Name == pair.Key)
                ?.GetComponent<ReferenceField<IWorldElement>>()?.Reference.Target;
            if (reference != null && !reference.IsRemoved && reference.IsChildOfElement(root) &&
                (pair.Value.Type == "Slot" && reference is Slot || reference is Component existing && FriendlyType(existing.GetType()) == pair.Value.Type))
            {
                bindings.Add(pair.Key, reference);
                continue;
            }
            Slot slot = root;
            foreach (string segment in pair.Value.Path)
            {
                var matches = slot.Children.Where(child => child.Name == segment).ToArray();
                if (matches.Length != 1) throw new InvalidDataException("UI binding path is missing or ambiguous: " + pair.Key);
                slot = matches[0];
            }
            IWorldElement target = slot;
            if (pair.Value.Type != "Slot")
            {
                var matches = slot.Components.Where(component => FriendlyType(component.GetType()) == pair.Value.Type).ToArray();
                if (matches.Length <= pair.Value.Index) throw new InvalidDataException("UI binding component is missing: " + pair.Key);
                target = matches[pair.Value.Index];
            }
            bindings.Add(pair.Key, target);
        }
        // These native references remap on duplication/export. Future UI edits can move a control
        // without losing its role; its immutable package contract remains the migration fallback.
        var metadata = root.Children.SingleOrDefault(child => child.Name == "Importer UI Bindings") ?? root.AddSlot("Importer UI Bindings");
        foreach (var pair in bindings)
        {
            var entry = metadata.Children.SingleOrDefault(child => child.Name == pair.Key) ?? metadata.AddSlot(pair.Key);
            entry.GetComponentOrAttach<ReferenceField<IWorldElement>>().Reference.Target = pair.Value;
        }
    }

    public T Get<T>(string key) where T : class, IWorldElement =>
        bindings.TryGetValue(key, out var value) && value is T typed
            ? typed : throw new InvalidDataException("UI binding has the wrong type: " + key);

    public bool TryGet<T>(string key, out T result) where T : class, IWorldElement
    {
        if (bindings.TryGetValue(key, out var value) && value is T typed)
        {
            result = typed;
            return true;
        }
        result = null;
        return false;
    }

    private sealed record LocalizedEntry(string MessageKey, object[] Args, string RawText);
    private readonly Dictionary<string, LocalizedEntry> activeLocaleEntries = new(StringComparer.Ordinal);
    public event Action<string> LocaleChanged;

    public IEnumerable<IWorldElement> Locale(string key) =>
        bindings.Where(pair => pair.Key.StartsWith("locale." + key + ".", StringComparison.Ordinal))
            .Select(pair => pair.Value);

    private static void ApplyTextToLabel(Text label, string text)
    {
        if (label == null || label.IsDestroyed) return;
        if (!label.Content.IsDriven) label.Content.Value = text;
        Slot singleLine = label.Slot?.Children.FirstOrDefault(child => child.Name == "Single Line Presentation");
        if (singleLine != null && !singleLine.IsDestroyed)
        {
            foreach (var childText in singleLine.GetComponentsInChildren<Text>())
            {
                if (childText != null && !childText.IsDestroyed && !childText.Content.IsDriven)
                    childText.Content.Value = text;
            }
        }
    }

    private void ApplyRawText(string key, string text)
    {
        foreach (var element in Locale(key))
        {
            if (element is Text label)
            {
                ApplyTextToLabel(label, text);
            }
            else if (element is BooleanValueDriver<string> driver && !driver.IsDestroyed)
            {
                if (key.Equals("complete", StringComparison.OrdinalIgnoreCase))
                    driver.FalseValue.Value = text;
                else
                    driver.TrueValue.Value = text;
            }
        }
    }

    public void SetLocalizedRaw(string key, string text)
    {
        string safe = text ?? "";
        activeLocaleEntries[key] = new LocalizedEntry(null, Array.Empty<object>(), safe);
        ApplyRawText(key, safe);
    }

    public void SetLocalized(string key, string english, string japanese)
    {
        string text = LocalizationManager.DetectLocaleCode() == "ja" ? japanese : english;
        SetLocalizedRaw(key, text);
    }

    public void SetLocalized(string key, params object[] args) =>
        SetLocalizedKey(key, key, args);

    public void SetLocalizedKey(string driverKey, string messageKey, params object[] args)
    {
        object[] safeArgs = args ?? Array.Empty<object>();
        activeLocaleEntries[driverKey] = new LocalizedEntry(messageKey, safeArgs, null);
        string text = LocalizationManager.GetString(messageKey, safeArgs);
        ApplyRawText(driverKey, text);
    }

    public void RefreshLocale(string localeCode = null)
    {
        if (Host == null || Host.IsDestroyed || Root == null || Root.IsDestroyed) return;
        string activeLocale = string.IsNullOrWhiteSpace(localeCode)
            ? LocalizationManager.DetectLocaleCode()
            : LocalizationManager.NormalizeLocaleCode(localeCode);
        foreach (var pair in activeLocaleEntries.ToArray())
        {
            var entry = pair.Value;
            if (entry.MessageKey != null)
            {
                if (entry.Args.Length == 0)
                {
                    LocalizationManager.TryGetStaticStringForLocale(activeLocale, entry.MessageKey, out string staticText);
                    ApplyRawText(pair.Key, staticText);
                }
                else
                {
                    ApplyRawText(pair.Key, LocalizationManager.GetStringForLocale(activeLocale, entry.MessageKey, entry.Args));
                }
            }
            else
            {
                ApplyRawText(pair.Key, entry.RawText ?? "");
            }
        }
        LocaleChanged?.Invoke(activeLocale);
    }

    private static string FriendlyType(Type type)
    {
        if (!type.IsGenericType) return type.FullName;
        string name = type.GetGenericTypeDefinition().FullName.Split('`')[0];
        string Argument(Type t) => t == typeof(float) ? "float" : t == typeof(bool) ? "bool" :
            t == typeof(int) ? "int" : t == typeof(string) ? "string" : t.Name;
        return name + "<" + string.Join(",", type.GetGenericArguments().Select(Argument)) + ">";
    }
}

internal static class StationTemplateLoader
{
    private const string ResourcePrefix = "UnityPackageImporter.UI.Templates.";
    private static readonly System.Threading.SemaphoreSlim CacheLock = new(1, 1);

    public static async Task<StationTemplate> LoadAsync(string name, World world, float3 position, floatQ rotation, bool activate = true)
    {
        byte[] package, contractData;
        string packageHash;
        Func<byte[]> getSharedSource;
        await default(ToBackground);
        string devDirectory = UnityPackageImporter.Config.GetValue(UnityPackageImporter.uiTemplateDevelopmentDirectory);
        if (!string.IsNullOrWhiteSpace(devDirectory))
        {
            // Explicit opt-in only. Each new instance reads the current export, never a stale cached object.
            package = await File.ReadAllBytesAsync(Path.Combine(devDirectory, name + ".ResonitePackage"));
            contractData = await File.ReadAllBytesAsync(Path.Combine(devDirectory, name + ".bindings.json"));
            packageHash = Convert.ToHexString(SHA256.HashData(package));
            getSharedSource = () => File.ReadAllBytes(Path.Combine(devDirectory, "station.ResonitePackage"));
        }
        else
        {
            using var manifestStream = Resource("manifest.json");
            var manifest = JsonSerializer.Deserialize<TemplateManifest>(manifestStream, TemplateContract.JsonOptions);
            if (manifest?.SchemaVersion != 1 || manifest.Templates == null || !manifest.Templates.TryGetValue(name, out var hashes))
                throw new InvalidDataException("This mod does not contain a prepared station UI bundle.");
            package = ReadResource(name + ".ResonitePackage");
            contractData = ReadResource(name + ".bindings.json");
            TemplateIntegrity.Verify(package, hashes.PackageSha256);
            TemplateIntegrity.Verify(contractData, hashes.ContractSha256);
            packageHash = hashes.PackageSha256;
            getSharedSource = () =>
            {
                if (!manifest.Templates.TryGetValue("station", out var stationHashes))
                    throw new InvalidDataException("Missing station UI bundle in manifest.");
                byte[] stationBytes = ReadResource("station.ResonitePackage");
                TemplateIntegrity.Verify(stationBytes, stationHashes.PackageSha256);
                return stationBytes;
            };
        }
        byte[] standalonePackage = TemplateIntegrity.EnsureStandalonePackage(package, getSharedSource);
        string standaloneHash = Convert.ToHexString(SHA256.HashData(standalonePackage));
        using var contractStream = new MemoryStream(contractData);
        var contract = TemplateContract.Read(contractStream);
        string directory = Path.Combine(Engine.Current.CachePath, "UnityPackageImporter", "UITemplates");
        string cached = TemplateIntegrity.SafeCacheFile(directory, packageHash);
        await CacheLock.WaitAsync();
        try
        {
            Directory.CreateDirectory(directory);
            bool valid = File.Exists(cached) && Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(cached)))
                .Equals(standaloneHash, StringComparison.OrdinalIgnoreCase);
            if (!valid)
            {
                string temporary = cached + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllBytesAsync(temporary, standalonePackage);
                    File.Move(temporary, cached, true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
        finally { CacheLock.Release(); }

        await default(ToWorld);
        var host = world.AddSlot("Unity Import UI " + name);
        host.PersistentSelf = false;
        host.ActiveSelf = false;
        try
        {
            // Native package import loads the saved root INTO the supplied slot. Keep a
            // separate wrapper inactive: the saved root can overwrite its own Active field.
            var root = host.AddSlot("Template");
            var progress = new TemplateImportProgress();
            await PackageImporter.ImportPackage(cached, root, progress);
            await default(ToWorld);
            // PackageImporter reports some failures through progress instead of throwing.
            if (progress.Failed)
                throw new InvalidDataException("Resonite could not load the bundled " + name + " UI package.");
            if (root.IsDestroyed || root.Parent != host || root.Name != contract.RootName)
                throw new InvalidDataException("The bundled " + name + " UI package did not load its approved root: " + contract.RootName);
            var template = new StationTemplate(host, root, contract, packageHash);
            template.Root.GlobalPosition = position;
            template.Root.GlobalRotation = rotation;
            // Preserve the template's authored scale; native import handles asset dependencies.
            template.Root.PersistentSelf = false;

            EnsureWorldFontFallbacks(world, root);

            // Runtime selection/progress fills parameterized labels later. Clear
            // their saved placeholder patterns before making the package visible.
            string activeLocale = LocalizationManager.DetectLocaleCode();
            foreach (string baseKey in contract.Bindings.Keys
                .Where(key => key.StartsWith("locale.", StringComparison.Ordinal))
                .Select(key => key.Split('.')[1]).Distinct(StringComparer.Ordinal))
            {
                string messageKey = baseKey == "phase" ? "ready" : baseKey;
                if (LocalizationManager.TryGetStaticStringForLocale(activeLocale, messageKey, out _))
                    template.SetLocalizedKey(baseKey, messageKey);
                else
                    template.SetLocalizedRaw(baseKey, "");
            }

            StartLocaleWatcher(host, template, activeLocale);

            host.ActiveSelf = activate;
            UnityPackageImporter.Msg("Loaded bundled " + name + " UI " + contract.TemplateVersion + " (" + contract.Bindings.Count + " bindings).");
            return template;
        }
        catch
        {
            // Roll back the incomplete template host, then rethrow.
            await default(ToWorld);
            if (!host.IsDestroyed) host.Destroy();
            throw;
        }
    }

    private static void EnsureWorldFontFallbacks(World world, Slot templateRoot)
    {
        try
        {
            var worldChain = world?.RootSlot?.GetComponentInChildren<FontChain>();
            if (worldChain == null) return;
            var fallbackProviders = new List<IAssetProvider<Font>>();
            if (worldChain.MainFont.Target != null) fallbackProviders.Add(worldChain.MainFont.Target);
            foreach (var fb in worldChain.FallbackFonts)
            {
                if (fb != null && !fallbackProviders.Contains(fb))
                    fallbackProviders.Add(fb);
            }
            if (fallbackProviders.Count == 0) return;

            foreach (var chain in templateRoot.GetComponentsInChildren<FontChain>())
            {
                if (chain == null || chain.IsDestroyed) continue;
                foreach (var provider in fallbackProviders)
                {
                    if (chain.MainFont.Target == provider || chain.FallbackFonts.Contains(provider)) continue;
                    chain.FallbackFonts.Add().Target = provider;
                }
            }
        }
        catch (Exception ex)
        {
            UnityPackageImporter.Warn("Failed to configure world font fallbacks: " + ex.Message);
        }
    }

    private static void StartLocaleWatcher(Slot host, StationTemplate template, string initialLocale)
    {
        if (host?.World?.RootSlot == null) return;

        Action<string> onGlobalChange = newLocale =>
        {
            if (host == null || host.IsDestroyed || template.Root == null || template.Root.IsDestroyed) return;
            host.World.RunSynchronously(() =>
            {
                if (host.IsDestroyed || template.Root == null || template.Root.IsDestroyed) return;
                template.RefreshLocale(newLocale);
            });
        };
        LocalizationManager.GlobalLocaleChanged += onGlobalChange;

        _ = host.World.RootSlot.StartGlobalTask(async () =>
        {
            string previous = initialLocale;
            try
            {
                while (host != null && !host.IsDestroyed && host.World?.RootSlot != null && !host.World.RootSlot.IsDestroyed)
                {
                    await Task.Delay(400);
                    if (host == null || host.IsDestroyed || host.World?.RootSlot == null || host.World.RootSlot.IsDestroyed) return;
                    await default(ToWorld);
                    if (host.IsDestroyed || template.Root == null || template.Root.IsDestroyed) return;
                    string current = LocalizationManager.DetectLocaleCode();
                    if (!string.Equals(previous, current, StringComparison.OrdinalIgnoreCase))
                    {
                        previous = current;
                        template.RefreshLocale(current);
                    }
                }
            }
            finally
            {
                LocalizationManager.GlobalLocaleChanged -= onGlobalChange;
            }
        });
    }


    private sealed class TemplateImportProgress : IProgressIndicator
    {
        public bool Failed { get; private set; }
        public void UpdateProgress(float progress, LocaleString description, LocaleString detail) { }
        public void ProgressDone(LocaleString message) { }
        public void ProgressFail(LocaleString message) => Failed = true;
    }

    private static System.IO.Stream Resource(string name)
    {
        var assembly = typeof(StationTemplateLoader).Assembly;
        string resource = assembly.GetManifestResourceNames().SingleOrDefault(candidate =>
            candidate.Equals(ResourcePrefix + name, StringComparison.OrdinalIgnoreCase));
        return resource == null ? throw new FileNotFoundException("Missing bundled UI resource: " + name) : assembly.GetManifestResourceStream(resource);
    }
    private static byte[] ReadResource(string name)
    {
        using var stream = Resource(name);
        using var result = new MemoryStream();
        stream.CopyTo(result);
        return result.ToArray();
    }
}
