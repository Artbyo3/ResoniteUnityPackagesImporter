using UnityPackageImporter.Models;

internal static class AuthoredAppearanceHierarchyTests
{
    public static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        test("Authored appearance hierarchy accepts an empty snapshot", () =>
        {
            var hierarchy = new AuthoredAppearanceHierarchy(Array.Empty<AuthoredAppearanceObject>(), Array.Empty<KeyValuePair<long, long>>());
            check(hierarchy.Objects.Count == 0 && hierarchy.TransformOwners.Count == 0, "Empty source snapshots must stay empty.");
        });

        test("Authored appearance hierarchy preserves repeated model instances by exact identity", () =>
        {
            var hierarchy = new AuthoredAppearanceHierarchy(new[]
            {
                new AuthoredAppearanceObject(102, 101, "Body", true),
                new AuthoredAppearanceObject(202, 201, "Body", false),
                new AuthoredAppearanceObject(101, 0, "Model", true),
                new AuthoredAppearanceObject(201, 0, "Model", true)
            }, new[] { Owner(1001, 101), Owner(1002, 102), Owner(2001, 201), Owner(2002, 202) });

            check(hierarchy.Objects.Count == 4, "Repeated model names must not collapse separate instances.");
            check(hierarchy.Objects[102].ParentId == 101 && hierarchy.Objects[202].ParentId == 201, "Repeated model parents must remain instance-scoped.");
            check(hierarchy.TransformOwners[1002] == 102 && hierarchy.TransformOwners[2002] == 202, "Transform aliases must preserve their exact local owners.");
            check(hierarchy.Objects[102].ActiveSelf && !hierarchy.Objects[202].ActiveSelf, "Repeated instance active states must not leak between instances.");
        });

        test("Authored appearance hierarchy preserves inactive ancestors and active self state", () =>
        {
            var hierarchy = new AuthoredAppearanceHierarchy(new[]
            {
                new AuthoredAppearanceObject(-1, 0, "Unaliased ancestor", false),
                new AuthoredAppearanceObject(10, -1, "Child", true)
            }, new[] { Owner(20, 10) });

            check(hierarchy.Objects[10].ParentId == -1 && !hierarchy.Objects[-1].ActiveSelf, "Private ancestor identity and inactive state must survive hydration.");
            check(hierarchy.Objects[10].ActiveSelf, "ActiveSelf must not be replaced by inherited effective visibility.");
            check(hierarchy.TransformOwners.Count == 1, "Unaliased ancestors do not require invented Transform aliases.");
        });

        test("Authored appearance hierarchy defensively copies mutable inputs", () =>
        {
            var objects = new List<AuthoredAppearanceObject> { new(1, 0, "Original", false) };
            var owners = new Dictionary<long, long> { [2] = 1 };
            var hierarchy = new AuthoredAppearanceHierarchy(objects, owners);
            objects[0] = objects[0] with { Name = "Changed", ActiveSelf = true, ParentId = 99 };
            objects.Clear();
            owners.Clear();
            owners.Add(3, 99);

            check(hierarchy.Objects.Count == 1 && hierarchy.Objects[1] == new AuthoredAppearanceObject(1, 0, "Original", false), "Input replacement or removal must not mutate the snapshot.");
            check(hierarchy.TransformOwners.Count == 1 && hierarchy.TransformOwners[2] == 1, "Input alias mutation must not mutate the snapshot.");
        });

        test("Authored appearance hierarchy exposes dictionaries that reject mutation", () =>
        {
            var hierarchy = new AuthoredAppearanceHierarchy(new[] { new AuthoredAppearanceObject(1, 0, "Root", true) }, new[] { Owner(2, 1) });
            var objects = (IDictionary<long, AuthoredAppearanceObject>)hierarchy.Objects;
            var owners = (IDictionary<long, long>)hierarchy.TransformOwners;

            check(objects.IsReadOnly && owners.IsReadOnly, "Snapshot dictionaries must be read-only even when cast to mutable interfaces.");
            Throws<NotSupportedException>(() => objects[1] = new(1, 0, "Changed", false), check);
            Throws<NotSupportedException>(() => objects.Add(3, new(3, 0, "Extra", true)), check);
            Throws<NotSupportedException>(() => objects.Clear(), check);
            Throws<NotSupportedException>(() => owners[2] = 99, check);
            Throws<NotSupportedException>(() => owners.Add(3, 1), check);
            Throws<NotSupportedException>(() => owners.Clear(), check);
            check(hierarchy.Objects[1].Name == "Root" && hierarchy.TransformOwners[2] == 1, "Failed mutation attempts must leave source data intact.");
        });

        test("Authored appearance hierarchy rejects null inputs and null object entries", () =>
        {
            Throws<ArgumentNullException>(() => new AuthoredAppearanceHierarchy(null!, Array.Empty<KeyValuePair<long, long>>()), check);
            Throws<ArgumentNullException>(() => new AuthoredAppearanceHierarchy(Array.Empty<AuthoredAppearanceObject>(), null!), check);
            Throws<ArgumentException>(() => new AuthoredAppearanceHierarchy(new AuthoredAppearanceObject[] { null! }, Array.Empty<KeyValuePair<long, long>>()), check);
        });

