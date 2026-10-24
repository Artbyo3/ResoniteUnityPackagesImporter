# Documentation site source

This folder contains the public documentation site source for the Unity Package Importer.
All documentation pages, compatibility guides, importing workflows, and legal notices
are maintained here. Keep private development notes outside this source directory; the
site build reads only `pages/`.

## Preview locally

From this folder, install the pinned dependency and run:

```powershell
python -m pip install -r requirements.txt
python serve.py
```

This builds both the English and Japanese sites and serves them at `http://localhost:8000/`.

To run a production build only:

```powershell
python build.py
```

The generated site is written to `site/` (with English at `/` and Japanese at `/ja/`) and is ignored by Git.

## GitHub-connected project activity

The documentation overview and `/project-status/` share a generated snapshot of
maintainer-approved GitHub Issues. Visitors fetch one local JSON file, not the
GitHub API. No token or raw report body is included in the site.

Users report failures with two required answers: expected/actual behavior and
reproduction steps. The importer report and versions are optional; existing
in-game drafts remain usable without starting over. Feature requests require
only the desired task and current obstacle. None of these forms auto-publishes.

Maintain public updates by reviewing the issue title, then adding `project-status`
and exactly one of `status:in-progress`, `status:known-issue`, `status:testing`, or
`status:planned`. A title and status are sufficient. Short plain-text
`### Why it matters` and `### Next step` sections are optional; raw report bodies
are never used as a fallback. A closed issue leaves the feed without changing
component compatibility or promising a released fix.

The docs workflow rebuilds on approved issue edits, status changes, closure,
reopening and removal of the publication label. A six-hour schedule is the
fallback; a manual run can also refresh it. This is an automatically refreshed
build-time snapshot, not post-build live API polling. It builds once, syncs
`site/data/project-status.json`, then deploys that exact artifact. CI uses the
job's read-only `GITHUB_TOKEN` through `GH_TOKEN`; PR builds do not query issues.

An incomplete public status or invalid title skips only that issue with a safe
warning. Unsafe optional fields are omitted rather than blocking other updates.
API, pagination and identity/integrity errors still stop publication, preserving
the previous deployed snapshot. Its timestamp warns visitors after 24 hours.
See `PROJECT_STATUS.md` for report triage, publication and failure handling.

For a connected local preview, build first and then run:

```powershell
python docs-site/sync_project_status.py
```

This writes generated output only. The committed
`pages/data/project-status.json` remains the honest disconnected fallback for
offline builds; no machine-generated issue timestamps need commits. Available
but empty data says that no public updates have been selected, not that the
project has no bugs. See `PROJECT_STATUS.md` for maintenance and troubleshooting.

## Integrated documentation reader

All English functional documentation and legal pages share one reader, task-based
navigation, and a full-text answer finder opened through the existing header
search or Ctrl+K. The original theme and site navigation are retained; there is
no second documentation shell or duplicate search bar. Essential actions and limits stay visible. Functional topics lead with real screenshots,
video or a focused diagram, at most three steps, and concise outcomes. Full technical
reference remains available in place; component rows disclose rationale and evidence. Topic switches keep the same reading area, update the
real URL, and support browser Back/Forward. History navigation restores the previous reading position. Search shows matching
passages and highlights them in the selected answer.

`workspace.toml` defines the groups, topic labels, existing routes, source files
or named source sections, and a small set of related tasks. The Markdown pages
remain the source of truth. Do not duplicate full answer text in this manifest.
`docs_workspace.py` stages the shared layout before the normal site build, then
exports the rendered answer bodies to `site/assets/docs-workspace.json` (one
shared payload, fetched once per document). Link and image URLs remain valid
under both localhost and the GitHub Pages project prefix.

The initial answer is server-rendered. With JavaScript disabled or the shared
payload unavailable, documentation and ordinary links remain usable. Source
pages with several topics render all of those answers for this fallback. The
English homepage, documentation landing page, avatar comparison, and project
status presentation are unchanged. Japanese content retains its current layout;
this proposal does not supply a translation or change runtime mod behavior.

Run workspace compiler tests with:

