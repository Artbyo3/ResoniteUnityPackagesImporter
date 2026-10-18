# VRCFury

**Status: Partial.** The importer translates supported structural VRCFury features into shared avatar operations during import. Other VRCFury features are deferred or unsupported, and their underlying assets are imported without those behaviors.

### Supported features

- **Armature Link (`VRCFuryArmatureLink`):**
  - **Recursive merge:** translated into structural armature merge operations (`ArmatureMergeOperation`), preserving target bone/offset resolution and link transforms (position, rotation, scale, auto-scale).
  - **Single bone attachment:** translated into bone attachment operations (`BoneAttachmentOperation`), maintaining bone targeting and offset paths.

### Deferred / Unsupported features

- **Non-structural features:** Toggles, blendshape drivers, props, menus, contact receivers, and animation controllers are not yet executed. When detected, they produce diagnostic warnings in the import plan and the base meshes, materials, and textures are imported without those behaviors.
