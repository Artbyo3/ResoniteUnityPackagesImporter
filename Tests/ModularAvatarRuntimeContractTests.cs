using System.Numerics;
using UnityPackageImporter.Models;

internal static class ModularAvatarRuntimeContractTests
{
    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        test("Attachment paths prefer exact identity and reject ambiguous siblings", () =>
        {
            var avatar = new Node("Avatar");
            var exact = new Node("Hand", avatar);
            var folded = new Node("hand", avatar);
            var children = new[] { exact, folded };
            check(BoneAttachmentTarget.Resolve(children, "Hand", null, node => node.Name, node => node.Name) == exact,
                "An exact path was rejected because of a differently cased sibling.");
            check(BoneAttachmentTarget.Resolve(children, "HAND", null, node => node.Name, node => node.Name) == null,
                "An ambiguous path selected a sibling by traversal order.");
            check(ModularAvatarMergePlanner.ResolveTarget(avatar, children, "Missing/Hand", "Hand", "", "",
                node => node.Name, node => node.Name) == null, "An explicit missing path fell back to its leaf name.");
        });

        test("Model-owned metadata does not borrow same-named clothing bones", () =>
        {
            var avatar = new Node("Avatar");
            var ownBone = new Node("Forearm", avatar);
            var garment = new Node("Garment", avatar);
            var clothingBone = new Node("Forearm", garment);
            check(ModelBoneIndex<Node>.FindOwned(new[] { ownBone, ownBone }, "Forearm", node => node.Name) == ownBone,
                "Duplicate identity aliases were treated as duplicate bones.");
            check(ModelBoneIndex<Node>.FindOwned(new[] { ownBone, clothingBone }, "Forearm", node => node.Name) == null,
                "An ambiguous model selected its first bone.");
            var index = new ModelBoneIndex<Node>();
            index.Add(avatar, 15, ownBone);
            index.Add(garment, 15, clothingBone);
            check(index.Resolve(15, avatar, garment, Contains, node => node.Alive) == ownBone,
                "A clothing model replaced the avatar's humanoid bone.");
            var accessory = new Node("Accessory", avatar);
            check(index.Resolve(15, avatar, accessory, Contains, node => node.Alive) == ownBone,
                "The avatar model did not take precedence over another humanoid model.");
        });

        test("Humanoid mapping refuses other avatars, dead bones and ambiguous models", () =>
        {
            var container = new Node("Container");
            var first = new Node("Avatar", container);
            var second = new Node("Avatar", container);
            var firstBone = new Node("Hand", first);
            var secondBone = new Node("Hand", second);
            var accessory = new Node("Accessory", second);
            var index = new ModelBoneIndex<Node>();
            index.Add(first, 17, firstBone);
            check(index.Resolve(17, second, accessory, Contains, node => node.Alive) == null, "Another avatar supplied the bone.");
            index.Add(second, 17, secondBone);
            secondBone.Alive = false;
            check(index.Resolve(17, second, accessory, Contains, node => node.Alive) == null, "A destroyed bone supplied the target.");
            secondBone.Alive = true;
            index.Add(second, 17, new Node("Hand", second));
            check(index.Resolve(17, second, accessory, Contains, node => node.Alive) == null, "An ambiguous primary model was accepted.");
        });

        test("Unresolved declared humanoid metadata cannot fall back to another named bone", () =>
        {
            var avatar = new Node("Avatar");
            var accessory = new Node("Accessory", avatar);
            var index = new ModelBoneIndex<Node>();
            index.Add(avatar, 17, null);
            check(index.TryResolve(17, avatar, accessory, Contains, node => node.Alive, out var bone) && bone == null,
                "A declared but unresolved bone was treated as absent metadata.");
            check(!index.TryResolve(18, avatar, accessory, Contains, node => node.Alive, out _), "An undeclared bone was invented.");
        });