```powershell
python -m unittest discover -s docs-site/tests
```

## GitHub Pages

The workflow at `.github/workflows/docs-site.yml` checks builds on pull
requests. Documentation changes merged into `master` are built and published
to GitHub Pages automatically. A manual workflow run can also publish when
its `publish` input is `true`; the repository's Pages source must be configured
as **GitHub Actions**.

The unchanged landing pages follow GitHub's latest-release ZIP. The English
installation guide deliberately links to the documented release's versioned
archive, so its instructions and compatibility claims refer to the same build.
Advance that link together with `release-baseline.json` when reviewing a release.

## Component support and delivery map

`capabilities.json` is the single authoring source for the English MA/VRCFury
inventory, generated at build time by `support_map.py`. Each record keeps a
stable ID, pinned upstream type, current outcome, limits, released-source code/test
references, and a separate delivery state. `workspace.toml` embeds each framework
inventory only in its dedicated guide. The compatibility overview uses the
separate `vrchat-capabilities.json` inventory instead of repeating MA/VRCFury. Counts are
inventory counts, not completion percentages. Legacy migration models and
nonportable platform details are not missing feature commitments.

The source snapshot includes MA Runtime components and VRCFury feature models
plus authorable runtime components. It is not every historical or future release.
Review upstream changes before advancing the pinned revision; update rows and
explicit exclusions together. Internal base/helper types are recorded as
exclusions. No upstream API is called at runtime or during normal builds.

`limited` means only the stated behavior is implemented, not complete component
support or verified visual parity. Initial appearance must never be described
as live control. New support requires local implementation and regression
evidence. Status and delivery are deliberately independent: `missing` is not
`planned`. An actual tracking issue is required to mark work `planned` or
`in-progress`. Compatibility issue metadata remains manual. The GitHub activity feed is a
separate read-only presentation and never promotes component support.

All outcomes render without JavaScript. Every matching component is visible
without pagination or reveal buttons; search and filters narrow the list.
Search includes exact serialized type names and finds legacy models too. Rows disclose reasons and source evidence in place. Cross-section navigation belongs to the sidebar; the shared reader retains
real URLs/history without adding cross-topic links to the body.

### Visual-first guides

`visual-guides.json` defines the short visual front for all 22 functional topics.
`visual_guides.py` validates existing media, renders informational diagrams and
keeps the complete authoritative Markdown in a native technical-reference disclosure.
Legal topics retain their complete prose. Do not put essential safety limits or
required actions only inside the reference. Use real screenshots for actual UI;
diagrams describe boundaries and do not imply unsupported features exist.

Media selectors change only the illustration, never hide required steps or facts.
There is no auto-rotation or autoplay. Without JavaScript all media remain available.
Search results and deep links reveal their containing reference automatically.
The support inventory shows all matching components without a reveal-more button.

### Three-view art-direction prototype

Only `avatars`, `compatibility`, and `missing-materials` use the optional
`composition` schema in `visual-guides.json`. `docs_compositions.py` renders
these as distinct scene, comparison, and diagnostic compositions. They share
`docs-compositions.css` and the existing reader/navigation, not a second shell.
Essential limits, diagnostic causes and fixes remain visible. Full source
reference remains available in place. Other topics and both landings are unchanged.

The comparison uses a keyboard-operable range with no decorative handle. Its
no-JavaScript fallback shows both original images in label order. All matching
component records remain available without pagination or a reveal gate.

`pages/images/avatar-demo-study.webp` is derived from the existing
`pages/assets/landing-background.mp4`, at 10 seconds: crop 1120x1080 at (480,0),
resize to 960x926. Only the bright studio background was isolated with an alpha
mask for composition; the avatar is not generated and its materials/geometry
were not retouched. The caption identifies the isolation. The original footage
remains available in the guide, without autoplay. This is a demo example, not
proof of arbitrary package compatibility or a new live import verification.

### Pending visual captures

Optional `capture.title` and `capture.caption` fields in `visual-guides.json`
keep an editorial record of footage still needed. They do not render empty media
areas, pending captions or placeholder icons. Capture-only introductions are
omitted, so MA/VRCFury inventories lead directly into their technical reference.
Existing screenshots, diagrams, technical limits and component inventories stay
available. Add real footage through `media` and remove its `capture` entry when
ready. Both landings remain unchanged.

