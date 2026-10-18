# Companion packages

Avatars often distribute shaders, materials, textures, or accessories in separate packages. The importer can merge companion packages into an active station to recover missing dependencies.

## Companion resolution

1. **Missing dependency detection**: If an imported avatar references textures or materials that were not included in the primary package, the station prompts you to import companion packages.
2. **Companion intake**: Drag and drop companion `.unitypackage` files into the world while the station is open.
3. **Automatic recovery**: The importer extracts the companion package, indexes its assets, and re-resolves pending material slots and texture assignments.
4. **Conflict rejection**: If a companion package contains an asset with a GUID identical to an existing asset but with differing payload content or metadata, the conflict is rejected symmetrically. The importer prevents arbitrary overwriting and logs the ambiguity.
