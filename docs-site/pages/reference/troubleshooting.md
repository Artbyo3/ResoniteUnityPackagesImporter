# Troubleshooting

## The import prompt does not appear

Fully restart Resonite after installation. Check the mod log for `UnityPackageImporter` startup errors and confirm that ResoniteModLoader is enabled.



If the mod did not load, the station's import report panel may not be available; review ordinary logs before sharing them because they can contain private paths or names.

## Textures are missing or materials look different

- **Missing source assets:** supply the matching companion package if the station requests one.
- **Different appearance:** check the source shader and its documented mappings. Supported material properties are converted to native Resonite materials; Unity shaders do not run directly.
- **Not sure which:** review the item's material warnings and import receipt before changing settings.



## The station will not create the avatar

Read the selected item's status and details on the tablet:

- **Ready:** structural checks passed and all assets were resolved. The avatar or object can be created or installed.
- **Partial compatibility:** warnings may concern approximate shader effects, missing assets, or skipped or deferred prefab content. This does not guarantee creation; actions still depend on structure and readiness.
- **Blocked:** a structural problem prevents automatic creation. Missing humanoid rigs, multiple unmerged root rigs, or unresolved parent references are possible causes.

A visual limitation is not necessarily a structural failure. Use the actual item details to choose the next step.

## An outfit target is rejected

For **"This avatar is not owned by the local user"**, select an avatar you spawned or own in the current session. Targets protected for or allocated by someone else, or worn by another user, are rejected.



## Companion packages conflict

Use companion packages from the same avatar release. Conflicting copies of the same asset are rejected rather than silently overwritten; remove the conflicting duplicates instead of repeatedly changing the import order.

### Why changing order does not help

Multiple packages can define the same asset GUID with different payloads or incompatible import settings. The importer rejects that ambiguity rather than selecting a copy based on input order.


## Some items imported, but another failed

A corrupt prefab or malformed scene can fail separately from valid items in the same package. The importer cleans up the broken item and presents the valid ones with a partial-status warning. Inspect each result rather than assuming the entire package failed.
