# Avatar component support coverage

Generated from docs/AVATAR_COMPONENT_COVERAGE.json. Formula: (supported + partial) / all catalog entries x 100.
Partial receives one component credit only because a documented functional subset exists; it is not full fidelity. Indexed-only and excluded entries receive no credit. Exclusions remain in the denominator. Runtime is reported separately; this is not live-test certification.

| Catalog | Implemented / total | Coverage | Full | Partial | Indexed only | Missing | Excluded | Runtime subsets |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Base VRChat SDK types | 1 / 28 | 3.6% | 0 | 1 | 3 | 24 | 0 | 1 |
| Modular Avatar components | 10 / 40 | 25.0% | 0 | 10 | 1 | 26 | 3 | 8 |
| VRCFury feature models | 4 / 56 | 7.1% | 0 | 4 | 0 | 44 | 8 | 2 |
| VRCFury action models | 4 / 19 | 21.1% | 0 | 4 | 0 | 15 | 0 | 4 |
| VRCFury wrapper components | 0 / 12 | 0.0% | 0 | 0 | 1 | 5 | 6 | 0 |

VRCFury combined concrete authoring-type coverage: 8 / 87 = 9.2% (feature models + action models + wrapper components). The breakdown stays visible rather than changing denominators silently.

## Base VRChat SDK types

Source revision: Avatar SDK3 type inventory in AVATAR_CONVERSION_SCOPE.md, 2026-10-24. 28 explicitly inventoried SDK component/behavior/asset types from scope sections 1-2. This denominator is not every SDK utility type, Unity component, controller feature or built-in signal. General Unity animation/geometry/rendering coverage is a separate dependency, not credited here.

