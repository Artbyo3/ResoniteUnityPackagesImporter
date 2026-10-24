using UnityPackageImporter.Models;

internal static class AvatarAnimationPathResolverTests
{
    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        AvatarAnimationObject[] Nodes() => new[]
        {
            new AvatarAnimationObject(1, 0, "Avatar"), new AvatarAnimationObject(2, 1, "Host"),
            new AvatarAnimationObject(3, 2, "Body"), new AvatarAnimationObject(4, 3, "Detail"),
            new AvatarAnimationObject(5, 1, "Body"), new AvatarAnimationObject(9, 0, "Foreign"),
            new AvatarAnimationObject(10, 9, "Body")
        };
        test("Animation paths prefer nearest exact host-relative hierarchy", () =>
        {
            var resolver = new AvatarAnimationPathResolver(1, Nodes());
            check(resolver.Resolve(2, "Body/Detail", _ => true) == 4 && resolver.Resolve(2, "Body", _ => true) == 3,
                "Exact local hierarchy was not retained.");
            check(resolver.Resolve(3, "Body", _ => true) == 3, "Ancestor-relative rewriting did not find the nearest complete path.");
        });
        test("Animation paths do not borrow a matching foreign leaf", () =>
        {
            var resolver = new AvatarAnimationPathResolver(1, Nodes());
            check(resolver.Resolve(2, "Foreign/Body", _ => true) == 0 && resolver.Resolve(9, "Body", _ => true) == 0,
                "Binding escaped the owning avatar.");
        });
        test("Animation paths reject ambiguity before searching another ancestor", () =>
        {
            var resolver = new AvatarAnimationPathResolver(1, Nodes().Append(new AvatarAnimationObject(6, 2, "Body")));
            check(resolver.Resolve(2, "Body", _ => true) == 0, "Ambiguous near path borrowed a different ancestor target.");
        });
        test("Animation path empty binding targets only its exact host", () =>
        {
            var resolver = new AvatarAnimationPathResolver(1, Nodes());
            check(resolver.Resolve(2, "", _ => true) == 2 && resolver.Resolve(2, "", identity => identity == 1) == 0,
                "Empty binding climbed to a different component owner.");
        });
        foreach (string path in new[] { "/Body", "../Body", "Body/../Detail", "./Body", "Body//Detail", "Body/", "Body\\Detail" })
            test("Animation paths reject unsafe serialized path " + path, () =>
                check(new AvatarAnimationPathResolver(1, Nodes()).Resolve(2, path, _ => true) == 0, "Unsafe animation path was accepted."));
        test("Animation paths fail closed on disconnected cyclic hierarchy", () =>
        {
            var resolver = new AvatarAnimationPathResolver(1, Nodes().Concat(new[] { new AvatarAnimationObject(20, 21, "A"), new AvatarAnimationObject(21, 20, "B") }));
            check(resolver.Resolve(20, "B", _ => true) == 0, "Cyclic objects gained avatar ownership.");
        });
    }
}

