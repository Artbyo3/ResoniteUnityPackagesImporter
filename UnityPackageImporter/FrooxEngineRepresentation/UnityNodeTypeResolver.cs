using System;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace UnityPackageImporter.FrooxEngineRepresentation;

class UnityNodeTypeResolver : INodeTypeResolver
{
    private const string UnityTagPrefix = "tag:unity3d.com,2011:";

    public bool Resolve(NodeEvent nodeEvent, ref Type currentType)
    {
        if (nodeEvent.Tag != null && nodeEvent.Tag.Value.StartsWith(UnityTagPrefix, StringComparison.Ordinal))
        {
            if (!int.TryParse(nodeEvent.Tag.Value.Replace(UnityTagPrefix, ""), out _))
            {
                return false;
            }
            currentType = typeof(UnityEngineObjectWrapper);
            return true;
        }

        return false;
    }
}
