# What can I import?

**Humanoid avatars, supported outfits, and prefab or scene content from Unity packages.** The importer reconstructs supported data in Resonite; it does not run Unity or reproduce every VRChat behavior.

<!-- component-support-map -->

## What transfers, and what does not

<div class="docs-coverage" markdown="1">

### [Avatars](../../importing/avatars.md)

Meshes, transforms, humanoid rigs, blendshape defaults, and supported materials. Creation depends on the resolved assets and the station's structural checks, not just the package extension.

### [Clothing and props](../../importing/clothing.md)

Outfits using Modular Avatar Merge Armature or Bone Proxy, and supported structural VRCFury Armature Link operations. The target avatar must be yours; installing on another user's avatar is rejected.

### [Materials](shaders/index.md)

Standard metallic materials convert to native PBS. lilToon and Poiyomi have supported mappings, with visual limits. Unity shader code, animated effects, and arbitrary vertex manipulation do not carry over as executable shaders.

### [Build-time components](modular-avatar.md)

Selected structural operations and initial appearance transfer. Initial blendshape weights, visibility or materials are not live controls. Full menus, runtime toggles, animation-controller behavior and contact interactions are not recreated. The dedicated MA and VRCFury inventories give the exact boundaries. The SDK inventory here covers VRChat-authored components, not those build-time tools.

</div>

## Before evaluating a package

- **Have the dependencies.** Separate material packages can be supplied through the station's companion-package prompt.
- **Keep the originals.** Your Unity project and package files remain the source of truth.
- **Read the result.** A visual limitation differs from a structural blocker. The station reports both; partial compatibility is not a promise that an item can be created.
- **Protect private content.** Use a private or local session. Inactive staging is world content, not a local-only sandbox.

The map combines compatibility and delivery tracking. Missing support is not a delivery commitment. Project activity is separate from compatibility: a closed issue is not proof of released, verified support.
