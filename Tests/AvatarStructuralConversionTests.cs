using UnityPackageImporter.Models;

internal static class AvatarStructuralConversionTests
{
    public static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        test("Structural plans have stable provenance IDs and collision suffixes", () =>
        {
            var manifest = FuryManifest("[{useBone: 1, bone: 0}]");
            var component = manifest.VrcFuryComponents.Single();
            manifest = new AvatarPackageManifest { VrcFuryComponents = new[] { component, component } };
            var first = AvatarBuildPlanner.CreatePlan(manifest);
            var second = AvatarBuildPlanner.CreatePlan(manifest);
            check(first.Operations.Select(o => o.OperationId).SequenceEqual(second.Operations.Select(o => o.OperationId)), "IDs changed across identical plans");
            check(first.Operations[0].OperationId == "VRCFury:VRCFuryArmatureLink:100:200", "Expected provenance ID");
            check(first.Operations[1].OperationId == first.Operations[0].OperationId + ":2", "Expected deterministic collision suffix");
        });

        foreach (var targets in new[] { "[]", "[{}]", "[{useBone: 0, bone: 0, useObj: 0, obj: {fileID: 0}}]", "[{useBone: 1}]", "[{useBone: 1, bone: garbage}]", "[{useObj: 1, obj: {fileID: 0}}]" })
        {
            test("Structural unresolved VRCFury target: " + targets, () =>
            {
                var plan = AvatarBuildPlanner.CreatePlan(FuryManifest(targets));
                check(plan.Operations.Count == 1, "Fixture did not reach the planner");
                check(plan.HasErrors && plan.Operations[0].Diagnostics.Any(d => d.Code == "TARGET_UNRESOLVED"), "Missing/malformed target must fail explicitly");
                check(((ArmatureMergeOperation)plan.Operations[0]).TargetHumanoidBone == -1, "Must not invent Hips");
            });
        }

        foreach (var targets in new[] { "[{useBone: 1, bone: 0}]", "[{useBone: true, bone: 0}]", "\n        - useBone: 1\n          bone: 0" })
        {
            test("Structural explicit Hips survives flow/block YAML: " + targets, () =>
            {
                var plan = AvatarBuildPlanner.CreatePlan(FuryManifest(targets));
                check(!plan.HasErrors, "Explicit Hips must remain supported");
                check(((ArmatureMergeOperation)plan.Operations.Single()).TargetHumanoidBone == 0, "Explicit Hips changed");
            });
        }

        test("Structural invalid humanoid bone fails during planning", () =>
        {
            var plan = AvatarBuildPlanner.CreatePlan(FuryManifest("\n        - useBone: 1\n          bone: 999"));
            check(plan.HasErrors && plan.Operations.Single().Diagnostics.Any(d => d.Code == "INVALID_HUMANOID_BONE"), "Invalid bone must fail before world mutation");
        });

        test("Structural MA implicit avatar root is explicit in diagnostics", () =>
        {
            var plan = AvatarBuildPlanner.CreatePlan(new AvatarPackageManifest
            {
                ModularAvatarComponents = new[] { new ModularAvatarComponentDefinition { Kind = ModularAvatarComponentKind.MergeArmature, GameObjectFileId = 100 } }
            });
            var op = (ArmatureMergeOperation)plan.Operations.Single();
            check(!plan.HasErrors && op.TargetReferencePath == "$$$AVATAR_ROOT$$$", "Default root semantics changed");
            check(op.Diagnostics.Any(d => d.Code == "DEFAULT_AVATAR_ROOT" && d.Severity == AvatarOperationSeverity.Info), "Implicit root must be explained");
        });

        test("Structural MA explicit object does not acquire an implicit root fallback", () =>
        {
            var plan = AvatarBuildPlanner.CreatePlan(new AvatarPackageManifest
            {
                ModularAvatarComponents = new[] { new ModularAvatarComponentDefinition { Kind = ModularAvatarComponentKind.MergeArmature, GameObjectFileId = 100, MergeTargetObjectFileId = 999 } }
            });
            check(((ArmatureMergeOperation)plan.Operations.Single()).TargetReferencePath == "", "Broken explicit reference must never become avatar root");
        });

