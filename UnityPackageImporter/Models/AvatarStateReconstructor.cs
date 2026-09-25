using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
#if !REGRESSION_TESTS
using Elements.Core;
using FrooxEngine;
#endif

namespace UnityPackageImporter.Models;

public class AnimToggleCurve
{
    public string Path = "";
    public string Attribute = "";
    public float Value = 0f;
}

public class AnimClipData
{
    public string Name = "";
    public string FilePath = "";
    public List<AnimToggleCurve> Curves = new();
}

public static class AvatarStateReconstructor
{
    public static Dictionary<string, AnimClipData> ParseAllAnimationClips(IEnumerable<string> files)
    {
        var result = new Dictionary<string, AnimClipData>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            if (!file.EndsWith(".anim", StringComparison.OrdinalIgnoreCase)) continue;
            if (!File.Exists(file)) continue;

            var clip = ParseAnimationClip(file);
            if (clip != null && clip.Curves.Count > 0)
            {
                result[clip.Name] = clip;
            }
        }

        return result;
    }

    public static AnimClipData ParseAnimationClip(string filePath)
    {
        var clip = new AnimClipData
        {
            Name = Path.GetFileNameWithoutExtension(filePath),
            FilePath = filePath
        };

        var lines = File.ReadAllLines(filePath);
        string curPath = null;
        string curAttr = null;
        float? curVal = null;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("path:"))
            {
                curPath = line.Substring(5).Trim().Trim('\'', '\"');
            }
            else if (line.StartsWith("attribute:"))
            {
                curAttr = line.Substring(10).Trim();
            }
            else if (line.StartsWith("value:") && curVal == null)
            {
                string vStr = line.Substring(6).Trim();
                if (float.TryParse(vStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
                {
                    curVal = val;
                }
            }

            if (curPath != null && curAttr != null && curVal != null)
            {
                if (curAttr == "m_IsActive" || curAttr.StartsWith("blendShape.", StringComparison.OrdinalIgnoreCase))
                {
                    clip.Curves.Add(new AnimToggleCurve
                    {
                        Path = curPath,
                        Attribute = curAttr,
                        Value = curVal.Value
                    });
                }
                curPath = null;
                curAttr = null;
                curVal = null;
            }
        }

        return clip;
    }

    public static string NormalizeName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        return new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    public static List<string> TokenizeName(string name)
    {
        if (string.IsNullOrEmpty(name)) return new List<string>();
        var tokens = new List<string>();
        var cur = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (!char.IsLetterOrDigit(c))
            {
                if (cur.Length > 0)
                {
                    tokens.Add(cur.ToString().ToLowerInvariant());
                    cur.Clear();
                }
            }
            else if (char.IsUpper(c) && cur.Length > 0 && char.IsLower(name[i - 1]))
            {
                tokens.Add(cur.ToString().ToLowerInvariant());
                cur.Clear();
                cur.Append(c);
            }
            else
            {
                cur.Append(c);
            }
        }
        if (cur.Length > 0)
        {
            tokens.Add(cur.ToString().ToLowerInvariant());
        }

        return tokens.Where(t => t.Length >= 2).ToList();
    }

    private static readonly string[][] SynonymClusters = new[]
    {
        new[] { "arm", "hand", "top", "upper", "sleeve" },
        new[] { "leg", "foot", "bottom", "lower", "thigh", "knee", "ankle" },
        new[] { "warmer", "warmers" },
        new[] { "belt", "strap", "harness" },
        new[] { "chest", "breast", "bust", "waist", "body" },
        new[] { "underwear", "under", "panties", "bra", "nipless", "pants" },
        new[] { "shoe", "shoes", "boot", "boots", "sock", "socks" },
        new[] { "hair", "hairpin", "ahoge", "front", "back", "side", "tail", "tails" },
        new[] { "accessory", "acc", "earring", "pierce", "glasses", "goggles", "hat", "cap" },
    };

    private static HashSet<string> GetSynonymCluster(string token)
    {
        string tLower = token.ToLowerInvariant();
        foreach (var cluster in SynonymClusters)
        {
            if (cluster.Contains(tLower))
            {
                return new HashSet<string>(cluster, StringComparer.OrdinalIgnoreCase);
            }
        }
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase) { tLower };
    }

    public static List<string> ResolveSlotNamesForControl(
        string controlName,
        string parameterName,
        Dictionary<string, AnimClipData> clips,
        IEnumerable<string> candidateSlotNames)
    {
        var matchedSlotNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var slotList = candidateSlotNames?.ToList() ?? new List<string>();

        // 1. Animation Clip lookup (exact or fuzzy/token)
        AnimClipData matchedClip = null;
        if (clips != null)
        {
            if (clips.TryGetValue(controlName, out matchedClip) ||
                (!string.IsNullOrEmpty(parameterName) && clips.TryGetValue(parameterName, out matchedClip)))
            {
                // Exact match
            }
            else
            {
                // Fuzzy / substring / token match on clips
                string ctrlNorm = NormalizeName(controlName);
                var ctrlTokens = TokenizeName(controlName);

                foreach (var clip in clips.Values)
                {
                    string clipNorm = NormalizeName(clip.Name);
                    if (ctrlNorm.Length >= 3 && (clipNorm.Contains(ctrlNorm) || ctrlNorm.Contains(clipNorm)))
                    {
                        matchedClip = clip;
                        break;
                    }

                    if (ctrlTokens.Count >= 2)
                    {
                        bool allTokensMatch = ctrlTokens.All(t => clipNorm.Contains(t));
                        if (allTokensMatch)
                        {
                            matchedClip = clip;
                            break;
                        }
                    }
                }
            }
        }

        if (matchedClip != null)
        {
            foreach (var curve in matchedClip.Curves)
            {
                if (curve.Attribute == "m_IsActive")
                {
                    string targetName = Path.GetFileName(curve.Path);
                    var matchedCandidate = slotList.FirstOrDefault(s =>
                        s.Equals(targetName, StringComparison.OrdinalIgnoreCase) ||
                        s.StartsWith(targetName + " [", StringComparison.OrdinalIgnoreCase) ||
                        NormalizeName(s) == NormalizeName(targetName));

                    if (matchedCandidate != null)
                    {
                        matchedSlotNames.Add(matchedCandidate);
                    }
                }
            }
        }

        if (matchedSlotNames.Count > 0)
        {
            return matchedSlotNames.ToList();
        }

        // 2. Direct Slot Name Normalized match
        string ctrlNormalized = NormalizeName(controlName);
        if (ctrlNormalized.Length >= 3)
        {
            foreach (var slot in slotList)
            {
                string slotNormalized = NormalizeName(slot);
                if (slotNormalized.Length >= 3 &&
                    (slotNormalized.Contains(ctrlNormalized) || ctrlNormalized.Contains(slotNormalized)))
                {
                    matchedSlotNames.Add(slot);
                }
            }
        }

        if (matchedSlotNames.Count > 0)
        {
            return matchedSlotNames.ToList();
        }

        // 3. Token & Synonym Cluster Match
        var tokens = TokenizeName(controlName);
        if (tokens.Count > 0)
        {
            foreach (var slot in slotList)
            {
                var slotTokens = TokenizeName(slot);
                bool matchesAllTokens = true;

                foreach (var token in tokens)
                {
                    var cluster = GetSynonymCluster(token);
                    bool hasClusterMatch = slotTokens.Any(st => cluster.Any(syn =>
                        st.Equals(syn, StringComparison.OrdinalIgnoreCase) ||
                        (st.Length >= 4 && syn.Length >= 4 && (st.StartsWith(syn, StringComparison.OrdinalIgnoreCase) || syn.StartsWith(st, StringComparison.OrdinalIgnoreCase)))));
                    if (!hasClusterMatch)
                    {
                        matchesAllTokens = false;
                        break;
                    }
                }

                if (matchesAllTokens)
                {
                    matchedSlotNames.Add(slot);
                }
            }
        }

        return matchedSlotNames.ToList();
    }

