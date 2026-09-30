using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using UnityPackageImporter.UI;

// Inspect embedded resources without loading the mod or executing game code.
internal static class UITemplateAudit
{
    public static int Run(string assemblyPath, string templateDirectory)
    {
        try
        {
            byte[] assemblyBytes = File.ReadAllBytes(assemblyPath);
            foreach (string prefix in new[] { @"C:\Users\", "C:/Users/" })
                if (assemblyBytes.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(prefix)) >= 0 ||
                    assemblyBytes.AsSpan().IndexOf(System.Text.Encoding.Unicode.GetBytes(prefix)) >= 0)
                    throw new InvalidDataException("The assembly contains an unmapped local account path.");
            using var assembly = File.OpenRead(assemblyPath);
            using var pe = new PEReader(assembly);
            var metadata = pe.GetMetadataReader();
            var section = pe.GetSectionData(pe.PEHeaders.CorHeader!.ResourcesDirectory.RelativeVirtualAddress);
            var resources = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var handle in metadata.ManifestResources)
            {
                var resource = metadata.GetManifestResource(handle);
                if (!resource.Implementation.IsNil) continue;
                var reader = section.GetReader(checked((int)resource.Offset), section.Length - checked((int)resource.Offset));
                resources.Add(metadata.GetString(resource.Name), reader.ReadBytes(reader.ReadInt32()));
            }
            byte[] Get(string name) => resources["UnityPackageImporter.UI.Templates." + name];
            var manifestBytes = Get("manifest.json");
            if (!manifestBytes.SequenceEqual(File.ReadAllBytes(Path.Combine(templateDirectory, "manifest.json"))))
                throw new InvalidDataException("The embedded UI manifest differs from the prepared bundle.");
            var manifest = JsonSerializer.Deserialize<TemplateManifest>(manifestBytes, TemplateContract.JsonOptions)!;
            if (manifest.SchemaVersion != 1 || manifest.Templates.Count != 2)
                throw new InvalidDataException("Invalid embedded UI bundle manifest.");
            foreach (string name in new[] { "station", "pre-import" })
            {
                byte[] package = Get(name + ".ResonitePackage"), contractBytes = Get(name + ".bindings.json");
                TemplateIntegrity.Verify(package, manifest.Templates[name].PackageSha256);
                TemplateIntegrity.Verify(contractBytes, manifest.Templates[name].ContractSha256);
                using var contractStream = new MemoryStream(contractBytes);
                var contract = TemplateContract.Read(contractStream);
                if (contract.TemplateVersion != manifest.Version)
                    throw new InvalidDataException("Embedded UI contract version mismatch.");
                using var packageStream = new MemoryStream(package);
                using var zip = new ZipArchive(packageStream, ZipArchiveMode.Read);
                using var recordStream = zip.GetEntry("R-Main.record")!.Open();
                using var record = JsonDocument.Parse(recordStream);
                foreach (string field in new[] { "ownerId", "ownerName", "path", "migrationMetadata" })
                    if (record.RootElement.TryGetProperty(field, out var value) && value.ValueKind != JsonValueKind.Null)
                        throw new InvalidDataException("The bundled package retains private account metadata.");
                foreach (var asset in zip.Entries.Where(entry => entry.FullName.StartsWith("Assets/", StringComparison.Ordinal)))
                {
                    using var stream = asset.Open();
                    using var bytes = new MemoryStream();
                    stream.CopyTo(bytes);
                    TemplateIntegrity.Verify(bytes.ToArray(), asset.FullName["Assets/".Length..]);
                }
                Console.WriteLine($"PASS embedded {name}: version {contract.TemplateVersion}, {contract.Bindings.Count} bindings, asset hashes and privacy checks.");
            }
            Console.WriteLine("Embedded bundle checks only; native UI execution still requires an in-game test.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL embedded UI bundle: " + error.Message);
            return 1;
        }
    }
}