| Concrete type | Status | Runtime subset | Implemented behavior / remaining gap | Code evidence | Test evidence | Upstream source |
| --- | --- | --- | --- | --- | --- | --- |
| PipelineManager | indexed | no | Provenance/blueprint parsing only; no native VRChat ownership or upload service. | UnityPackageImporter/Models/AvatarPackageIndex.cs |  | docs/AVATAR_CONVERSION_SCOPE.md sections 1-2: PipelineManager |
| VRCAccessoryPerPlatformOverrides | missing | no | Explicit target policy; preserve other variants. |  |  | docs/AVATAR_CONVERSION_SCOPE.md sections 1-2: VRCAccessoryPerPlatformOverrides |
| VRCAimConstraint | missing | no | Sources, axes, offsets, activation/world-up. |  |  | docs/AVATAR_CONVERSION_SCOPE.md sections 1-2: VRCAimConstraint |
| VRCAnimatorLayerControl | missing | no | Weight blend/interruption. |  |  | docs/AVATAR_CONVERSION_SCOPE.md sections 1-2: VRCAnimatorLayerControl |
| VRCAnimatorLocomotionControl | missing | no | Locomotion override with consent/restoration. |  |  | docs/AVATAR_CONVERSION_SCOPE.md sections 1-2: VRCAnimatorLocomotionControl |
| VRCAnimatorPlayAudio | missing | no | Native playback, clip/order and volume limits. |  |  | docs/AVATAR_CONVERSION_SCOPE.md sections 1-2: VRCAnimatorPlayAudio |
| VRCAnimatorTemporaryPoseSpace | missing | no | Pose space, exit/interruption cleanup. |  |  | docs/AVATAR_CONVERSION_SCOPE.md sections 1-2: VRCAnimatorTemporaryPoseSpace |
| VRCAnimatorTrackingControl | missing | no | Per-channel handoff, protect tracking/recovery. |  |  | docs/AVATAR_CONVERSION_SCOPE.md sections 1-2: VRCAnimatorTrackingControl |
| VRCAvatarDescriptor | partial | yes | Local partial: owned root/viewpoint, native humanoid setup and exact mode-3 viseme mappings with unique destinations. Jaw/parameter-only modes, duplicate viseme mixing, playable layers and many descriptor settings pending; no live certification. | UnityPackageImporter/Models/NativeAvatarCreation.cs, UnityPackageImporter/Models/AvatarVisemeSettings.cs, UnityPackageImporter/Models/AvatarVisemeRuntime.cs | Tests/AvatarViewpointTests.cs, Tests/AvatarVisemeTests.cs | docs/AVATAR_CONVERSION_SCOPE.md sections 1-2: VRCAvatarDescriptor |
| VRCAvatarParameterDriver | missing | no | Ordered writes, local-only and owner-generated randomness. |  |  | docs/AVATAR_CONVERSION_SCOPE.md sections 1-2: VRCAvatarParameterDriver |
| VRCContactReceiver | missing | no | Constant/on-enter/proximity, self/others/local filters and parameter authority. |  |  | docs/AVATAR_CONVERSION_SCOPE.md sections 1-2: VRCContactReceiver |
| VRCContactSender | missing | no | Typed volumes/shapes/transforms/tags. |  |  | docs/AVATAR_CONVERSION_SCOPE.md sections 1-2: VRCContactSender |
| VRCExpressionParameters | indexed | no | Exact typed AST declarations/defaults/saved/sync metadata and pure shared-state model; no installed SDK/MA shared parameter runtime or saved restoration. | UnityPackageImporter/Models/ExpressionMenuParser.cs, UnityPackageImporter/Models/AvatarParameterState.cs | Tests/AvatarWardrobeStateTests.cs | docs/AVATAR_CONVERSION_SCOPE.md sections 1-2: VRCExpressionParameters |
| VRCExpressionsMenu | indexed | no | Exact AST control structure, ordered puppet parameters/label names and external references; hierarchy/display subset only. Uncompiled controls remain disabled; no runtime credit. | UnityPackageImporter/Models/ExpressionMenuParser.cs | Tests/ExpressionMenuStructureTests.cs | docs/AVATAR_CONVERSION_SCOPE.md sections 1-2: VRCExpressionsMenu |
| VRCHeadChop | missing | no | Local first-person visibility/scale, preserve remote/mirror. |  |  | docs/AVATAR_CONVERSION_SCOPE.md sections 1-2: VRCHeadChop |
| VRCIKFollower | missing | no | Explicit legacy attachment migration. |  |  | docs/AVATAR_CONVERSION_SCOPE.md sections 1-2: VRCIKFollower |
| VRCLookAtConstraint | missing | no | Look-at/up-vector/weights. |  |  | docs/AVATAR_CONVERSION_SCOPE.md sections 1-2: VRCLookAtConstraint |
| VRCParentConstraint | missing | no | Multi-source position/rotation, offsets/rest. |  |  | docs/AVATAR_CONVERSION_SCOPE.md sections 1-2: VRCParentConstraint |
| VRCPerPlatformOverrides | missing | no | Explicit target policy; preserve other variants. |  |  | docs/AVATAR_CONVERSION_SCOPE.md sections 1-2: VRCPerPlatformOverrides |
| VRCPhysBone | missing | no | Roots/ignore/endpoints, forces/curves, limits, collision, stretch/squish, grab/pose and generated state; approximate until measured. |  |  | docs/AVATAR_CONVERSION_SCOPE.md sections 1-2: VRCPhysBone |
| VRCPhysBoneCollider | missing | no | Shapes/offsets/radii, inside/outside and scale; no silent cross-user interaction. |  |  | docs/AVATAR_CONVERSION_SCOPE.md sections 1-2: VRCPhysBoneCollider |
| VRCPlayableLayerControl | missing | no | Playable weights/durations. |  |  | docs/AVATAR_CONVERSION_SCOPE.md sections 1-2: VRCPlayableLayerControl |
| VRCPositionConstraint | missing | no | Masks, offsets/rest, freeze/activation. |  |  | docs/AVATAR_CONVERSION_SCOPE.md sections 1-2: VRCPositionConstraint |
| VRCRaycast | missing | no | Query/result-transform, layers, misses and cost caps. |  |  | docs/AVATAR_CONVERSION_SCOPE.md sections 1-2: VRCRaycast |
| VRCRotationConstraint | missing | no | Masks, weighted rotations/offsets. |  |  | docs/AVATAR_CONVERSION_SCOPE.md sections 1-2: VRCRotationConstraint |
| VRCScaleConstraint | missing | no | Weights, offsets, singular/nonuniform scale. |  |  | docs/AVATAR_CONVERSION_SCOPE.md sections 1-2: VRCScaleConstraint |
| VRCSpatialAudioSource | missing | no | Spatial audio/attenuation, not identical engine. |  |  | docs/AVATAR_CONVERSION_SCOPE.md sections 1-2: VRCSpatialAudioSource |
| VRCStation | missing | no | Seat/pose, enter/exit authority and escape. |  |  | docs/AVATAR_CONVERSION_SCOPE.md sections 1-2: VRCStation |

## Modular Avatar components

Source revision: f21ad465bb623e841b532a66aa555dbcddf2db47. 40 concrete Runtime authored types: 38 nonabstract tag-derived components plus MAMoveIndependently and the MMD state behavior. ShapeChanger Set/Delete are one component. Abstract bases and editor infrastructure are not components; excluded concrete types remain counted.

