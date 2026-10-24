import html
import json
import re
from collections import Counter
from pathlib import Path
from urllib.parse import quote

from release_baseline import load_release_baseline

HERE = Path(__file__).resolve().parent
LABELS = {"supported": "Supported", "limited": "Partial support", "missing": "Not supported", "not-applicable": "Not applicable", "legacy": "Legacy"}
WORK = {"unplanned": "Not scheduled", "planned": "Planned", "in-progress": "In progress", "blocked": "Blocked"}


def load_capabilities(path=HERE / "capabilities.json"):
    data = json.loads(path.read_text(encoding="utf-8"))
    seen = set()
    for entry in data["entries"]:
        if entry["id"] in seen:
            raise ValueError("Duplicate capability: " + entry["id"])
        seen.add(entry["id"])
        if entry["framework"] not in {"ma", "vrcfury", "vrchat"} or entry["status"] not in LABELS or entry["work"] not in WORK:
            raise ValueError("Invalid capability state: " + entry["id"])
        if not all(entry.get(key) for key in ["name", "type", "outcome", "reason"]):
            raise ValueError("Incomplete capability: " + entry["id"])
        if entry["status"] in {"supported", "limited"} and (len(entry["evidence"]) < 2 or not entry.get("summary")):
            raise ValueError("Support requires implementation and test evidence")
        for evidence in entry["evidence"]:
            resolved = (HERE.parent / evidence).resolve()
            if not resolved.is_relative_to(HERE.parent.resolve()) or not resolved.is_file():
                raise ValueError("Missing evidence: " + evidence)
        source = entry["source"]
        repositories = {"vrchat-community/creator-docs"} if entry["framework"] == "vrchat" else {"bdunderscore/modular-avatar", "VRCFury/VRCFury"}
        if source["repo"] not in repositories or not re.fullmatch(r"[0-9a-f]{40}", source["revision"]):
            raise ValueError("Source must use a pinned upstream revision")
        extensions = (".md", ".mdx") if entry["framework"] == "vrchat" else (".cs",)
        if not source["path"].endswith(extensions) or ".." in Path(source["path"]).parts:
            raise ValueError("Invalid upstream path")
        issue = entry.get("issue")
        if issue and not re.fullmatch(r"https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/issues/[1-9][0-9]*", issue):
            raise ValueError("Invalid issue link")
        if entry["work"] != "unplanned" and (not issue or entry["status"] in {"legacy", "not-applicable"}):
            raise ValueError("Scheduled work requires a relevant capability and real tracking issue")
    return data


