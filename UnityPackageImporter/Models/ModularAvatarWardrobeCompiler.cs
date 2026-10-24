#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace UnityPackageImporter.Models;

public static partial class ModularAvatarInitialAppearance
{
    public const string MenuGroupGuid = "97e46a47dd8a425eb4ce9411defe313d";
    public const string MenuInstallTargetGuid = "1fad1419b52a42ae89b0df52eb861e47";

    internal static AvatarWardrobePlan CompileWardrobe(string composedYaml, InitialAppearanceContext context,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        token.ThrowIfCancellationRequested();
        var diagnostics = new List<InitialAppearanceDiagnostic>();
        var controls = new List<AvatarWardrobeControl>();
        void Report(string code, string message, long source = 0, InitialAppearanceSeverity severity = InitialAppearanceSeverity.Error) =>
            diagnostics.Add(new(severity, "MA_WARDROBE_" + code, message, source));
        AvatarWardrobePlan Result(AvatarWardrobeGraph? shared = null)
        {
            bool unsupported = diagnostics.Any(item => item.Severity != InitialAppearanceSeverity.Info);
            return new()
            {
                Controls = controls.AsReadOnly(), Diagnostics = diagnostics.AsReadOnly(), HasUnsupportedContext = unsupported,
                SharedState = !unsupported && controls.Count > 0 ? shared : null
            };
        }
        Dictionary<long, Document> documents;
        try { documents = Read(composedYaml, token); }
        catch (Exception error) when (error is InvalidDataException or YamlException or FormatException or OverflowException or ArgumentException)
        {
            Report("YAML", "Malformed composed Unity YAML: " + error.Message);
            return Result();
        }
        var maMenuOrReactiveGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ObjectToggleGuid, ShapeChangerGuid, MaterialSetterGuid, MenuItemGuid,
            MenuGroupGuid, MenuInstallTargetGuid, AvatarPackageIndex.MenuInstallerScriptGuid, AvatarPackageIndex.ParametersScriptGuid
        };
        if (!documents.Values.Any(item => maMenuOrReactiveGuids.Contains(Script(item))))
            return Result();

        var gameObjects = documents.Values.Where(item => item.ClassId == 1).ToDictionary(item => item.Id);
        var transformByObject = new Dictionary<long, Document>();
        var parent = new Dictionary<long, long>();
        foreach (var transform in documents.Values.Where(item => item.ClassId is 4 or 224))
        {
            token.ThrowIfCancellationRequested();
            long owner = Reference(transform.Body, "m_GameObject");
            if (!transformByObject.TryAdd(owner, transform)) Report("HIERARCHY", "Duplicate Transform ownership.", transform.Id);
            long father = Reference(transform.Body, "m_Father");
            if (father != 0 && (!documents.TryGetValue(father, out var parentTransform) || parentTransform.ClassId is not (4 or 224)))
                Report("HIERARCHY", "Unresolved parent Transform.", transform.Id);
            else parent[owner] = father == 0 ? 0 : Reference(documents[father].Body, "m_GameObject");
        }
        bool Within(long candidate, long ancestor)
        {
            var visited = new HashSet<long>();
            while (candidate != 0 && visited.Add(candidate))
            {
                if (candidate == ancestor) return true;
                candidate = parent.GetValueOrDefault(candidate);
            }
            return false;
        }
        if (context.AvatarRootId == 0 || !gameObjects.ContainsKey(context.AvatarRootId) || !transformByObject.ContainsKey(context.AvatarRootId))
        {
            if (documents.Values.Any(item => maMenuOrReactiveGuids.Contains(Script(item))))
                Report("ROOT", "An explicit assembled avatar GameObject identity with a Transform is required.");
            return Result();
        }
        var scoped = gameObjects.Keys.Where(identity => Within(identity, context.AvatarRootId)).ToHashSet();
        if (!documents.Values.Any(item => scoped.Contains(Reference(item.Body, "m_GameObject")) &&
            (Script(item) == MenuItemGuid || IsReactive(Script(item)) || Script(item) == AvatarPackageIndex.MenuInstallerScriptGuid)))
            return Result();

        var ordered = new List<long>();
        var visitedObjects = new HashSet<long>();
        void Visit(long identity)
        {
            token.ThrowIfCancellationRequested();
            if (!visitedObjects.Add(identity)) { Report("HIERARCHY", "Cyclic or repeated hierarchy child.", identity); return; }
            ordered.Add(identity);
            if (!transformByObject.TryGetValue(identity, out var transform)) { Report("HIERARCHY", "Missing Transform.", identity); return; }
            foreach (var child in Sequence(transform.Body, "m_Children"))
            {
                long childTransform = ReferenceNode(child);
                if (!documents.TryGetValue(childTransform, out var childDocument) || childDocument.ClassId is not (4 or 224))
                { Report("HIERARCHY", "Unresolved child Transform.", identity); continue; }
                long childObject = Reference(childDocument.Body, "m_GameObject");
                if (!scoped.Contains(childObject) || parent.GetValueOrDefault(childObject) != identity)
                    Report("HIERARCHY", "Child and parent identities disagree.", childTransform);
                else Visit(childObject);
            }
        }
        Visit(context.AvatarRootId);
        if (!scoped.SetEquals(ordered)) Report("HIERARCHY", "Incomplete child order; precedence cannot be inferred.");
        var components = new List<Document>();
        foreach (long identity in ordered)
        {
            var componentIds = new HashSet<long>();
            foreach (var entry in Sequence(gameObjects[identity].Body, "m_Component"))
            {
                long componentId = Reference(entry as YamlMappingNode, "component");
                if (!componentIds.Add(componentId) || !documents.TryGetValue(componentId, out var component) || Reference(component.Body, "m_GameObject") != identity)
                    Report("COMPONENT", "Invalid component identity in hierarchy order.", identity);
                else components.Add(component);
            }
        }
        var menuItems = components.Where(item => Script(item) == MenuItemGuid).ToList();
        var reactive = components.Where(item => IsReactive(Script(item))).ToList();
        if (menuItems.Count == 0 || reactive.Count == 0)
            return Result();

