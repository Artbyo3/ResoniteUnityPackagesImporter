# Dead-code reviews

## Review branch follow-up — 2026-10-07

This pass is on `codex/importer-stability`. It changes source organization and
removes confirmed unused plumbing; it does not claim to resolve rendering,
tracking or material-fidelity issues that need a live comparison.

- The mod entry point now owns configuration and engine registration.
  `UniversalImporterPatch` owns Harmony interception and mixed-drop completion.
  `PackageImportCoordinator` owns extraction, project scheduling and the
  prompt/station lifecycle. The existing public extraction method delegates to it.
- `HumanoidRigSetup` now owns the shared per-instance humanoid preparation used
  by both scenes and prefabs. It is separate from native avatar creation and
  retains the existing rig logic. The old misspelled helper was replaced at all
  three call sites.
- Removed `allimportsroot` from the structure-import interface and its two
  implementations, along with the unused constructor argument. Actual staging
  ownership remains in `ImportSession`.
- Removed the unread `MissingMaterialBinding.RendererName` property. Renderer
  references and material restoration remain in use.
- Replaced the large two-way Unity class catalogue with four typed model class
  IDs. The catalogue was used only to match FBX metadata, not to dispatch YAML
  components. Both class names and their numerical IDs are preserved.
- Removed the stale `UnityPackageImporter/TODO.txt`. Remaining nested-prefab,
  scale and incomplete-conversion questions are retained in
  [the stability review](IMPORTER_STABILITY_REVIEW.md); implemented items are no
  longer repeated in an obsolete task list.

Whole-repository reference searches and call-site review established the unused
members. This was not a new exhaustive Roslyn dead-code scan. YAML model fields,
partial component handlers, config attributes and engine callbacks remain
because direct-call counts do not establish whether they are unused.

Moved package and humanoid method bodies were compared with the previous commit
after normalizing only their relocated symbols. Their control flow, cleanup,
cancellation and world/background await boundaries are unchanged. Runtime
validation remains necessary before merging this branch.

Validation for this follow-up: the production Release build has zero warnings
and errors, all 100 C# regressions pass, and all seven engine API metadata checks
pass. The test harness still emits existing nullable-analysis warnings in linked
production sources and fixtures; this refactor does not change those files.
Compiled Harmony target/parameter bindings and config registration metadata match
the pre-cleanup build, as do engine initialization instructions. The installed
mod's hash still matches that pre-cleanup build; this pass is not installed.

## Original review — 2026-09-30

The review used the project's actual compiler inputs and Roslyn semantic references, followed by manual checks for YAML deserialization, interface dispatch, Harmony patches, configuration registration, and engine reflection. A missing direct caller was treated as a review candidate, not sufficient evidence for deletion.

## Removed

- `UnityStructureImporter.cs`, the old importer already excluded from compilation, and its redundant project exclusion.
- `TarTarSource.cs`, the unused manual TAR reader. Extraction continues through `UnityPackageExtractor` and `System.Formats.Tar`.
- The old `Publicizer.Annotation` and `Publicizer.Runtime` source helpers. The active Krafs.Publicizer build package and its access-check attributes remain in use.
- The uncalled file-filter method and its eight configuration keys, which never affected the current importer. Active prefab import, package-content dumping, raw-file import, and template-development settings remain.
- Package-wide expression-menu guessing, superseded by loading the menu and parameters referenced by the selected avatar.
- Index-based blendshape default mapping, superseded by name-based mapping that survives stripped or reordered shapes. The relevant tests now exercise the active mapper.
- Unused 32-bit and stream hashing variants. The 64-bit buffer hash used for Unity object IDs and its helpers are unchanged.
- Unread task flags and metadata maps, uncalled convenience methods, ignored parameters, unused locals/imports, and the commented-out obsolete bounds experiment.

## Retained deliberately

Unity YAML fields, parsed VRChat metadata, and JSON contract fields can be populated by deserialization. Interface methods, event callbacks, and Harmony patches can be invoked indirectly. These were preserved, along with scene import, engine reflection bridges, native progress indicators, installation records, and cleanup of menus copied from older imported items. Partial handlers were not deleted or reclassified as finished features.

## Validation

- Normal Release build: zero warnings and errors.
- All 74 C# regression cases and six bundle-preparation checks pass.
- Embedded native UI hashes, binding versions, assets, and privacy checks pass.
- A second semantic scan reports no compiler errors or unnecessary imports. Remaining candidates are indirect entrypoints or data-model members retained after review.

The approved native UI packages were not edited. The review did not execute the mod inside Resonite; an in-game import remains necessary to validate the integration candidate. No installation, recurring automation, or GitHub publication is part of this cleanup.
