import json
import sys
import tempfile
import tomllib
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from docs_workspace import (
    AnswerExtractor, export_workspace, load_workspace, prepare_workspace,
    read_section, rebase_links, source_path, topic_content,
)


class WorkspaceTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def write(self, name, text):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")
        return path

    def config(self):
        return {
            "repository": "https://github.com/example/importer",
            "groups": [{"id": "start", "label": "Start here", "default": "intro"}],
            "topics": [{
                "id": "intro", "group": "start", "label": "Introduction",
                "title": "A complete answer", "page": "guide/index.md", "route": "guide/",
                "related": [], "sources": [{"file": "guide/index.md"}],
            }],
        }

    def test_source_frontmatter_and_title_are_removed(self):
        self.write("ref.md", "---\ntitle: Reference\n---\n# Reference\n\nThe complete answer.\n## Details\nFacts.")
        body, path = read_section(self.root, {"file": "ref.md"})
        self.assertTrue(body.startswith("The complete answer."))
        self.assertIn("## Details", body)
        self.assertEqual(path, self.root / "ref.md")

    def test_section_includes_children_not_the_next_section(self):
        self.write("ref.md", "# Ref\n## Install\nSteps.\n### Dependency\nLibrary.\n## Update\nOther.")
        body, _ = read_section(self.root, {"file": "ref.md", "section": "Install", "heading": "Install here"})
        self.assertTrue(body.startswith("## Install here"))
        self.assertIn("Library.", body)
        self.assertNotIn("Other.", body)

    def test_missing_or_ambiguous_section_fails(self):
        self.write("ref.md", "# Ref\n## Same\nOne.\n## Same\nTwo.")
        for section in ["Missing", "Same"]:
            with self.subTest(section=section), self.assertRaises(ValueError):
                read_section(self.root, {"file": "ref.md", "section": section})

    def test_invalid_sources_fail(self):
        self.write("image.png", "Not Markdown")
        for name in ["missing.md", "../escape.md", "image.png"]:
            with self.subTest(name=name), self.assertRaises(ValueError):
                source_path(self.root, name)

    def test_links_images_and_fragments_rebase_from_their_source(self):
        source = self.root / "reference/ref.md"
        host = self.root / "getting-started/index.md"
        text = '[Next](next.md#step) ![Image](../images/test.png) [Section](#detail) <a href="next.md?a=1&amp;b=2">Link</a> [Directory](../importing/) [External](https://example.com)'
        body = rebase_links(text, source, host)
        self.assertIn("../reference/next.md#step", body)
        self.assertIn("../images/test.png", body)
        self.assertIn("../reference/ref.md#detail", body)
        self.assertIn('href="../reference/next.md?a=1&amp;b=2"', body)
        self.assertIn("../importing/)", body)
        self.assertIn("https://example.com", body)

    def test_staging_keeps_sources_and_landing_unchanged(self):
        source = self.root / "source"
        source.mkdir()
        (source / "guide").mkdir()
        original = "# Guide\nA complete source answer."
        (source / "guide/index.md").write_text(original, encoding="utf-8")
        (source / "index.md").write_text("# Landing\nUntouched.", encoding="utf-8")
        destination = self.root / "staged"
        prepare_workspace(source, destination, self.config())
        generated = (destination / "guide/index.md").read_text(encoding="utf-8")
        self.assertIn("data-doc-workspace", generated)
        self.assertIn("A complete source answer.", generated)
        self.assertNotIn("<details", generated)
        self.assertEqual((source / "guide/index.md").read_text(encoding="utf-8"), original)
        self.assertEqual((destination / "index.md").read_text(encoding="utf-8"), "# Landing\nUntouched.")

    def test_sidebar_has_six_entry_points_with_compatibility_first(self):
        config_path = Path(__file__).resolve().parents[1] / "zensical.toml"
        config = tomllib.loads(config_path.read_text(encoding="utf-8"))["project"]
        self.assertEqual(len(config["nav"]), 6)
        self.assertEqual(config["nav"][0], {"What can I import?": "reference/compatibility/index.md"})
        self.assertNotIn("navigation.sections", config["theme"]["features"])

    def test_reader_keeps_native_navigation_without_second_app_shell(self):
        source = self.root / "source"
        source.mkdir()
        (source / "guide").mkdir()
        (source / "guide/index.md").write_text("# Guide\nAnswer.", encoding="utf-8")
        destination = self.root / "staged"
        prepare_workspace(source, destination, self.config())
        body = (destination / "guide/index.md").read_text(encoding="utf-8")
        self.assertNotIn("  - navigation", body)
        self.assertNotIn('class="docs-rail"', body)
        self.assertIn("data-doc-find hidden", body)
        self.assertNotIn("data-doc-topics", body)
        self.assertNotIn("data-doc-related", body)

    def test_legacy_related_metadata_does_not_add_content_navigation(self):
        source = self.root / "source"
        source.mkdir()
        (source / "guide").mkdir()
        (source / "guide/index.md").write_text("# Guide\nAnswer.", encoding="utf-8")
        config = self.config()
        config["topics"][0]["related"] = ["intro"]
        destination = self.root / "staged"
        prepare_workspace(source, destination, config)
        body = (destination / "guide/index.md").read_text(encoding="utf-8")
        self.assertNotIn("data-doc-topic=", body)
        self.assertNotIn("Related tasks", body)
        self.assertIn("Answer.", body)
        self.assertNotIn("View documentation source", body)
        self.assertNotIn("data-doc-source", body)

    def test_extractor_preserves_nested_sections_and_entities(self):
        body = '<main>\n<section data-doc-answer="intro"><h1>Answer</h1><section><p>A &amp; B</p></section></section>\n</main>'
        answers = AnswerExtractor(body).answers
        self.assertEqual(answers["intro"], '<section data-doc-answer="intro"><h1>Answer</h1><section><p>A &amp; B</p></section></section>')

    def test_extractor_rejects_duplicate_or_unclosed_answers(self):
        for body in ['<section data-doc-answer="intro">', '<section data-doc-answer="intro"></section><section data-doc-answer="intro"></section>']:
            with self.subTest(body=body), self.assertRaises(ValueError):
                AnswerExtractor(body)

    def test_export_requires_rendered_answers(self):
        site = self.root / "site"
        path = site / "guide/index.html"
        path.parent.mkdir(parents=True)
        path.write_text('<section data-doc-answer="intro"><h1>Answer</h1><p>Complete.</p></section>', encoding="utf-8")
        export_workspace(site, self.config())
        payload = json.loads((site / "assets/docs-workspace.json").read_text(encoding="utf-8"))
        self.assertIn("Complete.", payload["topics"][0]["html"])
        self.assertNotIn("sources", payload["topics"][0])
        path.write_text('<section data-doc-answer="intro" markdown="1">Raw</section>', encoding="utf-8")
        with self.assertRaises(ValueError):
            export_workspace(site, self.config())

    def test_every_configured_answer_resolves_to_real_content(self):
        config = load_workspace()
        root = Path(__file__).resolve().parents[1] / "pages"
        for topic in config["topics"]:
            with self.subTest(topic=topic["id"]):
                body = topic_content(root, topic, root / topic["page"])
                self.assertGreater(len(body), 100)
                self.assertNotIn("<docs-inline", body)
                if not topic.get("capabilities"):
                    self.assertNotIn("<details", body)
                else:
                    self.assertIn("data-support-map", body)

    def test_invalid_configuration_fails(self):
        config = self.write("invalid.toml", 'groups = [{id="start", label="Start", default="absent"}]\ntopics = []')
        with self.assertRaises(ValueError):
            load_workspace(config)


if __name__ == "__main__":
    unittest.main()
