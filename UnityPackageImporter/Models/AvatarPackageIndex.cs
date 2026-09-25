using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

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
    public UnityAssetReference ExpressionsMenu { get; init; }
    public UnityAssetReference ExpressionParameters { get; init; }
    public IReadOnlyList<VrcAnimationLayerReference> BaseAnimationLayers { get; init; } = Array.Empty<VrcAnimationLayerReference>();

    public UnityAssetReference FxController =>
        BaseAnimationLayers.FirstOrDefault(layer => layer.LayerType == 5)?.AnimatorController;
}

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

public sealed class AvatarPackageManifest
{
    public string SourcePath { get; init; } = "";
    public IReadOnlyList<VrcAvatarDefinition> Avatars { get; init; } = Array.Empty<VrcAvatarDefinition>();
    public IReadOnlyList<ModularAvatarComponentDefinition> ModularAvatarComponents { get; init; } = Array.Empty<ModularAvatarComponentDefinition>();

    public bool IsAvatarPrefab => Avatars.Count > 0;

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
    public const string VrcAvatarDescriptorScriptGuid = "67cc4cb7839cd3741b63733d5adf0442";

    public const string MergeArmatureScriptGuid = "2df373bf91cf30b4bbd495e11cb1a2ec";
    public const string OutfitRootScriptGuid = "1895bf16884f4064f8e9550e7493c205";
    public const string BoneProxyScriptGuid = "42581d8044b64899834d3d515ab3a144";
    public const string BlendshapeSyncScriptGuid = "6fd7cab7d93b403280f2f9da978d8a4f";
    public const string MenuInstallerScriptGuid = "7ef83cb0c23d4d7c9d41021e544a1978";
    public const string ParametersScriptGuid = "71a96d4ea0c344f39e277d82035bf9bd";
    public const string ObjectToggleScriptGuid = "a162bb8ec7e24a5abcf457887f1df3fa";

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

    public static AvatarPackageManifest ParseFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("A prefab or scene path is required.", nameof(filePath));
        return ParseText(File.ReadAllText(filePath), filePath);
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
        var gameObjectNames = documents
            .Where(document => document.ClassId == 1)
            .ToDictionary(document => document.FileId, document => ReadScalar(document.Lines, "m_Name") ?? "");

        var avatars = new List<VrcAvatarDefinition>();
        var modularAvatarComponents = new List<ModularAvatarComponentDefinition>();

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
            if (script.Guid.Equals(VrcAvatarDescriptorScriptGuid, StringComparison.OrdinalIgnoreCase) || hasDescriptorShape)
            {
                avatars.Add(new VrcAvatarDefinition
                {
                    SourcePath = sourcePath,
                    ComponentFileId = document.FileId,
                    GameObjectFileId = gameObjectFileId,
                    GameObjectName = gameObjectName ?? "",
                    ScriptGuid = script.Guid,
                    ExpressionsMenu = menu,
                    ExpressionParameters = parameters,
                    BaseAnimationLayers = ReadAnimationLayers(document.Lines)
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
            ModularAvatarComponents = modularAvatarComponents
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
            if (lines[i].StartsWith(prefix, StringComparison.Ordinal)) return i;
        }
        return -1;
    }

    private static string ReadScalar(IReadOnlyList<string> lines, string field)
    {
        int index = FindField(lines, field);
        if (index < 0) return null;
        string value = lines[index].Substring(("  " + field + ":").Length).Trim();
        return Unquote(value);
    }

    private static int ReadInt(IReadOnlyList<string> lines, string field, int fallback = 0)
    {
        return int.TryParse(ReadScalar(lines, field), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : fallback;
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

    private sealed class UnityYamlDocument
    {
        public int ClassId { get; init; }
        public long FileId { get; init; }
        public List<string> Lines { get; } = new();
    }
}
