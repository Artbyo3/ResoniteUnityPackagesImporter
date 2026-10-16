using System;
using System.Linq;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.FinalIK;
using Renderite.Shared;

namespace UnityPackageImporter.Models;

// Prepares the imported humanoid rig before the native avatar creation step.
// Per-instance metadata avoids sharing live bone references between prefabs.
internal static class HumanoidRigSetup
{
    internal static async Task<ImportedHumanoidRig> SetupAsync(FileImportTaskScene task, Slot FBXRoot)
    {
        if (task == null || FBXRoot == null)
        {
            UnityPackageImporter.Warn("SettupHumanoid skipped: task or FBXRoot was null.");
            return null;
        }

        UnityPackageImporter.Msg("checking if this FBX is a humanoid");
        Slot taskSlot = FBXRoot;

        // A shared FBX task can feed several prefabs concurrently. Bone Slots belong
        // to this instance, so never rescan/mutate the template's metadata here.
        var instanceMetadata = new MetaDataFile();
        await instanceMetadata.ScanFile(task, FBXRoot);
        await default(ToWorld);
        if (taskSlot.GetComponent<VRIK>() != null) return null;
        BipedRig biped = taskSlot.AttachComponent<BipedRig>();
        await instanceMetadata.GenerateComponents(biped);
        await default(ToWorld);

        // Unity metadata supplies the imported biped mapping. The movement parent
        // is an import-time IK reference space, owned by the returned preparation.
        // Native avatar creation must release it before the prefab is committed.
        if (biped.IsBiped) {

            await default(ToWorld);

            Slot parentSlot = taskSlot.Parent ?? taskSlot.World.RootSlot;
            Slot movecenter = parentSlot.AddSlot(taskSlot.Name + " - Move Me With This!");
            movecenter.TRS = taskSlot.TRS;
            taskSlot.SetParent(movecenter);

            Rig rig = taskSlot.GetComponent<Rig>();
            if (rig == null)
            {
                UnityPackageImporter.Msg("rig missing, attaching.");
                UnityPackageImporter.Msg("slot to string is: " + taskSlot.ToString());
                rig = taskSlot.AttachComponent<Rig>();
            }

            UnityPackageImporter.Msg("Finding if we set up VRIK and are biped.");
            if (biped.IsBiped)
            {
                UnityPackageImporter.Msg("We have not set up vrik!");

                // Put our stuff under a slot called rootnode so froox engine can set this model up as an avatar
                await default(ToWorld);
                Slot rootnode = FBXRoot; // Intentional - @989onan

                UnityPackageImporter.Msg("Registering bones to rig.");
                await default(ToWorld);
                foreach (Slot slot in rootnode.GetAllChildren(false).ToArray())
                {
                    if (null == slot.GetComponent<FrooxEngine.SkinnedMeshRenderer>())
                    {
                        UnityPackageImporter.Msg("adding bone " + slot.Name);
                        rig.Bones.AddUnique(slot);
                    }

                    UnityPackageImporter.Msg("creating bone colliders for bone " + slot.Name);
                    BodyNode node = BodyNode.NONE;
                    try
                    {
                        node = biped.Bones.FirstOrDefault(i => i.Value != null && i.Value.Name.Equals(slot.Name)).Key;
                    }
                    catch (Exception) { } //this is to catch key not found so we shouldn't handle this.

                    if (BodyNode.NONE != node)
                    {
                        UnityPackageImporter.Msg("finding length of bone " + slot.Name);
                        float3 v = float3.Zero;
                        UnityPackageImporter.Msg("finding bone length " + slot.Name);
                        foreach (Slot child in slot.Children.ToArray())
                        {
                            float3 globalPoint = child.GlobalPosition;
                            v = slot.GlobalPointToLocal(in globalPoint);
                            if (v.Magnitude > 0.1f)
                            {
                                break;
                            }
                        }
                        foreach (Slot child in slot.Children.ToArray())
                        {
                            await default(ToWorld);
                            float3 globalPoint = child.GlobalPosition;
                            float value = v.Magnitude * 0.125f;
                            float magnitude = v.Magnitude;

                            v = slot.GlobalPointToLocal(in globalPoint);
                            if (v.Magnitude <= 1e-5f)
                            {
                                continue;
                            }
                            float3 localPosition = v * 0.5f;
                            Slot slotcollider = slot.AddSlot(slot.Name + " Collider");
                            slotcollider.LocalPosition = localPosition;
                            floatQ a = floatQ.LookRotation(in v);
                            float3 globalPoint2 = float3.Up;
                            float3 to = float3.Forward;
                            floatQ b = floatQ.FromToRotation(in globalPoint2, in to);
                            slotcollider.LocalRotation = a * b;

                            await default(ToWorld);
                            CapsuleCollider capsuleCollider = slotcollider.AttachComponent<CapsuleCollider>();
                            capsuleCollider.Radius.Value = value;
                            capsuleCollider.Height.Value = magnitude;
                        }
                    }

                }

                await default(ToBackground);

                UnityPackageImporter.Msg("attaching VRIK");
                await default(ToWorld);

                await default(ToBackground);
                UnityPackageImporter.Msg("Initializing VRIK");
                await default(ToWorld);
                Grabbable componentInParents = rootnode.GetComponentInParents<Grabbable>(null, true, false);
                if (componentInParents != null)
                {
                    componentInParents.Destroy();
                }
                VRIK vrik = rootnode.AttachComponent<VRIK>(true, null);
                vrik.Solver.SimulationSpace.Target = rootnode.Parent;
                vrik.Solver.OffsetSpace.Target = rootnode.Parent;
                //this is here to initialize our Rig's biped forward at the end of the import for VRIK later on.
                biped.GuessForwardFlipped();
                try
                {
                    biped.DetectHandRigs();
                }
                catch (Exception e)
                {
                    UnityPackageImporter.Msg("Error when detecting hand rigs for model \"" + task.file + "\"");
                    UnityPackageImporter.Msg(e.Message,e.StackTrace);
                }
                vrik.Initiate();//create our vrik component using our custom biped rig as our humanoid. Since it's on the same slot.

                //set our ik draggables up so people can play with the model and know it worked. - @989onan
                //ps, stolen from FrooxEngine decompiled code.
                Slot slot3 = biped[BodyNode.Head];
                Slot slot4 = biped[BodyNode.Hips];
                Slot slot5 = biped[BodyNode.LeftHand];
                Slot slot6 = biped[BodyNode.RightHand];
                Slot slot7 = biped[BodyNode.LeftFoot];
                Slot slot8 = biped[BodyNode.RightFoot];
                Slot slot9 = biped.TryGetBone(BodyNode.LeftToes);
                Slot slot10 = biped.TryGetBone(BodyNode.RightToes);
                FrooxInternalBridge.SetupDraggable(slot3, vrik.Solver, vrik.Solver.spine.IKPositionHead, vrik.Solver.spine.IKRotationHead, vrik.Solver.spine.PositionWeight);
                FrooxInternalBridge.SetupDraggable(slot4, vrik.Solver, vrik.Solver.spine.IKPositionPelvis, vrik.Solver.spine.IKRotationPelvis, vrik.Solver.spine.PelvisPositionWeight);
                FrooxInternalBridge.SetupDraggable(slot5, vrik.Solver, vrik.Solver.leftArm.IKPosition, vrik.Solver.leftArm.IKRotation, vrik.Solver.leftArm.PositionWeight);
                FrooxInternalBridge.SetupDraggable(slot6, vrik.Solver, vrik.Solver.rightArm.IKPosition, vrik.Solver.rightArm.IKRotation, vrik.Solver.rightArm.PositionWeight);
                FrooxInternalBridge.SetupDraggable(slot9 ?? slot7, vrik.Solver, vrik.Solver.leftLeg.IKPosition, vrik.Solver.leftLeg.IKRotation, vrik.Solver.leftLeg.PositionWeight);
                FrooxInternalBridge.SetupDraggable(slot10 ?? slot8, vrik.Solver, vrik.Solver.rightLeg.IKPosition, vrik.Solver.rightLeg.IKRotation, vrik.Solver.rightLeg.PositionWeight);
                taskSlot.AttachComponent<DestroyRoot>();
                DynamicVariableSpace avatar = taskSlot.AttachComponent<DynamicVariableSpace>();
                avatar.SpaceName.Value = "Avatar";
                taskSlot.AttachComponent<ObjectRoot>();
                await default(ToBackground);
                return new ImportedHumanoidRig(movecenter, rootnode, vrik);
            }
            else
            {
                UnityPackageImporter.Msg("this prefab is not biped. ");
            }
        }
        else
        {
            UnityPackageImporter.Msg("this prefab is not biped. ");
        }
        await default(ToBackground);
        return null;
    }
}
