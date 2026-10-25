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

        test("VRCFury Talking compiles blendshape and object toggle actions", () =>
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
  m_Name: Mouth
--- !u!4 &20
Transform:
  m_GameObject: {fileID: 10}
  m_Father: {fileID: 2}
--- !u!137 &30
SkinnedMeshRenderer:
  m_GameObject: {fileID: 10}
--- !u!1 &50
GameObject:
  m_Name: VoiceIndicator
--- !u!4 &51
Transform:
  m_GameObject: {fileID: 50}
  m_Father: {fileID: 2}
--- !u!114 &40
MonoBehaviour:
  m_GameObject: {fileID: 1}
  m_Script: {fileID: 11500000, guid: " + AvatarPackageIndex.VrcFuryArmatureLinkScriptGuid + @"}
  content: {rid: 300}
  references:
    version: 2
    RefIds:
      - rid: 300
        type: {class: Talking, ns: VF.Model.Feature, asm: VRCFury}
        data:
          state:
            actions:
              - rid: 301
              - rid: 302
      - rid: 301
        type: {class: BlendShapeAction, ns: VF.Model.StateAction, asm: VRCFury}
        data:
          blendShape: Talk_Open
          blendShapeValue: 80
          renderer: {fileID: 30}
      - rid: 302
        type: {class: ObjectToggleAction, ns: VF.Model.StateAction, asm: VRCFury}
        data:
          obj: {fileID: 50}
          mode: TurnOn
";
            var plan = AvatarBehaviorPlanner.Compile(yaml, 1);
            check(plan.CanBuild, "Talking plan failed to build.");
            check(plan.TalkingBindings.Count == 2, $"Expected 2 talking bindings, got {plan.TalkingBindings.Count}.");
            var shapeBinding = plan.TalkingBindings.First(b => !string.IsNullOrEmpty(b.TargetBlendShape));
            check(shapeBinding.TargetBlendShape == "Talk_Open" && shapeBinding.TalkingWeight == 80f, "Wrong talking shape binding.");
            var objBinding = plan.TalkingBindings.First(b => b.TargetObjectFileId != 0);
            check(objBinding.TargetObjectFileId == 50 && objBinding.TurnOn, "Wrong talking object binding.");
        });

        test("VRCFury Visemes compiles multi-channel phonemes into native viseme bindings", () =>
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
  m_Name: Head
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
  content: {rid: 400}
  references:
    version: 2
    RefIds:
      - rid: 400
        type: {class: Visemes, ns: VF.Model.Feature, asm: VRCFury}
        data:
          state_aa:
            actions:
              - rid: 401
          state_O:
            actions:
              - rid: 402
          state_PP:
            actions:
              - rid: 403
      - rid: 401
        type: {class: BlendShapeAction, ns: VF.Model.StateAction, asm: VRCFury}
        data:
          blendShape: vrc.v_aa
          renderer: {fileID: 30}
      - rid: 402
        type: {class: BlendShapeAction, ns: VF.Model.StateAction, asm: VRCFury}
        data:
          blendShape: vrc.v_oh
          renderer: {fileID: 30}
      - rid: 403
        type: {class: BlendShapeAction, ns: VF.Model.StateAction, asm: VRCFury}
        data:
          blendShape: vrc.v_pp
          renderer: {fileID: 30}
";
            var plan = AvatarBehaviorPlanner.Compile(yaml, 1);
            check(plan.CanBuild, "Visemes plan failed to build.");
            check(plan.VisemeBindings.Count == 3, $"Expected 3 viseme bindings, got {plan.VisemeBindings.Count}.");
            var aa = plan.VisemeBindings.First(v => v.Channel == 10);
            check(aa.TargetBlendShape == "vrc.v_aa" && aa.TargetRendererFileId == 30, "Wrong aa viseme binding.");
            var oh = plan.VisemeBindings.First(v => v.Channel == 13);
            check(oh.TargetBlendShape == "vrc.v_oh" && oh.TargetRendererFileId == 30, "Wrong oh viseme binding.");
            var pp = plan.VisemeBindings.First(v => v.Channel == 1);
            check(pp.TargetBlendShape == "vrc.v_pp" && pp.TargetRendererFileId == 30, "Wrong pp viseme binding.");
        });

        test("VRCFury RemoveBlinking suppresses default eyelid blinking", () =>
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
  content: {rid: 500}
  references:
    version: 2
    RefIds:
      - rid: 500
        type: {class: RemoveBlinking, ns: VF.Model.Feature, asm: VRCFury}
        data: {}
";
            var plan = AvatarBehaviorPlanner.Compile(yaml, 1);
            check(plan.CanBuild, "RemoveBlinking plan failed to build.");
            check(plan.BlinkSuppressed, "Expected BlinkSuppressed to be true.");
        });

        test("VRCFury BlockBlinkingAction and BlockVisemesAction are detected in feature states", () =>
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
  content: {rid: 600}
  references:
    version: 2
    RefIds:
      - rid: 600
        type: {class: Toggle, ns: VF.Model.Feature, asm: VRCFury}
        data:
          state:
            actions:
              - rid: 601
              - rid: 602
      - rid: 601
        type: {class: BlockBlinkingAction, ns: VF.Model.StateAction, asm: VRCFury}
        data: {}
      - rid: 602
        type: {class: BlockVisemesAction, ns: VF.Model.StateAction, asm: VRCFury}
        data: {}
";
            var plan = AvatarBehaviorPlanner.Compile(yaml, 1);
            check(plan.CanBuild, "Blocker plan failed to build.");
            check(plan.HasBlockBlinking, "Expected HasBlockBlinking to be true.");
            check(plan.HasBlockVisemes, "Expected HasBlockVisemes to be true.");
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
