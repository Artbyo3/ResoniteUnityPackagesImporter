# Whole-avatar conversion implementation ledger

Local work on 2026-10-24, based on master a93144b. This ledger is not a release
announcement or a claim of complete avatar compatibility. No changes in this
ledger have been confirmed by a live Resonite import.

## Disabled action diagnosis and observability (2026-10-26)

The newest retained native gate file from installed `5DE646C0` confirms the graph
repair executed for S1N: 729 objects, 140 Transform identities, native Ready and
23/23 successful operations. The former 42 inherited-data errors are gone. The
next blocker is 24 static-clip binding errors from the real wing/tail idle clips.
The earlier exploratory replay omitted the production clip resolver, so it never
exercised those dependencies. Its zero-error result was not representative of the
native compiler boundary.

The next local batch validates transform-only runtime Toggle motion separately
without writing bone poses. Required build/upload poses, mixed bindings and
malformed evidence remain blocking. Exact-input local replay capsules capture the
production resolver dependencies and native-derived context before writes; a
missing dependency cannot silently become a resolver-free warning. Source audits
of both actual idle clips classify runtime playback with zero static curves.
Playback is unimplemented, no coverage is promoted, and native capture/creation
remain unverified. Current automated validation and install status are maintained
in WORKING_STATE.md. This batch was installed at the user's request on 2026-10-26
with Resonite and renderers closed. Test and Build were rerun: 1471/1471 passed,
0 errors and 79 warnings. All four installed hashes verified; DLL SHA-256:
`DF35C43D82136AF79EA0644F990E530700E55837BC9E251B3ED3E13D484A527D`.
Evidence: scratch/clip-replay-install.log and its adjacent test/build logs.
No process was stopped automatically. This supersedes installed 5DE646C0 and is
ready for live test after restart, not evidence of native creation or appearance.

Follow-up local gate evidence confirms the installed `F60EDE91` diagnostic build
recorded the exact user item despite an unflushed engine log: native Ready,
23/23 operations and the same 42 inherited model-graph/state errors. Native
diagnostic file generation is verified; avatar creation remains unsuccessful in
that checkpoint.

The integrated local repair captures immutable, instance-scoped hierarchy and active
state after source reconstruction/overrides and before conversion reparenting.
VRCFury initial appearance and wardrobe now consume that graph, including exact
aliases and private IDs for unaliased ancestors. Model-import scaffolds are
excluded by recorded identity, not names; authored inactive state is retained.
Repeated model Transform aliases can no longer borrow another instance's owner.
Malformed or contradictory source fields and missing evidence still reject;
the Create Avatar gate is unchanged. See IMPORT_ACTION_DIAGNOSTICS.md for the
snapshot boundary, regressions and remaining native acceptance. This repair is
not live-verified and does not promote component coverage. The final local batch
passes 1442/1442 regressions, builds with 0 errors and 77 warnings, and passes
all three embedded UI bundle audits. The Release DLL SHA-256 is
`5DE646C09DBCD26C9689CF6E9FEDE31A4A5A98F314CC06ED62447A6B2CAC3E40`.
The user subsequently requested installation on 2026-10-26. With Resonite and
renderers closed, Test and Build were rerun (1442/1442, 0 errors, 77 warnings),
and scripts/Install-Mod.ps1 verified all four installed file hashes. Evidence is
scratch/model-graph-install.log and the adjacent install test/build logs. No
process was stopped automatically. This supersedes installed F60 and is ready
for live test after a full restart; native capture, creation and appearance are
not yet verified. Explicit stripped parent references now require a Transform or
RectTransform with local ownership, and graph proof survives diagnostic warning
saturation independently of the sampled detail limit.

The later flushed log confirms `C282CA97` and the exact user-named S1N item.
Its rig is Ready, all 23 operations succeeded and no source objects failed, but
42 plan errors arise from stripped model aliases lacking inherited owner/parent
and active-state metadata. The broken root chain also hides most child VRCFury
controls from evaluation. This is not repaired by suppressing errors or merely
enabling Create Avatar. The new source codes distinguish incomplete inherited
evidence from malformed explicit fields, without changing the gate.

The actual main-button predicate now exposes reason codes. Distinct item/gate
observations immediately save separate local-only evidence with loaded DLL hash,
exact source context/messages, severity totals, error-first samples and operation
states. Shareable reports retain validated build identity, anonymous aliases,
severity/code details and actual button blockers without raw local context.
Storage bounds, privacy, native-only blockers, warning saturation and unchanged
policy behavior have reproducing regressions. See
[action-gate evidence and next repair](IMPORT_ACTION_DIAGNOSTICS.md).

