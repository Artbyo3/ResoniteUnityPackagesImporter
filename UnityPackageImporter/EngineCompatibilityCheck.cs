using System;
using System.Collections.Generic;
using System.Reflection;

namespace UnityPackageImporter;

// Engine-independent diagnostics; the bridge supplies the actual reflected members.
internal static class EngineCompatibilityCheck
{
    internal const string LastVerifiedResoniteVersion = "2026.9.18.82";

    internal static string MissingMemberMessage(string name) =>
        $"Required engine member {name} is unavailable. The feature will not work on this Resonite version.";

    internal static void Run(string runningVersion, IEnumerable<(string Name, MemberInfo Member)> members,
        Action<string> info, Action<string> warn, Action<string> error)
    {
        if (string.Equals(runningVersion, LastVerifiedResoniteVersion, StringComparison.Ordinal))
            info($"Resonite compatibility: running version {runningVersion} matches last verified version {LastVerifiedResoniteVersion}.");
        else
            warn($"Resonite compatibility: running version {runningVersion}; last verified version {LastVerifiedResoniteVersion}. Import hooks may break after engine updates.");

        foreach (var (name, member) in members)
            if (member == null)
                error(MissingMemberMessage(name));
    }
}
