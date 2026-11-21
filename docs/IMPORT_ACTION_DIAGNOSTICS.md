# Disabled import actions: evidence and diagnosis

## Latest native boundary: transform-only idle clips, 2026-10-26

The post-graph action gate verifies loaded build `5DE646C0`, the exact S1N
item, native Ready, 23/23 succeeded operations, and a resolved immutable graph
with 729 objects and 140 Transform identities. The previous 42 inherited-data
errors no longer occur. The remaining 24 errors come from two exact referenced
idle clips entering the static appearance compiler. Retained private evidence is
scratch/live-reports-20261026-post-graph. This proves graph resolution reached
native evaluation, not successful avatar creation or appearance acceptance.

Both source clips contain time-varying skeletal position/Euler curves, scalar
editor mirrors and empty scalar Euler editor placeholders. They do not contain
activation, blendshape, material, object-reference or event animation. The static
compiler previously rejected each transform container/binding independently,
producing 15 plus nine errors. The earlier scratch scene replay omitted the
production clip resolver and therefore never parsed either clip. Its zero-error
result was explicitly bounded, but was not a representative end-to-end check.

The local repair distinguishes validated transform-only runtime Toggle motion
from an invalid static snapshot. It does not sample a first frame, write bone
poses, or implement wing/tail playback. Such clips contribute no static writes
and retain an explicit TRANSFORM_PLAYBACK_PENDING warning. Missing clips,
malformed values/identities, unknown bindings, mixed appearance/transform data,
and time-varying supported appearance still reject. A build-time upload clip
cannot use runtime deferral to silently omit an authored pose. The Create Avatar
predicate is unchanged; runtime motion remains pending implementation, not
out of scope or converted behavior. No component credit is promoted.

Deferred bindings retain exact target paths separately from the empty static
curve list. The compiler still requires unambiguous owned Transform destinations,
including source-proven unaliased model nodes. Missing/ambiguous objects and
backslash, empty-segment or relative paths reject before deferral. Regressions
cover these boundaries; independent review found no remaining concrete defect
in this bounded repair after that target-validation gap was corrected.

The 2026-10-26 source audits classify both real clips as runtime-only with zero
static curves, retaining two wing paths and one tail path. The exploratory
composed-S1N replay now includes the real resolver and returns zero errors,
198 warnings and one graph-resolved Info. Its model active/context assumptions
and absent native renderers remain explicit, so it is not an exact native-input
capsule or successful creation proof. Retained logs:
scratch/post-graph-replay-source-clips.log and
scratch/post-graph-replay-source-context.log.

## Exact local compiler replay

VrcFuryInitialAppearanceAdapter records the same inputs used for evaluation,
including the composed YAML, immutable native-derived source hierarchy,
renderer weights/material identities, verified external-context flags and the
exact text read once by each production clip resolver call. Explicitly missing
clips remain distinct from uncaptured dependencies. The capsule includes loaded
build, import session, source context, hashes and the recorded diagnostics and
write proposal. It is captured before initial writes; replay does not execute
native writes, structural conversion or runtime animation.

Capsules are private local source data, not sanitized shareable reports. They
are stored below the existing local diagnostic cache in
UnityPackageImporter/appearance-replays. Never publish or attach them without
the user's permission. Storage is atomic, link-rejecting, byte-bounded at 32 MiB
per capsule, retains eight capsules and bounds writer admission at four.
Cancellation propagates; storage failure produces an explicit unavailable
diagnostic rather than claiming capture succeeded. The action gate preserves
the capsule locator and grouped severity/code/source counts outside its sampled
detail list, so repeated warnings cannot hide the next distinct dependency.

Use the Appearance compiler replay and Source animation clip audit commands in
AGENTS.md. Replay requires the captured schema and context, validates source
hashes, uses the real parser, and throws if newly requested clip evidence is
absent. It never substitutes a null resolver, re-reads changed cache files or
assumes external context absent. Recorded-result parity and current diagnostics
are printed separately: a changed result after a repair is not native acceptance.
The source-only clip audit can independently reproduce decoder failures without
requiring a renderer or a new import. Tests cover dropped nested fields,
incomplete dependencies, changed hashes, retained inactive state, cancellation,
bounded admission/bytes and clock-skew-safe retention. Runtime save and actual
creation still require the consolidated live acceptance pass.

