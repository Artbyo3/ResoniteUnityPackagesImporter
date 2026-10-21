using System.Collections.Generic;
using System.Linq;

namespace UnityPackageImporter.Models;

internal enum MeshReferenceOutcome { Resolved, MissingReference, MissingModel }

internal static class MeshReferenceLookup
{
    internal static MeshReferenceOutcome Evaluate(bool modelFound, long requestedFileId,
        IEnumerable<long> candidateFileIds) => !modelFound ? MeshReferenceOutcome.MissingModel :
        candidateFileIds.Contains(requestedFileId) ? MeshReferenceOutcome.Resolved : MeshReferenceOutcome.MissingReference;
}
