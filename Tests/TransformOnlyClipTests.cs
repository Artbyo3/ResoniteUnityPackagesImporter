#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using UnityPackageImporter.Models;

internal static class TransformOnlyClipTests
{
    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        test("Vector containers recognized as transform-only playback", () =>
        {
            var containers = new[]
            {
                ("m_PositionCurves", false),
                ("m_ScaleCurves", false),
                ("m_RotationCurves", true),
                ("m_EulerCurves", false),
                ("m_EulerEditorCurves", false)
            };
            foreach (var (container, isRotation) in containers)
            {
                string yaml = VectorClip(container, "Armature/Hips/Wing_L", isRotation);
                var result = UnityStaticAnimationClip.Parse(yaml, 7400000);
                check(result.RequiresTransformPlayback, container + " was not recognized as transform playback.");
                check(!result.CanApply, container + " allowed static application.");
                check(result.Curves.Count == 0, container + " produced static curves.");
                check(result.TransformPaths.SequenceEqual(new[] { "Armature/Hips/Wing_L" }), container + " lost its deferred binding target.");
                check(result.Diagnostics.Any(diagnostic => diagnostic.Code == "STATIC_CLIP_TRANSFORM_PLAYBACK_PENDING" && diagnostic.Severity == InitialAppearanceSeverity.Warning), container + " lacked transform pending warning.");
                check(result.Diagnostics.All(diagnostic => diagnostic.Severity != InitialAppearanceSeverity.Error), container + " produced errors.");
            }
        });

        test("Scalar class4 float bindings recognized as transform-only playback", () =>
        {
            var attributes = new[]
            {
                "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z",
                "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z",
                "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w",
                "localEulerAnglesRaw.x", "localEulerAnglesRaw.y", "localEulerAnglesRaw.z",
                "localEulerAnglesBaked.x", "localEulerAnglesBaked.y", "localEulerAnglesBaked.z"
            };
            foreach (string attribute in attributes)
            {
                foreach (string container in new[] { "m_FloatCurves", "m_EditorCurves" })
                {
                    string yaml = ScalarClip("Armature/Hips/Ear_R", attribute, container: container);
                    var result = UnityStaticAnimationClip.Parse(yaml, 7400000);
                    check(result.RequiresTransformPlayback, attribute + " in " + container + " was not recognized as transform playback.");
                    check(!result.CanApply, attribute + " in " + container + " allowed static application.");
                    check(result.Curves.Count == 0, attribute + " in " + container + " produced static curves.");
                    check(result.TransformPaths.SequenceEqual(new[] { "Armature/Hips/Ear_R" }), attribute + " lost its deferred binding target.");
                    check(result.Diagnostics.Any(diagnostic => diagnostic.Code == "STATIC_CLIP_TRANSFORM_PLAYBACK_PENDING" && diagnostic.Severity == InitialAppearanceSeverity.Warning), attribute + " lacked transform pending warning.");
                    check(result.Diagnostics.All(diagnostic => diagnostic.Severity != InitialAppearanceSeverity.Error), attribute + " produced errors.");
                }
            }
        });

        test("Transform-only runtime disposition produces no snapshot writes", () =>
        {
            string yaml = VectorClip("m_PositionCurves", "Armature/Hips");
            var result = UnityStaticAnimationClip.Parse(yaml, 7400000);
            check(result.RequiresTransformPlayback && !result.CanApply && result.Curves.Count == 0, "Transform clip modified static snapshot state.");
            string scalarYaml = ScalarClip("Armature/Hips", "m_LocalPosition.x", 10f, 20f);
            var scalarResult = UnityStaticAnimationClip.Parse(scalarYaml, 7400000);
            check(scalarResult.RequiresTransformPlayback && !scalarResult.CanApply && scalarResult.Curves.Count == 0, "Scalar transform clip sampled static curves.");
        });

