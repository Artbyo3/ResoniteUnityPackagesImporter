using UnityPackageImporter.Models;

internal static class MeshReferenceLookupTests
{
    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        test("Present model with absent signed mesh ID produces a missing reference", () =>
        {
            check(MeshReferenceLookup.Evaluate(true, -7001, new long[] { 12, -17 }) == MeshReferenceOutcome.MissingReference,
                "An absent subasset in an imported model should be a warning.");
            check(MeshReferenceLookup.Evaluate(true, -7001, Array.Empty<long>()) == MeshReferenceOutcome.MissingReference,
                "An imported model with no candidate meshes should retain the missing-reference outcome.");
        });
        test("Present mesh resolves only by exact signed file ID", () =>
        {
            check(MeshReferenceLookup.Evaluate(true, -17, new long[] { 17, -17, 12 }) == MeshReferenceOutcome.Resolved,
                "An exact signed ID should resolve as before.");
            check(MeshReferenceLookup.Evaluate(true, -17, new long[] { 17 }) == MeshReferenceOutcome.MissingReference,
                "The opposite sign must not select a different mesh.");
        });
        test("Absent model remains a failure even with a matching candidate ID", () =>
        {
            check(MeshReferenceLookup.Evaluate(false, -17, new long[] { -17 }) == MeshReferenceOutcome.MissingModel,
                "A missing model must never be classified as an optional missing subasset.");
        });
        test("Only missing mesh references retain structure and output actions", () =>
        {
            var result = ImportResult.Evaluate(true, false, NativeAvatarReadiness.Ready, 0, 0, 0,
                missingMeshReferences: 2);
            check(result.StructureReady && result.CanPerform(ImportOutputAction.CreateAvatar) &&
                result.CanPerform(ImportOutputAction.KeepObject), "Missing meshes must not block valid output.");
            check(result.IsPartial && result.MissingMeshReferences == 2 &&
                result.Issues.SequenceEqual(new[] { ImportResultIssue.MissingMeshes }),
                "The warning must be counted and surface as its own partial-compatibility issue.");
        });
        test("Missing mesh warnings do not hide real reconstruction failures", () =>
        {
            var result = ImportResult.Evaluate(true, false, NativeAvatarReadiness.Ready, 0, 0, 1,
                missingMeshReferences: 1);
            check(!result.StructureReady && !result.CanPerform(ImportOutputAction.CreateAvatar) &&
                !result.CanPerform(ImportOutputAction.KeepObject), "A real failure must still block both actions.");
            check(result.Issues.Contains(ImportResultIssue.MissingMeshes) &&
                result.Issues.Contains(ImportResultIssue.ReconstructionFailed), "Both reasons must be retained.");
        });
        test("Installed copies retain missing mesh warning counts", () =>
        {
            var result = ImportResult.Evaluate(true, true, NativeAvatarReadiness.NotApplicable, 0, 0, 0,
                missingMeshReferences: 1).ForInstalledCopy();
            check(result.StructureReady && result.IsPartial && result.MissingMeshReferences == 1 &&
                result.Issues.Contains(ImportResultIssue.MissingMeshes), "Installed receipts must retain the warning.");
        });
    }
}
