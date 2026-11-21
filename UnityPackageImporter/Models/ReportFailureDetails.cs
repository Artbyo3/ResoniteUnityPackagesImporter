#nullable enable
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace UnityPackageImporter.Models;

internal enum ReportFailureReason
{
    Unknown, FbxInvalidIdType, FbxParserRejectedData, ModelFormatUnsupported,
    ModelUnexpectedEnd, FileMissing, AccessDenied, OutOfMemory, InvalidNumber,
    InvalidStructuredData, UnsupportedOperation, ObjectDisposed, NativeLibraryUnavailable,
    PrefabExpansionUnsupported, PrefabParentMissing, PrefabParentUnresolved,
    PrefabInstanceUnresolved, PrefabTargetUnresolved, HierarchyGameObjectUnresolved,
    HierarchyParentMissing, HierarchyParentUnresolved, SerializedSkinSourceUnsupported,
    ModelSourceMissing, ModelTemplateUnavailable, SkinRendererUnresolved, ModelImportInterrupted,
    NativeMaterialMappingFailed, SerializedSkinDecodeRejected
}


internal static class ReportFailureDetails
{
    private static readonly object ReasonKey = new();

    internal static T Tag<T>(T error, ReportFailureReason reason) where T : Exception
    {
        ArgumentNullException.ThrowIfNull(error);
        if (reason == ReportFailureReason.Unknown || !Enum.IsDefined(reason)) throw new ArgumentOutOfRangeException(nameof(reason));
        error.Data[ReasonKey] = reason;
        return error;
    }

    internal static ReportFailureReason MissingSkinSource(bool sourceIncluded, string? extension) =>
        !sourceIncluded ? ReportFailureReason.ModelSourceMissing :
        string.Equals(extension, ".asset", StringComparison.OrdinalIgnoreCase) ? ReportFailureReason.SerializedSkinSourceUnsupported :
        ReportFailureReason.ModelTemplateUnavailable;

