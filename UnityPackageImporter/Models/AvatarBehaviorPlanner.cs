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

internal static class AvatarBehaviorPlanner
{
    private static readonly (string Name, int Channel)[] VisemeChannels = new[]
    {
        ("state_sil", 0),
        ("state_PP", 1),
        ("state_FF", 2),
        ("state_TH", 3),
        ("state_DD", 4),
        ("state_kk", 5),
        ("state_CH", 6),
        ("state_SS", 7),
        ("state_nn", 8),
        ("state_RR", 9),
        ("state_aa", 10),
        ("state_E", 11),
        ("state_I", 12),
        ("state_O", 13),
        ("state_U", 14),
    };

    internal static AvatarBehaviorPlan Compile(
        string yaml,
        long avatarRootId,
        Func<string, long, UnityStaticClipResult?>? clipResolver = null,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var diagnostics = new List<InitialAppearanceDiagnostic>();
        void Report(string code, string message, long source = 0, InitialAppearanceSeverity severity = InitialAppearanceSeverity.Warning) =>
            diagnostics.Add(new(severity, "BEHAVIOR_" + code, message, source));

        if (string.IsNullOrWhiteSpace(yaml))
            return new AvatarBehaviorPlan { Diagnostics = diagnostics.AsReadOnly() };

        var stream = new YamlStream();
        try
        {
            string normalized = Regex.Replace(AvatarPackageIndex.NormalizeDocumentHeaders(yaml), @"(?m)^(---[ \t]+)!u!(\d+)", "$1!<tag:unity3d.com,2011:$2>");
            stream.Load(new StringReader(normalized));
        }
        catch (Exception ex) when (ex is YamlException or InvalidDataException)
        {
            Report("YAML_PARSE", "Failed to parse YAML for avatar behavior compilation: " + ex.Message, 0, InitialAppearanceSeverity.Error);
            return new AvatarBehaviorPlan { Diagnostics = diagnostics.AsReadOnly(), HasUnsupportedContext = true };
        }

        token.ThrowIfCancellationRequested();


        var gameObjectToTransform = new Dictionary<long, long>();
        var transformToGameObject = new Dictionary<long, long>();
        var transformParents = new Dictionary<long, long>();
        var skinnedMeshRenderers = new Dictionary<long, (long GameObjectId, HashSet<string> Shapes)>();
        var monoBehaviours = new List<(long FileId, YamlMappingNode Body)>();

        foreach (var doc in stream.Documents)
        {
            token.ThrowIfCancellationRequested();
            if (doc.RootNode is not YamlMappingNode rootNode || rootNode.Children.Count != 1) continue;

            var tag = rootNode.Children.First().Key as YamlScalarNode;
            var body = rootNode.Children.First().Value as YamlMappingNode;
            if (tag == null || body == null) continue;

            string tagStr = tag.Value ?? "";
            long fileId = ParseHeaderId(rootNode);

            if (tagStr.EndsWith("!1") || tagStr == "GameObject")
            {

            }
            else if (tagStr.EndsWith("!4") || tagStr == "Transform" || tagStr.EndsWith("!224") || tagStr == "RectTransform")
            {
                long goRef = ParseFileId(Get(body, "m_GameObject"));
                long fatherRef = ParseFileId(Get(body, "m_Father"));
                if (fileId != 0 && goRef != 0)
                {
                    gameObjectToTransform[goRef] = fileId;
                    transformToGameObject[fileId] = goRef;
                    transformParents[fileId] = fatherRef;
                }
            }
            else if (tagStr.EndsWith("!137") || tagStr == "SkinnedMeshRenderer" || tagStr.EndsWith("!23") || tagStr == "MeshRenderer")
            {
                long goRef = ParseFileId(Get(body, "m_GameObject"));
                var shapes = new HashSet<string>(StringComparer.Ordinal);
                skinnedMeshRenderers[fileId] = (goRef, shapes);
            }
            else if (tagStr.EndsWith("!114") || tagStr == "MonoBehaviour")
            {
                if (fileId != 0) monoBehaviours.Add((fileId, body));
            }
        }


        bool IsDescendant(long candidateTransform, long targetAncestorTransform)
        {
            var seen = new HashSet<long>();
            long cur = candidateTransform;
            while (cur != 0 && seen.Add(cur))
            {
                if (cur == targetAncestorTransform) return true;
                cur = transformParents.GetValueOrDefault(cur);
            }
            return false;
        }

        long avatarRootTransform = gameObjectToTransform.GetValueOrDefault(avatarRootId);
        bool IsOwnedGameObject(long goId) =>
            goId != 0 && avatarRootTransform != 0 &&
            gameObjectToTransform.TryGetValue(goId, out var t) &&
            IsDescendant(t, avatarRootTransform);

        var breathingBindings = new List<AvatarBreathingBinding>();
        var blinkingBindings = new List<AvatarBlinkingBinding>();
        var talkingBindings = new List<AvatarTalkingBinding>();
        var visemeBindings = new List<AvatarFuryVisemeBinding>();
        var meshSettingsBindings = new List<AvatarMeshSettingsBinding>();
        var visibleInFirstPersonObjectIds = new List<long>();
        var boneConstraintBindings = new List<AvatarBoneConstraintBinding>();
        var worldConstraintBindings = new List<AvatarWorldConstraintBinding>();
        var moveToBindings = new List<AvatarMoveToBinding>();
        var worldFixedBindings = new List<AvatarWorldFixedBinding>();
        var worldScaleBindings = new List<AvatarWorldScaleBinding>();
        float? floorAdjustmentY = null;
        long floorAdjusterSourceId = 0;
        bool blinkSuppressed = false;
        bool hasBlockBlinking = false;
        bool hasBlockVisemes = false;


        foreach (var (mbFileId, body) in monoBehaviours)
        {
            token.ThrowIfCancellationRequested();
            var script = Get(body, "m_Script") as YamlMappingNode;
            string guid = Text(script, "guid");
            if (!AvatarPackageIndex.RecognizedVrcFuryScriptGuids.Contains(guid) &&
                !AvatarPackageIndex.HasVrcFuryStructuralSignature(body.ToString().Split('\n')))
                continue;

            long ownerGo = ParseFileId(Get(body, "m_GameObject"));
            if (!IsOwnedGameObject(ownerGo)) continue;

            var graph = VrcFuryManagedReferenceGraph.Read(body, token);
            if (!graph.IsValid)
            {
                foreach (var diag in graph.Diagnostics)
                    Report("GRAPH_ERROR", diag.Message, mbFileId, InitialAppearanceSeverity.Warning);
                continue;
            }


            foreach (var record in graph.Records.Values)
            {
                var type = Get(record, "type") as YamlMappingNode;
                string actionClass = Text(type, "class");
                if (actionClass == "BlockBlinkingAction") hasBlockBlinking = true;
                if (actionClass == "BlockVisemesAction") hasBlockVisemes = true;
            }

            foreach (long rootRid in graph.RootIds)
            {
                if (!graph.Records.TryGetValue(rootRid, out var entry)) continue;
                var typeNode = Get(entry, "type") as YamlMappingNode;
                var dataNode = Get(entry, "data") as YamlMappingNode ?? entry;
                string className = Text(typeNode, "class");
                if (className == "Blinking")
                {
                    CompileBlinking(mbFileId, dataNode, graph, clipResolver, ownerGo, IsOwnedGameObject, skinnedMeshRenderers, blinkingBindings, Report);
                }
                else if (className == "Breathing")
                {
                    CompileBreathing(mbFileId, dataNode, graph, ownerGo, IsOwnedGameObject, skinnedMeshRenderers, breathingBindings, Report);
                }
                else if (className == "Talking")
                {
                    CompileTalking(mbFileId, dataNode, graph, clipResolver, ownerGo, IsOwnedGameObject, skinnedMeshRenderers, talkingBindings, Report);
                }
                else if (className == "Visemes")
                {
                    CompileVisemes(mbFileId, dataNode, graph, clipResolver, ownerGo, IsOwnedGameObject, skinnedMeshRenderers, visemeBindings, Report);
                }
                else if (className == "RemoveBlinking")
                {
                    blinkSuppressed = true;
                }
                else if (className == "ShowInFirstPerson")
                {
                    CompileShowInFirstPerson(mbFileId, dataNode, ownerGo, IsOwnedGameObject, visibleInFirstPersonObjectIds);
                }
                else if (className is "AnchorOverrideFix" or "AnchorOverrideFix2")
                {
                    CompileAnchorOverrideFix(mbFileId, dataNode, ownerGo, gameObjectToTransform, transformParents, skinnedMeshRenderers, meshSettingsBindings);
                }
                else if (className is "BoundingBoxFix" or "BoundingBoxFix2")
                {
                    CompileBoundingBoxFix(mbFileId, dataNode, ownerGo, gameObjectToTransform, transformParents, skinnedMeshRenderers, meshSettingsBindings);
                }
                else if (className == "BoneConstraint")
                {
                    CompileBoneConstraint(mbFileId, dataNode, ownerGo, IsOwnedGameObject, boneConstraintBindings, Report);
                }
                else if (className == "WorldConstraint")
                {
                    CompileWorldConstraint(mbFileId, dataNode, ownerGo, IsOwnedGameObject, worldConstraintBindings, Report);
                }
                else if (className == "HeadChopHead")
                {
                    CompileHeadChopHead(mbFileId, dataNode, ownerGo, IsOwnedGameObject, visibleInFirstPersonObjectIds);
                }
            }

            if (guid.Equals(AvatarPackageIndex.VrcFuryShowInFirstPersonScriptGuid, StringComparison.OrdinalIgnoreCase))
            {
                if (!visibleInFirstPersonObjectIds.Contains(ownerGo))
                    visibleInFirstPersonObjectIds.Add(ownerGo);
            }
        }


        foreach (var (mbFileId, body) in monoBehaviours)
        {
            token.ThrowIfCancellationRequested();
            var script = Get(body, "m_Script") as YamlMappingNode;
            string guid = Text(script, "guid");
            long ownerGo = ParseFileId(Get(body, "m_GameObject"));
            if (!IsOwnedGameObject(ownerGo)) continue;

            string idClass = Text(body, "m_EditorClassIdentifier") + " " + Text(body, "m_Name");
            if (guid.Equals(AvatarPackageIndex.MeshSettingsScriptGuid, StringComparison.OrdinalIgnoreCase))
            {
                CompileMeshSettings(mbFileId, body, ownerGo, gameObjectToTransform, transformParents, skinnedMeshRenderers, meshSettingsBindings);
            }
            else if (guid.Equals(AvatarPackageIndex.FloorAdjusterScriptGuid, StringComparison.OrdinalIgnoreCase))
            {
                CompileFloorAdjuster(mbFileId, body, ownerGo, ref floorAdjustmentY, ref floorAdjusterSourceId, Report);
            }
            else if (guid.Equals(AvatarPackageIndex.VisibleHeadAccessoryScriptGuid, StringComparison.OrdinalIgnoreCase))
            {
                if (!visibleInFirstPersonObjectIds.Contains(ownerGo))
                    visibleInFirstPersonObjectIds.Add(ownerGo);
            }
            else if (guid.Equals(AvatarPackageIndex.MoveToScriptGuid, StringComparison.OrdinalIgnoreCase) ||
                     idClass.Contains("MoveTo", StringComparison.OrdinalIgnoreCase))
            {
                CompileMoveTo(mbFileId, body, ownerGo, IsOwnedGameObject, moveToBindings, Report);
            }
            else if (guid.Equals(AvatarPackageIndex.WorldFixedObjectScriptGuid, StringComparison.OrdinalIgnoreCase) ||
                     idClass.Contains("WorldFixed", StringComparison.OrdinalIgnoreCase))
            {
                worldFixedBindings.Add(new(mbFileId, ownerGo));
            }
            else if (guid.Equals(AvatarPackageIndex.WorldScaleObjectScriptGuid, StringComparison.OrdinalIgnoreCase) ||
                     idClass.Contains("WorldScale", StringComparison.OrdinalIgnoreCase))
            {
                worldScaleBindings.Add(new(mbFileId, ownerGo));
            }
        }


        foreach (var (mbFileId, body) in monoBehaviours)
        {
            token.ThrowIfCancellationRequested();
            long ownerGo = ParseFileId(Get(body, "m_GameObject"));
            if (!IsOwnedGameObject(ownerGo)) continue;

            string idClass = Text(body, "m_EditorClassIdentifier") + " " + Text(body, "m_Name");
            if (idClass.Contains("VRCHeadChop", StringComparison.OrdinalIgnoreCase) ||
                idClass.Contains("HeadChop", StringComparison.OrdinalIgnoreCase))
            {
                CompileVrcHeadChop(mbFileId, body, ownerGo, IsOwnedGameObject, visibleInFirstPersonObjectIds);
            }
        }

        return new AvatarBehaviorPlan
        {
            BreathingBindings = breathingBindings.AsReadOnly(),
            BlinkingBindings = blinkingBindings.AsReadOnly(),
            TalkingBindings = talkingBindings.AsReadOnly(),
            VisemeBindings = visemeBindings.AsReadOnly(),
            MeshSettingsBindings = meshSettingsBindings.AsReadOnly(),
            VisibleInFirstPersonObjectIds = visibleInFirstPersonObjectIds.AsReadOnly(),
            BoneConstraintBindings = boneConstraintBindings.AsReadOnly(),
            WorldConstraintBindings = worldConstraintBindings.AsReadOnly(),
            MoveToBindings = moveToBindings.AsReadOnly(),
            WorldFixedBindings = worldFixedBindings.AsReadOnly(),
            WorldScaleBindings = worldScaleBindings.AsReadOnly(),
            FloorAdjustmentY = floorAdjustmentY,
            FloorAdjusterSourceId = floorAdjusterSourceId,
            BlinkSuppressed = blinkSuppressed,
            HasBlockBlinking = hasBlockBlinking,
            HasBlockVisemes = hasBlockVisemes,
            Diagnostics = diagnostics.AsReadOnly(),
            HasUnsupportedContext = diagnostics.Any(d => d.Severity == InitialAppearanceSeverity.Error)
        };
    }