def render_support_map(scope="all", data=None):
    if scope not in {"all", "ma", "vrcfury", "vrchat"}:
        raise ValueError("Unknown capability scope")
    data = data or load_capabilities(HERE / ("vrchat-capabilities.json" if scope == "vrchat" else "capabilities.json"))
    entries = [entry for entry in data["entries"] if scope == "all" or entry["framework"] == scope]
    counts = Counter(entry["status"] for entry in entries if not entry["legacy"])
    escape = html.escape
    release = load_release_baseline()
    evidence_base = "https://github.com/" + release["repository"] + "/blob/" + release["revision"] + "/"
    applicable = [entry for entry in entries if entry["status"] in {"supported", "limited", "missing"} and not entry["legacy"]]
    numbers = {entry["id"]: index for index, entry in enumerate(applicable, 1)}
    grouped_rows = {"collection": [], "not-applicable": [], "legacy": []}
    symbols = {
        "supported": '<path d="m5 8 2 2 4-4"/>',
        "limited": '<path d="M8 2a6 6 0 0 0 0 12Z" fill="currentColor" stroke="none"/>',
        "missing": '',
        "not-applicable": '<path d="m4 12 8-8"/>',
        "legacy": '<path d="M8 4v4l3 2"/>',
    }
    for entry in entries:
        source = entry["source"]
        url = "https://github.com/" + source["repo"] + "/blob/" + source["revision"] + "/" + source["path"]
        evidence = "".join('<li><a href="' + evidence_base + quote(item, safe="/") + '"><code>' + escape(item) + "</code></a></li>" for item in entry["evidence"])
        proof = '<p>Released-source implementation and regression evidence, not a live visual guarantee:</p><ul>' + evidence + '</ul>' if evidence else ''
        tracking = '<a href="' + escape(entry["issue"], quote=True) + '">Tracking issue</a>' if entry.get("issue") else 'No scheduled delivery or tracking issue.'
        work_badge = '<span class="support-work">' + WORK[entry["work"]] + "</span>" if entry["work"] != "unplanned" else ""
        boundary = '<span class="support-row__outcome">' + escape(entry["summary"]) + "</span>" if entry.get("summary") else ""
        framework = {"ma": "MA", "vrcfury": "VRCFury", "vrchat": "VRChat SDK"}[entry["framework"]]
        group = "collection" if entry["id"] in numbers else "legacy" if entry["legacy"] or entry["status"] == "legacy" else "not-applicable"
        serial = '<span class="support-row__serial">' + str(numbers[entry["id"]]).zfill(2) + '</span>' if group == "collection" else ''
        symbol = '<svg class="support-row__symbol" width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.4" aria-hidden="true"><circle cx="8" cy="8" r="6"/>' + symbols[entry["status"]] + '</svg>'
        framework_label = '<small>' + framework + '</small>' if scope == "all" else ''
        grouped_rows[group].append(f'''<details class="support-row" name="support-components-{scope}" data-support-row data-framework="{entry['framework']}" data-status="{entry['status']}" data-work="{entry['work']}">
<summary><span class="support-row__name">{serial}{framework_label}<strong>{escape(entry['name'])}</strong></span><span class="support-row__badges"><span class="support-state support-state--{entry['status']}">{symbol}{LABELS[entry['status']]}</span>{work_badge}</span>{boundary}</summary>
<div class="support-row__detail"><div class="support-row__explanation"><p>{escape(entry['outcome'])}</p><p>{escape(entry['reason'])}</p><p><strong>{WORK[entry['work']]}</strong> &mdash; {tracking}</p></div><div class="support-row__evidence">{proof}<p><a href="{escape(url, quote=True)}">Upstream source</a> &middot; <code>{escape(entry['type'])}</code></p></div></div>
</details>''')
    scope_control = '<label>Framework<select data-support-framework><option value="all">MA + VRCFury</option><option value="ma">Modular Avatar</option><option value="vrcfury">VRCFury</option></select></label>' if scope == 'all' else ''
    segments = "".join(
        '<span data-support-segment data-framework="' + entry["framework"] + '" data-status="' + entry["status"] + '"></span>'
        for entry in applicable
    )
    snapshot = ('<div class="support-collection__score"><strong><span data-support-complete>' + str(counts["supported"]) + '</span> / <span data-support-total>' + str(len(applicable)) + '</span></strong> supported'
                '<span class="support-collection__partial"><span data-support-partial>' + str(counts["limited"]) + '</span> partial</span></div>'
                '<div class="support-collection__slots" data-support-slots role="img" aria-label="' + str(counts["supported"]) + ' supported, ' + str(counts["limited"]) + ' partial, ' + str(counts["missing"]) + ' not supported" style="--support-slot-count:' + str(max(1, len(applicable))) + '">' + segments + '</div>')
    groups = '<div class="support-map__group" data-support-group="collection" role="group" aria-label="Applicable components"><div class="support-map__rows">' + ''.join(grouped_rows["collection"]) + '</div></div>'
    for group, title, description in [
        ("not-applicable", "Not applicable in Resonite", "No direct translation. These do not count toward the collection."),
        ("legacy", "Legacy components", "Older upstream models. Not part of the current collection."),
    ]:
        if grouped_rows[group]:
            groups += '<section class="support-map__group support-map__group--excluded" data-support-group="' + group + '"><h3>' + title + '</h3><p class="support-map__group-note">' + description + '</p><div class="support-map__rows">' + ''.join(grouped_rows[group]) + '</div></section>'
    heading = "<h2>VRChat SDK components</h2>" if scope == "vrchat" else "<h2>MA + VRCFury support</h2>" if scope == "all" else ""
    exclusions = '<p>Excluded framework/helper types: ' + escape(', '.join(item['type'] for item in data['excluded'])) + '.</p>' if data['excluded'] else ''
    return f'''<section class="support-map" id="component-support" data-support-map data-support-scope="{scope}" aria-label="Component support map">
{heading}

<div class="support-collection" aria-label="Component collection progress">{snapshot}</div>
<p class="support-map__caution">{escape(release["version"])} &middot; Initial appearance is not live behavior. Partial support has limits.</p>
<div class="support-map__tools" data-support-tools hidden>
<label class="support-map__search"><span class="support-sr-only">Find a component</span><input type="search" data-support-search placeholder="Find a component" autocomplete="off"></label>
{scope_control}
<label><span class="support-sr-only">Show components</span><select data-support-status><option value="current">All current</option><option value="supported">Supported</option><option value="limited">Partial support</option><option value="missing">Not supported</option><option value="not-applicable">Not applicable</option><option value="scheduled">Scheduled work</option><option value="legacy">Legacy models</option><option value="all">Everything</option></select></label>
</div>
<p class="support-map__count" data-support-count role="status" aria-live="polite"></p>
{groups}
<details class="support-map__scope"><summary>Inventory scope &amp; release</summary><p>{escape(release['version'])}, published {escape(release['published'])}. Source review: {escape(release['reviewed'])}. {escape(data['scope'])}</p><p>Unsupported does not mean planned. Statuses describe the documented release source, not newer local changes or a completed Resonite live test. Native alternatives may still need design, even where a direct port is unnecessary.</p>{exclusions}</details>
</section>'''.replace('><', '>\n<')
