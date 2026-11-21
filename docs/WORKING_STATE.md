# Current working state

Last updated: 2026-10-26. This is the short continuation entry point, not a
transcript, release announcement, or substitute for inspecting the working tree.
Update it in place when evidence changes. The protocol lives in AGENTS.md.

## Objective and authority

Preserve whole-avatar appearance and usable behavior when importing VRChat SDK,
Modular Avatar, VRCFury, and mixed-source packages into Resonite. Wardrobe is one
fixture, not the scope boundary. Continue independent local fixes in integrated
batches; do not ask for a manual import after each small change.

- Contract: [avatar conversion scope](AVATAR_CONVERSION_SCOPE.md).
- Detailed evidence, open dependencies, installed history, and the single
  consolidated live checklist: [conversion ledger](AVATAR_CONVERSION_PROGRESS.md).
- Component evidence source: [coverage catalog](AVATAR_COMPONENT_COVERAGE.json);
  [generated report](AVATAR_COMPONENT_COVERAGE.md). Partial credit is not fidelity
  or live acceptance. Do not regenerate expected output inside a comparison test.
- Reflection provenance and native verification boundary:
  [engine compatibility](ENGINE_COMPATIBILITY.md).
- Local docs versus released/public status: docs-site/README.md and
  docs-site/PROJECT_STATUS.md. Do not publish local engineering state as a release.

## Workspace and latest change

Observed branch: `feature/avatar-conversion-scope`; HEAD: `c132c96`.
The working tree contains substantial uncommitted implementation, tests, docs,
and comment cleanup. These identifiers are a checkpoint, not a command to reset
or switch branches. Re-read `git status` and the diff; preserve existing edits.
No commit, branch creation, push, PR, or live world mutation is authorized.

Latest local gate evidence verifies `5DE646C0` loaded for the exact S1N item:
native Ready, 23/23 succeeded operations, and a resolved graph (729 objects,
140 Transform identities). The previous 42 inherited-data errors are absent.
The next blocker is 24 errors from two transform-only idle clips entering the
static appearance compiler. Private evidence: scratch/live-reports-20261026-post-graph.
The earlier offline replay omitted the clip resolver; it could not expose this
native boundary. This is failure evidence, not successful avatar creation.

The actual button predicate now exposes every disable reason. Independent local
gate files immediately preserve loaded build identity, exact source context and
messages, error-first plan diagnostics and operation states. Shareable reports
receive severity/code/alias details without private context. Missing inherited
source data is classified explicitly, not downgraded or guessed. See
[action diagnostics and next repair](IMPORT_ACTION_DIAGNOSTICS.md). No coverage
is promoted. The integrated local repair snapshots exact instance-scoped hierarchy,
names and active state before conversion reparenting, including private source
node identities and explicit model-import scaffold exclusion. VRCFury initial
appearance and wardrobe use the same immutable evidence. Repeated-instance
Transform owner lookup is repaired; explicit malformed/conflicting fields,
missing evidence and driver ownership still reject. The gate is unchanged.
Native evaluation confirms graph capture reached the initial appearance compiler;
creation and full appearance remain unverified. The new local batch separates
validated runtime Toggle motion from static build poses without sampling bone
transforms or weakening missing/malformed-reference checks. It adds exact local
compiler replay capsules with real clip dependencies and native-derived context;
source audits of both actual clips now classify runtime playback with zero static
curves and no errors. The exact-input replay and runtime/build-action boundaries
have passing regressions. This batch is installed and ready for live test;
native capsule capture and avatar creation remain unverified.

The newer [serialized skin reconstruction](UNITY_SERIALIZED_MESHES.md) decodes
bounded version-11 meshes, exact ordered bone slots and sparse shapes. Native
construction supports one full-weight frame per shape, rejecting other thresholds
and in-between frames rather than flattening them. Native influences are sorted
and normalized without trimming positive weights. Material slots retain indices.
Five retained source meshes pass native storage checks; renderer loading and
deformation remain unverified. No coverage credit is promoted.

Prior comment cleanup and repository-owned continuation remain in place: legal
notices, functional directives, strings, fixtures, and existing edits are retained.

