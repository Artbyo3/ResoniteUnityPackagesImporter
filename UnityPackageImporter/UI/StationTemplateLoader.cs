using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;

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

    public IEnumerable<BooleanValueDriver<string>> Locale(string key) =>
        bindings.Where(pair => pair.Key.StartsWith("locale." + key + ".", StringComparison.Ordinal))
            .Select(pair => (BooleanValueDriver<string>)pair.Value);

    public void SetLocalized(string key, string english, string japanese)
    {
        foreach (var driver in Locale(key))
        {
            driver.FalseValue.Value = english;
            driver.TrueValue.Value = japanese;
        }
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

    public static async Task<StationTemplate> LoadAsync(string name, World world, float3 position, floatQ rotation)
    {
        byte[] package, contractData;
        string packageHash;
        await default(ToBackground);
        string devDirectory = UnityPackageImporter.Config.GetValue(UnityPackageImporter.uiTemplateDevelopmentDirectory);
        if (!string.IsNullOrWhiteSpace(devDirectory))
        {
            // Explicit opt-in only. Each new instance reads the current export, never a stale cached object.
            package = await File.ReadAllBytesAsync(Path.Combine(devDirectory, name + ".ResonitePackage"));
            contractData = await File.ReadAllBytesAsync(Path.Combine(devDirectory, name + ".bindings.json"));
            packageHash = Convert.ToHexString(SHA256.HashData(package));
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
        }
        using var contractStream = new MemoryStream(contractData);
        var contract = TemplateContract.Read(contractStream);
        string directory = Path.Combine(Engine.Current.CachePath, "UnityPackageImporter", "UITemplates");
        string cached = TemplateIntegrity.SafeCacheFile(directory, packageHash);
        await CacheLock.WaitAsync();
        try
        {
            Directory.CreateDirectory(directory);
            bool valid = File.Exists(cached) && Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(cached)))
                .Equals(packageHash, StringComparison.OrdinalIgnoreCase);
            if (!valid)
            {
                string temporary = cached + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllBytesAsync(temporary, package);
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
            host.ActiveSelf = true;
            UnityPackageImporter.Msg("Loaded bundled " + name + " UI " + contract.TemplateVersion + " (" + contract.Bindings.Count + " bindings).");
            return template;
        }
        catch
        {
            await default(ToWorld);
            if (!host.IsDestroyed) host.Destroy();
            throw;
        }
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
