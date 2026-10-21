using System.Reflection;
using UnityPackageImporter;

internal static class EngineCompatibilityTests
{
    public static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        test("Engine compatibility: matching version logs one info line", () =>
        {
            var log = new Log();
            log.Run(EngineCompatibilityCheck.LastVerifiedResoniteVersion);
            check(log.Info.Count == 1 && log.Warnings.Count == 0 && log.Errors.Count == 0, "Wrong log severity/count.");
            check(log.Info[0].Contains("matches last verified version " + EngineCompatibilityCheck.LastVerifiedResoniteVersion), "Missing verified version match.");
        });

        test("Engine compatibility: different versions log one actionable warning", () =>
        {
            var log = new Log();
            log.Run("2099.1.1.1");
            check(log.Info.Count == 0 && log.Warnings.Count == 1 && log.Errors.Count == 0, "Wrong log severity/count.");
            check(log.Warnings[0].Contains("2099.1.1.1") && log.Warnings[0].Contains(EngineCompatibilityCheck.LastVerifiedResoniteVersion), "Both versions must be named.");
            check(log.Warnings[0].Contains("Import hooks may break after engine updates."), "Missing warning about import hooks.");
        });

        test("Engine compatibility: all missing members are reported even on a verified version", () =>
        {
            // Reflection fixtures stand in for engine types without loading game code.
            var members = new (string Name, MemberInfo Member)[]
            {
                ("Fixture.RenamedType", typeof(Fixture).GetNestedType("RenamedType")!),
                ("Fixture.Present", typeof(Fixture).GetMethod(nameof(Fixture.Present))!),
                ("Fixture.renamedField", typeof(Fixture).GetField("renamedField")!),
                ("Fixture.RenamedEvent", typeof(Fixture).GetEvent("RenamedEvent")!),
                ("Fixture.RenamedMethod", typeof(Fixture).GetMethod("RenamedMethod")!)
            };
            var log = new Log();
            log.Run(EngineCompatibilityCheck.LastVerifiedResoniteVersion, members);
            var missing = members.Where(m => m.Member == null).Select(m => m.Name).ToArray();
            check(log.Errors.Count == missing.Length, "The scan stopped at the first missing member or reported a present member.");
            for (int i = 0; i < missing.Length; i++)
                check(log.Errors[i] == $"Required engine member {missing[i]} is unavailable. The feature will not work on this Resonite version.", "Missing member name or feature failure sentence.");
            check(log.Info.Count == 1 && log.Warnings.Count == 0, "Member failures must not duplicate the version line.");
        });

        test("Engine compatibility: present reflected members are only inspected", () =>
        {
            var log = new Log();
            log.Run("2099.1.1.1", new (string Name, MemberInfo Member)[]
            {
                ("Fixture", typeof(Fixture)),
                ("Fixture..ctor", typeof(Fixture).GetConstructor(Type.EmptyTypes)!),
                ("Fixture.Present", typeof(Fixture).GetMethod(nameof(Fixture.Present))!)
            });
            check(log.Errors.Count == 0 && log.Warnings.Count == 1, "Present members must not be reported as missing.");
        });

        test("Engine compatibility: diagnostic callback exceptions propagate", () =>
        {
            var expected = new InvalidOperationException("diagnostic sink failed");
            Exception? actual = null;
            try
            {
                EngineCompatibilityCheck.Run(EngineCompatibilityCheck.LastVerifiedResoniteVersion,
                    new (string Name, MemberInfo Member)[] { ("Fixture.Missing", null!) },
                    _ => { }, _ => { }, _ => throw expected);
            }
            catch (InvalidOperationException error) { actual = error; }
            check(ReferenceEquals(actual, expected), "Diagnostic exceptions must not be swallowed.");
        });
    }

    public sealed class Fixture
    {
        public Fixture() => throw new InvalidOperationException("Startup diagnostics must not construct engine objects.");
        public static void Present() => throw new InvalidOperationException("Startup diagnostics must not invoke engine methods.");
    }

    private sealed class Log
    {
        public readonly List<string> Info = new();
        public readonly List<string> Warnings = new();
        public readonly List<string> Errors = new();

        public void Run(string version, IEnumerable<(string Name, MemberInfo Member)>? members = null) =>
            EngineCompatibilityCheck.Run(version, members ?? Array.Empty<(string, MemberInfo)>(), Info.Add, Warnings.Add, Errors.Add);
    }
}