    private static void CompileBlinking(
        long sourceId,
        YamlMappingNode data,
        VrcFuryManagedReferenceGraph graph,
        Func<string, long, UnityStaticClipResult?>? clipResolver,
        long ownerGo,
        Func<long, bool> isOwnedGo,
        Dictionary<long, (long GameObjectId, HashSet<string> Shapes)> renderers,
        List<AvatarBlinkingBinding> bindings,
        Action<string, string, long, InitialAppearanceSeverity> report)
    {
        var actions = ResolveActions(Get(data, "state"), graph);
        foreach (var (actionType, actionData) in actions)
        {
            string actionClass = Text(actionType, "class");
            if (actionClass == "BlendShapeAction")
            {
                string shape = Text(actionData, "blendShape");
                if (string.IsNullOrWhiteSpace(shape)) continue;

                float weight = 100f;
                if (Get(actionData, "blendShapeValue") is YamlScalarNode wScalar &&
                    float.TryParse(wScalar.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedW))
                {
                    weight = parsedW;
                }

                long rendererRef = ParseFileId(Get(actionData, "renderer"));
                bindings.Add(new AvatarBlinkingBinding(sourceId, rendererRef, shape, weight));
            }
            else if (actionClass == "AnimationClipAction" && clipResolver != null)
            {
                var clipRef = Get(actionData, "clip") as YamlMappingNode;
                string clipGuid = Text(clipRef, "guid");
                long clipFileId = ParseFileId(clipRef);
                var clip = clipResolver(clipGuid, clipFileId);
                if (clip != null && clip.CanApply)
                {
                    foreach (var curve in clip.Curves)
                    {
                        if (curve.ClassId == 137 && curve.Attribute.StartsWith("blendShape.", StringComparison.Ordinal))
                        {
                            string shape = curve.Attribute.Substring("blendShape.".Length);
                            bindings.Add(new AvatarBlinkingBinding(sourceId, 0, shape, curve.Value));
                        }
                    }
                }
            }
        }
    }

