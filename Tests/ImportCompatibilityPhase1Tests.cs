using System;
using System.Collections.Generic;
using System.IO;
using UnityPackageImporter.Models;
using YamlDotNet.Serialization;

internal static class ImportCompatibilityPhase1Tests
{
    public static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        IDeserializer Deserializer() => new DeserializerBuilder()
            .WithTagMapping("tag:unity3d.com,2011:1", typeof(Document))
            .IgnoreUnmatchedProperties()
            .Build();

        test("Unity YAML parser correctly parses negative 64-bit document anchors", () =>
        {
            long signedId = -5281850123512345L;
            ulong expectedUnsigned = unchecked((ulong)signedId);
            string yaml = $"""
                %YAML 1.1
                %TAG !u! tag:unity3d.com,2011:
                --- !u!1 &{signedId}
                Object:
                  Name: NegativeAnchorObject
                  Reference: 0
                  Value: 42
                """;
            var documents = UnityYamlDocumentParser.Parse<Document>(yaml, Deserializer());
            check(documents.Count == 1, "Must parse exactly one document.");
            check(documents.ContainsKey(expectedUnsigned), "Must index document under unsigned representation of negative anchor.");
            check(documents[expectedUnsigned].Object.Name == "NegativeAnchorObject", "Object content must be preserved.");
            check(documents[expectedUnsigned].Object.Value == 42, "Object values must be preserved.");
        });

