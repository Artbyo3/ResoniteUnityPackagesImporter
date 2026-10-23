# Changelog

## 0.0.6

- Change the mod maintainer and Harmony identity to Artbyo3, keeping all previous contributors in the in-game author field and preserving license notices.
- Prepare Resolute catalog metadata and pinned release artifacts, including YamlDotNet and license notices.
- Generate checksums and a local submission folder from verified release files.
- Keep the existing manual-install ZIP download name available alongside the versioned ZIP.

## 0.0.5

- Check engine compatibility at startup and warn when engine updates may break import hooks.
- Fill in missing translations for import result messages.
- Fix material picker navigation, search, and materials created during an import.
- Refresh material thumbnails when materials or textures change and clean up old cache files.
- Keep accordion groups independent when their names match.
- Resolve nested prefab object IDs that could incorrectly block avatar creation.
- Skip dangling mesh references in an imported model with a warning instead of blocking the whole avatar.
- Defer unsupported nested prefabs without meshes and their operations with a warning.
- Warn about referenced prefabs absent from the package instead of blocking otherwise valid output.
- Improve error reporting and keep the mod loading after an asset-hook registration failure.
- Update the station UI bundle and English and Japanese documentation.
- Check template binding contracts in CI to catch missing or renamed controls.

Real reconstruction failures still block output. Full expansion of unsupported nested prefabs is not included.
