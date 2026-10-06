# Companion package waiting prototype

Updated 2026-09-30. This work changes the live station prototype only. Mod
integration and actual package detection are not part of this pass.

## Current interaction

The existing companion-package popup keeps its original size and position.
Selecting **Import Companion Package** switches it to **Waiting for package**
instead of dismissing it. **Back** returns to the original choices and resets
the prototype decision to Pending.

The waiting page now displays only:

- Waiting for package
- The supplied `White_128_BoxIn.png` icon, centered at 64 by 64 UIX units
- Back

The context heading and all explanatory subtexts were removed at the user's
request. The icon has its own StaticTexture2D provider inside the waiting
page, with aspect ratio preserved, so deleting the loose reference image
does not break the station. Native Slot.OrderOffset values keep the title,
icon area, and Back action in that order.

The popup uses the approved station font, rounded sprites, colors, and opaque
depth-writing surface. Its canvas remains 320 by 276 UIX units. The waiting
screen is left open for review. The previously removed footer promising later
material replacement remains removed.

## Native wiring and persistence

A native Boolean ValueField selects either the existing choices or the new
waiting surface. BooleanValueDrivers and ValueCopies keep those pages mutually
exclusive. Import and Back use native ButtonValueSet callbacks; no external
watcher is needed for screen switching. The existing import decision callback
is retained, and its former dismissal callback now keeps the popup open.

All sprite, font, and material references remain within the station. Save the
updated station item in Resonite before closing; local scripts and manifests
do not save an in-world item.

Local helpers are `scratch/station-prototype/BuildCompanionWaiting.py`,
`scratch/station-prototype/WireCompanionWaiting.py`,
`scratch/station-prototype/SimplifyCompanionWaiting.py`, and
`scratch/station-prototype/FinishCompanionIcon.py`. Evidence, manifest, and
session-specific mappings are under
`scratch/station-prototype/companion-waiting/`. Resolve the mappings again
before replaying against a different object or session.

## Verification and limits

`verification.json` records passing checks for Import/Back target bindings,
exclusive page visibility, preserved canvas size, and no premature dismissal.
The latest scoped strict UIX audit observes all 7 remaining slots without
truncation and reports no structural issues. The icon simplification checks
also confirm the title-only copy, local texture provider, square icon, native
sibling order, preserved popup dimensions, and unchanged Back target.
Actual layout-size evidence is unavailable, so the audit is structural.
The user subsequently approved the simplified waiting-page design and both
transitions after the return hierarchy correction. The later shared button
palette pass still needs visual review; see `STATION_BUTTON_STYLE.md`.

## Page transition

Import and Back now reveal the destination page with a gentle native horizontal
slide. The waiting page enters from 12 UIX units to the right; the original
choices enter from 12 units to the left. Both settle at their original offsets.
SmoothValue<float2> uses Speed 9, matching the station's existing transform
transition. Each page's minimum and maximum offsets receive the same vector,
preserving its dimensions and settled centering.

The Companion Page Transition controller sits under the station root so its
native interpolation continues while either page is inactive. Page visibility
remains exclusive; this is an entrance animation, not a simultaneous crossfade.
It uses the existing waiting-state field and original button callbacks.
No external watcher or running Python process is required after deployment.

The original choices surface now sits inside a Companion Choices Animation
Container with the same 306 by 262 layout metrics as the waiting container.
The outer container participates in automatic layout; the inner surface's
offsets remain free for the native animation. This matches the approved forward
page's hierarchy and addresses the abrupt Back transition reported by the user.
The original surface, rounded background, content, and button identities are
preserved. The user visually approved both directions after the return fix.

`BuildCompanionTransition.py` prepares the native manifest;
`VerifyCompanionTransition.py` samples both transition directions. The evidence
under `companion-waiting/transition-verification.json` confirms intermediate
positions, convergence, preserved canvas size, and exclusive page visibility.
`BuildCompanionReturnHost.py` and `FinishCompanionReturn.py` prepare the layout
container and reparent the existing choices surface; corresponding evidence is
under `return-host-verification.json`. The waiting preview is left open. Apply
the return-host and transition manifests/states as well as the waiting-page
manifest when reconstructing the live prototype, then finish the surface
reparenting. The original button callbacks remain unchanged.

Dropping a package does not currently advance this prototype screen. The
agreed future flow is to treat a new package as a candidate tied to this
specific pending import, check matching missing assets, show Connecting
materials and then Materials ready, and return to the station. An unrelated
package should leave the prompt open with a clear explanation. Those detection
and result transitions are not implemented by this UI pass.
