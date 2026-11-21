# Current working state

Last updated: 2026-11-20. Short entry point; history lives in the
[conversion ledger](AVATAR_CONVERSION_PROGRESS.md). Updated in the same commit as
the work it describes (rules in AGENTS.md).

## Objective

Preserve whole-avatar appearance and usable behavior when importing VRChat SDK,
Modular Avatar, VRCFury, and mixed-source packages into Resonite. Contract:
[avatar conversion scope](AVATAR_CONVERSION_SCOPE.md). Component evidence:
[coverage catalog](AVATAR_COMPONENT_COVERAGE.json).

## Workspace

Branch `feature/avatar-conversion-scope`. Code changes after 992c506:
PhysBone and PhysBone collider detection by script GUID + fileID only (#49).

## Next actionable dependency

- Owner's consolidated live test of installed `DF35C43D` (after full restart).
  The last native gate on `5DE646C0` showed 24 errors from two transform-only idle
  clips entering the static appearance compiler; see
  [action diagnostics](IMPORT_ACTION_DIAGNOSTICS.md). The next native report must
  carry a replay locator or an explicit capture failure.
- Audit findings from 2026-11-20, not yet fixed: behavior and constraint
  planners match components by name text instead of script GUID; the PhysBone
  planner's Modular Avatar PB Blocker check still accepts editor-class text;
  malformed numeric values silently default; a catch-all in
  `ModularAvatarWardrobeCompiler.cs` returns false without logging; wrong audit
  arguments run the full suite and exit 0.
- Runtime wing/tail playback is unimplemented and stays a visible limitation.

## Latest automated validation (2026-11-20, PhysBone identity commit)

- Build: 0 errors, 79 warnings.
- Test: 1473/1473 (Python, docs-site, and UI bundle audit last run at 992c506).
- At 992c506: Test 1471/1471. Python: 28/28. Docs-site: 95/95. UI bundle audit: 3/3 PASS.
- Not run (engine-bound): ModelUnits, Textures, `--serialized-skin`,
  `--engine-api`/`--dump-type`, RunStationDemo.py, Install-Mod.ps1, in-game checks.

## Installed versus live-verified

- Installed 2026-10-26 by the owner's request: Release DLL SHA-256
  `DF35C43D82136AF79EA0644F990E530700E55837BC9E251B3ED3E13D484A527D`.
- No live acceptance yet. The consolidated checklist in the ledger is the
  acceptance boundary: appearance/deformation, authored targets, wardrobe,
  breathing timing, nonuniform scale/driver ownership, owner/observer/reset/re-equip.
