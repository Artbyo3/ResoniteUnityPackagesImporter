# Initial-appearance conversion

The target of this milestone is the deterministic, supported build-time appearance
of an assembled avatar. It is not a reimplementation of VRChat's running Animator,
saved user parameters, or shader rendering. A successful offline test is not live
Resonite confirmation.

## Pipeline and ownership

1. Select authored prefab assemblies while retaining the full dependency library.
2. Compose only reachable source prefabs, reserving their IDs before allocation.
3. Reconstruct objects and execute framework-independent attachment operations.
4. Evaluate supported MA initial reactive appearance.
5. Compile supported VRCFury upload actions and Toggle resting-state defaults.
6. Copy supported MA/VRCFury named blendshape defaults into native renderers.
7. Prepare the humanoid and apply native renderer activation/bounds policies.

The MA evaluator understands Object Toggle, Shape Changer Set, and Material Setter
snapshots, including hierarchy ordering, ancestor conditions, inversion, and a
bounded menu-default subset. The shared native boundary applies Active, Shape,
and exact external material assignments for verified assembled avatar roots.
Material Setter supports skinned renderers and imported static-renderer wrappers,
including inherited slots without serialized source-material entries. Metadata is
staged separately and rollback restores the original list reference and length.
Materials must resolve through the
asset index to an exact external .mat GUID/fileID and a single Material document;
null clears and unsupported subassets are warning-skipped, never substituted.

Destination preflight checks ownership, field drivers and links, unique native
fields, and stale state after asynchronous material loading. Transactions restore
attempted fields and material-wrapper metadata on failure. A plan-local ledger
allows identical repeated framework writes but rejects conflicting later snapshots
rather than guessing cross-framework precedence. Enabled same-value activation
assignments claim their destinations too; inactive fallback-only rules do not.
Unverified external animation or
parameter context still prevents MA reactive evaluation; serialized defaults and
the separate supported synchronization pass remain available.

Destructive conversion is deferred by policy: MA Shape Changer Delete emits
MA_INITIAL_DESTRUCTIVE_SKIPPED, leaves geometry intact, and does not suppress
independent supported Set/Toggle/Material entries. Unknown action modes are not
misclassified as safe destructive skips. Source prefabs and shared meshes are not
edited to approximate an omitted operation.

All references remain within their owning composed avatar. No filename, avatar
brand, package GUID, or accessory-specific fallback grants attachment permission.
Missing dependencies remain reported; validation is not bypassed to enable Create
Avatar. Unrelated malformed library prefabs do not poison an otherwise independent
assembly's composition.

## VRCFury reversible static actions

Managed-reference records execute only when reachable from content or legacy
config.features roots. Record IDs and types are validated, nested references are
bounded, and orphan features cannot create structural or appearance operations.
Nested StateAction records are not counted as independent unsupported features.

The initial-state compiler supports ApplyDuringUpload and a bounded non-slider
Toggle resting-state subset, with ObjectToggleAction, BlendShapeAction,
MaterialAction, and constant AnimationClipAction bindings. ObjectToggle version
migration and opposite/off defaults are distinct from selected/on actions.
Saved parameter values and subsequent custom animator overrides are not evaluated.
Dynamic, platform-dependent, and unsupported actions remain diagnostic; destructive
actions are skipped without deleting avatar content.

Animation clips resolve by exact indexed GUID/fileID, never by clip name or the
first matching document. Supported bindings are exact object activation and named
blendshape weights. A clip must be provably constant: differing keys, nonzero
tangents, nonfinite values, compression/packed samples, additive poses, transforms
(including Euler editor curves), object-reference curves and events are rejected
atomically. Present-but-malformed safety metadata also rejects the clip. This is
not arbitrary first-frame sampling or a runtime animation decoder.

## Blendshape snapshot

