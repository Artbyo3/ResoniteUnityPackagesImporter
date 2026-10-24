import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from docs_workspace import load_workspace, render_answer
from visual_guides import load_visual_guides, render_diagram, render_visual_guide


class VisualGuideTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(__file__).resolve().parents[1] / "pages"
        self.guides = load_visual_guides()
        self.topics = {topic["id"]: topic for topic in load_workspace()["topics"]}

    def test_first_import_steps_appear_before_demo_and_reference(self):
        topic = self.topics["first-import"]
        result = render_visual_guide(self.root, topic, self.root / topic["page"], "Full instructions", self.guides["first-import"])
        self.assertEqual(result.count('<ol class="visual-steps">'), 1)
        self.assertLess(result.index('class="visual-steps"'), result.index('class="visual-media"'))
        self.assertLess(result.index('class="visual-notice"'), result.index('class="visual-reference"'))
        self.assertIn("Add companion packages if requested.", result)
        self.assertIn("Full instructions", result)

    def test_all_functional_topics_have_visual_fronts(self):
        self.assertEqual(set(self.guides), set(self.topics) - {"license", "notices"})
        self.assertEqual(len(self.guides), 22)

    def test_walkthrough_links_each_visible_instruction_to_a_real_screenshot(self):
        topic = self.topics["first-import"]
        result = render_visual_guide(self.root, topic, self.root / topic["page"], "Reference", self.guides["first-import"])
        self.assertIn("data-import-walkthrough", result)
        self.assertEqual(result.count('aria-label="Views"'), 1)
        self.assertEqual(result.count("data-visual-frame"), 3)
        for index in range(3):
            self.assertIn(f'href="#visual-first-import-{index}" data-visual-select="{index}"', result)
            self.assertIn(f'id="visual-first-import-{index}" data-visual-frame', result)
        self.assertIn("pre-import-prompt.png", result)
        self.assertIn("material-customizer-spheres.png", result)
        self.assertIn("avatar-resonite.png", result)
        self.assertIn("An example Resonite conversion.", result)
        self.assertNotIn("data-visual-paired", result)

    def test_walkthrough_demo_is_optional_and_has_no_autoplay(self):
        topic = self.topics["first-import"]
        guide = dict(self.guides["first-import"])
        result = render_visual_guide(self.root, topic, self.root / topic["page"], "Reference", guide)
        demo = result.split('<details class="import-demo">', 1)[1].split('</details>', 1)[0]
        self.assertIn("Watch the full import", demo)
        self.assertIn("landing-background.webm", demo)
        self.assertIn("landing-background.mp4", demo)
        self.assertIn('preload="none"', demo)
        self.assertNotIn("autoplay", demo)
        guide.pop("demo")
        self.assertNotIn("import-demo", render_visual_guide(self.root, topic, self.root / topic["page"], "Reference", guide))

    def test_two_step_walkthrough_does_not_use_paired_media(self):
        topic = self.topics["first-import"]
        guide = {**self.guides["first-import"], "steps": self.guides["first-import"]["steps"][:2], "media": self.guides["first-import"]["media"][:2]}
        result = render_visual_guide(self.root, topic, self.root / topic["page"], "Reference", guide)
        self.assertNotIn("data-visual-paired", result)
        self.assertEqual(result.count("data-visual-frame"), 2)

    def test_invalid_walkthroughs_and_demo_assets_are_rejected(self):
        for change in [
            {"walkthrough": True},
            {"walkthrough": True, "steps": [{"title": "Test", "text": "Test"}], "media": []},
            {"walkthrough": True, "steps": [{"title": "Test", "text": "Test"}], "media": [{"video": "landing-background.webm"}]},
            {"demo": {}},
            {"demo": {"title": "Test", "caption": "Test", "video": "missing.webm", "fallback": "landing-background.mp4"}},
        ]:
            with self.subTest(change=change), tempfile.TemporaryDirectory() as temporary:
                path = Path(temporary) / "guides.json"
                path.write_text(json.dumps({"guides": {"test": {"lead": "Test", **change}}}), encoding="utf-8")
                with self.assertRaises(ValueError):
                    load_visual_guides(path)

    def test_essential_content_precedes_complete_reference(self):
        for identifier, guide in self.guides.items():
            topic = self.topics[identifier]
            result = render_visual_guide(self.root, topic, self.root / topic["page"], "Original technical source", guide)
            primary, reference = result.split('<details class="visual-reference"', 1)
            if identifier not in {"modular-avatar", "vrcfury"}:
                self.assertIn("visual-lead", primary)
            self.assertIn("Original technical source", reference)
            self.assertNotIn(" open", reference.split(">", 1)[0])
            if guide.get("notice") and identifier not in {"modular-avatar", "vrcfury"}:
                self.assertIn('class="visual-notice"', primary)

    def test_support_inventory_is_once_outside_reference(self):
        for identifier in ["compatibility", "modular-avatar", "vrcfury"]:
            topic = self.topics[identifier]
            result = render_answer(self.root, topic, self.root / topic["page"])
            primary, reference = result.split('<details class="visual-reference"', 1)
            self.assertEqual(primary.count("data-support-map"), 1)
            self.assertNotIn("data-support-map", reference)

    def test_framework_inventory_has_no_empty_visual_section(self):
        for identifier in ["modular-avatar", "vrcfury"]:
            topic = self.topics[identifier]
            body = render_answer(self.root, topic, self.root / topic["page"])
            self.assertLess(body.index('data-support-map'), body.index('<details class="visual-reference"'))
            self.assertNotIn('class="visual-capture"', body)
            self.assertNotIn('class="visual-guide"', body)
            self.assertNotIn('class="visual-boundary"', body)
            self.assertIn("support-row__outcome", body)

    def test_overview_replaces_combined_frameworks_with_vrchat_sdk(self):
        topic = self.topics["compatibility"]
        self.assertEqual(topic["capabilities"], "vrchat")
        body = render_answer(self.root, topic, self.root / topic["page"])
        self.assertIn('data-support-scope="vrchat"', body)
        self.assertIn("VRChat SDK components", body)
        self.assertIn("Avatar Descriptor", body)
        self.assertIn("PhysBone", body)
        self.assertNotIn('data-framework="ma"', body)
        self.assertNotIn('data-framework="vrcfury"', body)
        self.assertNotIn("MA + VRCFury support", body)
        self.assertIn("data-field-comparison", body)
        for identifier, scope in [("modular-avatar", "ma"), ("vrcfury", "vrcfury")]:
            topic = self.topics[identifier]
            framework = render_answer(self.root, topic, self.root / topic["page"])
            self.assertIn(f'data-support-scope="{scope}"', framework)
            self.assertNotIn('data-framework="vrchat"', framework)

    def test_legal_topics_keep_complete_prose(self):
        for identifier in ["license", "notices"]:
            topic = self.topics[identifier]
            self.assertNotIn("visual-reference", render_answer(self.root, topic, self.root / topic["page"]))

    def test_view_switcher_only_exists_for_multiple_media(self):
        for identifier, original in self.guides.items():
            guide = {key: value for key, value in original.items() if key != "composition"}
            topic = self.topics[identifier]
            result = render_visual_guide(self.root, topic, self.root / topic["page"], "Reference", guide)
            if identifier not in {"modular-avatar", "vrcfury"}:
                with self.subTest(identifier=identifier):
                    self.assertEqual('aria-label="Views"' in result, len(guide.get("media", [])) > 1)

    def test_generic_diagrams_do_not_repeat_across_material_guides(self):
        for identifier in ["packages", "standard", "liltoon", "poiyomi"]:
            with self.subTest(identifier=identifier):
                self.assertNotIn("diagram", self.guides[identifier])

    def test_invalid_guides_fail(self):
        for change in [
            {"steps": [{}, {}, {}, {}]}, {"diagram": "invented"},
            {"media": [{"image": "missing.png"}]},
            {"media": [{"image": "../../README.md"}]},
            {"pairs": [{"state": "invented"}]},
        ]:
            with self.subTest(change=change), tempfile.TemporaryDirectory() as temporary:
                path = Path(temporary) / "guides.json"
                path.write_text(json.dumps({"guides": {"test": {"lead": "Test", **change}}}), encoding="utf-8")
                with self.assertRaises(ValueError):
                    load_visual_guides(path)

    def test_pending_captures_stay_in_metadata_not_rendered_pages(self):
        expected = {"clothing", "materials", "standard", "liltoon", "poiyomi", "modular-avatar", "vrcfury", "blocked"}
        self.assertEqual({identifier for identifier, guide in self.guides.items() if guide.get("capture")}, expected)
        for identifier, guide in self.guides.items():
            with self.subTest(identifier=identifier):
                topic = self.topics[identifier]
                result = render_visual_guide(self.root, topic, self.root / topic["page"], "Reference", guide)
                self.assertNotIn('class="visual-capture', result)
                self.assertNotIn('Capture pending', result)
                self.assertNotIn('Planned capture:', result)
                self.assertIn('Reference', result)
                if identifier in expected:
                    for media in guide.get("media", []):
                        self.assertIn(media.get("image") or media["video"], result)
                    if guide.get("diagram"):
                        self.assertIn(render_diagram(guide["diagram"], lambda value: value).split('>')[0], result)

    def test_capture_metadata_is_validated_but_not_published(self):
        for capture in [{}, {"title": "Test"}, {"title": " ", "caption": "Test"}, "invalid"]:
            with self.subTest(capture=capture), tempfile.TemporaryDirectory() as temporary:
                path = Path(temporary) / "guides.json"
                path.write_text(json.dumps({"guides": {"test": {"lead": "Test", "capture": capture}}}), encoding="utf-8")
                with self.assertRaises(ValueError):
                    load_visual_guides(path)
        topic = self.topics["clothing"]
        result = render_visual_guide(self.root, topic, self.root / topic["page"], "Reference", {"lead": "Test", "capture": {"title": '<script>unsafe</script>', "caption": '<img src="unsafe">'}})
        self.assertNotIn('<script>', result)
        self.assertNotIn('<img src="unsafe">', result)
        self.assertNotIn('unsafe', result)
        self.assertNotIn('class="visual-guide"', result)

    def test_diagrams_use_correct_install_locations(self):
        result = render_diagram("install", lambda value: value)
        self.assertIn('<span>rml_mods/</span><code>UnityPackageImporter.dll', result)
        self.assertIn('<span>rml_libs/</span><code>YamlDotNet.dll', result)

    def test_editorial_text_is_escaped(self):
        topic = self.topics["clothing"]
        result = render_visual_guide(self.root, topic, self.root / topic["page"], "Reference", {"lead": "<script>unsafe</script>"})
        self.assertNotIn("<script>", result)
        self.assertIn("&lt;script&gt;", result)


if __name__ == "__main__":
    unittest.main()
