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

internal sealed record UnityStaticCurve(string Path, int ClassId, string Attribute, float Value);
internal sealed class UnityStaticClipResult
{
    internal IReadOnlyList<UnityStaticCurve> Curves { get; init; } = Array.Empty<UnityStaticCurve>();
    internal IReadOnlyList<InitialAppearanceDiagnostic> Diagnostics { get; init; } = Array.Empty<InitialAppearanceDiagnostic>();
    internal bool CanApply => Diagnostics.All(item => item.Severity != InitialAppearanceSeverity.Error);
}

internal static class UnityStaticAnimationClip
{
    private static readonly Regex Header = new(@"^--- !u!(?<class>\d+) &(?<id>-?\d+)(?: stripped)?[ \t]*\r?$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly string[] UnsupportedCurves = { "m_PositionCurves", "m_RotationCurves", "m_EulerCurves", "m_EulerEditorCurves", "m_ScaleCurves", "m_CompressedRotationCurves", "m_PPtrCurves", "m_Events" };

    internal static UnityStaticClipResult Parse(string yaml, long fileId, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        token.ThrowIfCancellationRequested();
        var diagnostics = new List<InitialAppearanceDiagnostic>();
        var curves = new Dictionary<(string Path, int ClassId, string Attribute), UnityStaticCurve>();
        void Error(string code, string message) => diagnostics.Add(new(InitialAppearanceSeverity.Error, code, message, fileId));
        var headers = Header.Matches(yaml);
        var selected = headers.Cast<Match>().Where(header => header.Groups["id"].Value == fileId.ToString(CultureInfo.InvariantCulture)).ToArray();
        if (selected.Length != 1 || selected[0].Groups["class"].Value != "74")
        {
            Error("STATIC_CLIP_IDENTITY", "Animation clip GUID/fileID must identify exactly one serialized AnimationClip; names are not identities.");
            return new() { Diagnostics = diagnostics };
        }
        var selectedHeader = selected[0];
        int index = headers.Cast<Match>().ToList().IndexOf(selectedHeader);
        int start = selectedHeader.Index + selectedHeader.Length;
        int end = index + 1 < headers.Count ? headers[index + 1].Index : yaml.Length;
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml.Substring(start, end - start)));
        }
        catch (YamlException exception)
        {
            Error("STATIC_CLIP_FORMAT", "The selected animation clip has malformed YAML: " + exception.Message);
            return new() { Diagnostics = diagnostics };
        }
        if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode wrapper ||
            Get(wrapper, "AnimationClip") is not YamlMappingNode clip)
        {
            Error("STATIC_CLIP_FORMAT", "The selected animation clip has no serialized mapping.");
            return new() { Diagnostics = diagnostics };
        }
        var compression = Get(clip, "m_Compressed");
        if (compression != null && (compression is not YamlScalarNode compressionFlag || compressionFlag.Value is not ("0" or "1")))
            Error("STATIC_CLIP_FORMAT", "Compression metadata must be an explicit serialized boolean when present.");
        else if (Text(clip, "m_Compressed") == "1")
            Error("STATIC_CLIP_COMPRESSED", "Compressed animation data requires an independent decoder; no editor-only approximation was sampled.");
        var checkedNodes = new HashSet<YamlNode>(ReferenceEqualityComparer.Instance);
        void CheckPacked(YamlNode node)
        {
            token.ThrowIfCancellationRequested();
            if (!checkedNodes.Add(node)) return;
            if (node is YamlMappingNode mapping)
            {
                foreach (var pair in mapping.Children)
                {
                    string key = (pair.Key as YamlScalarNode)?.Value ?? "";
                    if (key is "m_StreamedClip" or "m_DenseClip" or "m_ConstantClip")
                    {
                        if (pair.Value is not YamlMappingNode packed)
                            Error("STATIC_CLIP_PACKED", "Packed animation data cannot be treated as a proven appearance snapshot.");
                        else foreach (string data in new[] { "data", "m_Data", "m_SampleArray" })
                            if (Get(packed, data) is YamlNode packedData &&
                                (packedData is not YamlSequenceNode values || values.Children.Count != 0))
                                Error("STATIC_CLIP_PACKED", "Packed animation samples require an independent binding decoder.");
                    }
                    CheckPacked(pair.Value);
                }
            }
            else if (node is YamlSequenceNode sequence) foreach (var item in sequence) CheckPacked(item);
        }
        CheckPacked(clip);
        var settingsNode = Get(clip, "m_AnimationClipSettings");
        if (settingsNode != null)
        {
            if (settingsNode is not YamlMappingNode settings)
                Error("STATIC_CLIP_FORMAT", "Animation clip settings must be a mapping when present.");
            else if (Get(settings, "m_HasAdditiveReferencePose") is YamlNode additive)
            {
                if (additive is not YamlScalarNode additiveFlag || additiveFlag.Value is not ("0" or "1"))
                    Error("STATIC_CLIP_FORMAT", "Additive-pose metadata must be an explicit serialized boolean when present.");
                else if (additiveFlag.Value == "1")
                    Error("STATIC_CLIP_ADDITIVE", "Additive reference-pose evaluation is not an absolute appearance snapshot.");
            }
        }
        foreach (string field in UnsupportedCurves)
        {
            var node = Get(clip, field);
            if (node != null && (node is not YamlSequenceNode sequence || sequence.Children.Count != 0))
                Error("STATIC_CLIP_BINDING_UNSUPPORTED", "Static appearance clips cannot contain unsupported transforms, object curves or events: " + field + ".");
        }
        foreach (string field in new[] { "m_FloatCurves", "m_EditorCurves" })
        {
            token.ThrowIfCancellationRequested();
            var node = Get(clip, field);
            if (node == null) continue;
            if (node is not YamlSequenceNode bindings)
            {
                Error("STATIC_CLIP_FORMAT", field + " must be a curve sequence.");
                continue;
            }
            foreach (var bindingNode in bindings)
            {
                token.ThrowIfCancellationRequested();
                if (bindingNode is not YamlMappingNode binding || Get(binding, "path") is not YamlScalarNode ||
                    !int.TryParse(Text(binding, "classID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int classId))
                {
                    Error("STATIC_CLIP_FORMAT", "A float binding has invalid path or component identity.");
                    continue;
                }
                string path = Text(binding, "path"), attribute = Text(binding, "attribute");
                bool supported = classId == 1 && attribute == "m_IsActive" ||
                    classId == 137 && attribute.StartsWith("blendShape.", StringComparison.Ordinal) && attribute.Length > "blendShape.".Length;
                var scriptReference = Get(binding, "script");
                bool scripted = scriptReference != null && (scriptReference is not YamlMappingNode script || Text(script, "fileID") != "0" || Text(script, "guid").Length != 0);
                if (!supported || path.StartsWith("/", StringComparison.Ordinal) || path.Split('/').Any(segment => segment is "." or "..") || scripted)
                {
                    Error("STATIC_CLIP_BINDING_UNSUPPORTED", "Only component-relative object activation and named skinned blendshape bindings are supported.");
                    continue;
                }
                if (Get(binding, "curve") is not YamlMappingNode curve || Get(curve, "m_Curve") is not YamlSequenceNode keys || keys.Children.Count == 0)
                {
                    Error("STATIC_CLIP_FORMAT", "A float binding has no serialized keyframes.");
                    continue;
                }
                float? value = null;
                float lastTime = float.NegativeInfinity;
                bool valid = true;
                foreach (var keyNode in keys)
                {
                    if (keyNode is not YamlMappingNode key || !Number(key, "time", out float time) || time <= lastTime ||
                        !Number(key, "value", out float weight))
                    {
                        Error("STATIC_CLIP_FORMAT", "Keyframe times must be finite and strictly increasing, with finite values.");
                        valid = false;
                        break;
                    }
                    lastTime = time;
                    if (value.HasValue && value.Value != weight)
                    {
                        Error("STATIC_CLIP_TIME_VARYING", "Time-varying curves cannot be converted by selecting an arbitrary first frame.");
                        valid = false;
                        break;
                    }
                    value = weight;
                    foreach (string tangent in new[] { "inSlope", "outSlope" })
                        if (Get(key, tangent) != null && (!Number(key, tangent, out float slope) || slope != 0))
                        {
                            Error("STATIC_CLIP_TIME_VARYING", "Nonzero or non-finite tangents are not a proven constant resting curve.");
                            valid = false;
                        }
                }
                if (!valid || !value.HasValue) continue;
                if (attribute == "m_IsActive" && value.Value is not (0 or 1))
                {
                    Error("STATIC_CLIP_ACTIVE_VALUE", "An activation snapshot must explicitly be zero or one.");
                    continue;
                }
                var result = new UnityStaticCurve(path, classId, attribute, value.Value);
                var identity = (path, classId, attribute);
                if (curves.TryGetValue(identity, out var existing) && existing.Value != result.Value)
                    Error("STATIC_CLIP_CONFLICT", "Runtime/editor curves disagree on one appearance binding.");
                else curves[identity] = result;
            }
        }
        return new() { Curves = diagnostics.Count == 0 ? curves.Values.ToArray() : Array.Empty<UnityStaticCurve>(), Diagnostics = diagnostics };
    }

    private static YamlNode? Get(YamlMappingNode body, string key) => body.Children.TryGetValue(key, out var node) ? node : null;
    private static string Text(YamlMappingNode body, string key) => (Get(body, key) as YamlScalarNode)?.Value ?? "";
    private static bool Number(YamlMappingNode body, string key, out float value) =>
        float.TryParse(Text(body, key), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);
}
