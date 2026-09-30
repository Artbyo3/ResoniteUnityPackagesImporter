# Dead-code review — 2026-09-30

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
