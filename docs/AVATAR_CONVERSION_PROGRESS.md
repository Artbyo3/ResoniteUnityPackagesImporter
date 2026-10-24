# Whole-avatar conversion implementation ledger

Local work on 2026-10-24, based on master a93144b. This ledger is not a release
announcement or a claim of complete avatar compatibility. No changes in this
ledger have been confirmed by a live Resonite import.

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

The current validation run and installer result are recorded in
AVATAR_CONVERSION_SCOPE.md. Public-API compile checks and pure regression tests
cannot certify native FieldDrive lifecycle, voice source equip/dequip, face
tracking arbitration, remote synchronization or appearance in the real game.
Those require the installed mod, a full restart, and the user's live import.