    private static readonly Regex BinaryIdType = new(
        @"\A(?:Error importing file: )?FBX-Parser \(TOK_DATA, offset 0x[0-9a-fA-F]{1,16}\) failed to parse ID, unexpected data type, expected L\(ong\) \(binary\)\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex GeneratedMethod = new(@"<([^<>]+)>", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    internal static ReportFailureReason Classify(Exception error)
    {
        if (error.Data[ReasonKey] is ReportFailureReason reason && Enum.IsDefined(reason)) return reason;
        if (error.GetType().FullName == "Assimp.AssimpException") return Assimp(error.Message);
        return error switch
        {
            System.IO.FileNotFoundException or System.IO.DirectoryNotFoundException => ReportFailureReason.FileMissing,
            UnauthorizedAccessException => ReportFailureReason.AccessDenied,
            OutOfMemoryException => ReportFailureReason.OutOfMemory,
            FormatException or OverflowException => ReportFailureReason.InvalidNumber,
            System.IO.EndOfStreamException => ReportFailureReason.ModelUnexpectedEnd,
            System.IO.InvalidDataException => ReportFailureReason.InvalidStructuredData,
            NotSupportedException => ReportFailureReason.UnsupportedOperation,
            ObjectDisposedException => ReportFailureReason.ObjectDisposed,
            DllNotFoundException or BadImageFormatException or EntryPointNotFoundException => ReportFailureReason.NativeLibraryUnavailable,
            _ => ReportFailureReason.Unknown
        };
    }

    internal static ReportFailureReason Assimp(string? message)
    {
        if (string.IsNullOrEmpty(message) || message.Length > 4096) return ReportFailureReason.Unknown;
        if (BinaryIdType.IsMatch(message)) return ReportFailureReason.FbxInvalidIdType;
        string value = message.StartsWith("Error importing file: ", StringComparison.Ordinal) ? message[22..] : message;
        if (value.StartsWith("FBX-Parser (", StringComparison.Ordinal) || value.StartsWith("FBX-DOM (", StringComparison.Ordinal) ||
            value.StartsWith("FBX-Tokenize (", StringComparison.Ordinal)) return ReportFailureReason.FbxParserRejectedData;
        if (value.StartsWith("No suitable reader found for the file format", StringComparison.Ordinal)) return ReportFailureReason.ModelFormatUnsupported;
        if (value.StartsWith("Unexpected end of file", StringComparison.OrdinalIgnoreCase)) return ReportFailureReason.ModelUnexpectedEnd;
        return ReportFailureReason.Unknown;
    }

    internal static string Describe(ReportFailureReason reason) => reason switch
    {
        ReportFailureReason.FbxInvalidIdType => "The FBX binary parser rejected an ID: it expected a 64-bit integer and encountered another data type.",
        ReportFailureReason.FbxParserRejectedData => "The FBX parser rejected the model data. The raw parser text is withheld because it may contain content identifiers.",
        ReportFailureReason.ModelFormatUnsupported => "The model library could not find a reader for the supplied model format.",
        ReportFailureReason.ModelUnexpectedEnd => "A reader reached the end of the input before completing the expected data.",
        ReportFailureReason.FileMissing => "A required file or directory was unavailable.",
        ReportFailureReason.AccessDenied => "Access to a required resource was denied.",
        ReportFailureReason.OutOfMemory => "The process could not allocate the memory required by the operation.",
        ReportFailureReason.InvalidNumber => "A value could not be read as the expected numeric type or exceeded its range.",
        ReportFailureReason.InvalidStructuredData => "Structured input data could not be read. See the affected object and operation diagnostics.",
        ReportFailureReason.UnsupportedOperation => "The requested operation or data type is unsupported at this code path.",
        ReportFailureReason.ObjectDisposed => "A required object was already disposed.",
        ReportFailureReason.NativeLibraryUnavailable => "A required native library or entry point could not be loaded.",
        ReportFailureReason.PrefabExpansionUnsupported => "An included nested prefab requires expansion that this reconstruction path does not support.",
        ReportFailureReason.PrefabParentMissing => "The declared parent transform of a prefab instance was not found in this structure.",
        ReportFailureReason.PrefabParentUnresolved => "The prefab parent transform exists but could not resolve to a native slot.",
        ReportFailureReason.PrefabInstanceUnresolved => "A transform's prefab instance could not be resolved; source availability and instance composition require inspection.",
        ReportFailureReason.PrefabTargetUnresolved => "The prefab reference did not resolve to the required transform type.",
        ReportFailureReason.HierarchyGameObjectUnresolved => "A transform could not resolve its associated GameObject; the hierarchy cannot be reconstructed faithfully.",
        ReportFailureReason.HierarchyParentMissing => "A declared parent transform was not found in this structure.",
        ReportFailureReason.HierarchyParentUnresolved => "A parent transform exists but its GameObject or native slot could not be resolved.",
        ReportFailureReason.SerializedSkinSourceUnsupported => "An included serialized asset has no imported skinned mesh template. This is not evidence of a missing FBX dependency or proof that the asset is valid.",
        ReportFailureReason.SerializedSkinDecodeRejected => "An included serialized skin mesh is malformed or outside the supported decoder layouts. No static substitute was created; this is not a missing FBX dependency.",
        ReportFailureReason.ModelSourceMissing => "The renderer's model source reference is not included in the indexed package dependencies.",
        ReportFailureReason.ModelTemplateUnavailable => "The model source is indexed but no imported skinned mesh template is available. Inspect model-stage failures and source format support.",
        ReportFailureReason.SkinRendererUnresolved => "No native skinned renderer could be reconstructed for this source object.",
        ReportFailureReason.ModelImportInterrupted => "The engine model-import coroutine ended without completing. This identifies an interruption, not its underlying cause.",
        ReportFailureReason.NativeMaterialMappingFailed => "The native material mapper failed for this model. Its original exception type and method frames are retained; no fallback material was substituted.",
        _ => "No safe structured explanation is available. Raw exception text is withheld; use the caller and typed diagnostics."
    };

    internal static string[] Methods(Exception error) => new StackTrace(error, false).GetFrames()?
        .Take(128).Select(frame => Method(frame.GetMethod())).Where(name => name != null)
        .Cast<string>().Distinct(StringComparer.Ordinal).Take(24).ToArray() ?? Array.Empty<string>();

    private static string? Method(MethodBase? method)
    {
        if (method?.DeclaringType is not Type type) return null;
        string owner = type.FullName ?? type.Name;
        if (owner.StartsWith("System.Runtime.CompilerServices.", StringComparison.Ordinal) ||
            owner.StartsWith("System.Runtime.ExceptionServices.", StringComparison.Ordinal) ||
            owner.StartsWith("System.Threading.Tasks.", StringComparison.Ordinal)) return null;
        string name = method.Name;
        if (name == "MoveNext" || name.StartsWith('<'))
        {
            var generated = GeneratedMethod.Match(name.StartsWith('<') ? name : type.Name);
            if (generated.Success) name = generated.Groups[1].Value;
            while (type.Name.StartsWith('<') && type.DeclaringType != null) type = type.DeclaringType;
        }
        return name is "MoveNext" or "Throw" ? null : (type.FullName ?? type.Name) + "." + name;
    }
}
