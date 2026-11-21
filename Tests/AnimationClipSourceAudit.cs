using System.Globalization;
using UnityPackageImporter.Models;

internal static class AnimationClipSourceAudit
{
    internal static int Run(long identity, IEnumerable<string> paths)
    {
        bool invalid = false;
        foreach (string path in paths)
        {
            if (new FileInfo(path).Length > InitialAppearanceReplay.MaximumBytes) throw new InvalidDataException("Clip input exceeds the audit byte budget.");
            string yaml = File.ReadAllText(path);
            var result = UnityStaticAnimationClip.Parse(yaml, identity);
            Console.WriteLine("LOCAL-ONLY clip: " + Path.GetFileName(path) + "; fileID: " + identity.ToString(CultureInfo.InvariantCulture) + "; SHA256: " + InitialAppearanceReplay.Hash(yaml));
            Console.WriteLine("Static snapshot applicable: " + result.CanApply + "; requires transform playback: " + result.RequiresTransformPlayback + "; snapshot curves: " + result.Curves.Count);
            Console.WriteLine("Deferred Transform target paths: " + result.TransformPaths.Count + "; owned-target validation still requires the captured avatar hierarchy.");
            foreach (var diagnostic in result.Diagnostics) Console.WriteLine(diagnostic.Severity + " " + diagnostic.Code + ": " + diagnostic.Message);
            invalid |= result.Diagnostics.Any(diagnostic => diagnostic.Severity == InitialAppearanceSeverity.Error);
        }
        Console.WriteLine("Source decoder audit only; runtime playback, native creation and appearance remain unverified.");
        return invalid ? 1 : 0;
    }
}
