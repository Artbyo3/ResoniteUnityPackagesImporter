# Core import update — 2026-10-06

Implementation resumed with the user's approval after the roadmap review. The
roadmap is guidance; installed engine behavior and runtime evidence take precedence.
The approved native station and pre-import UI packages have not been redesigned.

## Implemented for the next test build

- Removed the forced 1.8 m humanoid normalization. Existing FBX/meta unit
  conversion remains in place; authored avatar and clothing scale is retained.
- Scene parsing no longer rewrites extracted source files to remove `stripped`
  headers. The shared parser already normalizes those headers in memory.
- Added a session-owned inactive staging container, with active asset providers.
  Prefabs, scenes, FBX templates, intermediate clones and rig wrappers enter that
  hierarchy at creation. Source `ActiveSelf` values are not used to hide staging.
- Cancellation stops new work; operation leases let outstanding native work
  settle before cleanup. Close, UI deletion and world destruction signal
  cancellation. Coroutine completion also handles interrupted native imports.
- Closing a station destroys unused sources and previews, rather than revealing
  them. Native `DestroyPreservingAssets` retains dependencies used outside the
  session. On first committed output, the shared provider tree moves to the
  engine asset area; cleanup subsequently prunes it. This also protects outputs
  when the entire session root is manually deleted.
- Successful scene imports commit on completion. Avatar/object creation commits
  its full wrapper only after finalization. Installed outfits remain copies on
  the selected avatar; incomplete outfit installation uses the existing rollback.
- Create Avatar now calls native `AvatarCreator.CreateBipedAvatarAsync`, using
  authored descriptor viewpoint and native hand-reference conventions. The four
  native switches are eyes, protection, volume meter and face tracking. Native
  rig, avatar root, group, voice, eyes and supported face setup are reused.
- Prevented a second generic humanoid setup when an inline FBX already supplied
  a valid rig. Native finalization requires one complete humanoid rig.
- Hidden staging no longer contaminates an outfit's recorded enabled state.
  Targets exclude temporary sources/previews from every current import session.
- Station imports suppress the separate FBX and scene progress entities.
  Completion/dissolve precedes preview staging. Station rotation preserves yaw
  and removes pitch/roll.
- Unmatched companion packages follow normal import prompting rather than being
  silently consumed. Waiting-session references are removed on world destruction;
  material application participates in session lifetime.

## Deliberate limits

- Staging is hidden, **not local-only**. Parenting under LocalUserSpace would not
  make reference IDs local, and reparenting cannot promote local objects to global.
- Existing bad outfit installation records are not migrated. Test a fresh import
  and installation; old records may still contain a false enabled-state value.
- Missing/ambiguous descriptor viewpoints use paired eye bones when available.
  Missing calibration or multiple rigs produces an error instead of guessing.
  Descriptor inheritance in variant-only YAML remains future work.
- Explicit `dumpPackageContents` retains the native independent import behavior,
  including its own indicators and outputs outside station ownership. Its
  disposable launcher is isolated from staging. The normal station workflow
  keeps this option off.
- The pedestal orientation is upright; floor-height placement and reach have not
  been calibrated across desktop/VR users.
- No new claim is made that the disappearing-mesh/bounds problem is solved.
- Native creation and Assimp conversion have sections without cancellation APIs.
  Closing waits for those sections to settle before destroying their targets.
- No installation, game restart, recurring automation or publication was performed.

## Verification and next gate

- Release build: zero warnings and errors.
- Pure regression suite: 84/84, including six cancellation/lifetime regressions
  and four viewpoint parsing regressions.
- Native-template integrity suite: 6/6.
- Engine API/IL review verified inactive staging, coroutine completion callbacks,
  native avatar creation parameters, and asset-preserving destruction semantics.

These checks do not establish in-game rendering or tracking correctness. Install
the prepared build only with Resonite and its rendering processes closed, then:

1. Import a known avatar and outfit together; verify no visible unfinished models
   or extra FBX/scene indicators, then no loading-blob/preview overlap.
2. Create short and tall avatars. Verify authored size, equipability, viewpoint,
   hands/feet tracking, all sibling meshes, and protection on/off.
3. Install fresh clothing; test initial visibility, toggle off/on, duplicate
   installation and optional menu/receipt. Check an avatar created by this build.
4. Close during extraction, FBX loading, prefab construction and outfit install.
   Check rollback, no leftover meshes, and successful retry.
5. Create one variant/install an outfit, discard remaining variants, then delete
   either the station UI or full session root. Finished outputs and materials
   must survive; unused sources and providers must disappear.
6. Save and respawn outputs after station cleanup. Verify materials, rig and
   outfit controls still work. Test scene-only, assets-only and split packages.
7. With two stations, confirm neither preview appears as an install target, and
   unrelated companion candidates retain their normal import path.

## Follow-on converter work

After this runtime gate, extract supported operations behind a small shared
converter contract: supported-input detection, conversion context/assets,
cancellation, ownership, and a result containing applied/skipped/unsupported
features. Do not add an unused extensibility framework or silently claim support
for future Modular Avatar or VRCFury components.

The official Resonite Unity SDK is the behavior reference for lilToon conversion.
Reuse runtime-compatible portions, adapt editor-only parts, pin the upstream
revision, and compare identical fixtures before changing material behavior.
Normal-map handling still requires verification of native assignment semantics.
Bone remapping performance, Shape Changer ownership, Blendshape Sync, broader
animation/parameter support and bounds diagnosis remain separate follow-ups.
