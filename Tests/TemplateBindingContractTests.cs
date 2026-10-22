using System.Text.Json;
using System.Text.RegularExpressions;

// Text-only contract audit: no mod assembly, engine types, or compiler services needed.
internal static class TemplateBindingContractTests
{
    private const string Station = "Models/UnityStationBuilder.cs";
    private const string Loader = "UI/StationTemplateLoader.cs";
    private const string Prompt = "UI/PreImportPrompt.cs";

    // Explicit ownership follows LoadAsync and the StationTemplate constructor handoffs.
    // A lookup in a new consumer fails until its matching contract is recorded here.
    private static readonly Dictionary<string, string> Contracts = new(StringComparer.Ordinal)
    {
        [Station] = "station",
        ["Models/MaterialCustomizerController.cs"] = "station",
        ["UI/ReusableDecisionPrompt.cs"] = "station",
        [Prompt] = "pre-import",
        ["UI/ImportErrorReportPanel.cs"] = "error-report",
        [Loader] = "both"
    };

    private sealed record DynamicUse(string File, string Method, string Argument, int Count,
        string Reason, params string[] Names);

    // These are call-site exceptions, not blanket permission to ignore nonliteral arguments.
    // Counts make newly added forwarding/dynamic calls require an explicit review.
    private static readonly DynamicUse[] DynamicAllowlist =
    [
        // Get/Bind forward key; the two foreach loops select these four literal binding names.
        new(Station, "Get", "key", 4, "Get/Bind forwarding and navigation/title loops",
            "previous", "next", "avatarTitle", "outfitTitle"),
        // The callback forwards keys from ReusableDecisionPrompt, whose literals are scanned.
        new(Station, "SetLocalizedKey", "driverKey", 1, "ReusableDecisionPrompt callback forwarding"),
        // Bind forwards the import/raw/cancel literals scanned at its callers.
        new(Prompt, "Get", "key", 1, "Pre-import Bind forwarding"),
        // Locale builds locale.<key>.<index> from the caller's base key.
        new(Loader, "Locale", "key", 1, "ApplyRawText forwards a locale group name"),
        // SetLocalized overloads forward their caller's key to the implementation.
        new(Loader, "SetLocalizedRaw", "key", 1, "SetLocalized overload forwarding"),
        new(Loader, "SetLocalizedKey", "key", 1, "SetLocalized overload forwarding"),
        // The loader enumerates locale groups from whichever contract it is loading.
        new(Loader, "SetLocalizedKey", "baseKey", 1, "Loader initializes each contract's locale groups"),
        new(Loader, "SetLocalizedRaw", "baseKey", 1, "Loader clears parameterized locale groups"),
        // Text application and locale refresh forward keys already checked at entry points.
        new(Loader, "ApplyRawText", "key", 1, "SetLocalizedRaw forwards its key"),
        new(Loader, "ApplyRawText", "driverKey", 1, "SetLocalizedKey forwards its driver key"),
        new(Loader, "ApplyRawText", "pair.Key", 3, "RefreshLocale replays active locale entries")
    ];

    // Loader-only reverse allowlist. Every unused key is still printed with its reason.
    private static readonly (Regex Pattern, string Reason)[] ReverseAllowlist =
    [
        // StationTemplateLoader enumerates these keys and initializes static translated text.
        (new(@"^locale\.[^.]+\.[0-9]+$", RegexOptions.CultureInvariant),
            "loader enumerates locale.<key>.<index> groups")
    ];

    private sealed record Token(string Text, int Line);
    private sealed record Lookup(string File, int Line, string Method, string Argument, string? Literal)
    {
        public bool Localized => Method is not ("Get" or "TryGet" or "Bind");
    }

