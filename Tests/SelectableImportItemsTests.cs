using UnityPackageImporter.Models;

internal static class SelectableImportItemsTests
{
    internal static void Register(Action<string, Action> test)
    {
        test("Selectable import items wrap navigation and hide it for zero or one item", () =>
        {
            var items = new SelectableImportItems<object>();
            var first = new object();
            var second = new object();
            items.Reset(new[] { first, second });

            Check(items.CanNavigate, "Two selectable items must allow navigation.");
            Check(ReferenceEquals(items.Select(-1), second), "Previous from the first item must wrap to the last.");
            Check(ReferenceEquals(items.Select(2), first), "Next from the last item must wrap to the first.");

            Check(items.Consume(second), "A selectable source must be removable after its consuming operation succeeds.");
            Check(items.Count == 1 && !items.CanNavigate, "One remaining item must hide navigation.");
            Check(ReferenceEquals(items.Current, first), "Removing another item must preserve the current selection.");
            Check(items.Consume(first), "The final consumed source must be removable.");
            Check(items.Count == 0 && items.SelectedIndex == -1 && items.Current == null && !items.CanNavigate,
                "An empty selection list must have no current item or navigation.");
        });

        test("Selectable import items choose the next remaining entry when the current source is consumed", () =>
        {
            var items = new SelectableImportItems<object>();
            var first = new object();
            var second = new object();
            var third = new object();
            items.Reset(new[] { first, second, third });
            items.Select(1);

            Check(items.Consume(second), "The current source must be removable after successful consumption.");
            Check(items.Count == 2 && items.SelectedIndex == 1 && ReferenceEquals(items.Current, third),
                "After removal, the next item at the same position should become current.");
            Check(!items.Consume(new object()), "An unrelated item must not change the selectable list.");
            Check(items.Count == 2 && ReferenceEquals(items.Current, third), "Failed removal must preserve selection.");
        });

        test("Reusable sources remain selectable until an operation explicitly consumes them", () =>
        {
            var items = new SelectableImportItems<object>();
            var source = new object();
            items.Reset(new[] { source });

            // A reusable operation, such as outfit installation, leaves the list untouched.
            Check(ReferenceEquals(items.Current, source) && items.Count == 1,
                "A reusable source must remain available after a non-consuming operation.");
            Check(items.Consume(source), "A later consuming operation must remove the same source.");
            Check(items.Count == 0, "The source must leave the list only after consumption.");
        });
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
