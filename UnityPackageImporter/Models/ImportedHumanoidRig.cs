using System;
using System.Linq;
using FrooxEngine;
using FrooxEngine.FinalIK;

namespace UnityPackageImporter.Models;

// Owned by the prefab task until native avatar creation has completed.
internal sealed class ImportedHumanoidRig
{
    private readonly VRIK ik;
    private readonly TemporaryRigParent<Slot> temporaryParent;

    internal ImportedHumanoidRig(Slot movementParent, Slot rigRoot, VRIK ik)
    {
        temporaryParent = new TemporaryRigParent<Slot>(movementParent, rigRoot);
        this.ik = ik;
    }

    internal void CompleteAvatar(Slot avatarRoot)
    {
        if (ik == null || ik.IsDestroyed)
            throw new InvalidOperationException("The imported avatar's IK is no longer available.");

        // VRIKAvatar.Setup transfers both spaces to its avatar root. Check the
        // actual result rather than assuming an engine version made that handoff.
        temporaryParent.Complete(avatarRoot,
            ik.Solver.SimulationSpace.Target, ik.Solver.OffsetSpace.Target,
            slot => slot.Parent, slot => !slot.IsDestroyed,
            (wrapper, destination) =>
            {
                // Import setup creates an active, component-free movement parent.
                // Refuse to erase additions or change visibility if it was edited.
                if (!wrapper.ActiveSelf || wrapper.GetComponents<Component>().Any())
                    throw new InvalidOperationException("The temporary rig parent was modified and cannot be removed automatically.");
                foreach (var child in wrapper.Children.ToArray())
                    child.SetParent(destination, true);
                wrapper.Destroy();
            });
    }
}
