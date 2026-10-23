namespace UnityPackageImporter.Models;

// Unity class IDs used to match an imported model to its .meta file IDs.
// These names also participate in Unity's hashed model-object identity paths.
// YAML component dispatch is independent of this enum.
internal enum UnityModelClassId
{
    GameObject = 1,
    Transform = 4,
    MeshRenderer = 23,
    Mesh = 43,
    SkinnedMeshRenderer = 137
}
