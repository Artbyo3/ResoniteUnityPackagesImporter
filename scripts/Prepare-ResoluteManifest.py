import argparse
import hashlib
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ElementTree


ROOT = Path(__file__).resolve().parents[1]
ARTIFACTS = (
    ("UnityPackageImporter.dll", "/rml_mods", "UnityPackageImporter.dll"),
    ("YamlDotNet.dll", "/rml_libs", "YamlDotNet.dll"),
    ("LICENSE", "/", "UnityPackageImporter.LICENSE"),
    ("THIRD_PARTY_NOTICES.md", "/", "UnityPackageImporter.THIRD_PARTY_NOTICES.md"),
)


def prepare_submission(artifact_directory, output_directory, version, release_tag=None):
    if not re.fullmatch(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)", version):
        raise ValueError("The release version must have three numeric SemVer components.")
    expected_version = ElementTree.parse(
        ROOT / "UnityPackageImporter/UnityPackageImporter.csproj"
    ).findtext("PropertyGroup/Version")
    if version != expected_version:
        raise ValueError(f"Release version {version} does not match project version {expected_version}.")
    release_tag = release_tag or f"v{version}"
    if release_tag not in (version, f"v{version}"):
        raise ValueError("The release tag must equal the version, optionally prefixed with v.")
    artifact_directory = Path(artifact_directory)
    output_directory = Path(output_directory)
    checksums = {}
    for line in (artifact_directory / "SHA256SUMS.txt").read_text(encoding="utf-8-sig").splitlines():
        digest, filename = line.split(maxsplit=1)
        if not re.fullmatch(r"[a-fA-F0-9]{64}", digest) or filename in checksums:
            raise ValueError("Invalid or duplicate release checksum.")
        checksums[filename] = digest.lower()
    author = json.loads((ROOT / "distribution/resolute/author.json").read_text(encoding="utf-8"))
    entry = json.loads((ROOT / "distribution/resolute/info.template.json").read_text(encoding="utf-8"))
    release_url = f"{entry['sourceLocation']}/releases/tag/{release_tag}"
    download_url = f"{entry['sourceLocation']}/releases/download/{release_tag}"
    artifacts = []
    for filename, location, installed_filename in ARTIFACTS:
        source = artifact_directory / filename
        if not source.is_file() or source.stat().st_size == 0:
            raise ValueError(f"Missing or empty release artifact: {filename}")
        with source.open("rb") as stream:
            digest = hashlib.file_digest(stream, "sha256").hexdigest()
        if checksums.get(filename) != digest:
            raise ValueError(f"Release checksum does not match artifact: {filename}")
        artifacts.append({
            "url": f"{download_url}/{filename}",
            "filename": installed_filename,
            "sha256": digest,
            "installLocation": location,
        })
    entry["versions"] = {
        version: {
            "releaseUrl": release_url,
            "conflicts": {"net.dfgHiatus.UnityPackageImporter": {"version": "*"}},
            "artifacts": artifacts,
        }
    }
    author_directory = output_directory / "manifest/Artbyo3"
    mod_directory = author_directory / "UnityPackageImporter"
    mod_directory.mkdir(parents=True, exist_ok=True)
    (author_directory / "author.json").write_text(json.dumps(author, indent=2) + "\n", encoding="utf-8")
    manifest_path = mod_directory / "info.json"
    manifest_path.write_text(json.dumps(entry, indent=2) + "\n", encoding="utf-8")
    return manifest_path


def main():
    parser = argparse.ArgumentParser(description="Prepare a local Resolute submission; never publishes or submits it.")
    parser.add_argument("--artifacts", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--release-tag")
    arguments = parser.parse_args()
    manifest_path = prepare_submission(
        arguments.artifacts, arguments.output, arguments.version, arguments.release_tag
    )
    print(f"Prepared {manifest_path}. Release URLs must be published and verified before submission.")


if __name__ == "__main__":
    main()