The earlier retained composed-source probe without a hierarchy reproduces 41 source-graph errors and
one source-state error, with zero authorized writes. It supplies no native
renderer/material context and is not complete appearance validation. Faithful
instance-scoped model-graph resolution is now implemented as described above;
its native execution and the newly reachable controls remain unverified.
No component coverage is promoted and no live resolution of the grey button is
claimed. Latest automated and installed checkpoint state is in WORKING_STATE.md.

The corrected 2026-10-26 exploratory composed-S1N replay resolves 77 GameObject
and 41 Transform aliases across the two included model GUIDs. With 630 projected
model objects and the explicit active/name overrides, the current pure evaluator
returns 0 errors, 198 warnings and one graph-resolved Info. This is not native
graph capture or acceptance: active defaults and absent external context are
assumptions, renderers and a clip resolver are absent, and no writes execute.
The newly reachable warnings and exact limits are in IMPORT_ACTION_DIAGNOSTICS.md.
The production adapter samples native state and supplies the clip resolver.

The requested diagnostic checkpoint was installed on 2026-10-26 with Resonite
and its renderers closed. `scripts/Install-Mod.ps1` verified all four copied files
against their source/build hashes. Built/installed DLL SHA-256:
`F60EDE91DE95D0A02C6FEBDCBF94228F91F000530557D65D87490BA5146A9813`.
This supersedes `C282CA97`; local evidence is scratch/action-gate-install.log.
Test and Build were rerun: 1410/1410 passed, 0 errors and 77 warnings; the rebuilt
DLL still matched the installed hash at that checkpoint. The later independent
gate file verifies diagnostic execution, but this installation does not repair the inherited
model graph or the grey Create Avatar.

## Serialized skin reconstruction (2026-10-26)

The local batch now reconstructs bounded version-11 inline serialized skins with
explicit one-to-four influences, bind poses and sparse single-frame blend shapes.
Targets use exact ordered renderer bone references, not names or optional hashes.
Native MeshX construction preserves indices/submeshes, normalizes and sorts near-unit
weights without trimming positive influences, and pads indexed material slots.
Multi-frame and non-100-percent shapes reject before native construction instead
of being flattened: installed native evaluation does not faithfully support them.
Missing weighted targets and unsupported layouts still reject;
the static path cannot substitute for deformation. A separate sanitized decoder
rejection reason distinguishes this failure from missing FBX sources.

See [serialized mesh evidence](UNITY_SERIALIZED_MESHES.md) and the new repeatable
native asset audit command in AGENTS.md. Five retained meshes plus the synthetic
single-frame fixture passed native Plain/LZ4 encode/decode checks, including all
geometry channels, bindings and frames. This is offline public API evidence, not
native renderer loading, deformation, appearance or user acceptance. Implicit
rigid weights and populated variable-count storage remain unsupported. The
earlier scene/scale repairs remain integrated; no component coverage is promoted.

Automated checkpoint: 1398/1398 regressions passed; Release build has 0 errors and
77 warnings. The integrated scene/scale/skin checkpoint was installed with Resonite
closed; all four copied files passed hash verification. Built/installed DLL:
`C282CA97959A3940993A956901C43D269A74451FE845B89203268F93ADD3BA70`.
It is ready for live test, not live-verified or approved. Keep the
existing single consolidated checklist rather than requesting per-fix imports.

## Scene composition and bounded scale repair (2026-10-26)

Later retained reports distinguish a ready rig blocked by unresolved hierarchy
from a separate package's missing rig. Scene imports now share the existing
prefab composer, feed composed evidence through conversion and initial appearance,
and preserve missing-source provenance and exact-root ownership safeguards.
The retained S1N source composes into 577 documents with two remaining included
model references; its 23-operation plan has no errors after implementing the
version-7 recursive, automatic, nonquantized root scale ratio. Optional feature
warnings remain. No gate is bypassed and no component coverage is promoted.

See [scene composition and scale evidence](SCENE_PREFAB_COMPOSITION.md) for the
primary upstream implementation, regressions, bounded policy and native gaps.
Local item/report/gate correlation is also added. A later retained report now
captures the original native material mapper exception at the Materials phase;
the underlying failure and other composition failures remain unresolved. The
new bounded serialized skin path is described above, not live-verified. The
subsequently flushed engine log confirms that
the installed `D408B1B6` checkpoint was loaded, not the new composition/scale batch.

Automated checkpoint: 1337/1337 regressions passed; Release build has 0 errors
and 77 warnings. This batch is not installed or live-verified. The installed
checkpoint remains `D408B1B6` below. Retain the single consolidated live checklist.

## Earlier live imports and diagnostics (2026-10-26)