MA Blendshape Sync preserves source renderer references, avatar-relative path
fallbacks, source/local shape names, and valid remap curves. Empty local names
inherit the source name. Remaps interpolate and extrapolate linearly. Unity's
percentage-weight domain is retained until the native boundary, including negative
weights and weights above 100.

VRCFury BlendShapeLink uses reachable managed-reference features, versioned legacy
object references or renderer references, base-skin selection within the owning
avatar, explicit includes, excludes, and exact/fuzzy name matching. Ambiguous
matches are not guessed. Initial writes are compiled from one immutable source
snapshot; unsupported chains and conflicting destination writes are diagnosed.
Preflight errors prevent the blendshape write set from applying.

The native adapter resolves named shapes again before mutation and rolls back its
write set on failure. INITIAL_APPEARANCE_ONLY explicitly records that continuous
Animator-curve propagation and native runtime synchronization are not supplied.

## Attachment normalization

VRCFury's feature data.version is separate from the managed-reference table's
references.version. A missing feature version means version zero, not modern
semantics. Serialized enum values and upgrades must be interpreted before planning.
Ordered target alternatives are preserved through planning and runtime selection;
an unavailable first alternative must not invalidate a valid later one. Attachment
alignment preserves the original world transform on unaligned axes.

Planning currently unions dependencies for every viable object alternative. This
can conservatively reject a cycle even when a later bone alternative could avoid
it. CONDITIONAL_TARGET_DEPENDENCIES reports that limitation; runtime-dependent
graph selection is not implemented. Unsupported legacy Auto analysis or scale
policies also remain explicit diagnostics rather than invented behavior.

## Explicit limits

- Destructive MA/VRCFury operations are deferred, not counted as implemented.
  MA Shape Changer Delete requires mesh polygon filtering; assigning a blendshape
  weight is not an implementation of Delete. The avatar can therefore retain
  geometry the Unity build would remove, with an explicit warning.
- Dynamic menus, animation-controller layering, parameter drivers, saved settings,
  arbitrary animated resting states, and continuous linked weights remain separate
  capabilities. Unsupported contexts must be visible in diagnostics.
- Blendshape chains/self-links, conflicting writes, and missing explicit mappings
  are deliberately stricter than upstream's traversal-dependent or no-op behavior.
  These are converter safety boundaries, not claims that the Unity source is invalid.
- Standalone outfit installation and scene imports do not currently execute the new
  prefab initial-appearance passes. They must not be advertised as equivalent.
- Unity lighting probes, shader effects, physics, and full VRChat rendering parity
  are not implied by successful hierarchy or initial-state reconstruction.
- Feature coverage is not a measured percentage. Supported initial subsets do not
  constitute full support for their parent runtime feature or a verified 50% target.

## Reproducible reference baseline

- Modular Avatar: bdunderscore/modular-avatar commit
  f21ad465bb623e841b532a66aa555dbcddf2db47.
  Relevant files: Runtime/ModularAvatarBlendshapeSync.cs,
  Editor/BlendshapeSyncAnimationProcessor.cs, Runtime/ReactiveObjects, and
  Editor/ReactiveObjects/AnimationGeneration/ReactiveObjectAnalyzer.LocateReactions.cs.
- VRCFury: VRCFury/VRCFury commit
  6bd33f4cb453df816c943a335e47f64e6a37e188.
  Relevant files: Runtime/Model/Feature/ArmatureLink.cs,
  Runtime/Model/Feature/BlendShapeLink.cs, and their editor builders/services.

## Validation

Use the Test, Build, and UI audit commands in AGENTS.md. Regression coverage must
assert resulting states as well as diagnostics, preserve source snapshot
immutability, and include malformed inputs, ownership isolation, version migration,
ordered fallback, and non-identity transforms. After installing and fully restarting
Resonite, test one representative assembled avatar through the real importer:
centering, clothing fit and visibility, accessories, named-shape defaults, and
Create Avatar readiness. Offline passes mean ready for live test, not visual parity.
