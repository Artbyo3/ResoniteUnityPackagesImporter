#nullable enable
using System;
using System.Collections.Generic;
using UnityPackageImporter.Extractor;

namespace UnityPackageImporter.Models;

internal enum ImportReportStage { Starting, Prompt, Extraction, Indexing, Models, Materials, Reconstruction, Preparation, CreateAvatar, KeepObject, InstallOutfit, MaterialCustomization, Validation, Unknown }
internal enum ReportWarningCategory { Reconstruction, Conversion, Materials, MissingReferences, UnsupportedFeatures, Reporting }
internal enum ReportDiagnosticCode { ObjectReconstructionFailed, SourceReconstructionFailed, ConversionDiagnostic, MissingMaterials, MissingMeshes, UnsupportedNestedPrefabs, MissingNestedPrefabs, UnsupportedFeatures, MaterialEffectsLimited, OptionalControlsFailed, SnapshotUnavailable }
internal enum ReportFileStatus { Available, Missing, AccessDenied, ReadFailed, Cancelled, Changed }
internal enum ReportOsFamily { Windows, Linux, MacOS, Other }
internal enum ReportObjectType { GameObject, Transform, PrefabInstance, SkinnedMeshRenderer, MeshRenderer, MeshFilter, MonoBehaviour, MeshCollider, RotationConstraint, Other }

internal sealed record ReportEnvironment(string ModVersion, string ResoniteVersion, string VerifiedVersion, ReportOsFamily Os, string Locale);
internal sealed record ReportException(string Type, PackageInputFailure? InputFailure, IReadOnlyList<string> Methods, ReportFailureReason Reason = ReportFailureReason.Unknown);
internal sealed record ReportObjectFailure(ReportObjectType Type, int? UnityClass, long Id, string? Name = null, ImportOwnership Ownership = ImportOwnership.Unknown, IReadOnlyList<ReportException>? Exceptions = null, string? SourceKey = null);
internal sealed record ReportOperation(AvatarBuildOperationKind Kind, AvatarOperationExecutionState State, ImportOwnership Ownership, long ComponentId, long GameObjectId, IReadOnlyList<string> Codes, string? Name = null, bool DeferredByPolicy = false, string? SourceKey = null);
internal sealed record ReportWarning(ReportWarningCategory Category, ReportDiagnosticCode Code, long? ObjectId = null, string? Name = null, string? SourceKey = null);
internal sealed record ReportNativeReadiness(int? RigCount = null, bool? IkPresent = null, bool? LeftHand = null, bool? RightHand = null, bool? LeftEye = null, bool? RightEye = null, bool? Viewpoint = null, bool? IkInitialized = null, int? CompleteRigCount = null);
internal sealed record ReportPackage(
    ReportFileStatus Status, long? Bytes, PackageInputKind? Format,
    int? Entries, IReadOnlyDictionary<ReportAssetCategory, int>? Assets, bool ExtractionComplete, string? Name = null);
internal enum ReportAssetCategory { Prefab, Scene, Model, Material, Texture, Animation, Script, Other }
internal sealed record ReportResult(
    bool StructureReady, NativeAvatarReadiness NativeReadiness, IReadOnlyList<ImportResultIssue> Issues,
    int FailedObjects, int Operations, int CompletedOperations, int MissingMaterials, int MissingMeshes,
    int UnsupportedNestedPrefabs, int MissingNestedPrefabs, ReportNativeReadiness Native,
    IReadOnlyList<ReportObjectFailure> Objects, IReadOnlyList<ReportOperation> OperationDetails,
    int PlanDiagnosticCount = 0, IReadOnlyList<string>? PlanDiagnosticCodes = null, string? Name = null,
    int UnsupportedFeatures = 0, int MaterialWarnings = 0, bool RequiresInstallation = false);

internal sealed record ImportReportData(
    ReportEnvironment Environment, Guid SessionId, DateTimeOffset Time, ImportReportStage Stage,
    IReadOnlyList<ReportException> Exceptions, IReadOnlyList<ReportPackage> Packages, int PackageCount,
    ReportResult? Result, IReadOnlyDictionary<ReportWarningCategory, int> WarningCounts,
    IReadOnlyList<ReportWarning> Warnings, int SourceFailures, IReadOnlyList<ReportObjectFailure> SourceFailureDetails,
    bool IncludeNames = false, bool ExceptionsTruncated = false,
    IReadOnlyList<ReportModelFailure>? ModelFailures = null, int ModelFailureCount = 0, Guid ReportId = default);
