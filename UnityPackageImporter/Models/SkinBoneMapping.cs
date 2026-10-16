using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UnityPackageImporter.Models;

internal static class SkinBoneMapping
{
    // Native ImportNode creates renderers before binding their bones. Prefer its
    // per-mesh Assimp-node map; it disambiguates equal names in different rigs.
    // Only older models without that map may use an unambiguous hierarchy lookup.
    internal static Dictionary<string, T> ResolveNative<T>(IEnumerable<string> meshBoneNames,
        IReadOnlyDictionary<string, T> nativeMapping, Func<IEnumerable<(string Name, T Bone)>> fallback) where T : class
    {
        var names = meshBoneNames.ToArray();
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        if (nativeMapping != null)
        {
            foreach (var name in names)
            {
                if (!nativeMapping.TryGetValue(name, out var bone) || bone == null)
                    throw new InvalidDataException("Native skin mapping has no target for '" + name + "'.");
                result[name] = bone;
            }
        }
        else if (names.Length > 0)
        {
            var bones = Resolve(names, fallback(), allowUnitySuffix: false);
            for (int i = 0; i < names.Length; i++) result[names[i]] = bones[i];
        }
        return result;
    }

    internal static void Validate<T>(int expectedCount, IEnumerable<T> bones, Func<T, bool> isValid) where T : class
    {
        var targets = bones.ToArray();
        if (targets.Length != expectedCount)
            throw new InvalidDataException($"Skin has {targets.Length} bone references; mesh requires {expectedCount}.");
        for (int i = 0; i < targets.Length; i++)
            if (targets[i] == null || !isValid(targets[i]))
                throw new InvalidDataException($"Skin bone {i} is missing, destroyed, or outside its imported hierarchy.");
    }

    /// <summary>
    /// Assimp and Unity can order bones differently. Resolve only within the
    /// renderer's authored file-ID references, rejecting missing/ambiguous names.
    /// </summary>
    internal static T[] Resolve<T>(IEnumerable<string> meshBoneNames, IEnumerable<(string Name, T Bone)> authored,
        bool allowUnitySuffix = true) where T : class
    {
        var candidates = authored.Where(item => item.Bone != null).ToArray();
        return meshBoneNames.Select(name =>
        {
            var matches = candidates.Where(item => item.Name == name).Select(item => item.Bone).Distinct().ToArray();
            // Unity may disambiguate a bone/mesh name collision with " 1".
            // Exact names always take precedence.
            if (matches.Length == 0 && allowUnitySuffix)
                matches = candidates.Where(item => item.Name == name + " 1").Select(item => item.Bone).Distinct().ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException(matches.Length == 0
                    ? "No authored skin bone matches '" + name + "'."
                    : "Several authored skin bones match '" + name + "'.");
            return matches[0];
        }).ToArray();
    }

    private static readonly Dictionary<string, string> UnityHumanoidToBodyNodeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Torso & Head
        ["Hips"] = "Hips",
        ["Spine"] = "Spine",
        ["Chest"] = "Chest",
        ["UpperChest"] = "UpperChest",
        ["Upper Chest"] = "UpperChest",
        ["Neck"] = "Neck",
        ["Head"] = "Head",
        ["LeftEye"] = "LeftEye",
        ["Left Eye"] = "LeftEye",
        ["RightEye"] = "RightEye",
        ["Right Eye"] = "RightEye",
        ["Jaw"] = "Jaw",

