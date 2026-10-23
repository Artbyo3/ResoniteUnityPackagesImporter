# Authored avatar assemblies

Prefab entries are selected from the package's structural dependency graph. A
prefab referenced by another prefab's PrefabInstance.m_SourcePrefab remains in
the asset library, but is not also published as an independent station result.
Unrelated root prefabs remain separate results. Cyclic or malformed dependency
graphs fail explicitly; a failed assembly never falls back to importing its
accessories separately.

Before reconstructing a root prefab, native prefab instances are composed in
memory. Composition preserves instance-local identities, stripped references,
authored parents, added and removed objects/components, and serialized overrides.
FBX instances remain native model imports. Inherited FBX object identities are
resolved against the imported model's actual identity index, not object names or
the first FBX found in an assembly. Missing prefab references remain explicit
dependencies; composition does not invent missing objects or modify the package
cache.

The semantic avatar/Modular Avatar index and runtime reconstruction consume the
same composed YAML. Structural conversion uses the operation's owning avatar
descriptor root. Humanoid metadata can resolve Bone Proxy targets before native
rig preparation, without depending on a literal Resonite bone-name spelling.
Ambiguous skeleton mappings and overlapping avatar ownership remain errors.

Humanoid targets are resolved from the owning model's imported identities, not a
recursive name search through the avatar and its clothing. Declared but unresolved
metadata stays unresolved instead of borrowing another model's bones. Explicit
attachment paths cannot fall back to an unrelated same-named armature; exact-case
paths take priority and ambiguous siblings fail resolution. Bone Proxy attachment
modes preserve the authored position, rotation, and scale policy, including
avatar-root-relative paths.

Armature conversion and standalone outfit installation retain the original skin
bone identities and bind spaces. Mapped source bones follow target bones through
world-transform-preserving parenting; unmapped children retain their original
parents. Mesh assets and renderer bone references are not rewritten. A transfer
that cannot preserve its transform is rejected rather than silently distorting
the skin. Standalone installation tracks transferred bones for removal, toggling,
and rollback.

Station previews are centered on the corresponding descriptor or unique biped
root using a presentation-only wrapper separate from the duplicated hierarchy.
The anchor offset is captured from source-local transforms before duplication,
without depending on the copied hierarchy's immediate world-transform state.
Staging placement, preview rotation, and preview scale do not enter this offset.
Authored source transforms and template drivers are left unchanged.
A recognized avatar keeps the Create Avatar label
when validation blocks it; this does not enable publication of incomplete results.
Missing prefab sources remain in result diagnostics. A missing source does not
necessarily mean that its visible content is absent: some exports contain complete
local serialized snapshots alongside their source provenance. The composer can
reconstruct supported, non-stripped local snapshots after verifying their object
references, component ownership, finite transforms, parent chains, and authored
overrides. IDs, materials, meshes, and MA attachment settings are retained. The
original package is never edited, and the absent source instance stays diagnostic.
Incomplete or ambiguous snapshots still fail rather than inventing missing data.

Static MeshFilter/MeshRenderer pairs are reconstructed using the exact model mesh
fileID, with imported material defaults and authored material overrides preserved.
Inherited model renderers retain their actual Unity component class and resolve
within their owning model instance. Static geometry uses the shared renderer and
material conversion path with no skin bones; a mesh requiring skin bindings is
rejected rather than silently losing its bindings.

Mesh source resolution distinguishes imported model meshes, locally serialized
Unity Mesh assets, and explicitly supported built-in resource identities. Static
Unity Mesh assets with readable vertex/index buffers are decoded without losing
their normals, UV channels, colors, tangents, or material submesh boundaries, then
passed through the same native model-import bridge as FBX geometry. Serialized
coordinates are already Unity mesh units and do not receive FBX unit scaling.
The built-in cube resource retains a centered unit-size mesh; unknown built-in
identities, compressed/streamed meshes, and unsupported topology remain explicit
errors rather than being replaced with unrelated geometry.

Native static renderer lookup and replacement use exact component types, not
base-class matching: Resonite's skinned renderer is also a MeshRenderer. A newly
created renderer must never be mistaken for the old static component to destroy.
Replacement only destroys the previously captured static component after its
mesh has loaded; it does not delete the owning object or its replacement.

An unavailable stripped Transform alias with no locally serialized GameObject can
be deferred only when nothing references it beyond a hierarchy child list. Its
serialized record and missing-source warning remain; no visible object is deleted.
An actual component target, parent link, or required alias remains blocking. This
does not implement the absent editor plugin or recover content that was never
exported. Reconstruction and conversion failures continue to disable Create Avatar.

Legacy Unity class-1001 records are interpreted by their schema, not their class
number alone. A legacy Prefab definition with no source parent is library metadata,
not a missing nested instance. Its root reference and authored objects are retained.
Linked legacy instances normalize their parent source and object ownership aliases
before dependency selection and composition, so modern variants can inherit legacy
assets without creating separate dependency-only outputs. Conflicting aliases or
inconsistent legacy records remain explicit errors; the extracted cache is immutable.

An override referencing an object absent from its source prefab remains a failure
of that assembly. It must not be redirected by name or silently dropped. Other
independent roots continue through the existing source-batch isolation.

This supports structural assembly, not all Unity editor build plugins, shaders,
or Modular Avatar animation/menu behavior. A package must still include its
referenced assets. Test and Build are necessary offline checks; only installing
the DLL, fully restarting Resonite, and importing through the real mod confirms
the resulting appearance and attachment behavior.
