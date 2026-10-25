#nullable enable
using System;
using System.IO;
using System.Linq;
using UnityPackageImporter.Models;

internal static class AvatarBehaviorTests
{
    public static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        test("VRCFury Blinking compiles BlendShapeAction target to native blinking binding", () =>
        {
            string yaml = @"--- !u!1 &1
GameObject:
  m_Name: AvatarRoot
--- !u!4 &2
Transform:
  m_GameObject: {fileID: 1}
  m_Father: {fileID: 0}
--- !u!1 &10
GameObject:
  m_Name: Body
--- !u!4 &20
Transform:
  m_GameObject: {fileID: 10}
  m_Father: {fileID: 2}
--- !u!137 &30
SkinnedMeshRenderer:
  m_GameObject: {fileID: 10}
--- !u!114 &40
MonoBehaviour:
  m_GameObject: {fileID: 1}
  m_Script: {fileID: 11500000, guid: " + AvatarPackageIndex.VrcFuryArmatureLinkScriptGuid + @"}
  content: {rid: 100}
  references:
    version: 2
    RefIds:
      - rid: 100
        type: {class: Blinking, ns: VF.Model.Feature, asm: VRCFury}
        data:
          state:
            actions:
              - rid: 101
      - rid: 101
        type: {class: BlendShapeAction, ns: VF.Model.StateAction, asm: VRCFury}
        data:
          blendShape: EyeClose
          blendShapeValue: 100
          renderer: {fileID: 30}
";
            var plan = AvatarBehaviorPlanner.Compile(yaml, 1);
            check(plan.CanBuild, "Plan failed to build.");
            check(plan.BlinkingBindings.Count == 1, $"Expected 1 blinking binding, got {plan.BlinkingBindings.Count}.");
            var blink = plan.BlinkingBindings[0];
            check(blink.TargetBlendShape == "EyeClose", $"Expected shape EyeClose, got {blink.TargetBlendShape}.");
            check(blink.TargetRendererFileId == 30, $"Expected renderer fileID 30, got {blink.TargetRendererFileId}.");
            check(blink.BlinkWeight == 100f, $"Expected weight 100, got {blink.BlinkWeight}.");
        });

        test("VRCFury Breathing compiles blendshape mode to native breathing binding", () =>
        {
            string yaml = @"--- !u!1 &1
GameObject:
  m_Name: AvatarRoot
--- !u!4 &2
Transform:
  m_GameObject: {fileID: 1}
  m_Father: {fileID: 0}
--- !u!114 &40
MonoBehaviour:
  m_GameObject: {fileID: 1}
  m_Script: {fileID: 11500000, guid: " + AvatarPackageIndex.VrcFuryArmatureLinkScriptGuid + @"}
  content: {rid: 200}
  references:
    version: 2
    RefIds:
      - rid: 200
        type: {class: Breathing, ns: VF.Model.Feature, asm: VRCFury}
        data:
          blendshape: Chest_Expand
";
            var plan = AvatarBehaviorPlanner.Compile(yaml, 1);
            check(plan.CanBuild, "Plan failed to build.");
            check(plan.BreathingBindings.Count == 1, $"Expected 1 breathing binding, got {plan.BreathingBindings.Count}.");
            var breath = plan.BreathingBindings[0];
            check(breath.Mode == AvatarBreathingTargetMode.BlendShape, "Expected BlendShape breathing mode.");
            check(breath.TargetBlendShape == "Chest_Expand", $"Expected shape Chest_Expand, got {breath.TargetBlendShape}.");
            check(breath.MinWeight == 0f && breath.MaxWeight == 100f, "Unexpected weight range.");
        });

        test("VRCFury Breathing compiles bone scale mode with valid owned object", () =>
        {
            string yaml = @"--- !u!1 &1
GameObject:
  m_Name: AvatarRoot
--- !u!4 &2
Transform:
  m_GameObject: {fileID: 1}
  m_Father: {fileID: 0}
--- !u!1 &10
GameObject:
  m_Name: Chest
--- !u!4 &20
Transform:
  m_GameObject: {fileID: 10}
  m_Father: {fileID: 2}
--- !u!114 &40
MonoBehaviour:
  m_GameObject: {fileID: 1}
  m_Script: {fileID: 11500000, guid: " + AvatarPackageIndex.VrcFuryArmatureLinkScriptGuid + @"}
  content: {rid: 200}
  references:
    version: 2
    RefIds:
      - rid: 200
        type: {class: Breathing, ns: VF.Model.Feature, asm: VRCFury}
        data:
          obj: {fileID: 10}
";
            var plan = AvatarBehaviorPlanner.Compile(yaml, 1);
            check(plan.CanBuild, "Plan failed to build.");
            check(plan.BreathingBindings.Count == 1, $"Expected 1 breathing binding, got {plan.BreathingBindings.Count}.");
            var breath = plan.BreathingBindings[0];
            check(breath.Mode == AvatarBreathingTargetMode.BoneScale, "Expected BoneScale breathing mode.");
            check(breath.TargetObjectFileId == 10, $"Expected target object fileID 10, got {breath.TargetObjectFileId}.");
        });

        test("VRCFury Breathing rejects unowned target object", () =>
        {
            string yaml = @"--- !u!1 &1
GameObject:
  m_Name: AvatarRoot
--- !u!4 &2
Transform:
  m_GameObject: {fileID: 1}
  m_Father: {fileID: 0}
--- !u!1 &99
GameObject:
  m_Name: OtherObject
--- !u!4 &990
Transform:
  m_GameObject: {fileID: 99}
  m_Father: {fileID: 0}
--- !u!114 &40
MonoBehaviour:
  m_GameObject: {fileID: 1}
  m_Script: {fileID: 11500000, guid: " + AvatarPackageIndex.VrcFuryArmatureLinkScriptGuid + @"}
  content: {rid: 200}
  references:
    version: 2
    RefIds:
      - rid: 200
        type: {class: Breathing, ns: VF.Model.Feature, asm: VRCFury}
        data:
          obj: {fileID: 99}
";
            var plan = AvatarBehaviorPlanner.Compile(yaml, 1);
            check(plan.BreathingBindings.Count == 0, "Breathing binding was created for unowned object.");
            check(plan.Diagnostics.Any(d => d.Code == "BEHAVIOR_TARGET_UNOWNED"), "Missing unowned target diagnostic.");
        });

        test("AvatarBehaviorPlanner handles empty or malformed YAML safely", () =>
        {
            var planEmpty = AvatarBehaviorPlanner.Compile("", 1);
            check(planEmpty.BreathingBindings.Count == 0 && planEmpty.BlinkingBindings.Count == 0, "Empty yaml produced bindings.");

            var planBad = AvatarBehaviorPlanner.Compile("not: [valid: {yaml", 1);
            check(planBad.HasUnsupportedContext, "Malformed YAML did not report unsupported context.");
        });
    }
}
