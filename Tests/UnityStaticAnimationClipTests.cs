using System.Globalization;
using UnityPackageImporter.Models;

internal static class UnityStaticAnimationClipTests
{
    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        test("Static clips preserve component path and named shape with Unity percentage units", () =>
        {
            var result = UnityStaticAnimationClip.Parse(Clip(Curve("Clothes/Coat", 137, "blendShape.Fit", 125)), 7400000);
            check(result.CanApply && result.Curves.Single() == new UnityStaticCurve("Clothes/Coat", 137, "blendShape.Fit", 125), "Path, component, shape identity or weight changed.");
        });
        test("Static clips accept exact constant activation without mutating source", () =>
        {
            string yaml = Clip(Curve("Clothes", 1, "m_IsActive", 0));
            var result = UnityStaticAnimationClip.Parse(yaml, 7400000);
            check(result.CanApply && result.Curves.Single().Value == 0 && yaml == Clip(Curve("Clothes", 1, "m_IsActive", 0)), "Constant activation did not survive parsing.");
        });
        test("Static clip selection uses fileID even with duplicate names", () =>
        {
            string yaml = Clip(Curve("First", 1, "m_IsActive", 1)) + Clip(Curve("Second", 1, "m_IsActive", 0), 7400001);
            var result = UnityStaticAnimationClip.Parse(yaml, 7400001);
            check(result.CanApply && result.Curves.Single().Path == "Second", "Clip name or document order replaced exact identity.");
            check(!UnityStaticAnimationClip.Parse(yaml, 123).CanApply, "Missing clip ID fell back to first clip.");
            check(!UnityStaticAnimationClip.Parse(yaml + Clip(""), 7400000).CanApply, "Duplicate selected identity was accepted.");
        });
        test("Static clips reject variable keys and nonzero tangents atomically", () =>
        {
            foreach (string curve in new[] { Curve("Body", 137, "blendShape.Size", 25).Replace("time: 1, value: 25", "time: 1, value: 75"),
                Curve("Body", 137, "blendShape.Size", 25).Replace("outSlope: 0", "outSlope: 20") })
            {
                var result = UnityStaticAnimationClip.Parse(Clip(curve + Curve("Clothes", 1, "m_IsActive", 1)), 7400000);
                check(!result.CanApply && result.Curves.Count == 0, "A non-static curve allowed a partial snapshot.");
            }
        });
        test("Static clips reject unsupported class and binding aliases", () =>
        {
            foreach (var pair in new[] { (Class: 4, Attribute: "m_LocalPosition.x"), (Class: 137, Attribute: "m_Enabled"), (Class: 1, Attribute: "blendShape.Size") })
                check(!UnityStaticAnimationClip.Parse(Clip(Curve("Body", pair.Class, pair.Attribute, 1)), 7400000).CanApply, "Unsupported component binding was guessed.");
        });
        test("Static clips reject path traversal and invalid activation values", () =>
        {
            foreach (string path in new[] { "../Body", "/Body", "Clothes/./Body" })
                check(!UnityStaticAnimationClip.Parse(Clip(Curve(path, 1, "m_IsActive", 1)), 7400000).CanApply, "Binding escaped relative component scope.");
            check(!UnityStaticAnimationClip.Parse(Clip(Curve("Body", 1, "m_IsActive", 0.5f)), 7400000).CanApply, "Non-boolean activation was rounded.");
        });
        test("Static clips reject compressed, transform, object and event data", () =>
        {
            foreach (string extra in new[] { "  m_PositionCurves: [{}]\n", "  m_EulerEditorCurves: [{}]\n", "  m_PPtrCurves: [{}]\n", "  m_Events: [{}]\n" })
                check(!UnityStaticAnimationClip.Parse(Clip(Curve("Body", 1, "m_IsActive", 1)) + extra, 7400000).CanApply, "Unsupported clip data was silently partially applied.");
            check(!UnityStaticAnimationClip.Parse(Clip("").Replace("m_Compressed: 0", "m_Compressed: 1"), 7400000).CanApply, "Compressed data was treated as a static no-op.");
        });
        test("Static clips report malformed YAML without returning partial curves", () =>
        {
            var result = UnityStaticAnimationClip.Parse(Clip(Curve("Body", 1, "m_IsActive", 1)) + "  broken: [\n", 7400000);
            check(!result.CanApply && result.Curves.Count == 0 && result.Diagnostics.Any(item => item.Code == "STATIC_CLIP_FORMAT"), "Malformed external clip bypassed the diagnostic result boundary.");
        });
        test("Static clip safety metadata cannot fail open on wrong-shaped YAML", () =>
        {
            string yaml = Clip(Curve("Body", 1, "m_IsActive", 1));
            foreach (string malformed in new[] { yaml.Replace("m_Compressed: 0", "m_Compressed: [1]"),
                yaml + "  m_AnimationClipSettings: []\n", yaml + "  m_AnimationClipSettings:\n    m_HasAdditiveReferencePose: [1]\n" })
            {
                var result = UnityStaticAnimationClip.Parse(malformed, 7400000);
                check(!result.CanApply && result.Curves.Count == 0 && result.Diagnostics.Any(item => item.Code == "STATIC_CLIP_FORMAT"), "Malformed safety metadata was treated as absent or disabled.");
            }
        });
        test("Static clip editor and runtime duplicates must agree", () =>
        {
            string curve = Curve("Body", 1, "m_IsActive", 1);
            string yaml = Clip(curve) + "  m_EditorCurves:\n" + curve;
            var result = UnityStaticAnimationClip.Parse(yaml, 7400000);
            check(result.CanApply && result.Curves.Count == 1, "Identical editor/runtime duplicate was not deduplicated.");
            yaml = Clip(curve) + "  m_EditorCurves:\n" + Curve("Body", 1, "m_IsActive", 0);
            check(!UnityStaticAnimationClip.Parse(yaml, 7400000).CanApply, "Conflicting duplicate selected by traversal order.");
        });
        test("Static clips cannot hide packed or additive animation behind editor curves", () =>
        {
            string yaml = Clip(Curve("Body", 1, "m_IsActive", 1));
            foreach (string extra in new[] { "  m_MuscleClip:\n    m_Clip:\n      m_StreamedClip:\n        data: [123]\n", "  m_AnimationClipSettings:\n    m_HasAdditiveReferencePose: 1\n" })
            {
                var result = UnityStaticAnimationClip.Parse(yaml + extra, 7400000);
                check(!result.CanApply && result.Curves.Count == 0, "Editor curves concealed unsupported packed/additive behavior.");
            }
            var empty = UnityStaticAnimationClip.Parse(yaml + "  m_MuscleClip:\n    m_Clip:\n      m_StreamedClip:\n        data: []\n", 7400000);
            check(empty.CanApply, "Empty packed metadata was treated as animation data.");
        });
        test("Static clips reject empty, unordered and nonfinite keyframes", () =>
        {
            foreach (string curve in new[] { Curve("Body", 1, "m_IsActive", 1).Replace("time: 1", "time: 0"),
                Curve("Body", 137, "blendShape.Size", 1).Replace("value: 1", "value: .nan"),
                Curve("Body", 1, "m_IsActive", 1).Replace("m_Curve:", "m_Curve: []\n      ignored:") })
                check(!UnityStaticAnimationClip.Parse(Clip(curve), 7400000).CanApply, "Malformed keyframes became a value.");
        });
        test("Static clip parsing honors cancellation and invariant locale", () =>
        {
            var culture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                check(UnityStaticAnimationClip.Parse(Clip(Curve("Body", 137, "blendShape.Size", 12.5f)), 7400000).Curves.Single().Value == 12.5f, "Locale changed parsed weight.");
                using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
                bool cancelled = false;
                try { UnityStaticAnimationClip.Parse("", 7400000, cancellation.Token); }
                catch (OperationCanceledException) { cancelled = true; }
                check(cancelled, "Empty clip ignored cancellation.");
            }
            finally { CultureInfo.CurrentCulture = culture; }
        });
    }

    private static string Clip(string curves, long id = 7400000) => $"--- !u!74 &{id}\nAnimationClip:\n  m_Name: SameName\n  m_Compressed: 0\n  m_FloatCurves:" + (curves.Length == 0 ? " []\n" : "\n" + curves);
    private static string Curve(string path, int classId, string attribute, float value)
    {
        string weight = value.ToString(CultureInfo.InvariantCulture);
        return $"  - curve:\n      m_Curve:\n      - {{time: 0, value: {weight}, inSlope: 0, outSlope: 0}}\n      - {{time: 1, value: {weight}, inSlope: 0, outSlope: 0}}\n    attribute: {attribute}\n    path: '{path}'\n    classID: {classId}\n    script: {{fileID: 0}}\n";
    }
}