| Concrete type | Status | Runtime subset | Implemented behavior / remaining gap | Code evidence | Test evidence | Upstream source |
| --- | --- | --- | --- | --- | --- | --- |
| MAMoveIndependently | excluded | no | Authoring relative-placement helper; preserve authored transforms, not editor live-lock machinery. |  |  | Runtime/MAMoveIndependently.cs |
| ModularAvatarBlendshapeSync | partial | yes | Initial exact blendshape weights and continuous ValueCopy<float> synchronization for unique destination blendshapes; multi-source blendshape blending pending. | UnityPackageImporter/Models/BlendshapeInitialAppearance.cs, UnityPackageImporter/Models/BlendshapeInitialAppearanceAdapter.cs | Tests/BlendshapeInitialAppearanceTests.cs | Runtime/ModularAvatarBlendshapeSync.cs |
| ModularAvatarBoneProxy | partial | no | Supported owned bone attachments/offsets; remaining modes/scale cases pending. | UnityPackageImporter/Models/BoneAttachmentTarget.cs | Tests/AvatarStructuralConversionTests.cs | Runtime/ModularAvatarBoneProxy.cs |
| ModularAvatarConvertConstraints | missing | no | Interpret Unity intent directly; VRC intermediate unnecessary. |  |  | Runtime/ModularAvatarConvertConstraints.cs |
| ModularAvatarFloorAdjuster | missing | no | Footwear/floor calibration; multiple-component conflicts. |  |  | Runtime/ModularAvatarFloorAdjuster.cs |
| ModularAvatarGlobalCollider | missing | no | Native dynamics/contacts; VRChat tags not universal protocols. |  |  | Runtime/ModularAvatarGlobalCollider.cs |
| ModularAvatarMaterialSetter | partial | yes | Initial exact external material-slot replacement and independent Boolean-toggle reactive runtime with baseline restoration; multi-state/shared-parameter reactive conditions pending. | UnityPackageImporter/Models/ModularAvatarInitialAppearance.cs, UnityPackageImporter/Models/ModularAvatarWardrobeCompiler.cs, UnityPackageImporter/Models/AvatarWardrobeRuntime.cs | Tests/ModularAvatarInitialAppearanceTests.cs, Tests/ModularAvatarWardrobeTests.cs | Runtime/ReactiveObjects/ModularAvatarMaterialSetter.cs |
| ModularAvatarMaterialSwap | missing | no | Substitution/conditions, not covered by Material Setter support. |  |  | Runtime/ReactiveObjects/ModularAvatarMaterialSwap.cs |
| ModularAvatarMenuGroup | partial | yes | MenuSource children/otherObjectChildren hierarchy traversal for installed Modular Avatar toggle menus; external asset menus pending. | UnityPackageImporter/Models/ModularAvatarWardrobeCompiler.cs | Tests/ModularAvatarWardrobeTests.cs | Runtime/Menu/ModularAvatarMenuGroup.cs |
| ModularAvatarMenuInstaller | partial | yes | Default root and hierarchy-scoped menu installation for independent Modular Avatar toggle/submenu trees; external menu assets and explicit install targets pending. | UnityPackageImporter/Models/AvatarPackageIndex.cs, UnityPackageImporter/Models/ModularAvatarWardrobeCompiler.cs | Tests/ModularAvatarWardrobeTests.cs | Runtime/Menu/ModularAvatarMenuInstaller.cs |
| ModularAvatarMenuInstallTarget | missing | no | Targeted insertion, cycle-safe resolution. |  |  | Runtime/ModularAvatarMenuInstallTarget.cs |
| ModularAvatarMenuItem | partial | yes | SubMenu hierarchy and independent binary Toggle controls with reactive child/parameter bindings, icons and default state; momentary Button, RadialPuppet/TwoAxisPuppet/FourAxisPuppet and multi-value controls pending. | UnityPackageImporter/Models/AvatarPackageIndex.cs, UnityPackageImporter/Models/ModularAvatarWardrobeCompiler.cs | Tests/ModularAvatarWardrobeTests.cs | Runtime/ModularAvatarMenuItem.cs |
| ModularAvatarMergeAnimator | missing | no | Controller merge, path remap, priority/layer semantics. |  |  | Runtime/ModularAvatarMergeAnimator.cs |
| ModularAvatarMergeArmature | partial | no | Supported structural merge/skin-reference subset; unsupported merge modes remain pending. | UnityPackageImporter/Models/ModularAvatarMergePlanner.cs | Tests/AvatarStructuralConversionTests.cs | Runtime/ModularAvatarMergeArmature.cs |
| ModularAvatarMergeBlendTree | missing | no | Shared motion/tree compilation. |  |  | Runtime/ModularAvatarMergeBlendTree.cs |
| ModularAvatarMeshCutter | missing | no | Pending isolated-copy geometry removal with skin/blendshape/reference remapping and source immutability; destructive outcome is not a technical exclusion. |  |  | Runtime/ReactiveObjects/ModularAvatarMeshCutter.cs |
| ModularAvatarMeshSettings | missing | no | Probe/anchor/bounds intent, lighting differences disclosed. |  |  | Runtime/ModularAvatarMeshSettings.cs |
| ModularAvatarMMDLayerControl | missing | no | VRChat world protocol excluded; usable animation intent separately. |  |  | Runtime/ModularAvatarMMDLayerControl.cs |
| ModularAvatarMoveTo | missing | no | Staged reparent/reference remap and cycle checks. |  |  | Runtime/ModularAvatarMoveTo.cs |
| ModularAvatarObjectToggle | partial | yes | Deterministic initial visibility and independent Boolean-toggle reactive runtime with baseline restoration; multi-state/shared-parameter reactive conditions pending. | UnityPackageImporter/Models/ModularAvatarInitialAppearance.cs, UnityPackageImporter/Models/ModularAvatarWardrobeCompiler.cs, UnityPackageImporter/Models/AvatarWardrobeRuntime.cs | Tests/ModularAvatarInitialAppearanceTests.cs, Tests/ModularAvatarWardrobeTests.cs | Runtime/ReactiveObjects/ModularAvatarObjectToggle.cs |
| ModularAvatarOutfitRoot | indexed | no | Only indexed/default metadata is interpreted; no corresponding native runtime behavior. Identification/selection metadata, not dressing runtime by itself. | UnityPackageImporter/Models/AvatarPackageIndex.cs |  | Runtime/ModularAvatarOutfitRoot.cs |
| ModularAvatarParameters | partial | yes | Scoped parameter remapping, internal parameter isolation and default values for Modular Avatar initial appearance and toggle compilation; saved/synced persistence and multi-writer arbitration pending. | UnityPackageImporter/Models/AvatarPackageIndex.cs, UnityPackageImporter/Models/ModularAvatarInitialAppearance.cs, UnityPackageImporter/Models/ModularAvatarWardrobeCompiler.cs | Tests/ModularAvatarInitialAppearanceTests.cs, Tests/ModularAvatarWardrobeTests.cs | Runtime/ModularAvatarParameters.cs |
| ModularAvatarPBBlocker | missing | no | Native chain exclusion, no component deletion needed. |  |  | Runtime/ModularAvatarPBBlocker.cs |
| ModularAvatarPlatformFilter | missing | no | Resonite target, not automatically PC/Android; explicit output exclusion, immutable source/dependency reports. |  |  | Runtime/ModularAvatarPlatformFilter.cs |
| ModularAvatarReactiveMove | missing | no | Reactive constraints, channels, world-fix and transitions. |  |  | Runtime/ReactiveObjects/ModularAvatarReactiveMove.cs |
| ModularAvatarRemoveVertexColor | missing | no | Pending isolated-copy vertex-color stripping honoring DontRemove overrides; preserve source mesh channels. |  |  | Runtime/RemoveVertexColor.cs |
| ModularAvatarRenameVRChatCollisionTags | missing | no | Namespaced native mapping, no VRChat interoperability claim. |  |  | Runtime/ModularAvatarRenameVRChatCollisionTags.cs |
| ModularAvatarReplaceObject | missing | no | Pending isolated-copy hierarchy replacement and exact reference remapping; preserve original hierarchy. |  |  | Runtime/ModularAvatarReplaceObject.cs |
| ModularAvatarScaleAdjuster | missing | no | Skin-aware compensation, not plain slot scale. |  |  | Runtime/ScaleAdjuster/ModularAvatarScaleAdjuster.cs |
| ModularAvatarShapeChanger | partial | yes | Initial Set mode weights and independent Boolean-toggle reactive runtime with baseline restoration; multi-state conditions and destructive Delete mode are not supported. | UnityPackageImporter/Models/ModularAvatarInitialAppearance.cs, UnityPackageImporter/Models/ModularAvatarWardrobeCompiler.cs, UnityPackageImporter/Models/AvatarWardrobeRuntime.cs | Tests/ModularAvatarInitialAppearanceTests.cs, Tests/ModularAvatarWardrobeTests.cs | Runtime/ReactiveObjects/ModularAvatarShapeChanger.cs |
| ModularAvatarSyncParameterSequence | excluded | no | VRChat ordering protocol unnecessary; declarations/defaults still preserved. |  |  | Runtime/ModularAvatarSyncParameterSequence.cs |
| ModularAvatarVisibleHeadAccessory | missing | no | Local first-person visibility, preserve mirror/remote. |  |  | Runtime/ModularAvatarVisibleHeadAccessory.cs |
| ModularAvatarVRChatSettings | excluded | no | Current MMD support flag has no literal native contract. |  |  | Runtime/ModularAvatarVRChatSettings.cs |
| ModularAvatarWorldFixedObject | missing | no | World anchoring, authority/cleanup/permissions. |  |  | Runtime/ModularAvatarWorldFixedObject.cs |
| ModularAvatarWorldScaleObject | missing | no | Effective size across avatar scale changes. |  |  | Runtime/ModularAvatarWorldScaleObject.cs |
| VertexFilterByAxisComponent | missing | no | Pending exact cutter axis-selection semantics on isolated output meshes. |  |  | Runtime/ReactiveObjects/VertexFilters/VertexFilterByAxisComponent.cs |
| VertexFilterByBoneComponent | missing | no | Pending exact cutter bone-weight selection on isolated output meshes. |  |  | Runtime/ReactiveObjects/VertexFilters/VertexFilterByBoneComponent.cs |
| VertexFilterByMaskComponent | missing | no | Pending exact cutter texture-mask selection and missing-asset diagnostics. |  |  | Runtime/ReactiveObjects/VertexFilters/VertexFilterByMaskComponent.cs |
| VertexFilterByShapeComponent | missing | no | Pending exact cutter blendshape-based selection and remapping. |  |  | Runtime/ReactiveObjects/VertexFilters/VertexFilterByShapeComponent.cs |
| VertexFilterByUVTileComponent | missing | no | Pending exact cutter UV-tile selection and remapping. |  |  | Runtime/ReactiveObjects/VertexFilters/VertexFilterByUVTileComponent.cs |

