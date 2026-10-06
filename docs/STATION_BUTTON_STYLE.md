# Live prototype button palette

Updated 2026-09-30. This finishing pass applies to the current station prototype
and the approved new pre-import prompt. It does not change mod code or the older
reference prompt/station.

## Shared roles

- Primary actions use the earlier pre-import prompt's blue palette: Import Unity
  Package, Create Avatar, Install on Selected Avatar, and Import Companion
  Package, including their advanced-view equivalents.
- Secondary controls share the approved gray checkbox-container palette:
  Advanced Options, Change, Back, raw-file/placeholder choices, Cancel,
  selection rows, fields, toggles, and preview navigation arrows.
- The station Close button retains its existing coral palette.

Every role defines the same normal, hover, pressed, and disabled colors wherever
it appears. All UIX.Button BaseColor values are white, avoiding unintended
multiplication of the state palette. Primary labels use the earlier pre-import
prompt's dark foreground to stay readable on blue. Cyan icons and checked-state
marks keep their existing styling.

The Companion Package primary action's ConvertMaterial icon uses the same dark
foreground as its label. The earlier pale tint lost contrast against blue.
Only the icon tint changed; its sprite, dimensions, spacing, and the button's
interaction palette are preserved. The live tint was read back and verified,
and the canonical companion prompt manifest stores this color. The helper is
`scratch/station-prototype/MatchCompanionPrimaryIcon.py`; the user still reviews
the visible result in-world.

The exact colors are recorded in the local
`scratch/station-prototype/button-theme/palette.json`. Use these shared values
when integrating the approved prototype; do not recreate independent palettes
for different screens.

## Verification and persistence

The pass inspected 59 UIX buttons: six primary, 52 secondary, and one Close.
The initial pass changed 52 buttons' palette or base tint. The user preferred
the earlier blue to green, so a follow-up changed all six primary actions and
their six labels to the blue palette with dark text. All 59 now match their role's
colors, including interaction states and resulting image tints. Existing button
event bindings, movement/scroll settings, non-color members, sibling components,
primary text contents, typography, and view states were checked and preserved.
Live hover, pressed status, and press position were allowed to change normally.

The canonical new-prompt and waiting-page manifests also store the shared colors,
and their builders read the shared palette. Other older local partial manifests
may still contain historical colors; run a newly resolved theme pass after
replaying those. Save the updated station and new prompt items in Resonite before
closing. Local files do not save the in-world items.

Helpers: `InspectButtonTheme.py`, `ApplyButtonTheme.py`, `RestorePrimaryBlue.py`, and
`PersistButtonTheme.py` under `scratch/station-prototype/`. Before-values,
target mappings, saved manifest copies, and verification are under
`scratch/station-prototype/button-theme/`. Target IDs are session-specific;
resolve them again before applying in another world or to a different copy.

The user approved the companion return transition immediately before this pass.
The initial shared styling mostly passed the user's visual review, and the
user subsequently approved the restored blue primary actions. The checks
verify fields and bindings, not a camera-based visual pass.

The avatar Candidate Card's small AVATAR PREFAB context label now copies the
exact blue Color value from the approved OUTFIT PREFAB context label. Its
content, size, alignment, font, and remaining fields are preserved. The helper
is `MatchPrefabHeaderAccent.py`; before-values and verification are under
`prefab-meta-before.json` and `prefab-meta-verification.json` in the theme
evidence folder. This final header accent still needs visual confirmation.

The approved Protect Avatar subtitle is now **Locks avatar against saving & use
by others**. It replaces the inaccurate inspection claim while keeping the
previous sentence structure. Only the live subtitle's Content changed; its
remaining text fields were verified unchanged. Evidence is under
`scratch/station-prototype/protection-wording/`. Use this wording for future
localization; historical before/after audit snapshots retain the earlier text.
