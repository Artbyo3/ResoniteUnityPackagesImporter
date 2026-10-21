using UnityPackageImporter.FrooxEngineRepresentation;
using UnityPackageImporter.Models;

internal static class PrefabObjectLookupTests
{
    private const string PrefabGuid = "e9357ac6af6331a4d9538b7fc8745d6d";
    private const string FbxGuid = "55bf97ffa881c0a4f9a09c9f9f6ca3ec";
    private const long InstanceId = 6395293676508719238;

    public static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        Dictionary<SourceObj, object> Map() => new(new SourceObjCompare());
        var lineage = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { PrefabGuid, FbxGuid, "ancestor-a", "ancestor-b" };
        bool Find(SourceObj source, Dictionary<SourceObj, object> hashes,
            Dictionary<SourceObj, object> aliases, out object target, out bool ambiguous) =>
            PrefabObjectLookup.TryFind(source, hashes, aliases, PrefabGuid, FbxGuid, lineage,
                out target, out ambiguous);

        void RegisterAlias(Dictionary<SourceObj, object> aliases, long sourceId, object target)
        {
            // Same registration and shared formula as PrefabInstance's override replay.
            long derivedId = PrefabVariantResolver.ComputeDerivedFileId(InstanceId, sourceId);
            PrefabVariantResolver.RegisterUniqueTarget(aliases, new SourceObj(derivedId, PrefabGuid, 3), target);
        }

        test("Stripped Transform resolves Shinano nested sofa identity", () =>
        {
            var root = new object();
            var hashes = Map();
            hashes.Add(new SourceObj(-8679921383154817045, FbxGuid, 3), root);
            var aliases = Map();
            RegisterAlias(aliases, -8679921383154817045, root);
            check(PrefabVariantResolver.ComputeDerivedFileId(InstanceId, -8679921383154817045) == 6866332237670371181,
                "The fixture must reproduce the actual logged nested Transform target.");
            check(Find(new SourceObj(6866332237670371181, PrefabGuid, 3), hashes, aliases,
                out var actual, out bool ambiguous) && ReferenceEquals(root, actual) && !ambiguous,
                "The stripped Transform must resolve to the nested sofa root, not an unrelated object.");
        });

        test("Stripped GameObject resolves nested sofa derived root identity", () =>
        {
            var root = new object();
            var hashes = Map();
            hashes.Add(new SourceObj(919132149155446097, FbxGuid, 3), root);
            var aliases = Map();
            RegisterAlias(aliases, 919132149155446097, root);
            check(Find(new SourceObj(6053344116557190615, PrefabGuid, 3), hashes, aliases,
                out var actual, out _) && ReferenceEquals(root, actual),
                "GameObject must use the same nested identity aliases as Transform.");
        });

        test("Stripped SkinnedMeshRenderer resolves through the shared derived identity lookup", () =>
        {
            const long skinId = -7511558181221131132;
            var skin = new object();
            var hashes = Map();
            hashes.Add(new SourceObj(skinId, FbxGuid, 3), skin);
            var aliases = Map();
            RegisterAlias(aliases, skinId, skin);
            long derivedId = PrefabVariantResolver.ComputeDerivedFileId(InstanceId, skinId);
            check(Find(new SourceObj(derivedId, PrefabGuid, 3), hashes, aliases, out var actual, out _) &&
                ReferenceEquals(skin, actual), "All stripped-object callers must share the lookup.");
        });

        test("Unresolved stripped Transform retains the original hierarchy failure message", () =>
        {
            var source = new SourceObj(6866332237670371181, PrefabGuid, 3);
            var hashes = Map();
            hashes.Add(new SourceObj(123, FbxGuid, 3), new object());
            try
            {
                if (!Find(source, hashes, Map(), out _, out bool ambiguous))
                {
                    check(!ambiguous, "A genuinely absent reference must remain absent.");
                    throw PrefabObjectLookup.UnresolvedTransform(8773007775650283060, source);
                }
                throw new Exception("A missing reference must not become a successful import.");
            }
            catch (InvalidDataException error)
            {
                check(error.Message == "Transform 8773007775650283060 could not resolve prefab target 6866332237670371181 in e9357ac6af6331a4d9538b7fc8745d6d. Hierarchy integrity cannot be guaranteed.",
                    "The actual Transform failure factory must retain the original message exactly.");
            }
        });

        test("Ordinary exact prefab references retain precedence over derived aliases", () =>
        {
            var ordinary = new object();
            var hashes = Map();
            hashes.Add(new SourceObj(42, PrefabGuid, 3), ordinary);
            var aliases = Map();
            aliases.Add(new SourceObj(42, PrefabGuid, 3), new object());
            check(Find(new SourceObj(42, PrefabGuid.ToUpperInvariant(), 1), hashes, aliases,
                out var actual, out _) && ReferenceEquals(ordinary, actual),
                "Preserve SourceObjCompare's case-insensitive GUID matching and existing type behavior.");
        });

        test("Ordinary FBX and empty-GUID fallback references behave as before", () =>
        {
            foreach (string guid in new[] { FbxGuid, "" })
            {
                var expected = new object();
                var hashes = Map();
                hashes.Add(new SourceObj(42, guid, 3), expected);
                check(Find(new SourceObj(42, PrefabGuid, 3), hashes, Map(), out var actual, out _) &&
                    ReferenceEquals(expected, actual), "Keep ordinary GUID fallback behavior.");
            }
        });

        test("Derived prefab aliases do not authorize foreign GUIDs", () =>
        {
            var aliases = Map();
            RegisterAlias(aliases, -8679921383154817045, new object());
            check(!Find(new SourceObj(6866332237670371181, "foreign-prefab", 3), Map(), aliases,
                out var actual, out _) && actual == null, "An equal ID is not proof of source identity.");
        });

        test("Conflicting derived aliases remain unresolved", () =>
        {
            var aliases = Map();
            RegisterAlias(aliases, -8679921383154817045, new object());
            RegisterAlias(aliases, -8679921383154817045, new object());
            check(!Find(new SourceObj(6866332237670371181, PrefabGuid, 3), Map(), aliases,
                out var actual, out bool ambiguous) && ambiguous && actual == null,
                "A retained conflict marker must not be published as a resolved object.");
        });

        test("Ambiguous ordinary prefab references cannot fall through to an alias", () =>
        {
            var hashes = Map();
            hashes.Add(new SourceObj(42, "ancestor-a", 3), new object());
            hashes.Add(new SourceObj(42, "ancestor-b", 3), new object());
            var aliases = Map();
            aliases.Add(new SourceObj(42, PrefabGuid, 3), new object());
            check(!Find(new SourceObj(42, PrefabGuid, 3), hashes, aliases, out var actual, out bool ambiguous) &&
                ambiguous && actual == null, "An alias must not hide an existing ambiguity.");
        });

        test("Repeated ancestor aliases preserve one object identity", () =>
        {
            var expected = new object();
            var aliases = Map();
            aliases.Add(new SourceObj(42, "ancestor-a", 3), expected);
            aliases.Add(new SourceObj(42, "ancestor-b", 3), expected);
            check(Find(new SourceObj(42, PrefabGuid, 3), Map(), aliases, out var actual, out bool ambiguous) &&
                !ambiguous && ReferenceEquals(expected, actual), "Repeated aliases are not distinct objects.");
        });
    }
}
