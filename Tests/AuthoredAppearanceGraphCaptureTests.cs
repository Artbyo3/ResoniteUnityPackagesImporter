using UnityPackageImporter.Models;

internal static class AuthoredAppearanceGraphCaptureTests
{
    private sealed class Node(string name, bool active = true)
    {
        internal string Name { get; set; } = name;
        internal bool Active { get; set; } = active;
        internal List<Node> Children { get; } = new();
    }

    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        AuthoredAppearanceGraphCapture<Node> Capture(Node stage, Dictionary<Node, long> aliases,
            Dictionary<long, Node>? transforms = null, HashSet<Node>? scaffolds = null, long[]? reserved = null) =>
            AuthoredAppearanceGraphCapture<Node>.Capture(stage, aliases, transforms ?? new(), scaffolds ?? new(),
                reserved ?? Array.Empty<long>(), node => node.Children, node => node.Name, node => node.Active);
        bool Reject(Action operation)
        {
            try { operation(); return false; }
            catch (InvalidDataException) { return true; }
        }
        test("Appearance capture removes only verified model scaffolds and freezes authored paths", () =>
        {
            var stage = new Node("Stage");
            var avatar = new Node("Avatar");
            var scaffold = new Node("RootNode");
            var body = new Node("Body", false);
            stage.Children.Add(avatar);
            avatar.Children.Add(scaffold);
            scaffold.Children.Add(body);
            var captured = Capture(stage, new() { [avatar] = 1, [body] = 3 }, new() { [101] = avatar, [103] = body }, new() { scaffold });
            check(captured.Hierarchy.Objects.Count == 2 && captured.Hierarchy.Objects[3].ParentId == 1 &&
                !captured.Hierarchy.Objects[3].ActiveSelf && ReferenceEquals(captured.Targets[3], body), "The native scaffold changed authored parentage or active state.");
            avatar.Children.Clear();
            body.Name = "Reparented";
            body.Active = true;
            check(captured.Hierarchy.Objects[3] == new AuthoredAppearanceObject(3, 1, "Body", false), "Subsequent native changes mutated the authored snapshot.");
        });
        test("Appearance capture keeps ordinary RootNode names and unaliased ancestors", () =>
        {
            var stage = new Node("Stage");
            var avatar = new Node("Avatar");
            var ordinary = new Node("RootNode");
            var body = new Node("Body");
            stage.Children.Add(avatar);
            avatar.Children.Add(ordinary);
            ordinary.Children.Add(body);
            var captured = Capture(stage, new() { [avatar] = 1, [body] = 3 }, reserved: new[] { long.MinValue, long.MinValue + 1 });
            long intermediate = captured.Hierarchy.Objects[3].ParentId;
            check(intermediate == long.MinValue + 2 && captured.Hierarchy.Objects[intermediate].Name == "RootNode" &&
                captured.Hierarchy.Objects[intermediate].ParentId == 1, "Names must not authorize scaffold removal; private IDs must avoid reserved identities.");
        });
        test("Appearance capture preserves two same-name model instances and nested scaffolds", () =>
        {
            var stage = new Node("Stage");
            var first = new Node("Model");
            var second = new Node("Model", false);
            var firstRoot = new Node("RootNode");
            var secondRoot = new Node("RootNode");
            var firstBody = new Node("Body");
            var secondBody = new Node("Body", false);
            stage.Children.Add(first);
            stage.Children.Add(second);
            first.Children.Add(firstRoot);
            second.Children.Add(secondRoot);
            firstRoot.Children.Add(firstBody);
            secondRoot.Children.Add(secondBody);
            var captured = Capture(stage, new() { [first] = 1, [firstBody] = 2, [second] = 3, [secondBody] = 4 },
                new() { [101] = firstBody, [102] = secondBody }, new() { firstRoot, secondRoot });
            check(captured.Hierarchy.Objects[2].ParentId == 1 && captured.Hierarchy.Objects[4].ParentId == 3 &&
                captured.Hierarchy.TransformOwners[101] == 2 && captured.Hierarchy.TransformOwners[102] == 4 &&
                !captured.Hierarchy.Objects[3].ActiveSelf && !captured.Hierarchy.Objects[4].ActiveSelf, "Repeated models leaked hierarchy or state.");
        });
        test("Appearance capture rejects scaffold flattening that loses identity or visibility", () =>
        {
            var stage = new Node("Stage");
            var scaffold = new Node("RootNode", false);
            stage.Children.Add(scaffold);
            check(Reject(() => Capture(stage, new(), scaffolds: new() { scaffold })), "An inactive scaffold must not disappear from inherited visibility.");
            scaffold.Active = true;
            check(Reject(() => Capture(stage, new() { [scaffold] = 1 }, scaffolds: new() { scaffold })), "An authored source node must not be flattened as scaffolding.");
        });
        test("Appearance capture rejects foreign aliases and Transform targets", () =>
        {
            var stage = new Node("Stage");
            var foreign = new Node("Foreign");
            check(Reject(() => Capture(stage, new() { [foreign] = 1 })), "A foreign GameObject acquired this import scope.");
            check(Reject(() => Capture(stage, new(), new() { [101] = foreign })), "A foreign Transform acquired this import scope.");
        });
        test("Appearance capture rejects cyclic graphs and duplicated source identities", () =>
        {
            var stage = new Node("Stage");
            var first = new Node("First");
            var second = new Node("Second");
            stage.Children.Add(first);
            stage.Children.Add(second);
            check(Reject(() => Capture(stage, new() { [first] = 1, [second] = 1 })), "Two source nodes claimed one identity.");
            first.Children.Add(stage);
            check(Reject(() => Capture(stage, new())), "A source cycle was captured.");
        });
    }
}
