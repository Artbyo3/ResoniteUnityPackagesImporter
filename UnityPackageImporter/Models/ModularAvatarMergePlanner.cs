using System;
using System.Collections.Generic;
using System.Linq;

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

public sealed class ModularAvatarMergePlan
{
    public IReadOnlyList<MergeBoneMapping> Mappings { get; init; } = Array.Empty<MergeBoneMapping>();
    public IReadOnlyList<RetainedBoneRoot> RetainedRoots { get; init; } = Array.Empty<RetainedBoneRoot>();
    public IReadOnlyList<MergePlanConflict> Conflicts { get; init; } = Array.Empty<MergePlanConflict>();
    public bool CanApply => Conflicts.Count == 0;
}

/// <summary>
/// Produces an immutable, reviewable Merge Armature plan. Matching follows
/// Modular Avatar's direct-child, prefix/suffix-aware zip merge behavior.
/// It does not mutate either hierarchy.
/// </summary>
public static class ModularAvatarMergePlanner
{
    public static ModularAvatarMergePlan CreatePlan(
        MergeHierarchyNode sourceRoot,
        MergeHierarchyNode targetRoot,
        string prefix = "",
        string suffix = "")
    {
        if (sourceRoot == null) throw new ArgumentNullException(nameof(sourceRoot));
        if (targetRoot == null) throw new ArgumentNullException(nameof(targetRoot));

        prefix ??= "";
        suffix ??= "";

        var mappings = new List<MergeBoneMapping>();
        var retained = new List<RetainedBoneRoot>();
        var conflicts = new List<MergePlanConflict>();

        AddMapping(sourceRoot, sourceRoot.Name, targetRoot, targetRoot.Name);
        Scan(sourceRoot, sourceRoot.Name, targetRoot, targetRoot.Name);

        return new ModularAvatarMergePlan
        {
            Mappings = mappings,
            RetainedRoots = retained,
            Conflicts = conflicts
        };

        void Scan(MergeHierarchyNode source, string sourcePath, MergeHierarchyNode target, string targetPath)
        {
            foreach (var child in source.Children)
            {
                string childSourcePath = Join(sourcePath, child.Name);
                if (child.StartsNestedMerge)
                {
                    retained.Add(new RetainedBoneRoot
                    {
                        SourceId = child.Id,
                        SourcePath = childSourcePath,
                        TargetParentId = target.Id,
                        TargetParentPath = targetPath,
                        Reason = "Nested Merge Armature is planned separately"
                    });
                    continue;
                }

                if (!TryRemoveAffixes(child.Name, prefix, suffix, out string targetName))
                {
                    Retain(child, childSourcePath, target, targetPath, "Bone name does not match the configured prefix and suffix");
                    continue;
                }

                var candidates = target.Children
                    .Where(candidate => candidate.Name.Equals(targetName, StringComparison.Ordinal))
                    .ToList();

                if (candidates.Count == 0)
                {
                    Retain(child, childSourcePath, target, targetPath, "No direct target bone with the mapped name");
                    continue;
                }

                if (candidates.Count > 1)
                {
                    conflicts.Add(new MergePlanConflict
                    {
                        SourcePath = childSourcePath,
                        Message = "Target path '" + targetPath + "' has multiple direct children named '" + targetName + "'"
                    });
                    continue;
                }

                var targetChild = candidates[0];
                string childTargetPath = Join(targetPath, targetChild.Name);
                AddMapping(child, childSourcePath, targetChild, childTargetPath);
                Scan(child, childSourcePath, targetChild, childTargetPath);
            }
        }

        void AddMapping(MergeHierarchyNode source, string sourcePath, MergeHierarchyNode target, string targetPath)
        {
            mappings.Add(new MergeBoneMapping
            {
                SourceId = source.Id,
                SourcePath = sourcePath,
                TargetId = target.Id,
                TargetPath = targetPath
            });
        }

        void Retain(MergeHierarchyNode source, string sourcePath, MergeHierarchyNode targetParent, string targetParentPath, string reason)
        {
            retained.Add(new RetainedBoneRoot
            {
                SourceId = source.Id,
                SourcePath = sourcePath,
                TargetParentId = targetParent.Id,
                TargetParentPath = targetParentPath,
                Reason = reason
            });
        }
    }

    public static bool TryRemoveAffixes(string name, string prefix, string suffix, out string mappedName)
    {
        mappedName = "";
        if (name == null) return false;
        prefix ??= "";
        suffix ??= "";
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal)) return false;
        if (name.Length <= prefix.Length + suffix.Length) return false;
        mappedName = name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length);
        return true;
    }

    private static string Join(string parent, string child) =>
        string.IsNullOrEmpty(parent) ? child : parent + "/" + child;
}
