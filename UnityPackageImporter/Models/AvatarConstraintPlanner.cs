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

public static class AvatarConstraintPlanner
{
    public const int UnityRotationConstraintClassId = 1083451514;
    public const int UnityPositionConstraintClassId = 2083052904;
    public const int UnityParentConstraintClassId = 1773428102;
    public const int UnityLookAtConstraintClassId = 1953259897;
    public const int UnityAimConstraintClassId = 1183024368;
    public const int UnityScaleConstraintClassId = 894565638;

    public const string VrcConstraintScriptGuid = "e7401dbab6930064d96a7d0e140d7c71";

    public static AvatarConstraintPlan Compile(string yaml, long avatarRootId, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var diagnostics = new List<InitialAppearanceDiagnostic>();
        void Report(string code, string message, long source = 0, InitialAppearanceSeverity severity = InitialAppearanceSeverity.Warning) =>
            diagnostics.Add(new(severity, "CONSTRAINT_" + code, message, source));

        if (string.IsNullOrWhiteSpace(yaml))
        {
            return new AvatarConstraintPlan { Diagnostics = diagnostics.AsReadOnly() };
        }

        var stream = new YamlStream();
        try
        {
            string normalized = Regex.Replace(AvatarPackageIndex.NormalizeDocumentHeaders(yaml), @"(?m)^(---[ \t]+)!u!(\d+)", "$1!<tag:unity3d.com,2011:$2>");
            stream.Load(new StringReader(normalized));
        }
        catch (Exception ex) when (ex is YamlException or InvalidDataException)
        {
            Report("YAML_PARSE", "Failed to parse YAML for constraint compilation: " + ex.Message, 0, InitialAppearanceSeverity.Error);
            return new AvatarConstraintPlan { Diagnostics = diagnostics.AsReadOnly(), HasUnsupportedContext = true };
        }

        token.ThrowIfCancellationRequested();


        var gameObjectToTransform = new Dictionary<long, long>();
        var transformToGameObject = new Dictionary<long, long>();
        var transformParents = new Dictionary<long, long>();
        var rawComponents = new List<(long FileId, int ClassId, string TagName, YamlMappingNode Body)>();

        foreach (var doc in stream.Documents)
        {
            token.ThrowIfCancellationRequested();
            if (doc.RootNode is not YamlMappingNode rootNode || rootNode.Children.Count != 1) continue;
            var entry = rootNode.Children.First();
            if (entry.Value is not YamlMappingNode body) continue;

            long fileId = ParseHeaderId(rootNode);
            string tag = rootNode.Tag.Value ?? "";
            int classId = ParseClassId(tag);
            string tagName = entry.Key is YamlScalarNode s ? (s.Value ?? "") : "";

            if (classId == 1)
            {

            }
            else if (classId == 4)
            {
                long goId = ReferenceNode(Get(body, "m_GameObject"));
                transformToGameObject[fileId] = goId;
                if (goId != 0) gameObjectToTransform[goId] = fileId;

                long father = ReferenceNode(Get(body, "m_Father"));
                if (father != 0) transformParents[fileId] = father;
            }
            else
            {
                rawComponents.Add((fileId, classId, tagName, body));
            }
        }

        bool Descends(long childTrans, long ancestorTrans)
        {
            if (childTrans == 0 || ancestorTrans == 0) return false;
            long cur = childTrans;
            while (cur != 0)
            {
                if (cur == ancestorTrans) return true;
                cur = transformParents.GetValueOrDefault(cur);
            }
            return false;
        }

        long avatarRootTransform = gameObjectToTransform.GetValueOrDefault(avatarRootId);

        var constraints = new List<AvatarConstraintDefinition>();
        var targetToSources = new Dictionary<long, List<long>>();

        foreach (var (fileId, classId, tagName, body) in rawComponents)
        {
            token.ThrowIfCancellationRequested();
            AvatarConstraintType? constraintType = null;

            if (classId == UnityRotationConstraintClassId || tagName.Equals("RotationConstraint", StringComparison.OrdinalIgnoreCase))
                constraintType = AvatarConstraintType.Rotation;
            else if (classId == UnityPositionConstraintClassId || tagName.Equals("PositionConstraint", StringComparison.OrdinalIgnoreCase))
                constraintType = AvatarConstraintType.Position;
            else if (classId == UnityParentConstraintClassId || tagName.Equals("ParentConstraint", StringComparison.OrdinalIgnoreCase))
                constraintType = AvatarConstraintType.Parent;
            else if (classId == UnityLookAtConstraintClassId || tagName.Equals("LookAtConstraint", StringComparison.OrdinalIgnoreCase))
                constraintType = AvatarConstraintType.LookAt;
            else if (classId == UnityAimConstraintClassId || tagName.Equals("AimConstraint", StringComparison.OrdinalIgnoreCase))
                constraintType = AvatarConstraintType.Aim;
            else if (classId == UnityScaleConstraintClassId || tagName.Equals("ScaleConstraint", StringComparison.OrdinalIgnoreCase))
                constraintType = AvatarConstraintType.Scale;
            else if (classId == 114)
            {
                var script = Get(body, "m_Script") as YamlMappingNode;
                string guid = Text(script, "guid");
                string idClass = Text(body, "m_EditorClassIdentifier");

                if (guid.Equals(VrcConstraintScriptGuid, StringComparison.OrdinalIgnoreCase) || idClass.StartsWith("VRC", StringComparison.OrdinalIgnoreCase))
                {
                    if (idClass.Contains("ParentConstraint", StringComparison.OrdinalIgnoreCase))
                        constraintType = AvatarConstraintType.Parent;
                    else if (idClass.Contains("PositionConstraint", StringComparison.OrdinalIgnoreCase))
                        constraintType = AvatarConstraintType.Position;
                    else if (idClass.Contains("RotationConstraint", StringComparison.OrdinalIgnoreCase))
                        constraintType = AvatarConstraintType.Rotation;
                    else if (idClass.Contains("LookAtConstraint", StringComparison.OrdinalIgnoreCase))
                        constraintType = AvatarConstraintType.LookAt;
                    else if (idClass.Contains("AimConstraint", StringComparison.OrdinalIgnoreCase))
                        constraintType = AvatarConstraintType.Aim;
                    else if (idClass.Contains("ScaleConstraint", StringComparison.OrdinalIgnoreCase))
                        constraintType = AvatarConstraintType.Scale;
                }
            }

            if (!constraintType.HasValue) continue;

            long goId = ReferenceNode(Get(body, "m_GameObject"));
            long targetTransId = gameObjectToTransform.GetValueOrDefault(goId);


            if (avatarRootTransform != 0 && targetTransId != 0 && !Descends(targetTransId, avatarRootTransform))
                continue;


            var sources = new List<AvatarConstraintSource>();
            var sourcesSeq = Sequence(body, "m_Sources").Concat(Sequence(body, "Sources"));
            foreach (var item in sourcesSeq)
            {
                if (item is not YamlMappingNode srcMap) continue;
                long srcTrans = ReferenceNode(Get(srcMap, "sourceTransform")) | ReferenceNode(Get(srcMap, "SourceTransform"));
                float srcWeight = ParseFloat(Text(srcMap, "weight") != "" ? Text(srcMap, "weight") : Text(srcMap, "Weight"), 1.0f);
                if (srcTrans != 0)
                {

                    if (avatarRootTransform != 0 && !Descends(srcTrans, avatarRootTransform))
                    {
                        Report("EXTERNAL_SOURCE", $"Constraint source transform {srcTrans} is outside avatar hierarchy and was skipped.", fileId);
                        continue;
                    }
                    sources.Add(new AvatarConstraintSource(srcTrans, srcWeight));
                }
            }

            if (sources.Count == 0)
            {
                Report("NO_SOURCES", $"Constraint on GameObject {goId} has no valid sources within avatar scope.", fileId);
                continue;
            }


            if (sources.Any(s => s.SourceTransformId == targetTransId))
            {
                Report("SELF_REFERENCE", $"Constraint on Transform {targetTransId} references itself as a source.", fileId, InitialAppearanceSeverity.Error);
                continue;
            }

            bool isActive = ParseInt(Text(body, "m_IsContraintActive") != "" ? Text(body, "m_IsContraintActive") : Text(body, "IsActive"), 1) != 0;
            float weight = Math.Clamp(ParseFloat(Text(body, "m_Weight") != "" ? Text(body, "m_Weight") : Text(body, "Weight"), 1.0f), 0f, 1f);

            var posOffset = ParseVector3(Get(body, "m_TranslationOffsets") ?? Get(body, "PositionOffset"));
            var rotOffset = ParseVector4(Get(body, "m_RotationOffsets") ?? Get(body, "RotationOffset"));

            bool affX = ParseInt(Text(body, "m_AffectTranslationX") != "" ? Text(body, "m_AffectTranslationX") : Text(body, "AffectTranslationX"), 1) != 0;
            bool affY = ParseInt(Text(body, "m_AffectTranslationY") != "" ? Text(body, "m_AffectTranslationY") : Text(body, "AffectTranslationY"), 1) != 0;
            bool affZ = ParseInt(Text(body, "m_AffectTranslationZ") != "" ? Text(body, "m_AffectTranslationZ") : Text(body, "AffectTranslationZ"), 1) != 0;

            bool rotX = ParseInt(Text(body, "m_AffectRotationX") != "" ? Text(body, "m_AffectRotationX") : Text(body, "AffectRotationX"), 1) != 0;
            bool rotY = ParseInt(Text(body, "m_AffectRotationY") != "" ? Text(body, "m_AffectRotationY") : Text(body, "AffectRotationY"), 1) != 0;
            bool rotZ = ParseInt(Text(body, "m_AffectRotationZ") != "" ? Text(body, "m_AffectRotationZ") : Text(body, "AffectRotationZ"), 1) != 0;

            var def = new AvatarConstraintDefinition
            {
                ComponentId = fileId,
                GameObjectId = goId,
                TargetTransformId = targetTransId,
                Type = constraintType.Value,
                IsActive = isActive,
                Weight = weight,
                Sources = sources.AsReadOnly(),
                PositionOffset = posOffset,
                RotationOffset = rotOffset,
                AffectTranslation = (affX, affY, affZ),
                AffectRotation = (rotX, rotY, rotZ)
            };

            constraints.Add(def);

            if (!targetToSources.TryGetValue(targetTransId, out var srcList))
            {
                srcList = new List<long>();
                targetToSources[targetTransId] = srcList;
            }
            srcList.AddRange(sources.Select(s => s.SourceTransformId));
        }


        foreach (var (target, srcs) in targetToSources)
        {
            foreach (var src in srcs)
            {
                if (targetToSources.TryGetValue(src, out var reverseSrcs) && reverseSrcs.Contains(target))
                {
                    Report("CYCLE_DETECTED", $"Cyclic constraint dependency detected between transforms {target} and {src}.", 0, InitialAppearanceSeverity.Error);
                }
            }
        }

        return new AvatarConstraintPlan
        {
            Constraints = constraints.AsReadOnly(),
            Diagnostics = diagnostics.AsReadOnly(),
            HasUnsupportedContext = diagnostics.Any(d => d.Severity == InitialAppearanceSeverity.Error)
        };
    }