        test("Mixed appearance and transform clips stay atomic errors without partial writes", () =>
        {
            string activeYaml = AppearanceClip("Clothes", 1, "m_IsActive", 1f);
            string shapeYaml = AppearanceClip("Body", 137, "blendShape.Smile", 100f);
            string posYaml = VectorClip("m_PositionCurves", "Armature/Hips");
            string rotYaml = VectorClip("m_RotationCurves", "Armature/Hips", true);
            string scalarTransform = "  - curve:\n      serializedVersion: 2\n      m_Curve:\n      - {time: 0, value: 0, inSlope: 0, outSlope: 0}\n    attribute: m_LocalPosition.x\n    path: 'Armature/Hips'\n    classID: 4\n    script: {fileID: 0}\n";

            var mixedCases = new[]
            {
                activeYaml.TrimEnd() + "\n" + scalarTransform,
                shapeYaml.TrimEnd() + "\n" + scalarTransform,
                posYaml + "  m_FloatCurves:\n  - curve:\n      serializedVersion: 2\n      m_Curve:\n      - {time: 0, value: 1, inSlope: 0, outSlope: 0}\n    attribute: m_IsActive\n    path: 'Clothes'\n    classID: 1\n    script: {fileID: 0}\n",
                rotYaml + "  m_FloatCurves:\n  - curve:\n      serializedVersion: 2\n      m_Curve:\n      - {time: 0, value: 50, inSlope: 0, outSlope: 0}\n    attribute: blendShape.Fit\n    path: 'Body'\n    classID: 137\n    script: {fileID: 0}\n"
            };
            foreach (string mixed in mixedCases)
            {
                var result = UnityStaticAnimationClip.Parse(mixed, 7400000);
                check(!result.CanApply, "Mixed clip allowed application.");
                check(!result.RequiresTransformPlayback, "Mixed clip was classified as transform-only.");
                check(result.Curves.Count == 0, "Mixed clip produced partial appearance writes.");
                check(result.Diagnostics.Any(diagnostic => diagnostic.Severity == InitialAppearanceSeverity.Error && diagnostic.Code == "STATIC_CLIP_BINDING_UNSUPPORTED"), "Mixed clip lacked binding unsupported error.");
            }
        });

        test("Unknown binding, unsupported curves, and non-transform classes produce errors", () =>
        {
            var unknownCases = new[]
            {
                ScalarClip("Body", "Chest Front-Back").Replace("classID: 4", "classID: 95"),
                ScalarClip("Body", "m_Enabled"),
                ScalarClip("Body", "blendShape.Size"),
                VectorClip("m_PositionCurves", "Armature/Hips", extra: "    classID: 137\n"),
                "--- !u!74 &7400000\nAnimationClip:\n  m_Name: Idle\n  m_Compressed: 0\n  m_CompressedRotationCurves: [{}]\n",
                "--- !u!74 &7400000\nAnimationClip:\n  m_Name: Idle\n  m_Compressed: 0\n  m_Events: [{}]\n",
                "--- !u!74 &7400000\nAnimationClip:\n  m_Name: Idle\n  m_Compressed: 0\n  m_PPtrCurves: [{}]\n"
            };
            foreach (string yaml in unknownCases)
            {
                var result = UnityStaticAnimationClip.Parse(yaml, 7400000);
                check(!result.CanApply, "Unknown or unsupported binding allowed application.");
                check(!result.RequiresTransformPlayback, "Unknown binding allowed transform playback.");
                check(result.Curves.Count == 0, "Unknown binding produced curves.");
                check(result.Diagnostics.Any(diagnostic => diagnostic.Severity == InitialAppearanceSeverity.Error), "Unknown binding lacked error diagnostic.");
            }
        });

