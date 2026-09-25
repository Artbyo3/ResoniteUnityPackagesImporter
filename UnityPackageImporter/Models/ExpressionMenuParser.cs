using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

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
}

public static class ExpressionMenuParser
{
    public static Dictionary<string, VrcParameter> ParseParameters(string filePath)
    {
        var result = new Dictionary<string, VrcParameter>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(filePath)) return result;

        var lines = File.ReadAllLines(filePath);
        bool inParameters = false;
        VrcParameter current = null;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("parameters:"))
            {
                inParameters = true;
                continue;
            }

            if (!inParameters) continue;

            if (line.StartsWith("- name:"))
            {
                if (current != null && !string.IsNullOrEmpty(current.Name))
                {
                    result[current.Name] = current;
                }
                current = new VrcParameter
                {
                    Name = line.Substring(7).Trim().Trim('\'', '\"')
                };
            }
            else if (current != null)
            {
                if (line.StartsWith("valueType:"))
                {
                    if (int.TryParse(line.Substring(10).Trim(), out int vt)) current.ValueType = vt;
                }
                else if (line.StartsWith("defaultValue:"))
                {
                    if (float.TryParse(line.Substring(13).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float dv))
                        current.DefaultValue = dv;
                }
                else if (line.StartsWith("saved:"))
                {
                    if (int.TryParse(line.Substring(6).Trim(), out int s)) current.Saved = s != 0;
                }
            }
        }

        if (current != null && !string.IsNullOrEmpty(current.Name))
        {
            result[current.Name] = current;
        }

        return result;
    }

    public static VrcMenu ParseMenu(string filePath, string guid)
    {
        if (!File.Exists(filePath)) return null;

        var lines = File.ReadAllLines(filePath);
        var menu = new VrcMenu
        {
            Guid = guid,
            FilePath = filePath,
            Name = Path.GetFileNameWithoutExtension(filePath)
        };

        bool inControls = false;
        VrcMenuControl current = null;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("controls:"))
            {
                inControls = true;
                continue;
            }

            if (!inControls) continue;

            if (line.StartsWith("- name:"))
            {
                if (current != null) menu.Controls.Add(current);
                current = new VrcMenuControl
                {
                    Name = line.Substring(7).Trim().Trim('\'', '\"')
                };
            }
            else if (current != null)
            {
                if (line.StartsWith("icon:"))
                {
                    current.IconGuid = ExtractGuid(line);
                }
                else if (line.StartsWith("type:"))
                {
                    if (int.TryParse(line.Substring(5).Trim(), out int t)) current.Type = t;
                }
                else if (line.StartsWith("name:") && string.IsNullOrEmpty(current.ParameterName))
                {
                    // under parameter:
                    current.ParameterName = line.Substring(5).Trim().Trim('\'', '\"');
                }
                else if (line.StartsWith("value:"))
                {
                    if (float.TryParse(line.Substring(6).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
                        current.Value = val;
                }
                else if (line.StartsWith("subMenu:"))
                {
                    current.SubMenuGuid = ExtractGuid(line);
                }
            }
        }

        if (current != null) menu.Controls.Add(current);
        return menu;
    }

    public static string ExtractGuid(string line)
    {
        int idx = line.IndexOf("guid:");
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
        var parameters = new Dictionary<string, VrcParameter>(StringComparer.OrdinalIgnoreCase);
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

    public static (VrcMenu RootMenu, Dictionary<string, VrcParameter> Parameters) DiscoverMenuHierarchy(
        IEnumerable<string> files,
        IDictionary<string, string> assetIdDict)
    {
        // 1. Invert assetIdDict to get path -> guid
        var pathToGuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in assetIdDict)
        {
            if (!string.IsNullOrEmpty(kvp.Value))
            {
                pathToGuid[kvp.Value] = kvp.Key;
            }
        }

        // 2. Scan for ExpressionsMenu and ExpressionParameters files
        var menusByGuid = new Dictionary<string, VrcMenu>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, VrcParameter> parameters = null;

        foreach (var file in files)
        {
            if (!file.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) continue;
            if (!File.Exists(file)) continue;

            string header = "";
            try
            {
                using var sr = new StreamReader(file);
                char[] buf = new char[500];
                int read = sr.Read(buf, 0, buf.Length);
                header = new string(buf, 0, read);
            }
            catch { continue; }

            if (header.Contains("MANUKA_ExpressionParameters") || header.Contains("parameters:") || header.Contains("m_EditorClassIdentifier:") && header.Contains("parameters"))
            {
                if (parameters == null || parameters.Count == 0)
                {
                    parameters = ParseParameters(file);
                }
            }

            if (header.Contains("controls:") || header.Contains("ExpressionsMenu"))
            {
                pathToGuid.TryGetValue(file, out string guid);
                var menu = ParseMenu(file, guid);
                if (menu != null && menu.Controls.Count > 0)
                {
                    if (!string.IsNullOrEmpty(guid))
                    {
                        menusByGuid[guid] = menu;
                    }
                    else
                    {
                        menusByGuid[menu.Name] = menu;
                    }
                }
            }
        }

        parameters ??= new Dictionary<string, VrcParameter>(StringComparer.OrdinalIgnoreCase);

        if (menusByGuid.Count == 0) return (null, parameters);

        // 3. Link submenus
        var referencedSubMenuGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var menu in menusByGuid.Values)
        {
            foreach (var control in menu.Controls)
            {
                if (control.IsSubMenu && !string.IsNullOrEmpty(control.SubMenuGuid))
                {
                    referencedSubMenuGuids.Add(control.SubMenuGuid);
                    if (menusByGuid.TryGetValue(control.SubMenuGuid, out var sub))
                    {
                        control.SubMenu = sub;
                    }
                }
            }
        }

        // 4. Root menu is the menu not referenced as a submenu by any other menu
        VrcMenu rootMenu = menusByGuid.Values.FirstOrDefault(m => !string.IsNullOrEmpty(m.Guid) && !referencedSubMenuGuids.Contains(m.Guid))
                           ?? menusByGuid.Values.FirstOrDefault();

        return (rootMenu, parameters);
    }
}
