import copy
import sys
import unittest
from html.parser import HTMLParser
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from docs_compositions import validate_composition
from docs_workspace import load_workspace, render_answer
from visual_guides import load_visual_guides


class CompositionParser(HTMLParser):
    def __init__(self, body):
        super().__init__()
        self.headings = []
        self.images = []
        self.feed(body)

    def handle_starttag(self, tag, attributes):
        attributes = dict(attributes)
        if tag == "h1":
            self.headings.append(attributes.get("id"))
        if tag == "img":
            self.images.append(attributes)


class CompositionTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(__file__).resolve().parents[1] / "pages"
        self.guides = load_visual_guides()
        self.topics = {topic["id"]: topic for topic in load_workspace()["topics"]}

    def render(self, identifier):
        topic = self.topics[identifier]
        return render_answer(self.root, topic, self.root / topic["page"])

    def test_prototype_is_limited_to_three_distinct_views(self):
        composed = {identifier for identifier, guide in self.guides.items() if guide.get("composition")}
        self.assertEqual(composed, {"avatars", "compatibility", "missing-materials"})
        self.assertEqual({self.guides[identifier]["composition"]["kind"] for identifier in composed}, {"avatar", "compatibility", "diagnostic"})

    def test_composed_answers_have_one_routable_title(self):
        for identifier in ["avatars", "compatibility", "missing-materials"]:
            body = self.render(identifier)
            parsed = CompositionParser(body)
            self.assertEqual(parsed.headings, ["answer-title-" + identifier])
            self.assertIn("docs-answer--composed", body)
            self.assertNotIn('class="visual-steps"', body)
            self.assertNotIn('class="visual-facts"', body)

    def test_real_images_have_alternatives_and_resolve(self):
        for identifier in ["avatars", "compatibility", "missing-materials"]:
            topic = self.topics[identifier]
            for image in CompositionParser(self.render(identifier)).images:
                self.assertTrue(image.get("alt"))
                path = (self.root / topic["page"]).parent / image["src"]
                self.assertTrue(path.is_file(), str(path))

    def test_essential_boundaries_and_fixes_are_not_disclosed(self):
        for identifier in ["avatars", "compatibility", "missing-materials"]:
            primary, reference = self.render(identifier).split('<details class="visual-reference"', 1)
            self.assertIn(self.guides[identifier]["composition"]["verdict"]["title"], primary)
            self.assertTrue(reference)
        primary = self.render("missing-materials").split('<details class="visual-reference"', 1)[0]
        self.assertIn("Add the matching package.", primary)
        self.assertIn("Check the conversion.", primary)
        self.assertIn("world content", self.render("avatars").split('<details class="visual-reference"', 1)[0])

    def test_comparison_fallback_is_in_label_order(self):
        primary = self.render("compatibility").split('<details class="visual-reference"', 1)[0]
        self.assertLess(primary.index('class="field-comparison__source"'), primary.index('class="field-comparison__result"'))
        self.assertIn('aria-label="Compare source and Resonite appearance"', primary)
        self.assertIn('aria-valuetext="50 percent source appearance"', primary)
        self.assertIn('value="50"', primary)

    def test_invalid_composition_fails_validation(self):
        original = self.guides["avatars"]["composition"]
        for mutation in [{"kind": "unknown"}, {"image": ""}, {"workflow": []}, {"verdict": {}}]:
            value = copy.deepcopy(original)
            value.update(mutation)
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                validate_composition(value)


if __name__ == "__main__":
    unittest.main()