    private static void CompileTalking(
        long sourceId,
        YamlMappingNode data,
        VrcFuryManagedReferenceGraph graph,
        Func<string, long, UnityStaticClipResult?>? clipResolver,
        long ownerGo,
        Func<long, bool> isOwnedGo,
        Dictionary<long, (long GameObjectId, HashSet<string> Shapes)> renderers,
        List<AvatarTalkingBinding> bindings,
        Action<string, string, long, InitialAppearanceSeverity> report)
    {
        var actions = ResolveActions(Get(data, "state"), graph);
        foreach (var (actionType, actionData) in actions)
        {
            string actionClass = Text(actionType, "class");
            if (actionClass == "BlendShapeAction")
            {
                string shape = Text(actionData, "blendShape");
                if (string.IsNullOrWhiteSpace(shape)) continue;

                float weight = 100f;
                if (Get(actionData, "blendShapeValue") is YamlScalarNode wScalar &&
                    float.TryParse(wScalar.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedW))
                {
                    weight = parsedW;
                }

                long rendererRef = ParseFileId(Get(actionData, "renderer"));
                bindings.Add(new AvatarTalkingBinding(sourceId, rendererRef, shape, weight));
            }
            else if (actionClass == "ObjectToggleAction")
            {
                long objRef = ParseFileId(Get(actionData, "obj"));
                if (objRef == 0) continue;
                if (!isOwnedGo(objRef))
                {
                    report("TARGET_UNOWNED", "Talking target object is outside avatar root.", sourceId, InitialAppearanceSeverity.Warning);
                    continue;
                }
                string modeStr = Text(actionData, "mode");
                bool turnOn = modeStr != "TurnOff";
                bindings.Add(new AvatarTalkingBinding(sourceId, 0, "", 0f, objRef, turnOn));
            }
            else if (actionClass == "AnimationClipAction" && clipResolver != null)
            {
                var clipRef = Get(actionData, "clip") as YamlMappingNode;
                string clipGuid = Text(clipRef, "guid");
                long clipFileId = ParseFileId(clipRef);
                var clip = clipResolver(clipGuid, clipFileId);
                if (clip != null && clip.CanApply)
                {
                    foreach (var curve in clip.Curves)
                    {
                        if (curve.ClassId == 137 && curve.Attribute.StartsWith("blendShape.", StringComparison.Ordinal))
                        {
                            string shape = curve.Attribute.Substring("blendShape.".Length);
                            bindings.Add(new AvatarTalkingBinding(sourceId, 0, shape, curve.Value));
                        }
                    }
                }
            }
        }
    }

