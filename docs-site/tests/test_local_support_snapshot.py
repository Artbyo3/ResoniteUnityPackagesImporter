import copy
import json
import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from local_support_snapshot import AUDIT_CONCERNS, CATALOG, local_support_snapshot
from release_baseline import load_release_baseline
from support_map import HERE, load_capabilities, render_support_map


class LocalSupportSnapshotTests(unittest.TestCase):
    def setUp(self):
        self.data = load_capabilities()

    def test_local_snapshot_preserves_release_data_and_existing_ids(self):
        original = copy.deepcopy(self.data)
        snapshot = local_support_snapshot(self.data)
        self.assertEqual(self.data, original)
        self.assertEqual(snapshot["entries"][:len(original["entries"])][0]["id"], original["entries"][0]["id"])
        self.assertEqual([entry["id"] for entry in snapshot["entries"][:len(original["entries"])]],
                         [entry["id"] for entry in original["entries"]])
        self.assertEqual(len({entry["id"] for entry in snapshot["entries"]}), len(snapshot["entries"]))
        self.assertTrue(all(entry["work"] == "unplanned" and entry["issue"] is None for entry in snapshot["entries"]))

    def test_all_action_cards_include_explicit_scale_and_drop_rejection(self):
        snapshot = local_support_snapshot(self.data)
        actions = [entry for entry in snapshot["entries"] if entry["id"].startswith("vrcfury-action-")]
        self.assertEqual(len(actions), 19)
        for name in ["ScaleAction", "WorldDropAction"]:
            entry = next(entry for entry in actions if entry["type"] == name)
            self.assertEqual(entry["status"], "missing")
            self.assertIn("rejects this action", entry["summary"])
            self.assertIn("No native runtime support", entry["runtime_note"])
            self.assertIn("Tests/VrcFuryWardrobeTests.cs", entry["evidence"])
        self.assertIn("all 19 VRCFury action cards", snapshot["scope"])

    def test_known_semantic_concerns_do_not_earn_partial_behavior_credit(self):
        entries = {entry["type"]: entry for entry in local_support_snapshot(self.data)["entries"]}
        for name in AUDIT_CONCERNS:
            with self.subTest(name=name):
                self.assertEqual(entries[name]["status"], "re-evaluation")
                self.assertIn(AUDIT_CONCERNS[name], entries[name]["reason"])
                self.assertIn("withheld", entries[name]["runtime_note"])

    def test_local_cards_follow_catalog_limits_and_evidence(self):
        catalog = json.loads(CATALOG.read_text(encoding="utf-8"))
        by_type = {entry["name"]: entry for group in catalog["groups"] for entry in group["entries"]}
        for filename in ["capabilities.json", "vrchat-capabilities.json"]:
            snapshot = local_support_snapshot(load_capabilities(HERE / filename))
            for entry in snapshot["entries"]:
                with self.subTest(name=entry["type"]):
                    name = "PipelineManager" if entry["type"] == "VRCPipelineManager" else entry["type"]
                    local = by_type[name]
                    self.assertEqual(entry["evidence"], list(dict.fromkeys(local["evidence"] + local["tests"])))
                    if name not in AUDIT_CONCERNS:
                        self.assertEqual(entry["summary"], local["reason"])
                        self.assertEqual(entry["status"] == "limited", local["status"] == "partial")
                    if local["status"] in {"missing", "indexed"}:
                        self.assertFalse(entry["legacy"])

    def test_pending_destructive_and_legacy_outcomes_are_not_excluded(self):
        entries = {entry["type"]: entry for entry in local_support_snapshot(self.data)["entries"]}
        for name in ["ModularAvatarReplaceObject", "ModularAvatarPlatformFilter", "DeleteDuringUpload",
                     "BlendshapeOptimizer", "MakeWriteDefaultsOff", "RemoveHandGestures"]:
            with self.subTest(name=name):
                self.assertEqual(entries[name]["status"], "missing")
                self.assertFalse(entries[name]["legacy"])

    def test_local_rendering_never_attributes_local_evidence_to_a_release(self):
        release = load_release_baseline()
        prefix = "https://github.com/" + release["repository"] + "/blob/"
        for scope in ["all", "ma", "vrcfury", "vrchat"]:
            with self.subTest(scope=scope):
                body = render_support_map(scope, local=True)
                self.assertIn("Local working tree (unreleased)", body)
                self.assertIn("Live test pending", body)
                self.assertIn("including uncommitted changes", body)
                self.assertIn("may differ", body)
                self.assertNotIn(prefix, body)
                self.assertNotIn("Released-source implementation", body)
                self.assertNotIn("published " + release["published"], body)
                self.assertIn("Upstream source", body)

    def test_release_rendering_remains_the_default_and_preview_is_opt_in(self):
        with patch.dict(os.environ, {"IMPORTER_DOCS_LOCAL_SNAPSHOT": "0"}):
            self.assertEqual(render_support_map("ma"), render_support_map("ma", local=False))
        with patch.dict(os.environ, {"IMPORTER_DOCS_LOCAL_SNAPSHOT": "1"}):
            self.assertIn("Local working tree (unreleased)", render_support_map("ma"))
            self.assertNotIn("Local working tree (unreleased)", render_support_map("ma", local=False))

    def test_missing_mapping_or_invalid_catalog_fails_without_mutating_source(self):
        original = copy.deepcopy(self.data)
        invalid = copy.deepcopy(self.data)
        invalid["entries"][0]["type"] = "UnmappedComponent"
        with self.assertRaisesRegex(ValueError, "No local component evidence"):
            local_support_snapshot(invalid)
        catalog = json.loads(CATALOG.read_text(encoding="utf-8"))
        for mutation in ["schema", "duplicate", "evidence"]:
            changed = copy.deepcopy(catalog)
            if mutation == "schema":
                changed["schemaVersion"] = 9
            elif mutation == "duplicate":
                changed["groups"][0]["entries"].append(changed["groups"][0]["entries"][0])
            else:
                entry = next(entry for group in changed["groups"] for entry in group["entries"]
                             if entry["name"] == self.data["entries"][0]["type"])
                entry["evidence"] = ["../outside.cs"]
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as temporary:
                path = Path(temporary) / "catalog.json"
                path.write_text(json.dumps(changed), encoding="utf-8")
                with self.assertRaises(ValueError):
                    local_support_snapshot(self.data, path)
        self.assertEqual(self.data, original)


if __name__ == "__main__":
    unittest.main()
