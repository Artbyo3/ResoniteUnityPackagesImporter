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
    InvalidStructuredData, UnsupportedOperation, ObjectDisposed, NativeLibraryUnavailable
}

// Native text is input to a classifier, never text to echo. Only fixed diagnoses leave it.
internal static class ReportFailureDetails
{
    private static readonly Regex BinaryIdType = new(
        @"\A(?:Error importing file: )?FBX-Parser \(TOK_DATA, offset 0x[0-9a-fA-F]{1,16}\) failed to parse ID, unexpected data type, expected L\(ong\) \(binary\)\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex GeneratedMethod = new(@"<([^<>]+)>", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    internal static ReportFailureReason Classify(Exception error)
    {
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
