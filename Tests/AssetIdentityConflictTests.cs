using System;
using System.Collections.Generic;
using System.IO;
using UnityPackageImporter.Models;

#nullable enable

internal static class AssetIdentityConflictTests
{
    public static void Register(Action<string, Action> test, Action<bool, string> check)
    {
        void InTemp(Action<string> run)
        {
            var root = Path.Combine(Path.GetTempPath(), "asset-id-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try { run(root); }
            finally { Directory.Delete(root, true); }
        }

        test("Asset identity: YAML structure and scalar content remain significant in both merge paths", () => InTemp(root =>
        {
            var variants = new (string Left, string Right)[]
            {
                ("TextureImporter:\n  mipmaps:\n    sRGBTexture: 0\n  textureSettings:\n    filterMode: 0\n",
                 "TextureImporter:\n  mipmaps:\n    sRGBTexture: 0\n    textureSettings:\n      filterMode: 0\n"),
                ("TextureImporter:\n  userData: |\n    first\n     second\n",
                 "TextureImporter:\n  userData: |\n    first\n    second\n"),
                ("TextureImporter:\n  userData: |\n    # first comment is actual scalar content\n",
                 "TextureImporter:\n  userData: |\n    # different scalar content\n"),
                ("TextureImporter:\n  customSettings:\n    timeCreated: 1\n",
                 "TextureImporter:\n  customSettings:\n    timeCreated: 2\n"),
                ("TextureImporter:\n  settings: [first, second]\n",
                 "TextureImporter:\n  settings: [second, first]\n"),
                ("TextureImporter:\n  setting: !!str false\n",
                 "TextureImporter:\n  setting: !!bool false\n"),
                ("TextureImporter:\n  invalid: [\n", "TextureImporter:\n  invalid: [\n")
            };
            for (int index = 0; index < variants.Length; index++)
            {
                var left = Path.Combine(root, "Left" + index + ".png");
                var right = Path.Combine(root, "Right" + index + ".png");
                File.WriteAllText(left, "same-payload");
                File.WriteAllText(right, "same-payload");
                const string header = "fileFormatVersion: 2\nguid: structural-conflict\n";
                File.WriteAllText(left + ".meta", header + variants[index].Left);
                File.WriteAllText(right + ".meta", header + variants[index].Right);
                foreach (var order in new[] { new[] { left + ".meta", right + ".meta" }, new[] { right + ".meta", left + ".meta" } })
                {
                    var batch = UnityPackageAssetIndex.Build(order);
                    check(batch.Assets.Count == 0 && batch.ConflictedGuids.Contains("structural-conflict"),
                        "Structurally different or invalid metadata silently deduplicated, case " + index);
                }
                var assets = new Dictionary<string, string> { ["structural-conflict"] = left };
                var conflicts = new HashSet<string>();
                UnityPackageAssetIndex.MergeCompanion(assets, conflicts,
                    new Dictionary<string, string> { ["structural-conflict"] = right });
                check(!assets.ContainsKey("structural-conflict") && conflicts.Contains("structural-conflict"),
                    "Companion merge did not apply the same metadata identity rule, case " + index);
            }
        }));

        test("Asset identity: YAML mapping order and root timestamp do not change equivalent settings", () => InTemp(root =>
        {
            var left = Path.Combine(root, "First.meta");
            var right = Path.Combine(root, "Second.meta");
            File.WriteAllText(left, "fileFormatVersion: 2\nguid: AbCd\ntimeCreated: 1\nTextureImporter:\n  mipmaps: {sRGBTexture: 1}\n  isReadable: 0\n");
            File.WriteAllText(right, "# harmless comment\nguid: abcd\nTextureImporter:\n  isReadable: 0\n  mipmaps: {sRGBTexture: 1}\ntimeCreated: 2\nfileFormatVersion: 2\n");
            check(UnityPackageAssetIndex.AreMetadataEquivalent(left, right), "Equivalent parsed settings were rejected.");
        }));

        test("Asset identity: Equivalent duplicate assets deduplicate safely to canonical path", () => InTemp(root =>
        {
            var dirA = Path.Combine(root, "pkgA");
            var dirB = Path.Combine(root, "pkgB");
            Directory.CreateDirectory(dirA);
            Directory.CreateDirectory(dirB);

            var pathA = Path.Combine(dirA, "Texture.png");
            var pathB = Path.Combine(dirB, "Texture.png");
            File.WriteAllBytes(pathA, new byte[] { 1, 2, 3, 4, 5 });
            File.WriteAllBytes(pathB, new byte[] { 1, 2, 3, 4, 5 });

            string metaContent = "fileFormatVersion: 2\nguid: tex-guid-001\nTextureImporter:\n  sRGBTexture: 1\n";
            File.WriteAllText(pathA + ".meta", metaContent);
            File.WriteAllText(pathB + ".meta", metaContent);

            var forward = UnityPackageAssetIndex.Build(new[] { pathA + ".meta", pathB + ".meta" });
            check(forward.Assets.ContainsKey("tex-guid-001"), "Equivalent asset was not indexed.");
            check(forward.Conflicts.Count == 0, "Equivalent duplicate produced a spurious conflict.");
            check(forward.ConflictedGuids.Count == 0, "Equivalent duplicate was marked as conflicted.");

            var reverse = UnityPackageAssetIndex.Build(new[] { pathB + ".meta", pathA + ".meta" });
            check(reverse.Assets.ContainsKey("tex-guid-001"), "Equivalent asset was not indexed on reverse.");
            check(reverse.Conflicts.Count == 0, "Equivalent duplicate produced a spurious conflict on reverse.");
            check(string.Equals(forward.Assets["tex-guid-001"], reverse.Assets["tex-guid-001"], StringComparison.OrdinalIgnoreCase),
                "Reversing order selected a different file path for equivalent duplicate.");
        }));

        test("Asset identity: Equivalent duplicate ignores volatile timeCreated in meta", () => InTemp(root =>
        {
            var pathA = Path.Combine(root, "A.mat");
            var pathB = Path.Combine(root, "B.mat");
            File.WriteAllText(pathA, "material-payload");
            File.WriteAllText(pathB, "material-payload");

            File.WriteAllText(pathA + ".meta", "fileFormatVersion: 2\nguid: mat-guid-002\ntimeCreated: 1618239012\n");
            File.WriteAllText(pathB + ".meta", "fileFormatVersion: 2\nguid: mat-guid-002\ntimeCreated: 1699999999\n");

            var index = UnityPackageAssetIndex.Build(new[] { pathA + ".meta", pathB + ".meta" });
            check(index.Assets.ContainsKey("mat-guid-002"), "Duplicate with differing timeCreated was rejected.");
            check(index.Conflicts.Count == 0, "Different timeCreated produced a conflict.");
            check(index.ConflictedGuids.Count == 0, "Different timeCreated marked GUID conflicted.");
        }));

        test("Asset identity: Equivalent duplicate survives CRLF vs LF line endings", () => InTemp(root =>
        {
            var pathA = Path.Combine(root, "A.txt");
            var pathB = Path.Combine(root, "B.txt");
            File.WriteAllBytes(pathA, new byte[] { 10, 20 });
            File.WriteAllBytes(pathB, new byte[] { 10, 20 });

            File.WriteAllText(pathA + ".meta", "fileFormatVersion: 2\r\nguid: txt-guid-003\r\nDefaultImporter:\r\n  userData:\r\n");
            File.WriteAllText(pathB + ".meta", "fileFormatVersion: 2\nguid: txt-guid-003\nDefaultImporter:\n  userData:\n");

            var index = UnityPackageAssetIndex.Build(new[] { pathA + ".meta", pathB + ".meta" });
            check(index.Assets.ContainsKey("txt-guid-003"), "Duplicate with CRLF vs LF was rejected.");
            check(index.Conflicts.Count == 0, "Line ending differences caused a conflict.");
        }));

        test("Asset identity: Conflicting payload bytes are rejected symmetrically", () => InTemp(root =>
        {
            var pathA = Path.Combine(root, "A.png");
            var pathB = Path.Combine(root, "B.png");
            File.WriteAllBytes(pathA, new byte[] { 1, 2, 3 });
            File.WriteAllBytes(pathB, new byte[] { 1, 2, 4 }); // Different bytes!

            string meta = "fileFormatVersion: 2\nguid: conflict-payload-004\n";
            File.WriteAllText(pathA + ".meta", meta);
            File.WriteAllText(pathB + ".meta", meta);

            var forward = UnityPackageAssetIndex.Build(new[] { pathA + ".meta", pathB + ".meta" });
            check(!forward.Assets.ContainsKey("conflict-payload-004"), "Conflicting payload was resolved to an asset in forward order.");
            check(forward.ConflictedGuids.Contains("conflict-payload-004"), "Conflicted GUID was not tracked in forward order.");
            check(forward.Conflicts.Count == 1, "Conflict was not reported in forward order.");

            var reverse = UnityPackageAssetIndex.Build(new[] { pathB + ".meta", pathA + ".meta" });
            check(!reverse.Assets.ContainsKey("conflict-payload-004"), "Conflicting payload was resolved to an asset in reverse order.");
            check(reverse.ConflictedGuids.Contains("conflict-payload-004"), "Conflicted GUID was not tracked in reverse order.");
            check(reverse.Conflicts.Count == 1, "Conflict was not reported in reverse order.");
        }));

        test("Asset identity: Conflicting importer metadata is rejected even with identical payloads", () => InTemp(root =>
        {
            var pathA = Path.Combine(root, "A.png");
            var pathB = Path.Combine(root, "B.png");
            File.WriteAllBytes(pathA, new byte[] { 1, 2, 3 });
            File.WriteAllBytes(pathB, new byte[] { 1, 2, 3 }); // Same payload

            // Differing texture importer settings
            File.WriteAllText(pathA + ".meta", "fileFormatVersion: 2\nguid: conflict-meta-005\nTextureImporter:\n  sRGBTexture: 1\n");
            File.WriteAllText(pathB + ".meta", "fileFormatVersion: 2\nguid: conflict-meta-005\nTextureImporter:\n  sRGBTexture: 0\n");

            var forward = UnityPackageAssetIndex.Build(new[] { pathA + ".meta", pathB + ".meta" });
            check(!forward.Assets.ContainsKey("conflict-meta-005"), "Conflicting metadata allowed silent resolution.");
            check(forward.ConflictedGuids.Contains("conflict-meta-005"), "Conflicted GUID was not registered.");
            check(forward.Conflicts.Count == 1, "Conflicting metadata was not reported.");

            var reverse = UnityPackageAssetIndex.Build(new[] { pathB + ".meta", pathA + ".meta" });
            check(!reverse.Assets.ContainsKey("conflict-meta-005"), "Conflicting metadata allowed silent resolution in reverse.");
            check(reverse.ConflictedGuids.Contains("conflict-meta-005"), "Conflicted GUID was not registered in reverse.");
            check(reverse.Conflicts.Count == 1, "Conflicting metadata was not reported in reverse.");
        }));

        test("Asset identity: Duplicate prefabs deduplicate when equivalent and omit when conflicting", () => InTemp(root =>
        {
            var prefab1 = Path.Combine(root, "P1.prefab");
            var prefab2 = Path.Combine(root, "P2.prefab");
            File.WriteAllText(prefab1, "%YAML 1.1\nGameObject:\n  name: Test\n");
            File.WriteAllText(prefab2, "%YAML 1.1\nGameObject:\n  name: Test\n");

            File.WriteAllText(prefab1 + ".meta", "fileFormatVersion: 2\nguid: prefab-equiv-006\n");
            File.WriteAllText(prefab2 + ".meta", "fileFormatVersion: 2\nguid: prefab-equiv-006\n");

            var equiv = UnityPackageAssetIndex.Build(new[] { prefab1 + ".meta", prefab2 + ".meta" });
            check(equiv.Prefabs.Count == 1 && equiv.Prefabs.ContainsKey("prefab-equiv-006"), "Equivalent prefabs did not deduplicate.");
            check(equiv.Conflicts.Count == 0, "Equivalent prefabs reported conflict.");

            var prefabDiff = Path.Combine(root, "P3.prefab");
            File.WriteAllText(prefabDiff, "%YAML 1.1\nGameObject:\n  name: Different\n");
            File.WriteAllText(prefabDiff + ".meta", "fileFormatVersion: 2\nguid: prefab-diff-007\n");
            var prefabDiff2 = Path.Combine(root, "P4.prefab");
            File.WriteAllText(prefabDiff2, "%YAML 1.1\nGameObject:\n  name: Another\n");
            File.WriteAllText(prefabDiff2 + ".meta", "fileFormatVersion: 2\nguid: prefab-diff-007\n");

            var diff = UnityPackageAssetIndex.Build(new[] { prefabDiff + ".meta", prefabDiff2 + ".meta" });
            check(!diff.Prefabs.ContainsKey("prefab-diff-007"), "Conflicting prefab was not excluded from Prefabs.");
            check(!diff.Assets.ContainsKey("prefab-diff-007"), "Conflicting prefab was not excluded from Assets.");
            check(diff.ConflictedGuids.Contains("prefab-diff-007"), "Conflicting prefab was not tracked in ConflictedGuids.");
        }));

        test("Asset identity: Duplicate scenes deduplicate when equivalent and omit when conflicting", () => InTemp(root =>
        {
            var s1 = Path.Combine(root, "S1.unity");
            var s2 = Path.Combine(root, "S2.unity");
            File.WriteAllText(s1, "%YAML 1.1\nScene:\n  m_Name: Scene\n");
            File.WriteAllText(s2, "%YAML 1.1\nScene:\n  m_Name: Scene\n");
            File.WriteAllText(s1 + ".meta", "fileFormatVersion: 2\nguid: scene-008\n");
            File.WriteAllText(s2 + ".meta", "fileFormatVersion: 2\nguid: scene-008\n");

            var equiv = UnityPackageAssetIndex.Build(new[] { s1 + ".meta", s2 + ".meta" });
            check(equiv.Scenes.Count == 1 && equiv.Scenes.ContainsKey("scene-008"), "Equivalent scenes did not deduplicate.");

            File.WriteAllText(s2, "%YAML 1.1\nScene:\n  m_Name: DifferentScene\n");
            var diff = UnityPackageAssetIndex.Build(new[] { s1 + ".meta", s2 + ".meta" });
            check(!diff.Scenes.ContainsKey("scene-008"), "Conflicting scene was not removed from Scenes.");
            check(diff.ConflictedGuids.Contains("scene-008"), "Conflicting scene was not recorded as conflicted.");
        }));

        test("Companion merge: Legitimate missing dependencies are added to asset index", () => InTemp(root =>
        {
            var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var conflicts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var companionMat = Path.Combine(root, "Restored.mat");
            File.WriteAllText(companionMat, "restored-material");
            File.WriteAllText(companionMat + ".meta", "fileFormatVersion: 2\nguid: restored-guid-009\n");

            var companionAssets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "restored-guid-009", companionMat }
            };

            var result = UnityPackageAssetIndex.MergeCompanion(assets, conflicts, companionAssets);
            check(result.AddedGuids.Count == 1 && result.AddedGuids[0] == "restored-guid-009", "Missing dependency was not added.");
            check(assets.ContainsKey("restored-guid-009") && assets["restored-guid-009"] == companionMat, "Asset index missing companion entry.");
            check(conflicts.Count == 0, "Spurious conflict during legitimate recovery.");
        }));

