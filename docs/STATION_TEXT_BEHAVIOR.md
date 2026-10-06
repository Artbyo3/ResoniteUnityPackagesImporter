# Station text containment â€” live prototype, 2026-09-29

The user approved implementing text containment in the live prototype first.
Integration into the importer follows review of the saved in-world object.

## Implemented behavior

- Dynamic prefab, selected-avatar, shader, and avatar-list names use a single
  line and append `...` when the text exceeds the available width. Font size,
  card dimensions, icons, and control widths remain fixed.
- The fitter measures rendered glyph width using the existing font, rather
  than guessing a character count. It waits for UIX layout and retries when
  font/fallback measurements change.
- The original full Content fields and existing callbacks remain intact.
  A separate presentation child renders the shortened name. Selection must
  continue to use the existing object identity, never the shortened label.
- Editable avatar names now follow the native inspector's UIX text-fitting
  approach: horizontal and vertical auto-size, literal input, and one always
  visible Text bound to the original TextEditor/TextField and clear button.
  The approved base font size remains 13.5 with AutoSizeMax 13.5; long input
  fits inside the existing field rather than expanding the card. As with the
  native inspector, fitting can reduce type size and wrap text. This field
  does not use the read-only labels' ellipsis policy.
- The earlier custom idle label, caret-offset graph, measurement children,
  and name viewport mask were removed after the user requested comparison
  with the native inspector. The original editor, Content field identity,
  entered name, rounded background, and clear-button target were retained.
- The 24 avatar-list labels retain their ellipsis fitters but no longer add
  a second Image/Mask inside each row. Clipping uses the original approved
  scrolling viewport mask. The chooser surface now has an opaque tint and
  its own alpha-clipped, depth-writing UI_UnlitMaterial using the approved
  pre-import prompt's recipe. Its scroll controls and selection setters
  were not rebuilt. The visual rendering correction still needs review.
- Pasted CRLF, CR, LF, and tabs become spaces. Literal formatting tags do not
  change font size or inject styling. Full normalized input is retained;
  there is no character-count limit based on the visible field width.
- Truncation guards surrogate pairs, common combining marks, variation
  selectors, and emoji joins/modifiers. This is a conservative native graph
  implementation, not full Unicode UAX #29 segmentation.

The functionality is native UIX/ProtoFlux in the object; it needs no Python
watcher or background process after deployment.

## Live verification

The scoped tests temporarily replace names, then restore all source values
and avatar/clothing/advanced/popover states in `finally`.

The temporary `Text Metrics Probe` test object was removed after verification.
Two final InteractiveCamera captures returned completely black images despite
the connected session and active station. Therefore the final visual review
remains pending; successful capture/export status alone is not visual proof.
The user's screenshot revealed a rendering defect despite the earlier
numerical checks. The final native-field revision passed a real focus and
defocus cycle, eight input cases, opaque visibility checks, and clear-button
reference checks. All source values and view states were restored. The 24
added row masks were confirmed removed and list ellipsis retained. A later
chooser camera capture again returned black, so user confirmation is still
needed. Do not describe property assertions as a visual pass.

The user subsequently confirmed the native name-field and chooser visual pass.
One first-row avatar choice is now intentionally left with a long test name,
starting `Artbyo3 - Unity Avatar Character`, for a separate visual overflow
review. Its full selection-name setter matches the full label source; its
selection index is unchanged. The displayed value was verified ellipsized.
Original row text, selection-name value, and view states are recorded in
`native-finishing/long-name-example-before.json`; the example is intentionally
left visible, not automatically restored or managed by a watcher.

The user then confirmed the 97-character avatar-choice example passed visually.

Confirmed cases include long wide-letter clothing and shader names, CJK
avatar names, joined emoji in list rows, and pasted editable names with line
breaks and literal rich-text tags. Earlier custom viewport checks are
historical evidence only; the latest editable-field behavior is native fitting.

Evidence and one-shot helpers are local under
`scratch/station-prototype/text-protection/`:

- `live-test-results.json`: four representative labels plus editable-name
  behavior, five groups passed.
- `all-label-test-results.json`: all 31 deployed labels and six additional
  input cases passed, including empty/short values, literal markup, combining
  accents, joined emoji, and narrow letters. Rendered widths fit their slots.