The flushed session confirms the installed `1B19FE57` checkpoint ran; it does
not confirm appearance or usable behavior. The retained evidence contains 20
reports and one engine log, not all historical imports. Of 146 unavailable
skin-template warnings, 145 reference included serialized Mesh assets in the
retained cache. Serialized skin reconstruction is the principal observed gap,
not proof of missing FBX dependencies. Composite-prefab hierarchy failures and
a native material callback exception also remain unresolved.

The local diagnostics batch separates typed hierarchy/source reasons, preserves
failure-group totals beyond sampled details, correlates reports in local logs
without exporting session identity, and captures native mapper faults per model.
See [import diagnostics](IMPORT_DIAGNOSTICS.md) for source/test evidence and
uncertainty. A later interruption cannot be attributed to an earlier callback
exception by timing alone. No fallback material or arbitrary hierarchy target
is introduced. No coverage credit is added.

Automated checkpoint: 1315/1315 regressions passed; Release build has 0 errors
and 77 warnings. At the user's subsequent installation request, the installer
verified all four copied files with Resonite closed. Installed DLL SHA-256:
`D408B1B6189A7C38861821BB80DF6B33527B74E334D57B2640903AFD9B3F4262`.
No process was stopped automatically. Native attribution, appearance and
behavior remain unverified; retain
the existing consolidated live acceptance checklist rather than per-fix probes.

## Coverage validation follow-up (2026-10-25)

The checked-in coverage report comparison is read-only. Previously the test
rewrote the report from the catalog before comparing it, hiding stale reports.
Coverage regeneration is an explicit authoring step, not a test side effect:
run `dotnet run --project Tests/Importer.RegressionTests.csproj -c Release -- --component-coverage docs/AVATAR_COMPONENT_COVERAGE.json`
and save its output to `docs/AVATAR_COMPONENT_COVERAGE.md` when intentionally
updating the catalog. Review both files together before running regressions.

A deliberate report-title mutation fails the comparison (1161/1162 tests pass)
without rewriting the mutated file. The probe is removed afterward. This checks
report freshness only; it does not certify any avatar behavior in Resonite.

The real mod build also exposed an unterminated wardrobe diagnostic string,
an inconsistent behavior-runtime accessibility declaration, and nonexistent
generic `Wiggler` components. The breathing adapter now uses native
`TimeSineDriver` and `Float3Driver` components. Angular speed preserves the
planned period, bone expansion preserves authored nonuniform scale, and
already-driven or linked scale fields are left untouched. Native behavior
still requires a live import after installing and fully restarting Resonite.

Validation for this follow-up: 1163/1163 Release regressions pass; the required
non-incremental mod build reports 0 errors and 77 warnings. All three embedded
UI bundle audits pass. After Resonite was closed, the installer retry succeeded
and verified all copied files. Installed DLL SHA-256:
`E10A34A6A3522FA925D86887339862DE5FB5D5A6F9D727552ACF7B8101FAAC6C`.
This installation is confirmed, but no live import has been verified.

## Action semantic audit (2026-10-25)

The local `ScaleAction` and `WorldDropAction` branches emitted only
`m_IsActive` bindings. They neither scale transforms nor implement world-space
drop/reset. Removed those substitutes: the existing unsupported-action diagnostic
now blocks wardrobe runtime construction instead of silently changing visibility.
This does not implement either action or remove them from the conversion scope.

Regressions cover ordinary, avatar-root and unresolved targets, no emitted
bindings, no shared runtime state, and rejection alongside supported controls.
The old assertions that credited visibility as scale/drop behavior are replaced
with these semantic rejection checks; the separate FxFloatAction assertion stays.
The coverage catalog marks both actions missing with no runtime credit, and a
regression pins that boundary. Action coverage is now 8/19 (42.1%); combined
VRCFury coverage is 29/87 (33.3%). All denominators stay unchanged. Other coverage
counts are not certified by this focused correction.

Remaining audit findings in `VrcFuryWardrobeCompiler.cs` require follow-up before
expanding support: MaterialPropertyAction and FxFloatAction use shape-effect
bindings; Modes emits separate Boolean controls without exclusive selection;
Puppet builds endpoints without authored stop/axis semantics; Toes targets a
synthetic `Toes_Curl` property on an owner/root rather than resolved toe bones.
These are unresolved semantic concerns, not verified native behavior. Their
existing adapters and coverage entries are unchanged in this focused increment.

The installed DLL hash above was rechecked during this audit. Resonite and its
renderer are running, so that earlier installation is preserved. It does not
contain this audit's action-rejection changes. Do not terminate the running
session or replace its DLL; installing the new build requires closing Resonite,
running `scripts/Install-Mod.ps1`, and fully restarting before the user live test.