        test("Malformed schema in vector and transform float bindings produce errors", () =>
        {
            var malformedCases = new[]
            {
                "--- !u!74 &7400000\nAnimationClip:\n  m_Name: Idle\n  m_Compressed: 0\n  m_PositionCurves: {}\n",
                "--- !u!74 &7400000\nAnimationClip:\n  m_Name: Idle\n  m_Compressed: 0\n  m_PositionCurves:\n  - 'not a mapping'\n",
                "--- !u!74 &7400000\nAnimationClip:\n  m_Name: Idle\n  m_Compressed: 0\n  m_PositionCurves:\n  - path: 'Armature'\n    curve:\n      m_Curve: []\n",
                VectorClip("m_PositionCurves", "Armature").Replace("value: {x: 0.1, y: 1.2, z: -0.5}", "value: 123"),
                VectorClip("m_PositionCurves", "Armature").Replace("value: {x: 0.1, y: 1.2, z: -0.5}", "value: {x: 0.1, y: 1.2}"),
                VectorClip("m_RotationCurves", "Armature", true).Replace("value: {x: 0, y: 0.7071068, z: 0, w: 0.7071068}", "value: {x: 0, y: 0.7071068, z: 0}"),
                ScalarClip("Armature", "m_LocalPosition.x").Replace("- {time: 0", "- broken: [")
            };
            foreach (string yaml in malformedCases)
            {
                var result = UnityStaticAnimationClip.Parse(yaml, 7400000);
                check(!result.CanApply, "Malformed schema allowed application.");
                check(!result.RequiresTransformPlayback, "Malformed schema allowed transform playback.");
                check(result.Diagnostics.Any(diagnostic => diagnostic.Severity == InitialAppearanceSeverity.Error && diagnostic.Code == "STATIC_CLIP_FORMAT"), "Malformed schema lacked format error.");
            }
        });

        test("Nonfinite times, values, slopes and unordered times produce errors", () =>
        {
            var nonfiniteCases = new[]
            {
                VectorClip("m_PositionCurves", "Armature").Replace("time: 0.5", "time: 0"),
                VectorClip("m_PositionCurves", "Armature").Replace("time: 0.5", "time: .nan"),
                VectorClip("m_PositionCurves", "Armature").Replace("x: 0.1", "x: .nan"),
                VectorClip("m_PositionCurves", "Armature").Replace("x: 0,", "x: .nan,"),
                VectorClip("m_PositionCurves", "Armature").Replace("inSlope: {x: 0, y: 0, z: 0}", "inSlope: 123"),
                ScalarClip("Armature", "m_LocalPosition.x").Replace("time: 0.5", "time: 0"),
                ScalarClip("Armature", "m_LocalPosition.x").Replace("value: 2", "value: .nan"),
                ScalarClip("Armature", "m_LocalPosition.x").Replace("inSlope: 1", "inSlope: .nan")
            };
            foreach (string yaml in nonfiniteCases)
            {
                var result = UnityStaticAnimationClip.Parse(yaml, 7400000);
                check(!result.CanApply, "Non-finite or unordered clip allowed application.");
                check(!result.RequiresTransformPlayback, "Non-finite or unordered clip allowed transform playback.");
                check(result.Diagnostics.Any(diagnostic => diagnostic.Severity == InitialAppearanceSeverity.Error), "Non-finite or unordered clip lacked error diagnostic.");
            }
        });

        test("Wrong script reference and path traversal produce errors", () =>
        {
            var pathAndScriptCases = new[]
            {
                ScalarClip("/Armature/Hips", "m_LocalPosition.x"),
                ScalarClip("Armature/../Hips", "m_LocalPosition.x"),
                ScalarClip("Armature/./Hips", "m_LocalPosition.x"),
                ScalarClip("Armature/Hips", "m_LocalPosition.x").Replace("script: {fileID: 0}", "script: {fileID: 11500000, guid: a1b2c3d4e5f60718293a4b5c6d7e8f90}"),
                VectorClip("m_PositionCurves", "/Armature/Hips"),
                VectorClip("m_PositionCurves", "Armature/../Hips"),
                VectorClip("m_PositionCurves", "Armature/./Hips"),
                VectorClip("m_PositionCurves", "Armature/Hips", extra: "    script: {fileID: 11500000, guid: a1b2c3d4e5f60718293a4b5c6d7e8f90}\n")
            };
            foreach (string yaml in pathAndScriptCases)
            {
                var result = UnityStaticAnimationClip.Parse(yaml, 7400000);
                check(!result.CanApply, "Invalid path or script reference allowed application.");
                check(!result.RequiresTransformPlayback, "Invalid path or script reference allowed transform playback.");
                check(result.Diagnostics.Any(diagnostic => diagnostic.Severity == InitialAppearanceSeverity.Error && diagnostic.Code == "STATIC_CLIP_BINDING_UNSUPPORTED"), "Invalid path or script reference lacked unsupported error.");
            }
        });