        test("Structural duplicate operation IDs produce a diagnostic instead of throwing", () =>
        {
            var plan = new AvatarBuildPlan { Operations = new() { new BoneAttachmentOperation { OperationId = "same" }, new BoneAttachmentOperation { OperationId = "same" } } };
            check(plan.GetOrderedOperations().Count == 2 && plan.HasErrors, "Duplicate IDs must preserve inspectable operations and reject execution");
        });

        test("Structural cycles are reported once across repeated inspection", () =>
        {
            var plan = new AvatarBuildPlan { Operations = new()
            {
                new BoneAttachmentOperation { OperationId = "a", DependsOnOperationIds = new() { "b" } },
                new BoneAttachmentOperation { OperationId = "b", DependsOnOperationIds = new() { "a" } }
            } };
            check(plan.GetOrderedOperations().Count == 2 && plan.GetOrderedOperations().Count == 2, "Cycle must retain every operation");
            check(plan.Diagnostics.Count(d => d.Code == "CYCLE_DETECTED") == 1, "Inspection duplicated the cycle diagnostic");
        });

        test("Structural modern empty targets do not revive legacy Hips", () =>
        {
            var manifest = FuryManifest("[]\n        boneOnAvatar: 0");
            check(AvatarBuildPlanner.CreatePlan(manifest).HasErrors, "Explicit empty list revived legacy default");
        });

        test("Structural target offsets participate in conflict detection", () =>
        {
            var plan = new AvatarBuildPlan { Operations = new()
            {
                new BoneAttachmentOperation { OperationId = "a", SourceObjectFileId = 1, TargetHumanoidBone = 0, TargetOffsetPath = "Spine" },
                new BoneAttachmentOperation { OperationId = "b", SourceObjectFileId = 1, TargetHumanoidBone = 0, TargetOffsetPath = "Tail" }
            } };
            AvatarBuildPlanner.ValidatePlan(plan);
            AvatarBuildPlanner.ValidatePlan(plan);
            check(plan.HasErrors && plan.Operations[1].Diagnostics.Count(d => d.Code == "CONFLICTING_TARGETS") == 1, "Distinct offsets must conflict without duplicating diagnostics");
        });

        test("Structural indexed dependencies preserve chained attachment order", () =>
        {
            var plan = new AvatarBuildPlan { Operations = new()
            {
                new BoneAttachmentOperation { OperationId = "a", SourceObjectFileId = 1, TargetObjectFileId = 2 },
                new BoneAttachmentOperation { OperationId = "b", SourceObjectFileId = 2, TargetObjectFileId = 3 },
                new BoneAttachmentOperation { OperationId = "c", SourceObjectFileId = 3, TargetHumanoidBone = 0 }
            } };
            AvatarBuildPlanner.ValidatePlan(plan);
            AvatarBuildPlanner.ValidatePlan(plan);
            check(!plan.HasErrors && plan.GetOrderedOperations().Select(o => o.OperationId).SequenceEqual(new[] { "c", "b", "a" }), "Dependency order changed");
            check(plan.Operations[0].DependsOnOperationIds.SequenceEqual(new[] { "b" }), "Dependencies duplicated on revalidation");
        });

        test("Structural explicit object target preserves signed fileID without Hips", () =>
        {
            var plan = AvatarBuildPlanner.CreatePlan(FuryManifest("[{useBone: 0, useObj: true, obj: {fileID: -9223372036854775807}}]"));
            var op = (ArmatureMergeOperation)plan.Operations.Single();
            check(!plan.HasErrors && op.TargetObjectFileId == -9223372036854775807 && op.TargetHumanoidBone == -1, "Object target was lost or replaced by Hips");
        });

