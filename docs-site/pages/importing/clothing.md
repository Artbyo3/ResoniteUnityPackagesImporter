# Importing clothing

The importer supports modular outfits and attachments configured with Modular Avatar or VRCFury components.

## Outfit installation workflow

1. **Select an outfit item**: When a package contains clothing or props with `ModularAvatarMergeArmature`, `ModularAvatarBoneProxy`, or supported `VRCFuryArmatureLink` components, the station marks the item as an outfit.
2. **Choose a target avatar**: The station lists available avatars in the current world that are owned by the local user.
3. **Install**: Selecting **Install outfit** duplicates the outfit into the target avatar's hierarchy, binds bones according to the authored plan, and configures skinned bounds.
4. **Context menu toggle**: If selected, the importer creates an outfit toggle in the avatar's context menu.

## Ownership and isolation

- **Target avatar protection**: Outfits can only be installed on avatars allocated by or protected for the local user. The station rejects targets owned by other users or currently worn by another player.
- **Transactional safety**: Installation operates on a duplicate copy. If an error occurs or the operation is cancelled, the transaction rolls back cleanly, destroying newly created slots and leaving the target avatar untouched.
- **Staging notice**: Uncommitted outfit templates remain in the station's inactive staging hierarchy until consumed or the station closes.
