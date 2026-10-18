#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityPackageImporter.Models;

internal static class FailureIsolationAndOwnershipTests
{
    private sealed class MockUser(string name)
    {
        public string Name { get; } = name;
    }

    private sealed class MockSlot
    {
        public string Name { get; set; }
        public MockSlot? Parent { get; private set; }
        public List<MockSlot> Children { get; } = new();
        public bool IsDestroyed { get; private set; }

        public MockSlot(string name, MockSlot? parent = null)
        {
            Name = name;
            Parent = parent;
            parent?.Children.Add(this);
        }

        public MockSlot AddChild(string childName)
        {
            var child = new MockSlot(childName, this);
            return child;
        }

        public void Destroy()
        {
            IsDestroyed = true;
            Parent?.Children.Remove(this);
            Parent = null;
            foreach (var child in Children.ToArray())
            {
                child.Destroy();
            }
        }
    }

    public static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        test("Source batch: Late preparation failure cannot enter the successful output set", () => Run(async () =>
        {
            var sources = new[] { new MockSlot("First"), new MockSlot("Broken"), new MockSlot("Last") };
            var prepared = new Dictionary<MockSlot, MockSlot>();
            var outcomes = await SourceImportBatch.RunAsync(sources, async (source, token) =>
            {
                var item = source.AddChild("Already prepared");
                lock (prepared) prepared[source] = item;
                await Task.Yield();
                if (source.Name == "Broken") throw new InvalidOperationException("Late preparation failure");
            }, (source, error) =>
            {
                source.Destroy();
                return Task.CompletedTask;
            }, maxDegreeOfParallelism: 2);

            check(outcomes.Succeeded.SequenceEqual(new[] { sources[0], sources[2] }),
                "Only fully completed sources may be aggregated, in source order.");
            check(outcomes.Failed.Single() == sources[1] && prepared[sources[1]].IsDestroyed,
                "A failed source's existing prepared state must not escape cleanup.");
            var selectable = outcomes.Succeeded.Select(source => prepared[source]).ToArray();
            check(selectable.Length == 2 && selectable.All(slot => !slot.IsDestroyed),
                "Output selection must never include the failed prepared item.");
        }));

        test("Source batch: An all-failed batch cannot masquerade as completed sources", () => Run(async () =>
        {
            var outcomes = await SourceImportBatch.RunAsync(new[] { new MockSlot("Broken") },
                (source, token) => throw new InvalidOperationException("Synthetic failure"),
                (source, error) => { source.Destroy(); return Task.CompletedTask; });
            check(outcomes.Succeeded.Count == 0 && outcomes.Failed.Count == 1,
                "The project must be able to distinguish total failure from an empty package.");
        }));

        // 1. Avatar ownership validation rules
        test("AvatarOwnership: Local allocating user owns unprotected avatar", () =>
        {
            var local = new MockUser("local");
            bool owned = AvatarOwnershipValidator.EvaluateOwnership(
                isRigValid: true,
                isRigDestroyed: false,
                isTemporaryStaging: false,
                isSameWorld: true,
                localUser: local,
                activeUser: null,
                allocatingUser: local,
                protectionUsers: Array.Empty<MockUser>());
            check(owned, "Local allocating user should own unprotected avatar.");
        });

        test("AvatarOwnership: Local active user owns wearing avatar", () =>
        {
            var local = new MockUser("local");
            var creator = new MockUser("other");
            bool owned = AvatarOwnershipValidator.EvaluateOwnership(
                isRigValid: true,
                isRigDestroyed: false,
                isTemporaryStaging: false,
                isSameWorld: true,
                localUser: local,
                activeUser: local,
                allocatingUser: creator,
                protectionUsers: Array.Empty<MockUser>());
            check(owned, "User actively wearing avatar should have ownership.");
        });

        test("AvatarOwnership: Explicit protection targeting local user grants ownership", () =>
        {
            var local = new MockUser("local");
            var creator = new MockUser("other");
            bool owned = AvatarOwnershipValidator.EvaluateOwnership(
                isRigValid: true,
                isRigDestroyed: false,
                isTemporaryStaging: false,
                isSameWorld: true,
                localUser: local,
                activeUser: null,
                allocatingUser: creator,
                protectionUsers: new[] { local });
            check(owned, "Explicit SimpleAvatarProtection for local user should grant ownership.");
        });

