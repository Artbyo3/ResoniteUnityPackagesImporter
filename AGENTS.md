# RESONITE UNITY PACKAGES IMPORTER — AGENT GUIDELINES

ResoniteModLoader mod (C#, .NET 10) that imports `.unitypackage` files into Resonite.

## Start here
- The repository is the handoff; no previous chat or agent summary is needed. Read `docs/WORKING_STATE.md`, then check the live branch, HEAD, `git status`, the referenced code, and the tests before acting.
- User requirements and this file are the contract. Code is current behavior; tests are executable examples, not proof of full fidelity; state docs are navigation, not authority. When they disagree, inspect the evidence and fix the stale record.
- Detailed evidence, open work, install history, and the one consolidated live checklist live in `docs/AVATAR_CONVERSION_PROGRESS.md`; the contract in `docs/AVATAR_CONVERSION_SCOPE.md`.

## Agent working rules
- Verify state before claiming it: branch, commit, merge, and ship state are checked against the live repo and the code, never from notes.
- Commit hygiene: no Co-Authored-By trailers, no email addresses in commit messages, no personal paths or real names in any public file.
- Scope lock: do exactly the listed changes; anything else that looks wrong is reported, never silently fixed.
- Finish inside the run: wait for your own builds and tests in the run; never end a turn with work running behind you.
- Never babysit external runs (CI, other machines): dispatch, report the run ID and state, and end.
- One topic per commit series and per PR.
- AI involvement stays disclosed: keep the README's AI disclosure image as it is.
- Report what you ran and what you did not verify. Never claim a pass without running the command. Say "ready for live test", never "done" or "works".
- Never delete, skip, or weaken a test to get a pass.

## Git
- Never push to or merge into `master` without the owner's explicit "ship it". Work lands on feature branches, one branch per task; push one only when the task asks for it.
- On "ship it": push the branch, open one PR, and let the maintainer merge.
- Commit identity: `Artbyo3 <Artbyo3@users.noreply.github.com>`. Short imperative messages.
- Before every push, scan the diff for usernames, local paths, emails, and machine names, and delete scratch files you created.
- When your task ends, remove your worktree and delete your merged branch.

## Testing boundary (hard rule)
- Tests that start, launch, or connect to a Resonite process are NEVER run by agents. Only provably Resonite-free checks run: the default Tests run, the Python suites, the docs-site suites, and the UI bundle audit.
- Skipped by agents, listed by name in every report: `Tests/ModelUnits`, `Tests/Textures` (load engine DLLs), `--serialized-skin`, `--engine-api` / `--dump-type`, `scripts/RunStationDemo.py`, `scripts/Install-Mod.ps1`, and all in-game checks.
- The station prototype in a world is only a UI mockup. Real behavior is confirmed only by the owner: build, install into `rml_mods`, fully restart Resonite, import through the real mod.

## Commands
Use these; do not search for alternatives. Rename or change one only together with this table, in the same commit.

| Task | Command | Who |
|---|---|---|
| Test | `dotnet run --project Tests/Importer.RegressionTests.csproj -c Release` | agent |
| Python tests | `python -B -m unittest discover -s Tests -p "test_*.py"` | agent |
| Docs-site tests | `python -B -m unittest discover -s docs-site/tests` | agent |
| Build | `dotnet build UnityPackageImporter/UnityPackageImporter.csproj -c Release --no-incremental` | agent |
| UI bundle audit (after Build) | `dotnet run --project Tests/Importer.RegressionTests.csproj -c Release -- --ui-bundle UnityPackageImporter/bin/Release/UnityPackageImporter.dll UnityPackageImporter/UI/Templates` | agent |
| Component coverage | `dotnet run --project Tests/Importer.RegressionTests.csproj -c Release -- --component-coverage docs/AVATAR_COMPONENT_COVERAGE.json` | agent |
| Appearance compiler replay | `dotnet run --project Tests/Importer.RegressionTests.csproj -c Release -- --appearance-replay <local capsule.json>` | agent |
| Source animation clip audit | `dotnet run --project Tests/Importer.RegressionTests.csproj -c Release -- --animation-clips <fileID> <local .anim files...>` | agent |
| Bundle templates | `python scripts/Prepare-StationTemplates.py` | agent |
| Native skin asset audit | `dotnet run --project Tests/Importer.RegressionTests.csproj -c Release -- --serialized-skin <built dll> <Resonite directory> [local mesh assets...]` | owner |
| Install to Resonite | `scripts/Install-Mod.ps1` (Resonite closed, then fully restart it) | owner |
| Station demo | `python scripts/RunStationDemo.py [--port <port>]` | owner |

A wrong or misspelled audit argument currently falls through to the full test run and exits 0: check the audit's own PASS lines, not the exit code.

## Execution flow
- Batch related fixes -> Test, Build, applicable audits -> owner installs -> one consolidated owner live test -> adjustments -> owner approves -> ship (one PR).
- Do not ask for an import, restart, or install after each small fix. Keep working through the known backlog, adding faithful regressions and recording native verification gaps.
- Ask for an intermediate live probe only when inaccessible runtime evidence blocks safe progress, and say what cannot be established automatically. Synthetic passes never establish whole-avatar appearance or native behavior.
- Diagnose repeat import failures from the newest loaded-build evidence. Reproduce the failing boundary offline with the same resolvers and captured context before calling it representative; a replay missing clip dependencies, renderer state, or context flags is exploratory. Add a regression for the missed boundary.
- A failed delegate or quota limit is not a result; re-inspect the working tree before retrying.
- Throwaway scripts and logs go in `scratch/` (gitignored), never in `scripts/`.

## Working state record
- `docs/WORKING_STATE.md` stays short: objective, current branch/HEAD, next actionable dependency, latest automated validation, installed checkpoint, open live verification. Replace superseded state; history goes in the ledger.
- It is updated in the same commit as the work it describes. Record exact commands, results, and dates; keep implemented, validated, installed, live-verified, and approved separate; carry unverified evidence forward with its original date.

## Station demo
- `scripts/RunStationDemo.py` finds the local ResoniteLink endpoint from the engine's session announcements; `--port` overrides. It connects to Resonite, so agents give the owner the command and never run it.
- Never recreate the demo, rescan the world, or inspect the mod to rerun it. Troubleshooting: `docs/STATION_DEMO.md`. Running it authorizes no integration, install, automation, or publication.

## UI ownership (template vs code)
- (A) What the in-world template can do from its own UI state (look, layout, UI-only reactions, native animations), the template owns; C# must not do it. Delete compensating code instead of adding more.
- (B) What depends on real data (counts, names, folders, import results), the code owns. Templates hold no fixture or fake-data logic.
- (C) A state handoff (code sets a flag or driver State, the template reacts) stays.
- Never write to a field a template driver drives; Resonite rejects it. No safety-net code for older templates: template and code ship together.

## Template workflow
- Templates change only by editing a live copy in a world through ResoniteLink, listing exactly what changes and asking the maintainer before anything destructive; the maintainer exports the Resonite Package.
- Bundle with `scripts/Prepare-StationTemplates.py` (version bump, hashes, manifest). Never hand-edit `manifest.json` hashes. Run the UI bundle audit and Test before committing.
- Every named binding the code uses must exist in `station.bindings.json` / `pre-import.bindings.json`.

## Code rules
- Fix the root cause. A change that only hides a symptom — guards, retries, swallowed errors, special cases — does not count as a fix. If the root cause sits outside the task's scope, report it instead of patching around it.
- No source comments (inline, block, XML doc) or nonfunctional docstrings. Keep legal notices, compiler/tool directives, shebangs, and encoding declarations; never alter strings or fixtures with comment-like text. Rationale and evidence go in `docs/`, agent rules here.
- Identify Unity components by script GUID + fileID, never by name or type text.
- Malformed source values (unparseable numbers, conflicting fields, missing references) produce a diagnostic, never a silent default or a guess.
- No empty or bare catch blocks, no catch-all `catch (Exception)`, and no catch used as control flow or to return a fallback. Catch only the specific exception you handle; cleanup catches roll back and rethrow.
- No reflection into engine internals outside `FrooxInternalBridge.cs`; record the verified Resonite version in `docs/ENGINE_COMPATIBILITY.md`.
- Engine-free logic goes in classes the test project compiles, with unit tests; keep UI controllers thin. Add a reproducing regression for every behavior fix the harness can express.
## Verified baseline (2026-11-20, `feature/avatar-conversion-scope` at 992c506)
- Build: 0 errors, 79 nullable warnings; 76 pre-existing, 3 added on this branch (`AvatarWardrobeRuntime.cs`, `ImportReportService.cs`).
- Resonite-free suites pass: 1471/1471 C#, 28/28 Python, 95/95 docs-site, 3/3 UI bundle audit.
