# Avatar conversion scope: VRChat, Modular Avatar, and VRCFury

Inventory date: 2026-10-24. Planning baseline, not a parity claim.
Branch: `feature/avatar-conversion-scope`, created from local `master`.

## Decision summary

For reproducible numeric component counts and per-type code/test evidence, see
AVATAR_COMPONENT_COVERAGE.md and its source AVATAR_COMPONENT_COVERAGE.json.
Its concrete-type denominator differs intentionally from grouped rows here.

The expanded goal is to preserve avatar **intent and usable behavior in Resonite**,
not run Unity, the VRChat client, MA, or VRCFury inside Resonite. An editor build
component can produce essential runtime behavior: editor-only does not mean its
output is disposable. Menus, parameters, gestures, dynamics, constraints and
expression animations are conversion work, not blanket exclusions.

This is proposed scope/order, not authorization for destructive conversion,
package script execution, live template edits, installation or publication.

| Code | Meaning | Policy |
| --- | --- | --- |
| K | Bounded implementation exists locally | Preserve limits; native behavior still needs live evidence. |
| P | Portable intent; implementation pending | Expanded scope, not implemented support. |
| E | Editor tooling or obsolete no-op | Do not reproduce tooling; retain useful authored output. |
| X | Platform-specific service/exact contract | No literal translation; disclose loss or explicitly different substitute. |
| D | Destructive/information-losing processing | Deferred; exact losses and explicit approval required. |
| A | Sensitive, permission-dependent or costly | Separate opt-in design; no automatic activation. |

Codes combine: K/P means a bounded part exists, not the rest. P/D separates safe
and destructive modes. Missing support must stay visible in import reports.
Native targets below are design proposals, not verified API availability.

## Product acceptance target: the whole avatar feels intact

The user-facing goal is recognition: someone imports their VRChat avatar and
feels that the same avatar, including its familiar wardrobe and controls, is now
in Resonite. A good-looking static reconstruction is not this milestone.

If a supported source avatar contains 30 clothing items controlled by base
VRChat menus/controllers, MA, VRCFury, or a combination of all three, the native
result must retain all 30 items and their authored selection behavior. The user
must not manually rebuild the menu, wire parameters, repair clothing links, or
understand which framework originally generated a control.

Familiar behavior is the contract, not a literal copy of VRChat's interface or
runtime. Native controls may look different, but labels, grouping, available
choices, defaults and outcomes must remain recognizable and usable.

### Wardrobe acceptance fixture

Use a versioned source fixture with at least 30 clothing items and an explicit
expected-state table. Exercise base-only, MA-only, VRCFury-only and mixed-source
composition; all must use the same native binding/state implementation.

| Required outcome | Acceptance evidence |
| --- | --- |
| Complete wardrobe and controls | Every source clothing item and reachable authored control has a resolved native identity; no silent omissions or arbitrary menu-size cap. |
| Familiar menu navigation | Names, grouping, order, icons where available, submenus and control types retain their authored intent; no manual reconstruction. |
| Correct selection rules | Independent toggles remain independent; exclusive outfits, multi-object selections, sliders and puppet controls produce their expected states. |
| Complete visual change | Visibility, body-hiding shapes, fit/correction shapes, linked clothing shapes, materials and animated properties change together as authored. |
| Correct initial and restored state | Authored defaults, resting states and deliberately saved native state have distinct, tested behavior. |
| Mixed-framework composition | Shared and remapped parameters, merged controller paths and references resolve consistently across base VRChat, MA and VRCFury. |
| Transitions and repeatability | Switching repeatedly, interrupting transitions and returning to prior outfits restores expected values without stale materials, shapes or hidden objects. |
| Avatar remains functional | Wardrobe switching does not break rigging, face/hand expressions, dynamics, constraints or tracking. |
| Owner and observer agreement | A second user observes the expected synchronized outfit; local-only behavior stays local and permitted interactions respect authority. |
| Native lifecycle | Wear, remove, re-wear, reset and saved-state reload behave according to the defined policy without manual repair. |
| Practical usability | Measure control responsiveness and resource cost on the fixture; agreed budgets must pass in the real mod, not only in a preview. |

Compare source behavior against native behavior for every control and its
affected properties, including representative combinations, conflicts and
transition sequences. Count-based checks alone are insufficient: importing 30
meshes with 30 nonfunctional menu entries does not pass.

This whole-avatar fixture is the first product milestone spanning roadmap Waves
1 through 3 plus the constraint, dynamics and rendering dependencies it actually
uses. It is not postponed until every optional integration is implemented.
Prioritize complete vertical slices of wardrobe behavior over accumulating
unconnected component parsers.

If a dependency prevents that experience, report the affected controls/items and
classify the result as partial or blocked, not faithful conversion. Existing
scope exclusions and permission/destructive gates still apply; this target does
not promise exact rendering or simulation for unsupported systems and does not
authorize deletion or external integration activation.

## Sources and inventory boundary

### First runtime wardrobe slice (local implementation)

The first vertical slice compiles independent VRCFury Boolean Toggles in composed prefabs into
native wardrobe submenus and native state/driver components, with exact source
object identities rather than clothing-name guesses. Supported actions are
explicit object on/off, constant blendshape changes and constant animation-clip
visibility/blendshape bindings with authored off-state
restoration. Menu paths, labels and authored defaults are retained, without an
arbitrary wardrobe-size cap. Structural MA/VRCFury composition is allowed only
when its independent-runtime preconditions hold.
Toggle icons are explicitly reported as unconverted in this slice. Scene-source
runtime compilation remains pending; this path consumes composed prefab YAML.

This is **not** the whole-avatar acceptance milestone. Base Animator-driven
and arbitrary runtime animation controllers remain uncompiled. Base menu controls
without an exact compiled binding are displayed disabled and reported, instead
of guessing targets by object names. Arbitrary multi-state controllers, saved parameter
persistence across sessions, shared-property arbitration, exclusive outfit groups,
time-varying animation clips, transitions, sliders, platform/local/global conditions, and mixed
runtime controllers remain pending. Unsupported or conflicting wardrobe context rejects the entire
wardrobe plan, rather than silently wiring only its easiest actions.

