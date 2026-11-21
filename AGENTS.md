# Workflow (read first)

## Repository-owned continuity

- The repository is the session handoff; do not require a previous chat or agent summary to continue. Start with `docs/WORKING_STATE.md`, then inspect the actual branch, HEAD, dirty diff, referenced implementation, and tests before acting.
- User requirements and these instructions define the contract. Code defines current behavior; tests provide executable examples, not proof of complete fidelity. State documents are evidence-backed navigation, not extra authority or permanent implementation limits. Resolve disagreements by inspecting the evidence and correcting stale records.
- Keep `docs/WORKING_STATE.md` short and current: objective, evidence links, next actionable dependency, latest automated validation, installed checkpoint, and unresolved live verification. Replace superseded state instead of accumulating chat transcripts. Do not duplicate the detailed backlog, coverage catalog, or acceptance checklist.
- At each meaningful checkpoint and before handing off, update that state alongside the change. Record exact commands/results and dates; separate implemented, automatically validated, installed, user-live-verified, and approved. Carry forward unverified evidence with its original date; never imply it was rechecked.
- Preserve knowledge through clear names, small explicit interfaces, faithful regression fixtures, and maintained docs, not source comments or agent-specific memory. Add a reproducing regression for each behavior fix when the existing test harness can express it. Keep rationale and native evidence in the relevant domain document and link it from the state record.
- Record actionable work without freezing a previous agent's assumptions: include the observed failure, source/test pointers, missing evidence, and acceptance condition. A failed delegate or quota limit is not an implementation result. Re-inspect the working tree before retrying work.

## Local execution

- Work locally. Do NOT push, open a PR, or create extra branches unless the
  user explicitly says "ship it". One branch per task.
- Before reporting: run Test and Build below. Never say "done" or "works";
  say "ready for live test". Only the user's live test in Resonite confirms it.
- Flow: batch related fixes -> automated Test + Build and applicable audits ->
  integrated Install -> consolidated user live test -> adjustments -> user approves
  -> ship (one PR).
- Do not require a manual import, restart, or installation after every small fix.
  Continue autonomously across the known failure backlog, adding faithful
  regressions and recording native verification gaps. Install only at meaningful
  integrated checkpoints, with Resonite closed; never stop it automatically.
- Keep one consolidated final live acceptance checklist. Ask for an intermediate
  live probe only when specific inaccessible runtime evidence blocks safe progress,
  and explain what cannot be established automatically. Synthetic passes do not
  establish whole-avatar appearance or native behavior.
- Never delete, skip or weaken tests to get a pass.
- Diagnose repeat import failures from the newest loaded-build/item evidence. Reproduce the failing production boundary with the same resolvers and captured context before calling an offline result representative. A replay without clip dependencies, renderer state, inherited visibility, or external-context flags is exploratory, not an end-to-end pass. Add a regression for the missed boundary and record what still needs native evidence; do not substitute repeated user imports for an available local reproduction.
- When your task ends, remove your worktree and delete your merged branch.
- Throwaway scripts go in `scratch/` (gitignored), never in `scripts/`.
- If you rename or change a command below, update this table in the same commit.

## Commands (use these; do not search for alternatives)

| Task | Command |
|---|---|
| Test | `dotnet run --project Tests/Importer.RegressionTests.csproj -c Release` |
| Build | `dotnet build UnityPackageImporter/UnityPackageImporter.csproj -c Release --no-incremental` |
| UI audit | `dotnet run --project Tests/Importer.RegressionTests.csproj -- --ui-bundle <built dll> UnityPackageImporter/UI/Templates` |
| Native skin asset audit | `dotnet run --project Tests/Importer.RegressionTests.csproj -c Release -- --serialized-skin <built dll> <Resonite directory> [local mesh assets...]` |
| Component coverage | `dotnet run --project Tests/Importer.RegressionTests.csproj -c Release -- --component-coverage docs/AVATAR_COMPONENT_COVERAGE.json` |
| Appearance compiler replay | `dotnet run --project Tests/Importer.RegressionTests.csproj -c Release -- --appearance-replay <local capsule.json>` |
| Source animation clip audit | `dotnet run --project Tests/Importer.RegressionTests.csproj -c Release -- --animation-clips <fileID> <local .anim files...>` |
| Install to Resonite | `scripts/Install-Mod.ps1` (then fully restart Resonite) |
| Station demo | `python scripts/RunStationDemo.py` |
| Bundle templates | `python scripts/Prepare-StationTemplates.py` |

