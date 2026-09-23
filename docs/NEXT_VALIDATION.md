# Rendering and compatibility follow-up

Checkpoint before this investigation: `ac5e8a8` (2026-09-20).

## User scope and observations

### Final placement policy

After discussing origin placement, the user requested the technically preferable behavior rather than treating origin placement as a requirement. Prefabs now get independent world roots placed on the existing grid near the user; source-local offsets remain beneath those roots. Scene imports get independent identity roots at world origin to retain scene coordinates. Neither is parented to the importer UI/temporary-template container. Progress indicators stay near the user. The copied FBX attachment continues to preserve local transforms. Existing humanoid normalization remains in place, so this is not a claim of exact Unity scale fidelity. Existing imported objects are not migrated by the DLL update.

### Prefab placement correction (port 14734)

The user clarified that avatars spawn near world origin while their import containers appear near the user. `PrefabInstance` attached copied FBX roots with `SetParent(targetParent)`. Inspection of the installed engine confirms its omitted `keepGlobalTransform` argument defaults to true. This preserves the template world position instead of composing its local transform with the destination prefab placement. The call now explicitly passes false. Local prefab offsets are retained; they are not reset to zero. The humanoid movement wrapper intentionally preserves world position when inserted and is unchanged. This placement correction is separate from the bounds-mode workaround and needs a fresh import away from the world origin to verify in-engine.

### Uruki bounds experiment on port 61990

MANUKA's avatar children had been deleted by the user; Uruki reproduces the reported issue. On `Uruki_poi` (`Reso_1C13`), moving the import parent 100 metres along X while compensating both child positions left the avatar visible in the fixed camera. All three local positions were restored and compared exactly with the saved originals. This argues against parent position alone causing the problem; it does not rule out a transform/bounds defect.

A close camera at `(2.92,1.2,-0.65)` showed the torso but not the back-mounted `C_bat` renderer. Changing only that renderer's bounds from `SlowRealtimeAccurate` to `MediumPerBoneApproximate` made the weapon appear at the same camera position. It stayed visible after restoring the prior mode, so invalid/stale bounds and the refresh effect remain possible; this is evidence of a workaround, not proof of an engine root cause.

The importer default now uses `MediumPerBoneApproximate`. All 28 current `Uruki_poi` renderers were updated to that mode, with original values saved in `retest-61990/uruki-bounds-backup.json`. The user subsequently reported that it no longer appears to disappear. This supports the workaround for this avatar, pending broader testing. Other avatar variants remain unchanged. The build has zero warnings/errors and 45 regressions pass; those tests do not validate visual culling. Parent backups, restored snapshots, and before/after camera captures are in the same ignored evidence directory.

- Target ordinary Unity prefab/model import first, then bounded Modular Avatar compatibility. Do not claim that Modular Avatar's editor/build pipeline is implemented. VRCFury and Lapwing-specific setup tools are outside current scope.
- MANUKA looks broadly correct to the user, but parts disappear at certain distances while the viewpoint is still outside the avatar. This remains an unresolved rendering defect.
- Wings from k are much larger than in Unity. A larger package was also imported; the live world contains Lasyusha variants from FILE, in addition to Lapwing.
- Material fidelity is incomplete. Later work should reuse packaged ramp textures and bake explicit serialized gradient data into small deterministic textures where the source shader exposes it. Do not substitute an invented gradient or claim shader equivalence from a ramp alone.

## Evidence from the current session (port 41527)

