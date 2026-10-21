#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityPackageImporter.Models;

internal enum ImportOutputAction { KeepObject, CreateAvatar, InstallOutfit }
internal enum ImportOwnership { Owned, Other, Unknown }
internal enum NativeAvatarReadiness { NotApplicable, Ready, MissingRig, MultipleRigs, MissingIk, MissingViewpoint, AlreadyCreated }

internal static class ImportResultScope
{
    internal static ImportOwnership Operation(AvatarBuildOperation operation, string sourcePath,
        Func<long, ImportOwnership> resolve)
    {
        // IDs from a prefab file cannot be interpreted as scene IDs.
        if (!string.Equals(operation.Source.SourcePath, sourcePath, StringComparison.OrdinalIgnoreCase))
            return ImportOwnership.Unknown;
        var ids = new List<long> { operation.Source.GameObjectFileId };
        bool implicitTarget = false;
        switch (operation)
        {
            case ArmatureMergeOperation merge:
                ids.Add(merge.SourceArmatureRootFileId);
                if (merge.TargetObjectFileId is long target && target != 0) ids.Add(target);
                else implicitTarget = true;
                break;
            case BoneAttachmentOperation attach:
                ids.Add(attach.SourceObjectFileId);
                if (attach.TargetObjectFileId is long attachmentTarget && attachmentTarget != 0) ids.Add(attachmentTarget);
                else implicitTarget = true;
                break;
            default: return ImportOwnership.Unknown;
        }
        var scopes = ids.Where(id => id != 0).Distinct().Select(resolve).ToArray();
        if (scopes.Contains(ImportOwnership.Owned)) return ImportOwnership.Owned;
        // A scene-wide bone/path lookup can affect another avatar. Until the
        // executor retains that resolved identity, do not claim isolation.
        if (scopes.Length == 0 || implicitTarget || scopes.Contains(ImportOwnership.Unknown)) return ImportOwnership.Unknown;
        return ImportOwnership.Other;
    }
}

// Machine-readable reasons are separate from presentation, package names and exceptions.
internal enum ImportResultIssue
{
    SourceUnavailable,
    ReconstructionFailed,
    OwnershipUnresolved,
    ConversionFailed,
    ConversionNotCompleted,
    AwaitingInstallation,
    UnsupportedFeatures,
    OptionalControlsFailed,
    MissingMaterials,
    MaterialEffectsLimited,
    MissingRig,
    MultipleRigs,
    MissingIk,
    MissingViewpoint,
    AlreadyCreated,
    MissingMeshes,
    UnsupportedNestedPrefabs
}

// Immutable snapshot; never cache it across awaits, material recovery or native creation.
internal sealed class ImportResult
{
    internal IReadOnlyList<ImportResultIssue> Issues { get; }
    internal bool StructureReady { get; }
    internal bool RequiresInstallation { get; }
    internal NativeAvatarReadiness NativeReadiness { get; }
    internal int MissingMaterialAssets { get; }
    internal int MissingMeshReferences { get; }
    internal int UnsupportedNestedPrefabCount { get; }
    internal int UnsupportedFeatureCount { get; }
    internal int FailedObjectCount { get; }
    internal int OperationCount { get; }
    internal int CompletedOperationCount { get; }
    internal IReadOnlyList<string> MaterialWarnings { get; }
    internal int MaterialWarningCount => MaterialWarnings.Count;
    internal bool IsPartial => Issues.Count > 0;

    private ImportResult(List<ImportResultIssue> issues, bool structureReady, bool requiresInstallation,
        NativeAvatarReadiness native, int missingMaterials, int unsupported, int failures, int operations, int completed,
        IReadOnlyList<string>? materialWarnings = null, int missingMeshReferences = 0, int unsupportedNestedPrefabs = 0)
    {
        Issues = Array.AsReadOnly(issues.Distinct().ToArray());
        StructureReady = structureReady;
        RequiresInstallation = requiresInstallation;
        NativeReadiness = native;
        MissingMaterialAssets = missingMaterials;
        MissingMeshReferences = missingMeshReferences;
        UnsupportedNestedPrefabCount = unsupportedNestedPrefabs;
        UnsupportedFeatureCount = unsupported;
        FailedObjectCount = failures;
        OperationCount = operations;
        CompletedOperationCount = completed;
        MaterialWarnings = materialWarnings ?? Array.Empty<string>();
    }

    internal bool CanPerform(ImportOutputAction action) => StructureReady && (action switch
    {
        ImportOutputAction.CreateAvatar => !RequiresInstallation && NativeReadiness == NativeAvatarReadiness.Ready,
        ImportOutputAction.InstallOutfit => RequiresInstallation,
        ImportOutputAction.KeepObject => !RequiresInstallation,
        _ => false
    });

    internal void Require(ImportOutputAction action)
    {
        if (!CanPerform(action))
            throw new InvalidOperationException("This import is not ready for the requested action. Review its import status before creating or installing it.");
    }

