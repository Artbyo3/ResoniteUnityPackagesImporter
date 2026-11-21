#nullable enable
using System;
using System.IO;
using System.Linq;
using UnityPackageImporter.Models;

internal static class AvatarBehaviorTests
{
    public static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        test("Breathing native sine speed preserves the authored period", () =>
        {
            var breathing = new AvatarBreathingBinding(1, AvatarBreathingTargetMode.BlendShape,
                30, "Chest", 0f, 100f, 0, PeriodSeconds: 4f);
            check(Math.Abs(breathing.AngularSpeed * breathing.PeriodSeconds - 2f * MathF.PI) < 0.00001f,
                "A full breathing cycle must span two pi radians, not one radian.");
            check(Math.Abs((breathing with { PeriodSeconds = 0f }).AngularSpeed - 4f * MathF.PI) < 0.00001f,
                "Zero periods must retain the half-second minimum.");
            check(Math.Abs((breathing with { PeriodSeconds = -1f }).AngularSpeed - 4f * MathF.PI) < 0.00001f,
                "Negative periods must retain the half-second minimum.");
        });

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

        test("ModularAvatarMeshSettings targets descendant renderers with probe and bounds intent", () =>
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
  m_Name: ClothingParent
--- !u!4 &20
Transform:
  m_GameObject: {fileID: 10}
  m_Father: {fileID: 2}
--- !u!1 &12
GameObject:
  m_Name: Dress
--- !u!4 &22
Transform:
  m_GameObject: {fileID: 12}
  m_Father: {fileID: 20}
--- !u!137 &30
SkinnedMeshRenderer:
  m_GameObject: {fileID: 12}
--- !u!114 &40
MonoBehaviour:
  m_GameObject: {fileID: 10}
  m_Script: {fileID: 11500000, guid: " + AvatarPackageIndex.MeshSettingsScriptGuid + @"}
  inheritProbeAnchor: 1
  probeAnchor: {fileID: 1}
  inheritBounds: 0
  bounds:
    m_Center: {x: 0, y: 1.2, z: 0.1}
    m_Extent: {x: 0.5, y: 0.8, z: 0.5}
";
            var plan = AvatarBehaviorPlanner.Compile(yaml, 1);
            check(plan.CanBuild, "MeshSettings plan failed to build.");
            check(plan.MeshSettingsBindings.Count == 1, $"Expected 1 mesh settings binding, got {plan.MeshSettingsBindings.Count}.");
            var setting = plan.MeshSettingsBindings[0];
            check(setting.TargetRendererFileId == 30, $"Expected target renderer 30, got {setting.TargetRendererFileId}.");
            check(setting.ProbeAnchorTargetFileId == 1, $"Expected probe anchor target 1, got {setting.ProbeAnchorTargetFileId}.");
            check(setting.SetBounds, "Expected SetBounds to be true.");
            check(setting.SetProbeAnchor, "Expected SetProbeAnchor to be true.");
            check(Math.Abs(setting.BoundsCenter.Y - 1.2f) < 0.001f, $"Expected BoundsCenter.Y 1.2, got {setting.BoundsCenter.Y}.");
        });

        test("ModularAvatarFloorAdjuster computes floor offset and handles multi-adjuster conflicts safely", () =>
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
  m_Script: {fileID: 11500000, guid: " + AvatarPackageIndex.FloorAdjusterScriptGuid + @"}
  adjustment: 0.08
--- !u!114 &41
MonoBehaviour:
  m_GameObject: {fileID: 1}
  m_Script: {fileID: 11500000, guid: " + AvatarPackageIndex.FloorAdjusterScriptGuid + @"}
  adjustment: 0.15
";
            var plan = AvatarBehaviorPlanner.Compile(yaml, 1);
            check(plan.CanBuild, "FloorAdjuster plan failed to build.");
            check(plan.FloorAdjustmentY.HasValue, "Expected FloorAdjustmentY to have value.");
            check(Math.Abs(plan.FloorAdjustmentY!.Value - 0.08f) < 0.001f, $"Expected adjustment 0.08, got {plan.FloorAdjustmentY}.");
            check(plan.FloorAdjusterSourceId == 40, $"Expected source ID 40, got {plan.FloorAdjusterSourceId}.");
            check(plan.Diagnostics.Any(d => d.Code == "BEHAVIOR_FLOOR_ADJUSTER_CONFLICT"), "Expected conflict diagnostic.");
        });

        test("ModularAvatarVisibleHeadAccessory marks target GameObject as visible in first person", () =>
        {
            string yaml = @"--- !u!1 &1
GameObject:
  m_Name: AvatarRoot
--- !u!4 &2
Transform:
  m_GameObject: {fileID: 1}
  m_Father: {fileID: 0}
--- !u!1 &15
GameObject:
  m_Name: Hat
--- !u!4 &25
Transform:
  m_GameObject: {fileID: 15}
  m_Father: {fileID: 2}
--- !u!114 &50
MonoBehaviour:
  m_GameObject: {fileID: 15}
  m_Script: {fileID: 11500000, guid: " + AvatarPackageIndex.VisibleHeadAccessoryScriptGuid + @"}
";
            var plan = AvatarBehaviorPlanner.Compile(yaml, 1);
            check(plan.CanBuild, "VisibleHeadAccessory plan failed to build.");
            check(plan.VisibleInFirstPersonObjectIds.Contains(15), "Expected Hat (15) in VisibleInFirstPersonObjectIds.");
        });

        test("VRCFury ShowInFirstPerson compiles feature and standalone script into visible first-person IDs", () =>
        {
            string yaml = @"--- !u!1 &1
GameObject:
  m_Name: AvatarRoot
--- !u!4 &2
Transform:
  m_GameObject: {fileID: 1}
  m_Father: {fileID: 0}
--- !u!1 &16
GameObject:
  m_Name: Glasses
--- !u!4 &26
Transform:
  m_GameObject: {fileID: 16}
  m_Father: {fileID: 2}
--- !u!1 &17
GameObject:
  m_Name: Earring
--- !u!4 &27
Transform:
  m_GameObject: {fileID: 17}
  m_Father: {fileID: 2}
--- !u!114 &60
MonoBehaviour:
  m_GameObject: {fileID: 1}
  m_Script: {fileID: 11500000, guid: " + AvatarPackageIndex.VrcFuryArmatureLinkScriptGuid + @"}
  content: {rid: 700}
  references:
    version: 2
    RefIds:
      - rid: 700
        type: {class: ShowInFirstPerson, ns: VF.Model.Feature, asm: VRCFury}
        data:
          target: {fileID: 16}
--- !u!114 &61
MonoBehaviour:
  m_GameObject: {fileID: 17}
  m_Script: {fileID: 11500000, guid: " + AvatarPackageIndex.VrcFuryShowInFirstPersonScriptGuid + @"}
";
            var plan = AvatarBehaviorPlanner.Compile(yaml, 1);
            check(plan.CanBuild, "ShowInFirstPerson plan failed to build.");
            check(plan.VisibleInFirstPersonObjectIds.Contains(16), "Expected Glasses (16) in VisibleInFirstPersonObjectIds.");
            check(plan.VisibleInFirstPersonObjectIds.Contains(17), "Expected Earring (17) in VisibleInFirstPersonObjectIds.");
                });

        test("VRCFury AnchorOverrideFix and AnchorOverrideFix2 compile lighting anchor target to mesh settings binding", () =>
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
  m_Name: ChestAnchor
--- !u!4 &20
Transform:
  m_GameObject: {fileID: 10}
  m_Father: {fileID: 2}
--- !u!1 &11
GameObject:
  m_Name: Shirt
--- !u!4 &21
Transform:
  m_GameObject: {fileID: 11}
  m_Father: {fileID: 2}
--- !u!137 &31
SkinnedMeshRenderer:
  m_GameObject: {fileID: 11}
--- !u!114 &70
MonoBehaviour:
  m_GameObject: {fileID: 1}
  m_Script: {fileID: 11500000, guid: " + AvatarPackageIndex.VrcFuryArmatureLinkScriptGuid + @"}
  content: {rid: 800}
  references:
    version: 2
    RefIds:
      - rid: 800
        type: {class: AnchorOverrideFix2, ns: VF.Model.Feature, asm: VRCFury}
        data:
          anchor: {fileID: 10}
";
            var plan = AvatarBehaviorPlanner.Compile(yaml, 1);
            check(plan.CanBuild, "AnchorOverrideFix plan failed to build.");
            check(plan.MeshSettingsBindings.Count == 1, $"Expected 1 mesh setting, got {plan.MeshSettingsBindings.Count}.");
            var setting = plan.MeshSettingsBindings[0];
            check(setting.TargetRendererFileId == 31, $"Expected renderer 31, got {setting.TargetRendererFileId}.");
            check(setting.ProbeAnchorTargetFileId == 10, $"Expected anchor 10, got {setting.ProbeAnchorTargetFileId}.");
            check(setting.SetProbeAnchor, "Expected SetProbeAnchor to be true.");
        });

        test("VRCFury BoundingBoxFix and BoundingBoxFix2 compile bounding box settings to mesh settings binding", () =>
        {
            string yaml = @"--- !u!1 &1
GameObject:
  m_Name: AvatarRoot
--- !u!4 &2
Transform:
  m_GameObject: {fileID: 1}
  m_Father: {fileID: 0}
--- !u!1 &12
GameObject:
  m_Name: Pants
--- !u!4 &22
Transform:
  m_GameObject: {fileID: 12}
  m_Father: {fileID: 2}
--- !u!137 &32
SkinnedMeshRenderer:
  m_GameObject: {fileID: 12}
--- !u!114 &80
MonoBehaviour:
  m_GameObject: {fileID: 1}
  m_Script: {fileID: 11500000, guid: " + AvatarPackageIndex.VrcFuryArmatureLinkScriptGuid + @"}
  content: {rid: 900}
  references:
    version: 2
    RefIds:
      - rid: 900
        type: {class: BoundingBoxFix2, ns: VF.Model.Feature, asm: VRCFury}
        data:
          bounds:
            m_Center: {x: 0, y: 0.5, z: 0}
            m_Extent: {x: 1, y: 1, z: 1}
";
            var plan = AvatarBehaviorPlanner.Compile(yaml, 1);
            check(plan.CanBuild, "BoundingBoxFix plan failed to build.");
            check(plan.MeshSettingsBindings.Count == 1, $"Expected 1 mesh setting, got {plan.MeshSettingsBindings.Count}.");
            var setting = plan.MeshSettingsBindings[0];
            check(setting.TargetRendererFileId == 32, $"Expected renderer 32, got {setting.TargetRendererFileId}.");
            check(setting.SetBounds, "Expected SetBounds to be true.");
            check(Math.Abs(setting.BoundsCenter.Y - 0.5f) < 0.001f, $"Expected BoundsCenter.Y 0.5, got {setting.BoundsCenter.Y}.");
        });

        test("VRCFury BoneConstraint compiles humanoid bone constraint binding", () =>
        {
            string yaml = @"--- !u!1 &1
GameObject:
  m_Name: AvatarRoot
--- !u!4 &2
Transform:
  m_GameObject: {fileID: 1}
  m_Father: {fileID: 0}
--- !u!1 &13
GameObject:
  m_Name: Sword
--- !u!4 &23
Transform:
  m_GameObject: {fileID: 13}
  m_Father: {fileID: 2}
--- !u!114 &90
MonoBehaviour:
  m_GameObject: {fileID: 1}
  m_Script: {fileID: 11500000, guid: " + AvatarPackageIndex.VrcFuryArmatureLinkScriptGuid + @"}
" +
@"  content: {rid: 1000}
  references:
    version: 2
    RefIds:
      - rid: 1000
        type: {class: BoneConstraint, ns: VF.Model.Feature, asm: VRCFury}
        data:
          obj: {fileID: 13}
          bone: 18
";
            var plan = AvatarBehaviorPlanner.Compile(yaml, 1);
            check(plan.CanBuild, "BoneConstraint plan failed to build.");
            check(plan.BoneConstraintBindings.Count == 1, $"Expected 1 bone constraint binding, got {plan.BoneConstraintBindings.Count}.");
            var bc = plan.BoneConstraintBindings[0];
            check(bc.TargetObjectFileId == 13, $"Expected target object 13, got {bc.TargetObjectFileId}.");
            check(bc.HumanoidBone == 18, $"Expected bone 18 (RightHand), got {bc.HumanoidBone}.");
        });

        test("VRCFury WorldConstraint compiles world constraint binding", () =>
        {
            string yaml = @"--- !u!1 &1
GameObject:
  m_Name: AvatarRoot
--- !u!4 &2
Transform:
  m_GameObject: {fileID: 1}
  m_Father: {fileID: 0}
--- !u!1 &14
GameObject:
  m_Name: CameraProp
--- !u!4 &24
Transform:
  m_GameObject: {fileID: 14}
  m_Father: {fileID: 2}
--- !u!114 &91
MonoBehaviour:
  m_GameObject: {fileID: 1}
  m_Script: {fileID: 11500000, guid: " + AvatarPackageIndex.VrcFuryArmatureLinkScriptGuid + @"}
" +
@"  content: {rid: 1001}
  references:
    version: 2
    RefIds:
      - rid: 1001
        type: {class: WorldConstraint, ns: VF.Model.Feature, asm: VRCFury}
        data:
          obj: {fileID: 14}
";
            var plan = AvatarBehaviorPlanner.Compile(yaml, 1);
            check(plan.CanBuild, "WorldConstraint plan failed to build.");
            check(plan.WorldConstraintBindings.Count == 1, $"Expected 1 world constraint binding, got {plan.WorldConstraintBindings.Count}.");
            var wc = plan.WorldConstraintBindings[0];
            check(wc.TargetObjectFileId == 14, $"Expected target object 14, got {wc.TargetObjectFileId}.");
        });

        test("VRCFury HeadChopHead compiles head target into visible first-person IDs", () =>
        {
            string yaml = @"--- !u!1 &1
GameObject:
  m_Name: AvatarRoot
--- !u!4 &2
Transform:
  m_GameObject: {fileID: 1}
  m_Father: {fileID: 0}
--- !u!1 &15
GameObject:
  m_Name: HeadAccessory
--- !u!4 &25
Transform:
  m_GameObject: {fileID: 15}
  m_Father: {fileID: 2}
--- !u!114 &92
MonoBehaviour:
  m_GameObject: {fileID: 1}
  m_Script: {fileID: 11500000, guid: " + AvatarPackageIndex.VrcFuryArmatureLinkScriptGuid + @"}
" +
@"  content: {rid: 1002}
  references:
    version: 2
    RefIds:
      - rid: 1002
        type: {class: HeadChopHead, ns: VF.Model.Feature, asm: VRCFury}
        data:
          target: {fileID: 15}
";
            var plan = AvatarBehaviorPlanner.Compile(yaml, 1);
            check(plan.CanBuild, "HeadChopHead plan failed to build.");
            check(plan.VisibleInFirstPersonObjectIds.Contains(15), "Expected HeadAccessory (15) in VisibleInFirstPersonObjectIds.");
        });

        test("ModularAvatarMoveTo compiles target GameObject and humanoid bone bindings", () =>
        {
            string yaml = @"--- !u!1 &1
GameObject:
  m_Name: AvatarRoot
--- !u!4 &2
Transform:
  m_GameObject: {fileID: 1}
  m_Father: {fileID: 0}
--- !u!1 &20
GameObject:
  m_Name: Hat
--- !u!4 &21
Transform:
  m_GameObject: {fileID: 20}
  m_Father: {fileID: 2}
--- !u!1 &30
GameObject:
  m_Name: HeadBone
--- !u!4 &31
Transform:
  m_GameObject: {fileID: 30}
  m_Father: {fileID: 2}
--- !u!114 &40
MonoBehaviour:
  m_GameObject: {fileID: 20}
  m_Script: {fileID: 11500000, guid: " + AvatarPackageIndex.MoveToScriptGuid + @"}
  target: {fileID: 30}
  bone: 10
  preserveWorldTransform: 1
";
            var plan = AvatarBehaviorPlanner.Compile(yaml, 1);
            check(plan.CanBuild, "MoveTo plan failed to build.");
            check(plan.MoveToBindings.Count == 1, $"Expected 1 MoveTo binding, got {plan.MoveToBindings.Count}.");
            var mt = plan.MoveToBindings[0];
            check(mt.SourceObjectFileId == 20, $"Expected source 20, got {mt.SourceObjectFileId}.");
            check(mt.DestinationObjectFileId == 30, $"Expected dest 30, got {mt.DestinationObjectFileId}.");
            check(mt.HumanoidBone == 10, $"Expected bone 10, got {mt.HumanoidBone}.");
            check(mt.PreserveWorldTransform, "Expected PreserveWorldTransform true.");
        });

        test("ModularAvatarWorldFixedObject and WorldScaleObject compile world anchors", () =>
        {
            string yaml = @"--- !u!1 &1
GameObject:
  m_Name: AvatarRoot
--- !u!4 &2
Transform:
  m_GameObject: {fileID: 1}
  m_Father: {fileID: 0}
--- !u!1 &22
GameObject:
  m_Name: Prop
--- !u!4 &23
Transform:
  m_GameObject: {fileID: 22}
  m_Father: {fileID: 2}
--- !u!114 &41
MonoBehaviour:
  m_GameObject: {fileID: 22}
  m_Script: {fileID: 11500000, guid: " + AvatarPackageIndex.WorldFixedObjectScriptGuid + @"}
--- !u!114 &42
MonoBehaviour:
  m_GameObject: {fileID: 22}
  m_Script: {fileID: 11500000, guid: " + AvatarPackageIndex.WorldScaleObjectScriptGuid + @"}
";
            var plan = AvatarBehaviorPlanner.Compile(yaml, 1);
            check(plan.CanBuild, "World anchors plan failed to build.");
            check(plan.WorldFixedBindings.Count == 1, $"Expected 1 WorldFixed binding, got {plan.WorldFixedBindings.Count}.");
            check(plan.WorldScaleBindings.Count == 1, $"Expected 1 WorldScale binding, got {plan.WorldScaleBindings.Count}.");
            check(plan.WorldFixedBindings[0].TargetObjectFileId == 22, "Wrong target object for WorldFixed.");
            check(plan.WorldScaleBindings[0].TargetObjectFileId == 22, "Wrong target object for WorldScale.");
        });

        test("VRCHeadChop compiles target bones into visible first-person IDs", () =>
        {
            string yaml = @"--- !u!1 &1
GameObject:
  m_Name: AvatarRoot
--- !u!4 &2
Transform:
  m_GameObject: {fileID: 1}
  m_Father: {fileID: 0}
--- !u!1 &15
GameObject:
  m_Name: HeadBone
--- !u!4 &25
Transform:
  m_GameObject: {fileID: 15}
  m_Father: {fileID: 2}
--- !u!114 &95
MonoBehaviour:
  m_GameObject: {fileID: 15}
  m_EditorClassIdentifier: VRCHeadChop
  targetBones:
    - {fileID: 15}
";
            var plan = AvatarBehaviorPlanner.Compile(yaml, 1);
            check(plan.CanBuild, "VRCHeadChop plan failed to build.");
            check(plan.VisibleInFirstPersonObjectIds.Contains(15), "Expected HeadBone (15) in VisibleInFirstPersonObjectIds.");
        });
    }
}
