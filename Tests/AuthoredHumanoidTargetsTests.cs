using UnityPackageImporter.Models;

internal static class AuthoredHumanoidTargetsTests
{
    private const string AvatarGuid = "4966b80a66479bb41b98ec02a92d74a7";
    private const string ForeignGuid = "11111111111111111111111111111111";
    private const string Prefab = """
        %YAML 1.1
        %TAG !u! tag:unity3d.com,2011:
        --- !u!1 &100
        GameObject:
          m_Name: RenamedAvatar
          m_Component:
          - component: {fileID: 950}
        --- !u!95 &950
        Animator:
          m_GameObject: {fileID: 100}
          m_Avatar: {fileID: 9000000, guid: 4966b80a66479bb41b98ec02a92d74a7, type: 3}
        """;
    private const string Meta = """
        fileFormatVersion: 2
        guid: 4966b80a66479bb41b98ec02a92d74a7
        ModelImporter:
          animationType: 3
          avatarSetup: 1
          humanDescription:
            human:
            - boneName: Head
              humanName: Head
            skeleton:
            - name: OriginalModel(Clone)
              parentName: ''
            - name: Armature
              parentName: OriginalModel(Clone)
            - name: Hips
              parentName: Armature
            - name: Spine
              parentName: Hips
            - name: Chest
              parentName: Spine
            - name: Neck
              parentName: Chest
            - name: Head
              parentName: Neck
        """;

    internal static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        test("Authored target metadata retains explicit avatar provenance and skeleton parents", () =>
        {
            var metadata = ModelMetaParser.Parse(Meta);
            check(metadata.Guid == AvatarGuid && metadata.IsAuthoredHumanoid, "Authored avatar provenance was lost.");
            check(metadata.Skeleton.Count == 7 && metadata.Skeleton[6] == ("Head", "Neck"), "Skeleton hierarchy was lost.");
            check(metadata.HumanBones.Single() == ("Head", "Head"), "Human mapping changed.");
            check(AuthoredHumanoidTargets.ReadAvatarGuid(Prefab, 100, out var guid) && guid == AvatarGuid,
                "The descriptor root's exact Animator reference was not selected.");
            check(!AuthoredHumanoidTargets.ReadAvatarGuid(Prefab, 200, out _), "Another avatar borrowed this Animator.");
        });

        var invalidAnimators = new Dictionary<string, string>
        {
            ["missing avatar"] = Prefab.Replace("fileID: 9000000", "fileID: 0"),
            ["wrong avatar fileID"] = Prefab.Replace("fileID: 9000000", "fileID: 9000001"),
            ["wrong asset type"] = Prefab.Replace("type: 3", "type: 2"),
            ["zero GUID"] = Prefab.Replace(AvatarGuid, new string('0', 32)),
            ["invalid GUID"] = Prefab.Replace(AvatarGuid, new string('x', 32)),
            ["foreign owner"] = Prefab.Replace("m_GameObject: {fileID: 100}", "m_GameObject: {fileID: 100, guid: " + ForeignGuid + "}"),
            ["malformed owner GUID"] = Prefab.Replace("m_GameObject: {fileID: 100}", "m_GameObject: {fileID: 100, guid: []}"),
            ["wrong owner"] = Prefab.Replace("m_GameObject: {fileID: 100}", "m_GameObject: {fileID: 200}"),
            ["foreign component"] = Prefab.Replace("component: {fileID: 950}", "component: {fileID: 950, guid: " + ForeignGuid + "}"),
            ["unlisted component"] = Prefab.Replace("component: {fileID: 950}", "component: {fileID: 951}"),
            ["duplicate Animator"] = Prefab + "\n--- !u!95 &951\nAnimator:\n  m_GameObject: {fileID: 100}\n  m_Avatar: {fileID: 9000000, guid: " + AvatarGuid + ", type: 3}\n",
            ["duplicate document identity"] = Prefab + "\n--- !u!4 &950\nTransform: {}\n"
        };
        foreach (var invalid in invalidAnimators)
        {
            test("Authored target rejects " + invalid.Key, () =>
                check(AuthoredHumanoidTargets.ReadAvatarGuid(invalid.Value, 100, out var guid) && guid == null,
                    "An invalid declared Animator reference became a usable GUID or enabled name fallback."));
        }

        test("Authored target ignores unrelated clothing Animator references", () =>
        {
            string yaml = Prefab + "\n--- !u!95 &951\nAnimator:\n  m_GameObject: {fileID: 200}\n  m_Avatar: {fileID: 9000000, guid: " + ForeignGuid + ", type: 3}\n";
            check(AuthoredHumanoidTargets.ReadAvatarGuid(yaml, 100, out var guid) && guid == AvatarGuid,
                "A clothing Animator replaced or invalidated the owning avatar reference.");
        });

        test("Authored Head resolves exact root-relative hierarchy despite a clothing Head", () =>
        {
            var root = new Node("RenamedAvatar");
            var head = AddMainSkeleton(root);
            var clothing = new Node("Nightframe", root);
            var clothingHead = AddMainSkeleton(clothing);
            var targets = Resolve(root, ModelMetaParser.Parse(Meta));
            check(targets["Head"] == head && targets["Head"] != clothingHead,
                "The authored hierarchy did not disambiguate the two Head objects.");
        });

