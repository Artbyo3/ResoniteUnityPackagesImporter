# Fresh pre-import prompt prototype

The user requested a new in-world pre-import prompt built from scratch using
the approved main station as its style reference. The existing pre-import
prompt was not adopted, cloned, modified, or parented under the new object.
This is prototype work; importer integration remains deferred.

The new item is named **[UnityPackage Pre-Import Prompt - New Prototype]**.
It is placed beside the main station, with matching scale, tilt, and top
alignment. It can be moved as a separate Grabbable item.

Its layout uses the main station's rounded sprite providers, fixed corner
sizing, fonts, dark blue surfaces, cyan accents,
and front/back material recipe. It has a package summary and three actions:
Import Unity Package, Import as Raw File, and Cancel. There are no additional
hidden template copies or duplicate close controls.

The approved wording pass is applied to this new item. The large product
heading and detected-package row are removed, and metadata shows only the
file size. The filename and size appear first, followed by a prominent blue
Import Unity Package button, a quieter Import as Raw File button, and Cancel
with the lowest emphasis. The canvas is 512 by 238, retaining the established
button heights, padding, corner shapes, and the current item's tilt, scale,
and upper-edge placement. This pass does not change the older prompt or the
main station.

The package name keeps its full source value, with a width-measured ellipsis
presentation at a fixed 20-point font size. A short name and two long filename
cases passed restoring checks. The UIX audit completed without structural
issues; it does not verify rendered appearance or expose all computed sizes.
The user confirmed that nothing looks off and no further design changes are
needed on this prompt. Its visual design is approved. Remaining live polish
returns to the main station prototype; importer integration is still deferred.

The simplified layout, wording, total row heights, native decision/dismiss
bindings, preserved tilt and scale, rounded/depth material recipe, and three
filename cases passed scoped checks. The strict UIX audit is valid, with
partial structural verification. The user subsequently approved the rendered
appearance in-world. The new prompt is left open.

Each button uses native ButtonValueSet components to record its distinct
prototype decision. Import Unity Package and Import as Raw File keep the
preview visible; Cancel dismisses only this prompt. The initial import test
hid the panel with nothing replacing it, so those two premature dismissals
were removed and the prompt reopened. These choices are not connected to
the actual importer yet. The original main station and older
prompt were only read for reference or placement; all mutations were scoped
to the newly created item.

The new root contains its own font, fallback-font, texture, sprite, material,
and fitting-graph providers. Its item audit passes with exactly the native
UI_UnlitMaterial and UI_TextUnlitMaterial shader roles allowed. Those private
shader references are engine dependencies: the native material types obtain
them from OfficialAssets/Shaders. No references to the old prompt or main
station's asset providers are needed by the new item.

Local source and evidence are under
`scratch/station-prototype/new-pre-import-prompt/`:

- `Build.py`: prepares the fresh hierarchy from the recorded main styles.
- `prompt.json`, `world-state.json`: native manifest and current session map.
- `flux.json`, `flux-report.json`: package-name fitting bindings/deployment.
- `Verify.py`, `verification.json`: scoped restoring checks.
- `uix-audit.json`, `item-audit-native-shaders.json`: structural/dependency checks.
- `placement.json`, `main-style-providers.json`: placement and style references.
- `Reopen.py`: reopens the new prompt after a prototype choice.
- `KeepPreviewOpen.py`, `preview-action-fix.json`: restores the preview and
  verifies that import choices keep it open while Cancel still dismisses.
- `PrepareSimplify.py`, `simplify-before.json`: preserves current placement
  and records the pre-edit values for the approved simplification.
- `simplify-apply-report.json`, `simplify-uix-audit.json`: final update evidence.

Native apply checkpoints briefly failed to replace their file inside the
synced repository. The update completed using a checkpoint in the writable
local visualization directory, then copied the completed map back to
`world-state.json` and the normal `.resoloop/state/` checkpoint. If a later
apply encounters that file-write error, resume from a copy outside the
synced folder; do not rebuild the live prompt or create a duplicate.

Live identifiers are session-specific. Resolve the new saved object before
using these manifests in another session. Local files preserve the recipe,
but the user must save the new item in Resonite before closing the game.

The final consistency pass now gives this prompt the same blue primary-action
and gray secondary-button palettes as the station, with dark primary text.
Normal, hover, pressed, and disabled colors match across the two items.
The current manifest and builder record those shared values. Layout and
callbacks remain as approved above; the user approved the restored blue colors.
See `STATION_BUTTON_STYLE.md` for the exact palette source and verification.
