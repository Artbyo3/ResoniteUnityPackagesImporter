using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace UnityPackageImporter.Models;

public sealed class UnityAssetReference
{
    public long FileId { get; init; }
    public string Guid { get; init; } = "";
    public int Type { get; init; }

    public bool HasGuid => !string.IsNullOrWhiteSpace(Guid) && Guid != "0";
}

public sealed class VrcAnimationLayerReference
{
    public int LayerType { get; init; }
    public bool Enabled { get; init; }
    public UnityAssetReference AnimatorController { get; init; }
}

public sealed class VrcAvatarDefinition
{
    public string SourcePath { get; init; } = "";
    public long ComponentFileId { get; init; }
    public long GameObjectFileId { get; init; }
    public string GameObjectName { get; init; } = "";
    public string ScriptGuid { get; init; } = "";
    public UnityPosition? ViewPosition { get; init; }
    public UnityAssetReference ExpressionsMenu { get; init; }
    public UnityAssetReference ExpressionParameters { get; init; }
    public IReadOnlyList<VrcAnimationLayerReference> BaseAnimationLayers { get; init; } = Array.Empty<VrcAnimationLayerReference>();

    public UnityAssetReference FxController =>
        BaseAnimationLayers.FirstOrDefault(layer => layer.LayerType == 5)?.AnimatorController;
}

// Unity descriptor coordinates, before the imported GameObject transform is applied.
public readonly record struct UnityPosition(float X, float Y, float Z);

public enum ModularAvatarComponentKind
{
    MergeArmature,
    OutfitRoot,
    BoneProxy,
    BlendshapeSync,
    MenuInstaller,
    Parameters,
    ObjectToggle
}

public sealed class ModularAvatarComponentDefinition
{
    public string SourcePath { get; init; } = "";
    public long ComponentFileId { get; init; }
    public long GameObjectFileId { get; init; }
    public string GameObjectName { get; init; } = "";
    public string ScriptGuid { get; init; } = "";
    public ModularAvatarComponentKind Kind { get; init; }
    public string MergeTargetPath { get; init; } = "";
    public long? MergeTargetObjectFileId { get; init; }
    public long? ArmatureRootFileId { get; init; }
    public string Prefix { get; init; } = "";
    public string Suffix { get; init; } = "";
    public int LockMode { get; init; }
    public bool MangleNames { get; init; }
    public int BindingCount { get; init; }
    public int BoneReference { get; init; } = -1;
    public string BoneProxySubPath { get; init; } = "";
    public int BoneProxyAttachmentMode { get; init; }
    public bool BoneProxyMatchScale { get; init; }
}

public sealed class VrcFuryLinkToTarget
{
    public bool UseBone { get; init; }
    public int Bone { get; init; } = 0;
    public bool UseObj { get; init; }
    public long? ObjFileId { get; init; }
    public string Offset { get; init; } = "";
}

public sealed class VrcFuryArmatureLinkDefinition
{
    public string SourcePath { get; init; } = "";
    public long ComponentFileId { get; init; }
    public long GameObjectFileId { get; init; }
    public string GameObjectName { get; init; } = "";
    public long? PropBoneFileId { get; init; }
    public IReadOnlyList<VrcFuryLinkToTarget> LinkToTargets { get; init; } = Array.Empty<VrcFuryLinkToTarget>();
    public int SerializedVersion { get; init; } = 7;
    public int LinkMode { get; init; } = 4;
    public bool Recursive { get; init; }
    public string RemoveBoneSuffix { get; init; } = "";
    public bool AlignPosition { get; init; }
    public bool AlignRotation { get; init; }
    public bool AlignScale { get; init; }
    public bool AutoScaleFactor { get; init; } = true;
    public bool KeepOffsets { get; init; }
    public IReadOnlyList<(string Code, string Message, bool IsError)> SemanticDiagnostics { get; init; } = Array.Empty<(string, string, bool)>();
}

public sealed class VrcFuryComponentDefinition
{
    public string SourcePath { get; init; } = "";
    public long ComponentFileId { get; init; }
    public long GameObjectFileId { get; init; }
    public string GameObjectName { get; init; } = "";
    public string ScriptGuid { get; init; } = "";
    public string FeatureSummary { get; init; } = "";
    public IReadOnlyList<VrcFuryArmatureLinkDefinition> ArmatureLinks { get; init; } = Array.Empty<VrcFuryArmatureLinkDefinition>();
    public IReadOnlyList<string> UnsupportedFeatures { get; init; } = Array.Empty<string>();
    public bool HasUnsupportedFeatures => UnsupportedFeatures.Count > 0;
}

