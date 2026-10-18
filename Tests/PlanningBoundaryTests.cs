using System.Reflection;
using System.Reflection.Emit;
using UnityPackageImporter.Conversion;
using UnityPackageImporter.Frameworks.ModularAvatar;
using UnityPackageImporter.Frameworks.VrcFury;
using UnityPackageImporter.Models;

// Checks compiled dependencies, including fully qualified calls and compiler-generated lambdas.
// This prevents a later adapter change from reaching the engine, UI, another adapter or the whole plan.
internal static class PlanningBoundaryTests
{
    internal static void Register(Action<string, Action> test)
    {
        var operations = new[] { typeof(IAvatarPlanSink), typeof(AvatarBuildOperation), typeof(ArmatureMergeOperation),
            typeof(BoneAttachmentOperation), typeof(AvatarBuildOperationSource), typeof(AvatarOperationSeverity) };
        test("Modular Avatar planning boundary excludes other frameworks and runtime services", () =>
            Verify(typeof(ModularAvatarPlanAdapter), operations.Concat(new[] { typeof(ModularAvatarComponentDefinition), typeof(ModularAvatarComponentKind) })));
        test("VRCFury planning boundary excludes other frameworks and runtime services", () =>
            Verify(typeof(VrcFuryPlanAdapter), operations.Concat(new[] { typeof(VrcFuryComponentDefinition), typeof(VrcFuryArmatureLinkDefinition), typeof(VrcFuryLinkToTarget) })));
        test("Shared planning validation depends only on shared operation contracts", () =>
            Verify(typeof(AvatarPlanValidator), operations.Concat(new[] { typeof(AvatarBuildPlan), typeof(SkinRebindOperation) })));
    }

    private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!).ToDictionary(c => c.Value);

    private static void Verify(Type root, IEnumerable<Type> permitted)
    {
        var allowed = permitted.ToHashSet();
        void CheckType(Type? type)
        {
            if (type == null || type.IsGenericParameter) return;
            if (type.HasElementType) { CheckType(type.GetElementType()); return; }
            if (type.IsGenericType) foreach (var argument in type.GetGenericArguments()) CheckType(argument);
            if (type.Namespace == "System" || type.Namespace?.StartsWith("System.", StringComparison.Ordinal) == true) return;
            for (Type? owner = type; owner != null; owner = owner.DeclaringType) if (owner == root) return;
            if (!allowed.Contains(type)) throw new Exception($"{root.Name} crosses its planning boundary through {type.FullName}.");
        }
        void Inspect(Type type)
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)) CheckType(field.FieldType);
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly).Cast<MethodBase>()
                .Concat(type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance));
            foreach (var method in methods)
            {
                if (method is MethodInfo info) CheckType(info.ReturnType);
                foreach (var p in method.GetParameters()) CheckType(p.ParameterType);
                var body = method.GetMethodBody();
                if (body == null) continue;
                foreach (var local in body.LocalVariables) CheckType(local.LocalType);
                var il = body.GetILAsByteArray()!;
                for (int i = 0; i < il.Length;)
                {
                    short code = il[i++];
                    if (code == 0xfe) code = unchecked((short)(0xfe00 | il[i++]));
                    var operand = Codes[code].OperandType;
                    if (operand is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok)
                    {
                        var member = method.Module.ResolveMember(BitConverter.ToInt32(il, i));
                        if (member is Type dependency) CheckType(dependency);
                        else CheckType(member?.DeclaringType);
                    }
                    i += operand switch
                    {
                        OperandType.InlineNone => 0,
                        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                        OperandType.InlineVar => 2,
                        OperandType.InlineI8 or OperandType.InlineR => 8,
                        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, i),
                        _ => 4
                    };
                }
            }
            foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)) Inspect(nested);
        }
        Inspect(root);
    }
}
