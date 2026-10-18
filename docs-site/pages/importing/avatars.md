# Importing avatars

The importer reconstructs humanoid avatars from Unity packages and stages them for review before releasing them into the world.

## Import workflow

1. **Package intake**: Drag and drop a `.unitypackage` file into Resonite. A pre-import prompt asks whether to extract the package through the importer station or import raw files directly.
2. **Station staging**: When you choose the station, the package is extracted to local cache and staged.
3. **Review and customize**: The station tablet displays each detected avatar or prefab. You can inspect the 3D turntable preview, switch material shader presets (Automatic, Toon, PBS), adjust textures, and view detected warnings or limitations.
4. **Avatar creation**: Selecting **Create avatar** completes humanoid rig calibration, attaches native IK references and eye setup, binds optional expression menus, applies native avatar protection (if selected), and moves the avatar out of staging into the world.

!!! warning "Staging hierarchy is world content"
    While an import is in progress, staged objects reside under an inactive global hierarchy in the world. Inactive staging is not an isolated local-only sandbox; other users present in the session can observe world state changes. If you are importing sensitive or private assets, perform the import in a private or local session.

## Material and shader handling

- **Shader presets**: Each item defaults to **Automatic** conversion, which selects Toon or PBS based on the source material properties. You can also explicitly choose **Toon** or **PBS**.
- **Item-level limitation warnings**: Visual approximations (such as unsupported blend modes, dynamic UV scrolling, unbaked lilToon layers, or second emission maps) are tracked as non-blocking limitations (`IsPartial = true`). The avatar remains structurally ready and can be created normally.
- **In-world receipts**: When enabled, the importer attaches a receipt to the avatar slot detailing the package source, assigned materials, and any visual limitations.