Drivers are attached after native avatar creation, preflight existing field
ownership and roll back on failure. Native per-copy state is not a claim of
VRChat saved/unsaved restoration or re-wear lifecycle parity. Pure regression
fixtures and compilation cannot establish synchronization, wearer interaction,
visual correctness or lifecycle behavior: these require the real mod's live
test after installation and a full Resonite restart.

- MA: bdunderscore/modular-avatar at f21ad465bb623e841b532a66aa555dbcddf2db47;
  concrete authored Runtime components, including reactive/vertex-filter types.
- VRCFury: VRCFury/VRCFury at 6bd33f4cb453df816c943a335e47f64e6a37e188;
  every Runtime/Model/Feature and Runtime/Model/StateAction file, plus wrappers.
  Includes legacy aliases and infrastructure, not just visible menu entries.
- Both matched upstream default-branch heads queried on 2026-10-24. This is not
  coverage of every historical/future release or third-party extension.
- VRChat: documented SDK3 avatar allowlist, descriptor, expressions, layers,
  dynamics and state behaviors on the audit date. Cached SDK 3.10.5 is older
  evidence, not a schema oracle for newer documented types such as VRCRaycast.
- Unity/third-party families are included where avatar dependencies. Unknown
  managed types, SDK2 systems and custom plugins need separate versioned adapters.

Primary references (tables also use inspection of pinned source and local code):

