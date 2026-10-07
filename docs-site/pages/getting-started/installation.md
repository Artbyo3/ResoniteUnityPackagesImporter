# Install the mod

The importer is an experimental ResoniteModLoader mod. There is currently no packaged build listed on [GitHub Releases](https://github.com/Artbyo3/ResoniteUnityPackagesImporter/releases), so the steps below describe installing a build compiled from source.

## Requirements

- Resonite
- [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader)
- A source build of the Unity Package Importer

## Steps

1. Install [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader) if it is not already installed.
2. Build the importer by following the [project build instructions](https://github.com/Artbyo3/ResoniteUnityPackagesImporter#building). The build outputs are in `UnityPackageImporter/bin/Release/`.
3. Fully close Resonite before copying or replacing mod files.
4. Copy `UnityPackageImporter.dll` into Resonite's `rml_mods` folder.
5. Copy `YamlDotNet.dll` into Resonite's `rml_libs` folder. It is a dependency of the importer, so keep it separate from the mod DLL. ResoniteModLoader uses `rml_libs` for additional mod libraries; see the [Resonite mod installation guide](https://wiki.resonite.com/Installing_mods).
6. Start Resonite. You can then [make your first import](index.md).

The `rml_mods` folder is inside your Resonite installation directory. If you are unsure where Resonite is installed, use your game launcher to locate its installation before copying the files.

For the licenses of included third-party components, see [Legal → third-party notices](../legal/third-party-notices/). Distributions that include those components should carry the notice file.

## Updating

Close Resonite, rebuild or download the update, and replace each file in its corresponding folder. Start Resonite again after both files have been updated. If a future release packages the dependency differently, follow the instructions provided with that release.
