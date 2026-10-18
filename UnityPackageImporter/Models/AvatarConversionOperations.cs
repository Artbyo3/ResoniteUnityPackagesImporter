#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UnityPackageImporter.Models;

public enum AvatarBuildOperationKind
{
    ArmatureMerge,
    BoneAttachment,
    SkinRebind,
    TransformAlignment
}

public enum AvatarOperationSeverity
{
    Info,
    Warning,
    Error
}

public enum AvatarOperationExecutionState
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Cancelled,
    AwaitingInstallation
}

public sealed class AvatarOperationDiagnostic
{
    public AvatarOperationSeverity Severity { get; init; } = AvatarOperationSeverity.Info;
    public string Message { get; init; } = "";
    public string SourceContext { get; init; } = "";
    public string Code { get; init; } = "";

    public override string ToString() =>
        $"[{Severity}] {(string.IsNullOrEmpty(Code) ? "" : "(" + Code + ") ")}{SourceContext}: {Message}";
}

public sealed class AvatarBuildOperationSource
{
    public string Framework { get; init; } = ""; // "ModularAvatar", "VRCFury", or "Native"
    public string ComponentType { get; init; } = ""; // e.g. "ModularAvatarMergeArmature", "VRCFuryArmatureLink"
    public long ComponentFileId { get; init; }
    public long GameObjectFileId { get; init; }
    public string GameObjectName { get; init; } = "";
    public string SourcePath { get; init; } = "";

    public override string ToString() =>
        $"{Framework}:{ComponentType} on '{GameObjectName}' (GO:{GameObjectFileId}, Comp:{ComponentFileId})";
}

public abstract class AvatarBuildOperation
{
    public AvatarOperationExecutionState ExecutionState { get; internal set; }
    private string? operationId;
    public string OperationId
    {
        get => operationId ?? FormattableString.Invariant($"{Source.Framework}:{Source.ComponentType}:{Source.GameObjectFileId}:{Source.ComponentFileId}");
        init => operationId = value;
    }
    internal void AssignOperationId(string value) => operationId = value;
    public abstract AvatarBuildOperationKind Kind { get; }
    public AvatarBuildOperationSource Source { get; init; } = new();
    public List<string> DependsOnOperationIds { get; init; } = new();
    public List<AvatarOperationDiagnostic> Diagnostics { get; init; } = new();

    public bool IsValid => !Diagnostics.Any(d => d.Severity == AvatarOperationSeverity.Error);

    public void AddDiagnostic(AvatarOperationSeverity severity, string message, string code = "")
    {
        if (Diagnostics.Any(d => d.Severity == severity && d.Code == code && d.Message == message)) return;
        Diagnostics.Add(new AvatarOperationDiagnostic
        {
            Severity = severity,
            Message = message,
            SourceContext = Source.ToString(),
            Code = code
        });
    }
}

/// <summary>Resolves only declared targets; never invents a replacement target.</summary>
public static class AvatarTargetResolution
{
    public static T? Resolve<T>(AvatarBuildOperation operation, T avatarRoot,
        Func<long, T?> resolveObject, Func<int, T?> resolveBone, Func<T, string, T?> resolvePath) where T : class
    {
        var (bone, objectId, path, offset) = operation switch
        {
            ArmatureMergeOperation m => (m.TargetHumanoidBone, m.TargetObjectFileId, m.TargetReferencePath, m.TargetOffsetPath),
            BoneAttachmentOperation a => (a.TargetHumanoidBone, a.TargetObjectFileId, a.TargetReferencePath, a.TargetOffsetPath),
            _ => throw new ArgumentException("Operation does not have an attachment target.", nameof(operation))
        };
        if (bone < -1 || bone >= 55)
        {
            operation.AddDiagnostic(AvatarOperationSeverity.Error, $"Invalid humanoid bone index {bone}.", "INVALID_HUMANOID_BONE");
            return null;
        }

        T? target = objectId is null or 0 ? null : resolveObject(objectId.Value);
        if (target == null && bone >= 0) target = resolveBone(bone);
        if (target == null && !string.IsNullOrWhiteSpace(path))
            target = path == "$$$AVATAR_ROOT$$$" ? avatarRoot : resolvePath(avatarRoot, path);
        if (target != null && !string.IsNullOrWhiteSpace(offset))
            target = resolvePath(target, offset);
        if (target == null)
            operation.AddDiagnostic(AvatarOperationSeverity.Error, "The declared target or its offset path could not be resolved.", "TARGET_NOT_FOUND");
        return target;
    }
}

