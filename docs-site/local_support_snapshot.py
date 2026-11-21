import copy
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
CATALOG = HERE.parent / "docs/AVATAR_COMPONENT_COVERAGE.json"
AUDIT_CONCERNS = {
    "Modes": "Independent Boolean controls do not establish exclusive mode selection.",
    "Puppet": "Endpoint bindings do not establish authored stop, axis or continuous puppet semantics.",
    "Toes": "The synthetic Toes_Curl property is not a resolved native toe-bone adapter.",
    "FxFloatAction": "Shape-effect bindings do not establish Animator float-parameter behavior.",
    "MaterialPropertyAction": "Shape-effect bindings do not establish native material-property behavior.",
}
LOCAL_STATUSES = {
    "supported": "supported", "partial": "limited", "indexed": "missing",
    "missing": "missing", "excluded": "not-applicable",
}


def local_support_snapshot(data, catalog_path=CATALOG):
    catalog = json.loads(catalog_path.read_text(encoding="utf-8"))
    if catalog["schemaVersion"] != 1:
        raise ValueError("Unsupported local component catalog schema")
    entries = [entry for group in catalog["groups"] for entry in group["entries"]]
    by_type = {entry["name"]: entry for entry in entries}
    if len(by_type) != len(entries):
        raise ValueError("Duplicate local component type")
    result = copy.deepcopy(data)
    if any(entry["framework"] == "vrcfury" for entry in result["entries"]):
        actions = next(group for group in catalog["groups"] if group["id"] == "vrcf-actions")
        for entry in actions["entries"]:
            result["entries"].append({
                "id": "vrcfury-action-" + entry["name"].lower(),
                "name": entry["name"], "type": entry["name"], "framework": "vrcfury",
                "outcome": "Preserve the authored action outcome using faithful native behavior.",
                "legacy": False,
                "source": {"repo": "VRCFury/VRCFury", "revision": actions["revision"], "path": entry["source"]},
            })
    for entry in result["entries"]:
        name = {"VRCPipelineManager": "PipelineManager"}.get(entry["type"], entry["type"])
        if name not in by_type:
            raise ValueError("No local component evidence for " + entry["type"])
        local = by_type[name]
        entry["status"] = LOCAL_STATUSES[local["status"]]
        entry["summary"] = local["reason"]
        entry["reason"] = local["reason"]
        entry["evidence"] = list(dict.fromkeys(local["evidence"] + local["tests"]))
        for evidence in entry["evidence"]:
            resolved = (HERE.parent / evidence).resolve()
            if not resolved.is_relative_to(HERE.parent.resolve()) or not resolved.is_file():
                raise ValueError("Missing local evidence: " + evidence)
        entry["work"] = "unplanned"
        entry["issue"] = None
        entry["runtime_note"] = (
            "Native adapter recorded in the local catalog; live behavior is unverified."
            if local["runtime"] else "No native runtime support credited by the local catalog."
        )
        if name in AUDIT_CONCERNS:
            entry["status"] = "re-evaluation"
            entry["summary"] = "Semantic audit pending: " + AUDIT_CONCERNS[name]
            entry["reason"] = entry["summary"] + " Existing code/tests do not certify faithful behavior."
            entry["runtime_note"] = "Native behavior credit withheld pending semantic review."
        if local["status"] != "excluded":
            entry["legacy"] = False
    result["local_snapshot"] = True
    result["scope"] = (
        "Local working-tree snapshot, including uncommitted changes; unreleased and not live-tested. "
        "Existing card IDs are retained. MA/VRCFury inventories add all 19 VRCFury action cards in local mode. "
        "Card counts differ from the concrete-type coverage catalog; semantic-audit concerns receive no support credit. "
        "Missing legacy built outcomes remain conversion targets. "
        "Whole-avatar fidelity, mixed-source composition and lifecycle acceptance remain unverified."
    )
    return result