        foreach (float sourceScale in new[] { 0.01f, 1f, 100f, -1f })
        {
            float scale = sourceScale;
            test("Skin transfer preserves bind pose with model scale " + scale, () =>
            {
                var world = new Node("World");
                var target = new Node("Armature", world, Matrix4x4.CreateRotationX(0.4f) * Matrix4x4.CreateTranslation(2, 3, 4));
                var source = new Node("ClothingArmature", world, Matrix4x4.CreateScale(scale) *
                    Matrix4x4.CreateRotationZ(0.7f) * Matrix4x4.CreateTranslation(-3, 2, 1));
                var child = new Node("Child", source, Matrix4x4.CreateTranslation(0, 0.25f, 0));
                var targetChild = new Node("Child", target, Matrix4x4.CreateTranslation(0, 0.2f, 0));
                var retained = new Node("Decoration", child, Matrix4x4.CreateTranslation(0.1f, 0, 0));
                var skinBones = new[] { source, child };
                var before = skinBones.Select(bone => bone.World).ToArray();
                var binds = before.Select(matrix => Inverse(matrix)).ToArray();
                var retainedWorld = retained.World;
                var plan = SkinBindingTransfer.Plan(new[] { (source, target), (child, targetChild) }, Contains);
                SkinBindingTransfer.Apply(plan, ReparentPreservingWorld);
                for (int index = 0; index < skinBones.Length; index++)
                {
                    check(Near(before[index], skinBones[index].World), "Original bone space changed during transfer.");
                    check(Near(binds[index] * skinBones[index].World, Matrix4x4.Identity), "Skin binding no longer matches its bone.");
                }
                check(Near(retainedWorld, retained.World) && retained.Parent == child, "An unmapped child lost its rest space.");
                check(skinBones[0] == source && skinBones[1] == child, "Skin bone identities were replaced.");
                var origin = Vector3.Transform(Vector3.Zero, child.World);
                target.Local *= Matrix4x4.CreateTranslation(1, 0, 0);
                check(Vector3.Distance(Vector3.Transform(Vector3.Zero, child.World), origin + Vector3.UnitX) < 0.002f,
                    "A retained binding did not follow its target.");
            });
        }

        test("Skin transfer rejects cycles and conflicting mappings before any move", () =>
        {
            var root = new Node("Root");
            var source = new Node("Source", root);
            var child = new Node("Child", source);
            var target = new Node("Target", root);
            Reject(() => SkinBindingTransfer.Plan(new[] { (source, source) }, Contains));
            Reject(() => SkinBindingTransfer.Plan(new[] { (source, child) }, Contains));
            Reject(() => SkinBindingTransfer.Plan(new[] { (source, target), (source, root) }, Contains));
            Reject(() => SkinBindingTransfer.Plan(new[] { (source, target), (target, source) }, Contains));
            check(source.Parent == root && child.Parent == source && target.Parent == root, "Preflight mutated a hierarchy.");
        });

        test("Skin rest-space validation rejects distortion and non-finite transforms", () =>
        {
            check(SkinBindingTransfer.WithinTolerance(0.00001f, 0.0001f), "Floating-point roundoff was rejected.");
            check(!SkinBindingTransfer.WithinTolerance(0.1f, 0.0001f) &&
                !SkinBindingTransfer.WithinTolerance(float.NaN, 0.001f) &&
                !SkinBindingTransfer.WithinTolerance(float.PositiveInfinity, 0.001f) &&
                !SkinBindingTransfer.WithinTolerance(0, float.NaN), "Invalid skin spaces were accepted.");
        });

        test("Preview anchors survive copied hierarchy identities and duplicate names", () =>
        {
            var original = new Node("Envelope");
            var first = new Node("SameName", original);
            var wrapper = new Node("SameName", original, Matrix4x4.CreateTranslation(1.259f, 0, 0));
            var avatar = new Node("Avatar", wrapper);
            var path = HierarchyIdentity.Path(original, avatar, node => node.Parent, node => node.Children);
            var offset = HierarchyIdentity.PointInAncestor(original, avatar, Vector3.Zero,
                node => node.Parent, (node, point) => Vector3.Transform(point, node.Local));
            var copy = Clone(original);
            var anchor = HierarchyIdentity.Resolve(copy, path, node => node.Children);
            copy.Local = Matrix4x4.CreateTranslation(-offset);
            check(Vector3.Transform(Vector3.Zero, anchor.World).Length() < 0.0001f, "The copied avatar remained offset from the preview origin.");
            check(wrapper.Local.Translation.X == 1.259f && avatar.Parent == wrapper, "Preview placement changed the source hierarchy.");
            check(anchor != first && anchor.Name == "Avatar", "Preview resolution selected a same-named sibling.");
        });