public sealed class ArmatureMergeOperation : AvatarBuildOperation
{
    public override AvatarBuildOperationKind Kind => AvatarBuildOperationKind.ArmatureMerge;

    public long SourceArmatureRootFileId { get; init; }
    public string SourceArmatureRootName { get; init; } = "";

    public string TargetReferencePath { get; init; } = "";
    public int TargetHumanoidBone { get; init; } = -1; // UnityEngine.HumanBodyBones numeric value (-1 if path/obj used)
    public long? TargetObjectFileId { get; init; }
    public string TargetOffsetPath { get; init; } = "";

    public string Prefix { get; init; } = "";
    public string Suffix { get; init; } = "";
    public bool Recursive { get; init; } = true;
    public string RemoveBoneSuffix { get; init; } = "";

    public bool AlignPosition { get; init; }
    public bool AlignRotation { get; init; }
    public bool AlignScale { get; init; }
    public bool AutoScaleFactor { get; init; } = true;

    public List<MergeBoneMapping> Mappings { get; init; } = new();
    public List<RetainedBoneRoot> RetainedRoots { get; init; } = new();
}

public sealed class BoneAttachmentOperation : AvatarBuildOperation
{
    public override AvatarBuildOperationKind Kind => AvatarBuildOperationKind.BoneAttachment;

    public long SourceObjectFileId { get; init; }
    public string SourceObjectName { get; init; } = "";

    public string TargetReferencePath { get; init; } = "";
    public int TargetHumanoidBone { get; init; } = -1;
    public long? TargetObjectFileId { get; init; }
    public string TargetOffsetPath { get; init; } = "";

    public bool AlignPosition { get; init; }
    public bool AlignRotation { get; init; }
    public bool AlignScale { get; init; }
    public bool AutoScaleFactor { get; init; }
    public bool KeepOffsets { get; init; } = true;
}

public sealed class SkinRebindOperation : AvatarBuildOperation
{
    public override AvatarBuildOperationKind Kind => AvatarBuildOperationKind.SkinRebind;

    public long RendererFileId { get; init; }
    public string RendererName { get; init; } = "";
    public long? OriginalRootBoneFileId { get; init; }
    public long? NewRootBoneFileId { get; init; }
    public Dictionary<long, long> BoneFileIdMap { get; init; } = new();
}

public sealed class AvatarBuildPlan
{
    private readonly HashSet<string> assignedIds = new(StringComparer.Ordinal);
    public string AvatarName { get; init; } = "";
    public List<AvatarBuildOperation> Operations { get; init; } = new();
    public List<AvatarOperationDiagnostic> Diagnostics { get; init; } = new();

    public bool HasErrors => Diagnostics.Any(d => d.Severity == AvatarOperationSeverity.Error) ||
                             Operations.Any(op => !op.IsValid);

    public bool HasWarnings => Diagnostics.Any(d => d.Severity == AvatarOperationSeverity.Warning) ||
                               Operations.Any(op => op.Diagnostics.Any(d => d.Severity == AvatarOperationSeverity.Warning));

    // Assign before wiring dependencies. Directly constructed plans are still
    // checked for duplicate IDs by GetOrderedOperations.
    internal void AddOperation(AvatarBuildOperation operation)
    {
        string baseId = operation.OperationId;
        string id = baseId;
        int suffix = 2;
        while (!assignedIds.Add(id))
            id = baseId + ":" + (suffix++).ToString(System.Globalization.CultureInfo.InvariantCulture);
        operation.AssignOperationId(id);
        Operations.Add(operation);
    }

    public void AddDiagnostic(AvatarOperationSeverity severity, string message, string context = "", string code = "")
    {
        Diagnostics.Add(new AvatarOperationDiagnostic
        {
            Severity = severity,
            Message = message,
            SourceContext = context,
            Code = code
        });
    }

