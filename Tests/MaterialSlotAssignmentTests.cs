using UnityPackageImporter.Materials;

internal static class MaterialSlotAssignmentTests
{
    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        test("Material conversion failure replaces its own slot without shifting later materials", () =>
        {
            var actual = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                MaterialSlotAssignment.AssignAsync(index,
                    () => index == 1 ? Task.FromException<string>(new IOException("conversion failed")) : Task.FromResult("material-" + index),
                    _ => Task.FromResult("fallback"),
                    (target, material) => { check(target == actual.Count, "Slot index shifted."); actual.Add(material); return Task.CompletedTask; }).GetAwaiter().GetResult();
            }
            check(actual.SequenceEqual(new[] { "material-0", "fallback", "material-2" }), "Material identity/order changed.");
        });
        test("First-slot failure creates exactly one fallback slot", () =>
        {
            int calls = 0;
            MaterialSlotAssignment.AssignAsync<string>(0, () => throw new IOException("decode failed"),
                _ => Task.FromResult("fallback"), (index, material) =>
                { calls++; check(index == 0 && material == "fallback", "Fallback went to a new slot."); return Task.CompletedTask; }).GetAwaiter().GetResult();
            check(calls == 1, "More than one assignment occurred.");
        });
        test("Material slots are not allocated while conversion is still pending", () =>
        {
            var source = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            int assignments = 0;
            var pending = MaterialSlotAssignment.AssignAsync(3, () => source.Task, _ => Task.FromResult("fallback"),
                (index, material) => { assignments++; check(index == 3 && material == "converted", "Pending material changed."); return Task.CompletedTask; });
            check(assignments == 0, "An empty slot was created before resolution.");
            source.SetResult("converted"); pending.GetAwaiter().GetResult();
            check(assignments == 1, "Resolved material was not assigned once.");
        });
        test("Material conversion cancellation propagates without fallback or slot changes", () =>
        {
            int changes = 0; bool cancelled = false;
            try
            {
                MaterialSlotAssignment.AssignAsync<string>(0, () => throw new OperationCanceledException(),
                    _ => { changes++; return Task.FromResult("fallback"); },
                    (_, _) => { changes++; return Task.CompletedTask; }).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { cancelled = true; }
            check(cancelled && changes == 0, "Cancellation created material state.");
        });
        test("Material assignment errors do not append a second fallback slot", () =>
        {
            int fallbacks = 0, assignments = 0; bool failed = false;
            try
            {
                MaterialSlotAssignment.AssignAsync(0, () => Task.FromResult("converted"),
                    _ => { fallbacks++; return Task.FromResult("fallback"); },
                    (_, _) => { assignments++; throw new InvalidOperationException("destination gone"); }).GetAwaiter().GetResult();
            }
            catch (InvalidOperationException) { failed = true; }
            check(failed && fallbacks == 0 && assignments == 1, "Assignment failure triggered another slot.");
        });
        test("Null conversion uses fallback and failed fallback leaves the slot untouched", () =>
        {
            int assignments = 0;
            MaterialSlotAssignment.AssignAsync<string>(0, () => Task.FromResult<string>(null!), _ => Task.FromResult("fallback"),
                (_, material) => { assignments++; check(material == "fallback", "Null assigned."); return Task.CompletedTask; }).GetAwaiter().GetResult();
            bool failed = false;
            try
            {
                MaterialSlotAssignment.AssignAsync<string>(1, () => throw new IOException(), _ => throw new IOException(),
                    (_, _) => { assignments++; return Task.CompletedTask; }).GetAwaiter().GetResult();
            }
            catch (IOException) { failed = true; }
            check(failed && assignments == 1, "Failed fallback mutated slots.");
        });
    }
}
