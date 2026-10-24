import json
import re
import subprocess
from datetime import date
from pathlib import Path

HERE = Path(__file__).resolve().parent


def load_release_baseline(path=HERE / "release-baseline.json"):
    data = json.loads(path.read_text(encoding="utf-8"))
    if not re.fullmatch(r"v[0-9]+\.[0-9]+\.[0-9]+", data["version"]):
        raise ValueError("Invalid documentation release version")
    if not re.fullmatch(r"[0-9a-f]{40}", data["revision"]):
        raise ValueError("Release evidence must use a full commit revision")
    if data["repository"] != "Artbyo3/ResoniteUnityPackagesImporter":
        raise ValueError("Unexpected release repository")
    if data["verification"] != "source-reviewed" or data["live_test"] not in {"pending", "verified"}:
        raise ValueError("Invalid release verification state")
    for field in ["published", "reviewed"]:
        date.fromisoformat(data[field])
    if data["archive"] != "UnityPackageImporter-" + data["version"] + ".zip":
        raise ValueError("Download archive must match the documented release")
    return data


def audit_release_evidence():
    baseline = load_release_baseline()
    evidence = set()
    for name in ["capabilities.json", "vrchat-capabilities.json"]:
        data = json.loads((HERE / name).read_text(encoding="utf-8"))
        evidence.update(path for entry in data["entries"] for path in entry["evidence"])
    for path in sorted(evidence):
        result = subprocess.run(["git", "cat-file", "-e", baseline["revision"] + ":" + path],
                                cwd=HERE.parent, capture_output=True, text=True)
        if result.returncode:
            raise ValueError("Evidence absent from documented release: " + path)
    print(f"{baseline['version']}: {len(evidence)} evidence files exist at {baseline['revision']}. Source review only; not a live import verification.")


if __name__ == "__main__":
    audit_release_evidence()
