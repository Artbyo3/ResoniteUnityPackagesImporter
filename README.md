# UnityPackageImporter for Resonite

> # ⚠️ NOTICE: 100% AI-GENERATED CODEBASE
> **This entire mod, its core algorithms, bugfixes, refactors, and documentation were written 100% by AI (LLM pair-programming agents).**
>
> If you are a modder, developer, or curious user wanting to explore or modify this codebase, **we strongly recommend using an AI coding agent** (such as Gemini, Claude, ChatGPT, Cursor, or GitHub Copilot) to navigate and explain the code to you.

---

A [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader) mod for [Resonite](https://resonite.com/) that lets you easily import **Unity Packages (`.unitypackage`)** directly into the game.

This mod is built for everyday users who just want to bring their **VRChat avatars, clothes, props, and worlds** into Resonite without having to manually reconstruct materials, re-align hierarchies, or fix broken meshes.

---

## 🎯 What Does This Mod Do?

In standard Unity-to-Resonite workflows, moving an avatar or accessory package usually requires manually setting up shaders, textures, blendshapes, and scaling factors.

With **UnityPackageImporter**, you simply **drag and drop a `.unitypackage` into Resonite**:
1. 📦 **Extracts & Imports:** Assets (models, textures, audio, materials) are unpacked and cached safely.
2. 🦴 **Reconstructs Prefabs:** Reassembles the game object hierarchies, bones, positions, and rotations.
3. 🎨 **Translates Shaders:** Converts Unity shaders (especially **lilToon**) into Resonite's native `XiexeToonMaterial`, keeping your avatar looking gorgeous.
4. 😊 **Preserves Expressions:** Retains avatar facial expressions, blendshape defaults, and visemes.
5. 📏 **Fixes Sizing Issues:** Automatically detects centimeter vs. meter FBX scaling so your clothes and accessories fit your avatar right out of the box.

---

## 🔍 Current Scope (What Works Right Now)

Here is what is currently working in this version:

### 🎨 lilToon Shader Translation
- **Multilayer Texture Compositing:** Automatically combines makeup layers, blush, body tattoos, decals, and eye highlights into unified textures.
- **Soft Shadow Ramps:** Procedurally generates custom toon shadow ramps based on your avatar's shadow color and border settings, avoiding harsh black shading.
- **Rim Lighting & Matcaps:** Preserves glowing edges and hair shine highlights.
- **Emission & Glow:** Maps emission masks, colors, and brightness directly to Resonite shaders.
- **Outline Fixes:** Properly maps outline thickness and color masks, preventing dark albedo textures from extinguishing outlines.

### 🦴 Prefab, Mesh & Scale Handling
- **Blendshape Defaults:** Keeps default face slider values (smile, eye shape, ear tilt) without resetting them to 0.
- **Correct FBX Sizing:** Intelligently calculates scale factors so centimeter-mode clothes fit meter-mode avatars without manual resizing.
- **Per-Bone Skinned Mesh Bounds:** Prevents avatar body parts, clothing, or hair from disappearing when looking at them from side angles.
- **Independent Roots:** Spawned avatars and prefabs remain independent of the importer tool window.

### 🛡️ Core Reliability & Performance
- **Secure File Extraction:** Hardened against path traversal and corrupted package archives.
- **Smart Asset Caching:** Skips re-extracting assets you have previously imported, saving disk space and import time.
- **52 Automated Regression Tests:** Every core feature is verified with automated tests.

---

## 🔮 Upcoming Roadmap (Planned Scope)

We are actively working on expanding support:
- [ ] **Modular Avatar Support:** Automatically merge bones, attach clothing items, and map Modular Avatar menu parameters onto the base avatar.
- [ ] **Poiyomi Toon Shader Translation:** Support for Poiyomi features (panosphere, glitter, audiolink color shifts, advanced masking).
- [ ] **Advanced lilToon Shading:** Glass/gem refraction, fur shading, and animated texture properties.
- [ ] **One-Click Avatar Rigging:** Automatic hookup for Resonite's Avatar Creator / biped humanoid setup.

---

## 🚀 Easy Installation (For Regular Users)

1. Make sure you have [ResoniteModLoader (RML)](https://github.com/resonite-modding-group/ResoniteModLoader) installed.
2. Go to the [Releases](https://github.com/Artbyo3/ResoniteUnityPackagesImporter/releases) page and download `UnityPackageImporter.dll`.
3. Place `UnityPackageImporter.dll` into your `rml_mods` folder:
   - Default path: `C:\Program Files (x86)\Steam\steamapps\common\Resonite\rml_mods`
4. Launch Resonite.
5. Drag and drop any `.unitypackage` into your Resonite window!

---

## 🛠️ For Developers & Modders

If you want to build or modify this mod yourself:

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Resonite installed with ResoniteModLoader

### Build Commands
```powershell
# Build Release version
dotnet build UnityPackageImporter.sln -c Release

# Run the 52 automated regression tests
dotnet run --project Tests/Importer.RegressionTests.csproj -c Release
```

> 💡 **Developer Tip:** If you have questions about how any part of this mod works or want to add a new feature, prompt your favorite AI coding assistant (like Gemini, Claude, ChatGPT, or Cursor) with the codebase—it was built by AI and is structured to be easily read and modified by AI!

---

## 📄 License

This project is licensed under the **MIT License** — you are free to use, copy, modify, merge, publish, distribute, and sublicense this software as you see fit. See the [LICENSE](LICENSE) file for the full license text.