    private static void CompileVisemes(
        long sourceId,
        YamlMappingNode data,
        VrcFuryManagedReferenceGraph graph,
        Func<string, long, UnityStaticClipResult?>? clipResolver,
        long ownerGo,
        Func<long, bool> isOwnedGo,
        Dictionary<long, (long GameObjectId, HashSet<string> Shapes)> renderers,
        List<AvatarFuryVisemeBinding> bindings,
        Action<string, string, long, InitialAppearanceSeverity> report)
    {
        foreach (var (stateName, channel) in VisemeChannels)
        {
            var actions = ResolveActions(Get(data, stateName), graph);
            foreach (var (actionType, actionData) in actions)
            {
                string actionClass = Text(actionType, "class");
                if (actionClass == "BlendShapeAction")
                {
                    string shape = Text(actionData, "blendShape");
                    if (string.IsNullOrWhiteSpace(shape)) continue;
                    long rendererRef = ParseFileId(Get(actionData, "renderer"));
                    bindings.Add(new AvatarFuryVisemeBinding(channel, rendererRef, shape));
                }
                else if (actionClass == "AnimationClipAction" && clipResolver != null)
                {
                    var clipRef = Get(actionData, "clip") as YamlMappingNode;
                    string clipGuid = Text(clipRef, "guid");
                    long clipFileId = ParseFileId(clipRef);
                    var clip = clipResolver(clipGuid, clipFileId);
                    if (clip != null && clip.CanApply)
                    {
                        foreach (var curve in clip.Curves)
                        {
                            if (curve.ClassId == 137 && curve.Attribute.StartsWith("blendShape.", StringComparison.Ordinal))
                            {
                                string shape = curve.Attribute.Substring("blendShape.".Length);
                                bindings.Add(new AvatarFuryVisemeBinding(channel, 0, shape));
                            }
                        }
                    }
                }
            }
        }
    }