        // Arms & Legs
        ["LeftShoulder"] = "LeftShoulder",
        ["Left Shoulder"] = "LeftShoulder",
        ["LeftUpperArm"] = "LeftUpperArm",
        ["Left UpperArm"] = "LeftUpperArm",
        ["Left Upper Arm"] = "LeftUpperArm",
        ["LeftLowerArm"] = "LeftLowerArm",
        ["Left LowerArm"] = "LeftLowerArm",
        ["Left Lower Arm"] = "LeftLowerArm",
        ["LeftHand"] = "LeftHand",
        ["Left Hand"] = "LeftHand",
        ["RightShoulder"] = "RightShoulder",
        ["Right Shoulder"] = "RightShoulder",
        ["RightUpperArm"] = "RightUpperArm",
        ["Right UpperArm"] = "RightUpperArm",
        ["Right Upper Arm"] = "RightUpperArm",
        ["RightLowerArm"] = "RightLowerArm",
        ["Right LowerArm"] = "RightLowerArm",
        ["Right Lower Arm"] = "RightLowerArm",
        ["RightHand"] = "RightHand",
        ["Right Hand"] = "RightHand",
        ["LeftUpperLeg"] = "LeftUpperLeg",
        ["Left UpperLeg"] = "LeftUpperLeg",
        ["Left Upper Leg"] = "LeftUpperLeg",
        ["LeftLowerLeg"] = "LeftLowerLeg",
        ["Left LowerLeg"] = "LeftLowerLeg",
        ["Left Lower Leg"] = "LeftLowerLeg",
        ["LeftFoot"] = "LeftFoot",
        ["Left Foot"] = "LeftFoot",
        ["LeftToes"] = "LeftToes",
        ["Left Toes"] = "LeftToes",
        ["RightUpperLeg"] = "RightUpperLeg",
        ["Right UpperLeg"] = "RightUpperLeg",
        ["Right Upper Leg"] = "RightUpperLeg",
        ["RightLowerLeg"] = "RightLowerLeg",
        ["Right LowerLeg"] = "RightLowerLeg",
        ["Right Lower Leg"] = "RightLowerLeg",
        ["RightFoot"] = "RightFoot",
        ["Right Foot"] = "RightFoot",
        ["RightToes"] = "RightToes",
        ["Right Toes"] = "RightToes",

        // Left Hand Fingers (Unity HumanTrait.BoneName + compact variants + Metacarpals)
        ["Left Thumb Metacarpal"] = "LeftThumb_Metacarpal",
        ["LeftThumbMetacarpal"] = "LeftThumb_Metacarpal",
        ["Left Thumb Proximal"] = "LeftThumb_Proximal",
        ["LeftThumbProximal"] = "LeftThumb_Proximal",
        ["Left Thumb Intermediate"] = "LeftThumb_Intermediate",
        ["LeftThumbIntermediate"] = "LeftThumb_Intermediate",
        ["Left Thumb Distal"] = "LeftThumb_Distal",
        ["LeftThumbDistal"] = "LeftThumb_Distal",

        ["Left Index Metacarpal"] = "LeftIndexFinger_Metacarpal",
        ["LeftIndexMetacarpal"] = "LeftIndexFinger_Metacarpal",
        ["Left Index Proximal"] = "LeftIndexFinger_Proximal",
        ["LeftIndexProximal"] = "LeftIndexFinger_Proximal",
        ["Left Index Intermediate"] = "LeftIndexFinger_Intermediate",
        ["LeftIndexIntermediate"] = "LeftIndexFinger_Intermediate",
        ["Left Index Distal"] = "LeftIndexFinger_Distal",
        ["LeftIndexDistal"] = "LeftIndexFinger_Distal",

        ["Left Middle Metacarpal"] = "LeftMiddleFinger_Metacarpal",
        ["LeftMiddleMetacarpal"] = "LeftMiddleFinger_Metacarpal",
        ["Left Middle Proximal"] = "LeftMiddleFinger_Proximal",
        ["LeftMiddleProximal"] = "LeftMiddleFinger_Proximal",
        ["Left Middle Intermediate"] = "LeftMiddleFinger_Intermediate",
        ["LeftMiddleIntermediate"] = "LeftMiddleFinger_Intermediate",
        ["Left Middle Distal"] = "LeftMiddleFinger_Distal",
        ["LeftMiddleDistal"] = "LeftMiddleFinger_Distal",

        ["Left Ring Metacarpal"] = "LeftRingFinger_Metacarpal",
        ["LeftRingMetacarpal"] = "LeftRingFinger_Metacarpal",
        ["Left Ring Proximal"] = "LeftRingFinger_Proximal",
        ["LeftRingProximal"] = "LeftRingFinger_Proximal",
        ["Left Ring Intermediate"] = "LeftRingFinger_Intermediate",
        ["LeftRingIntermediate"] = "LeftRingFinger_Intermediate",
        ["Left Ring Distal"] = "LeftRingFinger_Distal",
        ["LeftRingDistal"] = "LeftRingFinger_Distal",

