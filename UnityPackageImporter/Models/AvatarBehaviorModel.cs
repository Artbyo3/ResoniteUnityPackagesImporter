#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityPackageImporter.Models;

internal enum AvatarBreathingTargetMode
{
    BlendShape,
    BoneScale
}

internal sealed record AvatarBreathingBinding(
    long FeatureSourceId,
    AvatarBreathingTargetMode Mode,
    long TargetRendererFileId,
    string TargetBlendShape,
    float MinWeight,
    float MaxWeight,
    long TargetObjectFileId,
    float PeriodSeconds = 3.5f
);

internal sealed record AvatarBlinkingBinding(
    long FeatureSourceId,
    long TargetRendererFileId,
    string TargetBlendShape,
    float BlinkWeight = 100f
);

internal sealed record AvatarTalkingBinding(
    long FeatureSourceId,
    long TargetRendererFileId,
    string TargetBlendShape,
    float TalkingWeight = 100f,
    long TargetObjectFileId = 0,
    bool TurnOn = true
);

internal sealed record AvatarFuryVisemeBinding(
    int Channel,
    long TargetRendererFileId,
    string TargetBlendShape
);

internal sealed record AvatarBehaviorPlan
{
    public IReadOnlyList<AvatarBreathingBinding> BreathingBindings { get; init; } = Array.Empty<AvatarBreathingBinding>();
    public IReadOnlyList<AvatarBlinkingBinding> BlinkingBindings { get; init; } = Array.Empty<AvatarBlinkingBinding>();
    public IReadOnlyList<AvatarTalkingBinding> TalkingBindings { get; init; } = Array.Empty<AvatarTalkingBinding>();
    public IReadOnlyList<AvatarFuryVisemeBinding> VisemeBindings { get; init; } = Array.Empty<AvatarFuryVisemeBinding>();
    public bool BlinkSuppressed { get; init; }
    public bool HasBlockBlinking { get; init; }
    public bool HasBlockVisemes { get; init; }
    public IReadOnlyList<InitialAppearanceDiagnostic> Diagnostics { get; init; } = Array.Empty<InitialAppearanceDiagnostic>();
    public bool HasUnsupportedContext { get; init; }
    public bool CanBuild => !HasUnsupportedContext && (Diagnostics.Count == 0 ||
        Diagnostics.All(d => d.Severity != InitialAppearanceSeverity.Error));
}
