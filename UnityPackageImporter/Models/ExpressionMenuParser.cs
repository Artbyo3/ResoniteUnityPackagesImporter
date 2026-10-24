using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace UnityPackageImporter.Models;

public class VrcMenuControl
{
    public string Name = "";
    public string IconGuid = null;
    public int Type = 102; // 0/101: Button, 1/102: Toggle, 2/103: SubMenu, 3/104: TwoAxis, 4/105: Radial
    public string ParameterName = "";
    public float Value = 1f;
    public string SubMenuGuid = null;
    public VrcMenu SubMenu = null;
    public IReadOnlyList<string> SubParameters = Array.Empty<string>();
    public IReadOnlyList<string> Labels = Array.Empty<string>();

    public bool IsToggle => Type == 1 || Type == 102;
    public bool IsSubMenu => Type == 2 || Type == 103;
    public bool IsButton => Type == 0 || Type == 101;
}

public class VrcMenu
{
    public string Name = "";
    public string Guid = "";
    public string FilePath = "";
    public List<VrcMenuControl> Controls = new();
}

public class VrcParameter
{
    public string Name = "";
    public int ValueType = 2; // 0: Int, 1: Float, 2: Bool
    public float DefaultValue = 0f;
    public bool Saved = true;
    public bool Synced = true;
    public bool HasDefaultValue;
}

public static class ExpressionMenuParser
{
    public static Dictionary<string, VrcParameter> ParseParameters(string filePath)
    {
        if (!File.Exists(filePath)) return new(StringComparer.Ordinal);
        return ParseParameterYaml(File.ReadAllText(filePath));
    }

    internal static Dictionary<string, VrcParameter> ParseParameterYaml(string yaml, CancellationToken token = default)
    {
        var body = ParseAssetBody(yaml, token);
        if (Get(body, "parameters") is not YamlSequenceNode entries)
            throw new InvalidDataException("Expression parameters require one unambiguous serialized parameter asset.");
        var result = new Dictionary<string, VrcParameter>(StringComparer.Ordinal);
        foreach (var node in entries.Children)
        {
            token.ThrowIfCancellationRequested();
            if (node is not YamlMappingNode entry || Get(entry, "name") is not YamlScalarNode name ||
                string.IsNullOrWhiteSpace(name.Value) || name.Value.Contains('\0'))
                throw new InvalidDataException("Expression parameter requires an exact scalar name.");
            if (Get(entry, "valueType") is not YamlScalarNode typeNode ||
                !int.TryParse(typeNode.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int type) || type is < 0 or > 2)
                throw new InvalidDataException("Expression parameter requires a known value type.");
            float value = 0;
            var valueNode = Get(entry, "defaultValue");
            if (valueNode != null && (valueNode is not YamlScalarNode scalar ||
                !float.TryParse(scalar.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || !float.IsFinite(value)))
                throw new InvalidDataException("Expression parameter default must be finite.");
            if (type == 0 && (value < 0 || value > 255 || value != MathF.Truncate(value)) ||
                type == 1 && (value < -1 || value > 1) || type == 2 && value is not (0 or 1))
                throw new InvalidDataException("Expression parameter default is outside its declared SDK type.");
            var parameter = new VrcParameter
            {
                Name = name.Value, ValueType = type, DefaultValue = value, HasDefaultValue = valueNode != null,
                Saved = Flag(entry, "saved", true), Synced = Flag(entry, "networkSynced", true)
            };
            if (!result.TryAdd(parameter.Name, parameter)) throw new InvalidDataException("Duplicate expression parameter name: " + parameter.Name);
        }
        return result;
    }

    private static YamlNode Get(YamlMappingNode node, string key) => node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    private static bool Flag(YamlMappingNode node, string key, bool fallback)
    {
        var value = Get(node, key);
        if (value == null) return fallback;
        if (value is not YamlScalarNode scalar || scalar.Value is not ("0" or "1" or "false" or "true"))
            throw new InvalidDataException("Expression parameter flag must be a boolean: " + key);
        return scalar.Value is "1" or "true";
    }

    public static VrcMenu ParseMenu(string filePath, string guid)
    {
        if (!File.Exists(filePath)) return null;
        return ParseMenuYaml(File.ReadAllText(filePath), filePath, guid);
    }

    private static YamlMappingNode ParseAssetBody(string yaml, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        token.ThrowIfCancellationRequested();
        var normalized = Regex.Replace(yaml, @"^%(?:YAML|TAG)[^\r\n]*(?:\r?\n|$)", "", RegexOptions.Multiline | RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"^--- !u!\d+ &-?\d+(?: stripped)?[ \t]*\r?$", "---", RegexOptions.Multiline | RegexOptions.CultureInvariant);
        var stream = new YamlStream();
        try { stream.Load(new StringReader(normalized)); }
        catch (YamlException error) { throw new InvalidDataException("Malformed expression asset YAML.", error); }
        token.ThrowIfCancellationRequested();
        if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode root ||
            Get(root, "MonoBehaviour") is not YamlMappingNode body)
            throw new InvalidDataException("Expressions require one unambiguous serialized asset.");
        return body;
    }