Documentation follow-up, 2026-10-26: local English/Japanese previews now show the
installed DF35C43D stage, source-graph/clip/skin/diagnostic progress, privacy limits
and pending native acceptance. Corrupted Japanese framework copy is repaired.
Release builds omit the local summary and retain the release baseline; no public
deployment, coverage promotion or mod reinstallation was performed.

## Next actionable implementation dependency

The integrated batch was installed at the user's request on 2026-10-26 with
Resonite and its renderers closed. The next dependency is the consolidated live
test after restart. Independent bounded review found no remaining concrete
defects after fixing deferred-target validation. Runtime
wing/tail playback remains unimplemented and must remain visible as a limitation.
Do not sample arbitrary poses or suppress malformed/missing evidence. The next
native report must contain a replay locator or an explicit capture failure;
do not use a resolver-free synthetic replay as end-to-end acceptance.

- Follow IMPORT_ACTION_DIAGNOSTICS.md and the exact retained source probe. Once
  ownership is known, evaluate previously omitted controls; more real target or
  feature errors may surface. Native appearance remains a consolidated acceptance
  item; other framework consumers are not certified by the VRCFury repair.
- Native material callback and other composition failures remain separate open
  work in IMPORT_DIAGNOSTICS.md; the current grey button is not MissingRig.
- Preserve the new serialized skin source/native boundaries in
  UnitySerializedSkinData.cs and UnitySerializedSkinMeshImporter.cs. Multi-frame
  and non-full-weight shapes, implicit rigid weights and variable-count data
  remain rejected pending faithful native adapters, not permanent scope limits.
- Automated acceptance requires source-faithful data and binding invariants,
  not just a successful parse. Actual deformation and appearance remain live
  acceptance items. Do not promote coverage solely because tests pass.
- Existing constraints remain: unresolved weighted bones reject; proven-unused
  bones retain their index slots; authored humanoid targets require exact source
  provenance. See SkinBoneMapping.cs, AvatarBuildPlanExecutor.cs,
  Tests/NativeSkinBindingTests.cs, and Tests/AuthoredHumanoidTargetsTests.cs.
- Other controller/composition, scale-link, attachment, nested-source expansion,
  missing-source, and native lifecycle gaps remain in the ledger. Re-evaluate
  evidence before fixing; previous delegate failures establish no completion.

## Automated validation

2026-10-26, runtime clip classification and exact-input replay batch. Results below distinguish current
validation from evidence carried forward earlier on the same date.

- `dotnet run --project Tests/Importer.RegressionTests.csproj -c Release`:
  1471/1471 passed (scratch/post-graph-replay-tests.log).
- `dotnet build UnityPackageImporter/UnityPackageImporter.csproj -c Release --no-incremental`:
  0 errors, 79 warnings (scratch/post-graph-replay-build.log).
- UI audit command in AGENTS.md against the Release DLL: station, pre-import,
  and error-report embedded bundles passed; this is not native UI execution.
- Actual source audit: `--animation-clips 7400000` with WINGS IDLE.anim and
  TAIL IDLE.anim both returned runtime playback required, zero static curves and
  no errors, preserving two wing paths and one tail path for owned validation.
  Log: scratch/post-graph-replay-source-clips.log. This checks source
  classification, not runtime animation playback.
- Latest `dotnet run --project scratch/vrcf-source-replay/Probe.csproj -c Release`
  now supplies the real production clip resolver: 0 errors, 198 warnings, one
  graph-resolved Info, including two pending-playback warnings. This exploratory
  source-context check resolves actual wing/tail targets but retains assumed
  model active/context flags and no native renderers. It is not a native replay
  capsule or acceptance. Log: scratch/post-graph-replay-source-context.log.
- Earlier retained composed-source probe without a hierarchy: 41 source-graph
  errors, one source-state error, one unsupported-feature warning, zero writes.
  No native renderer context. This is the prior failing baseline, not a replay
  of the new graph adapter.
- `dotnet run --project scratch/vrcf-source-replay/Probe.csproj -c Release`:
  corrected exploratory S1N replay: 77 GameObject aliases, 41 Transform aliases,
  630 model objects, two active and two name overrides; 0 errors, 198 warnings,
  one graph-resolved Info. This assumes active model defaults and absent external
  animation context, lacks native renderers and a clip resolver, and does not
  validate native capture, writes, full appearance or creation. Detailed bounds
  and newly reachable warnings are in IMPORT_ACTION_DIAGNOSTICS.md.
