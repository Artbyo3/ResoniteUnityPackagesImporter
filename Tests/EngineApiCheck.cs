using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Collections.Immutable;

// Reads assembly metadata without loading or running any game code.
internal static class EngineApiCheck
{
    public static int Run(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var expected = new[]
        {
            ("ModelImporter", "PreprocessScene", 1),
            ("ModelImporter", "ImportNode", 3),
            ("ModelImporter", "SetupDraggable", 5),
            ("ModelImportData", ".ctor", 6),
            ("ModelImportData", "TryGetSlot", 1),
            ("SkinnedMeshRenderer", "SetupBlendShapes", 0)
        };
        int failed = 0;
        foreach (var (typeName, methodName, count) in expected)
        {
            var matches = new List<string>();
            foreach (var handle in metadata.TypeDefinitions)
            {
                var type = metadata.GetTypeDefinition(handle);
                if (metadata.GetString(type.Name) != typeName) continue;
                foreach (var methodHandle in type.GetMethods())
                {
                    var method = metadata.GetMethodDefinition(methodHandle);
                    if (metadata.GetString(method.Name) != methodName) continue;
                    var decoded = method.DecodeSignature(new TypeNames(), (object?)null);
                    if (methodName == "TryGetSlot" && !decoded.ParameterTypes.SequenceEqual(new[] { "Assimp.Node" })) continue;
                    var signature = metadata.GetBlobReader(method.Signature);
                    var header = signature.ReadSignatureHeader();
                    if (header.IsGeneric) signature.ReadCompressedInteger();
                    if (signature.ReadCompressedInteger() != count) continue;
                    matches.Add(string.Join(", ", method.GetParameters().Select(h => metadata.GetParameter(h))
                        .Where(p => p.SequenceNumber > 0).Select(p => metadata.GetString(p.Name))));
                }
            }
            bool found = matches.Count == 1;
            if (!found) failed++;
            Console.WriteLine($"{(found ? "PASS" : "FAIL")} {typeName}.{methodName} ({string.Join(" | ", matches)})");
        }
        Console.WriteLine("Metadata checks only; execution still requires an in-game test.");
        return failed == 0 ? 0 : 1;
    }

    public static int DumpType(string assemblyPath, string typeName)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        foreach (var handle in metadata.TypeDefinitions)
        {
            var type = metadata.GetTypeDefinition(handle);
            if (!metadata.GetString(type.Name).Contains(typeName, StringComparison.OrdinalIgnoreCase)) continue;
            var declaringHandle = type.GetDeclaringType();
            string declaring = declaringHandle.IsNil ? "" : (metadata.GetString(metadata.GetTypeDefinition(declaringHandle).Name) + "+");
            Console.WriteLine($"Type: {metadata.GetString(type.Namespace)}.{declaring}{metadata.GetString(type.Name)}");
            if (!type.BaseType.IsNil)
            {
                if (type.BaseType.Kind == HandleKind.TypeReference)
                {
                    var baseRef = metadata.GetTypeReference((TypeReferenceHandle)type.BaseType);
                    Console.WriteLine($"Base: {metadata.GetString(baseRef.Namespace)}.{metadata.GetString(baseRef.Name)}");
                }
                else if (type.BaseType.Kind == HandleKind.TypeSpecification)
                {
                    var baseSpec = metadata.GetTypeSpecification((TypeSpecificationHandle)type.BaseType);
                    var decoded = baseSpec.DecodeSignature(new TypeNames(), (object?)null);
                    Console.WriteLine($"Base: {decoded}");
                }
            }
            Console.WriteLine("Properties:");
            foreach (var propHandle in type.GetProperties())
            {
                var prop = metadata.GetPropertyDefinition(propHandle);
                Console.WriteLine($"  {metadata.GetString(prop.Name)}");
            }
            Console.WriteLine("Fields:");
            foreach (var fieldHandle in type.GetFields())
            {
                var field = metadata.GetFieldDefinition(fieldHandle);
                var sig = field.DecodeSignature(new TypeNames(), (object?)null);
                Console.WriteLine($"  {metadata.GetString(field.Name)} : {sig}");
            }
            Console.WriteLine("Methods:");
            foreach (var methodHandle in type.GetMethods())
            {
                var method = metadata.GetMethodDefinition(methodHandle);
                string mName = metadata.GetString(method.Name);
                var decoded = method.DecodeSignature(new TypeNames(), (object?)null);
                Console.WriteLine($"  {mName}({string.Join(", ", decoded.ParameterTypes)}) : {decoded.ReturnType}");
            }
        }
        return 0;
    }

    private sealed class TypeNames : ISignatureTypeProvider<string, object?>
    {
        public string GetArrayType(string type, ArrayShape shape) => type + "[]";
        public string GetByReferenceType(string type) => type + "&";
        public string GetFunctionPointerType(MethodSignature<string> signature) => "function";
        public string GetGenericInstantiation(string type, ImmutableArray<string> args) => type + "<" + string.Join(",", args) + ">";
        public string GetGenericMethodParameter(object? context, int index) => "!!" + index;
        public string GetGenericTypeParameter(object? context, int index) => "!" + index;
        public string GetModifiedType(string modifier, string type, bool required) => type;
        public string GetPinnedType(string type) => type;
        public string GetPointerType(string type) => type + "*";
        public string GetPrimitiveType(PrimitiveTypeCode code) => code.ToString();
        public string GetSZArrayType(string type) => type + "[]";
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte kind)
        {
            var type = reader.GetTypeDefinition(handle);
            return reader.GetString(type.Namespace) + "." + reader.GetString(type.Name);
        }
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte kind)
        {
            var type = reader.GetTypeReference(handle);
            return reader.GetString(type.Namespace) + "." + reader.GetString(type.Name);
        }
        public string GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte kind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, context);
    }
}