public sealed class AvatarPackageManifest
{
    public string SourcePath { get; init; } = "";
    public IReadOnlyList<VrcAvatarDefinition> Avatars { get; init; } = Array.Empty<VrcAvatarDefinition>();
    public IReadOnlyList<ModularAvatarComponentDefinition> ModularAvatarComponents { get; init; } = Array.Empty<ModularAvatarComponentDefinition>();
    public IReadOnlyList<VrcFuryComponentDefinition> VrcFuryComponents { get; init; } = Array.Empty<VrcFuryComponentDefinition>();

    public bool IsAvatarPrefab => Avatars.Count > 0;
    public bool HasVrcFuryComponents => VrcFuryComponents.Count > 0;
    public IReadOnlyList<VrcFuryArmatureLinkDefinition> AllVrcFuryArmatureLinks =>
        VrcFuryComponents.SelectMany(c => c.ArmatureLinks).ToList();
    public int UnsupportedVrcFuryFeatureCount =>
        VrcFuryComponents.Sum(c => c.UnsupportedFeatures.Count);
    public bool HasUnsupportedVrcFuryFeatures => UnsupportedVrcFuryFeatureCount > 0;

    // Components such as Bone Proxy are also valid inside ordinary avatar and
    // model prefabs. Merge Armature is the reliable signal that this prefab is
    // intended to be installed onto another avatar as clothing.
    public bool IsModularAvatarAttachment =>
        Avatars.Count == 0 &&
        ModularAvatarComponents.Any(component => component.Kind == ModularAvatarComponentKind.MergeArmature);

    // Preserve the importer's historic humanoid setup for ordinary prefab
    // models while keeping it disabled for actual Merge Armature attachments.
    public bool ShouldSetUpHumanoid => !IsModularAvatarAttachment;
}

/// <summary>
/// Builds a read-only semantic index from Unity text YAML. This intentionally does
/// not need the Unity editor or Modular Avatar assemblies, so package inspection
/// stays deterministic and safe.
/// </summary>
public static class AvatarPackageIndex
{
    // VRChat SDK3 avatar descriptor script used by current avatar packages.
    public const long VrcAvatarDescriptorScriptFileId = 542108242;
    public const string VrcAvatarDescriptorScriptGuid = "67cc4cb7839cd3741b63733d5adf0442";

    public const string MergeArmatureScriptGuid = "2df373bf91cf30b4bbd495e11cb1a2ec";
    public const string OutfitRootScriptGuid = "1895bf16884f4064f8e9550e7493c205";
    public const string BoneProxyScriptGuid = "42581d8044b64899834d3d515ab3a144";
    public const string BlendshapeSyncScriptGuid = "6fd7cab7d93b403280f2f9da978d8a4f";
    public const string MenuInstallerScriptGuid = "7ef83cb0c23d4d7c9d41021e544a1978";
    public const string ParametersScriptGuid = "71a96d4ea0c344f39e277d82035bf9bd";
    public const string ObjectToggleScriptGuid = "a162bb8ec7e24a5abcf457887f1df3fa";

    // Verified VRCFury component script GUIDs across versions.
    public const string VrcFuryFullScriptGuid = "d9e94e501a2d4c95bff3d5601013d923";
    public const string VrcFuryScriptGuid = "e011eed4b7a4cf848a61cb4f56f3458d";
    public const string VrcFuryLegacyScriptGuid = "46fb26db4e6e4774caee952b7bcfda88";
    public const string VrcFuryTestScriptGuid = "8e6c7102e3a1f7e44a7276dfc5cb1d61";
    public const string VrcFuryArmatureLinkScriptGuid = "1a4502d9220f4186a2f7b71cec18fec3";

    public static readonly HashSet<string> RecognizedVrcFuryScriptGuids = new(StringComparer.OrdinalIgnoreCase)
    {
        VrcFuryFullScriptGuid,
        VrcFuryScriptGuid,
        VrcFuryLegacyScriptGuid,
        VrcFuryTestScriptGuid,
        VrcFuryArmatureLinkScriptGuid
    };

