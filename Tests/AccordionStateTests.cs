using UnityPackageImporter.Models;

internal static class AccordionStateTests
{
    private sealed class Renderer(string name)
    {
        public string Name = name;
    }

    public static void Register(Action<string, Action> test)
    {
        test("Accordion starts collapsed and toggles the same entry closed", () =>
        {
            var state = new AccordionState<int>();
            Check(!state.IsExpanded(0), "An entry started expanded.");
            Check(state.Toggle(0) && state.IsExpanded(0), "Toggle did not expand the entry.");
            Check(!state.Toggle(0) && !state.IsExpanded(0), "Toggle did not collapse the entry.");
        });

        test("Accordion expands only one entry at a time", () =>
        {
            var state = new AccordionState<int>();
            state.Toggle(1);
            Check(state.Toggle(2), "A different entry did not expand.");
            Check(!state.IsExpanded(1) && state.IsExpanded(2), "Two entries remained expanded.");
            state.Toggle(1);
            Check(state.IsExpanded(1) && !state.IsExpanded(2), "Switching back did not collapse the second entry.");
        });

        foreach (string secondName in new[] { "Body", "bOdY" })
        {
            test("Accordion distinguishes renderer identities named Body and " + secondName, () =>
            {
                var first = new Renderer("Body");
                var second = new Renderer(secondName);
                var state = new AccordionState<Renderer>(ReferenceEqualityComparer.Instance);
                state.Toggle(first);
                Check(state.IsExpanded(first) && !state.IsExpanded(second), "Display names aliased distinct renderers.");
                Check(state.Toggle(second), "The second renderer collapsed instead of expanding.");
                Check(!state.IsExpanded(first) && state.IsExpanded(second), "Single expansion failed for duplicate names.");
                Check(!state.Toggle(second), "The second renderer did not collapse.");
                Check(!state.IsExpanded(first) && !state.IsExpanded(second), "Collapsing left an entry expanded.");
                Check(state.Toggle(first) && state.IsExpanded(first), "The first renderer could not reopen.");
            });
        }

        test("Accordion identity survives a renderer display name change", () =>
        {
            var renderer = new Renderer("Body");
            var state = new AccordionState<Renderer>(ReferenceEqualityComparer.Instance);
            state.Toggle(renderer);
            renderer.Name = "Renamed";
            Check(state.IsExpanded(renderer), "Renaming lost expansion state.");
            Check(!state.Toggle(renderer), "Renaming prevented collapse.");
        });

        test("Accordion clear resets expansion when rebuilding with the same renderers", () =>
        {
            var first = new Renderer("Body");
            var second = new Renderer("Body");
            var state = new AccordionState<Renderer>(ReferenceEqualityComparer.Instance);
            state.Toggle(first);
            state.Toggle(second);
            state.Clear();
            Check(!state.IsExpanded(first) && !state.IsExpanded(second), "Rebuilt entries retained expansion state.");
            state.Clear();
            Check(state.Toggle(second) && state.IsExpanded(second), "A rebuilt entry could not expand after clearing.");
        });
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