## VRCFury feature models

Source revision: 6bd33f4cb453df816c943a335e47f64e6a37e188. 56 concrete top-level feature model classes from 59 feature files, excluding FeatureModel/LegacyFeatureModel/NewFeatureModel schemas. Nested Gesture payload is not an extra feature. Legacy and current types are distinct serialized inputs; actions and wrappers are separate, not double-counted.

| Concrete type | Status | Runtime subset | Implemented behavior / remaining gap | Code evidence | Test evidence | Upstream source |
| --- | --- | --- | --- | --- | --- | --- |
| AdvancedCollider | missing | no | Shared collider/contact adapters. |  |  | Runtime/Model/Feature/AdvancedCollider.cs |
| AnchorOverrideFix | missing | no | Legacy/current common lighting-anchor intent if meaningful natively. |  |  | Runtime/Model/Feature/AnchorOverrideFix.cs |
| AnchorOverrideFix2 | missing | no | Legacy/current common lighting-anchor intent if meaningful natively. |  |  | Runtime/Model/Feature/AnchorOverrideFix2.cs |
| ApplyDuringUpload | partial | no | Reversible initial visibility/shape/material/constant-clip subset; arbitrary/destructive actions not executed. | UnityPackageImporter/Models/VrcFuryInitialAppearance.cs | Tests/VrcFuryInitialAppearanceTests.cs | Runtime/Model/Feature/ApplyDuringUpload.cs |
| ArmatureLink | partial | no | Supported staged structural attachment/merge subset; other link/scaling modes pending. | UnityPackageImporter/Frameworks/VrcFury/VrcFuryPlanAdapter.cs | Tests/VrcFuryArmatureSemanticsTests.cs | Runtime/Model/Feature/ArmatureLink.cs |
| AvatarScale | excluded | no | Retired upstream; native scaling belongs to base-avatar scope. |  |  | Runtime/Model/Feature/AvatarScale.cs |
| AvatarScale2 | excluded | no | Retired upstream; native scaling belongs to base-avatar scope. |  |  | Runtime/Model/Feature/AvatarScale2.cs |
| BlendShapeLink | partial | yes | Initial exact mapped blendshape weights and continuous ValueCopy<float> synchronization for unique destination blendshapes; multi-source blendshape blending pending. | UnityPackageImporter/Models/BlendshapeInitialAppearance.cs, UnityPackageImporter/Models/BlendshapeInitialAppearanceAdapter.cs | Tests/BlendshapeInitialAppearanceTests.cs | Runtime/Model/Feature/BlendShapeLink.cs |
| BlendshapeOptimizer | missing | no | Pending equivalent built appearance and animation bindings; preserve editable source and skip optimization only when behavior equivalence is proven. |  |  | Runtime/Model/Feature/BlendshapeOptimizer.cs |
| Blinking | missing | no | Native blink cycle and gesture suppression. |  |  | Runtime/Model/Feature/Blinking.cs |
| BoneConstraint | missing | no | Authored humanoid-bone constraint/attachment. |  |  | Runtime/Model/Feature/BoneConstraint.cs |
| BoundingBoxFix | missing | no | Legacy/current bounds/culling, cost caps. |  |  | Runtime/Model/Feature/BoundingBoxFix.cs |
| BoundingBoxFix2 | missing | no | Legacy/current bounds/culling, cost caps. |  |  | Runtime/Model/Feature/BoundingBoxFix2.cs |
| Breathing | missing | no | Cyclic motion/expression. |  |  | Runtime/Model/Feature/Breathing.cs |
| ConstraintRetarget | missing | no | Reference rewrite within verified ownership. |  |  | Runtime/Model/Feature/ConstraintRetarget.cs |
| CrossEyeFix | missing | no | Diagnose native eye solver; no blind compensating constraints. |  |  | Runtime/Model/Feature/CrossEyeFix.cs |
| CrossEyeFix2 | missing | no | Diagnose native eye solver; no blind compensating constraints. |  |  | Runtime/Model/Feature/CrossEyeFix2.cs |
| DeleteDuringUpload | missing | no | Pending staged-output subtree exclusion with dependent-feature/reference analysis; never remove the source subtree. |  |  | Runtime/Model/Feature/DeleteDuringUpload.cs |
| DescriptorDebug | excluded | no | Descriptor/inspector debugging. |  |  | Runtime/Model/Feature/DescriptorDebug.cs |
| DirectTreeOptimizer | excluded | no | Unity optimization unnecessary; preserve unoptimized source behavior. |  |  | Runtime/Model/Feature/DirectTreeOptimizer.cs |
| FixWriteDefaults | missing | no | Shared compiler default normalization. |  |  | Runtime/Model/Feature/FixWriteDefaults.cs |
| FullController | missing | no | Shared controller/menu/parameter merge, path/conflict rules. |  |  | Runtime/Model/Feature/FullController.cs |
| GestureDriver | missing | no | Native hand triggers/weights/composition. |  |  | Runtime/Model/Feature/GestureDriver.cs |
| Gizmo | excluded | no | Editor marker only. |  |  | Runtime/Model/Feature/Gizmo.cs |
| HeadChopHead | missing | no | Native head-chop retarget. |  |  | Runtime/Model/Feature/HeadChopHead.cs |
| MakeWriteDefaultsOff | missing | no | Normalize while preserving effective behavior. |  |  | Runtime/Model/Feature/MakeWriteDefaultsOff.cs |
| MakeWriteDefaultsOff2 | missing | no | Normalize while preserving effective behavior. |  |  | Runtime/Model/Feature/MakeWriteDefaultsOff2.cs |
| MmdCompatibility | excluded | no | VRChat MMD conventions excluded; preserve usable assets. |  |  | Runtime/Model/Feature/MmdCompatibility.cs |
| Modes | missing | no | Legacy exclusive modes; normalize without executing migration. |  |  | Runtime/Model/Feature/Modes.cs |
| MoveMenuItem | missing | no | Relocation/identity/cycle checks; blank destination deletes a control and requires an explicit output-removal policy. Diagnose removal intent; do not silently discard controls/references. |  |  | Runtime/Model/Feature/MoveMenuItem.cs |
| ObjectState | missing | no | Legacy ACTIVATE/DEACTIVATE reversible; DELETE destructive. No implicit migrations. Do not execute; warn/block dependent behavior as appropriate. |  |  | Runtime/Model/Feature/ObjectState.cs |
| OGBIntegration | missing | no | Separate specialized adapter, no auto-activation/substitution. |  |  | Runtime/Model/Feature/OGBIntegration.cs |
| OGBIntegration2 | missing | no | Separate specialized adapter, no auto-activation/substitution. |  |  | Runtime/Model/Feature/OGBIntegration2.cs |
| OverrideMenuSettings | missing | no | Native presentation where meaningful. |  |  | Runtime/Model/Feature/OverrideMenuSettings.cs |
| Puppet | missing | no | Continuous bounded native controls. Continuous 1D/2D authored stops/defaults. Down/up/splay via native menu puppet controls. |  |  | Runtime/Model/Feature/Puppet.cs |
| RemoveBlinking | missing | no | Reversible blink disable, not shape deletion. |  |  | Runtime/Model/Feature/RemoveBlinking.cs |
| RemoveHandGestures | missing | no | Reversible gesture-layer disable, not source stripping. |  |  | Runtime/Model/Feature/RemoveHandGestures.cs |
| RemoveHandGestures2 | missing | no | Reversible gesture-layer disable, not source stripping. |  |  | Runtime/Model/Feature/RemoveHandGestures2.cs |
| ReorderMenuItem | missing | no | Native control order. |  |  | Runtime/Model/Feature/ReorderMenuItem.cs |
| SecurityLock | missing | no | PIN UI is not authentication or asset-ownership protection. |  |  | Runtime/Model/Feature/SecurityLock.cs |
| SecurityRestricted | missing | no | Never silently unlock/reveal restricted objects; explicit policy required. |  |  | Runtime/Model/Feature/SecurityRestricted.cs |
| SenkyGestureDriver | missing | no | Specialized eye/mouth/ear expression states. |  |  | Runtime/Model/Feature/SenkyGestureDriver.cs |
| SetIcon | missing | no | Metadata/texture import. |  |  | Runtime/Model/Feature/SetIcon.cs |
| ShowInFirstPerson | missing | no | Local visibility, not mirror/remote deletion. |  |  | Runtime/Model/Feature/ShowInFirstPerson.cs |
| Slot4Fix | excluded | no | Deprecated upstream no-op for old Unity issue. |  |  | Runtime/Model/Feature/Slot4Fix.cs |
| SpsOptions | missing | no | Optional specialized interaction; settings alone not parity. |  |  | Runtime/Model/Feature/SpsOptions.cs |
| Talking | missing | no | Voice-activity actions. |  |  | Runtime/Model/Feature/Talking.cs |
| Toes | missing | no | Down/up/splay via native menu puppet controls. |  |  | Runtime/Model/Feature/Toes.cs |
| Toggle | partial | yes | Independent native Boolean visibility/blendshape/material and constant-animation-clip controls; mixed controllers, saved/exclusive/transitions/sliders pending. | UnityPackageImporter/Models/VrcFuryWardrobeCompiler.cs, UnityPackageImporter/Models/AvatarWardrobeRuntime.cs | Tests/VrcFuryWardrobeTests.cs | Runtime/Model/Feature/Toggle.cs |
| TPSIntegration | missing | no | Legacy/current specialized intent; no literal Unity shader protocol. |  |  | Runtime/Model/Feature/TPSIntegration.cs |
| TPSIntegration2 | missing | no | Legacy/current specialized intent; no literal Unity shader protocol. |  |  | Runtime/Model/Feature/TPSIntegration2.cs |
| TpsScaleFix | missing | no | Specialized deformation/scale compensation, integration-gated. |  |  | Runtime/Model/Feature/TpsScaleFix.cs |
| UnlimitedParameters | excluded | no | VRChat multiplexing protocol excluded; native logical state can avoid encoding. |  |  | Runtime/Model/Feature/UnlimitedParameters.cs |
| Visemes | missing | no | Advanced viseme action states/transitions. |  |  | Runtime/Model/Feature/Visemes.cs |
| WorldConstraint | missing | no | Drop/world-anchor, reset/pickup/cleanup/authority. |  |  | Runtime/Model/Feature/WorldConstraint.cs |
| ZawooIntegration | missing | no | Separate specialized adapter/fixtures. |  |  | Runtime/Model/Feature/ZawooIntegration.cs |

