using System;
using System.Collections.Generic;

namespace UnityPackageImporter.Models;

public sealed class MergeHierarchyNode
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public bool StartsNestedMerge { get; init; }
    public IReadOnlyList<MergeHierarchyNode> Children { get; init; } = Array.Empty<MergeHierarchyNode>();
}

public sealed class MergeBoneMapping
{
    public string SourceId { get; init; } = "";
    public string SourcePath { get; init; } = "";
    public string TargetId { get; init; } = "";
    public string TargetPath { get; init; } = "";
}

public sealed class RetainedBoneRoot
{
    public string SourceId { get; init; } = "";
    public string SourcePath { get; init; } = "";
    public string TargetParentId { get; init; } = "";
    public string TargetParentPath { get; init; } = "";
    public string Reason { get; init; } = "";
}

public sealed class MergePlanConflict
{
    public string SourcePath { get; init; } = "";
    public string Message { get; init; } = "";
}

/// <summary>Framework-neutral attachment operations. Adapters preserve their own matching semantics.</summary>
public sealed class BoneAttachmentPlan
{
    public IReadOnlyList<MergeBoneMapping> Mappings { get; init; } = Array.Empty<MergeBoneMapping>();
    public IReadOnlyList<RetainedBoneRoot> RetainedRoots { get; init; } = Array.Empty<RetainedBoneRoot>();
    public IReadOnlyList<MergePlanConflict> Conflicts { get; init; } = Array.Empty<MergePlanConflict>();
    public bool CanApply => Conflicts.Count == 0;
}