        static bool IsPureVrcFuryArmatureLink(YamlMappingNode body)
        {
            try
            {
                var graph = VrcFuryManagedReferenceGraph.Read(body);
                if (graph.IsValid && graph.ReachableFeatures.Count > 0)
                {
                    foreach (var entry in graph.ReachableFeatures)
                    {
                        if (Get(entry, "type") is YamlMappingNode type &&
                            Get(type, "class") is YamlScalarNode cls &&
                            cls.Value == "ArmatureLink")
                        {
                            continue;
                        }
                        return false;
                    }
                    return true;
                }
                return Get(body, "propBone") != null || Get(body, "linkTo") != null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return false;
            }
        }

        if (!context.ExternalAnimationAndParameterContextVerifiedAbsent && !context.AllowMixedFrameworks)
            Report("CONTEXT", "External controllers, parameters and uncompiled runtime context must be verified absent.");
        foreach (var component in components)
        {
            string script = Script(component);
            long scriptFileId = Reference(component.Body, "m_Script");
            bool structural = scriptFileId == 11500000 && (script.Equals(AvatarPackageIndex.MergeArmatureScriptGuid, StringComparison.OrdinalIgnoreCase) ||
                script.Equals(AvatarPackageIndex.BoneProxyScriptGuid, StringComparison.OrdinalIgnoreCase) ||
                script.Equals(AvatarPackageIndex.OutfitRootScriptGuid, StringComparison.OrdinalIgnoreCase) ||
                script.Equals(AvatarPackageIndex.BlendshapeSyncScriptGuid, StringComparison.OrdinalIgnoreCase) ||
                script.Equals(AvatarPackageIndex.VrcFuryArmatureLinkScriptGuid, StringComparison.OrdinalIgnoreCase) ||
                (AvatarPackageIndex.RecognizedVrcFuryScriptGuids.Contains(script) && IsPureVrcFuryArmatureLink(component.Body)));
            bool supportedMa = scriptFileId == 11500000 && maMenuOrReactiveGuids.Contains(script);
            bool descriptor = scriptFileId == AvatarPackageIndex.VrcAvatarDescriptorScriptFileId &&
                script.Equals(AvatarPackageIndex.VrcAvatarDescriptorScriptGuid, StringComparison.OrdinalIgnoreCase);
            bool HasReference(YamlNode? node) => node is YamlMappingNode reference &&
                (ReferenceNode(reference) != 0 || Text(reference, "guid").Trim('0').Length != 0);
            if (descriptor && !context.AllowMixedFrameworks &&
                (HasReference(Get(component.Body, "expressionParameters")) ||
                    Sequence(component.Body, "baseAnimationLayers").Concat(Sequence(component.Body, "specialAnimationLayers"))
                        .OfType<YamlMappingNode>().Any(layer => HasReference(Get(layer, "animatorController")))))
                Report("CONTEXT", "Descriptor parameters/controllers require the shared Animator compiler.", component.Id);
            if (!context.AllowMixedFrameworks && (component.ClassId is 95 or 111 || script.Equals(MergeAnimatorGuid, StringComparison.OrdinalIgnoreCase)))
                Report("CONTEXT", "Animator, legacy Animation or MergeAnimator requires the shared Animator compiler.", component.Id);
            if (component.ClassId == 114 && !structural && !supportedMa && !descriptor &&
                (!context.AllowMixedFrameworks || !AvatarPackageIndex.RecognizedVrcFuryScriptGuids.Contains(script)))
                Report("CONTEXT", "Coexisting MonoBehaviour runtime interactions have not been verified absent.", component.Id);
        }

        static bool TryParseBoolField(YamlMappingNode node, string key, bool defaultValue, out bool result)
        {
            var valNode = Get(node, key);
            if (valNode == null)
            {
                result = defaultValue;
                return true;
            }
            if (valNode is YamlScalarNode s)
            {
                if (s.Value is "1" or "true") { result = true; return true; }
                if (s.Value is "0" or "false") { result = false; return true; }
            }
            result = false;
            return false;
        }

