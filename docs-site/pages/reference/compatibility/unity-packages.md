# Unity package content

**Unity `.unitypackage` files: Supported.** The importer extracts the package and reconstructs supported prefab and scene content in Resonite.

**Content conversion: Partial.** Meshes, transforms, rigs, blendshape defaults, and selected material information are handled. Coverage depends on the data and components used by each package.

**Companion material packages: Supported.** If a package separates its materials into another Unity package, the importer can request that companion package during import.

Avatar creation and clothing installation have their own Resonite workflows. See [getting started](../../getting-started/) for the user flow, and the framework or shader pages for feature-specific compatibility.

Keep the original Unity project and package files as the source of truth. Imported results may differ from Unity or VRChat.
