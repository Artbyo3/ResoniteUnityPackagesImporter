using System;
using UnityPackageImporter.Models;

namespace UnityPackageImporter.Conversion;


internal interface IAvatarPlanSink
{
    void AddOperation(AvatarBuildOperation operation);
    void AddDiagnostic(AvatarOperationSeverity severity, string message, string context = "", string code = "");
}

internal sealed class AvatarPlanSink : IAvatarPlanSink
{
    private readonly AvatarBuildPlan plan;
    internal AvatarPlanSink(AvatarBuildPlan plan) => this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
    public void AddOperation(AvatarBuildOperation operation) => plan.AddOperation(operation);
    public void AddDiagnostic(AvatarOperationSeverity severity, string message, string context = "", string code = "") =>
        plan.AddDiagnostic(severity, message, context, code);
}
