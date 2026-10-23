using System.Collections.Generic;
using System.Threading.Tasks;
using UnityPackageImporter.Models;

namespace UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes;

public sealed class MeshFilter : IUnityObject
{
    public ulong id { get; set; }
    public bool instanciated { get; set; }
    public Dictionary<string, ulong> m_GameObject;
    public SourceObj m_Mesh;
    public SourceObj m_CorrespondingSourceObject { get; set; }
    public Dictionary<string, ulong> m_PrefabInstance { get; set; }

    public Task InstanciateAsync(IUnityStructureImporter importer)
    {
        instanciated = true;
        return Task.CompletedTask;
    }
}