    private static readonly Regex HeaderPattern = new(
        @"^--- !u!(?<class>\d+) &(?<id>-?\d+)(?: stripped)?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex StrippedHeaderPattern = new(
        @"(?m)^(--- !u!\d+ &-?\d+) stripped[ \t]*(?=\r?$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FileIdPattern = new(
        @"fileID:\s*(?<value>-?\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex GuidPattern = new(
        @"guid:\s*(?<value>[0-9a-fA-F]+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TypePattern = new(
        @"type:\s*(?<value>-?\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly IReadOnlyDictionary<string, ModularAvatarComponentKind> ModularAvatarScripts =
        new Dictionary<string, ModularAvatarComponentKind>(StringComparer.OrdinalIgnoreCase)
        {
            [MergeArmatureScriptGuid] = ModularAvatarComponentKind.MergeArmature,
            [OutfitRootScriptGuid] = ModularAvatarComponentKind.OutfitRoot,
            [BoneProxyScriptGuid] = ModularAvatarComponentKind.BoneProxy,
            [BlendshapeSyncScriptGuid] = ModularAvatarComponentKind.BlendshapeSync,
            [MenuInstallerScriptGuid] = ModularAvatarComponentKind.MenuInstaller,
            [ParametersScriptGuid] = ModularAvatarComponentKind.Parameters,
            [ObjectToggleScriptGuid] = ModularAvatarComponentKind.ObjectToggle
        };

    public static AvatarPackageManifest ParseFile(string filePath, IReadOnlyDictionary<string, string> prefabs = null)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("A prefab or scene path is required.", nameof(filePath));
        var manifest = ParseText(File.ReadAllText(filePath), filePath);
        if (prefabs != null && File.Exists(filePath))
        {
            string sourceGuid = PrefabVariantResolver.FindSourceGuidInPrefabFile(filePath);
            if (!string.IsNullOrEmpty(sourceGuid))
            {
                var ancestorFiles = PrefabVariantResolver.GetAncestorPrefabFiles(sourceGuid, prefabs);
                var inheritedAvatars = new List<VrcAvatarDefinition>(manifest.Avatars);
                var inheritedModularAvatar = new List<ModularAvatarComponentDefinition>(manifest.ModularAvatarComponents);
                var inheritedVrcFury = new List<VrcFuryComponentDefinition>(manifest.VrcFuryComponents);

                foreach (var ancestorFile in ancestorFiles)
                {
                    if (File.Exists(ancestorFile))
                    {
                        var ancestorManifest = ParseFile(ancestorFile, prefabs);
                        if (inheritedAvatars.Count == 0 && ancestorManifest.Avatars.Count > 0)
                        {
                            inheritedAvatars.AddRange(ancestorManifest.Avatars);
                        }
                        foreach (var mac in ancestorManifest.ModularAvatarComponents)
                        {
                            if (!inheritedModularAvatar.Any(existing => existing.ComponentFileId == mac.ComponentFileId))
                                inheritedModularAvatar.Add(mac);
                        }
                        foreach (var vfc in ancestorManifest.VrcFuryComponents)
                        {
                            if (!inheritedVrcFury.Any(existing => existing.ComponentFileId == vfc.ComponentFileId))
                                inheritedVrcFury.Add(vfc);
                        }
                    }
                }

                if (inheritedAvatars.Count != manifest.Avatars.Count ||
                    inheritedModularAvatar.Count != manifest.ModularAvatarComponents.Count ||
                    inheritedVrcFury.Count != manifest.VrcFuryComponents.Count)
                {
                    return new AvatarPackageManifest
                    {
                        SourcePath = filePath,
                        Avatars = inheritedAvatars,
                        ModularAvatarComponents = inheritedModularAvatar,
                        VrcFuryComponents = inheritedVrcFury
                    };
                }
            }
        }
        return manifest;
    }

    public static string NormalizeDocumentHeaders(string yaml)
    {
        if (yaml == null) throw new ArgumentNullException(nameof(yaml));
        return StrippedHeaderPattern.Replace(yaml, "$1");
    }