        ["Left Little Metacarpal"] = "LeftPinky_Metacarpal",
        ["LeftLittleMetacarpal"] = "LeftPinky_Metacarpal",
        ["Left Little Proximal"] = "LeftPinky_Proximal",
        ["LeftLittleProximal"] = "LeftPinky_Proximal",
        ["Left Little Intermediate"] = "LeftPinky_Intermediate",
        ["LeftLittleIntermediate"] = "LeftPinky_Intermediate",
        ["Left Little Distal"] = "LeftPinky_Distal",
        ["LeftLittleDistal"] = "LeftPinky_Distal",

        // Right Hand Fingers (Unity HumanTrait.BoneName + compact variants + Metacarpals)
        ["Right Thumb Metacarpal"] = "RightThumb_Metacarpal",
        ["RightThumbMetacarpal"] = "RightThumb_Metacarpal",
        ["Right Thumb Proximal"] = "RightThumb_Proximal",
        ["RightThumbProximal"] = "RightThumb_Proximal",
        ["Right Thumb Intermediate"] = "RightThumb_Intermediate",
        ["RightThumbIntermediate"] = "RightThumb_Intermediate",
        ["Right Thumb Distal"] = "RightThumb_Distal",
        ["RightThumbDistal"] = "RightThumb_Distal",

        ["Right Index Metacarpal"] = "RightIndexFinger_Metacarpal",
        ["RightIndexMetacarpal"] = "RightIndexFinger_Metacarpal",
        ["Right Index Proximal"] = "RightIndexFinger_Proximal",
        ["RightIndexProximal"] = "RightIndexFinger_Proximal",
        ["Right Index Intermediate"] = "RightIndexFinger_Intermediate",
        ["RightIndexIntermediate"] = "RightIndexFinger_Intermediate",
        ["Right Index Distal"] = "RightIndexFinger_Distal",
        ["RightIndexDistal"] = "RightIndexFinger_Distal",

        ["Right Middle Metacarpal"] = "RightMiddleFinger_Metacarpal",
        ["RightMiddleMetacarpal"] = "RightMiddleFinger_Metacarpal",
        ["Right Middle Proximal"] = "RightMiddleFinger_Proximal",
        ["RightMiddleProximal"] = "RightMiddleFinger_Proximal",
        ["Right Middle Intermediate"] = "RightMiddleFinger_Intermediate",
        ["RightMiddleIntermediate"] = "RightMiddleFinger_Intermediate",
        ["Right Middle Distal"] = "RightMiddleFinger_Distal",
        ["RightMiddleDistal"] = "RightMiddleFinger_Distal",

        ["Right Ring Metacarpal"] = "RightRingFinger_Metacarpal",
        ["RightRingMetacarpal"] = "RightRingFinger_Metacarpal",
        ["Right Ring Proximal"] = "RightRingFinger_Proximal",
        ["RightRingProximal"] = "RightRingFinger_Proximal",
        ["Right Ring Intermediate"] = "RightRingFinger_Intermediate",
        ["RightRingIntermediate"] = "RightRingFinger_Intermediate",
        ["Right Ring Distal"] = "RightRingFinger_Distal",
        ["RightRingDistal"] = "RightRingFinger_Distal",

        ["Right Little Metacarpal"] = "RightPinky_Metacarpal",
        ["RightLittleMetacarpal"] = "RightPinky_Metacarpal",
        ["Right Little Proximal"] = "RightPinky_Proximal",
        ["RightLittleProximal"] = "RightPinky_Proximal",
        ["Right Little Intermediate"] = "RightPinky_Intermediate",
        ["RightLittleIntermediate"] = "RightPinky_Intermediate",
        ["Right Little Distal"] = "RightPinky_Distal",
        ["RightLittleDistal"] = "RightPinky_Distal",
    };

    /// <summary>
    /// Normalizes a Unity HumanDescription <c>humanName</c> string into the corresponding
    /// Resonite <c>BodyNode</c> enum identifier string via an O(1) lookup table.
    /// </summary>
    internal static string NormalizeUnityHumanoidBoneName(string? boneNameHuman)
    {
        if (string.IsNullOrWhiteSpace(boneNameHuman))
            return string.Empty;

        string trimmed = boneNameHuman.Trim();
        if (UnityHumanoidToBodyNodeMap.TryGetValue(trimmed, out string? mapped))
            return mapped;

        return trimmed.Replace(" ", "");
    }
}