        test("Variable supported appearance curves still reject with time-varying error", () =>
        {
            string varActive = "--- !u!74 &7400000\nAnimationClip:\n  m_Name: Idle\n  m_Compressed: 0\n  m_FloatCurves:\n  - curve:\n      serializedVersion: 2\n      m_Curve:\n      - {time: 0, value: 0, inSlope: 0, outSlope: 0}\n      - {time: 1, value: 1, inSlope: 0, outSlope: 0}\n    attribute: m_IsActive\n    path: 'Clothes'\n    classID: 1\n    script: {fileID: 0}\n";
            string varShape = "--- !u!74 &7400000\nAnimationClip:\n  m_Name: Idle\n  m_Compressed: 0\n  m_FloatCurves:\n  - curve:\n      serializedVersion: 2\n      m_Curve:\n      - {time: 0, value: 0, inSlope: 0, outSlope: 0}\n      - {time: 1, value: 50, inSlope: 0, outSlope: 0}\n    attribute: blendShape.Fit\n    path: 'Body'\n    classID: 137\n    script: {fileID: 0}\n";
            string slopeShape = "--- !u!74 &7400000\nAnimationClip:\n  m_Name: Idle\n  m_Compressed: 0\n  m_FloatCurves:\n  - curve:\n      serializedVersion: 2\n      m_Curve:\n      - {time: 0, value: 50, inSlope: 1, outSlope: 1}\n      - {time: 1, value: 50, inSlope: 1, outSlope: 1}\n    attribute: blendShape.Fit\n    path: 'Body'\n    classID: 137\n    script: {fileID: 0}\n";

            foreach (string yaml in new[] { varActive, varShape, slopeShape })
            {
                var result = UnityStaticAnimationClip.Parse(yaml, 7400000);
                check(!result.CanApply, "Variable appearance allowed application.");
                check(!result.RequiresTransformPlayback, "Variable appearance allowed transform playback.");
                check(result.Curves.Count == 0, "Variable appearance produced curves.");
                check(result.Diagnostics.Any(diagnostic => diagnostic.Severity == InitialAppearanceSeverity.Error && diagnostic.Code == "STATIC_CLIP_TIME_VARYING"), "Variable appearance lacked time-varying error.");
            }
        });

