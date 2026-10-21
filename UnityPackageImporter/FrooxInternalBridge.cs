/*
 * Private Resonite engine members accessed via reflection.
 * After each Resonite update, check that these still exist with the same signatures;
 * ReflectedMembers lists all dependencies for the startup compatibility check.
 *
 * - FrooxEngine.ModelImporter.PreprocessScene(Assimp.Scene)                      [static]
 * - FrooxEngine.ModelImporter.ImportNode(Assimp.Node, Slot, ModelImportData)     [static, returns IEnumerator<Context>]
 * - FrooxEngine.ModelImporter.SetupDraggable(Slot, solver, pos, rot, weight)     [static, 5 args; exact types not pinned in this file]
 * - FrooxEngine.SkinnedMeshRenderer.SetupBlendShapes()                           [instance]
 * - FrooxEngine.ModelImporter+ModelImportData.TryGetSlot(Assimp.Node)            [instance]
 * - FrooxEngine.ModelImporter+ModelImportData.TryGetBoneMapping(StaticMesh)      [instance]
 * - FrooxEngine.ModelImporter+ModelImportData(string, Assimp.Scene, Slot, Slot,
 *       ModelImportSettings, IProgressIndicator)                                 [constructor]
 *
 * Verified version: EngineCompatibilityCheck.LastVerifiedResoniteVersion.
 */

using System;
using System.Collections.Generic;
using System.Reflection;
using Assimp;
using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.UIX;

namespace UnityPackageImporter;

public static class FrooxInternalBridge
{
    internal static readonly Type ImportExtensionType =
        typeof(AssetHelper).GetNestedType("ImportExtension", BindingFlags.NonPublic);

    internal static readonly ConstructorInfo ImportExtensionConstructor =
        ImportExtensionType?.GetConstructor(new[] { typeof(string), typeof(bool) });

    internal static readonly FieldInfo AssociatedExtensionsField =
        typeof(AssetHelper).GetField("associatedExtensions", BindingFlags.NonPublic | BindingFlags.Static);

    internal static readonly MethodInfo LocalPressedAddMethod =
        typeof(Button).GetEvent("LocalPressed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetAddMethod(true);

    private static readonly MethodInfo _preprocessSceneMethod =
        typeof(ModelImporter).GetMethod("PreprocessScene", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);

    private static readonly MethodInfo _importNodeMethod =
        typeof(ModelImporter).GetMethod("ImportNode", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);

    private static readonly MethodInfo _setupDraggableMethod =
        typeof(ModelImporter).GetMethod("SetupDraggable", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);

    private static readonly MethodInfo _setupBlendShapesMethod =
        typeof(FrooxEngine.SkinnedMeshRenderer).GetMethod("SetupBlendShapes", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);

    private static readonly Type _modelImportDataType =
        typeof(ModelImporter).Assembly.GetType("FrooxEngine.ModelImporter+ModelImportData")
        ?? typeof(ModelImporter).GetNestedType("ModelImportData", BindingFlags.NonPublic | BindingFlags.Public);

    private static readonly MethodInfo _tryGetSlotMethod =
        _modelImportDataType?.GetMethod("TryGetSlot", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, new Type[] { typeof(Assimp.Node) }, null);

    private static readonly MethodInfo _tryGetBoneMappingMethod =
        _modelImportDataType?.GetMethod("TryGetBoneMapping", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, new[] { typeof(StaticMesh) }, null);

    private static readonly ConstructorInfo _modelImportDataConstructor =
        _modelImportDataType?.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, new[] { typeof(string), typeof(Assimp.Scene), typeof(Slot), typeof(Slot), typeof(ModelImportSettings), typeof(IProgressIndicator) }, null);

