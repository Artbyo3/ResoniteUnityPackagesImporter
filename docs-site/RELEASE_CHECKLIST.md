# Documentation replacement checklist

## Reviewed baseline

- Release: v0.0.6, published October 23, 2026; reviewed October 24, 2026.
- Source: `90a462c8b2d75cad8131579231d90276b0d4d0ad`.
- Release metadata and downloads were read from the repository release API.
- The testing-build ZIP contains the license notices, `rml_mods/UnityPackageImporter.dll`
  and `rml_libs/YamlDotNet.dll`. Its archive digest matches the release metadata.
- Extracted importer SHA256: `44b63ec41b318f5a00c05ac033e9316b50d38d536f0ba32ce97f7bb924f5ca1f`.
- Extracted YamlDotNet SHA256: `86f873f682c619a0c24e263dcdd15fe027104666641c92f503516671d61edda6`.
- All 13 inventory evidence files exist in the released source. No support is
  inferred from newer uncommitted mod changes.
- The release source was extracted under ignored `scratch/` without changing
  the working checkout. Its regression suite passes 813/813; its build has
  zero errors and 76 warnings. This is not an installed-mod test.
- MA/VRCFury rows remain partial for structural/default-value translations;
  runtime menus, contacts and full Unity behavior are not advertised as supported.
- Public illustrative activity and the synthetic scale-test query are removed.
  Disconnected activity is explicitly unknown, not an empty issue backlog.
- The root video landing is unchanged. Only the documentation overview's sample
  activity block changed to the honest unavailable state.

## Required maintainer live test

Use the downloaded release ZIP, not the newer local build. Do not overwrite an
active installation or restart Resonite without arranging the test first.

- [ ] Follow only the installation guide from a clean or backed-up setup.
- [ ] Confirm the mod loads after a full restart with the matching dependency.
- [ ] Import a representative complete humanoid package; compare review states,
      material choices, warnings and creation labels with the guide.
- [ ] Try a supported clothing attachment and verify ownership restrictions.
- [ ] Try a package with missing dependencies and the companion-package path.
- [ ] Confirm an unsupported runtime feature is reported without implying support.
- [ ] Exercise a failed import and review the privacy-filtered report flow.
- [ ] Record exact package/build, result and any doc corrections. Do not mark
      compatibility fully supported based on a static screenshot or issue closure.

No fresh install, Resonite restart or live import was performed in this pass.
Only after the maintainer's live test and approval should the new docs replace
the public site. Publication still requires the explicit ship request.

## Language scope

The new reader and component inventories are English. Japanese pages remain
on their existing translated content; a successful Japanese build is not a
claim that the new content has been translated or release-reviewed in Japanese.
