using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityPackageImporter.Models;

/// <summary>
/// Produces an immutable, reviewable Merge Armature plan. Matching follows
/// Modular Avatar's direct-child, prefix/suffix-aware zip merge behavior.
/// It does not mutate either hierarchy.
/// </summary>
public static class ModularAvatarMergePlanner
{
    // MA AvatarObjectReference stores this marker for the avatar root; ordinary
    // referencePath values are exact paths relative to that root.
    public static T ResolveTarget<T>(T avatarRoot, IEnumerable<T> candidates,
        string referencePath, string sourceName, string prefix, string suffix,
        Func<T, string> name, Func<T, string> relativePath) where T : class
    {
        if (referencePath == "$$$AVATAR_ROOT$$$") return avatarRoot;
        TryRemoveAffixes(sourceName, prefix, suffix, out string inferredName);
        return BoneAttachmentTarget.Resolve(candidates, referencePath, inferredName, name, relativePath);
    }

    public static BoneAttachmentPlan CreatePlan(
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

        return new BoneAttachmentPlan
        {
            Mappings = mappings.AsReadOnly(),
            RetainedRoots = retained.AsReadOnly(),
            Conflicts = conflicts.AsReadOnly()
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
                    candidates = target.Children
                        .Where(candidate => candidate.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

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
