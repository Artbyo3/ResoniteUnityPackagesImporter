using System.Collections.Generic;
using System.Threading.Tasks;
using FrooxEngine;
using UnityPackageImporter.Models;

namespace UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes;

public class MonoBehaviour : IUnityObject
{
    public ulong id { get; set; }
    public bool instanciated { get; set; }
    public Dictionary<string, ulong> m_GameObject { get; set; }
    public SourceObj m_CorrespondingSourceObject { get; set; }
    public Dictionary<string, ulong> m_PrefabInstance { get; set; }

    public async Task InstanciateAsync(IUnityStructureImporter importer)
    {
        if (instanciated) return;
        instanciated = true;

        if (m_GameObject != null &&
            m_GameObject.TryGetValue("fileID", out ulong goId) &&
            importer.existingIUnityObjects.TryGetValue(goId, out IUnityObject slotOnity))
        {
            await default(ToWorld);
            await slotOnity.InstanciateAsync(importer);
            await default(ToBackground);
        }
    }
}
