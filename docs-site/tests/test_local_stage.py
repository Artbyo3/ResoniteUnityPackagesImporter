import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from docs_workspace import load_workspace, render_answer
from local_stage import HERE, MARKER, prepare_local_stage, render_local_stage


class LocalStageTests(unittest.TestCase):
    def test_release_build_excludes_development_stage_and_does_not_read_private_state(self):
        with patch.dict(os.environ, {"IMPORTER_DOCS_LOCAL_SNAPSHOT": "0"}), patch.object(Path, "read_text") as read:
            self.assertEqual(render_local_stage(), "")
            read.assert_not_called()

    def test_local_stage_separates_installed_validated_and_live_unverified(self):
        body = render_local_stage(local=True)
        for text in ["DF35C43D", "October 26, 2026", "1,471", "0 errors and 79 warnings",
                     "Installed, not live-verified", "not a public release", "Wing/tail runtime playback",
                     "Missing targets", "Private local replay", "not filtered reports"]:
            self.assertIn(text.lower(), body.lower())
        state = (HERE.parent / "docs/WORKING_STATE.md").read_text(encoding="utf-8")
        self.assertIn("DF35C43D", state)
        for private in ["C:\\Users", "S1N", "scratch/", "session.log", "sharepet_"]:
            self.assertNotIn(private, body)

    def test_japanese_stage_and_framework_pages_are_not_placeholder_text(self):
        body = render_local_stage("ja", local=True)
        self.assertIn("DF35C43D", body)
        self.assertIn("1,471", body)
        self.assertIn("\u672a\u78ba\u8a8d", body)
        for text in [body] + [(HERE / "pages/ja/reference/compatibility" / (name + ".md")).read_text(encoding="utf-8")
                             for name in ["vrcfury", "modular-avatar"]]:
            self.assertNotIn("????", text)
            self.assertNotIn("\ufffd", text)
        with self.assertRaises(ValueError):
            render_local_stage("unknown", local=True)

    def test_staging_expands_both_languages_without_modifying_sources(self):
        with tempfile.TemporaryDirectory() as temporary:
            pages = Path(temporary)
            (pages / "ja").mkdir()
            for name in ["index.md", "ja/index.md"]:
                (pages / name).write_text("# Page\n" + MARKER, encoding="utf-8")
            with patch.dict(os.environ, {"IMPORTER_DOCS_LOCAL_SNAPSHOT": "1"}):
                prepare_local_stage(pages)
            english = (pages / "index.md").read_text(encoding="utf-8")
            japanese = (pages / "ja/index.md").read_text(encoding="utf-8")
            self.assertIn("Current mod stage", english)
            self.assertIn("DF35C43D", japanese)
            self.assertNotIn("Current mod stage", japanese)
            self.assertNotIn(MARKER, english + japanese)
            self.assertIn(MARKER, (HERE / "pages/documentation/index.md").read_text(encoding="utf-8"))

    def test_release_staging_removes_marker_and_never_adds_local_claims(self):
        with tempfile.TemporaryDirectory() as temporary:
            pages = Path(temporary)
            (pages / "index.md").write_text("# Released\n" + MARKER, encoding="utf-8")
            with patch.dict(os.environ, {"IMPORTER_DOCS_LOCAL_SNAPSHOT": "0"}):
                prepare_local_stage(pages)
            self.assertEqual((pages / "index.md").read_text(encoding="utf-8"), "# Released\n")

    def test_reader_stage_is_visible_outside_collapsed_reference_only_in_local_mode(self):
        root = HERE / "pages"
        topic = next(topic for topic in load_workspace()["topics"] if topic["id"] == "compatibility")
        with patch.dict(os.environ, {"IMPORTER_DOCS_LOCAL_SNAPSHOT": "1"}):
            body = render_answer(root, topic, root / topic["page"])
        self.assertEqual(body.count("data-local-mod-stage"), 1)
        self.assertLess(body.index("data-local-mod-stage"), body.index('<details class="visual-reference"'))
        with patch.dict(os.environ, {"IMPORTER_DOCS_LOCAL_SNAPSHOT": "0"}):
            self.assertNotIn("data-local-mod-stage", render_answer(root, topic, root / topic["page"]))

    def test_overview_distinguishes_specific_native_controls_from_arbitrary_animators(self):
        body = (HERE / "pages/reference/compatibility/index.md").read_text(encoding="utf-8")
        self.assertIn("specific native wardrobe controls", body)
        self.assertIn("verification status", body)
        self.assertNotIn("runtime toggles, animation-controller behavior and contact interactions are not recreated", body)