    public IReadOnlyList<AvatarBuildOperation> GetOrderedOperations()
    {
        if (Operations.Any(o => string.IsNullOrWhiteSpace(o.OperationId)) ||
            Operations.Select(o => o.OperationId).Distinct(StringComparer.Ordinal).Count() != Operations.Count)
        {
            AddOrderingError("Build operation IDs must be nonempty and unique.", "DUPLICATE_OPERATION_ID");
            return Operations.AsReadOnly();
        }
        var opMap = Operations.ToDictionary(o => o.OperationId, StringComparer.Ordinal);
        var inDegree = Operations.ToDictionary(o => o.OperationId, _ => 0, StringComparer.Ordinal);
        var adj = Operations.ToDictionary(o => o.OperationId, _ => new List<string>(), StringComparer.Ordinal);

        foreach (var op in Operations)
        {
            foreach (var depId in op.DependsOnOperationIds)
            {
                if (opMap.ContainsKey(depId))
                {
                    adj[depId].Add(op.OperationId);
                    inDegree[op.OperationId]++;
                }
                else
                {
                    AddOrderingError($"Dependency '{depId}' does not exist in the plan.", "DEPENDENCY_NOT_FOUND");
                }
            }
        }

        var queue = new Queue<string>(Operations.Where(o => inDegree[o.OperationId] == 0).Select(o => o.OperationId));
        var result = new List<AvatarBuildOperation>();

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            result.Add(opMap[currentId]);

            foreach (var nextId in adj[currentId])
            {
                inDegree[nextId]--;
                if (inDegree[nextId] == 0)
                    queue.Enqueue(nextId);
            }
        }

        if (result.Count < Operations.Count)
        {
            AddOrderingError("Cyclic dependency detected between build operations.", "CYCLE_DETECTED");

            foreach (var op in Operations)
            {
                if (!result.Contains(op))
                    result.Add(op);
            }
        }

        return result.AsReadOnly();
    }

    private void AddOrderingError(string message, string code)
    {
        if (!Diagnostics.Any(d => d.Code == code && d.Message == message))
            AddDiagnostic(AvatarOperationSeverity.Error, message, AvatarName, code);
    }
}

/// <summary>
/// Engine-independent execution boundary. Ordering errors, diagnostic failures,
/// and exceptions all stop the plan; cancellation remains cancellation.
/// The caller owns staged output and reads plan.HasErrors for its receipt.
/// </summary>
public static class AvatarPlanExecution
{
    internal static void DeferSupportedInstallation(AvatarBuildPlan plan)
    {
        if (plan.Operations.Any(op => op.ExecutionState != AvatarOperationExecutionState.Pending))
            throw new InvalidOperationException("Only a new conversion plan can be deferred for installation.");
        foreach (var operation in plan.Operations)
            if (operation.Source.Framework == "ModularAvatar" &&
                operation is ArmatureMergeOperation or BoneAttachmentOperation)
                operation.ExecutionState = AvatarOperationExecutionState.AwaitingInstallation;
    }

    public static async Task ExecuteAsync(
        AvatarBuildPlan plan,
        Func<AvatarBuildOperation, CancellationToken, Task> executeOperation,
        CancellationToken cancellationToken = default)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        if (executeOperation == null) throw new ArgumentNullException(nameof(executeOperation));
        cancellationToken.ThrowIfCancellationRequested();
        if (plan.Operations.Any(op => op.ExecutionState != AvatarOperationExecutionState.Pending))
            throw new InvalidOperationException("A conversion plan cannot be executed twice. Reconstruct a fresh staged source first.");
        var ordered = plan.GetOrderedOperations();
        if (plan.HasErrors)
        {
            if (!plan.Diagnostics.Any(d => d.Code == "PLAN_EXECUTION_BLOCKED"))
                plan.AddDiagnostic(AvatarOperationSeverity.Error, "Conversion was not executed because the plan has errors.", plan.AvatarName, "PLAN_EXECUTION_BLOCKED");
            return;
        }

        foreach (var operation in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            operation.ExecutionState = AvatarOperationExecutionState.Running;
            try
            {
                await executeOperation(operation, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                operation.ExecutionState = AvatarOperationExecutionState.Cancelled;
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                operation.AddDiagnostic(AvatarOperationSeverity.Error, ex.Message, "EXECUTION_ERROR");
            }
            if (cancellationToken.IsCancellationRequested)
                operation.ExecutionState = AvatarOperationExecutionState.Cancelled;
            cancellationToken.ThrowIfCancellationRequested();
            operation.ExecutionState = plan.HasErrors
                ? AvatarOperationExecutionState.Failed : AvatarOperationExecutionState.Succeeded;
            if (plan.HasErrors) return;
        }
    }
}
