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

        // 1. Index GameObjects, Transforms, and Renderers
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
                // GameObject
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
            else if (tagStr.EndsWith("!137") || tagStr == "SkinnedMeshRenderer")
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

        // Descendant check
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
        bool blinkSuppressed = false;
        bool hasBlockBlinking = false;
        bool hasBlockVisemes = false;

        // 2. Scan VRCFury components
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

            // Check records for action blockers
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
            }
        }

        return new AvatarBehaviorPlan
        {
            BreathingBindings = breathingBindings.AsReadOnly(),
            BlinkingBindings = blinkingBindings.AsReadOnly(),
            TalkingBindings = talkingBindings.AsReadOnly(),
            VisemeBindings = visemeBindings.AsReadOnly(),
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
}
