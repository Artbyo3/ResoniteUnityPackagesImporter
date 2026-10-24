using System.Globalization;
using UnityPackageImporter.Models;

internal static class AvatarWardrobeStateTests
{
    private sealed record UnsupportedCondition : AvatarWardrobeCondition;
    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        const string avatar = "avatar-a";
        AvatarParameterId Id(string name, string scope = "") => new(avatar, scope, name);
        AvatarParameterDeclaration Declare(string name, AvatarParameterType type = AvatarParameterType.Bool, double? value = 0,
            bool? saved = false, bool? synced = true, string scope = "", string source = "fixture") => new(Id(name, scope), type, value, saved, synced, source);
        AvatarParameterCatalog Catalog(params AvatarParameterDeclaration[] declarations) => new(avatar, declarations);
        InitialAppearanceWrite Active(long target, bool value) => new(InitialAppearanceEffect.Active, target, "m_IsActive", Active: value);
        InitialAppearanceWrite Shape(float value) => new(InitialAppearanceEffect.Shape, 30, "BodyFit", Weight: value);
        AvatarParameterCondition On(string name) => new(Id(name), AvatarParameterComparison.Equal, 1);
        AvatarWardrobeRule Rule(string name, InitialAppearanceWrite write, params AvatarWardrobeCondition[] conditions) => new(name, write, conditions);
        void Reject(Action action)
        {
            try { action(); }
            catch (InvalidDataException) { return; }
            throw new Exception("Expected an explicit invalid-data rejection.");
        }
        void Cancel(Action action)
        {
            try { action(); }
            catch (OperationCanceledException) { return; }
            throw new Exception("Expected cancellation to propagate.");
        }
        test("Shared wardrobe preserves explicit zero and unspecified defaults", () =>
        {
            var catalog = Catalog(Declare("Outfit", value: null, saved: null, synced: null, source: "MA"), Declare("Outfit", value: 0, source: "SDK"));
            var definition = catalog.Definitions[Id("Outfit")];
            check(definition.DefaultValue == 0 && !definition.Saved && definition.Synced && definition.Sources.SequenceEqual(new[] { "MA", "SDK" }), "Declaration provenance/default policy changed.");
        });
        test("Shared wardrobe resolves scoped alias chains without linking outfit instances", () =>
        {
            var catalog = new AvatarParameterCatalog(avatar, new[] { Declare("Choice", scope: "outfit-a"), Declare("Choice", scope: "outfit-b"), Declare("Shared") },
                new[] { new AvatarParameterAlias(Id("Local", "child"), Id("Renamed", "parent")), new AvatarParameterAlias(Id("Renamed", "parent"), Id("Shared")) });
            var state = catalog.CreateState().With((Id("Local", "child"), 1), (Id("Choice", "outfit-a"), 1));
            check(state[Id("Shared")] == 1 && state[Id("Renamed", "parent")] == 1 && state[Id("Choice", "outfit-b")] == 0, "Aliases leaked across instances.");
        });
        test("Shared wardrobe parameter identities are case-sensitive and avatar-owned", () =>
        {
            var catalog = Catalog(Declare("Outfit"), Declare("outfit"));
            var state = catalog.CreateState().With((Id("Outfit"), 1));
            check(state[Id("outfit")] == 0, "Parameter names were folded together.");
            Reject(() => state.With((new AvatarParameterId("avatar-b", "", "Outfit"), 1)));
        });
        foreach (string conflict in new[] { "type", "default", "saved", "synced" })
            test("Shared wardrobe rejects declaration conflict " + conflict, () =>
            {
                var first = Declare("Shared");
                var second = conflict switch
                {
                    "type" => first with { Type = AvatarParameterType.Int },
                    "default" => first with { DefaultValue = 1 },
                    "saved" => first with { Saved = true },
                    _ => first with { Synced = false }
                };
                Reject(() => Catalog(first, second));
            });
        test("Shared wardrobe rejects alias cycles and missing destinations", () =>
        {
            Reject(() => new AvatarParameterCatalog(avatar, Array.Empty<AvatarParameterDeclaration>(), new[] { new AvatarParameterAlias(Id("A"), Id("B")), new AvatarParameterAlias(Id("B"), Id("A")) }));
            Reject(() => new AvatarParameterCatalog(avatar, Array.Empty<AvatarParameterDeclaration>(), new[] { new AvatarParameterAlias(Id("A"), Id("Missing")) }));
            Reject(() => new AvatarParameterCatalog(avatar, new[] { Declare("A") }, new[] { new AvatarParameterAlias(Id("A"), Id("A")) }));
        });
        foreach (var sample in new[] { (AvatarParameterType.Bool, 2d), (AvatarParameterType.Int, 1.5), (AvatarParameterType.Int, (double)int.MaxValue + 1),
            (AvatarParameterType.Float, double.NaN), (AvatarParameterType.Float, double.PositiveInfinity), (AvatarParameterType.Float, double.MaxValue) })
            test("Shared wardrobe rejects invalid typed value " + sample, () =>
            {
                var state = Catalog(Declare("Value", sample.Item1)).CreateState();
                Reject(() => state.With((Id("Value"), sample.Item2)));
                check(state[Id("Value")] == 0, "Rejected update changed the original state.");
            });
        test("Shared wardrobe preserves full animator int range without expression-budget clamping", () =>
        {
            var state = Catalog(Declare("Value", AvatarParameterType.Int)).CreateState().With((Id("Value"), int.MaxValue));
            check(state[Id("Value")] == int.MaxValue, "Animator int was rounded or clamped.");
        });
        test("Shared wardrobe atomically rejects conflicting alias writes", () =>
        {
            var catalog = new AvatarParameterCatalog(avatar, new[] { Declare("Shared"), Declare("Other") }, new[] { new AvatarParameterAlias(Id("Alias"), Id("Shared")) });
            var state = catalog.CreateState();
            Reject(() => state.With((Id("Other"), 1), (Id("Shared"), 1), (Id("Alias"), 0)));
            check(state.Values.Values.All(value => value == 0), "Atomic update leaked partial mutations.");
        });
        test("Shared wardrobe unsaved reset preserves saved state without claiming persistence", () =>
        {
            var catalog = Catalog(Declare("Saved", saved: true, synced: false), Declare("Temporary"));
            var original = catalog.CreateState();
            var changed = original.With((Id("Saved"), 1), (Id("Temporary"), 1));
            var reset = changed.ResetUnsaved();
            check(reset[Id("Saved")] == 1 && reset[Id("Temporary")] == 0 && original[Id("Saved")] == 0, "Reset policy or immutability changed.");
            check(!catalog.Definitions[Id("Saved")].Synced, "Saved and sync flags were conflated.");
        });
        test("Shared wardrobe unknown identities and invalid declarations fail closed", () =>
        {
            Reject(() => Catalog(Declare("Known")).CreateState().With((Id("Unknown"), 1)));
            Reject(() => Catalog(Declare("Bad", value: double.NaN)));
            Reject(() => Catalog(Declare("Bad", (AvatarParameterType)99)));
            Reject(() => Catalog(Declare(" ")));
            Reject(() => Catalog(Declare("Bad", source: "")));
            Reject(() => new AvatarParameterCatalog(avatar, new[] { Declare("Known") with { Id = new("other", "", "Known") } }));
        });
        string ParameterYaml(string records) => "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\nMonoBehaviour:\n  parameters:\n" + records;
        const string validRecord = "  - name: Outfit\n    valueType: 0\n    defaultValue: 2\n    saved: 1\n    networkSynced: 0\n";
        test("Expression parameter AST preserves quoted names flow records and sync policy", () =>
        {
            var parsed = ExpressionMenuParser.ParseParameterYaml(ParameterYaml("  - {name: 'Clothing: Coat #1', valueType: 0, defaultValue: 2, saved: 1, networkSynced: 0}\n  - {name: Outfit, valueType: 2}\n  - {name: outfit, valueType: 2, defaultValue: 0}\n"));
            var catalog = AvatarParameterCatalog.FromExpressions(avatar, parsed, "SDK asset GUID/fileID");
            check(parsed.Count == 3 && parsed["Clothing: Coat #1"].Saved && !parsed["Clothing: Coat #1"].Synced, "YAML structure/flags were not retained.");
            check(!parsed["Outfit"].HasDefaultValue && parsed["outfit"].HasDefaultValue && catalog.CreateState()[Id("Clothing: Coat #1")] == 2, "Default presence or typed catalog changed.");
        });
        foreach (string mutation in new[] { "duplicate", "missing-type", "unknown-type", "bad-name", "nonfinite", "bad-bool", "bad-saved", "bad-sync", "int-fraction", "int-overflow", "float-range", "wrong-record", "two-documents", "malformed" })
            test("Expression parameter AST rejects " + mutation, () =>
            {
                string yaml = ParameterYaml(validRecord);
                yaml = mutation switch
                {
                    "duplicate" => yaml + validRecord,
                    "missing-type" => yaml.Replace("    valueType: 0\n", "", StringComparison.Ordinal),
                    "unknown-type" => yaml.Replace("valueType: 0", "valueType: 3", StringComparison.Ordinal),
                    "bad-name" => yaml.Replace("name: Outfit", "name: []", StringComparison.Ordinal),
                    "nonfinite" => yaml.Replace("defaultValue: 2", "defaultValue: NaN", StringComparison.Ordinal),
                    "bad-bool" => yaml.Replace("valueType: 0", "valueType: 2", StringComparison.Ordinal),
                    "bad-saved" => yaml.Replace("saved: 1", "saved: yes", StringComparison.Ordinal),
                    "bad-sync" => yaml.Replace("networkSynced: 0", "networkSynced: 9", StringComparison.Ordinal),
                    "int-fraction" => yaml.Replace("defaultValue: 2", "defaultValue: 1.5", StringComparison.Ordinal),
                    "int-overflow" => yaml.Replace("defaultValue: 2", "defaultValue: 256", StringComparison.Ordinal),
                    "float-range" => yaml.Replace("valueType: 0", "valueType: 1", StringComparison.Ordinal),
                    "wrong-record" => ParameterYaml("  - []\n"),
                    "two-documents" => yaml + "---\nMonoBehaviour: {parameters: []}\n",
                    _ => "MonoBehaviour: [broken"
                };
                Reject(() => ExpressionMenuParser.ParseParameterYaml(yaml));
            });
        test("Expression parameter AST ignores nested unrelated parameter-like fields", () =>
        {
            var parsed = ExpressionMenuParser.ParseParameterYaml(ParameterYaml(validRecord) + "  unrelated:\n    parameters:\n    - {name: Fake, valueType: 2}\n");
            check(parsed.Count == 1 && !parsed.ContainsKey("Fake"), "Parser escaped its parameter sequence.");
        });
        test("Shared wardrobe ordered active rules restore the current winner not stale baseline", () =>
        {
            var catalog = Catalog(Declare("A"), Declare("B"));
            var graph = new AvatarWardrobeGraph(catalog, new[] { Shape(0) }, new[] { Rule("A", Shape(30), On("A")), Rule("B", Shape(70), On("B")) });
            var first = catalog.CreateState().With((Id("A"), 1));
            var both = first.With((Id("B"), 1));
            var remaining = both.With((Id("B"), 0));
            check(graph.Evaluate(first).Values.Single().Weight == 30 && graph.Evaluate(both).Values.Single().Weight == 70 &&
                graph.Evaluate(remaining).Values.Single().Weight == 30 && graph.Evaluate(catalog.CreateState()).Values.Single().Weight == 0, "Priority restoration used click order/stale snapshots.");
        });
        test("Shared wardrobe integer radio controls share one state", () =>
        {
            var catalog = Catalog(Declare("Outfit", AvatarParameterType.Int));
            var graph = new AvatarWardrobeGraph(catalog, new[] { Active(10, false), Active(20, false) }, new[]
            {
                Rule("Shirt", Active(10, true), new AvatarParameterCondition(Id("Outfit"), AvatarParameterComparison.Equal, 1)),
                Rule("Coat", Active(20, true), new AvatarParameterCondition(Id("Outfit"), AvatarParameterComparison.Equal, 2))
            });
            foreach (int selection in new[] { 0, 1, 2 })
            {
                var result = graph.Evaluate(catalog.CreateState().With((Id("Outfit"), selection)));
                check(result[AvatarPropertyKey.Of(Active(10, false))].Active == (selection == 1) && result[AvatarPropertyKey.Of(Active(20, false))].Active == (selection == 2), "Radio selection was split into unrelated booleans.");
            }
        });
        foreach (double value in new[] { 0.995, 0.996, 1, 1.004, 1.005 })
            test("Shared wardrobe MA-style threshold is open at " + value.ToString(CultureInfo.InvariantCulture), () =>
            {
                var catalog = Catalog(Declare("Value", AvatarParameterType.Float));
                var condition = new AvatarParameterCondition(Id("Value"), AvatarParameterComparison.OpenInterval, 0.995, 1.005);
                var graph = new AvatarWardrobeGraph(catalog, new[] { Shape(0) }, new[] { Rule("Range", Shape(100), condition) });
                check((graph.Evaluate(catalog.CreateState().With((Id("Value"), value))).Values.Single().Weight == 100) == (value > 0.995 && value < 1.005), "Threshold became inclusive or exact equality.");
            });
        foreach (bool inverted in new[] { false, true })
            foreach (int enabled in new[] { 0, 1, 2, 3 })
                test("Shared wardrobe inverts the whole conjunction " + inverted + ":" + enabled, () =>
                {
                    var catalog = Catalog(Declare("A"), Declare("B"));
                    var graph = new AvatarWardrobeGraph(catalog, new[] { Shape(0) }, new[] { Rule("Combined", Shape(100), On("A"), On("B")) with { Inverted = inverted } });
                    var result = graph.Evaluate(catalog.CreateState().With((Id("A"), enabled & 1), (Id("B"), (enabled >> 1) & 1)));
                    check((result.Values.Single().Weight == 100) == ((enabled == 3) != inverted), "Inversion was applied per condition.");
                });
        test("Shared wardrobe ancestor dependencies evaluate before child writes", () =>
        {
            var catalog = Catalog(Declare("Parent"), Declare("Detail", value: 1));
            var parent = AvatarPropertyKey.Of(Active(10, false));
            var graph = new AvatarWardrobeGraph(catalog, new[] { Shape(0), Active(10, false) }, new[]
            {
                Rule("Child", Shape(100), On("Detail"), new AvatarActiveCondition(parent)), Rule("Parent", Active(10, true), On("Parent"))
            });
            check(graph.Evaluate(catalog.CreateState()).Values.Single(write => write.Effect == InitialAppearanceEffect.Shape).Weight == 0 &&
                graph.Evaluate(catalog.CreateState().With((Id("Parent"), 1))).Values.Single(write => write.Effect == InitialAppearanceEffect.Shape).Weight == 100, "Parent gating used stale/output order-dependent state.");
        });
        foreach (bool self in new[] { false, true })
            test("Shared wardrobe rejects reactive dependency cycle self=" + self, () =>
            {
                var catalog = Catalog();
                var first = Active(10, false);
                var second = Active(20, false);
                Reject(() => new AvatarWardrobeGraph(catalog, new[] { first, second }, new[]
                {
                    Rule("A", first, new AvatarActiveCondition(AvatarPropertyKey.Of(self ? first : second))),
                    Rule("B", second, new AvatarActiveCondition(AvatarPropertyKey.Of(first)))
                }));
            });
        test("Shared wardrobe snapshots baseline rule and condition collections", () =>
        {
            var catalog = Catalog(Declare("A", value: 1));
            var baselines = new List<InitialAppearanceWrite> { Shape(0) };
            var conditions = new List<AvatarWardrobeCondition> { On("A") };
            var rules = new List<AvatarWardrobeRule> { new("A", Shape(100), conditions) };
            var graph = new AvatarWardrobeGraph(catalog, baselines, rules);
            baselines.Clear(); rules.Clear(); conditions.Add(new AvatarParameterCondition(Id("A"), AvatarParameterComparison.Equal, 0));
            check(graph.Evaluate(catalog.CreateState()).Values.Single().Weight == 100, "Input collections mutated compiled semantics.");
        });
        foreach (string invalid in new[] { "baseline", "duplicate-baseline", "duplicate-rule", "unknown-parameter", "unknown-active", "unknown-condition", "bad-interval", "nan-weight", "zero-target", "comparison" })
            test("Shared wardrobe rejects invalid property graph " + invalid, () =>
            {
                var catalog = Catalog(Declare("A"));
                var baselines = new List<InitialAppearanceWrite> { Shape(0) };
                var rules = new List<AvatarWardrobeRule> { Rule("A", Shape(100), On("A")) };
                switch (invalid)
                {
                    case "baseline": baselines.Clear(); break;
                    case "duplicate-baseline": baselines.Add(Shape(0)); break;
                    case "duplicate-rule": rules.Add(rules[0]); break;
                    case "unknown-parameter": rules[0] = Rule("A", Shape(100), On("Unknown")); break;
                    case "unknown-active": rules[0] = Rule("A", Shape(100), new AvatarActiveCondition(AvatarPropertyKey.Of(Active(99, true)))); break;
                    case "unknown-condition": rules[0] = Rule("A", Shape(100), new UnsupportedCondition()); break;
                    case "bad-interval": rules[0] = Rule("A", Shape(100), new AvatarParameterCondition(Id("A"), AvatarParameterComparison.OpenInterval, 1, 0)); break;
                    case "nan-weight": rules[0] = Rule("A", Shape(float.NaN)); break;
                    case "zero-target": baselines[0] = Shape(0) with { TargetId = 0 }; break;
                    case "comparison": rules[0] = Rule("A", Shape(100), new AvatarParameterCondition(Id("A"), (AvatarParameterComparison)99, 0)); break;
                }
                Reject(() => new AvatarWardrobeGraph(catalog, baselines, rules));
            });
        test("Shared wardrobe rejects a foreign state even if names happen to match", () =>
        {
            var catalog = Catalog(Declare("A"));
            var graph = new AvatarWardrobeGraph(catalog, new[] { Shape(0) }, Array.Empty<AvatarWardrobeRule>());
            Reject(() => graph.Evaluate(Catalog(Declare("A")).CreateState()));
        });
        test("Shared wardrobe independent native projection preserves all thirty controls", () =>
        {
            var controls = Enumerable.Range(1, 30).Select(index => new AvatarWardrobeControl("source:" + index, "Wardrobe/Outfit " + index, index % 2 == 0,
                new[] { new AvatarWardrobeBinding(Active(index, false), Active(index, true)) })).ToArray();
            var graph = AvatarWardrobeGraph.FromIndependentControls(avatar, controls);
            var state = graph.Parameters.CreateState();
            foreach (var control in controls)
            {
                var id = graph.IndependentControls![control.Id];
                check(state[id] == (control.DefaultOn ? 1 : 0), "Authored control default changed.");
                foreach (int value in new[] { 0, 1 })
                {
                    var result = graph.Evaluate(state.With((id, value)));
                    check(result[AvatarPropertyKey.Of(control.Bindings[0].On)].Active == (value == 1), "Independent native projection changed toggle behavior.");
                }
            }
            check(graph.Baselines.Count == 30 && graph.Parameters.Definitions.Count == 30, "Control/property identities were collapsed.");
        });
        test("Shared wardrobe generic graphs cannot bypass the native projection gate", () =>
        {
            var graph = new AvatarWardrobeGraph(Catalog(), new[] { Shape(0) }, Array.Empty<AvatarWardrobeRule>());
            var control = new AvatarWardrobeControl("A", "A", false, new[] { new AvatarWardrobeBinding(Shape(0), Shape(100)) });
            var plan = new AvatarWardrobePlan { SharedState = graph, Controls = new[] { control } };
            check(!plan.CanBuild, "Unevaluated generic rules were sent to independent native drivers.");
        });
        test("Shared wardrobe independent projection rejects conflicting field ownership", () =>
        {
            var first = new AvatarWardrobeControl("A", "A", false, new[] { new AvatarWardrobeBinding(Shape(0), Shape(100)) });
            Reject(() => AvatarWardrobeGraph.FromIndependentControls(avatar, new[] { first, first with { Id = "B", Path = "B" } }));
            Reject(() => AvatarWardrobeGraph.FromIndependentControls(avatar, new[] { first with { Bindings = new[] { new AvatarWardrobeBinding(Shape(0), Active(10, true)) } } }));
        });
        test("Shared parameters reconcile single precision float declarations", () =>
        {
            var catalog = Catalog(Declare("Amount", AvatarParameterType.Float, 0.1, source: "MA"),
                Declare("Amount", AvatarParameterType.Float, 0.1f, source: "SDK"));
            check(catalog.CreateState()[Id("Amount")] == (double)0.1f, "Equivalent serialized floats conflicted.");
            var changed = catalog.CreateState().With((Id("Amount"), 0.1), (Id("Amount"), 0.1f));
            check(changed[Id("Amount")] == (double)0.1f, "Typed updates did not preserve native single-precision values.");
            var graph = new AvatarWardrobeGraph(catalog, new[] { Shape(0) }, new[]
            {
                Rule("Float equality", Shape(100), new AvatarParameterCondition(Id("Amount"), AvatarParameterComparison.Equal, 0.1))
            });
            check(graph.Evaluate(changed).Values.Single().Weight == 100, "Float conditions used different precision from parameter values.");
            Reject(() => Catalog(Declare("Amount", AvatarParameterType.Float, 0.1), Declare("Amount", AvatarParameterType.Float, 0.2)));
            Reject(() => Catalog(Declare("Amount", AvatarParameterType.Float, double.MaxValue)));
        });
        test("Shared parameters treat signed zeros as equal", () =>
        {
            var catalog = Catalog(Declare("Amount", AvatarParameterType.Float, -0.0), Declare("Amount", AvatarParameterType.Float, 0.0));
            var state = catalog.CreateState().With((Id("Amount"), -0.0), (Id("Amount"), 0.0));
            check(state[Id("Amount")] == 0, "Signed zero caused a false write conflict.");
        });
        test("Shared projection rejects incomplete controls and bindings", () =>
        {
            var valid = new AvatarWardrobeControl("A", "A", false, new[] { new AvatarWardrobeBinding(Shape(0), Shape(100)) });
            foreach (var control in new[] { null!, valid with { Bindings = null! }, valid with { Bindings = Array.Empty<AvatarWardrobeBinding>() },
                valid with { Bindings = new AvatarWardrobeBinding[] { null! } }, valid with { Bindings = new[] { new AvatarWardrobeBinding(null!, Shape(1)) } } })
                Reject(() => AvatarWardrobeGraph.FromIndependentControls(avatar, new[] { control }));
        });
        test("Shared wardrobe compilation evaluation and parameter parsing propagate cancellation", () =>
        {
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            var catalog = Catalog(Declare("A"));
            var graph = new AvatarWardrobeGraph(catalog, new[] { Shape(0) }, Array.Empty<AvatarWardrobeRule>());
            Cancel(() => new AvatarParameterCatalog(avatar, Array.Empty<AvatarParameterDeclaration>(), token: cancelled.Token));
            Cancel(() => new AvatarWardrobeGraph(catalog, Array.Empty<InitialAppearanceWrite>(), Array.Empty<AvatarWardrobeRule>(), cancelled.Token));
            Cancel(() => graph.Evaluate(catalog.CreateState(), cancelled.Token));
            Cancel(() => AvatarWardrobeGraph.FromIndependentControls(avatar, Array.Empty<AvatarWardrobeControl>(), cancelled.Token));
            Cancel(() => ExpressionMenuParser.ParseParameterYaml(ParameterYaml(validRecord), cancelled.Token));
        });
    }
}