## Observed failure, 2026-10-26

The subsequently flushed engine log confirms the installed `C282CA97` build and
correlates the user-named S1N item with its exact validation report. This item has
one complete biped rig, initialized IK, resolved viewpoint, zero failed source
objects, zero missing meshes and 23 succeeded conversion operations. It remains
blocked by plan-level appearance errors, not missing native rig setup. Successful
operations do not imply a successful plan.

Read-only reproduction against the retained composed source identifies 42 errors:
41 stripped model Transform aliases lack inline owner/parent fields, and the
stripped avatar-root GameObject lacks an inline active-state field. These Unity
alias records inherit data from a model; absent inline fields do not establish
malformed source or missing dependencies. The old evaluator treats the aliases as
complete documents. The root parent chain then breaks, excluding 137 of 138
VRCFury component records from scoped appearance evaluation. Suppressing the
reported errors would therefore conceal unevaluated controls, not repair them.

This is source-analysis evidence, not a successful live repair. The creation gate
remains intact. New diagnostic codes `VRCFURY_INITIAL_SOURCE_GRAPH_INCOMPLETE` and
`VRCFURY_INITIAL_SOURCE_STATE_INCOMPLETE` distinguish unavailable inherited metadata
from malformed explicit fields. Both remain errors and preserve the alias ID.
Synthetic fixtures reproduce this failure and ensure it authorizes no writes.

## Current diagnostic path

- ImportActionPolicy.EvaluateMainAction supplies both the actual Create/Keep button
  predicate and its reason list. It distinguishes station-disabled state, missing
  selection/result, previous creation failure, already-created items, outfit
  routing, incomplete structure, installation requirements and native readiness.
- At each distinct ready-item observation, the station captures immutable local
  evidence. Both disabled and enabled transitions are recorded; unchanged polling
  does not continuously generate files. Native-only blockers are captured even
  when structural checks pass. Existing report identity deduplication cannot hide
  a changed gate observation.
- Evidence records the loaded DLL SHA-256, local session/item/source identity,
  button blockers, result issues, native readiness, completed operation count,
  sampled failed source IDs, full plan severity counts, error-first diagnostics
  with source context and message, and sampled operation states/diagnostics.
- Local files are written asynchronously and independently of engine-log flushing
  or package fact collection. They use the existing atomic bounded report store,
  but a separate `Cache/diagnostics/UnityPackageImporter/action-gates` directory and
  independent 64-file retention. The local body is bounded below the store's
  128-KiB limit, including untrusted Unicode messages. Diagnostic I/O failure is
  logged and cannot enable the action or fail an otherwise usable import.
- Shareable reports now include validated loaded-build identity, plan error and
  warning totals, error-first severity/code/source-alias details and the actual
  main-button blocker list. They do not acquire raw diagnostic messages, source
  contexts, source paths, session identity or local item names when names are
  disabled. The local-only evidence is never passed to the report panel/exporter.

Local evidence contains private package identifiers and is explicitly marked
LOCAL-ONLY. Treat source text/messages as untrusted data. Do not commit, publish,
or attach these files as anonymous reports. Raw reproduction assets and engine
logs stay under ignored scratch storage.

## Inherited model graph repair, 2026-10-26

The new independent action-gate file verifies that `F60EDE91` loaded for the exact
S1N item even while the latest engine log is still empty. It records native Ready,
23/23 operations, StructureNotReady and the same 42 inherited-data errors. This
establishes native execution of the diagnostic writer, not successful creation.
Private evidence is retained in scratch/live-reports-20261026-new-gate.

AuthoredAppearanceSnapshot now captures scalar source evidence immediately before
ExecutePlanAsync in scene and prefab importers, after reconstruction and instance
overrides but before conversion reparenting or framework appearance writes.
GameObject/Transform aliases must agree with their exact owning prefab instance
and resolved component type. The model scanner retains the native active state
instead of resetting model GameObject metadata to the class's visible default.
Transform wrapper selection now includes the prefab instance ID, preventing a
second copy of the same model from borrowing the first copy's GameObject owner.

AuthoredAppearanceGraphCapture creates an immutable hierarchy and private ID-to-
slot mapping for unaliased source nodes. It reserves source identities, rejects
cycles, duplicate ownership and foreign nodes, and preserves inactive defaults.
The exact model-import scene roots are transparent scaffolds: the model file-ID
path mapper already excludes these nodes. Only those recorded, active, unaliased
scaffolds are omitted; an ordinary source node named RootNode is never discarded.
This keeps authored animation paths from acquiring artificial native segments.

