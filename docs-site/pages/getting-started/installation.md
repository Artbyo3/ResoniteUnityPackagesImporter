# Installation

This guide describes installation, updates, and rollback for designated development testers and future releases.

!!! warning "Development checkpoint"
    Version 0.0.4 is a stabilization checkpoint, not a general-use release. The first public release is planned for 0.0.5 after further validation. These instructions do not mean a public download is available.

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