- `Prepare.py`, `Apply.py`: read-only manifest preparation/deployment.
- `NativeFinishing.py`: the current session-scoped native-field and chooser
  repair. It records its pre-change evidence under `native-finishing/`.
- `VerifyNativeFinishing.py`: restoring checks for the current behavior;
  `native-finishing/verification.json` records the passing results.
- `CaptureNativeFinishing.py`: one scoped capture, restoring view states.
- `ApplyName.py` is retired and refuses deployment after the native migration.
- `VerifyLive.py`, `VerifyAll.py`: temporary, restoring stress checks.
- `focus/results.json` and `name/visibility-fix-results.json`: historical
  custom viewport tests, superseded by the native-field revision.
- `VerifyFocus.py`, `VerifyNameOnly.py`: historical helpers; use
  `VerifyNativeFinishing.py` for the current field. Its temporary focus helper
  is removed immediately after checking focus.
- `Main/*.pg`: the native fitting, rendering, viewport, and normalization
  graphs. `NativeDeploy/` is a local SDK adapter for a field-element binding.

The pre-change snapshot is
`scratch/station-prototype/text-overflow-before.json`. Manifests record the
original styles, source IDs, and field identities. Live IDs are session
specific; do not replay deployment against another object/session without
resolving its mapping first.

## Integration and remaining decisions

Use one shared text policy when integrating the approved station. A C#
implementation can use proper grapheme segmentation and avoid reproducing
the prototype's per-label native measurement graphs.

The prototype retains complete names but does not add a new full-name
popover/tooltip for shortened read-only labels. Distinguishing two avatars
with identical shortened prefixes still needs a deliberate disclosure design.
An independent generous input-length limit also remains undecided; a visual
width limit must never silently truncate the stored name.

Save the updated station object in Resonite before closing the session.
Local manifests and documentation do not save an in-world item.

## Material control finishing pass

The user reported that the material preset Change button split the final
letter onto a new line, and the material picker showed objects behind it.
The pass applies to the main station only; the separately approved pre-import
prompt is finished and was not changed.

Both avatar and clothing material Change controls now reserve 64 UIX units
instead of 54. They retain their 11.5-point base size and enable native
horizontal/vertical fitting bounded between 10 and 11.5. Existing button
callbacks and rounded containers remain intact.

The Material Preset Popover Selection Surface now has alpha 1 and a
station-local UI_UnlitMaterial matching the approved avatar chooser recipe:
alpha clipping, depth writing, and the same depth comparison/offset settings.
Its provider sits under the station root, outside the UIX canvas. Color,
corner sprite, option layout, and selection flow are preserved.

Scoped button UIX audits pass with no issues or truncation. Shader-name
fitters still fit both modes after the button-width change; full shader names,
picker-opening callbacks, and the user's prior view states were preserved.
The complete picker audit was structurally valid but truncated by its existing
text-fitting graphs, so it is not a complete audit or visual proof. The user
subsequently confirmed the material controls' visual test passed. No new
camera capture was attempted after repeated black captures.

Local repair and evidence: `scratch/station-prototype/material-polish/`,
with `InspectMaterialPolish.py`, `PrepareMaterialPolish.py`,
`ApplyMaterialPolish.py`, and `VerifyMaterialPolish.py` in the parent folder.
Session IDs must be resolved again before using these helpers in another world.

The companion-package prompt had the same slightly transparent surface
(alpha 0.98) with no custom material. Its surface now uses alpha 1 and the
same station-local depth material approved visually for the material picker.
Its canvas size, sprite, layout, and callbacks are preserved. On request, its
existing BooleanValueDriver was enabled and the canvas/content confirmed
active, leaving it open for the user's review. Evidence and before-values
are under `scratch/station-prototype/companion-polish/`; helpers are
`PolishCompanionSurface.py` and `ShowCompanionPrompt.py` in the parent folder.

The companion footer promising that materials could be replaced later was
still present after the background repair. It was removed following the
user's feedback because it suggests an unconfirmed post-import workflow.
The footer contained only its Text, RectTransform, and LayoutElement;
its pre-removal snapshot is `companion-polish/footer-before.json`.
No replacement promise or extra UI option was added.
