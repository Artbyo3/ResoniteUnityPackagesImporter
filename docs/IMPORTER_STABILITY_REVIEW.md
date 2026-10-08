# Importer stability review branch

Branch: `codex/importer-stability`, based on public `master` at `7e862c4`.
This is an offline implementation pass awaiting Resonite verification, not a release.
Keep it separate until the checks below pass. The approved UI templates are unchanged.

## Implemented

- Packages containing prefabs use the station selection flow even when they also
  contain demonstration scenes. Those scenes stay in hidden session staging.
  Scene-only packages retain their automatic scene output.
- Prefab/scene component exceptions propagate to the import failure path instead
  of continuing to a success banner. Unsupported components that the importer
  deliberately ignores remain a separate compatibility limitation.
- FBX templates complete native bone initialization before they can be copied.
  The node importer alone does not bind skins; the full native model importer
  does that in a later step. Use its per-mesh bone map, then initialize blendshapes.
  Models without a native bone map require unambiguous exact joint names.
- Full FBX duplicates retain the engine's remapped skin references. The old
  hierarchy-wide name search could replace a bone with an unrelated same-named
  object. Copies are checked for the expected bone count, missing/destroyed
  targets, and references back into the temporary FBX template. An empty bone
  list only passes for a mesh whose asset actually requires no bones.
- Reconstructed skins map only their authored Unity bone references into Assimp
  mesh order. Missing or ambiguous matches fail with an explanation. Each prefab
  owns its material lists; overrides no longer share those mutable lists.
- Shared mesh-readiness code handles cancellation, destroyed targets, missing
  assets and a two-minute timeout. Material/texture loading checks cancellation
  before starting work and before creating engine objects after an await.
  Cancellation is no longer swallowed by material fallback or expression setup.
- Bounds stabilization observes cancellation. Static bounds behavior is retained;
  bone-reference fixes are not presented as proof that all culling bugs are solved.
- The right-hand import draggable uses position weight, consistent with the left
  hand and both feet. Native controller calibration rotation is unchanged because
  it already matches the installed engine's AvatarCreator.
- The extension-registration success message now interpolates its extension.
- Humanoid preparation returns ownership of its temporary movement parent to the
  prefab task. After native avatar creation, both IK reference spaces must point
  to the finished avatar root before that parent can be retired. Its children are
  reparented with the engine's world-transform-preserving operation, and only the
  emptied, recorded parent is destroyed. Authored same-named objects are never
  selected for removal. Changed or externally edited hierarchy fails before
  releasing the avatar. Import previews and ordinary scene imports retain their
  preparation behavior. Existing previously created avatars are not migrated.

## Materials and maintainability

`UnityMaterialData` parses actual YAML, including integer feature switches,
texture scale/offset, colors, shader references and render tags.
`LilToonSettings` contains testable conversion decisions;
`LilToonMaterialConverter` applies them through native material fields.
Asset loading and cancellation remain in the import task.

