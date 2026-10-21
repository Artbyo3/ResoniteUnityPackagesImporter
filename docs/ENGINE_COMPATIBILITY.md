# Engine compatibility startup check

`EngineCompatibilityCheck.LastVerifiedResoniteVersion` records the last version
verified in game. Keep it unchanged until the manual check below succeeds on a
new version. The startup callback runs once through `Engine.Current.RunPostInit`,
before registering the unitypackage import extension.

The callback reads `Engine.VersionNumber`, a public static string getter confirmed
in the referenced FrooxEngine assembly's metadata and getter IL. `CurrentVersion`
adds a display prefix, while `VersionString` includes extra display information.
The version check writes exactly one info line for a match or one warning naming
both versions. A different version only warns; it does not disable importing.

`FrooxInternalBridge.ReflectedMembers` is the data-driven inventory of engine
dependencies. Each entry pairs a diagnostic name with the same reflected member
used by the feature. It includes both nested types, both constructors, the asset
extension field, the button event add accessor, and all six model import methods.
The dictionary indexer and list `Add` invoked during asset registration are BCL
collection methods, not additional private engine members.

The regression suite tests version log severity/count, both version strings,
complete missing-member reporting, inspection without execution, and exception
propagation using reflection fixtures. `--engine-api` also checks all inventory
members and the version getter in the installed assemblies without loading or
executing the engine. Metadata checks cannot prove that hooks still work.

## Manual check after an engine update

1. Build the real DLL, install it into Resonite's `rml_mods`, and fully restart
   Resonite with the intended engine version.
2. Check the mod log for one compatibility version line, and confirm it names
   the running version correctly. On an unverified version, confirm the warning
   names the last verified version and says import hooks may break after updates.
3. Confirm no missing-member errors. On an engine with a removed member, confirm
   the error names it and says: "The feature will not work on this Resonite
   version." Missing asset hooks must leave the extension unregistered.
4. Import a unitypackage through the real mod. Exercise prefab/scene import,
   skin/blend shapes, draggable setup, and native button presses. Check the log
   for reflection failures. The world prototype alone does not verify this.

Only after these checks succeed should the verified-version constant be updated.
