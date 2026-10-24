import io
import json
import sys
import tempfile
import unittest
from datetime import datetime, timezone
from pathlib import Path
from urllib.error import HTTPError
from urllib.parse import parse_qs, urlsplit

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from sync_project_status import create_snapshot, fetch_issues, load_config, public_field, sync_status

HERE = Path(__file__).resolve().parents[1]
NOW = datetime(2026, 10, 24, 12, tzinfo=timezone.utc)


def issue(number=32, status="status:known-issue", **changes):
    item = {"number": number, "state": "open", "title": "Outfit installation",
            "html_url": f"https://github.com/Artbyo3/ResoniteUnityPackagesImporter/issues/{number}",
            "labels": [{"name": "project-status"}, {"name": status}],
            "updated_at": "2026-10-23T12:00:00Z",
            "body": "### Why it matters\nClothing should attach reliably.\n\n### Next step\nReproduce the reported installation failure.\n\n### Private report\nDo not publish this log."}
    return {**item, **changes}


class ProjectStatusSyncTests(unittest.TestCase):
    def setUp(self):
        self.config = load_config()

    def test_explicit_statuses_map_to_the_existing_ui(self):
        for label, status in self.config["status_labels"].items():
            with self.subTest(label=label):
                result = create_snapshot([issue(status=label)], self.config, NOW)
                self.assertEqual(result["issues"][0]["status"], status)
                self.assertEqual(result["source"]["repository"], self.config["repository"])

    def test_no_curation_no_publication_and_no_closed_items_or_pull_requests(self):
        records = [issue(labels=[{"name": "bug"}]), issue(33, state="closed"),
                   issue(34, pull_request={"url": "pull-request"})]
        result = create_snapshot(records, self.config, NOW)
        self.assertEqual(result["issues"], [])
        self.assertEqual(result["state"], "available")
        self.assertEqual(result["updatedAt"], "2026-10-24T12:00:00Z")

    def test_only_selected_public_fields_leave_the_adapter(self):
        result = create_snapshot([issue()], self.config, NOW)
        record = result["issues"][0]
        self.assertEqual(record["id"], "32")
        self.assertEqual(record["why"], "Clothing should attach reliably.")
        self.assertNotIn("Private report", json.dumps(result))
        self.assertNotIn("Do not publish", json.dumps(result))
        self.assertNotIn("body", record)

    def test_editorial_fields_support_crlf_and_normalize_whitespace(self):
        text = "### Why it matters\r\nFirst sentence.\r\nSecond sentence.\r\n\r\n### Next step\r\nCheck it."
        self.assertEqual(public_field(text, "Why it matters"), "First sentence. Second sentence.")

    def test_ambiguous_status_missing_fields_markup_and_untrusted_urls_fail(self):
        cases = [issue(labels=[{"name": "project-status"}]),
                 issue(labels=[{"name": "project-status"}, {"name": "status:testing"}, {"name": "status:planned"}]),
                 issue(body="A raw bug report without public fields"),
                 issue(body="### Why it matters\n_No response_\n### Next step\nCheck."),
                 issue(body="### Why it matters\n<script>bad</script>\n### Next step\nCheck."),
                 issue(body="### Why it matters\nOne.\n### Why it matters\nTwo.\n### Next step\nCheck."),
                 issue(html_url="https://example.com/issues/32"),
                 issue(updated_at="2026-10-24T12:00:00"), issue(number=True),
                 issue(title=""), issue(body=None)]
        for item in cases:
            with self.subTest(item=item), self.assertRaises(ValueError):
                create_snapshot([item], self.config, NOW)

    def test_duplicate_records_and_naive_snapshot_time_fail(self):
        with self.assertRaises(ValueError):
            create_snapshot([issue(), issue()], self.config, NOW)
        with self.assertRaises(ValueError):
            create_snapshot([], self.config, datetime(2026, 10, 24))

    def test_public_copy_is_bounded_and_uses_real_issue_update_time(self):
        item = issue(title="A" * 200, body="### Why it matters\n" + "B" * 500 + "\n### Next step\n" + "C" * 500)
        record = create_snapshot([item], self.config, NOW)["issues"][0]
        self.assertEqual(len(record["title"]), 140)
        self.assertEqual(len(record["why"]), 280)
        self.assertEqual(len(record["next"]), 280)
        self.assertEqual(record["updatedAt"], item["updated_at"])

    def test_snapshot_order_is_stable_by_status_then_recent_update(self):
        records = [issue(3, "status:planned"), issue(2, "status:in-progress"),
                   issue(1, "status:in-progress", updated_at="2026-10-24T12:00:00Z")]
        self.assertEqual([item["id"] for item in create_snapshot(records, self.config, NOW)["issues"]], ["1", "2", "3"])

    def test_all_pages_are_read_and_token_is_sent_only_as_a_header(self):
        requests = []
        def opener(request, timeout):
            requests.append(request)
            self.assertEqual(timeout, 20)
            query = parse_qs(urlsplit(request.full_url).query)
            batch = [issue(number) for number in range(1, 101)] if query["page"] == ["1"] else [issue(101)]
            return io.BytesIO(json.dumps(batch).encode())
        records = fetch_issues(self.config, "private-test-token", opener)
        self.assertEqual(len(records), 101)
        self.assertEqual(len(requests), 2)
        self.assertEqual(requests[0].get_header("Authorization"), "Bearer private-test-token")
        self.assertNotIn("private-test-token", requests[0].full_url)
        self.assertEqual(parse_qs(urlsplit(requests[0].full_url).query)["labels"], ["project-status"])

    def test_truncated_or_nonlist_api_responses_fail(self):
        def endless(request, timeout):
            return io.BytesIO(json.dumps([issue()] * 100).encode())
        with self.assertRaises(ValueError):
            fetch_issues(self.config, opener=endless)
        with self.assertRaises(ValueError):
            fetch_issues(self.config, opener=lambda request, timeout: io.BytesIO(b'{"message":"error"}'))

    def test_http_and_editorial_errors_preserve_previous_snapshot(self):
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary) / "status.json"
            output.write_text("previous snapshot", encoding="utf-8")
            def unavailable(request, timeout):
                raise HTTPError(request.full_url, 403, "Rate limited", {}, None)
            with self.assertRaises(HTTPError):
                sync_status(output, self.config, opener=unavailable, now=NOW)
            self.assertEqual(output.read_text(), "previous snapshot")
            invalid = lambda request, timeout: io.BytesIO(json.dumps([issue(body="raw report")]).encode())
            with self.assertRaises(ValueError):
                sync_status(output, self.config, opener=invalid, now=NOW)
            self.assertEqual(output.read_text(), "previous snapshot")

    def test_successful_write_is_atomic_without_source_changes_or_secrets(self):
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary) / "data/status.json"
            response = lambda request, timeout: io.BytesIO(json.dumps([issue()]).encode())
            snapshot = sync_status(output, self.config, token="private-test-token", opener=response, now=NOW)
            self.assertEqual(json.loads(output.read_text()), snapshot)
            self.assertNotIn("private-test-token", output.read_text())
            self.assertEqual(list(output.parent.glob(".project-status-*")), [])

    def test_template_does_not_let_public_authors_approve_their_own_update(self):
        template = (HERE.parent / ".github/ISSUE_TEMPLATE/project-update.yml").read_text(encoding="utf-8")
        self.assertIn("labels: [documentation]", template)
        self.assertNotIn("labels: [project-status", template)
        for heading in ["Why it matters", "Next step"]:
            self.assertIn("label: " + heading, template)

    def test_workflow_syncs_built_output_without_writes_or_pr_secrets(self):
        workflow = (HERE.parent / ".github/workflows/docs-site.yml").read_text(encoding="utf-8")
        self.assertIn("issues: read", workflow)
        self.assertNotIn("issues: write", workflow)
        self.assertNotIn("contents: write", workflow)
        self.assertIn("github.event_name != 'pull_request'", workflow)
        self.assertIn('cron: "17 */6 * * *"', workflow)
        self.assertIn("github.event.label.name == 'project-status'", workflow)
        self.assertIn("python docs-site/sync_project_status.py", workflow)
        self.assertEqual(workflow.count("run: python build.py"), 1)
        self.assertLess(workflow.index("run: python build.py"), workflow.index("run: python docs-site/sync_project_status.py"))
        self.assertLess(workflow.index("run: python docs-site/sync_project_status.py"), workflow.index("actions/upload-pages-artifact"))


if __name__ == "__main__":
    unittest.main()