- [VRChat allowlist](https://creators.vrchat.com/avatars/whitelisted-avatar-components/whitelisted-avatar-components/)
- [VRChat components](https://creators.vrchat.com/avatars/avatar-components/)
- [VRChat state behaviors](https://creators.vrchat.com/avatars/state-behaviors/)
- [VRChat parameters](https://creators.vrchat.com/avatars/animator-parameters/)
- [VRChat expressions](https://creators.vrchat.com/avatars/expression-menu-and-controls/)
- [VRChat PhysBones](https://creators.vrchat.com/common-components/physbones/)
- [VRChat contacts](https://creators.vrchat.com/common-components/contacts/)
- [VRChat constraints](https://creators.vrchat.com/common-components/constraints/)
- [VRChat raycast](https://creators.vrchat.com/avatars/avatar-components/raycast/)
- [MA reference](https://modular-avatar.nadena.dev/docs/reference)
- [Pinned MA source](https://github.com/bdunderscore/modular-avatar/tree/f21ad465bb623e841b532a66aa555dbcddf2db47/Runtime)
- [VRCFury components](https://vrcfury.com/components/)
- [VRCFury other components](https://vrcfury.com/components/other/)
- [VRCFury actions](https://vrcfury.com/actions/)
- [Pinned VRCFury source](https://github.com/VRCFury/VRCFury/tree/6bd33f4cb453df816c943a335e47f64e6a37e188/com.vrcfury.vrcfury/Runtime)

## Implemented locally today

| Area | Local evidence | Limit |
| --- | --- | --- |
| Composition/ownership | AvatarStateReconstructor, AvatarOwnershipValidator, AvatarPlanComposer | Referenced assets/unambiguous ownership required, not Unity build processing. |
| Native creation/viewpoint | NativeAvatarCreation, AvatarPackageIndex | Complete single humanoid rig/IK and readiness, not descriptor parity. |
| MA structure | ModularAvatarPlanAdapter | Merge Armature/Bone Proxy supported subset. |
| VRCFury structure | VrcFuryPlanAdapter, VrcFuryArmatureSemantics | Armature Link subset; unsupported versions/options diagnosed. |
| MA initial appearance & wardrobe | ModularAvatarInitialAppearance, ModularAvatarWardrobeCompiler, AvatarWardrobeRuntime | Object Toggle, Shape Changer Set, Material Setter initial snapshot and independent Toggle runtime under MenuInstaller/MenuItem/MenuGroup/Parameters. |
| VRCFury initial appearance | VrcFuryInitialAppearance and adapter | Apply During Upload / eligible Toggle resting states; ObjectToggleAction, BlendShapeAction, MaterialAction, bounded static AnimationClipAction. |
| Blendshape links | BlendshapeInitialAppearance, BlendshapeInitialAppearanceAdapter | Initial weights and continuous ValueCopy<float> sync for unique direct links; remap-curve runtime and multi-source blending pending. |
| Geometry/materials | Mesh import, static/skinned renderers, material adapters | Selected formats/properties/shaders, not full Unity rendering. |
| Unity RotationConstraint | FrooxEngineRepresentation/GameObjectTypes/RotationConstraint.cs | Parsed/traversed; code explicitly does not translate rotation/source weights. Not runtime support. |

Recognition/indexing is not runtime conversion. No support percentage follows from
counting component names. See [prior readiness audit](INITIAL_APPEARANCE_READINESS.md)
for earlier evidence/limits; it is not live proof for expanded scope.

## 1. Base VRChat: descriptor, rig, expressions and animation

| Component / functionality | Class | Native intent / exclusion |
| --- | --- | --- |
| VRCAvatarDescriptor: root/view position | K/P | Existing creation/viewpoint; remaining descriptor fields need adapters. |
| Humanoid definition, bone mapping, T-pose, scale | K/P | Rig/calibration; axes, missing bones and retargeting. |
| Generic/non-humanoid avatars | P | Preserve assets; native setup separate from humanoid readiness. |
| Lip sync: jaw bone/blendshape, visemes, mesh selection | P | Voice/viseme drivers, remapping and missing-shape diagnostics. |
| Eye look: bones, rotations/ranges, confidence/excitement | P | Native gaze; disclose approximation. |
| Eyelids: bones/shapes, blink, look up/down | P | Blink/gaze composition and single-writer ownership. |
| Descriptor collider settings | P/A | Native collision intent and explicit interaction policy. |
| Descriptor custom layers/animation overrides | P | Shared animation representation. |
| Expressions: Button, Toggle, SubMenu | P | Native controls, release, cycles, icons/references. |
| Expressions: Two Axis Puppet, Four Axis Puppet, Radial Puppet | P | Continuous bounded native controls. |
| Menu labels/icons/order/nesting | P | Preserve metadata, not VRChat menu implementation. |
| VRCExpressionParameters: Bool/Int/Float/defaults | K/P | Some defaults feed initial appearance; runtime state store pending. |
| Saved parameters/restoration | P/A | Explicit persistence; initial defaults are not restored state. |
| Synced versus local parameters | P/A | Owner-authoritative native sync, not VRChat bitstream. |
| Playable Base, Additive, Gesture, Action, FX | P | Compiler preserving masks, order, blending/transitions. |
| Special Sitting, TPose, IKPose | P | Native poses without unexpected tracking/calibration override. |
| Animator states/sub-state machines, entry/exit/Any State | P | Versioned deterministic state evaluator. |
| Conditions/triggers, exit time, interruption/duration | P | Explicit ordering/time semantics. |
| Weights, AvatarMasks, additive/override, synced layers | P | Property/bone composition. |
| 1D/2D/direct/nested BlendTrees, motion time/speed | P | Shared motion evaluator, not static parsing. |
| AnimationClip object/numeric curves | K/P | Bounded initial samples; playback/interpolation/bindings pending. |
| Write Defaults, reset/restoration | P | Property ownership/baseline restoration. |
| Animation events/custom StateMachineBehaviours | X/A | Never execute code; allowlisted declarative equivalents only. |
| Gesture parameters/weights | P | Native hand-state index/weight adapter. |
| Viseme/voice-level parameters | P/A | Voice-derived signals and sync policy. |
| Velocity X/Y/Z, angular velocity, upright, grounded, seated, jumping | P | Locomotion signals, ranges/units/authority. |
| TrackingType, VRMode, IsLocal, AFK, IsAnimatorEnabled | P | Available native state; disclose differences. |
| ScaleModified, ScaleFactor/inverse, eye-height signals | P | Bounded native scale/height. |
| AvatarVersion, IsOnFriendsList, Earmuffs, client/account flags | X/P | No exact contract; explicit native meaning or unsupported. |
| Scaling/eye-height limits/per-avatar settings | P/A | Native calibrated scale, not copied client limits. |
| VRCPerPlatformOverrides, VRCAccessoryPerPlatformOverrides | P/D | Explicit target policy; preserve other variants. |
| Impostors/fallback/performance rank/shader blocking | X | VRChat services/policies, not native optimization. |
| SDK upload/build target/blueprint/ownership metadata | E/X | Provenance only, never upload/claim ownership. |

## 2. Base VRChat: SDK state behaviors and components

| Component / behavior | Class | Native intent / exclusion |
| --- | --- | --- |
| VRCAnimatorLayerControl | P | Weight blend/interruption. |
| VRCPlayableLayerControl | P | Playable weights/durations. |
| VRCAnimatorLocomotionControl | P/A | Locomotion override with consent/restoration. |
| VRCAnimatorTemporaryPoseSpace | P | Pose space, exit/interruption cleanup. |
| VRCAnimatorTrackingControl | P/A | Per-channel handoff, protect tracking/recovery. |
| VRCAvatarParameterDriver: Set/Add/Random/Copy/remap/conversion | P | Ordered writes, local-only and owner-generated randomness. |
| VRCAnimatorPlayAudio | P/A | Native playback, clip/order and volume limits. |
| Debug strings/behavior visualizers | E | Diagnostics, not Unity debugger. |
| VRCPhysBone | P/A | Roots/ignore/endpoints, forces/curves, limits, collision, stretch/squish, grab/pose and generated state; approximate until measured. |
| VRCPhysBoneCollider | P/A | Shapes/offsets/radii, inside/outside and scale; no silent cross-user interaction. |
| VRCContactSender | P/A | Typed volumes/shapes/transforms/tags. |
| VRCContactReceiver | P/A | Constant/on-enter/proximity, self/others/local filters and parameter authority. |
| VRCAimConstraint | P | Sources, axes, offsets, activation/world-up. |
| VRCLookAtConstraint | P | Look-at/up-vector/weights. |
| VRCParentConstraint | P | Multi-source position/rotation, offsets/rest. |
| VRCPositionConstraint | P | Masks, offsets/rest, freeze/activation. |
| VRCRotationConstraint | P | Masks, weighted rotations/offsets. |
| VRCScaleConstraint | P | Weights, offsets, singular/nonuniform scale. |
| Common VRCConstraint settings | P | Spaces, solve order, locked/rest state, freeze-to-world/rebake; test cycles. |
| VRCHeadChop | P | Local first-person visibility/scale, preserve remote/mirror. |
| VRCRaycast | P/A | Query/result-transform, layers, misses and cost caps. |
| VRCIKFollower (deprecated) | P | Explicit legacy attachment migration. |
| VRCSpatialAudioSource | P/A | Spatial audio/attenuation, not identical engine. |
| VRCStation | P/A | Seat/pose, enter/exit authority and escape. |
| VRCPipelineManager / serialized PipelineManager | E/X | Provenance only, not native ownership. |
| PhysBone/contact permissions and VRChat global tags | X/P/A | Literal protocol excluded; explicit native consent/tag adapter. |

## 3. Unity and third-party avatar dependencies

Mesh import alone does not establish component/curve support.

| Component / asset family | Class | Native intent / exclusion |
| --- | --- | --- |
| GameObject/Transform, activeSelf, hierarchy, prefab variants/overrides | K/P | Existing structure, effective versus local active state. |
| MeshFilter/MeshRenderer/SkinnedMeshRenderer | K/P | Geometry; bounds, skinning/bindposes/properties need validation. |
| Mesh submeshes, skin weights, blendshapes/weights | K/P | Identity/mappings; unsupported encodings explicit. |
| Standard, Poiyomi, lilToon and other materials/textures | K/P | Selected adapters only; per-property fidelity report. |
| HLSL/binaries, grab-pass/stencil/geometry/tessellation | X/P/A | No literal shader execution; selected native effects separately. |
| Animation/Animator/RuntimeAnimatorController/OverrideController | P | Compiler with legacy clips/override resolution. |
| Unity AimConstraint | P | Shared adapter with Unity-specific semantics. |
| Unity LookAtConstraint | P | Up-vector/roll, not simple look-at. |
| Unity ParentConstraint | P | Offsets/masks/references. |
| Unity PositionConstraint | P | Shared native constraint adapter. |
| Unity RotationConstraint | P | Current class is not a functioning converter. |
| Unity ScaleConstraint | P | Nonuniform-scale tests. |
| AudioSource/AudioClip | P/A | Assets/settings/playback/control. |
| ParticleSystem/ParticleSystemRenderer | P/A | Per-module: emission/shape, lifetime/size/color, forces/collision, subemitters, trails, texture-sheet animation, renderer modes. |
| LineRenderer/TrailRenderer | P | Lines/trails, gradients/width/lifetime/texture. |
| Light/FlareLayer | P/A | Illumination; flares separately, cost caps. |
| Camera/RenderTexture | P/A | Explicit opt-in capture/display, no copied VRChat trust rules. |
| Collider: Box/Sphere/Capsule/Mesh/Terrain/Wheel/base | K/P/A | Limited mesh-collider path; shapes/trigger/layers separate; inapplicable shapes diagnosed. |
| Rigidbody | P/A | Gravity/kinematic/mass/collision, not identical physics. |
| Joints: Fixed/Hinge/Spring/Character/Configurable/base | P/A | Constraints, limits/break behavior. |
| Cloth | P/A | Native redesign, not Unity solver parity. |
| FinalIK AimIK/BipedIK/CCDIK/FABRIK/FullBodyBipedIK | P/A | Intent-level IK; avoid stacking solvers over native tracking. |
| FinalIK Grounder/LimbIK/RotationLimits/ShoulderRotator/TwistRelaxer/VRIK | P/A | Per-solver/helper targets/update order; no source runtime execution. |
| DynamicBone/DynamicBoneCollider (legacy) | P/A | Versioned secondary-motion adapter. |
| EditorOnly tags/gizmos/inspectors/preview helpers | E/D | Ignore tooling; object omission needs disclosed output policy, never source deletion. |
| Arbitrary MonoBehaviour/DLL/editor callbacks/AssetPostprocessor | X/A | Never execute package code; unknown types reported. |
| Udon/UdonSharp/world SDK | X | Separate world-conversion scope. |
| SDK2/legacy systems/external plugins | P/X | Outside SDK3 baseline; version/inventory before adapter. |

## 4. Modular Avatar complete authored-component inventory

Build instructions require translation even though MA is absent from uploaded
avatars. Names below are source types, not inferred marketing features.

| Component / source type | Class | Native intent / exclusion |
| --- | --- | --- |
| ModularAvatarMergeArmature | K/P | Supported merge subset; target, prefix/suffix, skin/reference rewrites. Editor armature lock UI is E. |
| ModularAvatarBoneProxy | K/P | Supported attachments/offsets; spaces, scale and ownership validation. |
| ModularAvatarOutfitRoot | K/P | Identification/selection metadata, not dressing runtime by itself. |
| ModularAvatarBlendshapeSync | K/P | Initial weights and continuous ValueCopy<float> sync for unique direct links; runtime remap curves and multi-source blending pending. |
| ModularAvatarMergeAnimator | P | Controller merge, path remap, priority/layer semantics. |
| ModularAvatarMergeBlendTree / Merge Motion | P | Shared motion/tree compilation. |
| ModularAvatarParameters | K/P | Scoped remaps, internal parameters and defaults for initial appearance and independent toggles; saved/synced persistence and multi-writer arbitration pending. |
| ModularAvatarMenuInstaller | K/P | Root/hierarchy installation for independent MA toggle/submenu trees; external menu assets and explicit install targets pending. |
| ModularAvatarMenuItem | K/P | SubMenu hierarchy and independent Toggle controls with icons and defaults; momentary Button, puppets and multi-value controls pending. |
| ModularAvatarMenuGroup | K/P | MenuSource children/otherObjectChildren hierarchy traversal for installed MA toggle menus. |
| ModularAvatarMenuInstallTarget | P | Targeted insertion, cycle-safe resolution. |
| ModularAvatarObjectToggle | K/P | Initial snapshot and independent Toggle reactive runtime with baseline restoration; multi-state conditions pending. |
| ModularAvatarShapeChanger: Set | K/P | Initial weights and independent Toggle reactive runtime with baseline restoration; multi-state conditions pending. |
| ModularAvatarShapeChanger: Delete | D | Geometry deletion is not weight assignment; retain source. |
| ModularAvatarMaterialSetter | K/P | Initial slot replacement and independent Toggle reactive runtime with baseline restoration; multi-state conditions pending. |
| ModularAvatarMaterialSwap | P | Substitution/conditions, not covered by Material Setter support. |
| ModularAvatarReactiveMove | P | Reactive constraints, channels, world-fix and transitions. |
| ModularAvatarMeshCutter | D | Geometry removal; approval/remapping design required. |
| VertexFilterByAxisComponent | D | Cutter selection dependency; recognize/report, do not cut. |
| VertexFilterByBoneComponent | D | Bone-weight selection. |
| VertexFilterByMaskComponent | D | Texture-mask selection/missing asset policy. |
| VertexFilterByShapeComponent | D | Blendshape-based selection. |
| VertexFilterByUVTileComponent | D | UV-tile selection. |
| ModularAvatarConvertConstraints | P | Interpret Unity intent directly; VRC intermediate unnecessary. |
| ModularAvatarFloorAdjuster | P | Footwear/floor calibration; multiple-component conflicts. |
| ModularAvatarGlobalCollider | P/A | Native dynamics/contacts; VRChat tags not universal protocols. |
| ModularAvatarMeshSettings | P | Probe/anchor/bounds intent, lighting differences disclosed. |
| MAMoveIndependently | E/P | Authoring relative-placement helper; preserve authored transforms, not editor live-lock machinery. |
| ModularAvatarMoveTo | P | Staged reparent/reference remap and cycle checks. |
| ModularAvatarPBBlocker | P | Native chain exclusion, no component deletion needed. |
| ModularAvatarPlatformFilter | P/D | Resonite target, not automatically PC/Android; explicit output exclusion, immutable source/dependency reports. |
| ModularAvatarRemoveVertexColor (file RemoveVertexColor.cs) | D | Lossy channel stripping; preserve colors and DontRemove overrides. |
| ModularAvatarRenameVRChatCollisionTags | P | Namespaced native mapping, no VRChat interoperability claim. |
| ModularAvatarReplaceObject | D | Removes/replaces hierarchy identities/references; defer, no guessed replacement. |
| ModularAvatarScaleAdjuster | P | Skin-aware compensation, not plain slot scale. |
| ModularAvatarVisibleHeadAccessory | P | Local first-person visibility, preserve mirror/remote. |
| ModularAvatarWorldFixedObject | P/A | World anchoring, authority/cleanup/permissions. |
| ModularAvatarWorldScaleObject | P | Effective size across avatar scale changes. |
| ModularAvatarMMDLayerControl | X/P | VRChat world protocol excluded; usable animation intent separately. |
| ModularAvatarVRChatSettings | X | Current MMD support flag has no literal native contract. |
| ModularAvatarSyncParameterSequence | X | VRChat ordering protocol unnecessary; declarations/defaults still preserved. |

### MA infrastructure, not additional avatar features

| Source family | Class | Treatment |
| --- | --- | --- |
| Activator, AvatarActivator, TestComponent | E | Legacy activation stubs/test inspector; never execute cleanup callbacks. |
| AvatarTagComponent, ReactiveComponent, MenuSourceComponent | E/P | Base schemas; interpret concrete entries, no native Unity bases. |
| AvatarObjectReference, IHaveObjReferences, VirtualMenuAPI, IModularAvatarMaterialChanger, IMeshSelectorBehavior | E/P | Resolver/schema contracts, not separate runtime features. |
| ArmatureAwase classes, move-independent manager, edit-mode PhysBone manager | E | Unity preview/jobs/memory/transform locks. |
| MAAssetBundle, UI attributes, menus, runtime utilities/version helpers | E | Tool resources/infrastructure. |
| NDMF passes/manual processing/simulator/build reports/third-party extensions | E/X | No plugin execution; new concrete types require separate inventory. |

## 5. VRCFury complete feature-model inventory

VRCFury uses managed references. One wrapper can contain multiple features and
nested actions. Legacy types must be decoded by version; never call upstream
migration code inside Resonite.

| Feature model | Class | Native intent / exclusion |
| --- | --- | --- |
| ArmatureLink | K/P | Supported merge/attachment subset; unsupported skin/scaling/link modes separate. |
| Toggle | K/P | Resting/default appearance and bounded independent native Boolean controls; saved/exclusive/transitions/security/reset pending. |
| FullController | P | Shared controller/menu/parameter merge, path/conflict rules. |
| GestureDriver | P | Native hand triggers/weights/composition. |
| SenkyGestureDriver | P | Specialized eye/mouth/ear expression states. |
| Puppet | P | Continuous 1D/2D authored stops/defaults. |
| Modes | P | Legacy exclusive modes; normalize without executing migration. |
| ApplyDuringUpload | K/P/D | Supported reversible initial actions only; each other action keeps its own classification. |
| ObjectState | P/D | Legacy ACTIVATE/DEACTIVATE reversible; DELETE destructive. No implicit migrations. |
| BlendShapeLink | K/P | Initial weights and continuous ValueCopy<float> sync for unique direct links; multi-source blending pending. |
| Blinking | P | Native blink cycle and gesture suppression. |
| RemoveBlinking | P | Reversible blink disable, not shape deletion. |
| Visemes | P | Advanced viseme action states/transitions. |
| Talking | P/A | Voice-activity actions. |
| Breathing | P | Cyclic motion/expression. |
| Toes / Toes Puppet | P | Down/up/splay via native menu puppet controls. |
| BoneConstraint | P | Authored humanoid-bone constraint/attachment. |
| ConstraintRetarget | P | Reference rewrite within verified ownership. |
| WorldConstraint | P/A | Drop/world-anchor, reset/pickup/cleanup/authority. |
| ShowInFirstPerson | P | Local visibility, not mirror/remote deletion. |
| HeadChopHead | P | Native head-chop retarget. |
| AdvancedCollider | P/A | Shared collider/contact adapters. |
| AnchorOverrideFix, AnchorOverrideFix2 | P | Legacy/current common lighting-anchor intent if meaningful natively. |
| BoundingBoxFix, BoundingBoxFix2 | P | Legacy/current bounds/culling, cost caps. |
| CrossEyeFix, CrossEyeFix2 | P | Diagnose native eye solver; no blind compensating constraints. |
| AvatarScale, AvatarScale2 | E/P | Retired upstream; native scaling belongs to base-avatar scope. |
| MoveMenuItem | P/D | Relocation/identity/cycle checks; blank destination deletes a control and requires an explicit output-removal policy. |
| ReorderMenuItem | P | Native control order. |
| SetIcon | P | Metadata/texture import. |
| OverrideMenuSettings | P | Native presentation where meaningful. |
| RemoveHandGestures, RemoveHandGestures2 | P | Reversible gesture-layer disable, not source stripping. |
| FixWriteDefaults | P | Shared compiler default normalization. |
| MakeWriteDefaultsOff, MakeWriteDefaultsOff2 | P | Normalize while preserving effective behavior. |
| DirectTreeOptimizer | E/P | Unity optimization unnecessary; preserve unoptimized source behavior. |
| BlendshapeOptimizer | D | Bake/removal loses editability; defer. |
| DeleteDuringUpload | D | Whole-subtree removal, not harmless editor metadata. |
| MmdCompatibility | X/P | VRChat MMD conventions excluded; preserve usable assets. |
| UnlimitedParameters / Parameter Compressor (Beta) | X/P | VRChat multiplexing protocol excluded; native logical state can avoid encoding. |
| SecurityLock | A/P | PIN UI is not authentication or asset-ownership protection. |
| SecurityRestricted | A/P | Never silently unlock/reveal restricted objects; explicit policy required. |
| Gizmo | E | Editor marker only. |
| DescriptorDebug | E | Descriptor/inspector debugging. |
| Slot4Fix | E | Deprecated upstream no-op for old Unity issue. |
| SpsOptions | A/P | Optional specialized interaction; settings alone not parity. |
| TPSIntegration, TPSIntegration2 | A/P/X | Legacy/current specialized intent; no literal Unity shader protocol. |
| TpsScaleFix | A/P | Specialized deformation/scale compensation, integration-gated. |
| OGBIntegration, OGBIntegration2 | A/P/X | Separate specialized adapter, no auto-activation/substitution. |
| ZawooIntegration | A/P/X | Separate specialized adapter/fixtures. |
| FeatureModel, LegacyFeatureModel, NewFeatureModel | E | Version/schema infrastructure, not user features. |

## 6. VRCFury complete action inventory

An action can appear in a toggle, gesture, viseme, upload action or nested state.
Support follows context, not just name. Local/remote, desktop/Android flags,
versions, resting eligibility and references are independent checks.

| Action type | Class | Native intent / exclusion |
| --- | --- | --- |
| ObjectToggleAction | K/P | Initial active state subset; independent runtime explicit on/off modes with inverse off state; legacy invert mode pending. |
| BlendShapeAction | K/P | Initial weights and independent constant runtime values/restoration; shared-property arbitration pending. |
| MaterialAction | K/P | Initial material-slot replacement and independent Toggle runtime switching with baseline restoration; shared-slot arbitration pending. |
| AnimationClipAction | K/P | Constant initial and independent native-toggle visibility/blendshape bindings with authored restoration; full playback/events/other property bindings pending. |
| MaterialPropertyAction | P | Mapped native properties, instance ownership/restoration. |
| ScaleAction | P | Animated/compensated scale with finite bounds. |
| FxFloatAction | P | Shared parameter writes/defaults/scope. |
| FlipbookAction / Poiyomi Flipbook Frame | P/X | Specific Poiyomi material frame selection; compatible native flipbook/material adapter required, not generic animation playback. |
| FlipBookBuilderAction | P | Nested pages and versioned states-to-pages normalization. |
| SmoothLoopAction | P | Loop timing/composition/transitions. |
| BlockBlinkingAction | P | Scoped native blink suppression/restoration. |
| BlockVisemesAction | P | Scoped lip-sync suppression/restoration. |
| DisableGesturesAction | P | Scoped gesture suppression/restoration. |
| ResetPhysboneAction | P/A | Native chain reset, not guessed object toggling. |
| WorldDropAction | P/A | Drop/reset with permissions/lifecycle. |
| PoiyomiUVTileAction | P/X | UV-tile visibility needs compatible native rendering design; property names alone not support. |
| ShaderInventoryAction | P/X | SCSS shader inventory needs specific material adapter. |
| SpsOnAction | A/P | Optional specialized interaction enablement. |
| ChangeSpsTagAction | A/P | Optional specialized tags, permission/namespacing. |
| Action | E/P | Common flags/version schema, not playable itself. |
| DoNotApplyRestingStateAttribute | E/P | Compiler metadata; honor restriction, no native attribute component. |

## 7. VRCFury wrappers and non-avatar systems

| Source type / family | Class | Native intent / exclusion |
| --- | --- | --- |
| VRCFury (Runtime/Model/VRCFury.cs) | K/P | Feature container; unknown reachable types diagnosed. |
| VRCFuryComponent, VRCFuryPlayComponent | E | Unity lifecycle bases, not additional features. |
| VRCFuryGlobalCollider | P/A | Native shared dynamics/tag adapters. |
| VRCFuryHapticPlug | A/P | Separate optional deformation/contact/haptic design. |
| VRCFuryHapticSocket | A/P | Optional activation/alignment/generated controls. |
| VRCFuryHapticTouchSender | A/P | Optional contact/touch, consent and scoped tags. |
| VRCFuryHapticTouchReceiver | A/P | Optional receiver/output; never auto-connect devices/services. |
| VRCFurySpsGreenScreenFix | E/X/P | Unity/SPS workaround not literal; native visual intent if needed. |
| VRCFuryNoUpdateWhenOffscreen | E/P | Unity preview/culling helper; native policy separate. |
| VRCFuryHideGizmoUnlessSelected, VRCFurySocketGizmo | E | Selection/editor gizmos only. |
| Builders/inspectors/updater/upload hooks/migrations/public API | E/X | No editor pipeline execution; interpret supported input only. |
| Editor-Worlds/Udon API/behaviours/world installation | X | Outside avatar scope despite shared package. |
| External haptic bridges/OSC/third-party services | A/X | Separate explicit integration/device permission, not inferred from assets. |

## 8. Exclusions and destructive-operation policy

Implementation progress and per-card evidence are tracked in
AVATAR_CONVERSION_PROGRESS.md. Deferred destructive build outcomes are missing
conversion work, not technical exclusions; originals remain immutable.

### Not to port literally

- Editor windows, inspectors, gizmos, preview jobs, upload/build tooling,
  installers/updaters and obsolete no-ops.
- VRChat uploads/blueprints, friends/trust/safety checks, performance ranks,
  impostors, cross-platform ordering, network encoding and world MMD conventions.
- Unity/VRChat executables, MonoBehaviour/custom state behavior code, shader
  binaries, editor/NDMF callbacks and automatic third-party migrations.
- Udon/world SDK behavior: outside this workstream, not declared impossible to
  recreate as a separate native-world project.

Excluding tooling does NOT exclude its useful authored output. MA and VRCFury
are conversion-input languages here, not runtime dependencies.

### Deferred information-losing operations

| Operation | Reason | Safe treatment |
| --- | --- | --- |
| MA Shape Changer Delete / Mesh Cutter / filters | Removes geometry; affects skin, indices, materials/shapes | Preserve geometry; report exact source and unmet removal intent. |
| MA Remove Vertex Color | Discards data channel | Preserve colors; do not silently fix shading by stripping them. |
| MA Replace Object | Removes/replaces identity/subtree and references | Diagnose, preserve source, no guessed target. |
| VRCFury DeleteDuringUpload / ObjectState DELETE | Removes subtree, including dependent features | Do not execute; warn/block dependent behavior as appropriate. |
| VRCFury BlendshapeOptimizer | Bakes/removes editable shapes and dependencies | Preserve shapes, separate optimization approval. |
| Platform filters/EditorOnly stripping/target exclusion | May omit required content | Explicit output selection with provenance, not source deletion. |
| VRCFury MoveMenuItem with blank destination | Deletes an authored menu control | Diagnose removal intent; do not silently discard controls/references. |
| Bone/component deletion, renderer collapse and merge optimization | Loses identity, breaks behavior references | Only already-tested staged structural operations allowed; new lossy optimizations deferred. |
| Texture downsampling/baking, decimation, data pruning | Loses visual/editable information | Separate opt-in, never hidden compatibility conversion. |

Source assets/extracted cache remain immutable even with consent. Staged output
deletions must be enumerated and recoverable by rebuilding from source. This task
grants no deletion approval. Current missing removal behavior must be disclosed;
do not present retained unwanted content as faithful conversion.

Never silently expose security-restricted content or content intended for
deletion/filtering. Until a native policy exists, report affected objects and do
not represent dependent output as complete. Cameras, interactions, locomotion,
external integrations and playback must not activate just because input exists.

## 9. Large-scope implementation sequence

Build shared semantic infrastructure, not three unrelated runtimes. Pure
parsing/planning/evaluation belongs in engine-independent tested classes; native
writes belong in thin staged adapters. Preserve existing UI template ownership.

| Wave | Deliverable | Unlocks | Gate |
| --- | --- | --- | --- |
| 0 | Versioned detection registry and component/action report | Honest coverage, unknown schemas, source provenance | Each inventory row recognized/deferred/excluded/unknown; no success-by-omission. |
| 1 | Structural/static completion and shared binding/reference representation | Reversible MA placement/settings, links, initial state, rig/face setup | Ownership/remap/missing-assets/rollback fixtures; no structural regressions. |
| 2 | Shared parameters and native controls/menus | Base expressions, MA menus/parameters, VRCFury Toggle/Puppet/Modes | Defaults vs persistence, names, owner sync, multiple wearers and transition tests. |
| 3 | Shared clip/layer/BlendTree/state-machine compiler | Base layers, MA Merge Animator/Motion, VRCFury FullController/Gestures/visemes/actions | Binding/mask/default/timing/interruption/single-writer oracle fixtures. |
| 4 | Constraints, gaze/viseme/visibility/world-space adapters | Constraint families, HeadChop, eye/mouth, drop/world-scale | Spaces/weights/scales/cycles, wearer identity and tracking recovery. |
| 5 | Dynamics/contacts/audio/effects | PhysBones, legacy dynamics, global colliders, touch, particles/lines/trails | Approximation reports, consent/permissions, cost bounds, multi-user authority. |
| 6 | Optional specialized integrations and approved lossy work | Specialized interactions/shaders/optimization | Separate approval, versioned adapters, before/after evidence; not prerequisite for normal avatars. |

Waves 1 and 2 can proceed independently after binding/state contracts stabilize.
Base VRChat, MA and VRCFury should compile to shared operations so the same menu,
parameter, shape link or constraint does not acquire three different meanings.
Component-count percentages are reproducible inventory metrics, not runtime
fidelity claims; see AVATAR_COMPONENT_COVERAGE.md for denominators and evidence.
Do not claim whole-avatar parity or delivery dates without acceptance evidence.

### Per-feature acceptance contract

1. Identify GUID/type/schema version, owner and original source. Resolve assets
   and aliases without name guessing.
2. Define supported fields/interactions. Report initial appearance versus runtime,
   approximation, intentional omission or blocked conversion distinctly.
3. Produce a deterministic pure plan with conflicts/dependencies. Unknown reachable
   managed types and missing required assets stay explicit.
4. Add fixtures for failure/rollback, source immutability, repeated import,
   mixed-framework behavior and driver ownership.
5. Apply only to isolated staged avatars with source-to-native identity maps.
   Never overwrite fields already driven by a native component/template.
6. Run repository Test + Build and applicable UI audit; for runtime changes,
   install the DLL, fully restart Resonite and test through the real mod.
7. Verify owner/remote users, saved reload, reset/removal and cancellation where
   relevant. Only then promote runtime support status.

## 10. Initial planning validation (before runtime slice)

The initial planning pass added inventory/proposed scope only. No parsers, native drivers, template changes,
destructive processing or new conversion-support claims. Unrelated existing
documentation-site edits remain untouched.

Planning-pass validation on 2026-10-24 (not validation of later runtime changes):

- Inventory name-coverage check: all 59 VRCFury feature files, 21 action/metadata
  files and 11 wrapper files accounted for. All 40 MA tag-derived declarations
  (including shared bases) accounted for; editor-only helpers listed separately.
  This checks catalog completeness, not conversion correctness.
- Test: dotnet run --project Tests/Importer.RegressionTests.csproj -c Release
  returned 813/813 passed.
- Build: dotnet build UnityPackageImporter/UnityPackageImporter.csproj -c Release
  --no-incremental returned 0 errors and 76 warnings.
- UI audit: dotnet run --project Tests/Importer.RegressionTests.csproj --
  --ui-bundle UnityPackageImporter/bin/Release/UnityPackageImporter.dll
  UnityPackageImporter/UI/Templates passed station, pre-import and error-report
  binding, asset-hash and privacy checks.
- No runtime changes were installed and no live Resonite import was verified for
  this documentation-only task. Expanded conversion remains unimplemented.
  Existing runtime build is ready for live test, not certified by this inventory.

## 11. First runtime slice validation

Validation on 2026-10-24 after the local runtime implementation:

- Required Release regression command: 884/884 passed, including 71 new wardrobe
  cases. The 30-control case validates independent VRCFury controls, not the
  mixed base/MA/VRCFury product acceptance fixture.
- Required non-incremental mod build: 0 errors, 76 warnings.
- Embedded station, pre-import and error-report UI bundle audits passed.
- Scoped whitespace/diff checks passed. Existing documentation-site edits were
  left untouched; nothing was committed, pushed or published.
- Installation was attempted with the repository installer. It refused because
  Resonite or its renderer was running, so the new DLL was not installed.
- No live imports, wearer/observer synchronization or native lifecycle behavior
  were verified. Ready for live test after closing Resonite, installation and a
  full restart; this is not whole-avatar parity certification.

## 12. Concrete-type coverage and constant-clip runtime slice

Validation on 2026-10-24 for the next local implementation:

- AVATAR_COMPONENT_COVERAGE.json inventories concrete types separately from
  grouped planning rows and abstract schemas. The generated Markdown report
  lists every type, exclusion, implementation boundary, code and test evidence.
- Functional subsets earn one partial component credit; recognition/indexing
  earns none. Excluded concrete types stay in the denominator. MA is 6/40
  (15.0%); VRCFury is 8/87 (9.2%) across feature/action/wrapper types. The
  explicitly bounded SDK-type inventory is 1/28 (3.6%). These are component
  coverage metrics, not avatar-success probabilities or full-fidelity claims.
- Independent VRCFury toggles now accept exactly resolved constant animation
  clips for child visibility and blendshapes, restoring authored values when
  off. Static and runtime conversion share an owned, exact animation path
  resolver. Ambiguous paths, variable curves, events and conflicting ownership
  reject the runtime plan rather than guessing or silently dropping behavior.
- Mixed MA/base Animator/VRCFury runtime composition, full curve playback,
  shared parameters, menu parity and the mixed 30-outfit acceptance fixture
  remain unsupported. A passing independent 30-control fixture is not parity.
- Required Release regression command: 931/931 passed. Required non-incremental
  mod build: 0 errors, 76 warnings. All three embedded UI bundle audits passed.
- The repository installer refused because Resonite or its renderer was
  running. This build was not installed; no process was terminated. Close
  Resonite completely, install, then fully restart it for the live test.
- No live avatar import, wearer/observer synchronization, saved reload or
  native driver lifecycle was verified. Ready for live test after installation,
  not certified as whole-avatar parity. Nothing was committed or published.

## 13. Whole-avatar foundations and authored viseme increment

Local implementation on 2026-10-24, based on master a93144b. The goal remains
all component-card outcomes across SDK, MA, VRCFury and combinations, not only
wardrobe. AVATAR_CONVERSION_PROGRESS.md logs the additions, per-card boundaries,
and the remaining dependency order.

- Added exact descriptor mode-3 viseme indexing/planning and a native adapter
  after AvatarCreator. It binds exact authored channels/shape identities, uses
  native avatar voice assignment, and replaces only heuristic direct-driver
  links created by this avatar-creation operation. Pre-creation field snapshots
  preserve retired heuristic destinations. Foreign ownership, duplicate target
  mixing, missing names/renderers and ambiguous descriptors/head nodes reject
  before adapter mutation. Rollback attempts all cleanup/restoration actions and
  reports both the original error and cleanup errors.
- Shared typed parameter/alias state and ordered property evaluation are pure
  foundations. Their native projection is still restricted to the existing
  independent VRCFury subset. No MA/base-controller runtime composition, saved
  parameter persistence, full Animator, dynamics or whole-avatar parity is claimed.
- Exact AST expression parsing retains nested control structure and avoids
  accidental parameter/type capture from puppet labels or unrelated records.
  Parsing does not earn control-behavior support credit.
- Fixed post-initial-appearance blendshape baseline capture, premature expression
  lifecycle completion, unknown runtime-context acceptance and control path
  collisions. Regression tests preserve strict open float thresholds using
  consistent single-precision parameter/comparison values.
- Destructive build outcomes formerly classified as excluded are now missing
  targets. No geometry/source deletion was added. MA remains 6/40 (15.0%),
  VRCFury 8/87 (9.2%), and the bounded SDK inventory 1/28 (3.6%). Descriptor
  support remains partial; an additional subset does not earn a second component.
- Required Release regression command: 1091/1091 passed. Required non-incremental
  mod build: 0 errors and 76 warnings. Embedded station, pre-import and error-report
  UI bundle audits passed. Existing documentation-site edits were preserved.
- The repository installer copied and SHA256-verified the mod, YAML dependency,
  license and notices. No running process was terminated. Fully restart Resonite
  before testing the actual importer.
- Public native API signatures were compiled against the locally installed
  FrooxEngine.dll, SHA256
  CD1F57C97408AADA9158E8C9D5C9ADAB05815DBB4A28756AA42681C6B462CEA8.
  This is an API/build check, not an in-game lifecycle verification.
- No live import, microphone/viseme equip-dequip, face-driver arbitration,
  remote observation or mixed SDK/MA/VRCF avatar fixture was verified. This
  increment is ready for live test, not whole-avatar parity certification.
  No commit, push, PR or released-card promotion was performed.

## 14. Runtime material toggles, continuous blendshape links, and Modular Avatar wardrobe slice

Local implementation on 2026-10-24, based on master a93144b:

- Added runtime MaterialAction compilation in VrcFuryWardrobeCompiler.cs (supporting v1 renderer and legacy v0 obj with exact 32-hex GUID and non-zero fileID baseline/target verification) and native BooleanReferenceDriver<IAssetProvider<Material>> binding on MeshRenderer.Materials.GetElement(index) in AvatarWardrobeRuntime.cs.
- Extended BlendshapeInitialAppearance.cs and BlendshapeInitialAppearanceAdapter.cs to emit verified BlendshapeRuntimeLink tuples (ModularAvatarBlendshapeSync and VRCFury BlendShapeLink, rejecting multi-source destination conflicts) and attach native ValueCopy<float> (WriteBack = false) drivers under a dedicated "Blendshape sync" slot when AvatarRoot is present, with all-or-nothing preflight (retaining static initial weights with a warning if any link uses a remap curve, stripped source shape, or already-driven destination) and transactional rollback.
- Added ModularAvatarWardrobeCompiler.cs (ModularAvatarInitialAppearance.CompileWardrobe) to compile ModularAvatarParameters (scoped remaps, internal parameters, defaults), ModularAvatarMenuInstaller, ModularAvatarMenuGroup, ModularAvatarMenuItem (SubMenu hierarchy, independent binary Toggle controls, icons, isDefault), and reactive components (ModularAvatarObjectToggle, ModularAvatarShapeChanger Set mode, ModularAvatarMaterialSetter with m_inverted and baseline restoration) into AvatarWardrobePlan.
- Updated AVATAR_COMPONENT_COVERAGE.json and regenerated AVATAR_COMPONENT_COVERAGE.md: Modular Avatar is now 10/40 (25.0%, 1 indexed), VRCFury is 8/87 (9.2%), and Base VRChat SDK is 1/28 (3.6%).
- Required regression command: 1110/1110 passed. Required mod build: 0 errors. Ready for live test; no live Resonite verification is claimed by automated tests.