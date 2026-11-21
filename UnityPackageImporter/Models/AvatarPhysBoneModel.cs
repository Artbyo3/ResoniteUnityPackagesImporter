#nullable enable
using System;
using System.Collections.Generic;

namespace UnityPackageImporter.Models;

public sealed record AvatarPhysBoneColliderDefinition
{
    public long ComponentId { get; init; }
    public long GameObjectId { get; init; }
    public long RootTransformId { get; init; }
    public int ShapeType { get; init; }
    public float Radius { get; init; }
    public float Height { get; init; }
    public UnityPosition Position { get; init; }
    public (float X, float Y, float Z, float W) Rotation { get; init; }
    public bool InsideBounds { get; init; }
}

public sealed record AvatarPhysBoneDefinition
{
    public long ComponentId { get; init; }
    public long GameObjectId { get; init; }
    public long RootTransformId { get; init; }
    public IReadOnlyList<long> IgnoreTransformIds { get; init; } = Array.Empty<long>();
    public UnityPosition EndpointPosition { get; init; }
    public int MultiChildType { get; init; }
    public float Pull { get; init; } = 0.2f;
    public float Spring { get; init; } = 0.2f;
    public float Stiffness { get; init; } = 0.2f;
    public float Gravity { get; init; }
    public float GravityFalloff { get; init; }
    public float Immobile { get; init; }
    public float Radius { get; init; }
    public bool AllowCollision { get; init; } = true;
    public IReadOnlyList<long> ColliderIds { get; init; } = Array.Empty<long>();
    public bool AllowGrabbing { get; init; } = true;
    public bool AllowPosing { get; init; } = true;
    public float MaxStretch { get; init; }
}

public sealed class AvatarPhysBonePlan
{
    public IReadOnlyList<AvatarPhysBoneDefinition> Bones { get; init; } = Array.Empty<AvatarPhysBoneDefinition>();
    public IReadOnlyList<AvatarPhysBoneColliderDefinition> Colliders { get; init; } = Array.Empty<AvatarPhysBoneColliderDefinition>();
    public IReadOnlyList<InitialAppearanceDiagnostic> Diagnostics { get; init; } = Array.Empty<InitialAppearanceDiagnostic>();
    public bool HasUnsupportedContext { get; init; }
    public bool CanBuild => !HasUnsupportedContext && (Bones.Count > 0 || Colliders.Count > 0);
}