        test("PrefabVariantResolver resolves multiline block-style m_SourcePrefab guid", () =>
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "ResolverTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string prefabPath = Path.Combine(tempDir, "Variant.prefab");
                string yamlContent = """
                    %YAML 1.1
                    %TAG !u! tag:unity3d.com,2011:
                    --- !u!1001 &100100000
                    PrefabInstance:
                      m_ObjectHideFlags: 0
                      serializedVersion: 2
                      m_Modification:
                        m_TransformParent: {fileID: 0}
                      m_SourcePrefab:
                        fileID: 100100000
                        guid: 89abcdef0123456789abcdef01234567
                        type: 3
                    """;
                File.WriteAllText(prefabPath, yamlContent);

                string foundGuid = PrefabVariantResolver.FindSourceGuidInPrefabFile(prefabPath);
                check(foundGuid == "89abcdef0123456789abcdef01234567", $"Expected resolved GUID 89abcdef0123456789abcdef01234567, got '{foundGuid}'");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        });

        test("PrefabVariantResolver resolves multiline block-style m_Mesh guid", () =>
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "MeshResolverTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string prefabPath = Path.Combine(tempDir, "Model.prefab");
                string yamlContent = """
                    %YAML 1.1
                    %TAG !u! tag:unity3d.com,2011:
                    --- !u!137 &13700000
                    SkinnedMeshRenderer:
                      m_Mesh:
                        fileID: 4300000
                        guid: fedcba9876543210fedcba9876543210
                        type: 3
                    """;
                File.WriteAllText(prefabPath, yamlContent);

                string foundGuid = PrefabVariantResolver.FindSourceGuidInPrefabFile(prefabPath);
                check(foundGuid == "fedcba9876543210fedcba9876543210", $"Expected resolved mesh GUID fedcba9876543210fedcba9876543210, got '{foundGuid}'");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        });

        test("AvatarPackageIndex inherits avatar descriptor for variants containing Modular Avatar components", () =>
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "AvatarIndexTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string baseGuid = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
                string basePath = Path.Combine(tempDir, "BaseAvatar.prefab");
                string variantPath = Path.Combine(tempDir, "VariantWithMA.prefab");

                // Base prefab contains VrcAvatarDescriptor
                string baseYaml = $$"""
                    --- !u!1 &100000
                    GameObject:
                      m_Name: BaseRoot
                    --- !u!114 &100001
                    MonoBehaviour:
                      m_GameObject: {fileID: 100000}
                      m_Script: {fileID: 542108242, guid: {{AvatarPackageIndex.VrcAvatarDescriptorScriptGuid}}, type: 3}
                      ViewPosition: {x: 0, y: 1.5, z: 0.1}
                      expressionsMenu: {fileID: 11400000, guid: eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee, type: 2}
                      expressionParameters: {fileID: 11400000, guid: ffffffffffffffffffffffffffffffff, type: 2}
                    """;
                File.WriteAllText(basePath, baseYaml);

                // Variant prefab points to BaseAvatar and adds a Modular Avatar MenuInstaller
                string variantYaml = $$"""
                    --- !u!1001 &200000
                    PrefabInstance:
                      m_SourcePrefab:
                        fileID: 100100000
                        guid: {{baseGuid}}
                        type: 3
                    --- !u!1 &200001
                    GameObject:
                      m_Name: MA_Menu
                    --- !u!114 &200002
                    MonoBehaviour:
                      m_GameObject: {fileID: 200001}
                      m_Script: {fileID: 11500000, guid: {{AvatarPackageIndex.MenuInstallerScriptGuid}}, type: 3}
                    """;
                File.WriteAllText(variantPath, variantYaml);

                var prefabs = new Dictionary<string, string>
                {
                    [baseGuid] = basePath
                };

                var manifest = AvatarPackageIndex.ParseFile(variantPath, prefabs);
                check(manifest.Avatars.Count == 1, $"Variant must inherit ancestor avatar descriptor (expected 1, got {manifest.Avatars.Count})");
                check(manifest.ModularAvatarComponents.Count == 1, $"Variant must retain its own MA component (expected 1, got {manifest.ModularAvatarComponents.Count})");
                check(manifest.IsAvatarPrefab, "Variant with inherited avatar descriptor must be classified as avatar prefab");
                check(!manifest.IsModularAvatarAttachment, "Avatar variant must not be misclassified as Modular Avatar attachment");
                check(manifest.ShouldSetUpHumanoid, "Avatar variant must set up humanoid rig");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        });

        test("AvatarPackageIndex detects VRCFury components and indexes them into manifest", () =>
        {
            string yaml = $$"""
                --- !u!1 &100000
                GameObject:
                  m_Name: FuryHolder
                --- !u!114 &100001
                MonoBehaviour:
                  m_GameObject: {fileID: 100000}
                  m_Script: {fileID: 11500000, guid: {{AvatarPackageIndex.VrcFuryScriptGuid}}, type: 3}
                  content: "ToggleClothes"
                --- !u!114 &100002
                MonoBehaviour:
                  m_GameObject: {fileID: 100000}
                  m_Script: {fileID: 11500000, guid: 11111111111111111111111111111111, type: 3}
                  vrcfury:
                    features:
                      - name: TestFeature
                """;
            var manifest = AvatarPackageIndex.ParseText(yaml, "TestFury.prefab");
            check(manifest.HasVrcFuryComponents, "Manifest must detect VRCFury components");
            check(manifest.VrcFuryComponents.Count == 2, $"Expected 2 VRCFury components, got {manifest.VrcFuryComponents.Count}");
            check(manifest.VrcFuryComponents[0].GameObjectName == "FuryHolder", "Component must identify parent GameObject name");
        });

        test("AvatarPackageIndex safely tolerates duplicate GameObject document IDs without throwing", () =>
        {
            string yaml = """
                --- !u!1 &100000
                GameObject:
                  m_Name: First
                --- !u!1 &100000
                GameObject:
                  m_Name: Duplicate
                """;
            var manifest = AvatarPackageIndex.ParseText(yaml, "Duplicate.prefab");
            check(manifest != null, "Manifest must parse without throwing on duplicate GameObject IDs.");
        });

        test("PrefabVariantResolver TryResolveTargetKey validates lineage and rejects foreign GUIDs", () =>
        {
            string fbxGuid = "fbx00000000000000000000000000001";
            var lineage = new HashSet<string> { "variant00000000000000000000000002", "base00000000000000000000000000003" };

            // 1. Foreign GUID not in lineage must be rejected
            bool foreignResolved = PrefabVariantResolver.TryResolveTargetKey(
                100, "foreign00000000000000000000000099", fbxGuid, lineage, out string foreignOut);
            check(!foreignResolved, "Foreign GUID not in lineage must be rejected");
            check(foreignOut == null, "Resolved GUID must be null for foreign GUID");

            // 2. Exact resolved FBX GUID matches
            bool fbxResolved = PrefabVariantResolver.TryResolveTargetKey(
                100, fbxGuid, fbxGuid, lineage, out string fbxOut);
            check(fbxResolved && fbxOut == fbxGuid, "Resolved FBX GUID must be accepted");

            // 3. Empty GUID matches resolved FBX GUID
            bool emptyResolved = PrefabVariantResolver.TryResolveTargetKey(
                100, "", fbxGuid, lineage, out string emptyOut);
            check(emptyResolved && emptyOut == fbxGuid, "Empty GUID must be resolved to FBX GUID");

            // 4. Lineage variant GUID matches
            bool lineageResolved = PrefabVariantResolver.TryResolveTargetKey(
                100, "variant00000000000000000000000002", fbxGuid, lineage, out string lineageOut);
            check(lineageResolved && lineageOut == fbxGuid, "Lineage variant GUID must be resolved to FBX GUID");
        });

        test("PrefabVariantResolver TryResolveDerivedTarget detects ambiguity and refuses arbitrary selection", () =>
        {
            long targetFileId = 123456789;
            long piId = 100100000;
            var (cand1, cand2) = PrefabVariantResolver.ReverseDerivedFileId(piId, targetFileId);

            var objectA = "TargetA";
            var objectB = "TargetB";

            // If candidates return multiple distinct objects, ambiguity must be detected and result in failure
            IEnumerable<string> AmbiguousFinder(long id)
            {
                if (id == cand1) yield return objectA;
                if (id == cand2) yield return objectB;
            }

            bool resolved = PrefabVariantResolver.TryResolveDerivedTarget(
                targetFileId,
                new[] { piId },
                AmbiguousFinder,
                out string resultObj,
                out bool ambiguous);

            check(!resolved, "TryResolveDerivedTarget must fail when multiple conflicting candidates match");
            check(ambiguous, "Ambiguity flag must be set to true");
            check(resultObj == null, "Result object must be null on ambiguous match");

            // If only one unique candidate matches, it resolves cleanly
            IEnumerable<string> UniqueFinder(long id)
            {
                if (id == cand1) yield return objectA;
            }

            bool uniqueResolved = PrefabVariantResolver.TryResolveDerivedTarget(
                targetFileId,
                new[] { piId },
                UniqueFinder,
                out string uniqueResult,
                out bool uniqueAmbiguous);

            check(uniqueResolved, "Unique candidate must resolve successfully");
            check(!uniqueAmbiguous, "Ambiguity flag must be false for unique candidate");
            check(uniqueResult == objectA, "Result must match unique candidate");
        });

        test("PrefabVariantResolver RegisterUniqueTarget detects collisions and sets null", () =>
        {
            var map = new Dictionary<long, string>();
            PrefabVariantResolver.RegisterUniqueTarget(map, 100L, "FirstInstance");
            check(map[100L] == "FirstInstance", "Initial target registration must succeed");

            // Duplicate same reference remains
            PrefabVariantResolver.RegisterUniqueTarget(map, 100L, "FirstInstance");
            check(map[100L] == "FirstInstance", "Duplicate identical reference must remain");

            // Conflicting reference sets entry to null to mark ambiguity
            PrefabVariantResolver.RegisterUniqueTarget(map, 100L, "ConflictingInstance");
            check(map[100L] == null, "Conflicting reference must set entry to null to detect collision");
        });

        IDeserializer PrefabDeserializer() => new DeserializerBuilder()
            .WithTagMapping("tag:unity3d.com,2011:1001", typeof(PrefabInstanceDoc))
            .IgnoreUnmatchedProperties()
            .Build();

        test("Missing optional renderers produce partial compatibility status and preserve avatar base", () =>
        {
            // Scenario: Avatar with 2 authored skinned mesh renderers (Body and Glasses accessory).
            // Glasses fails to reconstruct (e.g. missing mesh asset), while Body succeeds.
            int authoredSkins = 2;
            int successfulSkins = 1;
            string prefabName = "AvatarWithGlasses.prefab";

            bool reconstructionSurvived = PrefabVariantResolver.EvaluateSkinnedMeshReconstruction(
                authoredSkins, successfulSkins, prefabName, out string failureReason);
            check(reconstructionSurvived, "Reconstruction must survive when base avatar skin succeeds");
            check(failureReason == null, "Failure reason must be null for non-critical partial reconstruction");

            // Incomplete component is recorded for the failed optional accessory
            var incompleteComponents = new List<string> { "SkinnedMeshRenderer (13700002)" };
            bool isPartial = PrefabVariantResolver.EvaluatePartialCompatibility(incompleteComponents.Count, hasVrcFury: false);
            check(isPartial, "Import must report partial compatibility when optional renderer is incomplete");

            // When all renderers succeed, partial compatibility must be false (full success)
            bool fullSuccessPartial = PrefabVariantResolver.EvaluatePartialCompatibility(0, hasVrcFury: false);
            check(!fullSuccessPartial, "Full reconstruction without incomplete components must not flag partial compatibility");
        });

        test("Failure to reconstruct all required skinned renderers cannot report full success", () =>
        {
            // Scenario: Avatar where ALL authored skinned mesh renderers fail reconstruction.
            int authoredSkins = 2;
            int successfulSkins = 0;
            string prefabName = "BrokenAvatar.prefab";

            bool reconstructionSurvived = PrefabVariantResolver.EvaluateSkinnedMeshReconstruction(
                authoredSkins, successfulSkins, prefabName, out string failureReason);
            check(!reconstructionSurvived, "Reconstruction must fail when all skinned meshes fail");
            check(failureReason != null && failureReason.Contains("Critical reconstruction failure"),
                "Critical failure must produce explanatory error preventing unqualified success");
            check(failureReason.Contains("None of the 2 skinned mesh renderer(s) could be reconstructed"),
                "Failure reason must state the exact count of failed skins");
        });

        test("Prefab reference resolution preserves legitimate multi-level variant inheritance", () =>
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "VariantInheritanceTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string baseGuid = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
                string variantAGuid = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
                string childVariantGuid = "cccccccccccccccccccccccccccccccc";
                string fbxGuid = "dddddddddddddddddddddddddddddddd";

                string basePath = Path.Combine(tempDir, "Base.prefab");
                string variantAPath = Path.Combine(tempDir, "VariantA.prefab");
                string childVariantPath = Path.Combine(tempDir, "ChildVariant.prefab");

                // Base prefab referencing FBX
                File.WriteAllText(basePath, $"""
                    --- !u!1001 &100100000
                    PrefabInstance:
                      m_SourcePrefab:
                        fileID: 100100000
                        guid: {fbxGuid}
                        type: 3
                    """);

                // Variant A referencing Base
                File.WriteAllText(variantAPath, $"""
                    --- !u!1001 &200100000
                    PrefabInstance:
                      m_SourcePrefab:
                        fileID: 100100000
                        guid: {baseGuid}
                        type: 3
                    """);

                // Child Variant referencing Variant A
                File.WriteAllText(childVariantPath, $"""
                    --- !u!1001 &300100000
                    PrefabInstance:
                      m_SourcePrefab:
                        fileID: 200100000
                        guid: {variantAGuid}
                        type: 3
                    """);

                var prefabs = new Dictionary<string, string>
                {
                    [baseGuid] = basePath,
                    [variantAGuid] = variantAPath,
                    [childVariantGuid] = childVariantPath
                };

                // Traverse the full lineage chain
                var lineage = PrefabVariantResolver.GetLineageGuids(variantAGuid, fbxGuid, prefabs);
                check(lineage.Contains(variantAGuid), "Lineage must contain parent variant GUID");
                check(lineage.Contains(baseGuid), "Lineage must contain root ancestor GUID");
                check(lineage.Contains(fbxGuid), "Lineage must contain resolved FBX GUID");

                // Validate target key resolution across the multi-level hierarchy
                bool resolved = PrefabVariantResolver.TryResolveTargetKey(
                    13700000, baseGuid, fbxGuid, lineage, out string targetGuid);
                check(resolved && targetGuid == fbxGuid, "Legitimate ancestor target key must resolve to underlying FBX");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        });

        test("Hierarchy integrity: Valid root objects deserialize and import without parent", () =>
        {
            string yaml = """
                %YAML 1.1
                %TAG !u! tag:unity3d.com,2011:
                --- !u!1001 &100100000
                PrefabInstance:
                  m_Modification:
                    m_TransformParent: {fileID: 0}
                  m_SourcePrefab:
                    fileID: 100100000
                    guid: 1234567890abcdef1234567890abcdef
                    type: 3
                """;
            var docs = UnityYamlDocumentParser.Parse<PrefabInstanceDoc>(yaml, PrefabDeserializer());
            check(docs.ContainsKey(100100000), "Must deserialize PrefabInstance document");
            var mod = docs[100100000].PrefabInstance.m_Modification;
            check(mod.m_TransformParent != null, "m_TransformParent must be present in parsed document");
            check(mod.m_TransformParent.TryGetValue("fileID", out ulong parentTransformId) && parentTransformId == 0,
                "Serialized fileID must be 0 for root object");

            // Resolution must validate fileID 0 as a legitimate root placement
            bool resolved = PrefabVariantResolver.TryResolveParentTransform(
                parentTransformId,
                id => (exists: false, gameObjectId: 0),
                id => (exists: false, hasValidSlot: false),
                out string failureReason);
            check(resolved, "Valid root object with fileID 0 must resolve successfully");
            check(failureReason == null, "Root object resolution must not produce a failure reason");
        });

        test("Hierarchy integrity: Valid nested objects resolve to their parent and retain hierarchy", () =>
        {
            ulong parentTrId = 4000000000000001;
            ulong parentGoId = 1000000000000001;
            string yaml = $$"""
                %YAML 1.1
                %TAG !u! tag:unity3d.com,2011:
                --- !u!1001 &200100000
                PrefabInstance:
                  m_Modification:
                    m_TransformParent: {fileID: {{parentTrId}}}
                  m_SourcePrefab:
                    fileID: 100100000
                    guid: abcdef1234567890abcdef1234567890
                    type: 3
                """;
            var docs = UnityYamlDocumentParser.Parse<PrefabInstanceDoc>(yaml, PrefabDeserializer());
            var mod = docs[200100000].PrefabInstance.m_Modification;
            check(mod.m_TransformParent.TryGetValue("fileID", out ulong parsedParentId) && parsedParentId == parentTrId,
                "Parsed nested parent ID must match authored ID");

            // Parent Transform exists, GameObject exists, slot is valid
            bool resolved = PrefabVariantResolver.TryResolveParentTransform(
                parsedParentId,
                id => id == parentTrId ? (exists: true, gameObjectId: parentGoId) : (exists: false, gameObjectId: 0),
                id => id == parentGoId ? (exists: true, hasValidSlot: true) : (exists: false, hasValidSlot: false),
                out string failureReason);
            check(resolved, "Valid nested object must resolve to its parent");
            check(failureReason == null, "Valid nested parent resolution must not produce error");
        });

        test("Hierarchy integrity: Missing referenced parents produce structural failure and refuse root fallback", () =>
        {
            ulong missingParentTrId = 9999999999999999;
            string yaml = $$"""
                %YAML 1.1
                %TAG !u! tag:unity3d.com,2011:
                --- !u!1001 &300100000
                PrefabInstance:
                  m_Modification:
                    m_TransformParent: {fileID: {{missingParentTrId}}}
                  m_SourcePrefab:
                    fileID: 100100000
                    guid: fedcba0987654321fedcba0987654321
                    type: 3
                """;
            var docs = UnityYamlDocumentParser.Parse<PrefabInstanceDoc>(yaml, PrefabDeserializer());
            var mod = docs[300100000].PrefabInstance.m_Modification;
            mod.m_TransformParent.TryGetValue("fileID", out ulong parsedParentId);

            // Parent transform does NOT exist in existing objects
            bool resolved = PrefabVariantResolver.TryResolveParentTransform(
                parsedParentId,
                id => (exists: false, gameObjectId: 0),
                id => (exists: false, hasValidSlot: false),
                out string failureReason);
            check(!resolved, "Missing referenced parent must fail structural resolution");
            check(failureReason != null && failureReason.Contains(missingParentTrId.ToString()),
                "Failure reason must identify the missing parent transform ID");
            check(failureReason.Contains("parent transform does not exist"),
                "Failure reason must state that parent transform does not exist");
        });

        test("Hierarchy integrity: Invalid parent references (missing GameObject or null slot) produce structural failure", () =>
        {
            ulong parentTrId = 4000000000000002;
            ulong missingGoId = 1000000000000002;
            ulong validGoId = 1000000000000003;

            // Case A: Parent Transform exists, but its referenced GameObject is missing
            bool caseAResolved = PrefabVariantResolver.TryResolveParentTransform(
                parentTrId,
                id => (exists: true, gameObjectId: missingGoId),
                id => (exists: false, hasValidSlot: false),
                out string caseAFailure);
            check(!caseAResolved, "Missing parent GameObject must fail structural resolution");
            check(caseAFailure != null && caseAFailure.Contains("GameObject does not exist"),
                "Failure reason must state that referenced GameObject does not exist");

            // Case B: Parent Transform and GameObject exist, but GameObject slot is null
            bool caseBResolved = PrefabVariantResolver.TryResolveParentTransform(
                parentTrId,
                id => (exists: true, gameObjectId: validGoId),
                id => (exists: true, hasValidSlot: false),
                out string caseBFailure);
            check(!caseBResolved, "Null parent slot must fail structural resolution");
            check(caseBFailure != null && caseBFailure.Contains("has null slot"),
                "Failure reason must state that parent GameObject has null slot");
        });

        test("Hierarchy integrity: Parent transforms with direct slots (stripped FBX bones) resolve parent hierarchy", () =>
        {
            ulong strippedParentTrId = 7832099146100378785;
            // The stripped parent transform has a direct slot from the imported FBX (hasDirectSlot: true)
            // even if its GameObject was not serialized as an independent scene object.
            bool resolved = PrefabVariantResolver.TryResolveParentTransform(
                strippedParentTrId,
                id => id == strippedParentTrId ? (exists: true, gameObjectId: 0UL, hasDirectSlot: true) : (exists: false, gameObjectId: 0UL, hasDirectSlot: false),
                id => (exists: false, hasValidSlot: false),
                out string failureReason);
            check(resolved, "Parent transform with direct slot (e.g. FBX bone) must resolve successfully");
            check(failureReason == null, "Parent transform with direct slot must not fail");
        });

        test("Prefab reference resolution: Root transform fileID -8679921383154817045 and root GameObject 919132149155446097 resolve to model root", () =>
        {
            long rootGoId = PrefabVariantResolver.RootGameObjectFileId;
            long rootTrId = PrefabVariantResolver.RootTransformFileId;

            check(rootGoId == 919132149155446097L, "Root GameObject fileID must match Unity standard 919132149155446097");
            check(rootTrId == -8679921383154817045L, "Root Transform fileID must match Unity standard -8679921383154817045");
            check(PrefabVariantResolver.IsRootModelFileId(rootGoId), "Root GameObject must be recognized as root model file ID");
            check(PrefabVariantResolver.IsRootModelFileId(rootTrId), "Root Transform must be recognized as root model file ID");
            check(!PrefabVariantResolver.IsRootModelFileId(123456789L), "Arbitrary file ID must not be recognized as root");
        });

        test("Scene avatar packages wait for station selection even without prefab meshes", () =>
        {
            check(!AvatarImportSelection.AutoCommitScenes(hasAvatarDefinitions: true, hasAvatarOrOutfitItems: false),
                "A scene avatar must not publish its entire scene when it has no prefab mesh entry.");
            check(!AvatarImportSelection.AutoCommitScenes(hasAvatarDefinitions: false, hasAvatarOrOutfitItems: true),
                "A model or outfit package must not publish demonstration scenes automatically.");
            check(AvatarImportSelection.AutoCommitScenes(hasAvatarDefinitions: false, hasAvatarOrOutfitItems: false),
                "Ordinary environment packages retain scene publication.");
        });

        test("VRCFury upstream GUID d9e94e501a2d4c95bff3d5601013d923 and RefIds ArmatureLink decoded correctly", () =>
        {
            string yaml = $$"""
                --- !u!1 &100000
                GameObject:
                  m_Name: CostumeRoot
                --- !u!114 &100001
                MonoBehaviour:
                  m_GameObject: {fileID: 100000}
                  m_Script: {fileID: 11500000, guid: {{AvatarPackageIndex.VrcFuryFullScriptGuid}}, type: 3}
                  references:
                    version: 2
                    RefIds:
                    - rid: 101
                      type: {class: ArmatureLink, ns: VF.Model.Feature, asm: VRCFury}
                      data:
                        version: 7
                        propBone: {fileID: 200001}
                        linkTo:
                        - useBone: 1
                          bone: 0
                          useObj: 0
                          obj: {fileID: 0}
                          offset: ""
                        linkMode: 0
                        removeBoneSuffix: "_outfit"
                        alignPosition: 1
                        alignRotation: 1
                        alignScale: 0
                        autoScaleFactor: 1
                        recursive: 1
                    - rid: 102
                      type: {class: BoundingBoxFix, ns: VF.Model.Feature, asm: VRCFury}
                      data: {}
                """;

            var manifest = AvatarPackageIndex.ParseText(yaml, "TestCostume.prefab");
            check(manifest.HasVrcFuryComponents, "Manifest must detect VRCFury component by upstream GUID");
            check(manifest.VrcFuryComponents.Count == 1, $"Expected 1 VRCFury component, got {manifest.VrcFuryComponents.Count}");
            check(manifest.AllVrcFuryArmatureLinks.Count == 1, $"Expected 1 ArmatureLink, got {manifest.AllVrcFuryArmatureLinks.Count}");

            var link = manifest.AllVrcFuryArmatureLinks[0];
            check(link.PropBoneFileId == 200001, $"Expected PropBoneFileId 200001, got {link.PropBoneFileId}");
            check(link.Recursive, "Expected Recursive == true");
            check(link.RemoveBoneSuffix == "_outfit", $"Expected suffix '_outfit', got '{link.RemoveBoneSuffix}'");
            check(link.AlignPosition, "Expected AlignPosition == true");
            check(link.AlignRotation, "Expected AlignRotation == true");
            check(!link.AlignScale, "Expected AlignScale == false");
            check(link.LinkToTargets.Count == 1 && link.LinkToTargets[0].Bone == 0, "Expected linkTo bone 0 (Hips)");

            check(manifest.UnsupportedVrcFuryFeatureCount == 1, $"Expected 1 unsupported feature, got {manifest.UnsupportedVrcFuryFeatureCount}");
            check(manifest.VrcFuryComponents[0].UnsupportedFeatures[0] == "BoundingBoxFix", "Expected BoundingBoxFix recorded as unsupported feature");
        });

        test("Common AvatarBuildPlan generates unified operations from mixed Modular Avatar and VRCFury", () =>
        {
            var manifest = new AvatarPackageManifest
            {
                SourcePath = "MixedAvatar.prefab",
                ModularAvatarComponents = new List<ModularAvatarComponentDefinition>
                {
                    new()
                    {
                        Kind = ModularAvatarComponentKind.MergeArmature,
                        ArmatureRootFileId = 1001,
                        GameObjectName = "MA_Jacket_Armature",
                        MergeTargetPath = "$$$AVATAR_ROOT$$$",
                        Prefix = "",
                        Suffix = "_Jacket"
                    },
                    new()
                    {
                        Kind = ModularAvatarComponentKind.BoneProxy,
                        GameObjectFileId = 1002,
                        GameObjectName = "MA_Glasses_Proxy",
                        BoneReference = 10, // Head
                        BoneProxyMatchScale = true
                    }
                },
                VrcFuryComponents = new List<VrcFuryComponentDefinition>
                {
                    new()
                    {
                        GameObjectName = "VF_Costume",
                        ArmatureLinks = new List<VrcFuryArmatureLinkDefinition>
                        {
                            new()
                            {
                                PropBoneFileId = 2001,
                                GameObjectName = "VF_Skirt_Armature",
                                Recursive = true,
                                RemoveBoneSuffix = "_Skirt",
                                LinkToTargets = new List<VrcFuryLinkToTarget>
                                {
                                    new() { UseBone = true, Bone = 0 } // Hips
                                }
                            },
                            new()
                            {
                                PropBoneFileId = 2002,
                                GameObjectName = "VF_Watch_Prop",
                                Recursive = false,
                                AlignPosition = true,
                                AlignRotation = true,
                                LinkToTargets = new List<VrcFuryLinkToTarget>
                                {
                                    new() { UseBone = true, Bone = 18 } // RightHand
                                }
                            }
                        }
                    }
                }
            };

            var plan = AvatarBuildPlanner.CreatePlan(manifest, "MixedAvatar");
            check(!plan.HasErrors, "Mixed valid plan must have no errors");
            check(plan.Operations.Count == 4, $"Expected 4 operations, got {plan.Operations.Count}");

            var mergeOps = plan.Operations.OfType<ArmatureMergeOperation>().ToList();
            var attachOps = plan.Operations.OfType<BoneAttachmentOperation>().ToList();

            check(mergeOps.Count == 2, $"Expected 2 ArmatureMerge operations, got {mergeOps.Count}");
            check(attachOps.Count == 2, $"Expected 2 BoneAttachment operations, got {attachOps.Count}");

            check(mergeOps.Any(m => m.Source.Framework == "ModularAvatar" && m.SourceArmatureRootName == "MA_Jacket_Armature"),
                "Modular Avatar merge armature operation must be present");
            check(mergeOps.Any(m => m.Source.Framework == "VRCFury" && m.SourceArmatureRootName == "VF_Skirt_Armature"),
                "VRCFury recursive merge armature operation must be present");

            check(attachOps.Any(a => a.Source.Framework == "ModularAvatar" && a.TargetHumanoidBone == 10),
                "Modular Avatar BoneProxy operation targeting Head must be present");
            check(attachOps.Any(a => a.Source.Framework == "VRCFury" && a.TargetHumanoidBone == 18),
                "VRCFury prop link operation targeting RightHand must be present");
        });

        test("Headless merge planning maps bones and identifies retained roots", () =>
        {
            var avatarRoot = new MergeHierarchyNode
            {
                Id = "1",
                Name = "Hips",
                Children = new List<MergeHierarchyNode>
                {
                    new()
                    {
                        Id = "2",
                        Name = "Spine",
                        Children = new List<MergeHierarchyNode>
                        {
                            new() { Id = "3", Name = "Chest" }
                        }
                    }
                }
            };

            var outfitRoot = new MergeHierarchyNode
            {
                Id = "101",
                Name = "Hips_outfit",
                Children = new List<MergeHierarchyNode>
                {
                    new()
                    {
                        Id = "102",
                        Name = "Spine_outfit",
                        Children = new List<MergeHierarchyNode>
                        {
                            new()
                            {
                                Id = "103",
                                Name = "Chest_outfit",
                                Children = new List<MergeHierarchyNode>
                                {
                                    new() { Id = "104", Name = "Skirt_Special_Bone" } // Unique bone not on base avatar
                                }
                            }
                        }
                    }
                }
            };

            var mergeOp = new ArmatureMergeOperation
            {
                Suffix = "_outfit",
                SourceArmatureRootName = "Hips_outfit"
            };

            AvatarBuildPlanner.PlanArmatureMerge(mergeOp, outfitRoot, avatarRoot);

            check(mergeOp.IsValid, "Merge operation must be valid");
            check(mergeOp.Mappings.Count == 3, $"Expected 3 mapped bones, got {mergeOp.Mappings.Count}");
            check(mergeOp.Mappings.Any(m => m.SourcePath.EndsWith("Hips_outfit") && m.TargetPath.EndsWith("Hips")), "Hips must be mapped");
            check(mergeOp.Mappings.Any(m => m.SourcePath.EndsWith("Spine_outfit") && m.TargetPath.EndsWith("Spine")), "Spine must be mapped");
            check(mergeOp.Mappings.Any(m => m.SourcePath.EndsWith("Chest_outfit") && m.TargetPath.EndsWith("Chest")), "Chest must be mapped");

            check(mergeOp.RetainedRoots.Count == 1, $"Expected 1 retained bone root, got {mergeOp.RetainedRoots.Count}");
            check(mergeOp.RetainedRoots[0].SourcePath.EndsWith("Skirt_Special_Bone"), "Skirt bone must be retained to attach to avatar");
        });

        test("AvatarBuildPlanner detects conflicts, cycles, and invalid configurations", () =>
        {
            var plan = new AvatarBuildPlan { AvatarName = "ConflictedAvatar" };

            // 1. Conflicting targets for same source object
            plan.Operations.Add(new BoneAttachmentOperation
            {
                SourceObjectFileId = 500,
                SourceObjectName = "Hat",
                TargetHumanoidBone = 10 // Head
            });
            plan.Operations.Add(new BoneAttachmentOperation
            {
                SourceObjectFileId = 500,
                SourceObjectName = "Hat",
                TargetHumanoidBone = 18 // RightHand
            });

            // 2. Self-attachment
            plan.Operations.Add(new BoneAttachmentOperation
            {
                SourceObjectFileId = 600,
                SourceObjectName = "SelfProp",
                TargetObjectFileId = 600
            });

            // 3. Undefined target
            plan.Operations.Add(new BoneAttachmentOperation
            {
                SourceObjectFileId = 700,
                SourceObjectName = "FloatingProp",
                TargetHumanoidBone = -1,
                TargetObjectFileId = null,
                TargetReferencePath = ""
            });

            AvatarBuildPlanner.ValidatePlan(plan);

            check(plan.HasErrors, "Plan must have errors for invalid configurations");
            check(plan.Operations.Any(op => op.Diagnostics.Any(d => d.Code == "CONFLICTING_TARGETS")),
                "Must detect conflicting attachment targets for same source");
            check(plan.Operations.Any(op => op.Diagnostics.Any(d => d.Code == "SELF_ATTACHMENT")),
                "Must detect self-attachment attempt");
            check(plan.Operations.Any(op => op.Diagnostics.Any(d => d.Code == "TARGET_UNRESOLVED")),
                "Must detect unresolved target specification");

            // 4. Cyclic dependency
            var cyclicPlan = new AvatarBuildPlan { AvatarName = "CyclicPlan" };
            var op1 = new BoneAttachmentOperation { OperationId = "op1", SourceObjectFileId = 1, TargetObjectFileId = 2 };
            var op2 = new BoneAttachmentOperation { OperationId = "op2", SourceObjectFileId = 2, TargetObjectFileId = 1 };
            op1.DependsOnOperationIds.Add("op2");
            op2.DependsOnOperationIds.Add("op1");
            cyclicPlan.Operations.Add(op1);
            cyclicPlan.Operations.Add(op2);

            var ordered = cyclicPlan.GetOrderedOperations();
            check(cyclicPlan.HasErrors, "Cyclic plan must report errors");
            check(cyclicPlan.Diagnostics.Any(d => d.Code == "CYCLE_DETECTED"), "Must detect dependency cycle");
        });

        test("Compatibility evaluation correctly distinguishes full success from partial compatibility", () =>
        {
            // Case 1: 0 incomplete components, 0 unhandled VRCFury features, 0 plan errors -> Full Success (false)
            bool fullSuccess = PrefabVariantResolver.EvaluatePartialCompatibility(
                incompleteComponentCount: 0,
                unhandledVrcFuryFeatures: 0,
                planErrors: 0);
            check(!fullSuccess, "Clean supported conversion must report full success");

            // Case 2: Incomplete component -> Partial compatibility (true)
            bool incompleteComp = PrefabVariantResolver.EvaluatePartialCompatibility(
                incompleteComponentCount: 1,
                unhandledVrcFuryFeatures: 0,
                planErrors: 0);
            check(incompleteComp, "Incomplete components must report partial compatibility");

            // Case 3: Unhandled VRCFury feature -> Partial compatibility (true)
            bool unhandledVf = PrefabVariantResolver.EvaluatePartialCompatibility(
                incompleteComponentCount: 0,
                unhandledVrcFuryFeatures: 1,
                planErrors: 0);
            check(unhandledVf, "Unhandled VRCFury features must report partial compatibility");

            // Case 4: Plan errors -> Partial compatibility (true)
            bool planErr = PrefabVariantResolver.EvaluatePartialCompatibility(
                incompleteComponentCount: 0,
                unhandledVrcFuryFeatures: 0,
                planErrors: 1);
            check(planErr, "Plan errors must report partial compatibility");
        });
    }

    public sealed class Document
    {
        public ObjectData Object { get; set; } = new();
    }

    public sealed class ObjectData
    {
        public string Name { get; set; } = "";
        public ulong Reference { get; set; }
        public int Value { get; set; }
    }

    public sealed class PrefabInstanceDoc
    {
        public PrefabInstanceData PrefabInstance { get; set; } = new();
    }

    public sealed class PrefabInstanceData
    {
        public ModData m_Modification { get; set; } = new();
        public SourceObjData m_SourcePrefab { get; set; } = new();
    }

    public sealed class ModData
    {
        public Dictionary<string, ulong> m_TransformParent { get; set; } = new();
    }

    public sealed class SourceObjData
    {
        public long fileID { get; set; }
        public string guid { get; set; } = "";
        public int type { get; set; }
    }
}
