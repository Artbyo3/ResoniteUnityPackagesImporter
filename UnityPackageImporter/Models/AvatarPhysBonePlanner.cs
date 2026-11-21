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

public static class AvatarPhysBonePlanner
{
    public const string PhysBoneScriptGuid = "2a2c05204084d904aa4945ccff20d8e5";
    public const long PhysBoneScriptFileId = 1661641543;
    public const long PhysBoneColliderScriptFileId = -1631200402;
    public const string ModularAvatarBlockerScriptGuid = "a5bf908a199a4648845ebe2fd3b5a4bd";

    public static AvatarPhysBonePlan Compile(string yaml, long avatarRootId, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var diagnostics = new List<InitialAppearanceDiagnostic>();
        void Report(string code, string message, long source = 0, InitialAppearanceSeverity severity = InitialAppearanceSeverity.Warning) =>
            diagnostics.Add(new(severity, "PHYSBONE_" + code, message, source));

        if (string.IsNullOrWhiteSpace(yaml))
        {
            return new AvatarPhysBonePlan { Diagnostics = diagnostics.AsReadOnly() };
        }

        var stream = new YamlStream();
        try
        {
            string normalized = Regex.Replace(AvatarPackageIndex.NormalizeDocumentHeaders(yaml), @"(?m)^(---[ \t]+)!u!(\d+)", "$1!<tag:unity3d.com,2011:$2>");
            stream.Load(new StringReader(normalized));
        }
        catch (Exception ex) when (ex is YamlException or InvalidDataException)
        {
            Report("YAML_PARSE", "Failed to parse YAML for PhysBone compilation: " + ex.Message);
            return new AvatarPhysBonePlan { Diagnostics = diagnostics.AsReadOnly(), HasUnsupportedContext = true };
        }

        token.ThrowIfCancellationRequested();


        var gameObjects = new Dictionary<long, YamlMappingNode>();
        var transformToGameObject = new Dictionary<long, long>();
        var gameObjectToTransform = new Dictionary<long, long>();
        var transformParents = new Dictionary<long, long>();
        var transformChildren = new Dictionary<long, List<long>>();
        var components = new List<(long Id, int ClassId, YamlMappingNode Body)>();

        foreach (var doc in stream.Documents)
        {
            token.ThrowIfCancellationRequested();
            if (doc.RootNode is not YamlMappingNode rootNode || rootNode.Children.Count != 1) continue;
            var entry = rootNode.Children.First();
            if (entry.Value is not YamlMappingNode body) continue;
            string tag = rootNode.Tag.Value ?? "";
            long fileId = ParseHeaderId(rootNode);
            int classId = ParseClassId(tag, entry.Key);

            if (classId == 1)
            {
                gameObjects[fileId] = body;
            }
            else if (classId == 4)
            {
                long goId = ReferenceNode(Get(body, "m_GameObject"));
                transformToGameObject[fileId] = goId;
                if (goId != 0) gameObjectToTransform[goId] = fileId;

                long father = ReferenceNode(Get(body, "m_Father"));
                if (father != 0) transformParents[fileId] = father;

                var children = Sequence(body, "m_Children")
                    .Select(ReferenceNode)
                    .Where(id => id != 0)
                    .ToList();
                transformChildren[fileId] = children;
            }
            else if (classId == 114)
            {
                components.Add((fileId, classId, body));
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


        var colliders = new List<AvatarPhysBoneColliderDefinition>();
        var colliderIds = new HashSet<long>();

        foreach (var (compFileId, _, body) in components)
        {
            token.ThrowIfCancellationRequested();
            var script = Get(body, "m_Script") as YamlMappingNode;
            string guid = Text(script, "guid");
            long scriptId = ParseLong(Text(script, "fileID"));
            string name = Text(body, "m_Name");
            string idClass = Text(body, "m_EditorClassIdentifier");

            bool isPhysBoneCollider = (scriptId == PhysBoneColliderScriptFileId && guid.Equals(PhysBoneScriptGuid, StringComparison.OrdinalIgnoreCase))
                || idClass.Contains("PhysBoneCollider", StringComparison.OrdinalIgnoreCase)
                || name.Contains("DynamicBoneCollider", StringComparison.OrdinalIgnoreCase);

            if (!isPhysBoneCollider) continue;

            long goId = ReferenceNode(Get(body, "m_GameObject"));
            long transId = gameObjectToTransform.GetValueOrDefault(goId);
            long rootTransId = ReferenceNode(Get(body, "rootTransform"));
            if (rootTransId == 0) rootTransId = transId;


            if (avatarRootTransform != 0 && rootTransId != 0 && !Descends(rootTransId, avatarRootTransform))
                continue;

            int shapeType = ParseInt(Text(body, "shapeType"));
            float radius = ParseFloat(Text(body, "radius"), 0.05f);
            float height = ParseFloat(Text(body, "height"), 0.1f);
            var pos = ParseVector3(Get(body, "position"));
            var rot = ParseVector4(Get(body, "rotation"));
            bool insideBounds = ParseInt(Text(body, "insideBounds")) != 0;

            var colDef = new AvatarPhysBoneColliderDefinition
            {
                ComponentId = compFileId,
                GameObjectId = goId,
                RootTransformId = rootTransId,
                ShapeType = shapeType,
                Radius = Math.Max(0.001f, radius),
                Height = Math.Max(0.001f, height),
                Position = pos,
                Rotation = rot,
                InsideBounds = insideBounds
            };

            colliders.Add(colDef);
            colliderIds.Add(compFileId);
        }


        var blockerTransforms = new List<long>();
        foreach (var (_, _, body) in components)
        {
            token.ThrowIfCancellationRequested();
            var script = Get(body, "m_Script") as YamlMappingNode;
            string guid = Text(script, "guid");
            string idClass = Text(body, "m_EditorClassIdentifier");

            if (guid.Equals(ModularAvatarBlockerScriptGuid, StringComparison.OrdinalIgnoreCase) ||
                idClass.Contains("PBBlocker", StringComparison.OrdinalIgnoreCase))
            {
                long goId = ReferenceNode(Get(body, "m_GameObject"));
                long transId = gameObjectToTransform.GetValueOrDefault(goId);
                if (transId != 0 && (avatarRootTransform == 0 || Descends(transId, avatarRootTransform)))
                {
                    blockerTransforms.Add(transId);
                }
            }
        }


        var bones = new List<AvatarPhysBoneDefinition>();

        foreach (var (compFileId, _, body) in components)
        {
            token.ThrowIfCancellationRequested();
            var script = Get(body, "m_Script") as YamlMappingNode;
            string guid = Text(script, "guid");
            long scriptId = ParseLong(Text(script, "fileID"));
            string name = Text(body, "m_Name");
            string idClass = Text(body, "m_EditorClassIdentifier");

            bool isPhysBone = (scriptId == PhysBoneScriptFileId && guid.Equals(PhysBoneScriptGuid, StringComparison.OrdinalIgnoreCase))
                || idClass.Contains("VRCPhysBone", StringComparison.OrdinalIgnoreCase)
                || name.Contains("DynamicBone", StringComparison.OrdinalIgnoreCase);

            if (!isPhysBone) continue;

            long goId = ReferenceNode(Get(body, "m_GameObject"));
            long transId = gameObjectToTransform.GetValueOrDefault(goId);
            long rootTransId = ReferenceNode(Get(body, "rootTransform"));
            if (rootTransId == 0) rootTransId = transId;


            if (avatarRootTransform != 0 && rootTransId != 0 && !Descends(rootTransId, avatarRootTransform))
                continue;

            var ignores = Sequence(body, "ignoreTransforms")
                .Select(ReferenceNode)
                .Where(id => id != 0)
                .ToList();



            foreach (var blockerTrans in blockerTransforms)
            {
                if (blockerTrans != rootTransId && Descends(blockerTrans, rootTransId) && !ignores.Contains(blockerTrans))
                {
                    ignores.Add(blockerTrans);
                }
            }

            var endpoint = ParseVector3(Get(body, "endpointPosition"));
            int multiChildType = ParseInt(Text(body, "multiChildType"));
            float pull = Math.Clamp(ParseFloat(Text(body, "pull"), 0.2f), 0f, 1f);
            float spring = Math.Clamp(ParseFloat(Text(body, "spring"), 0.2f), 0f, 1f);
            float stiffness = Math.Clamp(ParseFloat(Text(body, "stiffness"), 0.2f), 0f, 1f);
            float gravity = ParseFloat(Text(body, "gravity"), 0f);
            float gravityFalloff = Math.Clamp(ParseFloat(Text(body, "gravityFalloff"), 0f), 0f, 1f);
            float immobile = Math.Clamp(ParseFloat(Text(body, "immobile"), 0f), 0f, 1f);
            float radius = Math.Max(0f, ParseFloat(Text(body, "radius"), 0.02f));
            bool allowCollision = ParseInt(Text(body, "allowCollision"), 1) != 0;
            bool allowGrabbing = ParseInt(Text(body, "allowGrabbing"), 1) != 0;
            bool allowPosing = ParseInt(Text(body, "allowPosing"), 1) != 0;
            float maxStretch = ParseFloat(Text(body, "maxStretch"), 0f);

            var referencedColliders = Sequence(body, "colliders")
                .Select(ReferenceNode)
                .Where(id => id != 0)
                .ToList();

            var boneDef = new AvatarPhysBoneDefinition
            {
                ComponentId = compFileId,
                GameObjectId = goId,
                RootTransformId = rootTransId,
                IgnoreTransformIds = ignores.AsReadOnly(),
                EndpointPosition = endpoint,
                MultiChildType = multiChildType,
                Pull = pull,
                Spring = spring,
                Stiffness = stiffness,
                Gravity = gravity,
                GravityFalloff = gravityFalloff,
                Immobile = immobile,
                Radius = radius,
                AllowCollision = allowCollision,
                ColliderIds = referencedColliders.AsReadOnly(),
                AllowGrabbing = allowGrabbing,
                AllowPosing = allowPosing,
                MaxStretch = maxStretch
            };

            bones.Add(boneDef);
        }

        return new AvatarPhysBonePlan
        {
            Bones = bones.AsReadOnly(),
            Colliders = colliders.AsReadOnly(),
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

    private static long ParseLong(string value, long fallback = 0) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long result) ? result : fallback;

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

    private static int ParseClassId(string tag, YamlNode key)
    {
        if (tag.StartsWith("tag:unity3d.com,2011:", StringComparison.Ordinal) &&
            int.TryParse(tag["tag:unity3d.com,2011:".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int classId))
            return classId;
        return 0;
    }
}
