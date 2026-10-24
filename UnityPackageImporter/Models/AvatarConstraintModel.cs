#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityPackageImporter.Models;

public enum AvatarConstraintType
{
    Parent,
    Position,
    Rotation,
    LookAt,
    Aim,
    Scale
}

public sealed record AvatarConstraintSource(long SourceTransformId, float Weight);

public sealed record AvatarConstraintDefinition
{
    public long ComponentId { get; init; }
    public long GameObjectId { get; init; }
    public long TargetTransformId { get; init; }
    public AvatarConstraintType Type { get; init; }
    public bool IsActive { get; init; } = true;
    public float Weight { get; init; } = 1.0f;
    public IReadOnlyList<AvatarConstraintSource> Sources { get; init; } = Array.Empty<AvatarConstraintSource>();
    public UnityPosition PositionOffset { get; init; }
    public (float X, float Y, float Z, float W) RotationOffset { get; init; } = (0, 0, 0, 1);
    public (bool X, bool Y, bool Z) AffectTranslation { get; init; } = (true, true, true);
    public (bool X, bool Y, bool Z) AffectRotation { get; init; } = (true, true, true);
}

public sealed class AvatarConstraintPlan
{
    public IReadOnlyList<AvatarConstraintDefinition> Constraints { get; init; } = Array.Empty<AvatarConstraintDefinition>();
    public IReadOnlyList<InitialAppearanceDiagnostic> Diagnostics { get; init; } = Array.Empty<InitialAppearanceDiagnostic>();
    public bool HasUnsupportedContext { get; init; }
    public bool CanBuild => !HasUnsupportedContext && Diagnostics.All(d => d.Severity != InitialAppearanceSeverity.Error);
}