        test("Companion merge: Equivalent duplicate asset is safely deduplicated", () => InTemp(root =>
        {
            var baseMat = Path.Combine(root, "Base.mat");
            var compMat = Path.Combine(root, "Comp.mat");
            File.WriteAllText(baseMat, "content");
            File.WriteAllText(compMat, "content");
            File.WriteAllText(baseMat + ".meta", "fileFormatVersion: 2\nguid: equiv-guid-010\n");
            File.WriteAllText(compMat + ".meta", "fileFormatVersion: 2\nguid: equiv-guid-010\n");

            var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "equiv-guid-010", baseMat }
            };
            var conflicts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var companionAssets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "equiv-guid-010", compMat }
            };

            var result = UnityPackageAssetIndex.MergeCompanion(assets, conflicts, companionAssets);
            check(result.IgnoredEquivalentGuids.Count == 1, "Equivalent companion duplicate was not recognized.");
            check(assets["equiv-guid-010"] == baseMat, "Existing asset was overwritten by equivalent companion.");
            check(conflicts.Count == 0, "Equivalent companion produced a conflict.");
        }));

        test("Companion merge: Conflict with already-assigned asset preserves existing assignment", () => InTemp(root =>
        {
            var baseMat = Path.Combine(root, "Base.mat");
            var compMat = Path.Combine(root, "Comp.mat");
            File.WriteAllText(baseMat, "base-content");
            File.WriteAllText(compMat, "comp-content-different");
            File.WriteAllText(baseMat + ".meta", "fileFormatVersion: 2\nguid: assigned-guid-011\n");
            File.WriteAllText(compMat + ".meta", "fileFormatVersion: 2\nguid: assigned-guid-011\n");

            var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "assigned-guid-011", baseMat }
            };
            var conflicts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var companionAssets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "assigned-guid-011", compMat }
            };

            var conflictWarnings = new List<string>();
            var result = UnityPackageAssetIndex.MergeCompanion(
                assets,
                conflicts,
                companionAssets,
                isAssetAssignedOrCached: guid => guid == "assigned-guid-011",
                onConflict: conflictWarnings.Add);

            check(result.ConflictedGuids.Contains("assigned-guid-011"), "Companion conflict was not recorded.");
            check(conflicts.Contains("assigned-guid-011"), "Conflicted GUID set does not contain conflicting GUID.");
            check(assets.ContainsKey("assigned-guid-011") && assets["assigned-guid-011"] == baseMat,
                "Assigned asset was replaced by conflicting companion asset.");
            check(conflictWarnings.Count == 1, "Conflict warning was not emitted.");
        }));

        test("Companion merge: Conflict with unassigned asset removes entry so neither silently wins", () => InTemp(root =>
        {
            var baseMat = Path.Combine(root, "Base.mat");
            var compMat = Path.Combine(root, "Comp.mat");
            File.WriteAllText(baseMat, "content-A");
            File.WriteAllText(compMat, "content-B");
            File.WriteAllText(baseMat + ".meta", "fileFormatVersion: 2\nguid: unassigned-guid-012\n");
            File.WriteAllText(compMat + ".meta", "fileFormatVersion: 2\nguid: unassigned-guid-012\n");

            var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "unassigned-guid-012", baseMat }
            };
            var conflicts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var companionAssets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "unassigned-guid-012", compMat }
            };

            var result = UnityPackageAssetIndex.MergeCompanion(
                assets,
                conflicts,
                companionAssets,
                isAssetAssignedOrCached: _ => false); // Not assigned

            check(result.ConflictedGuids.Contains("unassigned-guid-012"), "Conflict was not tracked.");
            check(conflicts.Contains("unassigned-guid-012"), "Conflicted GUID set missing entry.");
            check(!assets.ContainsKey("unassigned-guid-012"), "Unassigned conflicting asset was kept in asset index.");
        }));

        test("Companion merge: Internal companion conflict is propagated and blocks resolution", () => InTemp(root =>
        {
            var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var conflicts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var companionMat = Path.Combine(root, "InternalConflict.mat");
            File.WriteAllText(companionMat, "payload");
            File.WriteAllText(companionMat + ".meta", "fileFormatVersion: 2\nguid: internal-conf-013\n");

            var companionAssets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var companionConflicts = new[] { "internal-conf-013" };

            var warnings = new List<string>();
            var result = UnityPackageAssetIndex.MergeCompanion(
                assets,
                conflicts,
                companionAssets,
                companionConflictedGuids: companionConflicts,
                onConflict: warnings.Add);

            check(conflicts.Contains("internal-conf-013"), "Internal companion conflict was not registered.");
            check(!assets.ContainsKey("internal-conf-013"), "Internal conflict asset was unexpectedly added to index.");
            check(warnings.Count >= 1, "Warning was not emitted for companion internal conflict.");
        }));
    }
}