        test("Authored path never searches descendants to borrow a clothing skeleton", () =>
        {
            var root = new Node("Avatar");
            AddMainSkeleton(new Node("Nightframe", root));
            check(Resolve(root, ModelMetaParser.Parse(Meta))["Head"] == null,
                "A missing authored path borrowed a clothing hierarchy.");
        });

        test("Authored path rejects ambiguous exact siblings even when only one path completes", () =>
        {
            var root = new Node("Avatar");
            AddMainSkeleton(root);
            new Node("Armature", root);
            check(Resolve(root, ModelMetaParser.Parse(Meta))["Head"] == null,
                "An ambiguous ancestor was resolved by searching for the closest complete path.");
        });

        test("Authored path rejects duplicate leaves and differently cased ancestors", () =>
        {
            var root = new Node("Avatar");
            var head = AddMainSkeleton(root);
            new Node("Head", head.Parent);
            check(Resolve(root, ModelMetaParser.Parse(Meta))["Head"] == null, "Duplicate Head siblings were accepted.");
            root = new Node("Avatar");
            AddMainSkeleton(root);
            root.Children.Single().Name = "armature";
            check(Resolve(root, ModelMetaParser.Parse(Meta))["Head"] == null, "A case-insensitive path was invented.");
        });

        var invalidMetadata = new Dictionary<string, Action<ParsedModelMeta>>
        {
            ["foreign model GUID"] = metadata => metadata.Guid = ForeignGuid,
            ["copied avatar"] = metadata => metadata.IsAuthoredHumanoid = false,
            ["missing skeleton"] = metadata => metadata.Skeleton.Clear(),
            ["missing parent"] = metadata => metadata.Skeleton[1] = ("Armature", "Missing"),
            ["duplicate skeleton name"] = metadata => metadata.Skeleton.Add(("Head", "Neck")),
            ["multiple roots"] = metadata => metadata.Skeleton.Add(("OtherRoot", "")),
            ["skeleton cycle"] = metadata => metadata.Skeleton[1] = ("Armature", "Head"),
            ["missing human bone"] = metadata => metadata.HumanBones.Add(("Missing", "LeftHand")),
            ["duplicate human name"] = metadata => metadata.HumanBones.Add(("Neck", "Head")),
            ["duplicate human bone"] = metadata => metadata.HumanBones.Add(("Head", "Neck"))
        };
        foreach (var invalid in invalidMetadata)
        {
            test("Authored skeleton rejects " + invalid.Key, () =>
            {
                var root = new Node("Avatar");
                AddMainSkeleton(root);
                var metadata = ModelMetaParser.Parse(Meta);
                invalid.Value(metadata);
                check(Resolve(root, metadata).Count == 0, "Invalid skeleton provenance or hierarchy produced targets.");
            });
        }

        test("Malformed and fallback metadata cannot authorize authored targets", () =>
        {
            check(!ModelMetaParser.Parse(Meta.Replace("avatarSetup: 1", "avatarSetup: 2")).IsAuthoredHumanoid,
                "A copied external avatar retained authored skeleton authority.");
            var malformed = ModelMetaParser.Parse(Meta.Replace("- name: Head", "- invalid: Head"));
            check(!malformed.IsAuthoredHumanoid, "A malformed skeleton row retained authority.");
            var fallback = ModelMetaParser.Parse(Meta + "\ninvalid: [\n");
            check(!fallback.IsAuthoredHumanoid && fallback.Skeleton.Count == 0, "Line recovery retained partial skeleton authority.");
        });

        test("An unresolved authored target blocks same-named clothing fallback", () =>
        {
            var root = new Node("Avatar");
            var clothing = new Node("Nightframe", root);
            var clothingHead = AddMainSkeleton(clothing);
            var proxy = new Node("HeadProxy", root);
            var targets = Resolve(root, ModelMetaParser.Parse(Meta));
            var index = new ModelBoneIndex<Node>();
            index.Add(root, 10, targets["Head"]);
            index.Add(clothing, 10, clothingHead);
            check(index.TryResolve(10, root, proxy, Contains, _ => true, out var target) && target == null,
                "A null authored binding enabled name or clothing fallback.");
        });
    }

    private static IReadOnlyDictionary<string, Node?> Resolve(Node root, ParsedModelMeta metadata) =>
        AuthoredHumanoidTargets.Resolve(root, AvatarGuid, metadata, node => node.Children, node => node.Name);

    private static Node AddMainSkeleton(Node root)
    {
        var current = root;
        foreach (string name in new[] { "Armature", "Hips", "Spine", "Chest", "Neck", "Head" })
            current = new Node(name, current);
        return current;
    }

    private static bool Contains(Node child, Node root)
    {
        for (Node? current = child; current != null; current = current.Parent)
            if (current == root) return true;
        return false;
    }

    private sealed class Node
    {
        internal string Name;
        internal readonly Node? Parent;
        internal readonly List<Node> Children = new();

        internal Node(string name, Node? parent = null)
        {
            Name = name;
            Parent = parent;
            parent?.Children.Add(this);
        }
    }
}