        test("Structural golden mixed plan and repeated validation stay stable", () =>
        {
            var fury = FuryManifest("[{useBone: 1, bone: 0}]");
            var manifest = new AvatarPackageManifest
            {
                VrcFuryComponents = fury.VrcFuryComponents,
                ModularAvatarComponents = new[]
                {
                    new ModularAvatarComponentDefinition { Kind = ModularAvatarComponentKind.MergeArmature, GameObjectFileId = 10, ComponentFileId = 11 },
                    new ModularAvatarComponentDefinition { Kind = ModularAvatarComponentKind.BoneProxy, GameObjectFileId = 20, ComponentFileId = 21, BoneReference = 10 }
                }
            };
            string Snapshot(AvatarBuildPlan p) => string.Join("\n", p.GetOrderedOperations().Select(o =>
                $"{o.OperationId}|{o.Kind}|{string.Join(",", o.Diagnostics.Select(d => d.Code).OrderBy(c => c, StringComparer.Ordinal))}"));
            const string expected = "ModularAvatar:ModularAvatarMergeArmature:10:11|ArmatureMerge|DEFAULT_AVATAR_ROOT\nModularAvatar:ModularAvatarBoneProxy:20:21|BoneAttachment|\nVRCFury:VRCFuryArmatureLink:100:200|ArmatureMerge|";
            var plan = AvatarBuildPlanner.CreatePlan(manifest);
            check(Snapshot(plan) == expected, "Golden plan changed: " + Snapshot(plan));
            AvatarBuildPlanner.ValidatePlan(plan);
            check(Snapshot(plan) == expected && Snapshot(AvatarBuildPlanner.CreatePlan(manifest)) == expected, "Replanning/revalidation changed output");
        });

        foreach (bool merge in new[] { true, false })
        {
            test($"Structural {(merge ? "merge" : "attachment")} rejects dangling explicit targets", () =>
            {
                AvatarBuildOperation op = merge ? new ArmatureMergeOperation { TargetObjectFileId = 999 } : new BoneAttachmentOperation { TargetObjectFileId = 999 };
                int boneLookups = 0;
                var target = AvatarTargetResolution.Resolve(op, "root", id => (string)null!, bone => { boneLookups++; return "Hips"; }, (root, path) => root);
                check(target == null && boneLookups == 0 && op.Diagnostics.Any(d => d.Code == "TARGET_NOT_FOUND"), "Dangling object became an implicit root/bone target");
            });
            test($"Structural {(merge ? "merge" : "attachment")} requires its offset to resolve", () =>
            {
                AvatarBuildOperation Op() => merge ? new ArmatureMergeOperation { TargetHumanoidBone = 0, TargetOffsetPath = "Chest" } : new BoneAttachmentOperation { TargetHumanoidBone = 0, TargetOffsetPath = "Chest" };
                var missing = Op();
                check(AvatarTargetResolution.Resolve(missing, "root", id => "obj", bone => "Hips", (root, path) => (string)null!) == null && !missing.IsValid, "Missing offset was silently ignored");
                var valid = Op();
                check(AvatarTargetResolution.Resolve(valid, "root", id => "obj", bone => "Hips", (root, path) => root + "/" + path) == "Hips/Chest" && valid.IsValid, "Valid offset was not applied");
            });
        }

        test("Structural runner rejects errors, duplicate IDs, missing dependencies and cycles before dispatch", () =>
        {
            foreach (string failure in new[] { "plan", "operation", "duplicate", "missing", "cycle" })
            {
                var plan = ExecutionPlan();
                if (failure == "plan") plan.AddDiagnostic(AvatarOperationSeverity.Error, "Rejected");
                if (failure == "operation") plan.Operations[1].AddDiagnostic(AvatarOperationSeverity.Error, "Rejected");
                if (failure == "duplicate") plan.Operations.Add(new BoneAttachmentOperation { OperationId = "a" });
                if (failure == "missing") plan.Operations[0].DependsOnOperationIds.Add("missing");
                if (failure == "cycle")
                {
                    plan.Operations[0].DependsOnOperationIds.Add("b");
                    plan.Operations[1].DependsOnOperationIds.Add("a");
                }
                int calls = 0;
                AvatarPlanExecution.ExecuteAsync(plan, (_, _) => { calls++; return Task.CompletedTask; }).GetAwaiter().GetResult();
                check(calls == 0 && plan.HasErrors && plan.Diagnostics.Any(d => d.Code == "PLAN_EXECUTION_BLOCKED"), "Dispatch escaped preflight for " + failure);
            }
        });

        test("Structural runner respects dependencies and allows warnings", () =>
        {
            var plan = ExecutionPlan();
            plan.Operations[0].DependsOnOperationIds.Add("b");
            plan.AddDiagnostic(AvatarOperationSeverity.Warning, "Deferred optional behavior");
            var calls = new List<string>();
            AvatarPlanExecution.ExecuteAsync(plan, (op, _) => { calls.Add(op.OperationId); return Task.CompletedTask; }).GetAwaiter().GetResult();
            check(calls.SequenceEqual(new[] { "b", "a" }) && !plan.HasErrors, "Valid plan was blocked or reordered incorrectly");
        });

