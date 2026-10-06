# Station prototype localization

English and Japanese were added to the approved live station and new
pre-import prompt on 2026-09-30. This work applies to the world prototypes;
the production C# importer is not integrated with these translations yet.

## Language and copy policy

- Japanese (`ja`, including a Japanese regional locale through its primary
  language code) uses Japanese wording. Other languages use English.
- Keep **Unity Package Importer** as the product name. Keep shader brands,
  filenames, source asset paths, prefab names, avatar names, and user input
  in their original wording.
- Translate actions, descriptions, headings, missing-material choices,
  waiting/back controls, loading phases, counters, and success/error copy.
- Keep selection values and callbacks independent from displayed wording.
  `Automatic` displays as `自動` in Japanese, while the shader selection
  still stores `Automatic`. `Xiexe Toon` retains its name in both languages.
- Keep the approved card geometry, font settings, surfaces, colors, icons,
  text containment, and animations. No language selector was added.

The UTF-8 copy catalogs are `localization/station/en.json` and `ja.json`.
They contain 64 matching namespaced messages and use only Artbyo3 as the
author identity. Rich-text tags and formatter arguments are preserved.

## Native prototype implementation

Each prototype contains its own persistent `UI Localization` child:

1. `CurrentLocaleInfo.LanguageCode` supplies the local language.
2. `ValueEqualityDriver<string>` compares it to `ja`.
3. `ValueMultiDriver<bool>` distributes the result to
   `BooleanValueDriver<string>` components containing English and Japanese
   values. Their targets are the existing source text fields, not the
   derived ellipsis labels or editable names.

There are 67 translated field bindings: 64 on the station and three on the
pre-import prompt. Existing fitted-label graphs continue to measure and
display their original source fields. The two material formatters use an
additional display value; internal shader choices and selection callbacks
are retained.

The existing native `Importing Item` label keeps its `LocaleStringDriver`.
It now uses a station-local `StaticLocaleProvider` for the core collection,
with its override driven to `en` or `ja`. The shared world provider was not
modified. This keeps that one built-in label consistent with the two-language
policy even when Resonite is using a third language.

The custom catalogs are **not registered with Resonite's core collection**.
Custom prototype wording is embedded in its native string drivers, so the
saved items do not need the repository or a Python watcher to translate.
The existing demo reads the catalogs to update both language values for
changing phases and banners; unknown error details retain their original
wording. It discovers those drivers by their target fields, without fixed
session IDs, and remains compatible with an unlocalized station.

## Resonite research and future integration

Resonite uses UTF-8 JSON locale messages, ICU MessageFormat, IETF language
tags, and an English fallback. Its general and regional locale behavior is
documented in the official [Locale repository](https://github.com/Yellow-Dog-Man/Locale).
The relevant native components are
[CurrentLocaleInfo](https://wiki.resonite.com/Component%3ACurrentLocaleInfo),
[StaticLocaleProvider](https://wiki.resonite.com/Component%3AStaticLocaleProvider),
and [LocaleStringDriver](https://wiki.resonite.com/Component%3ALocaleStringDriver).

When integration is authorized, register the importer catalogs through a
supported locale-resource path and bind custom message keys to native
locale drivers. Prove that registration with the actual loader before
replacing the prototype bindings. The
[modding localization guide](https://modding.resonite.net/guides/localization/)
describes a BepisLoader-specific route; this prototype work does not add
that dependency.

The prototype's prefab counters and diagnostic metrics are fixtures. Actual
imports must supply their own count arguments and source data. Preserve
format arguments when moving those messages to runtime localization.

## Verification and saved evidence

- Both prototypes' native language output matched `en` in the current session.
- Temporary native driver settings exercised Japanese and English on all
  67 bindings; all expected source values matched. Global Resonite language
  settings were not changed.
- Both material views displayed Japanese `自動` or the unchanged `Xiexe Toon`
  brand as appropriate; stored selections were preserved.
- The native loading label and numeric counter switched in both directions.
- Existing components were retained. Text styles, images, buttons, layout
  elements, RectTransforms, and canvases had no field differences in the
  scoped style comparison. Protected name fields remained unchanged.
- The existing lifecycle demo ran once in Japanese: all nine phase strings,
  success and failure banners were observed. Its own final verification
  confirmed clean idle, cleared ring, hidden disclosure card, and reset flags.
- Automatic language detection and the original shader selection were
  restored after the checks.

These checks establish field behavior, not a visual Japanese typography or
native-language copy review. Japanese rendering and fit still need an
in-world visual pass. Do not treat the earlier English UI approval as
Japanese visual approval. Camera captures were not retried after the
previous repeated black captures.

Local preparation, manifests, snapshots, and results are under
`scratch/station-prototype/localization/`. `verification.json` and
`demo-verification.json` record the checks. `Build.py` prepares the initial
session-specific bindings; do not rerun it over an already localized object
as if it were a fresh English prototype. `VerifyLocalization.py` and
`VerifyDemo.py` also use the current session mapping and require review
before reuse in another world.

Save **both updated prototype items in Resonite** before closing. Catalogs,
manifests, and snapshots in this repository do not save an in-world item.
