# Unity Package Importer for Resonite

An experimental [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader) mod for importing Unity packages into [Resonite](https://resonite.com/), with an emphasis on VRChat avatars and Modular Avatar clothing.

This fork is under active development. It is useful for testing, but it does not yet reproduce every Unity, VRChat, shader, animation, or Modular Avatar feature.

## Current capabilities

- Safely extracts Unity packages with archive traversal, collision, corruption, and resource-limit checks.
- Reconstructs Unity prefab and scene hierarchies, meshes, transforms, materials, bones, and blendshape defaults.
- Normalizes common meter and centimeter FBX imports.
- Converts a supported subset of lilToon material properties to `XiexeToonMaterial`.
- Generates deterministic shadow-ramp textures when compatible lilToon data is available.
- Resolves material dependencies supplied in a later Unity package without reimporting the model.
- Reads the expression menu, parameters, and animator assets referenced by individual VRChat avatar descriptors.
- Detects supported Modular Avatar metadata, including Merge Armature and Bone Proxy.
- Installs supported Modular Avatar clothing on a selected avatar using a copied source, rollback on failure, and per-outfit installation records.
- Keeps imported prefab roots independent from the importer interface.
- Includes 70 regression tests plus checks against the installed Resonite engine API.

## Known limitations

- Skinned-mesh bounds are improved but can still cull some avatar parts incorrectly. This remains an active investigation.
- Modular Avatar support covers a limited subset. Shape Changer, full menu installation, and many build-time components are still planned.
- VRChat animator and expression behavior is reconstructed only for supported cases.
- VRC PhysBones are not translated yet.
- Poiyomi, VRCFury, custom shaders, and custom Unity editor pipelines are outside the current compatibility target.
- lilToon conversion is an approximation; advanced or animated shader features may be skipped.
- Some packages split models and materials across multiple Unity packages and require the missing-material workflow.

The importer reports unsupported or missing data where it can detect it. Keep the original Unity project and package files as the source of truth.

## Installation

This project currently targets experienced testers.

1. Install [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader).
2. Build the project as described below.
3. Close Resonite.
4. Copy `UnityPackageImporter.dll` from `UnityPackageImporter/bin/Release/` into the Resonite `rml_mods` directory.
5. Start Resonite and drag a `.unitypackage` into the game.

Do not replace the DLL while Resonite or Renderite is running.

## Building

Requirements:

- .NET 10 SDK
- A local Resonite installation
- ResoniteModLoader

The project defaults to the standard Steam installation directory. Override the `GamePath` MSBuild property if Resonite is installed elsewhere.

```powershell
dotnet build UnityPackageImporter/UnityPackageImporter.csproj -c Release
dotnet run --project Tests/Importer.RegressionTests.csproj -c Release
```

The regression suite covers package extraction, caching, prefab overrides, blendshape mapping, scale handling, material diagnostics, avatar asset selection, and Modular Avatar planning. Visual correctness still requires an in-game import test.

## Repository hygiene

Do not commit Unity packages, extracted avatar assets, models, textures from purchased packages, Resonite logs, live-session evidence, credentials, or local configuration. The ignore rules cover common forms of these files, but contributors must review staged changes before committing.

## Credits

Maintained by **Artbyo3**.

Built from the original project by [dfgHiatus](https://github.com/dfgHiatus/ResoniteUnityPackagesImporter) and its contributors.

## License

Licensed under the [MIT License](LICENSE).