    // One entry per reflected engine dependency. Reuse the members used by the hooks,
    // so the startup check and the feature cannot disagree about lookup flags/signatures.
    internal static readonly (string Name, MemberInfo Member)[] ReflectedMembers =
    {
        ("AssetHelper.ImportExtension", ImportExtensionType),
        ("AssetHelper.ImportExtension..ctor(string, bool)", ImportExtensionConstructor),
        ("AssetHelper.associatedExtensions", AssociatedExtensionsField),
        ("Button.LocalPressed (add accessor)", LocalPressedAddMethod),
        ("ModelImporter.PreprocessScene", _preprocessSceneMethod),
        ("ModelImporter.ImportNode", _importNodeMethod),
        ("ModelImporter.SetupDraggable", _setupDraggableMethod),
        ("SkinnedMeshRenderer.SetupBlendShapes", _setupBlendShapesMethod),
        ("ModelImporter.ModelImportData", _modelImportDataType),
        ("ModelImporter.ModelImportData.TryGetSlot(Assimp.Node)", _tryGetSlotMethod),
        ("ModelImporter.ModelImportData.TryGetBoneMapping(StaticMesh)", _tryGetBoneMappingMethod),
        ("ModelImporter.ModelImportData..ctor(string, Assimp.Scene, Slot, Slot, ModelImportSettings, IProgressIndicator)", _modelImportDataConstructor)
    };

    public static void ValidateModelImportApi()
    {
        if (_preprocessSceneMethod == null || _importNodeMethod == null ||
            _modelImportDataConstructor == null || _tryGetSlotMethod == null || _tryGetBoneMappingMethod == null)
            throw new MissingMethodException("This Resonite version has an incompatible model import API. Prefab and scene import is unavailable.");
    }

    public static void PreprocessScene(Assimp.Scene scene)
    {
        if (_preprocessSceneMethod != null)
        {
            _preprocessSceneMethod.Invoke(null, new object[] { scene });
        }
        else
        {
            throw new MissingMethodException("ModelImporter.PreprocessScene is unavailable.");
        }
    }

    public static object CreateModelImportData(string file, Assimp.Scene scene, Slot targetSlot, Slot assetSlot, ModelImportSettings settings, IProgressIndicator progress)
    {
        if (_modelImportDataConstructor != null)
        {
            return _modelImportDataConstructor.Invoke(new object[] { file, scene, targetSlot, assetSlot, settings, progress });
        }
        throw new MissingMethodException("ModelImporter.ModelImportData constructor is unavailable.");
    }

    public static Slot TryGetSlot(object modelImportData, Assimp.Node node)
    {
        if (modelImportData == null) throw new ArgumentNullException(nameof(modelImportData));
        if (_tryGetSlotMethod == null) throw new MissingMethodException("ModelImportData.TryGetSlot is unavailable.");
        return (Slot)_tryGetSlotMethod.Invoke(modelImportData, new object[] { node })
            ?? throw new InvalidOperationException("Engine import did not create a slot for node: " + node.Name);
    }

    public static IEnumerator<Context> ImportNode(Node node, Slot targetSlot, object data)
    {
        if (_importNodeMethod != null)
        {
            return (IEnumerator<Context>)_importNodeMethod.Invoke(null, new object[] { node, targetSlot, data });
        }
        throw new MissingMethodException("ModelImporter.ImportNode is unavailable.");
    }

    public static Dictionary<string, Slot> TryGetBoneMapping(object modelImportData, StaticMesh mesh)
    {
        if (modelImportData == null) throw new ArgumentNullException(nameof(modelImportData));
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));
        if (_tryGetBoneMappingMethod == null) throw new MissingMethodException("ModelImportData.TryGetBoneMapping is unavailable.");
        return (Dictionary<string, Slot>)_tryGetBoneMappingMethod.Invoke(modelImportData, new object[] { mesh });
    }

    public static void SetupDraggable(Slot slot, object solver, object pos, object rot, object weight)
    {
        if (_setupDraggableMethod != null)
        {
            _setupDraggableMethod.Invoke(null, new object[] { slot, solver, pos, rot, weight });
        }
        else
        {
            UnityPackageImporter.Warn("ModelImporter.SetupDraggable method not found via reflection!");
        }
    }

    public static void SetupBlendShapes(FrooxEngine.SkinnedMeshRenderer smr)
    {
        if (_setupBlendShapesMethod != null)
        {
            _setupBlendShapesMethod.Invoke(smr, null);
        }
        else
        {
            UnityPackageImporter.Warn("SkinnedMeshRenderer.SetupBlendShapes method not found via reflection!");
        }
    }
}