        test("Preview placement is independent of staging world and template transforms", () =>
        {
            var staging = new Node("Staging", null, Matrix4x4.CreateScale(0.01f) *
                Matrix4x4.CreateRotationY(0.9f) * Matrix4x4.CreateTranslation(23, -5, 17));
            var envelope = new Node("Envelope", staging, Matrix4x4.CreateTranslation(-7, 3, 11));
            var movement = new Node("Movement", envelope, Matrix4x4.CreateScale(1.2f, 0.8f, 1.4f) *
                Matrix4x4.CreateRotationY(0.6f) * Matrix4x4.CreateTranslation(1.259f, 0.3f, -0.2f));
            var avatar = new Node("Avatar", movement, Matrix4x4.CreateTranslation(0.2f, -0.1f, 0.4f));
            var sourceBefore = avatar.World;
            var path = HierarchyIdentity.Path(envelope, avatar, node => node.Parent, node => node.Children);
            var offset = HierarchyIdentity.PointInAncestor(envelope, avatar, Vector3.Zero,
                node => node.Parent, (node, point) => Vector3.Transform(point, node.Local));
            var template = new Node("Template Preview", null, Matrix4x4.CreateScale(0.35f, 0.6f, 0.5f) *
                Matrix4x4.CreateRotationY(MathF.PI) * Matrix4x4.CreateTranslation(4, 0, -8));
            var placement = new Node("Placement", template);
            var copy = Clone(envelope, placement);
            copy.Local = Matrix4x4.Identity;
            placement.Local = Matrix4x4.CreateTranslation(-offset);
            var copiedAvatar = HierarchyIdentity.Resolve(copy, path, node => node.Children);
            check(Vector3.Distance(Vector3.Transform(Vector3.Zero, copiedAvatar.World),
                Vector3.Transform(Vector3.Zero, template.World)) < 0.0001f,
                "Staging placement or template rotation and scale displaced the preview anchor.");
            check(Near(avatar.World, sourceBefore), "Presentation placement changed the authored source.");
        });

        test("Preview anchor resolution refuses out-of-scope or changed hierarchies", () =>
        {
            var root = new Node("Root");
            Reject(() => HierarchyIdentity.Path(root, new Node("Foreign"), node => node.Parent, node => node.Children));
            Reject(() => HierarchyIdentity.Resolve(root, new[] { 1 }, node => node.Children));
            Reject(() => HierarchyIdentity.PointInAncestor(root, new Node("Foreign"), Vector3.Zero,
                node => node.Parent, (node, point) => Vector3.Transform(point, node.Local)));
            var cyclic = new Node("Cycle");
            cyclic.Parent = cyclic;
            Reject(() => HierarchyIdentity.PointInAncestor(root, cyclic, Vector3.Zero,
                node => node.Parent, (node, point) => Vector3.Transform(point, node.Local)));
        });

        test("Blocked avatars keep avatar identity without bypassing validation", () =>
        {
            var result = ImportResult.Evaluate(true, false, NativeAvatarReadiness.Ready, 0, 0, 1);
            var action = ImportActionPolicy.Intended(true, false);
            check(action == ImportOutputAction.CreateAvatar && ImportActionPolicy.Label(action) == "createAvatar", "A blocked avatar was relabeled as an ordinary object.");
            check(!result.CanPerform(action) && !result.CanPerform(ImportOutputAction.KeepObject), "An incomplete avatar became publishable.");
            check(ImportActionPolicy.Intended(false, true) == ImportOutputAction.InstallOutfit &&
                ImportActionPolicy.Intended(false, false) == ImportOutputAction.KeepObject, "Outfit or prop intent changed.");
        });

        test("Native-unready avatars cannot fall back to keeping an ordinary object", () =>
        {
            foreach (var readiness in Enum.GetValues<NativeAvatarReadiness>().Where(value => value != NativeAvatarReadiness.Ready))
            {
                var result = ImportResult.Evaluate(true, false, readiness, 0, 0, 0);
                check(result.StructureReady && result.CanPerform(ImportOutputAction.KeepObject), "Fixture must isolate native readiness from structural readiness.");
                check(!ImportActionPolicy.CanPerformMainAction(result, true, false), "Create Avatar was enabled by KeepObject readiness.");
                bool rejected = false;
                try { result.Require(ImportActionPolicy.Intended(true, false)); }
                catch (InvalidOperationException) { rejected = true; }
                check(rejected, "Execution authorized the ordinary-object fallback for an avatar.");
                check(ImportActionPolicy.CanPerformMainAction(result, false, false), "A legitimate prop could not be kept.");
                check(!ImportActionPolicy.CanPerformMainAction(result, false, true), "An outfit was routed to the main create action.");
            }
            var ready = ImportResult.Evaluate(true, false, NativeAvatarReadiness.Ready, 0, 0, 0);
            check(ImportActionPolicy.CanPerformMainAction(ready, true, false), "A ready avatar was blocked.");
        });