## VRCFury action models

Source revision: 6bd33f4cb453df816c943a335e47f64e6a37e188. 19 concrete Action-derived classes. Action schema and DoNotApplyRestingStateAttribute metadata are not user actions. Actions are not added to the feature-model denominator.

| Concrete type | Status | Runtime subset | Implemented behavior / remaining gap | Code evidence | Test evidence | Upstream source |
| --- | --- | --- | --- | --- | --- | --- |
| AnimationClipAction | partial | yes | Initial and independent native-toggle constant visibility/blendshape clips, exact GUID/fileID/path binding and authored off-state restoration; time-varying curves, transforms, materials, events and Animator execution pending. | UnityPackageImporter/Models/VrcFuryInitialAppearance.cs, UnityPackageImporter/Models/VrcFuryWardrobeCompiler.cs, UnityPackageImporter/Models/AvatarAnimationPathResolver.cs | Tests/VrcFuryInitialAppearanceTests.cs, Tests/VrcFuryWardrobeTests.cs | Runtime/Model/StateAction/AnimationClipAction.cs |
| BlendShapeAction | partial | yes | Initial weights and independent constant runtime values/restoration; shared-property arbitration pending. | UnityPackageImporter/Models/VrcFuryWardrobeCompiler.cs | Tests/VrcFuryWardrobeTests.cs | Runtime/Model/StateAction/BlendShapeAction.cs |
| BlockBlinkingAction | missing | no | Scoped native blink suppression/restoration. |  |  | Runtime/Model/StateAction/BlockBlinkingAction.cs |
| BlockVisemesAction | missing | no | Scoped lip-sync suppression/restoration. |  |  | Runtime/Model/StateAction/BlockVisemesAction.cs |
| ChangeSpsTagAction | missing | no | Optional specialized tags, permission/namespacing. |  |  | Runtime/Model/StateAction/ChangeSpsTagAction.cs |
| DisableGesturesAction | missing | no | Scoped gesture suppression/restoration. |  |  | Runtime/Model/StateAction/DisableGesturesAction.cs |
| FlipbookAction | missing | no | Specific Poiyomi material frame selection; compatible native flipbook/material adapter required, not generic animation playback. |  |  | Runtime/Model/StateAction/FlipbookAction.cs |
| FlipBookBuilderAction | missing | no | Nested pages and versioned states-to-pages normalization. |  |  | Runtime/Model/StateAction/FlipBookBuilderAction.cs |
| FxFloatAction | missing | no | Shared parameter writes/defaults/scope. |  |  | Runtime/Model/StateAction/FxFloatAction.cs |
| MaterialAction | partial | yes | Initial material-slot replacement and independent Boolean-toggle runtime material switching with baseline restoration; shared-slot arbitration pending. | UnityPackageImporter/Models/VrcFuryInitialAppearance.cs, UnityPackageImporter/Models/VrcFuryWardrobeCompiler.cs, UnityPackageImporter/Models/AvatarWardrobeRuntime.cs | Tests/VrcFuryInitialAppearanceTests.cs, Tests/VrcFuryWardrobeTests.cs | Runtime/Model/StateAction/MaterialAction.cs |
| MaterialPropertyAction | missing | no | Mapped native properties, instance ownership/restoration. |  |  | Runtime/Model/StateAction/MaterialPropertyAction.cs |
| ObjectToggleAction | partial | yes | Initial active state subset; independent runtime explicit on/off modes with inverse off state; legacy invert mode pending. | UnityPackageImporter/Models/VrcFuryWardrobeCompiler.cs | Tests/VrcFuryWardrobeTests.cs | Runtime/Model/StateAction/ObjectToggleAction.cs |
| PoiyomiUVTileAction | missing | no | UV-tile visibility needs compatible native rendering design; property names alone not support. |  |  | Runtime/Model/StateAction/PoiyomiUVTileAction.cs |
| ResetPhysboneAction | missing | no | Native chain reset, not guessed object toggling. |  |  | Runtime/Model/StateAction/ResetPhysboneAction.cs |
| ScaleAction | missing | no | Animated/compensated scale with finite bounds. |  |  | Runtime/Model/StateAction/ScaleAction.cs |
| ShaderInventoryAction | missing | no | SCSS shader inventory needs specific material adapter. |  |  | Runtime/Model/StateAction/ShaderInventoryAction.cs |
| SmoothLoopAction | missing | no | Loop timing/composition/transitions. |  |  | Runtime/Model/StateAction/SmoothLoopAction.cs |
| SpsOnAction | missing | no | Optional specialized interaction enablement. |  |  | Runtime/Model/StateAction/SpsOnAction.cs |
| WorldDropAction | missing | no | Drop/reset with permissions/lifecycle. |  |  | Runtime/Model/StateAction/WorldDropAction.cs |

