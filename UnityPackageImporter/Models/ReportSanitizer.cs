#nullable enable
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace UnityPackageImporter.Models;

internal sealed class ReportSanitizer
{
    private static readonly Regex Email = new(@"[\w.!#$%&'*+/=?^`{|}~-]+@[\w.-]+\.[\p{L}]{2,}", RegexOptions.CultureInvariant);
    private static readonly Regex Url = new(@"(?i)(?:[a-z][a-z0-9+.-]*://|www\.)[^\s<>""']+", RegexOptions.CultureInvariant);
    private static readonly Regex Controls = new(@"\p{C}+", RegexOptions.CultureInvariant);
    private readonly string[] privateValues;

    internal ReportSanitizer(params string?[] privateValues) =>
        this.privateValues = privateValues.Where(v => !string.IsNullOrWhiteSpace(v)).Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(v => v.Length).ToArray();

    internal static ReportSanitizer ForCurrentUser() => new(Environment.UserName, Environment.MachineName,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetEnvironmentVariable("HOME"),
        Environment.GetEnvironmentVariable("USERPROFILE"));

    internal string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        value = value[..Math.Min(value.Length, 4096)];
        value = ImportFailureReport.Sanitize(value);
        value = Email.Replace(value, "<email>");
        value = Url.Replace(value, "<url>");
        foreach (string secret in privateValues)
            value = Regex.Replace(value, Regex.Escape(secret),
                "<private>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        value = Controls.Replace(value, " ").Trim();
        return value[..Math.Min(value.Length, 512)];
    }

    internal string Token(string? value) =>
        value != null && Regex.IsMatch(value, @"\A[A-Za-z_][A-Za-z0-9_.+`<>-]{0,119}\z", RegexOptions.CultureInvariant)
            ? Clean(value) : "Unknown";
}