Validation for this audit: 1171/1171 Release regressions pass; the required
non-incremental mod build reports 0 errors and 77 warnings; station, pre-import
and error-report embedded bundle audits pass; `git diff --check` passes.
Before explicit report regeneration, the stale-report assertion was the only
regression failure (1170/1171), confirming it still detects catalog/report drift.
The installer was attempted and refused because Resonite or its renderer is
running. Newly built DLL SHA-256:
`526CEFCE916F31C264B6B6BDD22E3FF261335524CC42BE299ADB07A89B8731AB`.
The installed DLL remains the earlier `E10A34A6...` build. No native startup,
import, wardrobe, breathing timing/scale, field-driver conflict, or remote-user
behavior has been verified. The build is ready for live test after installation
and a full restart, not a whole-avatar fidelity certification.

## Installation checkpoint after action audit (2026-10-25)

After the user authorized installation and Resonite was closed, the repository
installer succeeded and SHA-256-verified the mod, YAML dependency, license and
notices. The installed DLL now matches the action-audit build:
`526CEFCE916F31C264B6B6BDD22E3FF261335524CC42BE299ADB07A89B8731AB`.
This supersedes the earlier installation-blocked checkpoint, not its historical
validation results. The subsequent live-session findings are recorded below;
successful loading or attempted imports do not establish whole-avatar fidelity.

## Live import triage (2026-10-25)

The user attempted several imports in the fresh 10:53:02 session. Its loader log
confirms the installed `526CEFCE` build above, rather than the earlier breathing
build. Reports and extracted source assets distinguish these failure classes:

- A skinned renderer rejects absent authored bones even though the corresponding
  source FBX clusters have no vertex influences. The local fix reads all four
  native influence slots per vertex through public `MeshX.RawBoneBindings` and
  `BoneBinding.GetBinding`. Only proven zero-influence missing bones retain null
  slots at their original indices; bind poses and mesh indices remain unchanged.
  Weighted, unknown, malformed and ambiguous bindings remain rejected.
- Static renderers reference Unity's built-in resource mesh `10209`, unsupported
  by the previous cube-only implementation. Local plane reconstruction and
  strict identity/classification regressions now pass. This is an
  implementation gap, not a missing external dependency; native appearance and
  texture orientation still require a fresh live comparison.
- An unpacked avatar's bone attachment cannot select its main Head when clothing
  contains another Head. The local fix uses the exact descriptor-root Animator
  avatar reference and model skeleton provenance before structural conversion.
  Exact root-relative paths reject foreign, missing, duplicate, cyclic or
  ambiguous references without borrowing clothing bones or creating a rig early.
- One requested charm submesh is absent from the supplied model's 22 meshes.
  Preserve the owner and report the missing renderer; the matching source model
  revision or a repaired source reference is required, not a substitute mesh.
- Included nested prefab sources remain unsupported expansion, not missing
  dependencies. VRCFury automatic armature linking and unsupported scale options
  remain explicit limitations. Their diagnostics must not be hidden to pass.
- A separate model import was interrupted during native Nodes processing. The
  completion safeguard reports interruption but does not identify its cause;
  this remains unresolved rather than being attributed to a missing dependency.

These logs establish failure reproduction, not successful wardrobe controls,
breathing timing, preservation of nonuniform bone scale, existing-driver
coexistence, owner/observer agreement, or whole-avatar visual fidelity. No world
objects were changed during this offline triage. All changes remain local.

### Follow-up validation and installation gate

- Release regressions: **1234/1234 passed**, including 30 authored-target cases,
  unused-bone strictness/alignment cases and built-in plane geometry/identity cases.
- Non-incremental Release mod build: **0 errors, 77 warnings**.
- Release DLL UI bundle audit: station, pre-import and error-report passed.
- Documentation tests: **88 passed**; `python docs-site/build.py --local` passed.
- `git diff --check` passed (line-ending notices only).
- Validated build SHA-256:
  `C55310B636FEB75AC55C60C171FEB235686CA79F75371D4BE1661936FBC00CBB`.
- The guarded installer was attempted, but refused while Resonite or its renderer
  was running. No files were copied. The installed build remains `526CEFCE`.
  Close Resonite completely, install, then restart for another live test.

The user subsequently confirmed Resonite was closed. The installer retry
succeeded and verified all four copied files against their build/source hashes.
The installed DLL now matches the validated `C55310B6` build above, superseding
the blocked attempt and the previous `526CEFCE` installation. A fresh startup
and repeat imports are still required; this build has no live-test approval.

Implementation evidence: `SkinBoneMapping.cs`, `SkinnedMeshRenderer.cs`,
`AuthoredHumanoidTargets.cs`, `AvatarBuildPlanExecutor.cs`, `ModelMetaParser.cs`,
`UnitySerializedMesh.cs`; regressions in `NativeSkinBindingTests.cs`,
`AuthoredHumanoidTargetsTests.cs` and `UnitySerializedMeshTests.cs`. Native public
API compilation and null-bone handling evidence do not certify deformation.
The new build has not been live-tested or approved; missing source geometry,
nested expansion, armature/scale limitations and interruption cause remain open.

