# Resolute distribution

## Status and identity

The release tools prepare files locally. Running them does not publish a release,
push a branch, open a pull request, or make the mod visible in Resolute.

- Maintainer and author folder: Artbyo3.
- Mod/Harmony ID: Artbyo3.UnityPackageImporter.
- Candidate version: 0.0.6; tag: v0.0.6.
- Category: Asset Importing. Only Windows is claimed until other platforms are tested.
- Contributor credits and existing license notices remain intact.
- The old net.dfgHiatus.UnityPackageImporter identity is recorded as conflicting.
  Do not rely on manager conflict enforcement: replace the original DLL and remove
  renamed duplicates before launching. Do not run both builds together.

## Prepare locally

Run from the repository root with Python 3.11+ and the normal build prerequisites:

    powershell -NoProfile -File scripts/Package-Release.ps1

Packaging runs the real Release build, standalone regression suite, UI bundle
audit, and distribution tests. It creates:

- dist/release-assets/0.0.6/: the exact files to upload to the GitHub release.
- dist/resolute/0.0.6/manifest/Artbyo3/author.json.
- dist/resolute/0.0.6/manifest/Artbyo3/UnityPackageImporter/info.json.
- The existing versioned ZIP and checksum file in dist/.

The release upload contains both DLLs, LICENSE, THIRD_PARTY_NOTICES.md, README.md,
SHA256SUMS.txt, the versioned ZIP, and UnityPackageImporter-testing-build.zip.
The latter keeps existing documentation download links usable.

The manifest installs the importer into /rml_mods, YamlDotNet into /rml_libs,
and uniquely named license/notice files into the installation root. YamlDotNet
is an artifact of this entry, not a separate dependency that Resolute must resolve.
Engine assemblies and loader binaries are not redistributed.

To regenerate metadata without rebuilding:

    python scripts/Prepare-ResoluteManifest.py --artifacts dist/release-assets/0.0.6 --output dist/resolute/0.0.6 --version 0.0.6

Hashes are calculated from the supplied files and checked against SHA256SUMS.txt.
No latest-release URLs or placeholder hashes are submitted. The default tag is
v followed by the version; --release-tag also accepts the bare numeric version.
Never rebuild or replace release assets after submitting their hashes. Publish
a new version for subsequent binary changes.

## Live validation and approval

1. Back up the existing mod and configuration, close Resonite, and run
   scripts/Install-Mod.ps1. It installs both DLLs and notices; it does not install RML.
2. Fully restart Resonite and check startup logs, settings, package imports,
   native station UI, and local error reporting. Only this is a live test.
3. After live approval, ask the maintainer to say "ship it" before any push,
   GitHub release publication, or external manifest PR.
4. Publish v0.0.6 from the tested source with every file from its release-assets
   directory. Download the published DLLs and notices again and verify their hashes.
5. Copy the generated manifest/Artbyo3 tree into a fork of the community manifest,
   then submit one upstream PR. The upstream maintainers must approve and merge it.
6. After merge, test installation from Resolute on a clean RML setup, including
   dependency placement, upgrades, and removal. Do not advertise a verified
   Resolute installation until that has been tested.

## Subsequent updates

Run the local packaging command again for each new version. When updating an
existing catalog entry, merge the new version into its versions object instead
of discarding existing published versions or unrelated author information.

Submission is manual and requires explicit maintainer approval after live
testing. No public release-triggered submission workflow is included, and no
automation credential is needed to build, test, or prepare the local files.

## Upstream references

- https://github.com/resonite-modding-group/resonite-mod-manifest/wiki/Mod-Submission
- https://github.com/resonite-modding-group/resonite-mod-manifest/wiki/Submission-Guidelines
- https://github.com/resonite-modding-group/resonite-mod-manifest/tree/main/schemas
- https://github.com/Gawdl3y/Resolute
