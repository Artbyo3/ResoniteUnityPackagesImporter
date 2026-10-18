# Troubleshooting

This reference covers common import outcomes, status diagnostics, and steps to resolve unexpected behavior.

## Understanding item status

The importer assigns a structured status to each selectable output:

- **Ready**: Structural checks passed and all assets were resolved. The avatar or object can be created or installed immediately.
- **Partial compatibility**: The structure is valid and avatar creation is fully supported, but some visual shader features could not be translated natively (for example, dynamic UV animation, unbaked lilToon layers, second emission maps, or AudioLink). The limitation details are visible on the station tablet and recorded in the in-world receipt.
- **Blocked**: The item cannot be created automatically. Common reasons include missing humanoid rigs, multiple unmerged root rigs, or unresolved parent references.

## Common situations and solutions

### "This avatar is not owned by the local user"

- **Cause**: The selected target avatar has protection attached for another user, is actively worn by another user, or was allocated by someone else in a shared session.
- **Solution**: Target an avatar that you spawned or own in the current session.

### Duplicate asset conflicts

- **Cause**: Multiple packages define the same asset GUID with different file content or incompatible import settings.
- **Solution**: The importer rejects ambiguous conflicts rather than silently picking one by input order. Ensure companion packages come from the same avatar release or remove conflicting duplicates.

### Sibling failure isolation

- **Behavior**: If a complex package contains one corrupt prefab or malformed scene alongside valid prefabs, the importer cleans up the broken item and presents the valid items on the tablet with a partial status warning.

### Sharing diagnostic reports safely

- **Failure reports and material warnings**: Recognized local file paths are replaced with `<local path>` before they enter the station's reports or material-warning receipts. Review diagnostics before sharing them; arbitrary exception text can contain other private information, and ordinary game logs may still include source paths.