    // Tokenize comments, strings, and chars before looking for invocations, so examples in
    // comments/strings cannot create false lookups. Single-character angle tokens handle
    // nested generics without depending on the engine-bound source being compilable.
    private static readonly Regex Tokens = new(""""
        //[^\r\n]*|/\*[\s\S]*?\*/|(?<raw>"{3,})[\s\S]*?\k<raw>|\$?@"(?:""|[^"])*"|@?\$?"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|[A-Za-z_][A-Za-z_0-9]*|[^\s]
        """", RegexOptions.IgnorePatternWhitespace | RegexOptions.CultureInvariant);

    private static List<Token> Lex(string source)
    {
        var result = new List<Token>();
        int line = 1, offset = 0;
        foreach (Match match in Tokens.Matches(source))
        {
            line += source.AsSpan(offset, match.Index - offset).Count('\n');
            if (!match.Value.StartsWith("//", StringComparison.Ordinal) &&
                !match.Value.StartsWith("/*", StringComparison.Ordinal))
                result.Add(new Token(match.Value, line));
            line += match.Value.AsSpan().Count('\n');
            offset = match.Index + match.Length;
        }
        return result;
    }

    private static string? Literal(string token)
    {
        if (token.StartsWith("@\"", StringComparison.Ordinal)) return token[2..^1].Replace("\"\"", "\"");
        if (token.StartsWith('"') && !token.StartsWith("\"\"\"", StringComparison.Ordinal))
            return Regex.Unescape(token[1..^1]);
        return null;
    }

    private static List<Lookup> Scan(string file, string source)
    {
        var tokens = Lex(source);
        var lookups = new List<Lookup>();
        for (int i = 0; i < tokens.Count; i++)
        {
            string method = tokens[i].Text;
            if (method is not ("Get" or "TryGet" or "Bind" or "SetLocalizedKey" or
                "SetLocalizedRaw" or "SetLocalized" or "Locale" or "ApplyRawText" or
                "setLocalizedKey" or "setLocalized")) continue;
            // InitialToggleBinding.Bind is a model-state API, not a template lookup.
            if (method == "Bind" && i >= 2 && tokens[i - 1].Text == "." &&
                tokens[i - 2].Text == "InitialToggleBinding") continue;
            int next = i + 1;
            bool generic = next < tokens.Count && tokens[next].Text == "<";
            if (generic)
            {
                int depth = 0;
                do
                {
                    if (tokens[next].Text == "<") depth++;
                    if (tokens[next].Text == ">") depth--;
                    next++;
                } while (next < tokens.Count && depth > 0);
            }
            // Generic Bind<T> is InitialToggleBinding's declaration, not the UI helper.
            if (method == "Bind" && generic) continue;
            if (method is "Get" or "TryGet" && !generic) continue;
            if (next >= tokens.Count || tokens[next].Text != "(") continue;
            int start = ++next, nesting = 0;
            while (next < tokens.Count)
            {
                string text = tokens[next].Text;
                if (nesting == 0 && text is "," or ")") break;
                if (text is "(" or "[" or "{") nesting++;
                if (text is ")" or "]" or "}") nesting--;
                next++;
            }
            // Method declarations have a string parameter; invocations do not.
            if (start == next || next - start == 2 && tokens[start].Text == "string" &&
                Regex.IsMatch(tokens[start + 1].Text, @"^[A-Za-z_][A-Za-z_0-9]*$", RegexOptions.CultureInvariant)) continue;
            // Accept named first arguments as well as positional ones.
            if (start + 1 < next && tokens[start + 1].Text == ":") start += 2;
            string argument = string.Concat(tokens.Skip(start).Take(next - start).Select(t => t.Text));
            string normalizedMethod = method switch
            {
                "setLocalizedKey" => "SetLocalizedKey",
                "setLocalized" => "SetLocalized",
                _ => method
            };
            lookups.Add(new Lookup(file, tokens[i].Line, normalizedMethod, argument,
                next - start == 1 ? Literal(tokens[start].Text) : null));
        }
        return lookups;
    }

    private static string FindRepositoryRoot()
    {
        // dotnet run puts the executable beneath Tests/bin on Windows and Ubuntu.
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Tests", "Importer.RegressionTests.csproj")) &&
                Directory.Exists(Path.Combine(directory.FullName, "UnityPackageImporter", "UI", "Templates")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Cannot find repository root above the regression test executable.");
    }

    private static HashSet<string> ReadBindings(string path)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        return json.RootElement.GetProperty("bindings").EnumerateObject()
            .Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
    }

    private static string? Missing(Lookup use, HashSet<string> bindings)
    {
        if (use.Literal == null) throw new InvalidOperationException("Expected a resolved binding name.");
        bool exists = use.Localized
            ? bindings.Any(key => Regex.IsMatch(key, "^locale\\." + Regex.Escape(use.Literal) + @"\.[0-9]+$", RegexOptions.CultureInvariant))
            : bindings.Contains(use.Literal);
        return exists ? null : $"{use.File}:{use.Line}: missing {(use.Localized ? "locale group" : "binding")} '{use.Literal}'";
    }

    private static void Audit(string root, TextWriter output)
    {
        var bindings = new[] { "station", "pre-import", "error-report" }.ToDictionary(name => name,
            name => ReadBindings(Path.Combine(root, "UnityPackageImporter", "UI", "Templates", name + ".bindings.json")),
            StringComparer.Ordinal);
        var used = bindings.ToDictionary(pair => pair.Key, _ => new HashSet<string>(StringComparer.Ordinal));
        var dynamicCounts = new int[DynamicAllowlist.Length];
        var errors = new List<string>();
        int lookupCount = 0;
        string sourceRoot = Path.Combine(root, "UnityPackageImporter");
        foreach (string path in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string file = Path.GetRelativePath(sourceRoot, path).Replace('\\', '/');
            if (file.Split('/').Any(segment => segment is "bin" or "obj")) continue;
            foreach (var use in Scan(file, File.ReadAllText(path)))
            {
                lookupCount++;
                if (!Contracts.TryGetValue(file, out string? contract))
                {
                    errors.Add($"UnityPackageImporter/{file}:{use.Line}: assign a contract for {use.Method}({use.Argument})");
                    continue;
                }
                if (use.Literal == null)
                {
                    int index = Array.FindIndex(DynamicAllowlist, entry =>
                        entry.File == file && entry.Method == use.Method && entry.Argument == use.Argument);
                    if (index < 0)
                    {
                        errors.Add($"UnityPackageImporter/{file}:{use.Line}: unlisted dynamic lookup {use.Method}({use.Argument}); add explicit names and a reason");
                        continue;
                    }
                    dynamicCounts[index]++;
                    var entry = DynamicAllowlist[index];
                    output.WriteLine($"DYNAMIC {file}:{use.Line}: {use.Method}({use.Argument}): {entry.Reason}" +
                        (entry.Names.Length == 0 ? "" : "; names: " + string.Join(", ", entry.Names)));
                    foreach (string name in entry.Names) Validate(use with { Literal = name });
                }
                else Validate(use);

                void Validate(Lookup resolved)
                {
                    IEnumerable<string> targets = contract == "both" ? bindings.Keys : [contract];
                    foreach (string name in targets)
                    {
                        string? missing = Missing(resolved, bindings[name]);
                        if (missing != null) errors.Add($"UnityPackageImporter/{missing} in {name}.bindings.json");
                        foreach (string key in bindings[name].Where(key => resolved.Localized
                            ? key.StartsWith("locale." + resolved.Literal + ".", StringComparison.Ordinal)
                            : key == resolved.Literal)) used[name].Add(key);
                    }
                }
            }
        }
        for (int i = 0; i < dynamicCounts.Length; i++)
            if (dynamicCounts[i] != DynamicAllowlist[i].Count)
                errors.Add($"Dynamic allowlist drift: {DynamicAllowlist[i].File} {DynamicAllowlist[i].Method}({DynamicAllowlist[i].Argument}): expected {DynamicAllowlist[i].Count} calls, found {dynamicCounts[i]}");
        foreach (var pair in bindings)
        {
            foreach (string key in pair.Value.Except(used[pair.Key]).Order(StringComparer.Ordinal))
            {
                string? reason = ReverseAllowlist.FirstOrDefault(entry => entry.Pattern.IsMatch(key)).Reason;
                output.WriteLine($"UNREFERENCED {pair.Key}.bindings.json: {key}" +
                    (reason == null ? " (report only)" : " (allowlisted: " + reason + ")"));
            }
            output.WriteLine($"CONTRACT {pair.Key}: {used[pair.Key].Count}/{pair.Value.Count} bindings referenced by code");
        }
        output.WriteLine($"Scanned {lookupCount} template lookup calls.");
        if (errors.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));
    }