        test("AvatarOwnership: Foreign protection component blocks ownership completely", () =>
        {
            var local = new MockUser("local");
            var other = new MockUser("foreign");
            bool owned = AvatarOwnershipValidator.EvaluateOwnership(
                isRigValid: true,
                isRigDestroyed: false,
                isTemporaryStaging: false,
                isSameWorld: true,
                localUser: local,
                activeUser: local,
                allocatingUser: local,
                protectionUsers: new[] { other });
            check(!owned, "Protection for foreign user must block ownership even if local user is wearing it.");
        });

        test("AvatarOwnership: Foreign active user blocks other users from mutating avatar", () =>
        {
            var local = new MockUser("local");
            var other = new MockUser("other");
            bool owned = AvatarOwnershipValidator.EvaluateOwnership(
                isRigValid: true,
                isRigDestroyed: false,
                isTemporaryStaging: false,
                isSameWorld: true,
                localUser: local,
                activeUser: other,
                allocatingUser: local,
                protectionUsers: Array.Empty<MockUser>());
            check(!owned, "Cannot mutate an avatar actively worn by another user.");
        });

        test("AvatarOwnership: Foreign allocating user without protection is not owned by local user", () =>
        {
            var local = new MockUser("local");
            var other = new MockUser("other");
            bool owned = AvatarOwnershipValidator.EvaluateOwnership(
                isRigValid: true,
                isRigDestroyed: false,
                isTemporaryStaging: false,
                isSameWorld: true,
                localUser: local,
                activeUser: null,
                allocatingUser: other,
                protectionUsers: Array.Empty<MockUser>());
            check(!owned, "Avatar allocated by another user cannot be mutated by local user.");
        });

        test("AvatarOwnership: Temporary import staging rig is never considered owned avatar", () =>
        {
            var local = new MockUser("local");
            bool owned = AvatarOwnershipValidator.EvaluateOwnership(
                isRigValid: true,
                isRigDestroyed: false,
                isTemporaryStaging: true,
                isSameWorld: true,
                localUser: local,
                activeUser: null,
                allocatingUser: local,
                protectionUsers: Array.Empty<MockUser>());
            check(!owned, "Temporary staging slot must not be targeted as an installed avatar.");
        });

        test("AvatarOwnership: Destroyed rig or cross-world rig is rejected", () =>
        {
            var local = new MockUser("local");
            bool destroyed = AvatarOwnershipValidator.EvaluateOwnership(
                isRigValid: true,
                isRigDestroyed: true,
                isTemporaryStaging: false,
                isSameWorld: true,
                localUser: local,
                activeUser: null,
                allocatingUser: local,
                protectionUsers: Array.Empty<MockUser>());
            check(!destroyed, "Destroyed rig must be rejected.");

            bool crossWorld = AvatarOwnershipValidator.EvaluateOwnership(
                isRigValid: true,
                isRigDestroyed: false,
                isTemporaryStaging: false,
                isSameWorld: false,
                localUser: local,
                activeUser: null,
                allocatingUser: local,
                protectionUsers: Array.Empty<MockUser>());
            check(!crossWorld, "Cross-world rig must be rejected.");

            bool nullUser = AvatarOwnershipValidator.EvaluateOwnership<MockUser>(
                isRigValid: true,
                isRigDestroyed: false,
                isTemporaryStaging: false,
                isSameWorld: true,
                localUser: null,
                activeUser: null,
                allocatingUser: null,
                protectionUsers: Array.Empty<MockUser>());
            check(!nullUser, "Null local user must be rejected.");
        });

        // 2. Transaction isolation and rollback on failure
        test("TransactionIsolation: Mid-installation failure rolls back cloned hierarchy and preserves target", () =>
        {
            var targetRoot = new MockSlot("TargetAvatar");
            var targetHips = targetRoot.AddChild("Hips");
            var targetArm = targetHips.AddChild("Arm");

            var transaction = new OwnedAttachmentTransaction<MockSlot>();

            // Simulate cloning outfit and attaching to target
            var outfitClone = new MockSlot("OutfitClone");
            transaction.Track(outfitClone);

            var clonedAccessory = outfitClone.AddChild("Bag");
            transaction.Track(clonedAccessory);

            // Simulate mid-installation failure
            var rollbackErrors = transaction.RollBack(slot => slot.Destroy());

            check(rollbackErrors.Count == 0, "Rollback should succeed without errors.");
            check(outfitClone.IsDestroyed, "Cloned outfit root must be destroyed by rollback.");
            check(clonedAccessory.IsDestroyed, "Cloned accessory must be destroyed by rollback.");

            // Verify target avatar was completely untouched
            check(!targetRoot.IsDestroyed, "Target avatar root must remain alive.");
            check(!targetHips.IsDestroyed && !targetArm.IsDestroyed, "Target avatar bones must remain alive.");
            check(targetHips.Children.Contains(targetArm), "Target hierarchy must remain intact.");
            check(!targetHips.Children.Contains(clonedAccessory), "Target avatar must not retain cloned objects.");
        });