        test("Resolve passes yaml to captureSource callback and handles missing asset", () =>
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "ResolveTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string animFile = Path.Combine(tempDir, "test.anim");
                string clipYaml = VectorClip("m_PositionCurves", "Armature/Hips");
                File.WriteAllText(animFile, clipYaml);

                var assets = new Dictionary<string, string>
                {
                    { "guid_present", animFile },
                    { "guid_nonanim", Path.Combine(tempDir, "test.txt") }
                };

                string? capturedPresent = "not_set";
                var resultPresent = UnityStaticAnimationClip.Resolve(assets, "guid_present", 7400000, CancellationToken.None, source => capturedPresent = source);
                check(capturedPresent == clipYaml, "Resolve did not invoke captureSource with yaml for existing asset.");
                check(resultPresent != null && resultPresent.RequiresTransformPlayback, "Resolve did not return parsed result.");

                string? capturedMissing = "not_set";
                var resultMissing = UnityStaticAnimationClip.Resolve(assets, "guid_missing", 7400000, CancellationToken.None, source => capturedMissing = source);
                check(capturedMissing == null, "Resolve did not invoke captureSource with null for missing asset.");
                check(resultMissing == null, "Resolve did not return null for missing asset.");

                string? capturedNonAnim = "not_set";
                var resultNonAnim = UnityStaticAnimationClip.Resolve(assets, "guid_nonanim", 7400000, CancellationToken.None, source => capturedNonAnim = source);
                check(capturedNonAnim == null, "Resolve did not invoke captureSource with null for non-anim asset.");
                check(resultNonAnim == null, "Resolve did not return null for non-anim asset.");
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        });

        test("Native layout with empty scalar EulerEditor placeholders and runtime Euler curves is accepted as transform-only", () =>
        {
            string yaml = NativeLayoutClip("Armature/Hips/Tail_Root");
            var result = UnityStaticAnimationClip.Parse(yaml, 7400000);
            check(result.RequiresTransformPlayback, "Native S1N clip layout was not recognized as transform playback.");
            check(!result.CanApply, "Native S1N clip layout allowed static application.");
            check(result.Curves.Count == 0, "Native S1N clip layout produced curves.");
            check(result.TransformPaths.SequenceEqual(new[] { "Armature/Hips/Tail_Root" }), "Native runtime/editor bindings did not preserve one exact deferred target.");
            check(result.Diagnostics.Any(diagnostic => diagnostic.Code == "STATIC_CLIP_TRANSFORM_PLAYBACK_PENDING" && diagnostic.Severity == InitialAppearanceSeverity.Warning), "Native S1N clip layout lacked transform pending warning.");
            check(result.Diagnostics.All(diagnostic => diagnostic.Severity != InitialAppearanceSeverity.Error), "Native S1N clip layout produced unexpected errors.");
        });

        test("Orphan empty scalar EulerEditor placeholder without matching runtime Euler path produces error", () =>
        {
            string yaml = "--- !u!74 &7400000\nAnimationClip:\n  m_Name: OrphanTest\n  m_Compressed: 0\n  m_EulerCurves:\n  - curve:\n      serializedVersion: 2\n      m_Curve:\n      - {time: 0, value: {x: 0, y: 0, z: 0}, inSlope: {x: 0, y: 0, z: 0}, outSlope: {x: 0, y: 0, z: 0}}\n    path: 'Armature/Hips/Tail_Root'\n  m_EulerEditorCurves:\n  - curve:\n      serializedVersion: 2\n      m_Curve: []\n    attribute: m_LocalEulerAngles.x\n    path: 'Armature/Hips/Orphan'\n    classID: 4\n    script: {fileID: 0}\n";
            var result = UnityStaticAnimationClip.Parse(yaml, 7400000);
            check(!result.CanApply, "Orphan Euler editor placeholder allowed application.");
            check(!result.RequiresTransformPlayback, "Orphan Euler editor placeholder allowed transform playback.");
            check(result.Diagnostics.Any(diagnostic => diagnostic.Severity == InitialAppearanceSeverity.Error && diagnostic.Code == "STATIC_CLIP_BINDING_UNSUPPORTED"), "Orphan Euler editor placeholder lacked unsupported error.");
        });

        test("Scalar EulerEditor with wrong classID, unrecognized attribute, or nonfinite values produces errors", () =>
        {
            string baseClip = "--- !u!74 &7400000\nAnimationClip:\n  m_Name: ErrorTest\n  m_Compressed: 0\n  m_EulerCurves:\n  - curve:\n      serializedVersion: 2\n      m_Curve:\n      - {time: 0, value: {x: 0, y: 0, z: 0}, inSlope: {x: 0, y: 0, z: 0}, outSlope: {x: 0, y: 0, z: 0}}\n    path: 'Armature/Hips/Tail_Root'\n";
            var errorCases = new[]
            {
                baseClip + "  m_EulerEditorCurves:\n  - curve:\n      serializedVersion: 2\n      m_Curve: []\n    attribute: m_LocalEulerAngles.x\n    path: 'Armature/Hips/Tail_Root'\n    classID: 137\n    script: {fileID: 0}\n",
                baseClip + "  m_EulerEditorCurves:\n  - curve:\n      serializedVersion: 2\n      m_Curve: []\n    attribute: m_LocalPosition.x\n    path: 'Armature/Hips/Tail_Root'\n    classID: 4\n    script: {fileID: 0}\n",
                baseClip + "  m_EulerEditorCurves:\n  - curve:\n      serializedVersion: 2\n      m_Curve:\n      - {time: 0, value: .nan, inSlope: 0, outSlope: 0}\n    attribute: m_LocalEulerAngles.x\n    path: 'Armature/Hips/Tail_Root'\n    classID: 4\n    script: {fileID: 0}\n",
                baseClip + "  m_EulerEditorCurves:\n  - curve:\n      serializedVersion: 2\n      m_Curve:\n      - {time: 1, value: 0, inSlope: 0, outSlope: 0}\n      - {time: 0, value: 0, inSlope: 0, outSlope: 0}\n    attribute: m_LocalEulerAngles.x\n    path: 'Armature/Hips/Tail_Root'\n    classID: 4\n    script: {fileID: 0}\n"
            };
            foreach (string yaml in errorCases)
            {
                var result = UnityStaticAnimationClip.Parse(yaml, 7400000);
                check(!result.CanApply, "Invalid Euler editor entry allowed application.");
                check(!result.RequiresTransformPlayback, "Invalid Euler editor entry allowed transform playback.");
                check(result.Diagnostics.Any(diagnostic => diagnostic.Severity == InitialAppearanceSeverity.Error), "Invalid Euler editor entry lacked error diagnostic.");
            }
        });

        test("Nonempty scalar EulerEditor with valid keys is recognized as transform-only playback", () =>
        {
            string yaml = "--- !u!74 &7400000\nAnimationClip:\n  m_Name: ValidNonemptyEditor\n  m_Compressed: 0\n  m_EulerEditorCurves:\n  - curve:\n      serializedVersion: 2\n      m_Curve:\n      - {time: 0, value: 0, inSlope: 0, outSlope: 0}\n      - {time: 1, value: 45, inSlope: 45, outSlope: 45}\n    attribute: m_LocalEulerAngles.x\n    path: 'Armature/Hips/Tail_Root'\n    classID: 4\n    script: {fileID: 0}\n";
            var result = UnityStaticAnimationClip.Parse(yaml, 7400000);
            check(result.RequiresTransformPlayback, "Valid nonempty Euler editor curve was not recognized as transform playback.");
            check(!result.CanApply, "Valid nonempty Euler editor curve allowed static application.");
            check(result.Curves.Count == 0, "Valid nonempty Euler editor curve produced static curves.");
            check(result.Diagnostics.Any(diagnostic => diagnostic.Code == "STATIC_CLIP_TRANSFORM_PLAYBACK_PENDING" && diagnostic.Severity == InitialAppearanceSeverity.Warning), "Valid nonempty Euler editor curve lacked transform pending warning.");
            check(result.Diagnostics.All(diagnostic => diagnostic.Severity != InitialAppearanceSeverity.Error), "Valid nonempty Euler editor curve produced unexpected errors.");
        });
    }

    private static string VectorClip(string container, string path, bool isRotation = false, string extra = "")
    {
        string val = isRotation ? "{x: 0, y: 0.7071068, z: 0, w: 0.7071068}" : "{x: 0.1, y: 1.2, z: -0.5}";
        string inSlope = isRotation ? "{x: 0, y: 0, z: 0, w: 0}" : "{x: 0, y: 0, z: 0}";
        string outSlope = inSlope;
        return "--- !u!74 &7400000\nAnimationClip:\n  m_Name: Idle\n  m_Compressed: 0\n  " + container + ":\n  - curve:\n      serializedVersion: 2\n      m_Curve:\n      - serializedVersion: 3\n        time: 0\n        value: " + val + "\n        inSlope: " + inSlope + "\n        outSlope: " + outSlope + "\n      - serializedVersion: 3\n        time: 0.5\n        value: " + val + "\n        inSlope: " + inSlope + "\n        outSlope: " + outSlope + "\n" + extra + "    path: '" + path + "'\n";
    }

    private static string ScalarClip(string path, string attribute, float val1 = 1f, float val2 = 2f, string extra = "", string container = "m_FloatCurves") =>
        "--- !u!74 &7400000\nAnimationClip:\n  m_Name: Idle\n  m_Compressed: 0\n  " + container + ":\n  - curve:\n      serializedVersion: 2\n      m_Curve:\n      - {time: 0, value: " + val1.ToString(CultureInfo.InvariantCulture) + ", inSlope: 0, outSlope: 0}\n      - {time: 0.5, value: " + val2.ToString(CultureInfo.InvariantCulture) + ", inSlope: 1, outSlope: 1}\n" + extra + "    attribute: " + attribute + "\n    path: '" + path + "'\n    classID: 4\n    script: {fileID: 0}\n";

    private static string AppearanceClip(string path, int classId, string attribute, float val) =>
        "--- !u!74 &7400000\nAnimationClip:\n  m_Name: Idle\n  m_Compressed: 0\n  m_FloatCurves:\n  - curve:\n      serializedVersion: 2\n      m_Curve:\n      - {time: 0, value: " + val.ToString(CultureInfo.InvariantCulture) + ", inSlope: 0, outSlope: 0}\n      - {time: 1, value: " + val.ToString(CultureInfo.InvariantCulture) + ", inSlope: 0, outSlope: 0}\n    attribute: " + attribute + "\n    path: '" + path + "'\n    classID: " + classId.ToString(CultureInfo.InvariantCulture) + "\n    script: {fileID: 0}\n";

    private static string NativeLayoutClip(string path) =>
        "--- !u!74 &7400000\nAnimationClip:\n  m_Name: S1N_Native\n  m_Compressed: 0\n  m_EulerCurves:\n  - curve:\n      serializedVersion: 2\n      m_Curve:\n      - serializedVersion: 3\n        time: 0\n        value: {x: 0, y: 0, z: 0}\n        inSlope: {x: 0, y: 0, z: 0}\n        outSlope: {x: 0, y: -15, z: 0}\n      - serializedVersion: 3\n        time: 1\n        value: {x: 0, y: -15, z: 0}\n        inSlope: {x: 0, y: -15, z: 0}\n        outSlope: {x: 0, y: 15, z: 0}\n    path: '" + path + "'\n  m_PositionCurves:\n  - curve:\n      serializedVersion: 2\n      m_Curve:\n      - serializedVersion: 3\n        time: 0\n        value: {x: 0, y: 0.03, z: -0.01}\n        inSlope: {x: 0, y: 0, z: 0}\n        outSlope: {x: 0, y: 0, z: 0}\n    path: '" + path + "'\n  m_EditorCurves:\n  - curve:\n      serializedVersion: 2\n      m_Curve:\n      - serializedVersion: 3\n        time: 0\n        value: 0\n        inSlope: 0\n        outSlope: 0\n    attribute: localEulerAnglesRaw.x\n    path: '" + path + "'\n    classID: 4\n    script: {fileID: 0}\n  m_EulerEditorCurves:\n  - serializedVersion: 2\n    curve:\n      serializedVersion: 2\n      m_Curve: []\n    attribute: m_LocalEulerAngles.x\n    path: '" + path + "'\n    classID: 4\n    script: {fileID: 0}\n  - serializedVersion: 2\n    curve:\n      serializedVersion: 2\n      m_Curve: []\n    attribute: m_LocalEulerAngles.y\n    path: '" + path + "'\n    classID: 4\n    script: {fileID: 0}\n  - serializedVersion: 2\n    curve:\n      serializedVersion: 2\n      m_Curve: []\n    attribute: m_LocalEulerAngles.z\n    path: '" + path + "'\n    classID: 4\n    script: {fileID: 0}\n";
}
