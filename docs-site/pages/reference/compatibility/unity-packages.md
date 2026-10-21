# Unity package content

**Unity `.unitypackage` files: Supported.** The importer extracts a package and reconstructs supported prefab and scene content in Resonite.

**Content conversion: Partial.** Meshes, transforms, rigs, blendshape defaults, and selected material information are handled. Coverage depends on the data and components used by each package.

**Companion material packages: Supported.** If a package separates its materials into another Unity package, the importer can request that companion package during import.

## Missing meshes and nested prefabs

- A renderer that references a missing mesh subasset in an imported model is skipped with a warning; its owning object is kept.
- An unsupported plain nested prefab can be deferred only when its source and nested dependencies can be verified to contain no meshes. Its content and operations are not expanded or executed.
- A scene prefab instance whose source asset is absent from the package is skipped with a warning.

These warnings indicate partial compatibility and do not by themselves block avatar creation. Real reconstruction failures, missing model files, and mesh-bearing or unverifiable unsupported nested prefabs still block output.

## Before evaluating a package

Check whether required materials are included or supplied separately. If a package separates materials, the importer can request a [companion package](../../importing/companion-packages/) during import.

Avatar creation and clothing installation have their own Resonite workflows. The framework and shader pages describe feature-specific compatibility.

Keep the original Unity project and package files as the source of truth. Imported results may differ from Unity or VRChat.
