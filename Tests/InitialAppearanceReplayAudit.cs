using System.Text.Json;
using UnityPackageImporter.Models;

internal static class InitialAppearanceReplayAudit
{
    internal static int Run(string path)
    {
        if (new FileInfo(path).Length > InitialAppearanceReplay.MaximumBytes) throw new InvalidDataException("Replay file exceeds its byte budget.");
        var replay = InitialAppearanceReplay.Deserialize(File.ReadAllText(path));
        var result = replay.Evaluate();
        Console.WriteLine("LOCAL-ONLY replay; recorded loaded build: " + replay.LoadedBuildHash);
        Console.WriteLine("Session: " + replay.SessionId + "; avatar root: " + replay.AvatarRootId);
        Console.WriteLine("Graph objects: " + (replay.Objects?.Length.ToString() ?? "not captured") + "; renderers: " + replay.Renderers.Count + "; captured clip dependencies: " + replay.Clips.Length);
        Console.WriteLine("External animation/parameter context verified absent: " + replay.ExternalContextVerifiedAbsent);
        Console.WriteLine("Recorded result parity: " + replay.MatchesRecorded(result) + "; current CanApply: " + result.CanApply + "; proposed writes: " + result.Writes.Count);
        foreach (var group in result.Diagnostics.GroupBy(diagnostic => (diagnostic.Severity, diagnostic.Code)))
            Console.WriteLine(group.Key.Severity + " " + group.Key.Code + ": " + group.Count());
        foreach (var diagnostic in result.Diagnostics.Where(diagnostic => diagnostic.Severity == InitialAppearanceSeverity.Error))
            Console.WriteLine(JsonSerializer.Serialize(diagnostic));
        Console.WriteLine("Compiler replay only; native field writes, creation, playback and visual appearance are not verified.");
        return result.CanApply ? 0 : 1;
    }
}