    private static void CompileBreathing(
        long sourceId,
        YamlMappingNode data,
        VrcFuryManagedReferenceGraph graph,
        long ownerGo,
        Func<long, bool> isOwnedGo,
        Dictionary<long, (long GameObjectId, HashSet<string> Shapes)> renderers,
        List<AvatarBreathingBinding> bindings,
        Action<string, string, long, InitialAppearanceSeverity> report)
    {
        long targetObj = ParseFileId(Get(data, "obj"));
        if (targetObj != 0)
        {
            if (!isOwnedGo(targetObj))
            {
                report("TARGET_UNOWNED", "Breathing target object is outside avatar root.", sourceId, InitialAppearanceSeverity.Warning);
                return;
            }

            bindings.Add(new AvatarBreathingBinding(
                FeatureSourceId: sourceId,
                Mode: AvatarBreathingTargetMode.BoneScale,
                TargetRendererFileId: 0,
                TargetBlendShape: "",
                MinWeight: 1f,
                MaxWeight: 1.05f,
                TargetObjectFileId: targetObj
            ));
        }
        else
        {
            string directShape = Text(data, "blendshape");
            if (!string.IsNullOrWhiteSpace(directShape))
            {
                bindings.Add(new AvatarBreathingBinding(
                    FeatureSourceId: sourceId,
                    Mode: AvatarBreathingTargetMode.BlendShape,
                    TargetRendererFileId: 0,
                    TargetBlendShape: directShape,
                    MinWeight: 0f,
                    MaxWeight: 100f,
                    TargetObjectFileId: ownerGo
                ));
            }
            else
            {
                var actions = ResolveActions(Get(data, "state") ?? Get(data, "inState"), graph);
                foreach (var (actionType, actionData) in actions)
                {
                    if (Text(actionType, "class") == "BlendShapeAction")
                    {
                        string shape = Text(actionData, "blendShape");
                        if (!string.IsNullOrWhiteSpace(shape))
                        {
                            long rendRef = ParseFileId(Get(actionData, "renderer"));
                            bindings.Add(new AvatarBreathingBinding(
                                FeatureSourceId: sourceId,
                                Mode: AvatarBreathingTargetMode.BlendShape,
                                TargetRendererFileId: rendRef,
                                TargetBlendShape: shape,
                                MinWeight: 0f,
                                MaxWeight: 100f,
                                TargetObjectFileId: ownerGo
                            ));
                        }
                    }
                }
            }
        }
    }

    private static List<(YamlMappingNode Type, YamlMappingNode Data)> ResolveActions(
        YamlNode? stateNode,
        VrcFuryManagedReferenceGraph graph)
    {
        var result = new List<(YamlMappingNode Type, YamlMappingNode Data)>();
        if (stateNode == null) return result;

        YamlMappingNode? stateMapping = null;
        if (stateNode is YamlMappingNode m && m.Children.ContainsKey(new YamlScalarNode("rid")))
        {
            long rid = ParseFileId(m);
            if (graph.Records.TryGetValue(rid, out var record))
                stateMapping = Get(record, "data") as YamlMappingNode ?? record;
        }
        else if (stateNode is YamlMappingNode directMapping)
        {
            stateMapping = directMapping;
        }

        if (stateMapping != null && Get(stateMapping, "actions") is YamlSequenceNode actionsSeq)
        {
            foreach (var actionNode in actionsSeq.Children)
            {
                if (actionNode is YamlMappingNode actionMap && actionMap.Children.ContainsKey(new YamlScalarNode("rid")))
                {
                    long rid = ParseFileId(actionMap);
                    if (graph.Records.TryGetValue(rid, out var record))
                    {
                        var actionType = Get(record, "type") as YamlMappingNode;
                        var actionData = Get(record, "data") as YamlMappingNode ?? record;
                        if (actionType != null)
                            result.Add((actionType, actionData));
                    }
                }
            }
        }

        return result;
    }

