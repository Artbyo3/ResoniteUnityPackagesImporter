# Scene composition and armature scale evidence

## Observed failure and gate distinction

On 2026-10-26, retained reports for the package containing the user-named S1N
scene show a ready native rig but an unready structure: unresolved ownership,
nested prefab instances and parent/target failures. A different package reports
a missing rig; that report must not be used to diagnose this scene.

Create Avatar requires both structure readiness and native readiness. A ready rig
does not authorize creation from an unresolved hierarchy. Unsupported optional
features remain disclosed; they are not permission to ignore ownership failures.
The engine log subsequently flushed and confirms the installed `D408B1B6`
checkpoint was loaded. It does not include the newer composition/scale repair.
Retained reports are not user acceptance evidence.

## Shared scene and prefab reconstruction

UnityStructureComposition now supplies UnityPrefabComposer with the same exact
model identities for prefab and scene imports. UnitySceneImportTask composes
before parsing objects or manifests, retaining missing-source alias provenance.
It does not repair hierarchy failures by selecting arbitrary parents or targets.

Scene conversion and initial appearance use the composed manifest/YAML, exact
avatar roots and pre-initial appearance renderer baselines. The Modular Avatar,
VRCFury, blendshape, wardrobe, animator, constraint, dynamics and behavior paths
receive scene evidence rather than depending on a prefab-only cast. Existing
ownership checks remain in force; there is no scene staging-root fallback.

The retained source fixture was checked offline against the actual composer:
577 composed YAML documents, one avatar descriptor, 138 VRCFury components and
two remaining prefab instances, both referencing exact included FBX models.
Its 23-operation conversion plan has no errors after the bounded scale change.
Unsupported feature warnings remain. This establishes source composition and
planning only, not native target resolution, appearance, deformation or creation.
Private source assets and the reproduction harness stay in ignored scratch data.

## Bounded VRCFury automatic scale semantics

Primary implementation evidence: VRCFury revision
`6bd33f4cb453df816c943a335e47f64e6a37e188`,
`com.vrcfury.vrcfury/Editor-Avatars/Service/ArmatureLinkService.cs`,
GetScalingFactor and the mapped-bone alignment loop.

The newly supported policy is version 7, recursive automatic scale alignment,
with power-of-ten quantization disabled. It computes one root multiplier:
`abs(sourceRoot.worldScale.x) / abs(targetRoot.worldScale.x)`.
Every mapped bone receives its target bone's world scale multiplied by that
same factor. Target nonuniform proportions and mirror signs are retained; this
is not a per-bone ratio or forced uniform scale. Position and rotation still
follow their independently authored alignment flags.

The executor rejects nonfinite/zero root inputs, nonrepresentable factors and
nonfinite/singular scaled target axes before moving any bones. The existing
skin-binding preservation check remains. Modular Avatar and other non-ratio
operations retain their prior transform path. Manual non-unit factors,
quantized automatic scaling, forced unit scale and active legacy scaling
remain explicitly unsupported. This is not complete ArmatureLink parity.

## Executable evidence and remaining acceptance

- Tests/UnityPrefabComposerTests.cs covers repeated scene composite instances,
  model-parent aliases, inherited descriptors, per-instance skin references and
  missing-source provenance beside included composites.
- Tests/ImportResultTests.cs retains the ready-rig/unready-structure gate.
- Tests/VrcFuryArmatureSemanticsTests.cs checks policy propagation, a shared
  nonquantized ratio, signed/nonuniform target scales and invalid numeric input.
- The Release build checks engine-facing adapter compilation, not execution.

An independent read-only review found no actionable defects in the shared scene
path and bounded scale changes. That review is not native execution evidence.

Native execution must still establish that the composed scene reconstructs with
resolved ownership, the exact authored rig remains usable, appearance defaults
are applied, and armature alignment preserves skin deformation. Whole-operation
rollback on other existing merge failures is not established by scale preflight.
Use the single consolidated checklist in AVATAR_CONVERSION_PROGRESS.md.
The newer bounded serialized skin path is documented in UNITY_SERIALIZED_MESHES.md;
native renderer deformation is still unverified. Other composer failures and the
native material mapper exception remain unresolved; do not promote coverage here.
