using System;
using UnityPackageImporter.Conversion;
using UnityPackageImporter.Frameworks.ModularAvatar;
using UnityPackageImporter.Frameworks.VrcFury;
using UnityPackageImporter.Models;

namespace UnityPackageImporter.Importing;

/// <summary>Composition point: framework order is explicit and stable. Adapters emit; shared rules validate once all contributions exist.</summary>
internal static class AvatarPlanComposer
{
    internal static AvatarBuildPlan Create(AvatarPackageManifest manifest, string avatarName)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var plan = new AvatarBuildPlan
        {
            AvatarName = string.IsNullOrWhiteSpace(avatarName) ? PathSafeName(manifest.SourcePath) : avatarName
        };
        var sink = new AvatarPlanSink(plan);
        ModularAvatarPlanAdapter.AppendOperations(manifest.ModularAvatarComponents, sink);
        VrcFuryPlanAdapter.AppendOperations(manifest.VrcFuryComponents, sink);
        AvatarPlanValidator.Validate(plan);
        return plan;
    }

    private static string PathSafeName(string path)
    {
        if (string.IsNullOrEmpty(path)) return "Avatar";
        int lastSlash = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        return lastSlash >= 0 ? path.Substring(lastSlash + 1) : path;
    }
}