    internal static void Register(Action<string, Action> test)
    {
        test("Template binding contract matches source lookups", () => Audit(FindRepositoryRoot(), Console.Out));
        test("Template binding scanner handles comments, nested generics, and localization callbacks", () =>
        {
            var uses = Scan(Station, """"
                // Get<Text>("comment");
                /* Bind("comment", null); */
                var example = "Get<Text>(\"string\")";
                private T Get<T>(string key) => ui.Get<T>(key);
                ui.TryGet<BooleanValueDriver<float3>>("tabletScale", out var driver);
                Get<Dictionary<string, List<Text>>>(@"preview");
                Bind("close", CloseAsync);
                setLocalizedKey("waiting", "messageKey");
                ui.SetLocalizedRaw(key: "phase", "text");
                """
                Get<Text>("raw string example");
                """;
                """");
            string actual = string.Join(",", uses.Select(use => use.Literal ?? use.Argument));
            Check(actual == "key,tabletScale,preview,close,waiting,phase", actual);
            Check(uses[1].Line == 5, "Lookup line number must include comments and strings.");
        });
        test("Template binding check rejects a renamed JSON key and accepts the restored copy", () =>
        {
            string repository = FindRepositoryRoot();
            string copy = Path.Combine(Path.GetTempPath(), "binding-contract-" + Guid.NewGuid().ToString("N"));
            try
            {
                // Copy the real consumers and contracts; exercise the same Audit as CI.
                foreach (string file in Contracts.Keys.Concat(new[]
                    { "UI/Templates/station.bindings.json", "UI/Templates/pre-import.bindings.json", "UI/Templates/error-report.bindings.json" }))
                {
                    string destination = Path.Combine(copy, "UnityPackageImporter", file);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(Path.Combine(repository, "UnityPackageImporter", file), destination);
                }
                string contract = Path.Combine(copy, "UnityPackageImporter", "UI", "Templates", "station.bindings.json");
                string original = File.ReadAllText(contract);
                File.WriteAllText(contract, original.Replace("\"progressFile\":", "\"progressFile_RENAMED\":"));
                string failure = AuditFailure(copy);
                Check(failure.Contains("missing binding 'progressFile' in station.bindings.json", StringComparison.Ordinal), failure);
                Console.WriteLine("Expected FAIL against renamed JSON copy:\n" + failure);
                File.WriteAllText(contract, original);
                Audit(copy, TextWriter.Null);
                string reportContract = Path.Combine(copy, "UnityPackageImporter", "UI", "Templates", "error-report.bindings.json");
                string reportOriginal = File.ReadAllText(reportContract);
                File.WriteAllText(reportContract, reportOriginal.Replace("\"copyReportAction\":", "\"copyReportAction_RENAMED\":"));
                Check(AuditFailure(copy).Contains("missing binding 'copyReportAction' in error-report.bindings.json", StringComparison.Ordinal),
                    "Error report controller did not use its own binding contract.");
                File.WriteAllText(reportContract, reportOriginal);
                Audit(copy, TextWriter.Null);
                Console.WriteLine("PASS against restored JSON copy: full source contract audit");
            }
            finally { Directory.Delete(copy, true); }
        });
        test("Template binding check uses the matching contract and locale namespace", () =>
        {
            var station = new HashSet<string>(["close", "locale.phase.0"], StringComparer.Ordinal);
            var prompt = new HashSet<string>(["cancel"], StringComparer.Ordinal);
            var direct = Scan(Station, "Bind(\"close\", action);").Single();
            Check(Missing(direct, station) == null && Missing(direct, prompt) != null, "Contract names were mixed.");
            var localized = Scan(Station, "ui.SetLocalizedKey(\"phase\", \"ready\");").Single();
            Check(Missing(localized, station) == null, "Locale group was not resolved.");
            Check(Missing(localized, new HashSet<string>(["phase"])) != null, "Raw key incorrectly satisfied locale group.");
        });
        test("Template binding audit rejects new dynamic expressions and unmapped consumers", () =>
        {
            // Use a tiny repository copy so the real audit (including dispatch) must fail.
            string root = Path.Combine(Path.GetTempPath(), "binding-contract-" + Guid.NewGuid().ToString("N"));
            try
            {
                string templates = Path.Combine(root, "UnityPackageImporter", "UI", "Templates");
                Directory.CreateDirectory(templates);
                foreach (string name in new[] { "station", "pre-import", "error-report" })
                    File.WriteAllText(Path.Combine(templates, name + ".bindings.json"), "{\"bindings\":{}}");
                File.WriteAllText(Path.Combine(root, "UnityPackageImporter", "NewConsumer.cs"), "ui.Get<Text>(\"typo\");");
                Directory.CreateDirectory(Path.Combine(root, "UnityPackageImporter", "Models"));
                File.WriteAllText(Path.Combine(root, "UnityPackageImporter", Station), "ui.Get<Text>(prefix + suffix);");
                string message = AuditFailure(root);
                Check(message.Contains("NewConsumer.cs:1: assign a contract", StringComparison.Ordinal), message);
                Check(message.Contains("unlisted dynamic lookup Get(prefix+suffix)", StringComparison.Ordinal), message);
                Check(Scan(Station, "ui.Get<Text>(string.Concat(prefix, suffix));").Single().Literal == null,
                    "A dynamic string expression was mistaken for a method declaration.");
            }
            finally { Directory.Delete(root, true); }
        });
        test("Template binding root discovery is independent of working directory", () =>
        {
            string expected = FindRepositoryRoot();
            string previous = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(Path.GetTempPath());
                Check(FindRepositoryRoot() == expected, "Root discovery depends on working directory.");
            }
            finally { Directory.SetCurrentDirectory(previous); }
        });
    }

    private static string AuditFailure(string root)
    {
        try { Audit(root, TextWriter.Null); }
        catch (InvalidDataException error) { return error.Message; }
        throw new Exception("Expected contract audit to fail.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