- MANUKA body renderer is enabled and uses `SlowRealtimeAccurate` bounds. Its inspected material is opaque with culling off. Ordinary backface culling on this material does not explain the reported disappearance. This does not establish which renderer disappears or rule out other materials, bounds, camera clipping, or overlapping surfaces.
- Wings renderer is enabled and also uses `SlowRealtimeAccurate`; its local scale is approximately `(100,100,100)`. The source prefab also has large transforms, so changing the live scale alone would not identify the conversion bug. The active FBX path uses Assimp scale 1; metadata handling does not currently model `useFileScale`/FBX file units explicitly. Compare source units, imported mesh coordinates, transforms, and bind poses before changing conversion.
- Larger imports report blendshape count differences: Body 483 source weights versus 461 imported shapes; Body_base 38 versus 37; Socks 20 versus 19; Inner 12 versus 10. The current implementation applies a common index prefix. Missing shapes may shift subsequent indices, so those defaults cannot be considered validated. Preserve original shape identities or prevent importer-side removal before relying on index mapping; merely dropping trailing weights is insufficient.
- FILE/project completion appears at 22:37:46.637. The surrounding log reports approximately 16–17 FPS with many variants and other imports in the world; this is not an isolated performance benchmark.
- Lapwing has missing-renderer warnings and a 155-versus-147 shape count difference. Record these separately from the supported baseline.

Raw snapshots and logs are in `UnityPackageImporter/bin/live-tests-20260920/rendering-investigation/` (ignored by Git). No diagnostic mutations were made to the user's live objects.

## Next controlled tests

### Name-based blendshape correction

Inspection of the installed engine's `ModelImporter.ImportMesh` iterator confirms an unconditional call to `MeshX.StripEmptyBlendshapes` before saving the imported mesh. The new build records original Assimp shape names per mesh-bearing node before engine mesh conversion. Prefab defaults and sparse overrides retain that source order, then target the imported renderer through `BlendShapeIndex(name)`. Missing empty shapes no longer shift later weights. Ambiguous node/submesh names or duplicate shape names are rejected with a diagnostic instead of guessing. The build has zero warnings/errors and 45 regressions pass; a live FILE/MANUKA retest remains required.

Two camera captures of MANUKA at near and far positions did not reproduce the reported disappearance of MANUKA itself. They show extensive obstruction by the oversized wings, which are absent from the close camera view. This does not identify the user's reported MANUKA defect. Temporary capture cameras were cleaned up. Images are `rendering-investigation/manuka-far.jpg` and `manuka-near.jpg` under the ignored live-test directory.

### Additional live batch audit

All 11 Lasyusha roots were inspected: the two base variants have 9 and 10 skinned renderers, and all nine clothing/color variants have 15 each (154 total). These counts match the serialized SkinnedMeshRenderer documents in their source prefabs. Counts alone do not establish geometric correctness.

A deeper sample covered the no-heel base and Co2_C2 variant: all 24 renderer components had mesh references, no null material entries, and no null bone references. All used `SlowRealtimeAccurate` bounds. The base has 395 bone entries per renderer and the clothed variant 463. Source prefabs likewise serialize large bone lists (396/464 references), so the evidence does not support blaming importer duplication for their size. The one-entry differences and actual name/index correspondence still need investigation; non-null references do not prove correct rigging.

The batch strengthens these priorities:

- Resolve missing/reordered blendshapes before applying positional defaults to FILE; this affects body and clothing shapes in the sampled live avatars.
- Profile accurate bounds/skinning with one variant isolated, then compare the full batch. Do not change bounds methods globally merely because this mode is expensive by name.
- Add prefab selection and avoid importing all 11 complete avatars plus auxiliary prefabs by default.
- Surface unsupported Unity script components: these prefabs contain 85–173 MonoBehaviour documents each. This count does not identify which are Modular Avatar, physics, menus, or editor-only tools; GUID/type classification is needed before making compatibility claims.
- Audit material parameters and texture roles independently of reference presence. Assigned materials do not establish shader fidelity.

Evidence: `UnityPackageImporter/bin/live-tests-20260920/batch-audit/` contains source/live counts, sampled renderer component snapshots, bone counts, and logs. This audit made no world edits.

1. Reproduce MANUKA disappearance at recorded near/far camera positions, identify the affected renderer, then compare bounds behavior with materials held constant. Restore each temporary diagnostic change.
2. Trace k FBX unit conversion against its Unity metadata and prefab transforms; use a known-size mesh and a skinned fixture to verify both geometry and bind poses.
3. Compare original FBX blendshape names/order against the engine-imported mesh for FILE. Add a regression containing an empty shape between two nonempty shapes.
4. Only then expand material mapping and supported Modular Avatar operations, reporting unsupported build-time features explicitly.