    internal static VrcMenu ParseMenuYaml(string yaml, string filePath, string guid, CancellationToken token = default)
    {
        var body = ParseAssetBody(yaml, token);
        if (Get(body, "controls") is not YamlSequenceNode controls)
            throw new InvalidDataException("Expression menu requires an explicit controls sequence.");
        string Scalar(YamlMappingNode mapping, string key, string fallback = "")
        {
            var node = Get(mapping, key);
            if (node == null) return fallback;
            if (node is not YamlScalarNode scalar || scalar.Value?.Contains('\0') == true)
                throw new InvalidDataException("Expression menu requires a scalar " + key + ".");
            return scalar.Value ?? "";
        }
        string Parameter(YamlNode node)
        {
            if (node == null) return "";
            if (node is not YamlMappingNode mapping)
                throw new InvalidDataException("Expression menu parameter must be a named record.");
            return Scalar(mapping, "name");
        }
        string Reference(YamlNode node)
        {
            if (node == null) return null;
            if (node is not YamlMappingNode mapping)
                throw new InvalidDataException("Expression menu reference must be a record.");
            string value = Scalar(mapping, "guid");
            if (value.Length == 0 || value == "0") return null;
            if (value.Length != 32 || !System.Linq.Enumerable.All(value, Uri.IsHexDigit))
                throw new InvalidDataException("Expression menu reference requires an exact asset GUID.");
            if (!long.TryParse(Scalar(mapping, "fileID", "0"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long fileId) || fileId == 0)
                throw new InvalidDataException("Expression menu external reference requires a nonzero fileID.");
            return value;
        }
        var menu = new VrcMenu
        {
            Guid = guid,
            FilePath = filePath,
            Name = Scalar(body, "m_Name", Path.GetFileNameWithoutExtension(filePath))
        };
        foreach (var node in controls.Children)
        {
            token.ThrowIfCancellationRequested();
            if (node is not YamlMappingNode entry ||
                !int.TryParse(Scalar(entry, "type"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int type))
                throw new InvalidDataException("Expression menu control requires an explicit numeric type.");
            if (!float.TryParse(Scalar(entry, "value", "1"), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value))
                throw new InvalidDataException("Expression menu control value must be finite.");
            var subParameters = new List<string>();
            if (Get(entry, "subParameters") is { } subNode)
            {
                if (subNode is not YamlSequenceNode sequence) throw new InvalidDataException("Puppet parameters must be a sequence.");
                foreach (var parameter in sequence.Children)
                {
                    token.ThrowIfCancellationRequested();
                    subParameters.Add(Parameter(parameter));
                }
            }
            var labels = new List<string>();
            if (Get(entry, "labels") is { } labelNode)
            {
                if (labelNode is not YamlSequenceNode sequence) throw new InvalidDataException("Puppet labels must be a sequence.");
                foreach (var label in sequence.Children)
                {
                    token.ThrowIfCancellationRequested();
                    if (label is not YamlMappingNode mapping) throw new InvalidDataException("Puppet label must be a record.");
                    labels.Add(Scalar(mapping, "name"));
                }
            }
            menu.Controls.Add(new VrcMenuControl
            {
                Name = Scalar(entry, "name"), Type = type, Value = value,
                ParameterName = Parameter(Get(entry, "parameter")),
                IconGuid = Reference(Get(entry, "icon")), SubMenuGuid = Reference(Get(entry, "subMenu")),
                SubParameters = subParameters.AsReadOnly(), Labels = labels.AsReadOnly()
            });
        }
        return menu;
    }

    public static string ExtractGuid(string line)
    {
        int idx = line.IndexOf("guid:", StringComparison.Ordinal);
        if (idx < 0) return null;
        string sub = line.Substring(idx + 5).Trim();
        int end = sub.IndexOfAny(new[] { ',', ' ', '}', '\r', '\n' });
        string guid = (end >= 0) ? sub.Substring(0, end).Trim() : sub.Trim();
        return string.IsNullOrEmpty(guid) || guid == "0" ? null : guid;
    }

    public static (VrcMenu RootMenu, Dictionary<string, VrcParameter> Parameters) LoadMenuHierarchy(
        string rootMenuGuid,
        string parametersGuid,
        IDictionary<string, string> assetIdDict)
    {
        var parameters = new Dictionary<string, VrcParameter>(StringComparer.Ordinal);
        if (TryResolveAsset(parametersGuid, assetIdDict, out string parametersPath) && File.Exists(parametersPath))
            parameters = ParseParameters(parametersPath);

        if (!TryResolveAsset(rootMenuGuid, assetIdDict, out string rootMenuPath) || !File.Exists(rootMenuPath))
            return (null, parameters);

        var loaded = new Dictionary<string, VrcMenu>(StringComparer.OrdinalIgnoreCase);
        var loading = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        VrcMenu Load(string guid)
        {
            if (string.IsNullOrWhiteSpace(guid)) return null;
            if (loading.Contains(guid)) return null;
            if (loaded.TryGetValue(guid, out var existing)) return existing;
            loading.Add(guid);
            if (!TryResolveAsset(guid, assetIdDict, out string path) || !File.Exists(path))
            {
                loading.Remove(guid);
                return null;
            }

            var menu = ParseMenu(path, guid);
            if (menu == null)
            {
                loading.Remove(guid);
                return null;
            }

            loaded[guid] = menu;
            foreach (var control in menu.Controls)
            {
                if (control.IsSubMenu && !string.IsNullOrWhiteSpace(control.SubMenuGuid))
                    control.SubMenu = Load(control.SubMenuGuid);
            }

            loading.Remove(guid);
            return menu;
        }

        return (Load(rootMenuGuid), parameters);
    }

    private static bool TryResolveAsset(string guid, IDictionary<string, string> assetIdDict, out string path)
    {
        path = null;
        if (string.IsNullOrWhiteSpace(guid) || assetIdDict == null) return false;
        if (assetIdDict.TryGetValue(guid, out path)) return !string.IsNullOrWhiteSpace(path);

        foreach (var pair in assetIdDict)
        {
            if (pair.Key.Equals(guid, StringComparison.OrdinalIgnoreCase))
            {
                path = pair.Value;
                return !string.IsNullOrWhiteSpace(path);
            }
        }

        return false;
    }

}