    private static YamlNode? Get(YamlMappingNode? node, string key) =>
        node != null && node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    private static string Text(YamlMappingNode? node, string key) =>
        Get(node, key) is YamlScalarNode scalar ? scalar.Value ?? "" : "";

    private static long ParseHeaderId(YamlMappingNode rootNode)
    {
        if (!string.IsNullOrEmpty(rootNode.Anchor.Value) &&
            long.TryParse(rootNode.Anchor.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
            return id;
        return 0;
    }


    private static void CompileMoveTo(
        long sourceId,
        YamlMappingNode body,
        long ownerGo,
        Func<long, bool> isOwnedGo,
        List<AvatarMoveToBinding> bindings,
        Action<string, string, long, InitialAppearanceSeverity> report)
    {
        long targetObj = ParseFileId(Get(body, "target") ?? Get(body, "targetObject") ?? Get(body, "m_Target"));
        int bone = -1;
        if (Get(body, "bone") is YamlScalarNode boneScalar &&
            int.TryParse(boneScalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedBone))
        {
            bone = parsedBone;
        }

        bool preserveWorld = true;
        if (Get(body, "preserveWorldTransform") is YamlScalarNode pwScalar &&
            int.TryParse(pwScalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pwInt))
        {
            preserveWorld = pwInt != 0;
        }

        if (targetObj == 0 && bone < 0)
        {
            report("MA_MOVETO_NOTARGET", "ModularAvatarMoveTo has neither a target GameObject nor humanoid bone specified.", sourceId, InitialAppearanceSeverity.Warning);
            return;
        }

        if (targetObj == ownerGo)
        {
            report("MA_MOVETO_SELF", "ModularAvatarMoveTo cannot target itself.", sourceId, InitialAppearanceSeverity.Error);
            return;
        }

        bindings.Add(new(sourceId, ownerGo, targetObj, bone, preserveWorld));
    }

    private static long ParseFileId(YamlNode? node)
    {
        if (node is YamlMappingNode map)
        {
            if (map.Children.TryGetValue(new YamlScalarNode("fileID"), out var fileIdVal) &&
                fileIdVal is YamlScalarNode fScalar &&
                long.TryParse(fScalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
            {
                return id;
            }
            if (map.Children.TryGetValue(new YamlScalarNode("rid"), out var ridVal) &&
                ridVal is YamlScalarNode rScalar &&
                long.TryParse(rScalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long rid))
            {
                return rid;
            }
        }
        return 0;
    }

    private static void CompileShowInFirstPerson(
        long sourceId,
        YamlMappingNode data,
        long ownerGo,
        Func<long, bool> isOwnedGo,
        List<long> visibleObjectIds)
    {
        long targetGo = ParseFileId(Get(data, "target") ?? Get(data, "targetObject") ?? Get(data, "gameObject"));
        long target = (targetGo != 0 && isOwnedGo(targetGo)) ? targetGo : ownerGo;
        if (target != 0 && !visibleObjectIds.Contains(target))
        {
            visibleObjectIds.Add(target);
        }
    }

    private static void CompileMeshSettings(
        long sourceId,
        YamlMappingNode body,
        long ownerGo,
        Dictionary<long, long> gameObjectToTransform,
        Dictionary<long, long> transformParents,
        Dictionary<long, (long GameObjectId, HashSet<string> Shapes)> renderers,
        List<AvatarMeshSettingsBinding> meshSettingsBindings)
    {
        long probeAnchorTargetGo = ParseFileId(Get(body, "probeAnchor") ?? Get(body, "m_ProbeAnchor"));
        bool inheritProbeAnchor = ParseBool(Get(body, "inheritProbeAnchor"), true);
        bool setProbeAnchor = probeAnchorTargetGo != 0 || inheritProbeAnchor;

        var boundsNode = Get(body, "bounds") as YamlMappingNode ?? Get(body, "m_Bounds") as YamlMappingNode;
        var centerPos = ParsePosition(Get(boundsNode, "m_Center") ?? Get(boundsNode, "center")) ?? new UnityPosition(0, 0, 0);
        var sizePos = ParsePosition(Get(boundsNode, "m_Extent") ?? Get(boundsNode, "extent") ?? Get(boundsNode, "m_Size") ?? Get(boundsNode, "size")) ?? new UnityPosition(1, 1, 1);
        bool inheritBounds = ParseBool(Get(body, "inheritBounds"), false);
        bool setBounds = boundsNode != null || inheritBounds;

        long ownerTransform = gameObjectToTransform.GetValueOrDefault(ownerGo);
        if (ownerTransform == 0) return;

        bool IsUnderOwner(long rendererGo)
        {
            long t = gameObjectToTransform.GetValueOrDefault(rendererGo);
            var seen = new HashSet<long>();
            while (t != 0 && seen.Add(t))
            {
                if (t == ownerTransform) return true;
                t = transformParents.GetValueOrDefault(t);
            }
            return false;
        }

        foreach (var (rendererFileId, (rendererGo, _)) in renderers)
        {
            if (IsUnderOwner(rendererGo))
            {
                meshSettingsBindings.Add(new AvatarMeshSettingsBinding(
                    sourceId,
                    rendererFileId,
                    probeAnchorTargetGo,
                    centerPos,
                    sizePos,
                    setBounds,
                    setProbeAnchor
                ));
            }
        }
    }

    private static void CompileFloorAdjuster(
        long sourceId,
        YamlMappingNode body,
        long ownerGo,
        ref float? floorAdjustmentY,
        ref long floorAdjusterSourceId,
        Action<string, string, long, InitialAppearanceSeverity> report)
    {
        float parsedAdjustment = 0f;
        var adjNode = Get(body, "adjustment") ?? Get(body, "m_Adjustment") ??
                      Get(body, "offset") ?? Get(body, "floorOffset") ??
                      Get(body, "height") ?? Get(body, "yOffset");

        if (adjNode is YamlScalarNode scalar &&
            float.TryParse(scalar.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float adj) &&
            float.IsFinite(adj))
        {
            parsedAdjustment = adj;
        }

        if (floorAdjustmentY.HasValue)
        {
            report("FLOOR_ADJUSTER_CONFLICT",
                $"Multiple ModularAvatarFloorAdjuster components found on avatar; ignoring conflicting adjuster on GameObject {ownerGo}.",
                sourceId,
                InitialAppearanceSeverity.Warning);
            return;
        }

        floorAdjustmentY = parsedAdjustment;
        floorAdjusterSourceId = sourceId;
    }

    private static bool ParseBool(YamlNode? node, bool fallback = false)
    {
        if (node is YamlScalarNode scalar)
        {
            if (bool.TryParse(scalar.Value, out bool b)) return b;
            if (int.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) return i != 0;
        }
        return fallback;
    }

    private static UnityPosition? ParsePosition(YamlNode? node)
    {
        if (node is not YamlMappingNode map) return null;
        float x = 0f, y = 0f, z = 0f;
        if (Get(map, "x") is YamlScalarNode sx && float.TryParse(sx.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float px)) x = px;
        if (Get(map, "y") is YamlScalarNode sy && float.TryParse(sy.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float py)) y = py;
        if (Get(map, "z") is YamlScalarNode sz && float.TryParse(sz.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float pz)) z = pz;
        return new UnityPosition(x, y, z);
    }

    private static void CompileAnchorOverrideFix(
        long sourceId,
        YamlMappingNode data,
        long ownerGo,
        Dictionary<long, long> gameObjectToTransform,
        Dictionary<long, long> transformParents,
        Dictionary<long, (long GameObjectId, HashSet<string> Shapes)> renderers,
        List<AvatarMeshSettingsBinding> meshSettingsBindings)
    {
        long anchorGo = ParseFileId(Get(data, "anchor") ?? Get(data, "target") ?? Get(data, "targetObject"));
        long targetRenderer = ParseFileId(Get(data, "renderer"));

        if (targetRenderer != 0)
        {
            meshSettingsBindings.Add(new AvatarMeshSettingsBinding(
                sourceId,
                targetRenderer,
                anchorGo,
                new UnityPosition(0, 0, 0),
                new UnityPosition(1, 1, 1),
                SetBounds: false,
                SetProbeAnchor: true
            ));
            return;
        }

        long ownerTransform = gameObjectToTransform.GetValueOrDefault(ownerGo);
        if (ownerTransform == 0) return;

        bool IsUnderOwner(long rendererGo)
        {
            long t = gameObjectToTransform.GetValueOrDefault(rendererGo);
            var seen = new HashSet<long>();
            while (t != 0 && seen.Add(t))
            {
                if (t == ownerTransform) return true;
                t = transformParents.GetValueOrDefault(t);
            }
            return false;
        }

        foreach (var (rendererFileId, (rendererGo, _)) in renderers)
        {
            if (IsUnderOwner(rendererGo))
            {
                meshSettingsBindings.Add(new AvatarMeshSettingsBinding(
                    sourceId,
                    rendererFileId,
                    anchorGo,
                    new UnityPosition(0, 0, 0),
                    new UnityPosition(1, 1, 1),
                    SetBounds: false,
                    SetProbeAnchor: true
                ));
            }
        }
    }

    private static void CompileBoundingBoxFix(
        long sourceId,
        YamlMappingNode data,
        long ownerGo,
        Dictionary<long, long> gameObjectToTransform,
        Dictionary<long, long> transformParents,
        Dictionary<long, (long GameObjectId, HashSet<string> Shapes)> renderers,
        List<AvatarMeshSettingsBinding> meshSettingsBindings)
    {
        long targetRenderer = ParseFileId(Get(data, "renderer"));
        var boundsNode = Get(data, "bounds") as YamlMappingNode ?? Get(data, "boundingBox") as YamlMappingNode;
        var centerPos = ParsePosition(Get(boundsNode, "m_Center") ?? Get(boundsNode, "center")) ?? new UnityPosition(0, 0, 0);
        var sizePos = ParsePosition(Get(boundsNode, "m_Extent") ?? Get(boundsNode, "extent") ?? Get(boundsNode, "m_Size") ?? Get(boundsNode, "size")) ?? new UnityPosition(2, 2, 2);

        if (targetRenderer != 0)
        {
            meshSettingsBindings.Add(new AvatarMeshSettingsBinding(
                sourceId,
                targetRenderer,
                0,
                centerPos,
                sizePos,
                SetBounds: true,
                SetProbeAnchor: false
            ));
            return;
        }

        long ownerTransform = gameObjectToTransform.GetValueOrDefault(ownerGo);
        if (ownerTransform == 0) return;

        bool IsUnderOwner(long rendererGo)
        {
            long t = gameObjectToTransform.GetValueOrDefault(rendererGo);
            var seen = new HashSet<long>();
            while (t != 0 && seen.Add(t))
            {
                if (t == ownerTransform) return true;
                t = transformParents.GetValueOrDefault(t);
            }
            return false;
        }

        foreach (var (rendererFileId, (rendererGo, _)) in renderers)
        {
            if (IsUnderOwner(rendererGo))
            {
                meshSettingsBindings.Add(new AvatarMeshSettingsBinding(
                    sourceId,
                    rendererFileId,
                    0,
                    centerPos,
                    sizePos,
                    SetBounds: true,
                    SetProbeAnchor: false
                ));
            }
        }
    }

    private static void CompileBoneConstraint(
        long sourceId,
        YamlMappingNode data,
        long ownerGo,
        Func<long, bool> isOwnedGo,
        List<AvatarBoneConstraintBinding> bindings,
        Action<string, string, long, InitialAppearanceSeverity> report)
    {
        long objRef = ParseFileId(Get(data, "obj"));
        if (objRef == 0) objRef = ownerGo;
        if (!isOwnedGo(objRef))
        {
            report("BONE_CONSTRAINT_TARGET", "BoneConstraint target object is outside avatar root.", sourceId, InitialAppearanceSeverity.Warning);
            return;
        }
        int bone = 0;
        if (Get(data, "bone") is YamlScalarNode boneScalar &&
            int.TryParse(boneScalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedBone))
        {
            bone = parsedBone;
        }
        bindings.Add(new AvatarBoneConstraintBinding(sourceId, objRef, bone));
    }

    private static void CompileWorldConstraint(
        long sourceId,
        YamlMappingNode data,
        long ownerGo,
        Func<long, bool> isOwnedGo,
        List<AvatarWorldConstraintBinding> bindings,
        Action<string, string, long, InitialAppearanceSeverity> report)
    {
        long objRef = ParseFileId(Get(data, "obj"));
        if (objRef == 0) objRef = ownerGo;
        if (!isOwnedGo(objRef))
        {
            report("WORLD_CONSTRAINT_TARGET", "WorldConstraint target object is outside avatar root.", sourceId, InitialAppearanceSeverity.Warning);
            return;
        }
        bindings.Add(new AvatarWorldConstraintBinding(sourceId, objRef));
    }

    private static void CompileHeadChopHead(
        long sourceId,
        YamlMappingNode data,
        long ownerGo,
        Func<long, bool> isOwnedGo,
        List<long> visibleInFirstPersonObjectIds)
    {
        long target = ParseFileId(Get(data, "target"));
        if (target == 0) target = ownerGo;
        if (isOwnedGo(target) && !visibleInFirstPersonObjectIds.Contains(target))
        {
            visibleInFirstPersonObjectIds.Add(target);
        }
    }

    private static IEnumerable<YamlNode> Sequence(YamlMappingNode? mapping, string key) =>
        Get(mapping, key) is YamlSequenceNode sequence ? sequence.Children : Array.Empty<YamlNode>();

    private static void CompileVrcHeadChop(
        long sourceId,
        YamlMappingNode body,
        long ownerGo,
        Func<long, bool> isOwnedGo,
        List<long> visibleInFirstPersonObjectIds)
    {
        if (isOwnedGo(ownerGo) && !visibleInFirstPersonObjectIds.Contains(ownerGo))
            visibleInFirstPersonObjectIds.Add(ownerGo);
        foreach (var targetNode in Sequence(body, "targetBones"))
        {
            long target = ParseFileId(targetNode);
            if (target != 0 && isOwnedGo(target) && !visibleInFirstPersonObjectIds.Contains(target))
                visibleInFirstPersonObjectIds.Add(target);
        }
    }
}
