using UnityPackageImporter.Models;

namespace UnityPackageImporter.FrooxEngineRepresentation;

public class MModificationsParser
{
    public static bool ParseModifcation(IUnityObject targetobj, ModsPrefab mod)
    {
        if (mod == null) return false;
        if (UnityPropertyPath.TrySet(targetobj, mod.propertyPath, mod.value, mod.objectReference, out var error))
            return true;
        UnityPackageImporter.Warn($"Unsupported prefab override '{mod.propertyPath}' on {targetobj?.GetType().Name}: {error}");
        return false;
    }
}