## Second live import batch (2026-10-25)

The fresh 11:26:34 session loader confirms the installed `C55310B6` build above.
Its raw log identifies ten extracted/reused package cache identities: four also
occur in the prior 10:53:02 session and six do not. The twenty retained failure
reports cover only four package inventories; they are not a count of attempted
packages or a success denominator. Their source-failure lists can include other
results in the same import session.

The earlier unused-bone fixture and unpacked authored-Animator target fixture
were not repeated. Absence of their old errors in this different batch does not
validate either fix. The built-in plane reaches activation, but a plane material
reports a duplicate YAML key; neither texture fidelity nor appearance is proved.
Four built-in identity failures now name `10207`, not the implemented `10209`.
This is another unsupported built-in identity, not a missing external asset.

Remaining observed failures include prefab override targets/parent transforms
that cannot resolve, two YAML deserialization failures, unresolved attachment
targets, and the already disclosed VRCFury scale/automatic-link limitations.
The standalone light-helper HeadProxy failures are not sufficient evidence of a
regression in the unpacked whole-avatar target fix. One package import reports
that no prefab or scene could be reconstructed. No new code or live-world changes
were made in this comparison, and no whole-avatar success is claimed.

Keep the two original targeted fixtures in the consolidated live acceptance
checklist before accepting those fixes. Their absence from this batch does not
block further implementation. Preserve the new batch as separate diagnostic
evidence rather than assuming it exercises the same assets or suppressing its
remaining failures.

## Batched development workflow (2026-10-25)

The user explicitly requested fewer manual test/restart cycles. Continue through
the known failure backlog autonomously in coherent batches: diagnose from existing
logs/source fixtures, implement faithful fixes, add regressions, and run Test +
Build plus applicable audits. A small code change is not a mandatory user live-test
gate. Do not repeatedly reinstall or ask the user to retry individual fixtures
while other independently actionable failures remain.

Install a validated integrated build at a meaningful checkpoint, only while
Resonite is closed. Consolidate appearance, rigging, attachment, controls,
breathing, scale/driver-conflict and lifecycle checks into a final live acceptance
pass, followed by a grouped adjustment round if needed. Keep unverified native
semantics explicit; neither automated tests nor absence of an error establishes
whole-avatar fidelity. Request an earlier narrow runtime probe only if unavailable
evidence genuinely prevents safe progress, not as the default development loop.

## Offline reconstruction repair batch (2026-10-25)

The failed delegated attempts produced no usable implementation. Work continued
locally without another manual import gate. This checkpoint addresses source
reconstruction; it does not establish complete avatar compatibility or add
component coverage credit.

### Implemented and reproduced

- Replaced procedural built-in geometry with six exact Unity 2022.3.22f1
  primitive captures: cube, cylinder, sphere, capsule, plane and quad. Full raw
  payload hashes, topology/channel counts, corruption rejection and independent
  import arrays are pinned. The observed sphere identity `10207` now resolves.
  Capturing the actual plane also corrected the previous reversed UV orientation
  and tangent frame. Small editor float deviations are retained; analytic tests
  allow those deviations while full-data fingerprints remain exact. See
  `UNITY_BUILTIN_MESHES.md` for capture provenance and format.
- Typed reference dictionaries previously rejected negative Unity fileIDs even
  though document anchors already retained their unsigned bitwise identities.
  Added a read-only YAML converter restricted to reference dictionaries and
  signed `fileID` values. Other unsigned data retains its previous validation.
  Regressions reproduce the old deserializer failure, preserve cross-object
  identity, exercise signed/unsigned boundaries, and reject overflow, malformed
  values, duplicate keys and nested values. The actual TPS source prefab that
  previously failed now parses into 24 objects in an offline runtime-parser
  replay; no engine object is instantiated by that probe.
- Reproduced the plane material failure in its actual included sensor material,
  rather than the unrelated light-handle material. The source contains Unity's
  sole empty `stringTagMap` entry (`    : `), which YamlDotNet rejects. Normalize
  only that exact, otherwise-empty mapping in memory. The same source now parses;
  LF/CRLF and property-preservation regressions pass. Duplicate tags and ambiguous
  empty mappings remain rejected. This repairs parsing, not the sensor shader's
  executable behavior or native visual equivalence.

### Automated evidence and remaining blockers

