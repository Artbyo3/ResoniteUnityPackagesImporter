# Import diagnostics and evidence

## Disabled action investigation, 2026-10-26

The later engine log confirms the installed `C282CA97` build and the exact S1N
item: native Ready, zero failed objects, all 23 operations completed, but plan-level
appearance errors block creation. Stripped model aliases lack inline inherited
hierarchy/state; the appearance evaluator cannot establish their source graph.
The gate is not bypassed. See [action-gate evidence](IMPORT_ACTION_DIAGNOSTICS.md)
for the reproduced cause, immediate local files, error-first shareable diagnostics
and the source-proven graph repair still required.

## Observed live checkpoint: 2026-10-26

The retained session loaded DLL SHA-256
`1B19FE57F848CFA9EF2A76620C613123425F67A8EB5AA23F26B116E0D9635CF9`.
The local evidence snapshot is `scratch/live-reports-20261026-0308/`.
It contains one flushed engine log and 20 retained reports, not a complete
history of all imports. Do not publish the raw log or package contents.

- Of 146 skinned-renderer warnings about unavailable imported templates,
  145 occurrences resolve in the retained cache to included serialized Unity
  Mesh assets. There are 141 distinct source references across the warnings.
  One source was not found in that cache; this alone does not prove absence
  from the exact package index used by its import.
- At that loaded checkpoint serialized skin reconstruction was unsupported by
  the static-only generated-mesh path. A newer bounded local path is described
  in [serialized mesh evidence](UNITY_SERIALIZED_MESHES.md), not live-verified.
  These observations do not establish missing FBX files,
  nor validate every included asset's contents.
- Included composite-prefab references and unresolved stripped transforms
  remain distinct from genuinely absent nested sources. Do not substitute
  arbitrary parents, bones or geometry to suppress hierarchy errors.
- A native PBS material callback threw a NullReferenceException, and a model
  import later reported an interruption. Concurrent imports prevent attributing
  the callback fault to that model by time alone. A missing-texture cause is
  unproven. Three reports also originate in prefab composition; their precise
  source identities and override failures still require investigation.

## Later retained reports: 2026-10-26

The later local snapshot in `scratch/live-reports-20261026-0744/` contains 20
reports and the subsequently flushed engine log. The loader confirms DLL SHA-256
`D408B1B6189A7C38861821BB80DF6B33527B74E334D57B2640903AFD9B3F4262`.
This validates loaded-build identity, not successful conversion. A model report
now preserves the original
NullReferenceException from FrooxEngine.ModelImportSettings.PBS, classified as
NativeMaterialMappingFailed at the Materials phase. This is evidence that fault
capture provides more precise attribution, not a repair or proof of its cause.

The package containing the user-named S1N scene reports a ready rig but unresolved
structure ownership and prefab hierarchy failures. It must not be conflated
with the separate package's MissingRig reports. See
[scene composition evidence](SCENE_PREFAB_COMPOSITION.md) for the integrated
repair and remaining native verification. The newer bounded serialized skin
path has offline native asset checks; live skin reconstruction and other composer
failure groups remain unverified or unresolved respectively.

## Current observability implementation

ReportFailureDetails attaches fixed typed reasons to original exceptions without
changing their types or local messages. Hierarchy resolution, unsupported prefab
expansion, missing indexed model sources, unavailable templates and included
serialized skin sources have separate categories. Untagged exceptions retain the
existing type-based classification; raw error text is not used to guess causes.

ImportReportContext counts all source failures by safe object type and reason,
independently of the 32-detail sample limit. ImportFailureReport renders those
aggregates. ImportReportService logs a local report/import-token correlation
summary after saving. Session tokens and precise event times remain excluded
from shareable reports; original privacy regressions remain intact.

At snapshot capture, an additional local-only item summary records the selected
display name, report token, structure readiness, native readiness and issue
codes. Names are flattened to one line, unavailable snapshots remain unknown,
and no item name is added to exported report text. This should disambiguate
concurrent per-item gate failures once the engine log flushes; execution of the
new summary has not yet been observed.

FileImportTaskScene wraps the existing public MaterialMapper delegate with one
ModelImportFailureState per model. Normal results are returned unchanged. The
first non-cancellation callback error is retained and rethrown, with its original
type and stack, for subsequent interruption reporting at the Materials phase.
Unexplained interruption remains explicitly unexplained. No dummy material,
texture substitution, import retry or internal reflection is added.

The public ModelImportSettings.MaterialMapper field and delegate signature were
verified read-only against the installed FrooxEngine assembly for Resonite
2026.9.18.82. This is API evidence, not native execution of the new wrapper or a
repair of the underlying engine exception.

## Reproduction and remaining acceptance

Tests/AnonymousReportDiagnosticsTests.cs covers reason privacy, aggregate counts
after sample saturation, concurrent updates, isolated snapshots, local-only
correlation, original native error preservation, cancellation and per-model
isolation. Tests/PrefabObjectLookupTests.cs exercises actual unresolved-target
tagging. The Release build checks the native delegate adapter's compilation.

The later report establishes original mapper fault capture; native cause and
successful model reconstruction remain unverified. The one consolidated acceptance
checklist remains in AVATAR_CONVERSION_PROGRESS.md. Better diagnostics do not
repair serialized skin/composition gaps or establish whole-avatar fidelity.