## VRCFury wrapper components

Source revision: 6bd33f4cb453df816c943a335e47f64e6a37e188. 12 concrete VRCFury-prefixed Runtime MonoBehaviour wrappers from Component/ and Model/: nine Component/ wrappers plus container/debug/test types. The two abstract lifecycle bases are excluded as schemas. Debug/editor concrete types stay in the denominator with explicit exclusion status. Container recognition is indexed-only to avoid duplicate support credit.

| Concrete type | Status | Runtime subset | Implemented behavior / remaining gap | Code evidence | Test evidence | Upstream source |
| --- | --- | --- | --- | --- | --- | --- |
| VRCFury | indexed | no | Feature container only; functional feature/action converters are scored separately, not credited again for their wrapper. | UnityPackageImporter/Models/VrcFuryManagedReferenceGraph.cs |  | Runtime/Model/VRCFury.cs |
| VRCFuryDebugInfo | excluded | no | Editor/debug/test/preview infrastructure; no native runtime counterpart is claimed. |  |  | Runtime/Model/VRCFuryDebugInfo.cs |
| VRCFuryGlobalCollider | missing | no | Native shared dynamics/tag adapters. |  |  | Runtime/Component/VRCFuryGlobalCollider.cs |
| VRCFuryHapticPlug | missing | no | Separate optional deformation/contact/haptic design. |  |  | Runtime/Component/VRCFuryHapticPlug.cs |
| VRCFuryHapticSocket | missing | no | Optional activation/alignment/generated controls. |  |  | Runtime/Component/VRCFuryHapticSocket.cs |
| VRCFuryHapticTouchReceiver | missing | no | Optional receiver/output; never auto-connect devices/services. |  |  | Runtime/Component/VRCFuryHapticTouchReceiver.cs |
| VRCFuryHapticTouchSender | missing | no | Optional contact/touch, consent and scoped tags. |  |  | Runtime/Component/VRCFuryHapticTouchSender.cs |
| VRCFuryHideGizmoUnlessSelected | excluded | no | Selection/editor gizmos only. |  |  | Runtime/Component/VRCFuryHideGizmoUnlessSelected.cs |
| VRCFuryNoUpdateWhenOffscreen | excluded | no | Unity preview/culling helper; native policy separate. |  |  | Runtime/Component/VRCFuryNoUpdateWhenOffscreen.cs |
| VRCFurySocketGizmo | excluded | no | Selection/editor gizmos only. |  |  | Runtime/Component/VRCFurySocketGizmo.cs |
| VRCFurySpsGreenScreenFix | excluded | no | Unity/SPS workaround not literal; native visual intent if needed. |  |  | Runtime/Component/VRCFurySpsGreenScreenFix.cs |
| VRCFuryTest | excluded | no | Editor/debug/test/preview infrastructure; no native runtime counterpart is claimed. |  |  | Runtime/Model/VRCFuryTest.cs |