- Release regressions: **1263/1263 passed**.
- Non-incremental Release mod build: **0 errors, 77 warnings**.
- Release DLL UI bundle audit: station, pre-import and error-report passed.
- Documentation tests: **88 passed**; local English/Japanese docs build passed.
- Offline parsing with the rebuilt mod's actual YAML wrappers: **541 of 542**
  cached prefab/scene files parsed. The remaining source is a binary Unity scene
  identifying Unity 5.6.3p1, not text YAML. No binary scene decoder is implemented.
  These files span the retained cache, not just the latest ten-package session.
  Parsing is not prefab composition, native reconstruction, or live acceptance.
- The large serialized-skin failure group is **included mesh `.asset` data**, not
  a missing FBX dependency. One examined mesh is serializedVersion 11 with an
  exact renderer mesh reference. The current generated-scene path is static-only;
  faithful bind poses, bone influences, blend shapes and index correspondence
  must be implemented together before these skin assets can render correctly.
- The GoGoLoco variant's failing override points to a component absent from the
  included source prefab; another override points to its current constraint.
  No component is guessed or substituted, and strict rejection is retained.
- The observed scene parent alias refers to a prefab GUID not present in that
  extracted package's metadata. It cannot provide a parent slot. This specific
  missing-source case is separate from included nested-source expansion gaps.
- Previously recorded controller/composition, scale-link, attachment, missing
  charm geometry and native lifecycle/appearance gaps remain open. No new live
  success or user approval is inferred from this offline batch.

The consolidated live checklist remains unchanged: original unused-bone and
authored-target fixtures, whole-avatar appearance/deformation, wardrobe controls,
breathing timing, authored nonuniform scale and existing field drivers, plus
owner/observer, reset and re-equip behavior. Do not request individual restarts
for each fix while the independent backlog is still actionable.

### Integrated installation checkpoint

With Resonite and its renderer closed, `scripts/Install-Mod.ps1` succeeded and
verified all four copied files. Installed Release DLL SHA-256:
`E6F3522DBECD0E555653F43AFFC9321D50043139A00E4F161B57122AACAD0EF5`.
This supersedes `C55310B6` as the installed build. `git diff --check` passed.
The local docs preview was rebuilt without changing the released-source baseline
or assigning new component coverage credit. Changes remain uncommitted on the
existing branch, with prior local edits preserved. No Resonite process was
stopped, world modified, commit created, branch created, push made or PR opened.
Ready for live test as an integrated engineering checkpoint only; no new live
success is claimed, and no per-fix manual test is requested.

## Serialized mesh structural validation (2026-10-26)

The static decoder now distinguishes absent optional metadata from malformed
present YAML, rejects orphan blend-shape weights and overlapping attributes,
bounds stream arithmetic before narrowing, and rejects incomplete index elements.
Seventeen regressions reproduced the preceding silent-acceptance/overflow paths;
three further regressions preserve empty optional metadata and sparse unpadded
streams and check partial 32-bit indices. See
[serialized mesh evidence](UNITY_SERIALIZED_MESHES.md) for exact scope and
reproduction, and [working state](WORKING_STATE.md) for current validation.

This does not implement serialized skinning or certify native appearance. The
included-skinned-asset dependency remains open. No coverage promotion, new
installation, live success, or per-fix manual import gate is implied by that
automated checkpoint alone.

### User-requested installation (2026-10-26)

`scripts/Install-Mod.ps1` succeeded and verified the mod DLL, YamlDotNet, license,
and third-party notices against their build/source hashes. Installed DLL SHA-256:
`1B19FE57F848CFA9EF2A76620C613123425F67A8EB5AA23F26B116E0D9635CF9`.
This supersedes the earlier installed build and includes the structural decoder
repair. No process was stopped automatically. Ready for live test after a full
Resonite restart; no user-confirmed live result or shipping approval is recorded.

## Acceptance contract


Reconstruct the built/avatar-upload outcome of plain VRChat SDK, Modular Avatar,
VRCFury, and their combinations for avatars, clothing, and combined packages.
Wardrobe is one acceptance fixture, not the project boundary. Every component
card remains a target unless its literal platform function has an explicit,
substantiated exclusion. Useful editor/build-time output is still in scope.

The acceptance fixture must compare appearance, skeleton/viewpoint, expressions,
clothing/material/shape controls, face/voice behavior, animations, physics,
constraints, and lifecycle. It must also cover owner/observer behavior, reset,
saved reload, repeated import, cancellation, and missing/ambiguous dependencies.
A synthetic independent 30-toggle test is not this mixed-system fixture.

## Card evidence rules

- Use concrete type names in AVATAR_COMPONENT_COVERAGE.json as stable local keys.
  Preserve the public cards' own IDs and denominators when publishing updates.
- Parsing/indexing, pure semantic evaluation, native implementation, regression
  validation, live validation, and released support are distinct milestones.