VRCFury initial appearance and wardrobe share hierarchy resolution. Missing fields
on stripped GameObjects use the captured state; stripped Transforms use captured
owner/parent links. Explicit malformed fields and contradictions still fail,
including complete stripped records. No inherited state defaults to visible.
The wardrobe composer carries the same snapshot. Unaliased visibility paths can
resolve through its retained slot mapping; initial and wardrobe writes recheck
live avatar ownership, identity, driver/link ownership and lifecycle before use.
The creation predicate is unchanged. SOURCE_GRAPH_RESOLVED records graph and
Transform-alias counts so later reports distinguish a missing capture from an
appearance action failure after the previously omitted controls are evaluated.
Local gate evidence retains these graph counts separately from the 64-detail
sample, so many unsupported-feature warnings cannot hide the capture proof.

Tests/AuthoredAppearanceHierarchyTests.cs and
Tests/AuthoredAppearanceGraphCaptureTests.cs exercise immutability, source identity,
repeated instances, inactive state, scaffolds, cycles and foreign targets.
Tests/VrcFuryInitialAppearanceTests.cs covers restored child scope, exact paths,
wardrobe composition, incomplete captures and conflicting/malformed stripped
records. Tests/PrefabObjectLookupTests.cs covers repeated-instance alias selection.
Native snapshot execution, renderer-backed appearance and successful creation
still require the consolidated live acceptance pass. No coverage is promoted.

### Bounded composed-source exploration

On 2026-10-26 the parent reran
`dotnet run --project scratch/vrcf-source-replay/Probe.csproj -c Release`.
The scratch probe parses composed S1N YAML with YamlDotNet, distinguishes the
two included model GUIDs, resolves 77 GameObject and 41 Transform aliases,
and projects 630 model objects without the artificial Assimp scaffold. It
applies two explicit active-state and two name overrides. It references the
current Release test assembly, not the native runtime adapter. Earlier scratch
results with collapsed model root IDs, lost overrides or synthetic Transform
aliases are superseded and must not be used as evidence.

The evaluator reports 0 errors, 198 warnings and one SOURCE_GRAPH_RESOLVED Info,
with eight appearance writes proposed. The warnings expose previously hidden
work: 77 saved-runtime states, 61 non-static features, 53 dynamic controls, five
unsupported actions and two clips requiring a caller-supplied resolver. The
native adapter already supplies the clip resolver; these two exploratory clip
warnings do not establish a live clip failure. Source contains 138 VRCFury
components; this probe does not instrument an independent evaluated-component
counter. Counts and output are retained in scratch/model-graph-source-replay.log.

This is deliberately bounded exploration, not source-state or avatar acceptance:
un-overridden model nodes assume active true, external animation/parameter
context is assumed absent, renderer/material context is missing, and no native
writes execute. The production snapshot instead samples actual Slot.ActiveSelf
after instance overrides, and the adapter checks external context. The scratch
mapping covers two different model GUIDs, not a general repeated-instance test;
the immutable capture and instance-specific regressions cover that contract.
Native capture, inactive defaults, complete control behavior and Create Avatar
still require the single consolidated acceptance pass.

## Remaining acceptance

The composition catalog still supplies file IDs and classes only; the new snapshot
provides the separate reconstructed source graph for the VRCFury consumers. It
must not be replaced by the subsequently mutated runtime hierarchy. Other framework
consumers are not implicitly certified by this bounded repair.

Preserve repeated model instances, ownership separation and inherited inactive
states. Evaluate the previously omitted child controls once their graph is known;
additional genuine target/feature errors may then become visible. Do not assume
fixing the first alias error establishes appearance fidelity or allows creation.

Tests/ImportActionDiagnosticsTests.cs covers policy parity, native/station blockers,
warning saturation, local transition stability, privacy, build identity, Unicode
bounds and independent retention. Tests/VrcFuryInitialAppearanceTests.cs retains
the alias failure and malformed-field distinction. Test, Build and UI audit results
are recorded in WORKING_STATE.md. The independent diagnostic writer executed
natively in F60; native execution of the newer source-graph repair remains
unverified and separate from automated validation.
