import hashlib
import importlib.util
import json
from pathlib import Path
import re
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ElementTree


ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("resolute_manifest", ROOT / "scripts/Prepare-ResoluteManifest.py")
MANIFEST = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MANIFEST)
VERSION = ElementTree.parse(ROOT / "UnityPackageImporter/UnityPackageImporter.csproj").findtext("PropertyGroup/Version")


class ResoluteDistributionTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.artifacts = self.root / "release-assets"
        self.artifacts.mkdir()
        self.output = self.root / "submission"
        checksums = []
        for filename, _, _ in MANIFEST.ARTIFACTS:
            content = f"fixture for {filename}".encode()
            (self.artifacts / filename).write_bytes(content)
            checksums.append(f"{hashlib.sha256(content).hexdigest()}  {filename}")
        (self.artifacts / "SHA256SUMS.txt").write_text("\n".join(checksums), encoding="utf-8-sig")

    def prepare(self, version=VERSION, tag=None):
        return MANIFEST.prepare_submission(self.artifacts, self.output, version, tag)

    def test_artifacts_have_exact_hashes_locations_and_pinned_urls(self):
        entry = json.loads(self.prepare().read_text(encoding="utf-8"))
        self.assertEqual(entry["id"], "Artbyo3.UnityPackageImporter")
        self.assertEqual(entry["category"], "Asset Importing")
        self.assertEqual(entry["platforms"], ["windows"])
        release = entry["versions"][VERSION]
        self.assertEqual(release["conflicts"], {"net.dfgHiatus.UnityPackageImporter": {"version": "*"}})
        self.assertEqual(len(release["artifacts"]), 4)
        for artifact, (filename, location, installed_name) in zip(release["artifacts"], MANIFEST.ARTIFACTS):
            self.assertEqual(artifact["installLocation"], location)
            self.assertEqual(artifact["filename"], installed_name)
            self.assertEqual(artifact["sha256"], hashlib.sha256((self.artifacts / filename).read_bytes()).hexdigest())
            self.assertEqual(artifact["url"], f"{entry['sourceLocation']}/releases/download/v{VERSION}/{filename}")
            self.assertNotIn("latest", artifact["url"])

    def test_author_and_contributor_credits_are_preserved(self):
        entry = json.loads(self.prepare().read_text(encoding="utf-8"))
        author = json.loads((self.output / "manifest/Artbyo3/author.json").read_text(encoding="utf-8"))
        self.assertEqual(author["author"]["Artbyo3"]["url"], "https://github.com/Artbyo3")
        self.assertIn("dfgHiatus", entry["additionalAuthors"])
        self.assertIn("dfgHiatus", (ROOT / "LICENSE").read_text(encoding="utf-8"))

    def test_identity_and_versions_match_the_compiled_sources(self):
        metadata = (ROOT / "UnityPackageImporter/ModMetadata.cs").read_text(encoding="utf-8")
        constants = dict(re.findall(r'internal const string (\w+) = "([^"]+)";', metadata))
        template = json.loads((ROOT / "distribution/resolute/info.template.json").read_text(encoding="utf-8"))
        self.assertEqual(constants["Id"], template["id"])
        self.assertEqual(constants["Version"], VERSION)
        self.assertEqual(constants["Maintainer"], "Artbyo3")
        self.assertEqual(
            constants["Author"].split(", "),
            ["dfgHiatus", "eia485", "delta", "Frozenreflex", "benaclejames", "989onan", "Artbyo3"],
        )
        self.assertEqual(constants["Author"].split(", "), template["additionalAuthors"] + [constants["Maintainer"]])
        self.assertEqual(constants["Repository"], template["sourceLocation"])
        source = (ROOT / "UnityPackageImporter/UnityPackageImporter.cs").read_text(encoding="utf-8")
        self.assertIn("new Harmony(ModMetadata.Id)", source)
        self.assertIn("Author => ModMetadata.Author", source)
        self.assertIn("Version => ModMetadata.Version", source)
        assembly = (ROOT / "UnityPackageImporter/Properties/AssemblyInfo.cs").read_text(encoding="utf-8")
        self.assertIn("AssemblyCompany(UnityPackageImporter.ModMetadata.Maintainer)", assembly)
        self.assertIn("AssemblyVersion(UnityPackageImporter.ModMetadata.Version)", assembly)
        self.assertIn("AssemblyFileVersion(UnityPackageImporter.ModMetadata.Version)", assembly)

    def test_invalid_versions_and_mismatched_tags_are_rejected(self):
        for version in ("../escape", "01.0.0", "1x0x0", "1.0", "1.0.0-beta", "999.0.0"):
            with self.subTest(version=version), self.assertRaises(ValueError):
                self.prepare(version=version)
        for tag in ("latest", "v999.0.0", "../../escape"):
            with self.subTest(tag=tag), self.assertRaises(ValueError):
                self.prepare(tag=tag)
        self.assertFalse(self.output.exists())

    def test_missing_empty_and_modified_artifacts_are_rejected(self):
        source = self.artifacts / "YamlDotNet.dll"
        for content in (b"", b"modified"):
            source.write_bytes(content)
            with self.assertRaises(ValueError):
                self.prepare()
        source.unlink()
        with self.assertRaises(ValueError):
            self.prepare()
        self.assertFalse(self.output.exists())

    def test_missing_or_duplicate_checksums_are_rejected(self):
        checksums = self.artifacts / "SHA256SUMS.txt"
        original = checksums.read_text(encoding="utf-8-sig")
        checksums.write_text(original + "\n" + original.splitlines()[0], encoding="utf-8")
        with self.assertRaises(ValueError):
            self.prepare()
        checksums.write_text("", encoding="utf-8")
        with self.assertRaises(ValueError):
            self.prepare()
        self.assertFalse(self.output.exists())

    def test_repeated_preparation_is_deterministic(self):
        first = self.prepare().read_bytes()
        self.assertEqual(self.prepare().read_bytes(), first)

    def test_unprefixed_version_tag_is_supported(self):
        entry = json.loads(self.prepare(tag=VERSION).read_text(encoding="utf-8"))
        self.assertTrue(entry["versions"][VERSION]["releaseUrl"].endswith(f"/tag/{VERSION}"))


if __name__ == "__main__":
    unittest.main(testRunner=unittest.TextTestRunner(stream=sys.stdout))