- A functional subset remains partial. Uncompiled semantics stay visible.
- Public cards currently describe the released baseline. Do not promote local
  code into released support, or overwrite the maintainer's docs-site edits.
- The generated coverage report includes excluded concrete types in its
  denominator. Partial credit is not full-fidelity credit or avatar-success odds.

## Added or corrected locally

| Card/type key or dependency | Change | Evidence and limits |
| --- | --- | --- |
| VRCAvatarDescriptor | Parse exact lipSync, local renderer reference, and ordered 15 viseme names. After native avatar creation, replace only newly created heuristic viseme links with exact authored mode-3 mappings. Use native voice source assignment. Restore authored weights on abandoned heuristic targets. | AvatarVisemeSettings.cs, AvatarVisemeRuntime.cs, NativeAvatarCreation.cs; AvatarVisemeTests.cs. Partial. Empty channels are retained; duplicate destination shapes require a future multi-source adapter. Non-viseme driver conflicts, ambiguous descriptor/head ownership, missing shapes and foreign renderers reject before adapter mutation. Native fallback is retained with a diagnostic on failed preflight. |
| VRCExpressionParameters | AST parsing retains exact case-sensitive names, typed/default presence and saved/networkSynced flags. Shared catalog supports scoped aliases, atomic typed updates, and pure unsaved reset. | ExpressionMenuParser.cs, AvatarParameterState.cs; AvatarWardrobeStateTests.cs. Indexed/foundation only for SDK/MA. This does not implement persistence, owner/remote policy, MA parameter precedence or arbitrary Animator parameters. |
| VRCExpressionsMenu | Replace indentation guessing with AST control parsing. Preserve control order, exact parameter names, ordered puppet subparameters, label names and external menu/icon references. Ignore nested unrelated names/types. | ExpressionMenuParser.cs; ExpressionMenuStructureTests.cs. Still indexed, not functional Button/Puppet/base-Animator support. Unknown control types remain unknown; unsupported native controls stay disabled. |
| Shared reactive/animation dependency | Immutable property baselines, ordered last-active rules, typed comparisons, ancestor dependencies, cycle rejection and cancellation. Only explicit independent-toggle projections may use the existing native driver adapter. | AvatarWardrobeGraph.cs, AvatarParameterState.cs; AvatarWardrobeStateTests.cs. Not an MA reactive compiler, Animator state machine, blend-tree player or whole-avatar runtime. |
| VRCFury Toggle / BlendShapeAction / AnimationClipAction / MaterialAction | Snapshot renderer baselines after structural preparation but before initial-appearance adapters. Default-on conversion restores original shape and material slot values when turned off, not recapturing the already-applied on value. Added runtime `MaterialAction` compilation (v1 `renderer` and legacy v0 `obj`) and native `BooleanReferenceDriver<IAssetProvider<Material>>` binding on `MeshRenderer.Materials.GetElement(index)`. | UnityPrefabImportTask.cs, PreparedImportItem.cs, VrcFuryWardrobeCompiler.cs, AvatarWardrobeRuntime.cs; VrcFuryWardrobeTests.cs. Bounded independent runtime support only; shared slot arbitration pending. |
| ModularAvatarBlendshapeSync / VRCFury BlendShapeLink | Emit verified runtime blendshape link tuples `(SourceRendererId, SourceBlendshape, TargetRendererId, TargetBlendshape)` alongside initial weight evaluation, rejecting multi-source conflicts on the same destination shape. Wire native `ValueCopy<float>` (`WriteBack = false`) drivers under a dedicated `"Blendshape sync"` slot when `AvatarRoot` is present, with all-or-nothing preflight and transactional rollback if any field is missing, remap-curved, or already driven. | BlendshapeInitialAppearance.cs, BlendshapeInitialAppearanceAdapter.cs, PreparedImportItem.cs; BlendshapeInitialAppearanceTests.cs. Partial. Multi-source blendshape blending remains pending. |
| ModularAvatarParameters / ModularAvatarMenuInstaller / ModularAvatarMenuGroup / ModularAvatarMenuItem / ModularAvatarObjectToggle / ModularAvatarShapeChanger / ModularAvatarMaterialSetter | Compile independent Modular Avatar menu trees (`MenuInstaller`, `MenuItem` SubMenu/Toggle, `MenuGroup` `children`/`otherObjectChildren`), scoped `ModularAvatarParameters` remaps/defaults/internal parameters, and reactive components (`ObjectToggle`, `ShapeChanger` Set mode, `MaterialSetter` with `m_inverted` and baseline restoration) into `AvatarWardrobePlan` and drive via `AvatarWardrobeRuntime`. Fail closed on external menu assets, `MenuInstallTarget`, `ShapeChanger` Delete mode, radial/axis puppets, multi-control shared parameters, and conflicting property writers. | ModularAvatarWardrobeCompiler.cs, ModularAvatarInitialAppearance.cs, PreparedImportItem.cs, AvatarWardrobeRuntime.cs; ModularAvatarWardrobeTests.cs. Promotes 4 MA components (`ModularAvatarMenuGroup`, `ModularAvatarMenuInstaller`, `ModularAvatarMenuItem`, `ModularAvatarParameters`) to partial (MA 10/40 = 25.0%). |
| Native conversion lifecycle | Defer expression creation until the selected avatar has native AvatarRoot setup; an early raw-prefab call no longer consumes the one-time build opportunity. Reject toggle/submenu path collisions. | PreparedImportItem.cs, AvatarWardrobeRuntime.cs. Native lifecycle and re-equip behavior still need live validation. |
| Mixed-source safety | Scope VRCFury and MA wrappers to the actual avatar. Reject unresolved ownership, legacy Animation, unknown interacting MonoBehaviours and wrong script fileIDs instead of treating runtime context as absent. Equal action platform flags are unrestricted; differing flags remain unsupported target restrictions. | VrcFuryWardrobeCompiler.cs, ModularAvatarWardrobeCompiler.cs; VrcFuryWardrobeTests.cs, ModularAvatarWardrobeTests.cs. Rejection is not mixed-system conversion support. |
| MA MeshCutter, RemoveVertexColor, ReplaceObject, five VertexFilter components; VRCFury BlendshapeOptimizer, DeleteDuringUpload | Reclassify destructive build outcomes from excluded to missing. They remain conversion targets with isolated-copy/remapping requirements. | AVATAR_COMPONENT_COVERAGE.json; AvatarComponentCoverageTests.cs prevents treating them as technical exclusions. No new geometry or subtree deletion is executed. |