        foreach (int attachmentMode in Enumerable.Range(0, 5))
        {
            int mode = attachmentMode;
            test("MA YAML Bone Proxy attachment mode " + mode + " preserves its authored contract", () =>
            {
                var manifest = AvatarPackageIndex.ParseText(ProxyYaml(mode, 15, "Socket"));
                var operation = (BoneAttachmentOperation)AvatarBuildPlanner.CreatePlan(manifest).Operations.Single();
                check(operation.AlignPosition == (mode is not (2 or 4)) && operation.AlignRotation == (mode is not (2 or 3)), "Authored pose mode was replaced by unconditional alignment.");
                check(operation.KeepOffsets == (mode is 2 or 3 or 4) && operation.TargetHumanoidBone == 15 && operation.TargetOffsetPath == "Socket", "Target or offset semantics changed.");
            });
        }

        test("MA YAML avatar-root and root-relative proxies do not use invalid humanoid index 55", () =>
        {
            foreach (string path in new[] { "$$AVATAR", "Armature/Socket" })
            {
                var plan = AvatarBuildPlanner.CreatePlan(AvatarPackageIndex.ParseText(ProxyYaml(2, 55, path)));
                var operation = (BoneAttachmentOperation)plan.Operations.Single();
                check(!plan.HasErrors && operation.TargetHumanoidBone == -1 && operation.TargetReferencePath == "$$$AVATAR_ROOT$$$", "An authored avatar-relative target was rejected.");
                check(operation.TargetOffsetPath == (path == "$$AVATAR" ? "" : path), "Avatar-relative path was lost.");
            }
        });

        test("MA invalid proxy mode and unassigned target remain blocked", () =>
        {
            check(AvatarBuildPlanner.CreatePlan(AvatarPackageIndex.ParseText(ProxyYaml(99, 15, ""))).HasErrors, "Unknown pose mode was silently approximated.");
            check(AvatarBuildPlanner.CreatePlan(AvatarPackageIndex.ParseText(ProxyYaml(0, 55, ""))).HasErrors, "An unassigned proxy target was invented.");
        });
    }

    private static string ProxyYaml(int mode, int bone, string path) =>
        $"--- !u!1 &1\nGameObject:\n  m_Name: Accessory\n--- !u!114 &2\nMonoBehaviour:\n  m_GameObject: {{fileID: 1}}\n  m_Script: {{fileID: 11500000, guid: 42581d8044b64899834d3d515ab3a144, type: 3}}\n  boneReference: {bone}\n  subPath: '{path}'\n  attachmentMode: {mode}\n";

    private static bool Contains(Node child, Node root)
    {
        for (Node? current = child; current != null; current = current.Parent) if (current == root) return true;
        return false;
    }

    private static Matrix4x4 Inverse(Matrix4x4 matrix) => Matrix4x4.Invert(matrix, out var inverse) ? inverse : throw new InvalidOperationException("Singular test transform.");

    private static void ReparentPreservingWorld(Node source, Node target)
    {
        var world = source.World;
        source.Parent?.Children.Remove(source);
        source.Parent = target;
        target.Children.Add(source);
        source.Local = world * Inverse(target.World);
    }

    private static bool Near(Matrix4x4 first, Matrix4x4 second) => new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ }
        .All(point => Vector3.Distance(Vector3.Transform(point, first), Vector3.Transform(point, second)) < 0.002f);

    private static Node Clone(Node source, Node? parent = null)
    {
        var result = new Node(source.Name, parent, source.Local);
        foreach (var child in source.Children) Clone(child, result);
        return result;
    }

    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new Exception("Expected invalid structural input to be rejected.");
    }

    private sealed class Node
    {
        internal string Name;
        internal Node? Parent;
        internal Matrix4x4 Local;
        internal bool Alive = true;
        internal List<Node> Children = new();
        internal Matrix4x4 World => Parent == null ? Local : Local * Parent.World;

        internal Node(string name, Node? parent = null, Matrix4x4? local = null)
        {
            Name = name;
            Parent = parent;
            Local = local ?? Matrix4x4.Identity;
            parent?.Children.Add(this);
        }
    }
}