#if !REGRESSION_TESTS
    public static Dictionary<string, List<string>> BuildParameterToSlotsMap(
        VrcMenu rootMenu,
        Dictionary<string, AnimClipData> clips,
        Slot avatarRoot)
    {
        var paramToSlots = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (rootMenu == null || avatarRoot == null) return paramToSlots;

        var candidateSlots = avatarRoot.GetAllChildren()
            .Where(c => c.GetComponent<MeshRenderer>() != null || c.GetComponent<FrooxEngine.SkinnedMeshRenderer>() != null)
            .Select(c => c.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        void TraverseMenu(VrcMenu menu)
        {
            if (menu == null) return;
            foreach (var control in menu.Controls)
            {
                if (control.IsSubMenu && control.SubMenu != null)
                {
                    TraverseMenu(control.SubMenu);
                }
                else if (control.IsToggle && !string.IsNullOrEmpty(control.ParameterName))
                {
                    if (!paramToSlots.ContainsKey(control.ParameterName))
                    {
                        var slots = ResolveSlotNamesForControl(control.Name, control.ParameterName, clips, candidateSlots);
                        if (slots.Count > 0)
                        {
                            paramToSlots[control.ParameterName] = slots;
                        }
                    }
                }
            }
        }

        TraverseMenu(rootMenu);
        return paramToSlots;
    }
#endif
}
