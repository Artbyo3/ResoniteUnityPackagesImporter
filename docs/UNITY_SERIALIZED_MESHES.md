# Serialized mesh decoding evidence

## Current boundary

UnityPackageImporter/Models/UnitySerializedMesh.cs decodes inline, uncompressed
Unity YAML mesh buffers. The original static entry point remains static-only and
rejects deformation data. UnityMeshSourceImporter.cs still converts static data
into an Assimp scene. A separate bounded serialized-skin path now preserves
bind poses, explicit vertex influences and sparse blend shape frames, then
builds native MeshX assets directly. Unsupported layouts still reject rather
than producing flattened static substitutes. Native avatar deformation remains
unverified; offline asset serialization is not live acceptance.

This is an implementation boundary to extend with evidence, not an exclusion
from the whole-avatar goal. Built-in mesh resource verification is documented
separately in UNITY_BUILTIN_MESHES.md.

## Structural validation repair, 2026-10-26

Inspection found optional sequence reads returned an empty sequence for both
missing fields and fields of the wrong YAML type. As a result, scalar/mapping
bind poses and malformed shape containers could pass the unsupported-skin guard.
The parser also ignored orphan shape weights, overlapping vertex attributes,
and trailing partial index elements. A declared large stream extent overflowed
32-bit arithmetic rather than producing a structured invalid-data diagnostic.

The repair distinguishes absent optional fields from malformed present fields:

- Bind poses, bone bounds, and shape arrays must be sequences when present.
- Shape and external-stream containers must be mappings when present; scalar
  fields such as the external path must not hide a mapping or sequence.
- Nonempty shape fullWeights are unsupported deformation evidence even if the
  other shape arrays are empty. These guarded fields no longer silently bypass
  the static-only decoder boundary when malformed.
- Active vertex attributes cannot overlap byte ranges within the same stream.
  Equal offsets in different streams remain valid. Disabled channels consume no
  storage and do not create false overlaps.
- Stream extents use 64-bit arithmetic and are checked against the actual buffer
  before narrowing to offsets or allocating output channels. Inter-stream
  alignment remains supported without requiring unused final alignment padding.
- Both 16-bit and 32-bit index buffers must contain complete index elements.
  Existing submesh range, topology, and vertex-index checks remain in force.

## Executable evidence

Tests/UnitySerializedMeshTests.cs contains 20 added regressions: 17 negative
reproductions plus explicitly empty optional metadata, sparse split streams
without final padding, and partial 32-bit index rejection. The first 17 cases
failed against the preceding implementation (16 silent acceptances and one
OverflowException). The original tests were retained unchanged.

Final automated results are recorded in WORKING_STATE.md. This batch establishes
decoder rejection and static-buffer preservation only. It does not establish a
real package's appearance, native deformation, or support for additional Unity
serialization versions. No component coverage credit is assigned.

## Serialized skin reconstruction batch, 2026-10-26

UnitySerializedSkinData.cs retains ordered bind matrices, one-to-four explicit
weight/index pairs per vertex, and the shape channel/frame/delta hierarchy.
The supported mesh serialization version is 11; an explicit vertex-data version
must be 3. Supported index formats are unsigned 8-, 16- and 32-bit. Matching
weight/index dimensions, finite normalized weights, valid affine bind matrices,
triangle indices and exact frame ownership are required. Compression, external
streams, implicit rigid weights, populated variable-count storage and unsupported
versions remain rejected. Empty variable-count `m_Data` storage is accepted.

Native bone targets come only from the renderer's exact ordered `m_Bones`
references, which must match bind-pose count. Name hashes are optional scalar
metadata, not bone identities; no name/hash guessing is used. Missing weighted
targets reject even for a positive epsilon weight. Proven-unused null slots
retain their original indices. Empty infinite min/max bounds are permitted only
for proven-unused bones; used bounds must be finite and valid.

