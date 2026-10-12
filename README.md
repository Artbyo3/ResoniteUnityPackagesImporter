# UnityPackageImporter for Resonite

> # ⚠️ NOTICE: AI-DEVELOPED FORK & MODERN OVERHAUL
> **This modern fork, including its stabilization refactors, lilToon material mapping engine, FBX auto-scaling, nested prefab variant resolution, Modular Avatar support, regression test suite, and documentation, was developed 100% using AI (LLM pair-programming agents), built on top of the original human-authored foundation by [dfgHiatus](https://github.com/dfgHiatus/ResoniteUnityPackagesImporter).**
>
> If you are a modder, developer, or curious user wanting to explore or modify this codebase, **we strongly recommend using an AI coding agent** (such as Gemini, Claude, ChatGPT, Cursor, or GitHub Copilot) to navigate and explain the code to you.

---

A [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader) mod for [Resonite](https://resonite.com/) that lets you easily import **Unity Packages (`.unitypackage`)** directly into the game.

This mod is built for everyday users who want to bring their **VRChat avatars, clothes, props, and worlds** into Resonite without having to manually reconstruct materials, re-align hierarchies, or fix broken meshes.

---

## 🎯 What Does This Mod Do?

In standard Unity-to-Resonite workflows, moving an avatar or accessory package usually requires manually setting up shaders, textures, blendshapes, and scaling factors.

With **UnityPackageImporter**, you simply **drag and drop a `.unitypackage` into Resonite**:
1. 📦 **Extracts & Imports:** Assets (models, textures, audio, materials) are unpacked and cached safely.
2. 🦴 **Reconstructs Prefabs:** Reassembles game object hierarchies, bones, transforms, blendshape defaults, and nested prefab variants.
3. 🎨 **Translates Shaders:** Converts Unity shaders (especially **lilToon**) into Resonite's native `XiexeToonMaterial`, keeping your avatar looking gorgeous.
4. 😊 **Preserves Expressions:** Retains avatar facial expressions, blendshape defaults, and visemes.
5. 📏 **Fixes Sizing Issues:** Automatically detects centimeter vs. meter FBX scaling so your clothes and accessories fit your avatar right out of the box.
6. 👗 **Modular Avatar Installer:** Supports attaching compatible Modular Avatar clothing directly to existing avatars in-game with full rollback safety.

---

## 🔍 Current Capabilities

Here is what is currently working in this version:

### 🎨 lilToon Shader Translation
- **Multilayer Texture Compositing:** Automatically combines makeup layers, blush, body tattoos, decals, and eye highlights into unified textures.
- **Soft Shadow Ramps:** Procedurally generates deterministic toon shadow ramps based on your avatar's shadow color and border settings, avoiding harsh black shading.
- **Rim Lighting & Matcaps:** Preserves glowing edges and hair shine highlights.
- **Emission & Glow:** Maps emission masks, colors, and brightness directly to Resonite shaders.
- **Outline Fixes:** Properly maps outline thickness and color masks, preventing dark albedo textures from extinguishing outlines.
- **Missing Material Resolution:** Resolves material dependencies supplied across multiple packages without reimporting models.

### 🦴 Prefab, Mesh & Scale Handling
- **Nested Prefab Variants & Derived FileIDs:** Resolves 64-bit derived XOR fileIDs and modification targets across nested prefab variants, ensuring variant-specific textures and materials are correctly bound.
- **Blendshape Defaults:** Keeps default face slider values (smile, eye shape, ear tilt) without resetting them to 0.
- **Correct FBX Sizing:** Intelligently calculates scale factors so centimeter-mode clothes fit meter-mode avatars without manual resizing.
- **Per-Bone Skinned Mesh Bounds:** Mirrors completed native Resonite FBX imports using mesh static bounds to prevent mesh popping from side angles.
- **Independent Roots:** Spawned avatars and prefabs remain independent of the importer tool window.

### 👗 Modular Avatar & VRChat Metadata
- **Modular Avatar Support:** Detects Merge Armature and Bone Proxy components, automatically matching bones and parenting attachments.
- **Avatar Clothing Installer:** Installs clothing onto a duplicate target avatar with non-destructive backup and automatic rollback on failure.
- **VRChat Avatar Descriptors:** Reads expression menus, parameters, and animator controllers referenced by avatar descriptors.

### 🛡️ Core Reliability & Testing
- **Secure File Extraction:** Hardened against zip-slip / path traversal and corrupted package archives.
- **Smart Asset Caching:** Skips re-extracting assets you have previously imported, saving disk space and import time.
- **In-Engine Station & UI:** Bundled native station and pre-import prompt packages with English and Japanese localization.
- **137 Automated Regression Tests:** Every core feature is verified with automated test suites covering extraction, prefab overrides, blendshapes, FBX scaling, lilToon translation, and Modular Avatar planning.

---

## 🔮 Known Limitations & Roadmap

This fork is under active development. Keep the original Unity project and package files as the source of truth.

- **Modular Avatar:** Currently covers Merge Armature and Bone Proxy. Shape Changer, full expression menu installation, and build-time components are planned.
- **PhysBones:** VRC PhysBones are not yet converted to Dynamic Bone / Resonite equivalents.
- **Other Shaders:** Poiyomi, VRCFury, custom shaders, and complex Unity render pipelines are outside the current target.
- **lilToon Approximations:** Advanced or animated shader features (refraction, fur, animated UVs) may be skipped.

---

## 🚀 Installation (For Players)

1. Make sure you have [ResoniteModLoader (RML)](https://github.com/resonite-modding-group/ResoniteModLoader) installed.
2. Download `UnityPackageImporter.dll` from the Releases page.
3. Place `UnityPackageImporter.dll` into your `rml_mods` folder:
   - Default path: `C:\Program Files (x86)\Steam\steamapps\common\Resonite\rml_mods`
4. Launch Resonite.
5. Drag and drop any `.unitypackage` into your Resonite window!

> ⚠️ **Note:** Do not replace or modify the DLL while Resonite is running.

---

## 🛠️ For Developers & Modders

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Local Resonite installation with ResoniteModLoader

### Build Commands
```powershell
# Build Release version
dotnet build UnityPackageImporter/UnityPackageImporter.csproj -c Release

# Run the 137 automated regression tests
dotnet run --project Tests/Importer.RegressionTests.csproj -c Release
```

The MSBuild project defaults to the standard Steam installation directory (`C:\Program Files (x86)\Steam\steamapps\common\Resonite`). If Resonite is installed elsewhere, override the `GamePath` property:
```powershell
dotnet build UnityPackageImporter/UnityPackageImporter.csproj -c Release -p:GamePath="D:\Games\Resonite"
```

---

## 🧹 Repository Hygiene

Do not commit `.unitypackage` files, extracted avatar assets, proprietary 3D models, textures, Resonite logs, runtime credentials, or personal configuration files. The `.gitignore` is configured to exclude these, but contributors should always review staged files before committing.

---

## 📄 License & Credits

Maintained by **Artbyo3**.
Built from the original project by [dfgHiatus](https://github.com/dfgHiatus/ResoniteUnityPackagesImporter) and its contributors.

This project is licensed under the **MIT License** — see the [LICENSE](LICENSE) file for details.
