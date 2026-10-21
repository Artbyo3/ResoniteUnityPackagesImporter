# Stabilization pass

Recovery point: commit `94256d8` preserves the source before this pass, including the unfinished prefab edit. That checkpoint does not compile.

## Changes

- Restore compilation by fixing the missing prefab branch brace.
- Target .NET 10 with SDK reference assemblies; remove manual runtime references and the user-specific YamlDotNet path. Allow `GamePath` overrides and include YamlDotNet in the build output.
- Extract archives into a unique staging directory. Validate both tar entry names and Unity asset pathnames; reject traversal, absolute paths, links, Windows device names, alternate data streams, and duplicate destinations.
- Limit extraction to 8 GiB of decompressed tar data and 100,000 visible tar entries by default. Assets must be under `Assets/` or `Packages/`.
- Preserve Unicode filenames, numeric extensions, and metadata independently of import settings.
- Publish the extracted cache only after success. Use a versioned completion manifest, check cached file existence/length, and rebuild incomplete caches. New imports use a `v2` cache subdirectory; old caches are not reused.
- Close hashing streams and use a separate hash instance for each package.
- Share material and texture import tasks by asset GUID. Same-name assets no longer collide, and callers wait for the shared import to finish. Read texture scale/offset by material property rather than texture GUID.
- Reuse the actual FBX import task on repeated calls; propagate model/coroutine failures, clean up temporary FBX templates on failure, and fail mesh waits after two minutes.
- Check required model import bridge members before creating project assets. Missing required members now produce explicit errors instead of null results.
- Preserve exception stack traces, and assign imported materials on the world thread.

The extraction cancellation token is available to callers; this pass does not add an in-game Cancel button. Cache manifests validate file lengths, not the content of already extracted files. The cache directory is dedicated to one package hash and must not be used for user files.

## Automated verification

The console regression suite links the actual extraction and async-cache source files and needs no game installation or additional test framework. It generates small gzip/tar packages and checks extraction, cache reuse/recovery, rejected paths and links, duplicate destinations, limits, cancellation, and shared task success/failure.

The optional engine metadata check reads the installed DLL without executing game code. It checks method presence/arity and distinguishes the Node overload of TryGetSlot; it does not test the engine's runtime behavior.

## In-game verification still required

1. Build the Release mod DLL. With Resonite closed, put `UnityPackageImporter.dll` in `rml_mods` and the build output's `YamlDotNet.dll` in `rml_libs`. Fully restart Resonite and check the log for loader or patch failures.
2. In a disposable local world, import a small known package with a prefab, FBX, material, and texture. Check object count, hierarchy, transforms, material assignments, and texture appearance.
3. Import it again. Verify the cache is reused and the resulting objects match the first import.
4. Test two different textures with the same basename and two same-name materials in separate asset folders. Verify each renderer gets its own intended asset.
5. Test a skinned humanoid: inspect bone mapping, blend shapes, scale, and IK. Test a non-humanoid prop separately.
6. Test a missing dependency or unsupported component and inspect the failure/warning output. No import should remain waiting forever for a mesh.

The initial stabilization pass had no real-package verification. Real packages were subsequently tested in-engine. Successful extraction does not establish correct prefab appearance.

## Remaining work

- Implement full expansion of plain nested prefabs without a backing model. Mesh-free content can be deferred; mesh-bearing or unverifiable content still blocks output.
- Normalize signed Unity file IDs throughout parsing and lookup, removing numeric conversion heuristics.
- Add dedicated static MeshRenderer/MeshFilter conversion and report unsupported Unity components.
- Improve transparency, shader mapping, normal/color texture handling, and conversion diagnostics beyond the existing structured material YAML parser.
- Make humanoid/IK setup optional, preserve authored scale, and validate required bones and degenerate bounds.
- Extend cancellation and per-asset reporting for the remaining import paths; bounded model imports and station-close cancellation already exist.
- Add representative synthetic fixtures and record in-game regression results before calling this a stable release.
