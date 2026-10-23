using System.Collections.Generic;
using System.Linq;

namespace UnityPackageImporter.Models;

internal enum MeshReferenceOutcome { Resolved, MissingReference, MissingModel }

internal static class MeshReferenceLookup
{
    internal static bool IsExactStaticRenderer(System.Type candidate, System.Type staticRenderer) => candidate == staticRenderer;

    internal static T ResolveUnique<T>(long requestedFileId, IEnumerable<(long FileId, T Value)> candidates) where T : class
        => AvatarConversionScope.Unique(candidates.Where(candidate => candidate.FileId == requestedFileId)
            .Select(candidate => candidate.Value));

    internal static MeshReferenceOutcome Evaluate(bool modelFound, long requestedFileId,
        IEnumerable<long> candidateFileIds) => !modelFound ? MeshReferenceOutcome.MissingModel :
        candidateFileIds.Contains(requestedFileId) ? MeshReferenceOutcome.Resolved : MeshReferenceOutcome.MissingReference;
}