        test("Authored appearance hierarchy rejects zero and duplicate object identities", () =>
        {
            Throws<ArgumentException>(() => new AuthoredAppearanceHierarchy(new[] { new AuthoredAppearanceObject(0, 0, "Root", true) }, Array.Empty<KeyValuePair<long, long>>()), check);
            Throws<ArgumentException>(() => new AuthoredAppearanceHierarchy(new[]
            {
                new AuthoredAppearanceObject(1, 0, "Root", true),
                new AuthoredAppearanceObject(1, 0, "Other", false)
            }, Array.Empty<KeyValuePair<long, long>>()), check);
            Throws<ArgumentException>(() => new AuthoredAppearanceHierarchy(new[]
            {
                new AuthoredAppearanceObject(1, 0, "Root", true),
                new AuthoredAppearanceObject(1, 0, "Root", true)
            }, Array.Empty<KeyValuePair<long, long>>()), check);
        });

        test("Authored appearance hierarchy rejects missing parents even on unaliased objects", () =>
        {
            Throws<ArgumentException>(() => new AuthoredAppearanceHierarchy(new[]
            {
                new AuthoredAppearanceObject(1, 0, "Root", true),
                new AuthoredAppearanceObject(3, 99, "Unaliased", true)
            }, new[] { Owner(2, 1) }), check);
        });

        test("Authored appearance hierarchy rejects self parent and disconnected multi object cycles", () =>
        {
            Throws<ArgumentException>(() => new AuthoredAppearanceHierarchy(new[] { new AuthoredAppearanceObject(1, 1, "Self", true) }, Array.Empty<KeyValuePair<long, long>>()), check);
            Throws<ArgumentException>(() => new AuthoredAppearanceHierarchy(new[]
            {
                new AuthoredAppearanceObject(1, 0, "Valid root", true),
                new AuthoredAppearanceObject(2, 3, "Cycle first", true),
                new AuthoredAppearanceObject(3, 4, "Cycle second", true),
                new AuthoredAppearanceObject(4, 2, "Cycle third", true),
                new AuthoredAppearanceObject(5, 2, "Cycle descendant", true)
            }, new[] { Owner(10, 1) }), check);
        });

        test("Authored appearance hierarchy rejects zero and duplicate Transform aliases", () =>
        {
            var objects = new[] { new AuthoredAppearanceObject(1, 0, "Root", true), new AuthoredAppearanceObject(3, 1, "Child", true) };
            Throws<ArgumentException>(() => new AuthoredAppearanceHierarchy(objects, new[] { Owner(0, 1) }), check);
            Throws<ArgumentException>(() => new AuthoredAppearanceHierarchy(objects, new[] { Owner(2, 1), Owner(2, 1) }), check);
            Throws<ArgumentException>(() => new AuthoredAppearanceHierarchy(objects, new[] { Owner(2, 1), Owner(2, 3) }), check);
        });

        test("Authored appearance hierarchy rejects missing and zero Transform owners", () =>
        {
            var objects = new[] { new AuthoredAppearanceObject(1, 0, "Root", true) };
            Throws<ArgumentException>(() => new AuthoredAppearanceHierarchy(objects, new[] { Owner(2, 99) }), check);
            Throws<ArgumentException>(() => new AuthoredAppearanceHierarchy(objects, new[] { Owner(2, 0) }), check);
            Throws<ArgumentException>(() => new AuthoredAppearanceHierarchy(Array.Empty<AuthoredAppearanceObject>(), new[] { Owner(2, 1) }), check);
        });

        test("Authored appearance hierarchy rejects multiple Transform aliases assigned to one owner", () =>
        {
            Throws<ArgumentException>(() => new AuthoredAppearanceHierarchy(new[] { new AuthoredAppearanceObject(1, 0, "Root", true) }, new[] { Owner(2, 1), Owner(3, 1) }), check);
        });

        test("Authored appearance hierarchy accepts signed identities and separate identity namespaces", () =>
        {
            var hierarchy = new AuthoredAppearanceHierarchy(new[]
            {
                new AuthoredAppearanceObject(long.MinValue, 0, "Root", true),
                new AuthoredAppearanceObject(long.MaxValue, long.MinValue, "Child", true)
            }, new[] { Owner(long.MaxValue, long.MinValue), Owner(long.MinValue, long.MaxValue) });

            check(hierarchy.TransformOwners[long.MinValue] == long.MaxValue && hierarchy.TransformOwners[long.MaxValue] == long.MinValue, "Signed aliases and object IDs must retain exact values in their separate dictionaries.");
        });

        test("Authored appearance hierarchy validates deep unordered ancestry without recursion", () =>
        {
            const int count = 20000;
            var objects = Enumerable.Range(1, count).Select(identity => new AuthoredAppearanceObject(identity, identity == count ? 0 : identity + 1, "Node", true));
            var hierarchy = new AuthoredAppearanceHierarchy(objects, new[] { Owner(-1, 1) });

            check(hierarchy.Objects.Count == count && hierarchy.Objects[1].ParentId == 2 && hierarchy.Objects[count].ParentId == 0, "Deep ancestry must remain complete without stack-dependent traversal.");
        });
    }

    private static KeyValuePair<long, long> Owner(long transformId, long objectId) => new(transformId, objectId);

    private static void Throws<TException>(Action action, Action<bool, string> check) where TException : Exception
    {
        Exception? observed = null;
        try
        {
            action();
        }
        catch (TException exception)
        {
            observed = exception;
        }
        check(observed != null, $"Expected {typeof(TException).Name} for invalid or immutable hierarchy operation.");
    }
}
