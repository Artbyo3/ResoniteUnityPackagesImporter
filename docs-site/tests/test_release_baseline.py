import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from release_baseline import load_release_baseline
from support_map import load_capabilities, render_support_map
from visual_guides import load_visual_guides

HERE = Path(__file__).resolve().parents[1]


class ReleaseBaselineTests(unittest.TestCase):
    def test_release_has_immutable_source_and_honest_verification(self):
        release = load_release_baseline()
        self.assertEqual(len(release["revision"]), 40)
        self.assertEqual(release["verification"], "source-reviewed")
        self.assertEqual(release["live_test"], "pending")

    def test_invalid_release_metadata_is_rejected(self):
        for change in [{"revision": "main"}, {"version": "latest"},
                       {"repository": "example/other"}, {"verification": "live-tested"},
                       {"live_test": "probably"}, {"archive": "another-version.zip"}]:
            data = {**load_release_baseline(), **change}
            with self.subTest(change=change), tempfile.TemporaryDirectory() as temporary:
                path = Path(temporary) / "release.json"
                path.write_text(json.dumps(data), encoding="utf-8")
                with self.assertRaises(ValueError):
                    load_release_baseline(path)

    def test_component_evidence_points_to_released_source(self):
        release = load_release_baseline()
        prefix = "https://github.com/" + release["repository"] + "/blob/" + release["revision"] + "/"
        for filename, scope in [("capabilities.json", "ma"), ("capabilities.json", "vrcfury"),
                                ("vrchat-capabilities.json", "vrchat")]:
            data = load_capabilities(HERE / filename)
            body = render_support_map(scope, data)
            self.assertIn(release["version"], body)
            self.assertIn("not newer local changes", body)
            for entry in data["entries"]:
                if entry["framework"] == scope:
                    for evidence in entry["evidence"]:
                        self.assertIn('href="' + prefix + evidence + '"', body)

    def test_install_download_matches_the_documented_release(self):
        release = load_release_baseline()
        url = "https://github.com/" + release["repository"] + "/releases/download/" + release["version"] + "/" + release["archive"]
        guide = load_visual_guides()["install"]
        self.assertEqual(guide["actions"][0]["target"], url)
        instructions = (HERE / "pages/getting-started/installation.md").read_text(encoding="utf-8")
        self.assertIn(url, instructions)
        self.assertNotIn("releases/latest", instructions)
        self.assertIn("SHA256SUMS.txt", instructions)
        self.assertIn("dependency from the same earlier release", instructions)

    def test_public_project_status_contains_no_illustrative_records(self):
        data = json.loads((HERE / "pages/data/project-status.json").read_text(encoding="utf-8"))
        self.assertEqual(data, {"state": "not-connected", "updatedAt": None, "issues": []})
        self.assertFalse((HERE / "pages/data/project-status-preview.json").exists())
        for filename in ["project-status/index.md", "documentation/index.md"]:
            body = (HERE / "pages" / filename).read_text(encoding="utf-8")
            self.assertIn("not connected yet", body)
            self.assertNotIn("Sample data", body)
            self.assertNotIn("sample items", body)

    def test_synthetic_scale_query_cannot_enable_fake_public_updates(self):
        source = (HERE / "pages/javascripts/project-status.js").read_text(encoding="utf-8")
        self.assertNotIn('parameters.get("preview")', source)
        self.assertNotIn("Scale test", source)
        self.assertIn('new Set(data.issues.map(item => item.id))', source)


if __name__ == "__main__":
    unittest.main()