### Sidebar-owned section navigation

The sidebar follows the documentation reading order, including update/settings
and each troubleshooting topic. Guide action rows retain downloads and external
resources, not cross-section jumps. Related-topic rows, in-content topic tabs
and the inline cross-topic reader are removed. `docs_navigation.py` removes
local documentation link wrappers from reference prose while retaining the
information, media, same-section controls and external sources. Source Markdown
and both landing pages are preserved. This policy is shared by static output
and the exported reader payload, not a JavaScript-only visual hiding rule.

### Component-first framework pages

MA and VRCFury start with their component inventory, before example media or
translation explanations. Card surfaces and explicit labels distinguish supported,
partial, unsupported and not-applicable statuses. Totals include zero fully
supported components; implemented partial translations are never labelled fully
supported. The actual capability evidence and search/filter behaviour are unchanged.

### Component collection progress

The compact collection strip has one slot per applicable, current component.
Only full support fills a slot; a half-filled slot represents partial support.
Numbered cards keep their status labels, boundaries and implementation evidence.
Not-applicable and legacy entries live in separate grey groups below the
collection and never increase its denominator. Search/status filters hide empty
groups but do not change progress totals; the framework filter scopes those totals.
This is importer coverage, not a personal achievement tracker or a delivery promise.

### Component detail motion

Only one component card can be open per inventory. Opening another closes the
previous card in the same animation. Opening a component expands the card itself
across the complete inventory row.
Neighboring cards move into the rows below; no floating panel covers them.
The expanded card uses two columns for the explanation and implementation
references, stacking those columns on narrow screens. JavaScript animates card
size and neighboring positions in both directions, including rapid reversals.
Reduced-motion preferences remove the animation. Native `details` retain the
same full-width layout and complete evidence without JavaScript.

### VRChat SDK inventory

The compatibility overview lists SDK avatar components from the official Allowed
Avatar Components documentation, pinned to a specific `vrchat-community/creator-docs`
revision in `vrchat-capabilities.json`. The six concrete VRChat constraint types
have individual cards. This is not a Unity component inventory or a world/Udon
compatibility claim. Pipeline Manager is excluded from progress as publishing
metadata; deprecated IK Follower appears separately under legacy components.
Avatar Descriptor is partial: local avatar identification and viewpoint handling
are not complete SDK behavior. No runtime component is labelled supported merely
because its object survives import. Implementation evidence is local, and normal
builds and the browser make no upstream requests for these records. The shared
full-width, one-open-card interaction, search and status filters are unchanged.

### First-read hierarchy

The native sidebar has six entry points, led by ?What can I import??. Setup,
import workflows, compatibility details, troubleshooting and project information
remain grouped rather than exposing every topic at once. Reader navigation
opens only the selected topic's ancestor groups and clears stale native active
states. Sidebar links retain the native theme's rounded shape, padding and type
across guides and project status; there is no page-specific button geometry.
There is no second navigation ribbon or duplicated in-page sidebar TOC.

Shared guide spacing separates the main answer, evidence and optional reference.
The overview pairs the existing comparison with three explicit transfer outcomes;
its limitation is plain text rather than another verdict card. Single-image
guides do not show a one-option view switcher. Generic package/shader diagrams
are omitted where they repeat the introduction without explaining a procedure.
Actual media, support records and reference content remain available. Both
landing page sources and their video/comparison interactions are unchanged.

### Released-source baseline

`release-baseline.json` pins the documented mod version and source commit.
Component evidence links resolve to that commit, not the working tree or a
moving branch. The installation download uses the same release archive.
`python docs-site/release_baseline.py` checks that every inventory evidence file
exists in that commit; the commit must be available in the local Git checkout.
It makes no network requests and does not establish semantic or live correctness.
Updating the baseline requires reviewing the actual release, its asset layout,
component claims and evidence, then updating the download and verification
record together. See `RELEASE_CHECKLIST.md` for the remaining live-test gate.