        // 1. Resolve ModularAvatarParameters scopes (outermost to innermost)
        var paramComponentsByObject = components.Where(item => Script(item) == AvatarPackageIndex.ParametersScriptGuid)
            .GroupBy(item => Reference(item.Body, "m_GameObject")).ToDictionary(group => group.Key, group => group.ToList());
        var remapAtObject = new Dictionary<long, Dictionary<string, (string Mapped, bool Internal, AvatarParameterType? Type, double? DefaultValue, bool? Saved, bool? Synced, long SourceId)>>();
        var explicitDecls = new Dictionary<string, (AvatarParameterType? Type, double? DefaultValue, bool OverrideAnimatorDefaults, bool? Saved, bool? Synced, long SourceId)>(StringComparer.Ordinal);
        string avatarId = context.AvatarRootId.ToString(CultureInfo.InvariantCulture);
        foreach (long objId in ordered)
        {
            var currentMap = objId != context.AvatarRootId && parent.TryGetValue(objId, out long parentId) && remapAtObject.TryGetValue(parentId, out var inherited)
                ? new Dictionary<string, (string Mapped, bool Internal, AvatarParameterType? Type, double? DefaultValue, bool? Saved, bool? Synced, long SourceId)>(inherited, StringComparer.Ordinal)
                : new Dictionary<string, (string Mapped, bool Internal, AvatarParameterType? Type, double? DefaultValue, bool? Saved, bool? Synced, long SourceId)>(StringComparer.Ordinal);
            if (paramComponentsByObject.TryGetValue(objId, out var paramComps))
            {
                foreach (var paramComp in paramComps)
                {
                    if (Get(paramComp.Body, "parameters") is not YamlSequenceNode paramList)
                    {
                        Report("PARAMETER_LIST", "ModularAvatarParameters is missing a valid parameters sequence.", paramComp.Id);
                        continue;
                    }
                    int pIndex = 0;
                    foreach (var childNode in paramList.Children)
                    {
                        if (childNode is not YamlMappingNode pNode)
                        {
                            Report("PARAMETER_ENTRY", "ModularAvatarParameters contains a non-mapping entry.", paramComp.Id);
                            continue;
                        }
                        if (!TryParseBoolField(pNode, "isPrefix", false, out bool isPrefix) ||
                            !TryParseBoolField(pNode, "internalParameter", false, out bool internalParam) ||
                            !TryParseBoolField(pNode, "saved", false, out bool savedVal) ||
                            !TryParseBoolField(pNode, "localOnly", false, out bool localOnly) ||
                            !TryParseBoolField(pNode, "m_overrideAnimatorDefaults", false, out bool pbOnly))
                        {
                            Report("PARAMETER_BOOL", "ModularAvatarParameters entry has a malformed boolean field.", paramComp.Id);
                            continue;
                        }
                        string nameOrPrefix = Text(pNode, "nameOrPrefix");
                        string remapTo = Text(pNode, "remapTo");
                        if (isPrefix)
                        {
                            Report("PARAMETER_PREFIX", "ModularAvatarParameters isPrefix remapping is not supported in this slice.", paramComp.Id);
                            continue;
                        }
                        if (string.IsNullOrWhiteSpace(nameOrPrefix))
                        {
                            Report("PARAMETER_NAME", "ModularAvatarParameters entry has an empty nameOrPrefix.", paramComp.Id);
                            continue;
                        }
                        if (Get(pNode, "syncType") is not YamlScalarNode syncScalar ||
                            !int.TryParse(syncScalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int syncType) ||
                            syncType is < 0 or > 3)
                        {
                            Report("PARAMETER_SYNC_TYPE", "ModularAvatarParameters entry has a missing or invalid syncType.", paramComp.Id);
                            continue;
                        }
                        double? defVal = null;
                        if (Get(pNode, "defaultValue") != null)
                        {
                            if (Get(pNode, "defaultValue") is not YamlScalarNode defScalar ||
                                !double.TryParse(defScalar.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double dv) ||
                                !double.IsFinite(dv))
                            {
                                Report("PARAMETER_DEFAULT", "ModularAvatarParameters entry has a non-finite or malformed defaultValue.", paramComp.Id);
                                continue;
                            }
                            if (dv is not (0d or 1d))
                            {
                                Report("PARAMETER_DEFAULT", "Non-binary ModularAvatarParameters defaultValue requires multi-state parameter evaluation.", paramComp.Id);
                                continue;
                            }
                            defVal = dv;
                        }
                        string effectiveName = internalParam
                            ? nameOrPrefix + "$$Internal$$" + paramComp.Id.ToString(CultureInfo.InvariantCulture) + "_" + pIndex.ToString(CultureInfo.InvariantCulture)
                            : (!string.IsNullOrWhiteSpace(remapTo) ? remapTo : nameOrPrefix);
                        if (currentMap.TryGetValue(effectiveName, out var outer))
                            effectiveName = outer.Mapped;
                        AvatarParameterType? pType = syncType switch
                        {
                            1 => AvatarParameterType.Int,
                            2 => AvatarParameterType.Float,
                            3 => AvatarParameterType.Bool,
                            _ => null
                        };
                        bool? saved = Get(pNode, "saved") != null ? savedVal : null;
                        bool? synced = syncType == 0 ? false : (Get(pNode, "localOnly") != null ? !localOnly : null);
                        currentMap[nameOrPrefix] = (effectiveName, internalParam, pType, defVal, saved, synced, paramComp.Id);
                        if (explicitDecls.TryGetValue(effectiveName, out var existingDecl))
                        {
                            var mergedType = pType ?? existingDecl.Type;
                            if (pType.HasValue && existingDecl.Type.HasValue && pType.Value != existingDecl.Type.Value)
                                Report("PARAMETER_CONFLICT", "Conflicting ModularAvatarParameters syncType declarations for '" + effectiveName + "'.", paramComp.Id);
                            var mergedDef = defVal ?? existingDecl.DefaultValue;
                            if (defVal.HasValue && existingDecl.DefaultValue.HasValue && defVal.Value != existingDecl.DefaultValue.Value)
                                Report("PARAMETER_CONFLICT", "Conflicting ModularAvatarParameters defaultValue declarations for '" + effectiveName + "'.", paramComp.Id);
                            var mergedSaved = saved ?? existingDecl.Saved;
                            if (saved.HasValue && existingDecl.Saved.HasValue && saved.Value != existingDecl.Saved.Value)
                                Report("PARAMETER_CONFLICT", "Conflicting ModularAvatarParameters saved declarations for '" + effectiveName + "'.", paramComp.Id);
                            var mergedSynced = synced ?? existingDecl.Synced;
                            if (synced.HasValue && existingDecl.Synced.HasValue && synced.Value != existingDecl.Synced.Value)
                                Report("PARAMETER_CONFLICT", "Conflicting ModularAvatarParameters localOnly/synced declarations for '" + effectiveName + "'.", paramComp.Id);
                            explicitDecls[effectiveName] = (mergedType, mergedDef, existingDecl.OverrideAnimatorDefaults || pbOnly, mergedSaved, mergedSynced, paramComp.Id);
                        }
                        else
                        {
                            explicitDecls[effectiveName] = (pType, defVal, pbOnly, saved, synced, paramComp.Id);
                        }
                        pIndex++;
                    }
                }
            }
            remapAtObject[objId] = currentMap;
        }

