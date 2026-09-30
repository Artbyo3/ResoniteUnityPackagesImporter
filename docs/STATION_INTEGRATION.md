# Bundled station integration

The approved native station and pre-import prompt are the visual source of truth. The mod loads exported Resonite packages, validates a versioned binding contract, and supplies runtime data. It no longer looks for a prototype in the world, caches its raw BSON once, or generates a procedural showroom fallback.

## Current checkpoint

The C# loader and controllers have been implemented. The baseline source is recoverable at local commit `0387424`. Compile checks and regression checks are separate from native runtime validation.

The native exports were supplied and bundled on 2026-09-30 as template version `1.0.1`. The first installed candidate failed before extraction while loading the pre-import prompt. The engine imports the saved root into the supplied slot; the loader incorrectly searched only that slot's descendants. The corrected loader keeps an inactive wrapper, imports into a dedicated child, and validates that child itself before resolving the controls. It also captures native import failures through `IProgressIndicator`, because the engine can report a failure without throwing. Both UI packages remain unchanged. The corrected Release build passes; a new in-game run is still required to validate the mod-generated station and its controls.

All 99 bindings were checked against the serialized exports. Their object references are internal, all local asset URLs resolve to packaged assets, and both root License components remain present. The retired advanced layout, its 20 obsolete drivers, and 14 obsolete localization targets have been removed from the station package. The old clothing-install context-menu factory is deleted; station installation and optional outfit controls remain. Shared localization drivers retain their live targets. The pre-import filename binding updates the full source used by the approved native fitter, rather than writing to its driven display label.

The 74 C# regression tests and six native-bundle preparation checks pass. The `--ui-bundle` audit verifies the actual Release DLL's embedded hashes, contract versions, assets, and stripped account metadata without executing game code. Compilation and structural checks do not replace the runtime checklist below.

## Finish the bundle

1. Save the approved station and the approved new pre-import prompt in Resonite.
2. Use Resonite Files to export each as **Resonite Package (+variants)**, collecting assets. Export only the UI items, without avatar or clothing content.
3. Put the exports in `UnityPackageImporter/UI/Templates/`, named `station.ResonitePackage` and `pre-import.ResonitePackage`.
4. Run `python scripts/Prepare-StationTemplates.py`. This validates native ZIP entries and asset hashes, removes account metadata from record files, normalizes extension casing, and atomically writes `manifest.json`. Original exports are copied and hash-verified under the ignored `private/ui-export-originals/` directory before any metadata change. UI object and asset payloads are preserved byte for byte.
5. Build normally, with no validation bypass. The DLL embeds both packages, both contracts, and the manifest.
6. Test a fresh world without either prototype present. Verify import/raw/cancel, multiple prefabs, created-avatar persistence, clothing installation ownership checks, companion-package matching, both languages, scroll/drag behavior, shader reset, and the approved lifecycle.

Run the offline bundle checks with:

```text
python -B -m unittest discover -s Tests -p test_station_templates.py -v
dotnet run --project Tests/Importer.RegressionTests.csproj -- --ui-bundle UnityPackageImporter/bin/Release/UnityPackageImporter.dll UnityPackageImporter/UI/Templates
```

The default build fails if either export or the manifest is missing. A compile-only build using `SkipUITemplateValidation=true` must never be installed. Build paths in the assembly's debug metadata are mapped to `/_/` so the DLL does not disclose the developer's local account path.

If a template or required control is missing, loading fails explicitly. A differently styled fallback is not substituted.

## UI updates during long sessions

Keep a saved editable master and a backup before each UI revision. Use Resonite's native save/export so fonts, sprites, materials, localization, and ProtoFlux remain intact. JSON hierarchy snapshots are diagnostic evidence, not object backups.

The `uiTemplateDevelopmentDirectory` configuration key is an explicit development override. Point it to a directory containing both native packages and their contracts. Each new import reads the current export and gets a fresh instance. Existing stations retain their own template and state; changing the template does not alter an import already in progress. Loading a changed DLL still requires a game restart.

Loaded templates receive an `Importer UI Bindings` child containing native `ReferenceField<IWorldElement>` entries. These references remap during duplication/export and allow referenced controls to move while keeping their roles. The loader first uses valid internal references, then falls back to the exact versioned path/type/ordinal contract. Ambiguous paths and missing controls fail validation. Never rename role entries casually or remove the metadata from an authoring copy derived from a loaded template.

When promoting a UI revision, give both binding contracts the same new `templateVersion`, re-export, prepare the hashes, test, and create a local Git checkpoint. GitHub publication remains a separate user decision.

## Runtime behavior

- Extraction begins only after choosing Import Unity Package. Raw-file import bypasses the Unity-package interception; Cancel performs no extraction.
- Import replaces the prompt only after the station loads successfully. Errors propagate through the actual import task.
- Prefab progress is aggregated rather than sharing a progress interface that each concurrent prefab can complete independently. `SmoothValue<float>.TargetValue` drives the ring; the visual arc is never written directly.
- Success holds for 3.5 seconds, then the native card transition and the ring dissolve together. Failure retains its progress until dismissal.
- Imported sources are world objects with independent transforms and lifetimes. The pedestal shows a disposable preview copy with grabbing blocked. Creating an avatar activates the independent source. Closing/deleting the station restores remaining sources.
- Navigation uses the actual imported prefab list. The avatar chooser duplicates the approved row style and text fitting for owned, active rigs; ownership is checked again at installation time.
- Companion waiting is explicitly armed by its button and tied to that import. Another dropped package becomes a candidate. GUID matching restores materials, mismatches stay in the waiting flow, and Back disarms waiting without losing the import.
- English/Japanese runtime labels update the native localization sources. Brand names, filenames, user names, and shader identifiers keep their original wording.

## Capability limits requiring review

The UI is not evidence that an unsupported importer feature works. Shape Changer is not parsed by the current importer, so its switch is disabled and marked unavailable. Blendshape Sync also remains unsupported. The outfit-menu switch currently adds the existing native per-outfit enable/disable control; it does not implement arbitrary MA menu/parameter merging.

The shader picker supports Automatic and a generated Xiexe Toon alternative. It reuses the existing material translator, whose fallback behavior and visual fidelity still need in-game review. Automatic restores captured assignments. Receipts are currently native string records under the output, rather than a separate polished receipt panel.

Scene-only and assets-only packages use a completion state without pretending an example avatar was imported. Native scene import progress remains owned by the existing scene importer.

Snapshots and live session IDs remain in ignored local scratch storage. Installation is performed only when requested and Resonite is closed. No recurring installer or GitHub publication is part of this checkpoint.

## Removing retired authoring layouts

`Tools/UITemplateTool` uses the installed Elements.Core native data-tree serializer to remove the exact inactive retired advanced layout from a station export. It removes only drivers whose targets were deleted and trims those targets from shared localization drivers. Unexpected retained dependencies abort cleanup. Original native exports and contracts are backed up before replacement. Runtime bindings are recalculated by their existing object references, so component ordinal changes do not silently point at the wrong controls. This development tool does not create UI or enter the running world.

If an authoring master still contains that retired layout, run the tool after exporting, then prepare the manifest again:

```text
dotnet run --project Tools/UITemplateTool -- UnityPackageImporter/UI/Templates/station.ResonitePackage UnityPackageImporter/UI/Templates/station.bindings.json private/ui-export-originals "<Resonite installation directory>"
python scripts/Prepare-StationTemplates.py
```

Old UI factory code is not kept as a runtime fallback. The native engine progress indicators used for scenes and non-station importer calls are still used import functionality.