# Station demo shortcut

When the user asks to run the station demo, run the existing
`scripts/RunStationDemo.py` once. It automatically discovers and verifies the
local ResoniteLink endpoint using the engine's native session announcements.
If the user supplies a port, pass it explicitly with `--port` instead.

Do not recreate the demo, rescan the world, or inspect the mod just to rerun it.
Wait for completion and report the result. If discovery finds several sessions,
ask which session to use; do not choose the first. If discovery finds none,
ask the user to enable ResoniteLink in the intended world and retry. An explicit
port remains available when native announcements are unavailable. For details
or troubleshooting, read `docs/STATION_DEMO.md`.

This is a live prototype demo. Running it does not authorize mod integration,
installation, recurring automations, or GitHub publication.

# Project working rules

Mod: ResoniteModLoader mod (C#, .NET 10) importing .unitypackage files into Resonite.
Commands:
- Tests: dotnet run --project Tests/Importer.RegressionTests.csproj -c Release
- Real mod build: dotnet build UnityPackageImporter/UnityPackageImporter.csproj -c Release --no-incremental
- UI bundle audit: dotnet run --project Tests/Importer.RegressionTests.csproj -- --ui-bundle <built dll> UnityPackageImporter/UI/Templates

## Git
- Never push to or merge into master. Only when the user says "ship it": push the branch and let the maintainer merge.
- Commit identity: Artbyo3 <Artbyo3@users.noreply.github.com>. Short imperative commit messages.
- Before every push, scan the diff for usernames, local paths, emails and machine names. Delete scratch files and temporary scripts you created.

## Agent working rules
- Verify state before claiming it: merge, ship and commit state is checked against the live repo and the code, never from notes.
- Commit hygiene: no Co-Authored-By trailers, no email addresses in commit messages, no personal paths or real names in any public file.
- Scope lock: do exactly the listed changes and nothing else; anything else that looks wrong is reported, never silently fixed or built.
- Finish inside the run: wait for your own builds and tests inside the run (foreground or poll loops); never end a turn with work still running behind it.
- Never babysit external runs (CI, builds on other machines): dispatch, report the run ID and state, and end; Harbor watches from there.
- One topic per PR and per commit series; keep changes reviewable.
- AI involvement is disclosed, not hidden: keep the existing AI disclosure in README.md as it is.

## UI ownership rule (template vs code)
- (A) If the in-world UI template can do it from its own UI state (look, layout, UI-only reactions, native animations), the template owns it and the C# code must not do it. Delete compensating code instead of adding more.
- (B) If it depends on real data (counts, names, folders, import results), the code owns it. The template must not hold fixture or fake-data logic.
- (C) A legitimate state handoff (code sets a flag or driver State, the template reacts) stays.
- Never write to a field that a template driver drives; Resonite rejects it with a warning. Never add safety-net code for older templates: the template and the code ship together.

## Template workflow
- Templates change only by editing a live copy in a world (ResoniteLink), asking the maintainer before any destructive change and listing exactly what changes, then the maintainer exports it as a Resonite Package.
- Bundle with scripts/Prepare-StationTemplates.py (version bump, hashes, manifest). Never hand-edit manifest.json hashes. Run the audit and tests before committing.
- Every named binding the code uses must exist in station.bindings.json / pre-import.bindings.json.

## Code rules
- Do not add source comments (inline, block, or XML documentation) or nonfunctional source docstrings. Preserve required legal notices and functional compiler/tool directives, shebangs, and encoding declarations; do not alter strings or test fixtures that contain comment-like text.
- Keep agent instructions in AGENTS.md and durable rationale or verification evidence in docs, not in source comments. Never put session history, agent plans, TODO instructions, or speculative limitations in source comments. Existing comments are not authority over user requirements, actual code, or tests.
- No reflection into engine internals except through the documented bridge (FrooxInternalBridge.cs); record the verified Resonite version in docs/ENGINE_COMPATIBILITY.md.
- No empty or bare catch blocks, and no catch used as control flow. Cleanup catches must roll back and rethrow.
- Logic that does not need the engine goes into classes that the test project compiles, with unit tests. Keep UI controllers thin.

## Testing
- Report what you ran and what you did NOT verify. Never claim a pass without running the command.
- The prototype station in a world is only a UI mockup. Real functionality is tested by building the DLL, installing it into Resonite's rml_mods, and fully restarting Resonite, then importing through the real mod.
