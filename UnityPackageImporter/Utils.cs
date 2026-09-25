using FrooxEngine;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace UnityPackageImporter;

internal static class Utils
{
    internal static bool ContainsUnicodeCharacter(string input)
    {
        const int MaxAnsiCode = 255;
        return input.Any(c => c > MaxAnsiCode);
    }

    internal static string GenerateMD5(string filepath)
    {
        // Credit to delta for this method https://github.com/XDelta/
        using var hasher = MD5.Create();
        using var stream = File.OpenRead(filepath);
        var hash = hasher.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", "");
    }

    internal static ColliderType GetColliderFromULong(ulong @ulong)
    {
        switch (@ulong)
        {
            case 1:
                return ColliderType.Trigger;
            default:
                return ColliderType.Static;
        }
    }

    internal static bool GetBoolFromULong(ulong IsEnabled)
    {
        return IsEnabled == 1;
    }

    internal static void AddRange<TKey, TValue>(this System.Collections.Generic.IDictionary<TKey, TValue> dict, System.Collections.Generic.IDictionary<TKey, TValue> other)
    {
        if (other == null) return;
        foreach (var kvp in other)
        {
            if (!dict.ContainsKey(kvp.Key))
            {
                dict.Add(kvp.Key, kvp.Value);
            }
        }
    }
}