        string ResolveParameterName(long ownerObj, string rawName)
        {
            if (remapAtObject.TryGetValue(ownerObj, out var map) && map.TryGetValue(rawName, out var mapped))
                return mapped.Mapped;
            return rawName;
        }

        // 2. Reconstruct Modular Avatar menu tree (MenuInstaller, MenuInstallTarget, MenuItem, MenuGroup)
        var menusByOwner = menuItems.GroupBy(item => Reference(item.Body, "m_GameObject")).ToDictionary(group => group.Key, group => group.ToList());
        var installersByOwner = components.Where(item => Script(item) == AvatarPackageIndex.MenuInstallerScriptGuid)
            .GroupBy(item => Reference(item.Body, "m_GameObject")).ToDictionary(group => group.Key, group => group.ToList());
        var groupsByOwner = components.Where(item => Script(item) == MenuGroupGuid)
            .GroupBy(item => Reference(item.Body, "m_GameObject")).ToDictionary(group => group.Key, group => group.ToList());
        var targetsByOwner = components.Where(item => Script(item) == MenuInstallTargetGuid)
            .GroupBy(item => Reference(item.Body, "m_GameObject")).ToDictionary(group => group.Key, group => group.ToList());

        bool TryResolveOptionalOwnedObjectRef(YamlNode? refNode, long defaultObjId, bool requireExplicitTarget, out long resolvedObjId)
        {
            resolvedObjId = defaultObjId;
            if (refNode == null)
                return !requireExplicitTarget;
            if (refNode is not YamlMappingNode mapNode ||
                Get(mapNode, "fileID") is not YamlScalarNode idScalar ||
                !long.TryParse(idScalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long fileId) ||
                Text(mapNode, "guid").Trim('0').Length != 0)
            {
                return false;
            }
            if (fileId == 0)
                return !requireExplicitTarget;
            if (!scoped.Contains(fileId))
                return false;
            resolvedObjId = fileId;
            return true;
        }

        static bool IsNullAssetReference(YamlNode? refNode)
        {
            if (refNode == null) return true;
            return refNode is YamlMappingNode mapNode &&
                Get(mapNode, "fileID") is YamlScalarNode idScalar &&
                long.TryParse(idScalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long fileId) &&
                fileId == 0 &&
                Text(mapNode, "guid").Trim('0').Length == 0;
        }

        foreach (var installer in components.Where(item => Script(item) == AvatarPackageIndex.MenuInstallerScriptGuid))
        {
            if (!IsNullAssetReference(Get(installer.Body, "menuToAppend")))
                Report("MENU_ASSET", "ModularAvatarMenuInstaller menuToAppend external VRCExpressionsMenu assets are not supported in this slice.", installer.Id);
            if (!IsNullAssetReference(Get(installer.Body, "installTargetMenu")))
                Report("MENU_TARGET", "ModularAvatarMenuInstaller non-root installTargetMenu asset is not supported in this slice.", installer.Id);
        }
        foreach (var targetComp in components.Where(item => Script(item) == MenuInstallTargetGuid))
        {
            long owner = Reference(targetComp.Body, "m_GameObject");
            if (installersByOwner.ContainsKey(owner))
                Report("MENU_CYCLE", "ModularAvatarMenuInstallTarget on the same object as ModularAvatarMenuInstaller is unsupported.", targetComp.Id);
        }

