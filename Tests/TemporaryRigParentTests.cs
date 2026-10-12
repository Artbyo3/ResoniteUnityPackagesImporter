using UnityPackageImporter.Models;

internal static class TemporaryRigParentTests
{
    internal static void Register(Action<string, Action> test)
    {
        test("Temporary rig parent retires only after both native spaces reach the avatar", () =>
        {
            var f = new Fixture();
            f.Complete(f.Avatar, f.Avatar);
            Check(f.Promotions == 1 && !f.Wrapper.Alive && f.Rig.Parent == f.AuthoredParent,
                "The importer-owned parent was not retired.");
            Check(f.Mesh.Parent == f.Rig && f.Bone.Parent == f.Rig && f.AuthoredParent.Alive,
                "Authored rig or renderer hierarchy was changed.");
        });

        test("Temporary rig parent remains when either IK space still depends on it", () =>
        {
            var f = new Fixture();
            Reject(() => f.Complete(f.Wrapper, f.Avatar));
            Reject(() => f.Complete(f.Avatar, f.Wrapper));
            Check(f.Promotions == 0 && f.Wrapper.Alive && f.Rig.Parent == f.Wrapper,
                "Cleanup ran before the native handoff.");
        });

        test("Temporary rig cleanup cannot escape its completed avatar or match by name", () =>
        {
            var f = new Fixture();
            var otherAvatar = new Node(f.Avatar.Name);
            Reject(() => f.Owner.Complete(otherAvatar, otherAvatar, otherAvatar,
                n => n.Parent!, n => n.Alive, f.Promote));
            Check(f.Promotions == 0, "A same-named avatar acquired another import's wrapper.");
            var authoredLookalike = new Node(f.Wrapper.Name, f.AuthoredParent);
            f.Complete(f.Avatar, f.Avatar);
            Check(authoredLookalike.Alive && authoredLookalike.Parent == f.AuthoredParent,
                "An authored same-named object was removed.");
        });

        test("Temporary rig cleanup refuses destroyed or reparented imported content", () =>
        {
            var dead = new Fixture();
            dead.Rig.Alive = false;
            Reject(() => dead.Complete(dead.Avatar, dead.Avatar));
            Check(dead.Promotions == 0, "Destroyed rig cleanup was attempted.");

            var moved = new Fixture();
            moved.Rig.Parent = moved.AuthoredParent;
            Reject(() => moved.Complete(moved.Avatar, moved.Avatar));
            Check(moved.Promotions == 0 && moved.Wrapper.Alive,
                "A changed import hierarchy was treated as an intact temporary wrapper.");
        });

        test("Temporary rig cleanup rejects cyclic ancestry and the avatar root itself", () =>
        {
            var cycle = new Fixture();
            cycle.AuthoredParent.Parent = cycle.AuthoredParent;
            Reject(() => cycle.Complete(cycle.Avatar, cycle.Avatar));
            Check(cycle.Promotions == 0, "Cyclic ancestry reached the engine mutation.");

            var root = new Fixture();
            var invalidOwner = new TemporaryRigParent<Node>(root.Avatar, root.AuthoredParent);
            Reject(() => invalidOwner.Complete(root.Avatar, root.Avatar, root.Avatar,
                n => n.Parent!, n => n.Alive, root.Promote));
            Check(root.Promotions == 0, "Cleanup was allowed to destroy the completed avatar root.");
        });

        test("Temporary rig cleanup completes once and does not promote twice", () =>
        {
            var f = new Fixture();
            f.Complete(f.Avatar, f.Avatar);
            f.Complete(f.Avatar, f.Avatar);
            Check(f.Promotions == 1, "Completed hierarchy was mutated twice.");
        });

        test("Failed temporary rig promotion does not falsely complete the handoff", () =>
        {
            var f = new Fixture();
            Reject(() => f.Owner.Complete(f.Avatar, f.Avatar, f.Avatar,
                n => n.Parent!, n => n.Alive,
                (_, _) => throw new InvalidOperationException("Engine adapter refused mutation")));
            f.Complete(f.Avatar, f.Avatar);
            Check(f.Promotions == 1, "An unsuccessful promotion was marked completed.");
        });
    }

    private sealed class Node(string name, Node? parent = null)
    {
        public readonly string Name = name;
        public Node? Parent = parent;
        public bool Alive = true;
    }

    private sealed class Fixture
    {
        internal readonly Node Avatar = new("Avatar");
        internal readonly Node AuthoredParent;
        internal readonly Node Wrapper;
        internal readonly Node Rig;
        internal readonly Node Mesh;
        internal readonly Node Bone;
        internal readonly TemporaryRigParent<Node> Owner;
        internal int Promotions;

        internal Fixture()
        {
            AuthoredParent = new Node("Authored parent", Avatar);
            Wrapper = new Node("Temporary movement parent", AuthoredParent);
            Rig = new Node("Rig", Wrapper);
            Mesh = new Node("Body mesh", Rig);
            Bone = new Node("Bone", Rig);
            Owner = new TemporaryRigParent<Node>(Wrapper, Rig);
        }

        internal void Complete(Node simulation, Node offset) => Owner.Complete(
            Avatar, simulation, offset, n => n.Parent!, n => n.Alive, Promote);

        internal void Promote(Node wrapper, Node destination)
        {
            Check(wrapper == Wrapper && destination == AuthoredParent,
                "Promotion was not scoped to the recorded wrapper and its current parent.");
            Promotions++;
            Rig.Parent = destination;
            wrapper.Alive = false;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new Exception("Unsafe hierarchy was accepted.");
    }
}
