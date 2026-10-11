using UnityPackageImporter.Models;

internal static class InitialToggleBindingTests
{
    private sealed class Target(bool active, bool driven = false)
    {
        public bool Active = active;
        public bool Driven = driven;
    }

    public static void Register(Action<string, Action> test)
    {
        test("Generated grouped toggle preserves current visible and hidden clothing", () =>
        {
            foreach (bool active in new[] { true, false })
            {
                var targets = new[] { new Target(active), new Target(active) };
                bool driver = false;
                var result = InitialToggleBinding.Bind(targets, target => target.Active, target => target.Driven,
                    initial => driver = initial, target => { target.Driven = true; target.Active = driver; });
                Check(result == InitialToggleBindingResult.Bound, "Uniform clothing state was not supported.");
                Check(targets.All(target => target.Active == active), "Acquiring the toggle overwrote current appearance.");
            }
        });
        test("Inferred mixed clothing state remains untouched without guessing Animator behavior", () =>
        {
            var targets = new[] { new Target(true), new Target(false) };
            var result = InitialToggleBinding.Bind(targets, target => target.Active, target => target.Driven,
                _ => throw new Exception("Mixed control was initialized."),
                _ => throw new Exception("Mixed target was driven."));
            Check(result == InitialToggleBindingResult.MixedStates, "Mixed authored states were not identified.");
            Check(targets[0].Active && !targets[1].Active, "Mixed authored appearance changed.");
        });
        test("Generated toggle preserves existing drives and latest user state", () =>
        {
            var targets = new[] { new Target(false), new Target(false, true) };
            var blocked = InitialToggleBinding.Bind(targets, target => target.Active, target => target.Driven,
                _ => throw new Exception("Control initialized despite another drive."),
                _ => throw new Exception("Another drive was replaced."));
            Check(blocked == InitialToggleBindingResult.AlreadyDriven, "Existing drive was not respected.");
            foreach (var target in targets) { target.Driven = false; target.Active = true; }
            bool state = false;
            InitialToggleBinding.Bind(targets, target => target.Active, target => target.Driven,
                value => state = value, target => target.Active = state);
            Check(state && targets.All(target => target.Active), "Current user choice was replaced by old defaults.");
        });
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
