import argparse
import json
import os
import re
import sys
import tempfile
from datetime import datetime, timezone
from pathlib import Path
from urllib.error import URLError
from urllib.parse import urlencode
from urllib.request import Request, urlopen

HERE = Path(__file__).resolve().parent
PRIORITIES = {"working": 1, "problem": 2, "testing": 3, "planned": 4}


class StatusSyncError(ValueError):
    pass


def load_config(path=HERE / "project-status-config.json"):
    config = json.loads(path.read_text(encoding="utf-8"))
    if config["repository"] != "Artbyo3/ResoniteUnityPackagesImporter":
        raise StatusSyncError("Unexpected issue repository")
    if not re.fullmatch(r"[a-z][a-z0-9:-]+", config["publish_label"]):
        raise StatusSyncError("Invalid publication label")
    if set(config["status_labels"].values()) != set(PRIORITIES) or len(config["status_labels"]) != 4:
        raise StatusSyncError("Every public status needs one distinct label")
    if config["publish_label"] in config["status_labels"]:
        raise StatusSyncError("Publication approval must be separate from work status")
    return config


def parse_timestamp(value):
    if not isinstance(value, str):
        raise StatusSyncError("Missing issue timestamp")
    parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    if parsed.tzinfo is None:
        raise StatusSyncError("Issue timestamp must include its timezone")
    return parsed


def public_text(value, limit):
    if not isinstance(value, str) or not value.strip() or value.strip() == "_No response_":
        raise StatusSyncError("Missing public issue copy")
    if any(marker in value for marker in ["```", "<", ">"]):
        raise StatusSyncError("Public fields must be plain text, not reports or markup")
    text = " ".join(value.split())
    return text if len(text) <= limit else text[:limit - 1].rstrip() + "\u2026"


def public_field(body, heading):
    if not isinstance(body, str):
        raise StatusSyncError("Missing public issue description")
    pattern = r"^### " + re.escape(heading) + r"[ \t]*\r?$\n(.*?)(?=^#{1,6} |\Z)"
    matches = re.findall(pattern, body, re.MULTILINE | re.DOTALL)
    if len(matches) != 1:
        raise StatusSyncError("Public update requires one " + heading + " section")
    return public_text(matches[0], 280)


def fetch_issues(config, token=None, opener=urlopen):
    items = []
    headers = {"Accept": "application/vnd.github+json", "X-GitHub-Api-Version": "2022-11-28",
               "User-Agent": "UnityPackageImporter-Documentation"}
    if token:
        headers["Authorization"] = "Bearer " + token
    for page in range(1, 51):
        query = urlencode({"state": "open", "labels": config["publish_label"],
                           "per_page": 100, "page": page, "sort": "updated", "direction": "desc"})
        request = Request("https://api.github.com/repos/" + config["repository"] + "/issues?" + query,
                          headers=headers)
        with opener(request, timeout=20) as response:
            batch = json.load(response)
        if not isinstance(batch, list):
            raise StatusSyncError("Unexpected GitHub issue response")
        items.extend(batch)
        if len(batch) < 100:
            return items
    raise StatusSyncError("Issue feed exceeds the pagination limit; refusing a partial snapshot")


def create_snapshot(items, config, now=None):
    records = []
    seen = set()
    for item in items:
        if not isinstance(item, dict):
            raise StatusSyncError("Invalid issue record")
        if "pull_request" in item or item.get("state") != "open":
            continue
        labels = {label.get("name") for label in item.get("labels", []) if isinstance(label, dict)}
        if config["publish_label"] not in labels:
            continue
        number = item.get("number")
        if type(number) is not int or number < 1 or number in seen:
            raise StatusSyncError("Invalid or duplicate issue number")
        seen.add(number)
        statuses = [status for label, status in config["status_labels"].items() if label in labels]
        if len(statuses) != 1:
            raise StatusSyncError(f"Issue #{number} requires exactly one public status label")
        source = "https://github.com/" + config["repository"] + "/issues/" + str(number)
        if item.get("html_url") != source:
            raise StatusSyncError("Issue URL does not match the configured repository")
        parse_timestamp(item.get("updated_at"))
        records.append({"id": str(number), "title": public_text(item.get("title"), 140),
                        "status": statuses[0], "why": public_field(item.get("body"), "Why it matters"),
                        "next": public_field(item["body"], "Next step"),
                        "priority": PRIORITIES[statuses[0]], "updatedAt": item["updated_at"], "url": source})
    records.sort(key=lambda item: (item["priority"], -parse_timestamp(item["updatedAt"]).timestamp(), int(item["id"])))
    now = now or datetime.now(timezone.utc)
    if now.tzinfo is None:
        raise StatusSyncError("Snapshot timestamp must include its timezone")
    query = urlencode({"q": "is:issue is:open label:" + config["publish_label"]})
    return {"state": "available", "updatedAt": now.astimezone(timezone.utc).isoformat().replace("+00:00", "Z"),
            "source": {"repository": config["repository"],
                       "url": "https://github.com/" + config["repository"] + "/issues?" + query},
            "issues": records}


def write_snapshot(path, snapshot):
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=path.parent,
                                     prefix=".project-status-", suffix=".json", delete=False) as output:
        output.write(json.dumps(snapshot, ensure_ascii=False, indent=2) + "\n")
        temporary = Path(output.name)
    try:
        temporary.replace(path)
    finally:
        temporary.unlink(missing_ok=True)


def sync_status(output, config=None, token=None, opener=urlopen, now=None):
    config = config or load_config()
    snapshot = create_snapshot(fetch_issues(config, token, opener), config, now)
    write_snapshot(output, snapshot)
    return snapshot


def main():
    parser = argparse.ArgumentParser(description="Build a curated public GitHub issue snapshot for the docs.")
    parser.add_argument("--output", type=Path, default=HERE / "site/data/project-status.json")
    arguments = parser.parse_args()
    try:
        snapshot = sync_status(arguments.output, token=os.environ.get("GH_TOKEN"))
    except (URLError, ValueError, OSError, KeyError, TypeError) as error:
        detail = str(error) if isinstance(error, StatusSyncError) else type(error).__name__
        print("Project-status sync failed: " + detail + ". Existing snapshot preserved; publication must stop.", file=sys.stderr)
        return 1
    print(f"Synced {len(snapshot['issues'])} curated open GitHub issues at {snapshot['updatedAt']}.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