    // Only the installer calls this, after its duplicate passes validation and
    // its owned transaction commits. The reusable source snapshot is unchanged.
    internal ImportResult ForInstalledCopy(bool optionalControlsFailed = false)
    {
        Require(ImportOutputAction.InstallOutfit);
        var issues = Issues.Where(i => i != ImportResultIssue.AwaitingInstallation).ToList();
        if (optionalControlsFailed) issues.Add(ImportResultIssue.OptionalControlsFailed);
        return new ImportResult(issues,
            StructureReady, false, NativeAvatarReadiness.NotApplicable, MissingMaterialAssets,
            UnsupportedFeatureCount, FailedObjectCount, OperationCount, OperationCount,
            MaterialWarnings, MissingMeshReferences, UnsupportedNestedPrefabCount);
    }

    internal static ImportResult Evaluate(bool sourceAvailable, bool requiresInstallation,
        NativeAvatarReadiness native, int missingMaterialAssets, int unsupportedFeatures,
        int failedObjects, AvatarBuildPlan? plan = null,
        Func<AvatarBuildOperation, ImportOwnership>? ownership = null, bool unresolvedReconstruction = false,
        bool optionalControlsFailed = false, IEnumerable<string>? materialWarnings = null, int missingMeshReferences = 0,
        int unsupportedNestedPrefabs = 0, Func<AvatarBuildOperation, bool>? deferredNestedPrefab = null)
    {
        var issues = new List<ImportResultIssue>();
        bool ready = sourceAvailable && failedObjects == 0 && !unresolvedReconstruction;
        if (!sourceAvailable) issues.Add(ImportResultIssue.SourceUnavailable);
        if (failedObjects > 0) issues.Add(ImportResultIssue.ReconstructionFailed);
        if (unresolvedReconstruction) issues.Add(ImportResultIssue.OwnershipUnresolved);
        int operations = 0, completed = 0;
        if (plan != null)
        {
            // Unattributed plan errors are conservatively source-wide. Operation
            // errors are scoped below; a failed sibling does not invalidate completed work.
            if (plan.Diagnostics.Any(d => d.Severity == AvatarOperationSeverity.Error))
            {
                ready = false;
                issues.Add(ImportResultIssue.ConversionFailed);
            }
            foreach (var operation in plan.Operations)
            {
                if (operation.ExecutionState == AvatarOperationExecutionState.DeferredNestedPrefab &&
                    deferredNestedPrefab?.Invoke(operation) == true) continue;
                var scope = ownership?.Invoke(operation) ?? ImportOwnership.Owned;
                if (scope == ImportOwnership.Other) continue;
                operations++;
                if (scope == ImportOwnership.Unknown)
                {
                    ready = false;
                    issues.Add(ImportResultIssue.OwnershipUnresolved);
                }
                if (!operation.IsValid || operation.ExecutionState is AvatarOperationExecutionState.Failed or AvatarOperationExecutionState.Cancelled)
                {
                    ready = false;
                    issues.Add(ImportResultIssue.ConversionFailed);
                }
                else if (operation.ExecutionState == AvatarOperationExecutionState.Succeeded) completed++;
                else if (requiresInstallation && operation.ExecutionState == AvatarOperationExecutionState.AwaitingInstallation)
                    issues.Add(ImportResultIssue.AwaitingInstallation);
                else
                {
                    ready = false;
                    issues.Add(ImportResultIssue.ConversionNotCompleted);
                }
            }
        }
        if (missingMaterialAssets > 0) issues.Add(ImportResultIssue.MissingMaterials);
        if (missingMeshReferences > 0) issues.Add(ImportResultIssue.MissingMeshes);
        if (unsupportedNestedPrefabs > 0) issues.Add(ImportResultIssue.UnsupportedNestedPrefabs);
        if (unsupportedFeatures > 0) issues.Add(ImportResultIssue.UnsupportedFeatures);
        if (optionalControlsFailed) issues.Add(ImportResultIssue.OptionalControlsFailed);
        // This snapshot feeds world-visible UI and receipts. Protect the output
        // boundary even if a future warning producer bypasses the catalog.
        var warnings = materialWarnings?.Where(w => !string.IsNullOrWhiteSpace(w))
            .Select(ImportFailureReport.Sanitize).Distinct().ToArray() ?? Array.Empty<string>();
        if (warnings.Length > 0) issues.Add(ImportResultIssue.MaterialEffectsLimited);
        if (native is not NativeAvatarReadiness.Ready and not NativeAvatarReadiness.NotApplicable)
            issues.Add(native switch
            {
                NativeAvatarReadiness.MultipleRigs => ImportResultIssue.MultipleRigs,
                NativeAvatarReadiness.MissingIk => ImportResultIssue.MissingIk,
                NativeAvatarReadiness.MissingViewpoint => ImportResultIssue.MissingViewpoint,
                NativeAvatarReadiness.AlreadyCreated => ImportResultIssue.AlreadyCreated,
                _ => ImportResultIssue.MissingRig
            });
        return new ImportResult(issues, ready, requiresInstallation, native, missingMaterialAssets,
            unsupportedFeatures, failedObjects, operations, completed, Array.AsReadOnly(warnings), missingMeshReferences, unsupportedNestedPrefabs);
    }
}