        foreach (bool throws in new[] { true, false })
        {
            test("Structural runner aborts and reports partial on " + (throws ? "exception" : "diagnostic error"), () =>
            {
                var plan = ExecutionPlan();
                int calls = 0;
                AvatarPlanExecution.ExecuteAsync(plan, (op, _) =>
                {
                    calls++;
                    if (throws) return Task.FromException(new InvalidOperationException("Broken bind"));
                    op.AddDiagnostic(AvatarOperationSeverity.Error, "Missing target", "TARGET_NOT_FOUND");
                    return Task.CompletedTask;
                }).GetAwaiter().GetResult();
                check(calls == 1 && plan.HasErrors, "Execution continued after failure");
                check(plan.Operations[0].Diagnostics.Single().Code == (throws ? "EXECUTION_ERROR" : "TARGET_NOT_FOUND"), "Failure diagnostic lost");
                check(PrefabVariantResolver.EvaluatePartialCompatibility(0, plan.HasErrors), "Execution error must reach the partial receipt input");
            });
        }

        test("Structural runner propagates cancellation instead of recording success/error", () =>
        {
            var plan = ExecutionPlan();
            int calls = 0;
            bool cancelled = false;
            try
            {
                AvatarPlanExecution.ExecuteAsync(plan, (_, _) => { calls++; throw new OperationCanceledException(); }).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { cancelled = true; }
            check(cancelled && calls == 1 && !plan.HasErrors, "Cancellation was swallowed or misreported");
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            try
            {
                AvatarPlanExecution.ExecuteAsync(plan, (_, _) => { calls++; return Task.CompletedTask; }, cts.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { }
            check(calls == 1, "Pre-cancelled plan dispatched work");
        });

        test("Structural ambiguous merge mapping stops before the apply step", () =>
        {
            var op = new ArmatureMergeOperation { OperationId = "merge", TargetHumanoidBone = 0 };
            var plan = new AvatarBuildPlan { Operations = new() { op, new BoneAttachmentOperation { OperationId = "next" } } };
            var source = new MergeHierarchyNode { Id = "s", Name = "Hips", Children = new[] { new MergeHierarchyNode { Id = "s1", Name = "Spine" } } };
            var target = new MergeHierarchyNode { Id = "t", Name = "Hips", Children = new[] { new MergeHierarchyNode { Id = "t1", Name = "Spine" }, new MergeHierarchyNode { Id = "t2", Name = "Spine" } } };
            int calls = 0;
            bool applied = false;
            AvatarPlanExecution.ExecuteAsync(plan, (operation, _) =>
            {
                calls++;
                AvatarBuildPlanner.PlanArmatureMerge(op, source, target);
                if (op.IsValid) applied = true;
                return Task.CompletedTask;
            }).GetAwaiter().GetResult();
            check(calls == 1 && !applied && plan.HasErrors && op.Diagnostics.Any(d => d.Code == "AMBIGUOUS_BONE_MATCH"), "Ambiguous map reached apply or subsequent operations");
        });
    }

    private static AvatarBuildPlan ExecutionPlan() => new()
    {
        Operations = new() { new BoneAttachmentOperation { OperationId = "a", TargetHumanoidBone = 0 }, new BoneAttachmentOperation { OperationId = "b", TargetHumanoidBone = 10 } }
    };

    private static AvatarPackageManifest FuryManifest(string targets) => AvatarPackageIndex.ParseText($$"""
        --- !u!1 &100
        GameObject:
          m_Name: Costume
        --- !u!114 &200
        MonoBehaviour:
          m_GameObject: {fileID: 100}
          m_Script: {fileID: 11500000, guid: {{AvatarPackageIndex.VrcFuryFullScriptGuid}}, type: 3}
          references:
            version: 2
            RefIds:
            - rid: 300
              type: {class: ArmatureLink, ns: VF.Model.Feature, asm: VRCFury}
              data:
                propBone: {fileID: 100}
                linkTo: {{targets}}
                recursive: 1
        """, "structural.prefab");
}
