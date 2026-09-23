using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using UnityPackageImporter.Extractor;

internal static class PackageAudit
{
    public static int Run(string inputDirectory, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var results = new List<object>();
        int failures = 0;
        foreach (var package in Directory.GetFiles(inputDirectory, "*.unitypackage").OrderBy(p => new FileInfo(p).Length))
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var stream = File.OpenRead(package);
                var hash = Convert.ToHexString(SHA256.HashData(stream));
                var files = UnityPackageExtractor.Unpack(package, Path.Combine(outputDirectory, hash));
                var prefabs = files.Where(p => Path.GetExtension(p).Equals(".prefab", StringComparison.OrdinalIgnoreCase)).ToArray();
                int negativeAnchors = 0, nestedPrefabReferences = 0;
                foreach (var prefab in prefabs)
                {
                    foreach (var line in File.ReadLines(prefab))
                    {
                        if (Regex.IsMatch(line, @"^--- !u!\d+ &-\d+")) negativeAnchors++;
                        if (line.TrimStart().StartsWith("m_SourcePrefab:")) nestedPrefabReferences++;
                    }
                }
                var result = new
                {
                    package = Path.GetFileName(package), success = true, hash,
                    extractedFiles = files.Count, seconds = Math.Round(timer.Elapsed.TotalSeconds, 2),
                    extensions = files.GroupBy(p => Path.GetExtension(p).ToLowerInvariant()).ToDictionary(g => g.Key, g => g.Count()),
                    negativeDocumentIds = negativeAnchors,
                    prefabSourceReferences = nestedPrefabReferences,
                    extractionDirectory = Path.Combine(outputDirectory, hash)
                };
                results.Add(result);
                Console.WriteLine(JsonSerializer.Serialize(result));
            }
            catch (Exception error)
            {
                failures++;
                var result = new { package = Path.GetFileName(package), success = false, error = error.ToString() };
                results.Add(result);
                Console.WriteLine(JsonSerializer.Serialize(result));
            }
        }
        File.WriteAllText(Path.Combine(outputDirectory, "package-audit.json"), JsonSerializer.Serialize(results,
            new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }
}