    private static YamlNode? Get(YamlMappingNode? node, string key) =>
        node != null && node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    private static string Text(YamlNode? node, string key) =>
        node is YamlMappingNode mapping && Get(mapping, key) is YamlScalarNode scalar ? (scalar.Value ?? "") : "";

    private static IEnumerable<YamlNode> Sequence(YamlMappingNode? node, string key) =>
        node != null && Get(node, key) is YamlSequenceNode sequence ? sequence.Children : Enumerable.Empty<YamlNode>();

    private static long ReferenceNode(YamlNode? node) =>
        node is YamlMappingNode mapping && Get(mapping, "fileID") is YamlScalarNode scalar &&
        long.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) ? id : 0;

    private static float ParseFloat(string value, float fallback = 0f) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result) ? result : fallback;

    private static int ParseInt(string value, int fallback = 0) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : fallback;

    private static UnityPosition ParseVector3(YamlNode? node)
    {
        if (node is not YamlMappingNode mapping) return default;
        float x = ParseFloat(Text(mapping, "x"));
        float y = ParseFloat(Text(mapping, "y"));
        float z = ParseFloat(Text(mapping, "z"));
        return new UnityPosition(x, y, z);
    }

    private static (float X, float Y, float Z, float W) ParseVector4(YamlNode? node)
    {
        if (node is not YamlMappingNode mapping) return (0, 0, 0, 1);
        float x = ParseFloat(Text(mapping, "x"));
        float y = ParseFloat(Text(mapping, "y"));
        float z = ParseFloat(Text(mapping, "z"));
        float w = ParseFloat(Text(mapping, "w"), 1f);
        return (x, y, z, w);
    }

    private static long ParseHeaderId(YamlMappingNode rootNode)
    {
        if (!string.IsNullOrEmpty(rootNode.Anchor.Value) &&
            long.TryParse(rootNode.Anchor.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
            return id;
        return 0;
    }

    private static int ParseClassId(string tag)
    {
        if (tag.StartsWith("tag:unity3d.com,2011:", StringComparison.Ordinal) &&
            int.TryParse(tag["tag:unity3d.com,2011:".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int classId))
            return classId;
        return 0;
    }
}
