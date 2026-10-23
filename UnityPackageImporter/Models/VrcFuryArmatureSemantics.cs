#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace UnityPackageImporter.Models;

internal static class VrcFuryArmatureSemantics
{
    internal sealed class Normalized
    {
        internal int Version { get; set; }
        internal int LinkMode { get; set; }
        internal bool Recursive { get; set; }
        internal bool AlignPosition { get; set; }
        internal bool AlignRotation { get; set; }
        internal bool AlignScale { get; set; }
        internal bool AutoScaleFactor { get; set; }
        internal List<(string Code, string Message, bool IsError)> Diagnostics { get; } = new();
    }

    internal static Normalized Normalize(IReadOnlyDictionary<string, string> fields)
    {
        var result = new Normalized();
        int Integer(string name, int fallback)
        {
            if (!fields.TryGetValue(name, out var text)) return fallback;
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)) return value;
            result.Diagnostics.Add(("VRCFURY_INVALID_FIELD", $"ArmatureLink {name} is not an integer.", true));
            return fallback;
        }
        bool Boolean(string name, bool fallback)
        {
            if (!fields.TryGetValue(name, out var text)) return fallback;
            if (text == "1" || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (text == "0" || string.Equals(text, "false", StringComparison.OrdinalIgnoreCase)) return false;
            result.Diagnostics.Add(("VRCFURY_INVALID_FIELD", $"ArmatureLink {name} is not a boolean.", true));
            return fallback;
        }
        result.Version = Integer("version", 0);
        result.LinkMode = Integer("linkMode", 4);
        result.Recursive = Boolean("recursive", false);
        result.AlignPosition = Boolean("alignPosition", false);
        result.AlignRotation = Boolean("alignRotation", false);
        result.AlignScale = Boolean("alignScale", false);
        result.AutoScaleFactor = Boolean("autoScaleFactor", true);
        if (Boolean("forceOneWorldScale", false))
            result.Diagnostics.Add(("VRCFURY_UNSUPPORTED_SCALE_OPTIONS",
                "ArmatureLink forceOneWorldScale overrides alignment and requires unit world scale; this scale policy is not implemented.", true));
        int keepOffsets = Integer("keepBoneOffsets2", 0);
        bool powersOfTen = Boolean("scalingFactorPowersOf10Only", true);
        float factor = 1;
        if (fields.TryGetValue("skinRewriteScalingFactor", out var factorText) &&
            (!float.TryParse(factorText, NumberStyles.Float, CultureInfo.InvariantCulture, out factor) || !float.IsFinite(factor)))
            result.Diagnostics.Add(("VRCFURY_INVALID_FIELD", "ArmatureLink skinRewriteScalingFactor is not finite.", true));
        if (result.Version is < 0 or > 7)
        {
            result.Diagnostics.Add(("VRCFURY_UNSUPPORTED_VERSION", $"ArmatureLink serialized version {result.Version} is unsupported (supported: 0 through 7).", true));
            return result;
        }
        if (result.LinkMode is < 0 or > 4)
            result.Diagnostics.Add(("VRCFURY_UNKNOWN_LINK_MODE", $"ArmatureLink linkMode {result.LinkMode} is unknown.", true));
        if (keepOffsets is < 0 or > 2)
            result.Diagnostics.Add(("VRCFURY_UNKNOWN_KEEP_OFFSETS", $"ArmatureLink keepBoneOffsets2 {keepOffsets} is unknown.", true));
        if (result.Version < 1) result.LinkMode = Boolean("useBoneMerging", false) ? 0 : 1;
        if (result.Version < 2) factor = 1;
        if (result.Version < 3) keepOffsets = Boolean("keepBoneOffsets", false) ? 1 : 2;
        if (result.Version < 4 && result.LinkMode != 0) factor = 0;
        if (result.Version < 5 && result.LinkMode == 1) powersOfTen = false;
        if (result.Version < 7)
        {
            if (result.LinkMode == 4)
                result.Diagnostics.Add(("VRCFURY_AUTO_REQUIRES_SKIN_ANALYSIS", "Legacy ArmatureLink Auto requires external skin-bone reference analysis; recursive behavior was not guessed.", true));
            else result.Recursive = result.LinkMode != 3;
            bool align = keepOffsets == 2 || (keepOffsets == 0 && result.Recursive);
            result.AlignPosition = result.AlignRotation = result.AlignScale = align;
            if (factor <= 0)
            {
                factor = 1;
                result.AutoScaleFactor = result.Recursive;
            }
            else result.AutoScaleFactor = false;
        }
        bool needsScaling = result.AlignScale && (result.AutoScaleFactor && result.Recursive || !result.AutoScaleFactor && factor != 1);
        if (needsScaling || fields.ContainsKey("skinRewriteScalingFactor") && factor != 1 || fields.ContainsKey("scalingFactorPowersOf10Only"))
            result.Diagnostics.Add(("VRCFURY_UNSUPPORTED_SCALE_OPTIONS",
                $"ArmatureLink scale options (skinRewriteScalingFactor={factor.ToString(CultureInfo.InvariantCulture)}, scalingFactorPowersOf10Only={powersOfTen}) are not implemented" +
                (needsScaling ? "; scale alignment cannot be applied faithfully." : "; they have no effect while scale alignment does not use them."), needsScaling));
        return result;
    }

    internal static (TPosition Position, TRotation Rotation, TScale Scale) SelectWorldTransform<TPosition, TRotation, TScale>(
        (TPosition Position, TRotation Rotation, TScale Scale) original,
        (TPosition Position, TRotation Rotation, TScale Scale) target,
        bool alignPosition, bool alignRotation, bool alignScale) =>
        (alignPosition ? target.Position : original.Position,
         alignRotation ? target.Rotation : original.Rotation,
         alignScale ? target.Scale : original.Scale);
}