The reference is [Resonite.UnitySDK at b7b5662](https://github.com/Yellow-Dog-Man/Resonite.UnitySDK/tree/b7b566260a8ebdee6dbdc5cdab32c7b06a87a9f7/Assets/ResoniteSDK/MaterialConverters/Custom/lilToon).
The implemented runtime rules cover:

- Shader-based blend modes, including all Multi modes, rather than guessing from
  filenames or the presence of a cutoff property.
- Normal-map feature switches, second-map fallback, UV selection and native
  normal-map import settings.
- Emission enable/blend/fluorescence rules and RGB-by-alpha texture processing.
- Additive unmasked MatCap, with the SDK's exclusions for other modes.
- Outline enablement, width, lighting, tint and mask.
- Separate shadow-border occlusion and shadow-strength ramp masks.
- A CPU adaptation of [lilToon's ramp baker](https://github.com/lilxyzw/lilToon/blob/72fc09625b24c9a750591c286e9192512a5177a1/Assets/lilToon/Shader/ltspass_bakeramp.shader),
  including second/third shadow alpha, strength, border color and the SDK's
  vertical white-to-shadow mask expansion. Generated files are content-addressed.

The old approximate compositor and ramp generator were removed. **This changes
unsupported layered materials:** main layers, HSVG/color adjustment, gradation
and alpha masks fall back to the base texture/tint with a warning. The Unity-editor
baker cannot run directly in this mod. Emission blend masks/main-texture blending
also warn and fall back to the source emission texture. Second emission, advanced
MatCap, reflection and other unmapped lilToon effects are not full-fidelity conversions.
Shader defaults absent from serialized materials cannot all be recovered when the
shader is missing. No visual parity with the SDK is claimed without comparison.

Upstream MIT notices are in `THIRD_PARTY_NOTICES.md`, copied alongside build output.

This pass does not introduce a universal converter/plugin framework. Future MA and
VRCFury adapters should interpret their own settings and reuse only equivalent
operations. Begin with an actual second use case, rather than abstracting unknown
future components. No new MA/VRCFury component support is claimed here.

### Future animation conversion boundary (design only)

Consider both Modular Avatar and VRCFury before implementing this boundary.
Their components can modify or generate the eventual Animator configuration;
reading only the controller referenced by the original prefab is insufficient.
The current component index does not yet recognize MA Merge Animator, and it
does not provide VRCFury conversion.

Use framework-specific readers for detection, dependencies and build semantics,
feeding shared behavior operations only where their meanings agree. Preserve
parameter scopes/defaults, object identity and binding-path changes, layer order,
merge/replace rules, and conditions when they affect a supported behavior. Do not
infer an Animator's effective default from a menu label or a Boolean parameter in
isolation. Build a supported behavior plan, not an unbounded reimplementation of
Unity's entire Animator or the upstream editor pipelines.

Start with small fixtures from both frameworks for the same visible behavior,
such as clothing visibility and its default state. Compare against each
framework's actual Unity build output before calling those cases supported.
Unresolved dependencies and unsupported animation constructs need explicit
reporting. Considering both frameworks now reduces rework; it cannot guarantee
that no changes will be needed later. Reassess this design before implementation.

Official references checked for this planning note:

- [MA Merge Animator](https://modular-avatar.nadena.dev/docs/reference/merge-animator):
  binding-path modes, layer priority, replacement and Write Defaults handling.
- [MA Parameters](https://modular-avatar.nadena.dev/docs/reference/parameters):
  parameter scope, renaming and default overrides.
- [VRCFury Full Controller](https://vrcfury.com/components/full-controller/):
  controller/menu/parameter merging, path rewriting and parameter isolation.
- [VRCFury Toggle](https://vrcfury.com/components/toggle/):
  authored toggle defaults and rest-pose options. These are not equivalent to
  blindly driving all related objects from one initially false value.

## Offline verification

The subsequent [maintainability pass](CODE_CLEANUP.md) separates mod registration,
Harmony interception, package coordination and humanoid rig preparation. It
removes only confirmed unused fields, root-argument plumbing and the oversized
model class catalogue. Import scheduling and rig behavior are preserved.

Validated on this branch: Release build succeeded with zero warnings/errors;
107/107 C# regressions and 16/16 Python tests passed. All seven engine
reflection signature checks passed against the installed engine assemblies.

Run from the repository root:

```powershell
dotnet build UnityPackageImporter/UnityPackageImporter.csproj -c Release
dotnet run --project Tests/Importer.RegressionTests.csproj -c Release
python -m unittest discover -s Tests -p "test_*.py"
```

The new regressions exercise real production material parsing/rules, decoded PNG
pixels and mask direction, concurrent cache writes, authored bone ordering,
ambiguity rejection and existing import-lifetime races. Engine API metadata checks
verify signatures without starting Resonite. These checks cannot validate rendering,
tracking, or the engine's live transfer queue.

The movement-parent follow-up adds seven regressions for ownership, both IK
reference-space handoffs, cross-avatar isolation, changed/destroyed hierarchies,
cycles, repeated completion and failed promotion. Those tests exercise the
production lifecycle policy with a hierarchy double. They do not execute native
transform math or prove rendered/posed equivalence in Resonite. Native metadata
inspection confirmed that `VRIKAvatar.Setup` moves both solver spaces to its own
avatar root; the implementation also checks those actual targets at runtime.

### Post-install skin initialization correction

The first review build produced invisible avatar geometry in a user test. The
native engine's import sequence established a concrete regression: calling
`ImportNode` creates skinned renderers but does not run the later `SetupBones`
step. Removing the old name-based reconstruction without adding that native
finalization left the template and its copies with empty bone lists. The old
copy check iterated existing references and therefore incorrectly accepted zero.

Initialization now happens once on the template, using
`ModelImportData.TryGetBoneMapping` followed by `SetupBones` and blendshape setup.
The exact native method signature is checked offline. Six additional regressions
cover native mapping precedence, incomplete maps, ambiguous legacy names, empty
and partial binding lists, invalid copy references, and boneless blendshape meshes.

This fixes the confirmed missing-initialization path in code. A fresh import is
still required to verify rendered geometry; existing imported objects are not
repaired automatically. It is not evidence that the older distance-dependent
culling, scale, or hand-orientation reports are resolved.

## Required before merging

The subsequent [offline bug audit](BUG_AUDIT_2026-10-08.md) records three further
follow-ups: texture-only companion recovery, dependency-aware outfit revision
detection, and concurrent installs from separate stations. This was an
inspection-only pass with no production changes; the note distinguishes
executable helper reproductions from engine paths that still need live tests.

### Follow-up observations — 2026-10-08

The user's acceptance criterion is fidelity to the avatar's authored Unity
configuration wherever it can be reproduced. Preserve serialized appearance and
visibility, and reproduce supported Animator defaults from their actual logic.
A generated Resonite menu must not arbitrarily replace those values. When
Animator behavior is unsupported, retain the available imported state and report
the gap rather than inferring state from menu labels or parameter names alone.
The user also reports improved but still insufficient lilToon appearance compared
with the official Unity SDK; the material limitations above remain open.

- A brief user test no longer reproduced disappearing geometry on the tested
  avatar. This is a limited pass, not validation across all affected models.
- **Root-level duplicate output remains open.** The user reports that at least
  one later test still produced an extra avatar outside the station. The earlier
  mixed-scene ownership change addresses a known path, not every possible cause.
  Identify the remaining path from a reproducible package and live hierarchy in
  a future session; do not infer that it is the same scene-commit bug. Further
  investigation is deferred at the user's request.
- The reported general blendshape reset was not confirmed on retest. Do not
  treat it as an established regression or conflate it with face-tracking
  drivers. Animator-controlled defaults require a separate investigation.
- **Lower priority: expression-menu initialization changes clothing visibility.**
  The user can restore the clothing through the generated menu. Code inspection
  confirms that grouped toggles create a `ValueMultiDriver<bool>` without setting
  its initial value, then force-link the targets' active fields. The native
  driver's change handler writes that initially false value to every valid
  target. This is a concrete reset path, but the affected live targets have not
  been inspected. Preserve existing states when creating controls; mixed states,
  inverted Animator conditions and parameter defaults must not be collapsed into
  an assumed shared Boolean. The current heuristic menu mapping does not evaluate
  the Animator controller, and parsed expression parameters are discarded by the
  menu-building caller. Full Animator default-state support is not implemented.
  This item is deferred at the user's request; no code fix has been applied.
- The importer-generated movement wrapper was retained inside completed avatars.
  It came from humanoid preparation, and finalization released the complete
  envelope without retiring it. The ownership/finalization change above addresses
  that path in the review build; fresh in-game validation is still pending.

### Live verification checklist

1. Import a mixed scene/prefab package. Only the station preview should be visible.
   Close it and check for leftover scenes. Separately verify scene-only output.
2. Compare the same model imported natively and through this branch. Check head/body
   visibility at different viewing distances, after switching variants, after
   Create Avatar, and after saving/respawning.
3. Repeat the previously oversized/collapsed model cases. No arbitrary armature
   scale clamp was introduced. Bind poses, mesh transforms and authored prefab
   scales still need source-level comparison if those cases remain broken.
4. Create/equip an avatar and check both hands, palms, thumbs and controller/tool
   anchors. The small draggable correction does not establish a fix for every
   reported hand twist. First-person head hiding remains a separate investigation.
   Also verify the import-only movement parent is absent after creation, while
   model position, rotation, scale, clothing and skin references are unchanged.
   Test an authored nested transform, preview switching and station deletion;
   moving the finished avatar must still move its entire rig normally.
5. Compare representative lilToon materials with the SDK: opaque/cutout/transparent,
   shadows, normals, emission, MatCap and outlines. Include layered makeup and
   alpha-masked clothing so the documented fallback tradeoff is visible.
6. Install clothing, switch color variants, toggle it, close the station, then save
   and respawn the avatar. Confirm installed output survives while temporary
   content is removed.
7. Close/delete a station during model parsing, mesh readiness, texture import and
   companion-package loading. No new objects should appear afterward. Native
   operations without cancellation APIs are awaited and their results discarded;
   this is not a claim that all shared engine asset transfers can be aborted.
8. Verify malformed/failed model imports show failure, not completed partial output.
   The previously observed binary-FBX parser rejection still requires a source
   fixture and native-import comparison; it has not been fixed speculatively.
9. Check nested prefab/scene overrides and source-object identity against authored
   Unity data, including non-humanoid imports. Some handlers still provide only
   partial translation. Existing humanoid guards and expression-menu handling
   do not establish complete support for every Unity or VRChat component. These
   are retained verification items from the removed legacy TODO, not new claims
   of implementation or confirmed regressions.

Single-option navigation is already disabled by the existing station code.
The remaining rendering, scale and tracking questions should be resolved with
live evidence before merging, installing for general use, or updating public
compatibility claims.
