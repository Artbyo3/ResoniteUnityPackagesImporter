# Modular Avatar

**Overall status: Partial.** The importer handles selected Modular Avatar behavior. Other build-time components may import their underlying assets without recreating their behavior in Resonite.

## Supported components

### Merge Armature

Maps matching bones in the clothing armature onto the target avatar's rig. Unmatched bones are retained with the outfit.

### Bone Proxy

Attaches a clothing object to a selected humanoid bone or avatar path on the target. Its configured attachment and scale behavior is applied during installation.

## Not supported

- Shape Changer
- Full expression-menu installation
- Many other build-time components

## Planned

Additional Modular Avatar behavior support is planned for future updates.
