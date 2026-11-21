# Unity built-in mesh reference data

The importer recognizes the built-in resource GUID
`0000000000000000e000000000000000` only with the identities below. Unknown
identities remain unsupported; package mesh GUIDs are not replaced by primitives.

## Provenance

Captured locally on 2026-10-25 using Unity Editor **2022.3.22f1**, in an isolated
empty project, with no imported package scripts. For each `PrimitiveType`, the
capture created a temporary `GameObject.CreatePrimitive`, read its MeshFilter's
shared mesh and destroyed the temporary object. Identity came from
`AssetDatabase.TryGetGUIDAndLocalFileIdentifier`; geometry came from the public
`Mesh.vertices`, `normals`, `tangents`, `uv` and `triangles` APIs. No user avatar
geometry is included. This capture does not establish equivalence with every
Unity editor version or successful native rendering in Resonite.

| Identity | Primitive | Vertices | Triangle indices |
| --- | --- | ---: | ---: |
| 10202 | Cube | 24 | 36 |
| 10206 | Cylinder | 88 | 240 |
| 10207 | Sphere | 515 | 2304 |
| 10208 | Capsule | 550 | 2496 |
| 10209 | Plane | 121 | 600 |
| 10210 | Quad | 4 | 6 |

The sphere and capsule retain the editor's actual seam topology, rather than an
approximate procedural replacement. The plane's first vertex is approximately
`(5, 0, 5)` with UV `(0, 0)` and tangent `(-1, 0, 0, -1)`. Its X/Z grid decreases
as UV increases. The previous generated plane reversed that UV orientation.
Small captured floating-point deviations are retained, not rounded away.

## Resource format and validation

`Models/BuiltinMeshes/<identity>.mesh.txt` stores Base64-encoded gzip data.
The uncompressed, little-endian payload contains:

1. Four ASCII bytes `UPM1`, then three int32 values: identity, vertex count,
   triangle-index count.
2. For each vertex, twelve float32 values: position XYZ, normal XYZ, tangent
   XYZW, UV XY.
3. The triangle indices as int32 values, preserving their authored order.

`UnityBuiltinMeshes` pins the counts and SHA-256 digest of each entire raw
payload. Loading is bounded to that exact length; excess and truncated data are
rejected. Each import receives separate mutable arrays. Regression tests
independently serialize the decoded channels and compare their whole-data hashes,
check corruption and isolation, and check plane winding, area and UV orientation.
Analytic grid checks allow capture-level float error; exact reference fingerprints
still reject any changed component or index. No live appearance claim follows
from those tests.
