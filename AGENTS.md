# Workflow (read first)

- Work locally. Do NOT push, open a PR, or create extra branches unless the
  user explicitly says "ship it". One branch per task.
- Before reporting: run Test and Build below. Never say "done" or "works";
  say "ready for live test". Only the user's live test in Resonite confirms it.
- Flow: change -> Test + Build -> Install -> user tests live -> user approves -> ship (one PR).
- Never delete, skip or weaken tests to get a pass.
- When your task ends, remove your worktree and delete your merged branch.
- Throwaway scripts go in `scratch/` (gitignored), never in `scripts/`.
- If you rename or change a command below, update this table in the same commit.

## Commands (use these; do not search for alternatives)

| Task | Command |
|---|---|
| Test | `dotnet run --project Tests/Importer.RegressionTests.csproj -c Release` |
| Build | `dotnet build UnityPackageImporter/UnityPackageImporter.csproj -c Release --no-incremental` |
| UI audit | `dotnet run --project Tests/Importer.RegressionTests.csproj -- --ui-bundle <built dll> UnityPackageImporter/UI/Templates` |
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
- No reflection into engine internals except through the documented bridge (FrooxInternalBridge.cs); note the Resonite version it was verified on.
- No empty or bare catch blocks, and no catch used as control flow. Cleanup catches must roll back and rethrow.
- Logic that does not need the engine goes into classes that the test project compiles, with unit tests. Keep UI controllers thin.

## Testing
- Report what you ran and what you did NOT verify. Never claim a pass without running the command.
- The prototype station in a world is only a UI mockup. Real functionality is tested by building the DLL, installing it into Resonite's rml_mods, and fully restarting Resonite, then importing through the real mod.