    public static AvatarPackageManifest ParseText(string yaml, string sourcePath = "")
    {
        if (yaml == null) throw new ArgumentNullException(nameof(yaml));

        var documents = SplitDocuments(yaml);
        var gameObjectNames = new Dictionary<long, string>();
        foreach (var document in documents.Where(d => d.ClassId == 1))
        {
            gameObjectNames.TryAdd(document.FileId, ReadScalar(document.Lines, "m_Name") ?? "");
        }

        var avatars = new List<VrcAvatarDefinition>();
        var modularAvatarComponents = new List<ModularAvatarComponentDefinition>();
        var vrcFuryComponents = new List<VrcFuryComponentDefinition>();

        foreach (var document in documents.Where(document => document.ClassId == 114))
        {
            var script = ReadReference(document.Lines, "m_Script");
            if (script == null || !script.HasGuid) continue;

            var gameObject = ReadReference(document.Lines, "m_GameObject");
            long gameObjectFileId = gameObject?.FileId ?? 0;
            gameObjectNames.TryGetValue(gameObjectFileId, out string gameObjectName);

            var menu = ReadReference(document.Lines, "expressionsMenu");
            var parameters = ReadReference(document.Lines, "expressionParameters");
            bool hasDescriptorShape = menu != null && parameters != null;
            // The GUID identifies the SDK assembly, not a component type. Seats
            // and other SDK behaviours share it; the local file ID selects the type.
            bool knownDescriptor = script.Guid.Equals(VrcAvatarDescriptorScriptGuid, StringComparison.OrdinalIgnoreCase) &&
                script.FileId == VrcAvatarDescriptorScriptFileId;
            if (knownDescriptor || hasDescriptorShape)
            {
                avatars.Add(new VrcAvatarDefinition
                {
                    SourcePath = sourcePath,
                    ComponentFileId = document.FileId,
                    GameObjectFileId = gameObjectFileId,
                    GameObjectName = gameObjectName ?? "",
                    ScriptGuid = script.Guid,
                    ViewPosition = ReadPosition(document.Lines, "ViewPosition"),
                    ExpressionsMenu = menu,
                    ExpressionParameters = parameters,
                    BaseAnimationLayers = ReadAnimationLayers(document.Lines)
                });
            }

            if (RecognizedVrcFuryScriptGuids.Contains(script.Guid) || HasVrcFuryStructuralSignature(document.Lines))
            {
                var (armatureLinks, unsupportedFeatures) = ReadVrcFuryFeatures(document.Lines, sourcePath, document.FileId, gameObjectFileId, gameObjectName);
                string summary = ReadScalar(document.Lines, "content");
                if (string.IsNullOrEmpty(summary) && armatureLinks.Count > 0)
                {
                    summary = "ArmatureLink";
                }
                vrcFuryComponents.Add(new VrcFuryComponentDefinition
                {
                    SourcePath = sourcePath,
                    ComponentFileId = document.FileId,
                    GameObjectFileId = gameObjectFileId,
                    GameObjectName = gameObjectName ?? "",
                    ScriptGuid = script.Guid,
                    FeatureSummary = summary ?? "",
                    ArmatureLinks = armatureLinks,
                    UnsupportedFeatures = unsupportedFeatures
                });
            }

            if (!ModularAvatarScripts.TryGetValue(script.Guid, out var kind)) continue;

            modularAvatarComponents.Add(new ModularAvatarComponentDefinition
            {
                SourcePath = sourcePath,
                ComponentFileId = document.FileId,
                GameObjectFileId = gameObjectFileId,
                GameObjectName = gameObjectName ?? "",
                ScriptGuid = script.Guid,
                Kind = kind,
                MergeTargetPath = ReadNestedScalar(document.Lines, "mergeTarget", "referencePath") ?? "",
                MergeTargetObjectFileId = ReadNestedReference(document.Lines, "mergeTarget", "targetObject")?.FileId,
                ArmatureRootFileId = ReadReference(document.Lines, "armatureRoot")?.FileId,
                Prefix = ReadScalar(document.Lines, "prefix") ?? "",
                Suffix = ReadScalar(document.Lines, "suffix") ?? "",
                LockMode = ReadInt(document.Lines, "LockMode"),
                MangleNames = ReadInt(document.Lines, "mangleNames") != 0,
                BindingCount = CountSequenceEntries(document.Lines, "Bindings"),
                BoneReference = ReadInt(document.Lines, "boneReference", -1),
                BoneProxySubPath = ReadScalar(document.Lines, "subPath") ?? "",
                BoneProxyAttachmentMode = ReadInt(document.Lines, "attachmentMode"),
                BoneProxyMatchScale = ReadInt(document.Lines, "matchScale") != 0
            });
        }

        return new AvatarPackageManifest
        {
            SourcePath = sourcePath,
            Avatars = avatars,
            ModularAvatarComponents = modularAvatarComponents,
            VrcFuryComponents = vrcFuryComponents
        };
    }

    private static IReadOnlyList<UnityYamlDocument> SplitDocuments(string yaml)
    {
        var result = new List<UnityYamlDocument>();
        UnityYamlDocument current = null;

        using var reader = new StringReader(yaml);
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            var header = HeaderPattern.Match(line);
            if (header.Success)
            {
                if (current != null) result.Add(current);
                current = new UnityYamlDocument
                {
                    ClassId = int.Parse(header.Groups["class"].Value, CultureInfo.InvariantCulture),
                    FileId = long.Parse(header.Groups["id"].Value, CultureInfo.InvariantCulture)
                };
                continue;
            }

            current?.Lines.Add(line);
        }

