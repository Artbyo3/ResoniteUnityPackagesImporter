# Installation

This guide covers prerequisites, installation, configuration, updates, and uninstallation for the Unity Package Importer.

## Prerequisites

- **Resonite**: A valid installation of Resonite on Windows.
- **ResoniteModLoader (RML)**: Installed and configured for your Resonite installation.

## Clean installation

1. Download the release archive (`UnityPackageImporter-x.x.x.zip`).
2. Verify the checksum of the downloaded archive against the release notes.
3. Extract the contents:
    - Place `UnityPackageImporter.dll` into your Resonite `rml_mods` folder.
    - Place `YamlDotNet.dll` into your Resonite `rml_libs` folder (if not already present).
4. Launch Resonite with ResoniteModLoader enabled.
5. In your mod logs, verify that `UnityPackageImporter` is loaded and reports no startup errors.

## Updating

1. Exit Resonite completely.
2. Replace `UnityPackageImporter.dll` in `rml_mods` with the new version.
3. If new dependencies are included in the release, update the corresponding files in `rml_libs`.
4. Relaunch Resonite.

## Rollback and uninstallation

- **Rollback**: To revert to an earlier version, replace `UnityPackageImporter.dll` with the previously downloaded DLL.
- **Uninstallation**: Remove `UnityPackageImporter.dll` from `rml_mods`. If no other mods require `YamlDotNet.dll`, you may optionally remove it from `rml_libs`.

## Configuration

The importer supports configuration options through ResoniteModLoader. You can customize them in your mod configuration file or in-game mod settings:

| Setting | Default | Description |
| --- | --- | --- |
| `importPrefab` | `true` | Intercepts dropped `.unitypackage` files to stage and reconstruct supported prefabs and scenes in the station. |
| `dumpPackageContents` | `false` | When enabled alongside `importPrefab`, extracts and imports all package files into the world rather than just prefabs. |
| `importAsRawFiles` | `false` | Imports files inside unity packages as raw binary assets directly. |