        var resolvedMenuPaths = new Dictionary<long, string>();
        var visitedMenuNodes = new HashSet<long>();
        void TraverseMenuChildren(long parentObj, string prefix, long sourceCompId)
        {
            if (!transformByObject.TryGetValue(parentObj, out var transform)) return;
            foreach (var childNode in Sequence(transform.Body, "m_Children"))
            {
                long childTransform = ReferenceNode(childNode);
                if (!documents.TryGetValue(childTransform, out var childDoc)) continue;
                long childObj = Reference(childDoc.Body, "m_GameObject");
                if (installersByOwner.ContainsKey(childObj)) continue;
                ExpandMenuSource(childObj, prefix, false);
            }
        }
        void ExpandMenuSource(long objId, string prefix, bool fromInstaller)
        {
            if (menusByOwner.TryGetValue(objId, out var ownerMenus))
            {
                if (ownerMenus.Count > 1)
                {
                    Report("MENU_AMBIGUOUS", "Multiple ModularAvatarMenuItem components on the same GameObject are not supported.", ownerMenus[0].Id);
                    return;
                }
                var menu = ownerMenus[0];
                if (!visitedMenuNodes.Add(menu.Id))
                {
                    Report("MENU_CYCLE", "Menu item is installed multiple times or in a cycle.", menu.Id);
                    return;
                }
                string label = Text(menu.Body, "label");
                if (string.IsNullOrEmpty(label)) label = Text(gameObjects[objId].Body, "m_Name");
                if (string.IsNullOrWhiteSpace(label) || label.Contains('/', StringComparison.Ordinal) || label is "." or "..")
                {
                    Report("MENU_PATH", "Menu item label must be non-empty and cannot contain '/' or relative dot segments.", menu.Id);
                    return;
                }
                string fullPath = string.IsNullOrEmpty(prefix) ? label : prefix + "/" + label;
                var control = Get(menu.Body, "Control") as YamlMappingNode;
                string typeStr = Text(control, "type");
                if (typeStr == "103") // SubMenu
                {
                    var msNode = Get(menu.Body, "MenuSource");
                    if (msNode != null && (msNode is not YamlScalarNode msScalar || !int.TryParse(msScalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
                    {
                        Report("MENU_SUBMENU_SOURCE", "SubMenu has a malformed MenuSource value.", menu.Id);
                        return;
                    }
                    int subSource = msNode is YamlScalarNode msVal ? int.Parse(msVal.Value!, CultureInfo.InvariantCulture) : 0;
                    if (subSource == 0) // VRCExpressionsMenu asset
                    {
                        Report("MENU_SUBMENU_ASSET", "SubMenu referencing an external VRCExpressionsMenu asset is not supported in this slice.", menu.Id);
                        return;
                    }
                    if (subSource is not (1 or 2))
                    {
                        Report("MENU_SUBMENU_SOURCE", "SubMenu has an unsupported MenuSource value.", menu.Id);
                        return;
                    }
                    long container = objId;
                    if (subSource == 2 &&
                        !TryResolveOptionalOwnedObjectRef(Get(menu.Body, "menuSource_otherObjectChildren"), objId, requireExplicitTarget: true, out container))
                    {
                        Report("MENU_SUBMENU_TARGET", "menuSource_otherObjectChildren must reference an owned GameObject.", menu.Id);
                        return;
                    }
                    TraverseMenuChildren(container, fullPath, menu.Id);
                }
                else
                {
                    resolvedMenuPaths[menu.Id] = fullPath;
                }
            }
            else if (groupsByOwner.TryGetValue(objId, out var ownerGroups))
            {
                if (ownerGroups.Count > 1)
                {
                    Report("MENU_AMBIGUOUS", "Multiple ModularAvatarMenuGroup components on the same GameObject are not supported.", ownerGroups[0].Id);
                    return;
                }
                var group = ownerGroups[0];
                if (!visitedMenuNodes.Add(group.Id))
                {
                    Report("MENU_CYCLE", "Menu group is installed multiple times or in a cycle.", group.Id);
                    return;
                }
                if (!TryResolveOptionalOwnedObjectRef(Get(group.Body, "targetObject"), objId, requireExplicitTarget: false, out long container))
                {
                    Report("MENU_GROUP_TARGET", "MenuGroup targetObject must reference an owned GameObject.", group.Id);
                    return;
                }
                TraverseMenuChildren(container, prefix, group.Id);
            }
        }

        foreach (long objId in ordered)
        {
            if (installersByOwner.TryGetValue(objId, out var installers))
            {
                if (installers.Count > 1)
                    Report("MENU_AMBIGUOUS", "Multiple ModularAvatarMenuInstaller components on the same GameObject are not supported.", installers[0].Id);
                ExpandMenuSource(objId, "", true);
            }
        }
        foreach (var menu in menuItems)
        {
            var control = Get(menu.Body, "Control") as YamlMappingNode;
            if (Text(control, "type") == "103") continue;
            if (!resolvedMenuPaths.ContainsKey(menu.Id))
            {
                Report("MENU_UNINSTALLED", "ModularAvatarMenuItem has no ModularAvatarMenuInstaller installing it into the avatar root menu.", menu.Id);
            }
        }

        // 3. Parse Toggle MenuItems and resolve parameters
        var menuControlByMenuId = new Dictionary<long, (string Path, string ParameterName, double Value, bool IsDefault, string IconGuid, long OwnerObj)>();
        var usedPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var menu in menuItems)
        {
            if (!resolvedMenuPaths.TryGetValue(menu.Id, out string? path)) continue;
            long ownerObj = Reference(menu.Body, "m_GameObject");
            var control = Get(menu.Body, "Control") as YamlMappingNode;
            string typeStr = Text(control, "type");
            if (typeStr == "103") continue;
            if (typeStr != "2") // 2 = Toggle; 1 = Button (momentary press, not a persistent toggle)
            {
                Report("MENU_CONTROL_TYPE", "Only Toggle (2) and SubMenu (103) ModularAvatarMenuItem controls are supported in wardrobe compilation; momentary Button controls are not persistent toggles.", menu.Id);
                continue;
            }
            if (!usedPaths.Add(path) || usedPaths.Any(other => other != path &&
                (other.StartsWith(path + "/", StringComparison.Ordinal) || path.StartsWith(other + "/", StringComparison.Ordinal))))
            {
                Report("MENU_PATH_CONFLICT", "Menu item path '" + path + "' is duplicated or collides with a submenu.", menu.Id);
                continue;
            }
            string rawParam = Text(Get(control, "parameter") as YamlMappingNode, "name");
            string resolvedParam = string.IsNullOrEmpty(rawParam)
                ? "___MA_AnonMenu_" + menu.Id.ToString(CultureInfo.InvariantCulture)
                : ResolveParameterName(ownerObj, rawParam);
            if (!TryParseBoolField(menu.Body, "automaticValue", false, out bool autoVal))
            {
                Report("MENU_BOOL", "Malformed MenuItem automaticValue flag.", menu.Id);
                continue;
            }
            float val = 1f;
            if (!autoVal && Get(control, "value") != null)
            {
                if (!float.TryParse(Text(control, "value"), NumberStyles.Float, CultureInfo.InvariantCulture, out val) || !float.IsFinite(val))
                {
                    Report("MENU_VALUE", "Menu item control value must be finite.", menu.Id);
                    continue;
                }
            }
            if (val != 1f)
            {
                Report("MENU_VALUE_UNSUPPORTED", "Multi-state or non-unit menu control values require multi-state radio/parameter compilation.", menu.Id);
                continue;
            }
            if (!TryParseBoolField(menu.Body, "isDefault", false, out bool isDefault))
            {
                Report("MENU_DEFAULT", "Malformed MenuItem isDefault flag.", menu.Id);
                continue;
            }
            string iconGuid = "";
            if (Get(control, "icon") is YamlMappingNode iconNode && ReferenceNode(iconNode) != 0)
            {
                string ig = Text(iconNode, "guid");
                if (Regex.IsMatch(ig, @"\A[0-9a-fA-F]{32}\z"))
                    iconGuid = ig.ToLowerInvariant();
            }
            menuControlByMenuId[menu.Id] = (path, resolvedParam, val, isDefault, iconGuid, ownerObj);
        }

        // Check if two MenuItems share the same resolved parameter in this independent toggle slice
        foreach (var group in menuControlByMenuId.Values.GroupBy(item => item.ParameterName, StringComparer.Ordinal))
        {
            if (group.Count() > 1)
                Report("PARAMETER_SHARED_MENU", "Multiple ModularAvatarMenuItem controls write the same parameter '" + group.Key + "'; radio/shared-parameter menus are not supported by independent toggle bindings.");
        }

        // 4. Parse Reactive Components (ObjectToggle, ShapeChanger, MaterialSetter)
        long ResolveTarget(YamlMappingNode? reference, long source)
        {
            string path = Text(reference, "referencePath");
            long target = Reference(reference, "targetObject");
            if (Get(reference, "targetObject") is YamlNode targetReference &&
                (targetReference is not YamlMappingNode targetMapping ||
                    !long.TryParse(Text(targetMapping, "fileID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ||
                    Text(targetMapping, "guid").Trim('0').Length != 0))
            {
                Report("TARGET", "Malformed or external explicit object identity cannot become a path-only reference.", source);
                return 0;
            }
            if (path.Length == 0)
            {
                Report("TARGET", "Empty AvatarObjectReference is unresolved, even with targetObject.", source);
                return 0;
            }
            if (target != 0)
            {
                if (scoped.Contains(target)) return target;
                Report("TARGET", "Explicit target is missing, external or outside this avatar.", source);
                return 0;
            }
            if (path == "$$$AVATAR_ROOT$$$") return context.AvatarRootId;
            long cursor = context.AvatarRootId;
            foreach (string segment in path.Split('/'))
            {
                var matches = Sequence(transformByObject[cursor].Body, "m_Children")
                    .Select(child => Reference(documents[ReferenceNode(child)].Body, "m_GameObject"))
                    .Where(child => Text(gameObjects[child].Body, "m_Name") == segment).ToArray();
                if (matches.Length != 1)
                {
                    Report("TARGET", "AvatarObjectReference path is missing or ambiguous.", source);
                    return 0;
                }
                cursor = matches[0];
            }
            return cursor;
        }
        long ResolveRenderer(long target, long source)
        {
            var matches = context.Renderers.Where(item => item.Value.GameObjectId == target && documents.ContainsKey(item.Key)).Select(item => item.Key).ToArray();
            if (matches.Length == 1) return matches[0];
            Report("RENDERER", "Target GameObject requires one unambiguous captured Renderer snapshot.", source);
            return 0;
        }

        var bindingsByMenuId = menuControlByMenuId.Keys.ToDictionary(id => id, _ => new List<AvatarWardrobeBinding>());
        var destinations = new HashSet<(InitialAppearanceEffect, long, string)>();
        var controlHosts = menuItems.Concat(reactive).Select(item => Reference(item.Body, "m_GameObject")).Distinct().ToArray();

        foreach (var component in reactive)
        {
            token.ThrowIfCancellationRequested();
            string script = Script(component);
            long compOwner = Reference(component.Body, "m_GameObject");
            if (!TryParseBoolField(component.Body, "m_inverted", false, out bool inverted))
            {
                Report("INVERTED", "Reactive component has malformed m_inverted.", component.Id);
                continue;
            }

            // Walk ancestors from compOwner to AvatarRootId to find the controlling MenuItem and verify no other active-state dependencies exist
            long? controllingMenuId = null;
            for (long cursor = compOwner; cursor != 0 && cursor != context.AvatarRootId; cursor = parent.GetValueOrDefault(cursor))
            {
                if (Text(gameObjects[cursor].Body, "m_IsActive") != "1")
                {
                    Report("INACTIVE_ANCESTOR", "Reactive component has an inactive ancestor GameObject; static/conditional ancestor gating is not supported in independent toggle compilation.", component.Id);
                    break;
                }
                if (menusByOwner.TryGetValue(cursor, out var cursorMenus))
                {
                    if (cursorMenus.Count != 1)
                    {
                        Report("AMBIGUOUS_MENU", "Reactive component ancestor has multiple ModularAvatarMenuItem components.", component.Id);
                        break;
                    }
                    var candidateMenu = cursorMenus[0];
                    var cNode = Get(candidateMenu.Body, "Control") as YamlMappingNode;
                    if (Text(cNode, "type") == "103") continue;
                    if (controllingMenuId.HasValue)
                    {
                        Report("NESTED_MENU_CONJUNCTION", "Reactive component is guarded by multiple ancestor Toggle/Button MenuItems; conjunction rules are not supported by independent toggle bindings.", component.Id);
                        controllingMenuId = null;
                        break;
                    }
                    controllingMenuId = candidateMenu.Id;
                }
            }
            if (!controllingMenuId.HasValue || !menuControlByMenuId.ContainsKey(controllingMenuId.Value))
            {
                Report("UNCONTROLLED", "Reactive component has no unambiguous controlling ModularAvatarMenuItem toggle.", component.Id);
                continue;
            }

            var entries = Get(component.Body, script == ShapeChangerGuid ? "m_shapes" : "m_objects") ?? (script == ShapeChangerGuid ? Get(component.Body, "Shapes") : null);
            if (entries is not YamlSequenceNode sequence)
            {
                Report("ENTRIES", "Missing or invalid reactive entry list.", component.Id);
                continue;
            }
            foreach (var entry in sequence.Children)
            {
                if (entry is not YamlMappingNode mapping)
                {
                    Report("ENTRIES", "Malformed reactive entry.", component.Id);
                    continue;
                }
                if (script == ShapeChangerGuid && Text(mapping, "ChangeType") != "1")
                {
                    Report("SHAPE_CHANGE_TYPE", "Only reversible ShapeChanger Set mode is supported in runtime wardrobe controls.", component.Id);
                    continue;
                }
                long target = ResolveTarget(Get(mapping, "Object") as YamlMappingNode, component.Id);
                if (target == 0) continue;
                InitialAppearanceWrite offWrite;
                InitialAppearanceWrite onWrite;
                if (script == ObjectToggleGuid)
                {
                    if (target == context.AvatarRootId || controlHosts.Any(host => Within(host, target)))
                    {
                        Report("TARGET", "Visibility cannot affect the avatar root or menu/reactive component hosts.", component.Id);
                        continue;
                    }
                    string value = Text(mapping, "Active");
                    if (value is not ("0" or "1") || Text(gameObjects[target].Body, "m_IsActive") is not ("0" or "1"))
                    {
                        Report("VALUE", "ObjectToggle target or action has malformed boolean state.", component.Id);
                        continue;
                    }
                    bool baselineActive = Text(gameObjects[target].Body, "m_IsActive") == "1";
                    bool activeWhenTriggered = value == "1";
                    offWrite = new(InitialAppearanceEffect.Active, target, "m_IsActive", Active: inverted ? activeWhenTriggered : baselineActive);
                    onWrite = new(InitialAppearanceEffect.Active, target, "m_IsActive", Active: inverted ? baselineActive : activeWhenTriggered);
                }
                else if (script == ShapeChangerGuid)
                {
                    long renderer = ResolveRenderer(target, component.Id);
                    if (renderer == 0) continue;
                    var snapshot = context.Renderers[renderer];
                    string shape = Text(mapping, "ShapeName");
                    if (documents[renderer].ClassId != 137 || !snapshot.Shapes.TryGetValue(shape, out float baseline) || !float.IsFinite(baseline) ||
                        !float.TryParse(Text(mapping, "Value"), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value))
                    {
                        Report("SHAPE", "Missing skinned blendshape baseline or invalid Set weight.", component.Id);
                        continue;
                    }
                    offWrite = new(InitialAppearanceEffect.Shape, renderer, shape, Weight: inverted ? value : baseline);
                    onWrite = new(InitialAppearanceEffect.Shape, renderer, shape, Weight: inverted ? baseline : value);
                }
                else
                {
                    long renderer = ResolveRenderer(target, component.Id);
                    if (renderer == 0) continue;
                    var snapshot = context.Renderers[renderer];
                    var material = Get(mapping, "Material") as YamlMappingNode;
                    string guid = Text(material, "guid");
                    long materialId = ReferenceNode(material);
                    if (!int.TryParse(Text(mapping, "MaterialIndex"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) || index < 0 || index >= snapshot.Materials.Count ||
                        material == null || materialId == 0 || guid.Length != 32 || !guid.All(Uri.IsHexDigit))
                    {
                        Report("MATERIAL", "Invalid material slot or unresolved external material identity.", component.Id);
                        continue;
                    }
                    var baselineMat = snapshot.Materials[index];
                    if (baselineMat == null || baselineMat.FileId == 0 || baselineMat.Guid.Length != 32 || !baselineMat.Guid.All(Uri.IsHexDigit))
                    {
                        Report("MATERIAL_BASELINE", "MaterialSetter requires a valid baseline material GUID/fileID to restore the off state.", component.Id);
                        continue;
                    }
                    var baselineRef = new InitialAppearanceMaterial(baselineMat.FileId, baselineMat.Guid.ToLowerInvariant());
                    var targetRef = new InitialAppearanceMaterial(materialId, guid.ToLowerInvariant());
                    string slot = index.ToString(CultureInfo.InvariantCulture);
                    offWrite = new(InitialAppearanceEffect.Material, renderer, slot, Material: inverted ? targetRef : baselineRef);
                    onWrite = new(InitialAppearanceEffect.Material, renderer, slot, Material: inverted ? baselineRef : targetRef);
                }
                if (!destinations.Add((onWrite.Effect, onWrite.TargetId, onWrite.Property)))
                    Report("CONFLICT", "Multiple Modular Avatar reactive entries write the same terminal property.", component.Id);
                bindingsByMenuId[controllingMenuId.Value].Add(new(offWrite, onWrite));
            }
        }

        foreach (var pair in menuControlByMenuId)
        {
            long menuId = pair.Key;
            var info = pair.Value;
            var bindings = bindingsByMenuId[menuId];
            if (bindings.Count == 0)
            {
                Report("EMPTY_CONTROL", "ModularAvatarMenuItem '" + info.Path + "' has no supported reactive bindings.", menuId);
                continue;
            }
            bool defaultOn = info.IsDefault;
            bool saved = false;
            bool synced = true;
            AvatarParameterType paramType = AvatarParameterType.Bool;
            if (explicitDecls.TryGetValue(info.ParameterName, out var decl))
            {
                if (decl.Type.HasValue) paramType = decl.Type.Value;
                if (decl.DefaultValue.HasValue)
                {
                    bool paramDefault = decl.DefaultValue.Value != 0;
                    defaultOn = decl.OverrideAnimatorDefaults ? paramDefault : (info.IsDefault || paramDefault);
                }
                if (decl.Saved.HasValue) saved = decl.Saved.Value;
                if (decl.Synced.HasValue) synced = decl.Synced.Value;
                if (decl.Saved == true)
                    Report("OPTION", "Toggle '" + info.Path + "' uses uncompiled option saved.", decl.SourceId);
                if (decl.Synced == false)
                    Report("OPTION", "Toggle '" + info.Path + "' uses uncompiled option localOnly.", decl.SourceId);
            }
            string controlId = "ma:" + menuId.ToString(CultureInfo.InvariantCulture);
            controls.Add(new(controlId, info.Path, defaultOn, bindings.AsReadOnly(), info.IconGuid, saved, synced, paramType));
        }

        if (diagnostics.Any(item => item.Severity != InitialAppearanceSeverity.Info) || controls.Count == 0)
            return Result();

        var shared = AvatarWardrobeGraph.FromIndependentControls(avatarId, controls, token);
        return Result(shared);
    }
}