- Prior 2026-10-26 native skin asset audit (not rerun for diagnosis-only changes):
  synthetic supported-frame baking and rejection checks,
  untrimmed normalization/sentinels, and five retained meshes passed public MeshX
  Plain/LZ4 round trips. This does not verify renderer loading or avatar deformation.
- `python -m unittest discover -s docs-site/tests`: 95 passed in the documentation
  follow-up, including seven stage/release-isolation and wording regressions.
- `python -m unittest discover -s Tests -p 'test_*.py'`: 28 passed.
- Prior 2026-10-26 component coverage command: completed without errors; not
  rerun for the clip/replay-only batch and no coverage credit promoted.
- `python docs-site/build.py --local`: English/Japanese local build passed;
  component cards remain evidence-based, unreleased and live-unverified.
- Documentation follow-up also ran `python docs-site/build.py`; checks of the
  generated overview, project-status, compatibility and reader-search outputs
  verify that release builds omit local stage claims and local builds include
  the installed checkpoint in both languages. Logs: scratch/docs-current-stage-*.log.
  Test and Build were rerun: 1471/1471, 0 errors/79 warnings; DLL hash still
  matches installed DF35C43D. Visual QA was unavailable: T3 snapshots timed out
  twice and the fallback browser connection reported no browser available.
- `git -c core.safecrlf=false diff --check`: passed.

These are checkpoint results, not assertions about future edits. Re-run the
required validation after subsequent changes and replace these results.

## Installed versus live-verified

Latest installed Release DLL SHA-256:
`DF35C43D82136AF79EA0644F990E530700E55837BC9E251B3ED3E13D484A527D`.
Installed on 2026-10-26 at the user's request via `scripts/Install-Mod.ps1`, with
Resonite and renderers closed; all four copied file hashes verified. Test and
Build were rerun first: 1471/1471 passed, 0 errors and 79 warnings. Evidence:
scratch/clip-replay-install-tests.log, scratch/clip-replay-install-build.log and
scratch/clip-replay-install.log. No process was stopped automatically. This
supersedes installed 5DE646C0. The exact-input capsule writer still has no native
capture evidence until exercised after restart. The existing consolidated live
checklist remains the acceptance boundary; no live acceptance is claimed.

The previous diagnostic checkpoint was the 2026-10-26 requested installation via
`scripts/Install-Mod.ps1`, with Resonite closed and all four copied hashes verified:
`F60EDE91DE95D0A02C6FEBDCBF94228F91F000530557D65D87490BA5146A9813`.
Local evidence is scratch/action-gate-install.log. Its subsequent independent gate
file verifies native diagnostic execution and the continuing failure for the exact
user item; no avatar acceptance is established. Earlier checkpoints and their
validation history remain in the conversion ledger.

The source-graph repair was installed at the user's request on 2026-10-26 via
`scripts/Install-Mod.ps1`, with Resonite and renderer processes closed. All four
copied file hashes verified. Its built/installed Release DLL SHA-256 is
`5DE646C09DBCD26C9689CF6E9FEDE31A4A5A98F314CC06ED62447A6B2CAC3E40`.
Test and Build were rerun before installation: 1442/1442 passed, 0 errors and
77 warnings. Evidence: scratch/model-graph-install-tests.log,
scratch/model-graph-install-build.log and scratch/model-graph-install.log.
No process was stopped automatically. This supersedes installed F60; a full
Resonite restart is required. Native graph capture, creation and appearance remain
unverified; this checkpoint is ready for live test, not avatar acceptance.

The consolidated checklist remains in the ledger: appearance/deformation,
authored targets, wardrobe, breathing timing, nonuniform scale/driver ownership,
and owner/observer/reset/re-equip behavior. Do not create a competing checklist
or request per-fix restarts. Install at the next meaningful integrated checkpoint
only with Resonite closed; never stop it automatically. User live acceptance and
shipping approval remain separate, outstanding gates.
