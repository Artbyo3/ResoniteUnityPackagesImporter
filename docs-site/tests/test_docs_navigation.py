import sys
import tomllib
import unittest
from html.parser import HTMLParser
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from docs_navigation import is_section_link, strip_section_links
from docs_workspace import load_workspace, render_answer
from visual_guides import render_visual_guide


class LinkParser(HTMLParser):
    def __init__(self, body):
        super().__init__()
        self.links = []
        self.feed(body)

    def handle_starttag(self, tag, attributes):
        if tag == "a":
            self.links.append(dict(attributes).get("href", ""))


class DocumentationNavigationTests(unittest.TestCase):
    def test_section_links_are_distinct_from_assets_and_external_resources(self):
        for target in ["next.md", "../next/", "/reference/guide/", "index.html#details", "next%2Emd", "next/?query=test"]:
            with self.subTest(target=target):
                self.assertTrue(is_section_link(target))
        for target in ["#demo", "images/example.png", "assets/demo.mp4", "build.zip", "https://example.com/guide/", "//example.com/", "mailto:help@example.com"]:
            with self.subTest(target=target):
                self.assertFalse(is_section_link(target))

    def test_markdown_cross_links_become_readable_text(self):
        original = '[**Materials**](shaders/index.md) and [Settings](../install.md#configuration){ .md-button }.'
        self.assertEqual(strip_section_links(original), '**Materials** and Settings.')
        retained = '![Preview](images/test.png) [Watch](#demo) [Download](build.zip) [Source](https://example.com/source)'
        self.assertEqual(strip_section_links(retained), retained)

    def test_raw_html_links_preserve_content_and_external_attributes(self):
        original = '<a class="next" href="../next/"><strong>A &amp; B</strong></a> <a href="https://example.com" target="_blank">Source</a>'
        self.assertEqual(strip_section_links(original), '<strong>A &amp; B</strong> <a href="https://example.com" target="_blank">Source</a>')
        self.assertEqual(strip_section_links('<a href="next.md">One</a>\n<a href="other.html">Two</a>'), 'One\nTwo')

    def test_fenced_examples_are_not_rewritten(self):
        for delimiter in ["```", "~~~"]:
            original = delimiter + 'md\n[Example](next.md) <a href="next/">Example</a>\n' + delimiter
            self.assertEqual(strip_section_links(original), original)

    def test_every_answer_has_no_cross_section_links(self):
        root = Path(__file__).resolve().parents[1] / "pages"
        for topic in load_workspace()["topics"]:
            with self.subTest(topic=topic["id"]):
                body = render_answer(root, topic, root / topic["page"])
                self.assertEqual([link for link in LinkParser(body).links if is_section_link(link)], [])
                self.assertNotIn('class="field-links"', body)
                self.assertNotIn('data-doc-inline', body)

    def test_action_renderer_keeps_downloads_but_not_internal_jumps(self):
        root = Path(__file__).resolve().parents[1] / "pages"
        topic = {"id": "test", "page": "index.md", "route": ""}
        guide = {"lead": "Test", "actions": [{"label": "Jump", "target": "next.md"}, {"label": "Download", "target": "https://example.com/build.zip"}]}
        body = render_visual_guide(root, topic, root / "index.md", "Reference", guide)
        self.assertNotIn('>Jump</a>', body)
        self.assertIn('href="https://example.com/build.zip"', body)

    def test_every_topic_is_reachable_in_sidebar_order(self):
        root = Path(__file__).resolve().parents[1]
        config = tomllib.loads((root / "zensical.toml").read_text(encoding="utf-8"))
        def destinations(value):
            if isinstance(value, str):
                return [value]
            if isinstance(value, list):
                return [target for item in value for target in destinations(item)]
            return [target for item in value.values() for target in destinations(item)]
        sidebar = destinations(config["project"]["nav"])
        topics = load_workspace()["topics"]
        expected = [
            topic["route"] + "#" + topic["anchor"] if topic.get("anchor") else topic["page"]
            for topic in topics
        ]
        self.assertTrue(set(expected).issubset(sidebar))
        for page in {topic["page"] for topic in topics}:
            route = next(topic["route"] for topic in topics if topic["page"] == page)
            positions = [sidebar.index(target) for target in expected if target.split("#", 1)[0] in {page, route}]
            self.assertEqual(positions, sorted(positions))



if __name__ == "__main__":
    unittest.main()
