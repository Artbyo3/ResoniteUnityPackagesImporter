#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityPackageImporter.Models;

internal sealed record AvatarWardrobeBinding(InitialAppearanceWrite Off, InitialAppearanceWrite On);

internal sealed record AvatarWardrobeControl(string Id, string Path, bool DefaultOn,
    IReadOnlyList<AvatarWardrobeBinding> Bindings, string IconGuid = "", bool Saved = false, bool Synced = true,
    AvatarParameterType ParameterType = AvatarParameterType.Bool);

internal sealed class AvatarWardrobePlan
{
    internal IReadOnlyList<AvatarWardrobeControl> Controls { get; init; } = Array.Empty<AvatarWardrobeControl>();
    internal IReadOnlyList<InitialAppearanceDiagnostic> Diagnostics { get; init; } = Array.Empty<InitialAppearanceDiagnostic>();
    internal bool HasUnsupportedContext { get; init; }
    internal AvatarWardrobeGraph? SharedState { get; init; }
    internal bool CanBuild => Controls.Count > 0 && SharedState?.IndependentControls != null && !HasUnsupportedContext &&
        Diagnostics.All(diagnostic => diagnostic.Severity != InitialAppearanceSeverity.Error);
}