All implementation/test paths in this table are under UnityPackageImporter/Models
and Tests respectively. The coverage catalog stores the full paths.

## Next dependency order across all cards

1. **Shared authored state and composition:** SDK/MA/VRCF parameter declarations,
   scopes/remaps, menu source/install hierarchy, reactive ordering and provenance.
   Independent VRCFury and Modular Avatar toggle compilers exist; mixed SDK/MA/VRCF
   multi-writer parameter and property arbitration remains next.
2. **Animator semantics:** exact controller/motion references, layers/masks,
   transitions/interruption, blend trees, curve playback, write defaults and state
   behaviors. Compose MA MergeAnimator/MergeBlendTree and VRCF FullController into
   the same semantics instead of three independent conflicting driver systems.
3. **Avatar descriptor and face:** extend authored visemes to duplicate-target
   mixing, jaw modes, eye/blink settings, parameter-only signals, gesture/face
   ownership and tracking handoffs. Current native audio analysis is not promised
   to produce bit-identical VRChat viseme outputs.
4. **Native motion, physics and interaction:** Unity/VRC constraints, PhysBones,
   colliders/contacts, permissions/tags, transforms, scaling and world-lock intent.
   Test authority, reset/removal, remote observation and tracking recovery.
5. **Materials, meshes and build outcomes:** continuous material/shape behavior,
   shader-property mappings, bounds/probes, skin-aware changes, replacement/cuts,
   platform filtering and dependency remapping on isolated output copies.
6. **Remaining feature/action/wrapper cards:** compile against these dependencies;
   distinguish retired no-ops and editor UI from useful built output. Close each
   card with targeted tests, mixed-source fixtures and live evidence, not merely
   parser recognition.

This dependency order does not delete or reduce any card's requested behavior.
Every unimplemented catalog entry remains pending. Earlier waves may unlock
several frameworks, but that is not automatic support credit for those cards.

## Technical limits versus missing work

Literal VRChat services, upload/blueprint identity, social/safety/network protocols,
Unity/VRChat executable code and binary shader execution do not transfer as-is.
Native equivalents or explicit loss diagnostics are required for useful intent.
Editor UI with no built outcome and verified obsolete no-ops need no native UI.
Resonite's exclusive field ownership means duplicate viseme destinations need a
multi-source aggregator; it does not make that authored intent out of scope.

Unimplemented controllers, physics, menus, geometry operations, or parameter
composition are missing work, not proof of a technical impossibility. No arbitrary
editor callbacks, migrations or source-destructive operations are executed.

## Validation boundary

The current follow-up validation and installation checkpoints are recorded above;
AVATAR_CONVERSION_SCOPE.md also retains historical increment results.
Public-API compile checks and pure regression tests
cannot certify native FieldDrive lifecycle, voice source equip/dequip, face
tracking arbitration, remote synchronization or appearance in the real game.
Those require the installed mod, a full restart, and the user's live import.