UnitySerializedSkinMeshImporter.cs uses public Elements.Assets MeshX APIs to
preserve vertex indices, material submeshes, normals, UVs, colors, influence bone
identities, bind matrices and shape names. Sparse untouched deltas stay zero;
empty shape frames are retained. Decoding retains every authored frame, but native
construction rejects channels other than one frame at exactly 100 percent before
creating a provider or renderer. Tangent handedness follows the native
ModelImporter convention. No Assimp vertex merging, weight trimming,
shape stripping or single-frame flattening is applied to this path. It does not
reuse FBX unit scaling for already-authored serialized positions/matrices.

Native bindings use -1 for exactly-zero influence indices and public BoneBinding
Normalize/Sort without Trim or FillInEmpty. The decoder already requires a near-unit
positive total; normalization removes quantization drift while retaining every
positive influence, including epsilon. The original decoded weights stay intact.
The audit checks normalization, descending weights, target identity and sentinels.
Material tasks are padded to native submesh count before applying authored indexed
overrides, so empty slots cannot shift subsequent material assignments. Existing
renderers are not reused or destroyed based only on their component type.

Installed Resonite 2026.9.18.82 MeshX CPU evaluation reads only shape frame zero
and ignores its threshold; baking rejects multiple frames. Storage support alone
therefore cannot authorize those channels. Non-full-weight single frames also
reject rather than silently changing the deformation slope. Source parsing tests
retain multi-frame evidence independently of this native construction guard.

Mesh assets are cached per exact GUID/fileID. Each renderer retains its own
authored owner, targets, materials and default shape weights. Mesh readiness and
vertex/bone/shape counts are checked before binding. Decoder rejection has its
own sanitized SerializedSkinDecodeRejected reason, retaining the distinction
from missing models. Existing source-structure and avatar-readiness gates remain.

## Repeatable native asset audit

Use the Native skin asset audit command in AGENTS.md after building the DLL.
Tests/SerializedSkinNativeAudit.cs loads that exact build and the supplied engine
assemblies, calls repository-owned decoding/building code, and exercises public
MeshX Plain and LZ4 serialization. It creates no world content or renderer.
Optional local assets remain private; do not commit paid fixtures or raw output.

The synthetic fixture checks matrix orientation/translation, influence identity,
single-frame thresholds/deltas, sparse zeros and tangent handedness. It also checks
rejection of multi-frame/non-full-weight channels and native half-weight baking
against the expected vertex displacement. Round trips
compare geometry channels, submesh indices, bone identities/bind matrices and
every shape frame, including normal/tangent deltas. Five retained source assets
also passed the audit, spanning these observed layouts:

| Vertices | Bind poses | Shape channels | Input variation |
| --- | --- | --- | --- |
| 1,553 | 146 | 0 | Compact explicit weights, 32-bit bone indices |
| 835 | 198 | 3 | Four explicit weights, sparse shapes |
| 17,749 | 296 | 49 | Body mesh with shape-heavy deformation data |
| 1,030 | 510 | 0 | 16-bit bone indices, optional hashes absent |
| 6,468 | 348 | 84 | Compact weights and zero-delta shape frames |

Installed API evidence is from Resonite 2026.9.18.82. No new engine-internal
reflection is used; audit reflection into nonpublic members is restricted to
this repository's own decoder/builder. The original static rejection regressions
remain, with additional guards against legacy/variable deformation storage even
when bind poses are absent. UnitySerializedSkinTests.cs supplies positive and
negative deformation cases independently of engine availability.

## Remaining evidence needed

The integrated batch is installed, but no fresh engine load is confirmed. Verify native asset loading,
deformation after armature alignment, shape-driven appearance, normals/tangents,
material slot correspondence and ownership in the consolidated live pass. The
offline audit cannot establish these outcomes. Multi-frame and non-full-weight
shape channels, implicit single-bone weights,
variable counts above four and other rejected layouts need separate source/API
evidence and regressions before support. The native material callback exception
and unrelated prefab composer failures remain separate work. No component
coverage promotion or whole-avatar success is claimed. The single consolidated
live checklist remains in AVATAR_CONVERSION_PROGRESS.md.
