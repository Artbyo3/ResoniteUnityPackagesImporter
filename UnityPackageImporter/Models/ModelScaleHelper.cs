#nullable enable
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace UnityPackageImporter.Models;

public static class ModelScaleHelper
{
    private static readonly byte[] UnitScaleFactorBytes = Encoding.ASCII.GetBytes("UnitScaleFactor");
    private static readonly Regex AsciiUnitScaleRegex = new Regex(@"UnitScaleFactor[^\r\n]*,[^\r\n]*,[^\r\n]*,[^\r\n]*,\s*([0-9.]+)", RegexOptions.Compiled);

    public static Action<string>? WarnHandler { get; set; } = msg => Console.WriteLine("[WARN] " + msg);

    private static void LogWarn(string message)
    {
        if (WarnHandler != null) WarnHandler(message);
        else Console.WriteLine("[WARN] " + message);
    }







    public static float GetFbxUnitScaleFactor(string? fbxPath)
    {
        if (string.IsNullOrEmpty(fbxPath) || !File.Exists(fbxPath))
            return 100f;

        try
        {
            using var stream = File.OpenRead(fbxPath);

            int toRead = (int)Math.Min(1024 * 1024, stream.Length);
            byte[] buffer = new byte[toRead];
            int read = stream.Read(buffer, 0, toRead);
            if (read <= 0) return 100f;


            int idx = FindBytes(buffer, read, UnitScaleFactorBytes);
            if (idx >= 0)
            {

                for (int i = idx + UnitScaleFactorBytes.Length; i < Math.Min(read - 8, idx + 80); i++)
                {
                    if (buffer[i] == (byte)'D')
                    {
                        double val = BitConverter.ToDouble(buffer, i + 1);
                        if (double.IsFinite(val) && val > 1e-6 && val < 1e6)
                        {
                            return (float)val;
                        }
                    }
                }
            }


            string text = Encoding.ASCII.GetString(buffer, 0, read);
            var match = AsciiUnitScaleRegex.Match(text);
            if (match.Success && float.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float asciiVal))
            {
                if (float.IsFinite(asciiVal) && asciiVal > 1e-6f && asciiVal < 1e6f)
                {
                    return asciiVal;
                }
            }
        }
        catch (Exception ex)
        {
            LogWarn($"Failed to read FBX UnitScaleFactor from '{fbxPath}': {ex.Message}");
        }

        return 100f;
    }







    public static float CalculateEffectiveScale(float globalScale, bool useFileUnits, bool useFileScale, float unitScaleFactor)
    {
        if (!float.IsFinite(globalScale) || globalScale <= 0f)
            globalScale = 1f;

        if (!useFileScale)
            return globalScale;

        if (useFileUnits)
        {
            if (!float.IsFinite(unitScaleFactor) || unitScaleFactor <= 0f)
                unitScaleFactor = 100f;

            return globalScale * (unitScaleFactor / 100f);
        }

        return globalScale * 0.01f;
    }

    private static int FindBytes(byte[] haystack, int count, byte[] needle)
    {
        ArgumentNullException.ThrowIfNull(haystack);
        ArgumentNullException.ThrowIfNull(needle);
        if (needle.Length == 0 || count < needle.Length) return -1;
        for (int i = 0; i <= count - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) return i;
        }
        return -1;
    }
}
