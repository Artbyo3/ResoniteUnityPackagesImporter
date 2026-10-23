# Installation

This guide covers prerequisites, installation, configuration, updates, and uninstallation for the Unity Package Importer.

## Prerequisites

- **Resonite**: A valid installation of Resonite on Windows.
- **ResoniteModLoader (RML)**: Installed and configured for your Resonite installation.

## Clean installation

[Download the latest ZIP](https://github.com/Artbyo3/ResoniteUnityPackagesImporter/releases/latest/download/UnityPackageImporter-testing-build.zip){ .md-button .md-button--primary }
[Release notes and individual DLLs](https://github.com/Artbyo3/ResoniteUnityPackagesImporter/releases/latest){ .md-button }

The ZIP includes the mod and its YamlDotNet dependency. The download link always follows the latest published release.

1. Exit Resonite completely and back up your current mod files.
2. Download and extract `UnityPackageImporter-testing-build.zip`.
3. Copy the extracted `rml_mods` and `rml_libs` folders into your Resonite installation:
    - `rml_mods/UnityPackageImporter.dll` is the mod.
    - `rml_libs/YamlDotNet.dll` is its dependency; replace it with the supplied copy when updating.
4. Launch Resonite with ResoniteModLoader enabled.
5. Check the mod log to confirm that `UnityPackageImporter` loaded without startup errors.

For optional download verification, compare the extracted `UnityPackageImporter.dll` SHA256 with the value in the release notes.

## Updating

Version 0.0.6 changes the Harmony identity to Artbyo3.UnityPackageImporter. Replace the existing DLL rather than installing a second renamed copy. Do not keep another UnityPackageImporter build in the mod directory. Existing settings use the same mod name and keys; back up your configuration before updating.

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
| `importAsRawFiles` | `false` | When enabled, imports package files directly through Resonite's native file importer instead of processing them through the importer station. |

