#nullable enable
using System;
using System.IO;
using System.Linq;
using UnityPackageImporter.Models;

internal static class AvatarEyeLookTests
{
    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        string Descriptor(string fields) => "--- !u!114 &100\nMonoBehaviour:\n" + fields;

        test("EyeLook parses blendshape eyelids, eye bones, and movement metrics", () =>
        {
            string yaml = Descriptor(
                "  customEyeLookSettings:\n" +
                "    eyelidType: 1\n" +
                "    eyelidsMesh: {fileID: 137001, guid: 11111111111111111111111111111111, type: 3}\n" +
                "    eyelidsBlendshapes:\n" +
                "    - 3\n" +
                "    - 7\n" +
                "    - 9\n" +
                "    leftEye: {fileID: 105, guid: 0}\n" +
                "    rightEye: {fileID: 106, guid: 0}\n" +
                "    eyeMovement:\n" +
                "      confidence: 0.85\n" +
                "      excitement: 0.25\n");

            var settings = AvatarEyeLookSettings.Parse(yaml);
            check(settings.HasSettings, "Expected HasSettings == true");
            check(settings.EyelidType == 1, "Expected EyelidType == 1 (blendshapes)");
            check(settings.EyelidsMesh?.FileId == 137001, "Expected eyelidsMesh FileId == 137001");
            check(settings.EyelidBlendShapeIndices.SequenceEqual(new[] { 3, 7, 9 }), "Blendshape indices must match [3, 7, 9]");
            check(settings.LeftEyeTransform?.FileId == 105, "Expected leftEye == 105");
            check(settings.RightEyeTransform?.FileId == 106, "Expected rightEye == 106");
            check(Math.Abs(settings.Confidence - 0.85f) < 0.001f, "Expected confidence == 0.85");
            check(Math.Abs(settings.Excitement - 0.25f) < 0.001f, "Expected excitement == 0.25");
        });

        test("EyeLook parses bone eyelids and clamps movement ranges", () =>
        {
            string yaml = Descriptor(
                "  customEyeLookSettings:\n" +
                "    eyelidType: 2\n" +
                "    leftEye: {fileID: 201}\n" +
                "    rightEye: {fileID: 202}\n" +
                "    eyeMovement:\n" +
                "      confidence: 1.5\n" +
                "      excitement: -0.5\n");

            var settings = AvatarEyeLookSettings.Parse(yaml);
            check(settings.HasSettings, "Expected HasSettings == true");
            check(settings.EyelidType == 2, "Expected EyelidType == 2 (bones)");
            check(settings.LeftEyeTransform?.FileId == 201, "Expected leftEye == 201");
            check(settings.RightEyeTransform?.FileId == 202, "Expected rightEye == 202");
            check(Math.Abs(settings.Confidence - 1.0f) < 0.001f, "Confidence must be clamped to 1.0 max");
            check(Math.Abs(settings.Excitement - 0.0f) < 0.001f, "Excitement must be clamped to 0.0 min");
        });

        test("EyeLook returns HasSettings false when customEyeLookSettings is absent", () =>
        {
            string yaml = Descriptor("  lipSync: 3\n  ViewPosition: {x: 0, y: 1.5, z: 0.1}\n");
            var settings = AvatarEyeLookSettings.Parse(yaml);
            check(!settings.HasSettings, "Expected HasSettings == false when block absent");
            check(settings.EyelidType == 0, "Expected default eyelidType == 0");
        });

        test("EyeLook is indexed on VrcAvatarDefinition during package scanning", () =>
        {
            string scene = "--- !u!1 &1\nGameObject:\n  m_Name: TestAvatar\n" +
                "--- !u!114 &10\nMonoBehaviour:\n  m_GameObject: {fileID: 1}\n" +
                "  m_Script: {fileID: " + AvatarPackageIndex.VrcAvatarDescriptorScriptFileId + ", guid: " + AvatarPackageIndex.VrcAvatarDescriptorScriptGuid + "}\n" +
                "  customEyeLookSettings:\n" +
                "    eyelidType: 1\n" +
                "    eyelidsMesh: {fileID: 555}\n" +
                "    eyelidsBlendshapes: [1]\n";

            var avatars = AvatarPackageIndex.ParseText(scene, "avatar.prefab").Avatars;
            check(avatars.Count == 1, "Expected 1 avatar");
            var eyeLook = avatars[0].EyeLook;
            check(eyeLook != null && eyeLook.HasSettings, "EyeLook must be indexed on avatar descriptor");
            check(eyeLook.EyelidType == 1 && eyeLook.EyelidsMesh?.FileId == 555, "Eyelid mesh and type must match");
            check(eyeLook.EyelidBlendShapeIndices.Count == 1 && eyeLook.EyelidBlendShapeIndices[0] == 1, "Blendshape index 1 must match");
        });
    }
}
