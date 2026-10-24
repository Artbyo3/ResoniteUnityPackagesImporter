import copy
import json
import sys
import tempfile
import unittest
from pathlib import Path
from html.parser import HTMLParser

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from support_map import load_capabilities, render_support_map


class InventoryParser(HTMLParser):
    def __init__(self, body):
        super().__init__()
        self.groups = []
        self.rows = []
        self.accordion_names = []
        self.segments = []
        self.feed(body)

    def handle_starttag(self, tag, attributes):
        attributes = dict(attributes)
        if tag in {"div", "section"}:
            self.groups.append(attributes.get("data-support-group", self.groups[-1] if self.groups else None))
        if "data-support-row" in attributes:
            self.accordion_names.append(attributes.get("name"))
            self.rows.append((self.groups[-1], attributes["data-status"]))
        if "data-support-segment" in attributes:
            self.segments.append(attributes["data-status"])

    def handle_endtag(self, tag):
        if tag in {"div", "section"}:
            self.groups.pop()


class SupportMapTests(unittest.TestCase):
    def setUp(self):
        self.data = load_capabilities()

    def assert_invalid(self, data):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "capabilities.json"
            path.write_text(json.dumps(data), encoding="utf-8")
            with self.assertRaises(ValueError):
                load_capabilities(path)

    def test_component_cards_share_one_exclusive_group(self):
        for scope in ["all", "ma", "vrcfury"]:
            with self.subTest(scope=scope):
                parsed = InventoryParser(render_support_map(scope, self.data))
                self.assertEqual(len(parsed.accordion_names), len(parsed.rows))
                self.assertEqual(set(parsed.accordion_names), {f"support-components-{scope}"})

    def test_vrchat_inventory_is_separate_and_evidence_backed(self):
        path = Path(__file__).resolve().parents[1] / "vrchat-capabilities.json"
        data = load_capabilities(path)
        expected = {"VRCAvatarDescriptor", "VRCPhysBone", "VRCPhysBoneCollider",
                    "VRCContactSender", "VRCContactReceiver", "VRCAimConstraint",
                    "VRCLookAtConstraint", "VRCParentConstraint", "VRCPositionConstraint",
                    "VRCRotationConstraint", "VRCScaleConstraint", "VRCHeadChop",
                    "VRCRaycast", "VRCSpatialAudioSource", "VRCStation",
                    "VRCPipelineManager", "VRCIKFollower"}
        self.assertEqual({entry["type"] for entry in data["entries"]}, expected)
        self.assertTrue(all(entry["framework"] == "vrchat" for entry in data["entries"]))
        self.assertTrue(all(entry["source"]["repo"] == "vrchat-community/creator-docs" for entry in data["entries"]))
        partial = [entry for entry in data["entries"] if entry["status"] == "limited"]
        self.assertEqual([entry["type"] for entry in partial], ["VRCAvatarDescriptor"])
        self.assertIn("Tests/AvatarViewpointTests.cs", partial[0]["evidence"])
        self.assertIn("viewpoint", partial[0]["summary"])
        self.assertFalse(any(entry["status"] == "supported" for entry in data["entries"]))
        body = render_support_map("vrchat")
        parsed = InventoryParser(body)
        self.assertEqual(len(parsed.rows), 17)
        self.assertEqual(len(parsed.segments), 15)
        self.assertIn('<span data-support-partial>1</span>', body)
        self.assertIn(("not-applicable", "not-applicable"), parsed.rows)
        self.assertIn(("legacy", "legacy"), parsed.rows)
        self.assertEqual(set(parsed.accordion_names), {"support-components-vrchat"})
        self.assertNotIn("MA + VRCFury", body)
        self.assertNotIn("Excluded framework/helper types: .", body)
        self.assertNotIn("data-support-framework", body)

    def test_vrchat_sources_require_pinned_official_documentation(self):
        path = Path(__file__).resolve().parents[1] / "vrchat-capabilities.json"
        for change in [{"repo": "example/unverified"}, {"revision": "main"}, {"path": "fake.cs"}]:
            data = load_capabilities(path)
            data["entries"][0]["source"].update(change)
            self.assert_invalid(data)

    def test_inventory_has_unique_pinned_sources_and_real_evidence(self):
        self.assertEqual(len(self.data["entries"]), 94)
        self.assertEqual(sum(item["framework"] == "ma" for item in self.data["entries"]), 33)
        self.assertEqual(sum(item["framework"] == "vrcfury" for item in self.data["entries"]), 61)
        self.assertEqual(len({item["id"] for item in self.data["entries"]}), len(self.data["entries"]))
        self.assertEqual({item["framework"] for item in self.data["entries"]}, {"ma", "vrcfury"})
        for entry in self.data["entries"]:
            with self.subTest(entry=entry["id"]):
                self.assertEqual(len(entry["source"]["revision"]), 40)
                if entry["status"] == "limited":
                    self.assertTrue(any(item.startswith("Tests/") for item in entry["evidence"]))
                    self.assertTrue(any(item.startswith("UnityPackageImporter/") for item in entry["evidence"]))

    def test_duplicate_ids_invalid_states_and_missing_evidence_fail(self):
        for mutation in [lambda data: data["entries"].append(data["entries"][0]),
                         lambda data: data["entries"][0].update(status="complete"),
                         lambda data: data["entries"][0].update(evidence=[]),
                         lambda data: data["entries"][0]["source"].update(revision="main")]:
            data = copy.deepcopy(self.data)
            mutation(data)
            self.assert_invalid(data)

    def test_roadmap_requires_real_issue_and_relevant_scope(self):
        for status, issue in [("missing", None), ("missing", "javascript:alert(1)"), ("not-applicable", "https://github.com/example/repo/issues/1")]:
            data = copy.deepcopy(self.data)
            data["entries"][0].update(work="planned", status=status, issue=issue)
            self.assert_invalid(data)

    def test_essential_outcome_is_visible_in_summary_without_javascript(self):
        body = render_support_map("ma", self.data)
        self.assertIn("Shape Changer", body)
        self.assertIn('id="component-support"', body)
        self.assertIn("Initial Set weights only. No deletion or live control.</span></summary>", body.replace("\n", ""))
        self.assertIn("Initial appearance is not live behavior", body)
        self.assertIn("data-support-tools hidden", body)
        self.assertNotIn('data-framework="vrcfury"', body)
        self.assertEqual(body.count("data-support-row"), sum(item["framework"] == "ma" for item in self.data["entries"]))

    def test_all_component_rows_render_without_reveal_controls(self):
        body = render_support_map("all", self.data)
        self.assertEqual(body.count("data-support-row"), len(self.data["entries"]))
        self.assertNotIn("data-support-more", body)
        self.assertNotIn("Show more components", body)
        for summary in body.split('<details class="support-row"')[1:]:
            self.assertNotIn(" hidden", summary.split(">", 1)[0])

    def test_scope_filter_and_html_escaping(self):
        data = copy.deepcopy(self.data)
        data["entries"][0].update(name='<script>alert("test")</script>')
        body = render_support_map("all", data)
        self.assertIn("&lt;script&gt;", body)
        self.assertNotIn("<script>", body)
        self.assertIn("Scheduled work", body)
        with self.assertRaises(ValueError):
            render_support_map("unknown", data)

    def test_future_tracking_renders_without_changing_support_status(self):
        data = copy.deepcopy(self.data)
        entry = data["entries"][0]
        entry.update(work="in-progress", issue="https://github.com/example/repo/issues/12")
        body = render_support_map(data=data)
        self.assertIn('<span class="support-work">In progress</span>', body)
        self.assertIn('href="https://github.com/example/repo/issues/12"', body)
        self.assertEqual(entry["status"], "limited")
        entry.update(status="supported")
        self.assertIn('support-state--supported', render_support_map(data=data))

    def test_status_totals_include_zero_fully_supported(self):
        for scope in ["ma", "vrcfury"]:
            body = render_support_map(scope, self.data)
            self.assertIn('<span data-support-complete>0</span>', body)
            self.assertIn('Partial support</span>', body.replace('\n', ''))
            self.assertIn('Not supported</span>', body.replace('\n', ''))
            self.assertIn('Not applicable</span>', body.replace('\n', ''))
            self.assertIn('data-support-scope="' + scope + '"', body)
            self.assertNotIn('<h2>', body)
            self.assertNotIn('support-state--supported', body)

    def test_collection_excludes_not_applicable_and_legacy(self):
        for scope, total, partial, excluded, legacy in [("ma", 29, 7, 4, 0), ("vrcfury", 43, 4, 9, 9), ("all", 72, 11, 13, 9)]:
            with self.subTest(scope=scope):
                body = render_support_map(scope, self.data)
                parsed = InventoryParser(body)
                self.assertIn('<span data-support-total>' + str(total) + '</span>', body)
                self.assertIn('<span data-support-partial>' + str(partial) + '</span>', body)
                self.assertEqual(len(parsed.segments), total)
                self.assertNotIn("not-applicable", parsed.segments)
                self.assertNotIn("legacy", parsed.segments)
                self.assertEqual(sum(group == "collection" for group, status in parsed.rows), total)
                self.assertEqual(sum(group == "not-applicable" for group, status in parsed.rows), excluded)
                self.assertEqual(sum(group == "legacy" for group, status in parsed.rows), legacy)
                self.assertTrue(all(status == "not-applicable" for group, status in parsed.rows if group == "not-applicable"))
                self.assertLess(body.index('data-support-group="collection"'), body.index('data-support-group="not-applicable"'))
                self.assertEqual(body.count('class="support-row__serial"'), total)

    def test_only_full_support_completes_a_collection_slot(self):
        data = copy.deepcopy(self.data)
        data["entries"][0]["status"] = "supported"
        body = render_support_map("ma", data)
        self.assertIn('<span data-support-complete>1</span>', body)
        self.assertIn('<span data-support-total>29</span>', body)
        self.assertIn('<span data-support-partial>6</span>', body)
        self.assertEqual(InventoryParser(body).segments.count("supported"), 1)
        self.assertIn('1 supported, 6 partial, 22 not supported', body)

    def test_snapshot_does_not_invent_delivery_commitments(self):
        self.assertTrue(all(entry["work"] == "unplanned" and entry["issue"] is None for entry in self.data["entries"]))
        body = render_support_map(data=self.data)
        self.assertNotIn("100%", body)
        self.assertIn("No scheduled delivery or tracking issue", body)


if __name__ == "__main__":
    unittest.main()