        if (current != null) result.Add(current);
        return result;
    }

    private static int FindField(IReadOnlyList<string> lines, string field)
    {
        string prefix = "  " + field + ":";
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    private static string ReadScalar(IReadOnlyList<string> lines, string field)
    {
        int index = FindField(lines, field);
        if (index < 0) return null;
        int colon = lines[index].IndexOf(':');
        string value = colon >= 0 ? lines[index].Substring(colon + 1).Trim() : "";
        return Unquote(value);
    }

    private static int ReadInt(IReadOnlyList<string> lines, string field, int fallback = 0)
    {
        return int.TryParse(ReadScalar(lines, field), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : fallback;
    }

    private static UnityPosition? ReadPosition(IReadOnlyList<string> lines, string field)
    {
        int index = FindField(lines, field);
        if (index < 0) return null;
        int colon = lines[index].IndexOf(':');
        string value = colon >= 0 ? lines[index].Substring(colon + 1).Trim() : "";
        float[] coordinates = new float[3];
        string[] axes = { "x", "y", "z" };

        if (!string.IsNullOrEmpty(value) && value.Contains('{'))
        {
            for (int i = 0; i < axes.Length; i++)
            {
                var match = Regex.Match(value, @"(?:\{|,)\s*" + axes[i] + @":\s*(?<value>[^,}]+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                if (!match.Success || !float.TryParse(match.Groups["value"].Value.Trim(),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out coordinates[i]) || !float.IsFinite(coordinates[i]))
                    return null;
            }
            return new UnityPosition(coordinates[0], coordinates[1], coordinates[2]);
        }

        int parentIndent = CountIndent(lines[index]);
        bool[] found = new bool[3];
        for (int i = index + 1; i < lines.Count; i++)
        {
            int indent = CountIndent(lines[i]);
            if (lines[i].Length > 0 && indent <= parentIndent) break;
            string trimmed = lines[i].Trim();
            for (int a = 0; a < axes.Length; a++)
            {
                string axisPrefix = axes[a] + ":";
                if (trimmed.StartsWith(axisPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    string raw = trimmed.Substring(axisPrefix.Length).Trim();
                    if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out coordinates[a]) &&
                        float.IsFinite(coordinates[a]))
                    {
                        found[a] = true;
                    }
                    break;
                }
            }
        }
        if (found[0] && found[1] && found[2])
            return new UnityPosition(coordinates[0], coordinates[1], coordinates[2]);

        return null;
    }

    private static UnityAssetReference ReadReference(IReadOnlyList<string> lines, string field)
    {
        int index = FindField(lines, field);
        return index < 0 ? null : ReadReferenceAt(lines, index);
    }

    private static UnityAssetReference ReadReferenceAt(IReadOnlyList<string> lines, int index)
    {
        string text = lines[index].Trim();
        int startingIndent = CountIndent(lines[index]);
        for (int i = index + 1; i < lines.Count && text.Contains('{') && !text.Contains('}'); i++)
        {
            if (CountIndent(lines[i]) <= startingIndent) break;
            text += " " + lines[i].Trim();
        }

        var fileIdMatch = FileIdPattern.Match(text);
        if (!fileIdMatch.Success) return null;

        var guidMatch = GuidPattern.Match(text);
        var typeMatch = TypePattern.Match(text);
        return new UnityAssetReference
        {
            FileId = long.Parse(fileIdMatch.Groups["value"].Value, CultureInfo.InvariantCulture),
            Guid = guidMatch.Success ? guidMatch.Groups["value"].Value : "",
            Type = typeMatch.Success ? int.Parse(typeMatch.Groups["value"].Value, CultureInfo.InvariantCulture) : 0
        };
    }

    private static string ReadNestedScalar(IReadOnlyList<string> lines, string parentField, string field)
    {
        int parent = FindField(lines, parentField);
        if (parent < 0) return null;
        int parentIndent = CountIndent(lines[parent]);
        string prefix = new string(' ', parentIndent + 2) + field + ":";
        for (int i = parent + 1; i < lines.Count; i++)
        {
            int indent = CountIndent(lines[i]);
            if (lines[i].Length > 0 && indent <= parentIndent) break;
            if (lines[i].StartsWith(prefix, StringComparison.Ordinal))
                return Unquote(lines[i].Substring(prefix.Length).Trim());
        }
        return null;
    }

    private static UnityAssetReference ReadNestedReference(IReadOnlyList<string> lines, string parentField, string field)
    {
        int parent = FindField(lines, parentField);
        if (parent < 0) return null;
        int parentIndent = CountIndent(lines[parent]);
        string prefix = new string(' ', parentIndent + 2) + field + ":";
        for (int i = parent + 1; i < lines.Count; i++)
        {
            int indent = CountIndent(lines[i]);
            if (lines[i].Length > 0 && indent <= parentIndent) break;
            if (lines[i].StartsWith(prefix, StringComparison.Ordinal)) return ReadReferenceAt(lines, i);
        }
        return null;
    }

    private static IReadOnlyList<VrcAnimationLayerReference> ReadAnimationLayers(IReadOnlyList<string> lines)
    {
        int start = FindField(lines, "baseAnimationLayers");
        if (start < 0) return Array.Empty<VrcAnimationLayerReference>();

        var result = new List<VrcAnimationLayerReference>();
        int? type = null;
        bool enabled = false;
        UnityAssetReference controller = null;

        void Flush()
        {
            if (!type.HasValue) return;
            result.Add(new VrcAnimationLayerReference
            {
                LayerType = type.Value,
                Enabled = enabled,
                AnimatorController = controller
            });
            type = null;
            enabled = false;
            controller = null;
        }

        for (int i = start + 1; i < lines.Count; i++)
        {
            string line = lines[i];
            int indent = CountIndent(line);
            if (line.Length > 0 && indent <= 2 && !line.StartsWith("  - ", StringComparison.Ordinal)) break;

            if (line.StartsWith("  - ", StringComparison.Ordinal))
            {
                Flush();
                string firstField = line.Substring(4);
                if (firstField.StartsWith("isEnabled:", StringComparison.Ordinal))
                    enabled = ParseInt(firstField.Substring("isEnabled:".Length)) != 0;
                continue;
            }

            if (indent != 4) continue;
            string trimmed = line.Trim();
            if (trimmed.StartsWith("isEnabled:", StringComparison.Ordinal))
                enabled = ParseInt(trimmed.Substring("isEnabled:".Length)) != 0;
            else if (trimmed.StartsWith("type:", StringComparison.Ordinal))
                type = ParseInt(trimmed.Substring("type:".Length));
            else if (trimmed.StartsWith("animatorController:", StringComparison.Ordinal))
                controller = ReadReferenceAt(lines, i);
        }

        Flush();
        return result;
    }

    private static int CountSequenceEntries(IReadOnlyList<string> lines, string field)
    {
        int start = FindField(lines, field);
        if (start < 0) return 0;
        int count = 0;
        for (int i = start + 1; i < lines.Count; i++)
        {
            int indent = CountIndent(lines[i]);
            if (lines[i].Length > 0 && indent <= 2 && !lines[i].StartsWith("  - ", StringComparison.Ordinal)) break;
            if (lines[i].StartsWith("  - ", StringComparison.Ordinal)) count++;
        }
        return count;
    }

    private static int ParseInt(string value) =>
        int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : 0;

    private static int CountIndent(string line)
    {
        int count = 0;
        while (count < line.Length && line[count] == ' ') count++;
        return count;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && ((value[0] == '\'' && value[^1] == '\'') || (value[0] == '"' && value[^1] == '"')))
            return value.Substring(1, value.Length - 2);
        return value;
    }

    public static bool HasVrcFuryStructuralSignature(IReadOnlyList<string> lines)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            string trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("vrcfury:", StringComparison.OrdinalIgnoreCase))
                return true;
            if (trimmed.StartsWith("type:", StringComparison.Ordinal) &&
                (trimmed.Contains("asm: VRCFury", StringComparison.Ordinal) || trimmed.Contains("ns: VF.", StringComparison.Ordinal)))
                return true;
        }
        return false;
    }

    private static (List<VrcFuryArmatureLinkDefinition> ArmatureLinks, List<string> UnsupportedFeatures) ReadVrcFuryFeatures(
        IReadOnlyList<string> lines, string sourcePath, long componentFileId, long gameObjectFileId, string gameObjectName)
    {
        var armatureLinks = new List<VrcFuryArmatureLinkDefinition>();
        var unsupportedFeatures = new List<string>();

        // 1. Look for references.RefIds section (Unity [SerializeReference])
        int refIdsIndex = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            string trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("RefIds:", StringComparison.Ordinal))
            {
                refIdsIndex = i;
                break;
            }
        }

        if (refIdsIndex >= 0)
        {
            int i = refIdsIndex + 1;
            while (i < lines.Count)
            {
                string line = lines[i];
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("- rid:", StringComparison.Ordinal))
                {
                    int refIndent = CountIndent(line);
                    var entryLines = new List<string> { line };
                    i++;
                    while (i < lines.Count)
                    {
                        if (lines[i].Length > 0 && CountIndent(lines[i]) <= refIndent && lines[i].TrimStart().StartsWith("- rid:", StringComparison.Ordinal))
                            break;
                        if (lines[i].Length > 0 && CountIndent(lines[i]) < refIndent)
                            break;
                        entryLines.Add(lines[i]);
                        i++;
                    }

                    string typeLine = entryLines.FirstOrDefault(l => l.TrimStart().StartsWith("type:", StringComparison.Ordinal));
                    if (typeLine != null)
                    {
                        if (typeLine.Contains("ArmatureLink", StringComparison.OrdinalIgnoreCase))
                        {
                            var link = ParseArmatureLinkFromLines(entryLines, sourcePath, componentFileId, gameObjectFileId, gameObjectName);
                            if (link != null) armatureLinks.Add(link);
                        }
                        else
                        {
                            var match = Regex.Match(typeLine, @"class:\s*([^,}\s]+)", RegexOptions.CultureInvariant);
                            string className = match.Success ? match.Groups[1].Value : "UnknownFeature";
                            unsupportedFeatures.Add(className);
                        }
                    }
                    continue;
                }
                i++;
            }
        }

        // 2. Also check if the MonoBehaviour directly contains ArmatureLink fields (inline or legacy without RefIds)
        if (armatureLinks.Count == 0)
        {
            if (FindField(lines, "propBone") >= 0 || FindField(lines, "linkTo") >= 0)
            {
                var link = ParseArmatureLinkFromLines(lines, sourcePath, componentFileId, gameObjectFileId, gameObjectName);
                if (link != null) armatureLinks.Add(link);
            }
        }

        return (armatureLinks, unsupportedFeatures);
    }

    private static VrcFuryArmatureLinkDefinition ParseArmatureLinkFromLines(
        IReadOnlyList<string> lines, string sourcePath, long componentFileId, long gameObjectFileId, string gameObjectName)
    {
        int fieldIndent = lines.Where(line => !string.IsNullOrWhiteSpace(line)).Select(CountIndent).DefaultIfEmpty(0).Min();
        var fieldLines = lines.Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Substring(fieldIndent));
        var parseDiagnostics = new List<(string Code, string Message, bool IsError)>();
        YamlMappingNode fields = null;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(string.Join("\n", fieldLines)));
            var root = stream.Documents.FirstOrDefault()?.RootNode;
            if (root is YamlSequenceNode entries) root = entries.Children.FirstOrDefault();
            if (root is YamlMappingNode mapping)
            {
                if (mapping.Children.TryGetValue(new YamlScalarNode("data"), out var data)) fields = data as YamlMappingNode;
                else if (mapping.Children.TryGetValue(new YamlScalarNode("MonoBehaviour"), out var behaviour)) fields = behaviour as YamlMappingNode;
                else fields = mapping;
            }
            if (fields == null)
                parseDiagnostics.Add(("VRCFURY_INVALID_ARMATURE_DATA", "ArmatureLink data must be a mapping.", true));
        }
        catch (YamlException exception)
        {
            parseDiagnostics.Add(("VRCFURY_INVALID_ARMATURE_DATA", "ArmatureLink YAML could not be decoded: " + exception.Message, true));
        }
        var scalars = new Dictionary<string, string>(StringComparer.Ordinal);
        if (fields != null)
            foreach (var field in fields.Children)
                if (field.Key is YamlScalarNode key && field.Value is YamlScalarNode value && key.Value != null)
                    scalars[key.Value] = value.Value ?? "";
        var semantics = VrcFuryArmatureSemantics.Normalize(scalars);
        parseDiagnostics.AddRange(semantics.Diagnostics);
        long? propBoneFileId = null;
        if (fields != null && fields.Children.TryGetValue(new YamlScalarNode("propBone"), out var propNode) &&
            propNode is YamlMappingNode prop && long.TryParse(ReadTargetScalar(prop, "fileID"), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out long propId) && propId != 0)
            propBoneFileId = propId;
        var linkToTargets = new List<VrcFuryLinkToTarget>();
        if (semantics.Version < 6 && semantics.Version >= 0)
        {
            string legacyPath = ReadTargetScalar(fields, "bonePathOnAvatar") ?? "";
            if (!string.IsNullOrWhiteSpace(legacyPath))
                linkToTargets.Add(new VrcFuryLinkToTarget { Offset = legacyPath });
            else
            {
                string boneText = ReadTargetScalar(fields, "boneOnAvatar");
                int bone = boneText == null ? 0 : int.TryParse(boneText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedBone) ? parsedBone : -1;
                linkToTargets.Add(new VrcFuryLinkToTarget { UseBone = true, Bone = bone });
                if (fields != null && fields.Children.TryGetValue(new YamlScalarNode("fallbackBones"), out var fallbackNode) && fallbackNode is YamlSequenceNode fallbacks)
                    foreach (var fallback in fallbacks.Children)
                        linkToTargets.Add(new VrcFuryLinkToTarget { UseBone = true,
                            Bone = fallback is YamlScalarNode scalar && int.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int fallbackBone) ? fallbackBone : -1 });
            }
        }
        else if (fields != null && fields.Children.TryGetValue(new YamlScalarNode("linkTo"), out var targets))
        {
            if (targets is YamlSequenceNode sequence)
            {
                foreach (var entry in sequence.Children)
                {
                    if (entry is not YamlMappingNode mapping ||
                        !IsTargetBoolean(mapping, "useBone") || !IsTargetBoolean(mapping, "useObj"))
                    {
                        linkToTargets.Add(new VrcFuryLinkToTarget { UseBone = true, Bone = -1 });
                        continue;
                    }
                    YamlMappingNode obj = null;
                    if (mapping.Children.TryGetValue(new YamlScalarNode("obj"), out var objNode)) obj = objNode as YamlMappingNode;
                    string boneText = ReadTargetScalar(mapping, "bone");
                    linkToTargets.Add(new VrcFuryLinkToTarget
                    {
                        UseBone = ReadTargetScalar(mapping, "useBone") == null || ReadTargetBool(mapping, "useBone"),
                        Bone = boneText == null ? 0 : int.TryParse(boneText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int bone) ? bone : -1,
                        UseObj = ReadTargetBool(mapping, "useObj"),
                        ObjFileId = long.TryParse(ReadTargetScalar(obj, "fileID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) && id != 0 ? id : null,
                        Offset = ReadTargetScalar(mapping, "offset") ?? ""
                    });
                }
            }
            else parseDiagnostics.Add(("VRCFURY_INVALID_LINK_TARGETS", "ArmatureLink linkTo must be a sequence.", true));
        }
        else linkToTargets.Add(new VrcFuryLinkToTarget { UseBone = true });
        return new VrcFuryArmatureLinkDefinition
        {
            SourcePath = sourcePath,
            ComponentFileId = componentFileId,
            GameObjectFileId = gameObjectFileId,
            GameObjectName = gameObjectName,
            PropBoneFileId = propBoneFileId,
            LinkToTargets = linkToTargets.AsReadOnly(),
            SerializedVersion = semantics.Version,
            LinkMode = semantics.LinkMode,
            Recursive = semantics.Recursive,
            RemoveBoneSuffix = ReadTargetScalar(fields, "removeBoneSuffix") ?? "",
            AlignPosition = semantics.AlignPosition,
            AlignRotation = semantics.AlignRotation,
            AlignScale = semantics.AlignScale,
            AutoScaleFactor = semantics.AutoScaleFactor,
            KeepOffsets = !semantics.AlignPosition || !semantics.AlignRotation || !semantics.AlignScale,
            SemanticDiagnostics = parseDiagnostics.AsReadOnly()
        };
    }

    private static string ReadTargetScalar(YamlMappingNode mapping, string key) =>
        mapping != null && mapping.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlScalarNode scalar
            ? scalar.Value : null;

    private static bool ReadTargetBool(YamlMappingNode mapping, string key)
    {
        string value = ReadTargetScalar(mapping, key);
        return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTargetBoolean(YamlMappingNode mapping, string key)
    {
        if (!mapping.Children.TryGetValue(new YamlScalarNode(key), out var node)) return true;
        return node is YamlScalarNode scalar && (scalar.Value is "0" or "1" || bool.TryParse(scalar.Value, out _));
    }

    private sealed class UnityYamlDocument
    {
        public int ClassId { get; init; }
        public long FileId { get; init; }
        public List<string> Lines { get; } = new();
    }
}