        test("TransactionIsolation: Committed transaction cannot be rolled back or tracked further", () =>
        {
            var transaction = new OwnedAttachmentTransaction<MockSlot>();
            var committedObject = new MockSlot("CommittedObject");
            transaction.Track(committedObject);

            transaction.Commit();

            // Attempting rollback after commit must be a no-op
            var rollbackErrors = transaction.RollBack(slot => slot.Destroy());
            check(rollbackErrors.Count == 0, "Rollback on committed transaction should return no errors.");
            check(!committedObject.IsDestroyed, "Committed object must never be destroyed by rollback.");

            // Attempting to track new objects after commit must throw
            bool threw = false;
            try { transaction.Track(new MockSlot("LateObject")); }
            catch (InvalidOperationException) { threw = true; }
            check(threw, "Tracking after commit must throw InvalidOperationException.");
        });

        // 3. Sibling failure isolation during concurrent reconstruction
        test("SiblingIsolation: Failed sibling is cleaned up while successful sibling completes", () => Run(async () =>
        {
            var worldRoot = new MockSlot("WorldRoot");
            var committedAvatar = worldRoot.AddChild("ExistingCommittedAvatar");

            var sessionStaging = worldRoot.AddChild("SessionStaging");
            var siblingASlot = sessionStaging.AddChild("AvatarA_Valid");
            var siblingBSlot = sessionStaging.AddChild("AvatarB_Corrupt");

            var tasks = new List<(string Name, MockSlot Slot, bool ShouldFail)>
            {
                ("AvatarA", siblingASlot, false),
                ("AvatarB", siblingBSlot, true),
                ("AvatarC", sessionStaging.AddChild("AvatarC_Valid"), false)
            };

            bool anyTaskFailed = false;
            var completedOutputs = new List<string>();

            await BoundedTaskScheduler.RunAsync(tasks, async (task, token) =>
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    if (task.ShouldFail)
                    {
                        throw new InvalidOperationException($"Corrupt payload in {task.Name}");
                    }
                    await Task.Yield();
                    lock (completedOutputs) completedOutputs.Add(task.Name);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    anyTaskFailed = true;
                    // Sibling failure cleanup policy: destroy aborted structure slot
                    task.Slot.Destroy();
                }
            }, maxDegreeOfParallelism: 2);

            check(anyTaskFailed, "Batch should record that a task failed.");
            check(completedOutputs.Contains("AvatarA") && completedOutputs.Contains("AvatarC"),
                "Successful siblings must complete and register their outputs.");
            check(!completedOutputs.Contains("AvatarB"), "Failed sibling must not register output.");
            check(siblingBSlot.IsDestroyed, "Failed sibling's structure slot must be destroyed.");
            check(!siblingASlot.IsDestroyed, "Successful sibling's structure slot must remain alive.");

            // Verify pre-existing committed avatar in world is completely untouched
            check(!committedAvatar.IsDestroyed, "Existing committed avatar in world must remain intact.");
            check(committedAvatar.Parent == worldRoot, "Existing committed avatar hierarchy must not change.");
        }));

        test("SiblingIsolation: Cancellation halts pending tasks without destroying already committed outputs", () => Run(async () =>
        {
            var worldRoot = new MockSlot("WorldRoot");
            var existingCommitted = worldRoot.AddChild("CommittedOutput");

            using var cts = new CancellationTokenSource();
            var startedTasks = new List<int>();
            int taskCounter = 0;

            var items = Enumerable.Range(0, 10).ToList();

            bool cancelledThrown = false;
            try
            {
                await BoundedTaskScheduler.RunAsync(items, async (item, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    int id = Interlocked.Increment(ref taskCounter);
                    lock (startedTasks) startedTasks.Add(id);

                    if (id == 2)
                    {
                        cts.Cancel();
                    }

                    await Task.Delay(50, token);
                }, maxDegreeOfParallelism: 2, cancellationToken: cts.Token);
            }
            catch (OperationCanceledException)
            {
                cancelledThrown = true;
            }

            check(cancelledThrown, "Cancellation should rethrow OperationCanceledException.");
            check(startedTasks.Count < 10, "Cancellation must prevent queued tasks from starting.");
            check(!existingCommitted.IsDestroyed, "Cancellation must never damage existing committed output.");
        }));
    }

    private static void Run(Func<Task> action) => Task.Run(action).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
}
