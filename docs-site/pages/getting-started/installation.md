---
title: Install & update
---

# Install and update

These instructions cover **v0.0.6**, published October 23, 2026. Install the mod and its dependency, then fully restart Resonite.

## Prerequisites

- **Resonite**: A valid installation of Resonite on Windows.
- **[ResoniteModLoader (RML)](https://github.com/resonite-modding-group/ResoniteModLoader)**: Installed and configured for your Resonite installation.

## Install the mod

[Download v0.0.6](https://github.com/Artbyo3/ResoniteUnityPackagesImporter/releases/download/v0.0.6/UnityPackageImporter-v0.0.6.zip){ .md-button .md-button--primary }
[Release notes and individual DLLs](https://github.com/Artbyo3/ResoniteUnityPackagesImporter/releases/tag/v0.0.6){ .md-button }

The versioned ZIP includes the mod, its YamlDotNet dependency, and license notices. This download matches the compatibility information in these docs.

1. Exit Resonite completely and back up your current mod files.
2. Download and extract `UnityPackageImporter-v0.0.6.zip`.
3. Copy the extracted `rml_mods` and `rml_libs` folders into your Resonite installation:
    - `rml_mods/UnityPackageImporter.dll` is the mod.
    - `rml_libs/YamlDotNet.dll` is its dependency; replace it with the supplied copy when updating.
4. Launch Resonite with ResoniteModLoader enabled.
5. Keep the included license and third-party notices. Remove renamed duplicate importer DLLs; do not run old and new mod identities together.
6. Check the mod log to confirm that `UnityPackageImporter` loaded without startup errors.

For optional download verification, compare the extracted DLLs with `SHA256SUMS.txt` from the same release. A matching checksum confirms the downloaded files, not import compatibility.

## Updating

Replace the existing build rather than keeping a second renamed copy. Back up your configuration and read the target release's notes before updating.

1. Exit Resonite completely.
2. Replace `UnityPackageImporter.dll` in `rml_mods` with the new version.
3. If new dependencies are included in the release, update the corresponding files in `rml_libs`.
4. Relaunch Resonite.

## Rollback and uninstallation

- **Rollback**: To revert to an earlier version, restore the importer and dependency from the same earlier release or your backup while Resonite is closed.
- **Uninstallation**: Remove `UnityPackageImporter.dll` from `rml_mods`. If no other mods require `YamlDotNet.dll`, you may optionally remove it from `rml_libs`.

## Configuration

The defaults are intended for the station workflow. These optional settings are available through ResoniteModLoader:

| Setting | Default | Description |
| --- | --- | --- |
| `importPrefab` | `true` | Intercepts dropped `.unitypackage` files to stage and reconstruct supported prefabs and scenes in the station. |
| `dumpPackageContents` | `false` | When enabled alongside `importPrefab`, extracts and imports all package files into the world rather than just prefabs. |
| `importAsRawFiles` | `false` | When enabled, imports package files directly through Resonite's native file importer instead of processing them through the importer station. |
