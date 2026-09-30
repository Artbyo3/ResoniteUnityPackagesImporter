"""Checks for package integrity and privacy before sealing the native UI bundle."""
import hashlib
import importlib.util
import json
import tempfile
import unittest
import zipfile
from pathlib import Path

script = Path(__file__).resolve().parents[1] / 'scripts/Prepare-StationTemplates.py'
spec = importlib.util.spec_from_file_location('station_templates', script)
bundle = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bundle)


class StationBundleTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)
        self.backups = self.directory / 'originals'
        self.payload = b'unchanged native object payload'
        self.signature = hashlib.sha256(self.payload).hexdigest()
        for name in ('station', 'pre-import'):
            record = {'id': 'R-Main', 'ownerId': 'private-account-id', 'ownerName': 'private-name',
                      'assetUri': 'packdb:///' + self.signature, 'path': '/private-folder',
                      'migrationMetadata': {'private': True},
                      'assetManifest': [{'hash': self.signature, 'bytes': len(self.payload)}]}
            self.write_package(name, record)
            (self.directory / (name + '.bindings.json')).write_text(json.dumps({
                'schemaVersion': 1, 'templateVersion': '1.0.0',
                'bindings': {'test': {'path': [], 'type': 'Slot', 'index': 0}}}))

    def write_package(self, name, record, payload=None):
        with zipfile.ZipFile(self.directory / (name + '.resonitepackage'), 'w') as archive:
            archive.writestr('Assets/' + self.signature, self.payload if payload is None else payload)
            archive.writestr('R-Main.record', json.dumps(record))

    def record(self, name='station'):
        with zipfile.ZipFile(self.directory / (name + '.resonitepackage')) as archive:
            return json.loads(archive.read('R-Main.record'))

    def test_private_records_are_cleared_without_changing_native_assets(self):
        original_hashes = [bundle.digest(p) for p in self.directory.glob('*.resonitepackage')]
        manifest = bundle.prepare(self.directory, self.backups)
        self.assertEqual(manifest['version'], '1.0.0')
        self.assertEqual(sorted(bundle.digest(p) for p in self.backups.iterdir()), sorted(original_hashes))
        for name in ('station', 'pre-import'):
            path = self.directory / (name + '.ResonitePackage')
            self.assertEqual(path.name, name + '.ResonitePackage')
            with zipfile.ZipFile(path) as archive:
                self.assertEqual(archive.read('Assets/' + self.signature), self.payload)
                record = json.loads(archive.read('R-Main.record'))
                for field in ('ownerId', 'ownerName', 'path', 'migrationMetadata'):
                    self.assertIsNone(record[field])
            self.assertEqual(bundle.digest(path), manifest['templates'][name]['packageSha256'])
        hashes = [bundle.digest(p) for p in self.directory.glob('*.ResonitePackage')]
        self.assertEqual(bundle.prepare(self.directory, self.backups), manifest)
        self.assertEqual([bundle.digest(p) for p in self.directory.glob('*.ResonitePackage')], hashes)
        self.assertEqual(len(list(self.backups.iterdir())), 2)

    def test_asset_tampering_does_not_publish_a_manifest(self):
        self.write_package('station', self.record(), b'tampered')
        with self.assertRaisesRegex(ValueError, 'checksum mismatch'):
            bundle.prepare(self.directory, self.backups)
        self.assertFalse((self.directory / 'manifest.json').exists())
        self.assertFalse(self.backups.exists())

    def test_missing_dependencies_are_rejected(self):
        record = self.record()
        record['assetManifest'].append({'hash': '0' * 64, 'bytes': 1})
        self.write_package('station', record)
        with self.assertRaisesRegex(ValueError, 'dependency is missing'):
            bundle.prepare(self.directory, self.backups)

    def test_missing_native_object_is_rejected(self):
        record = self.record()
        record['assetUri'] = 'packdb:///' + '0' * 64
        self.write_package('station', record)
        with self.assertRaisesRegex(ValueError, 'record asset is not bundled'):
            bundle.prepare(self.directory, self.backups)

    def test_mismatched_versions_leave_exports_untouched(self):
        path = self.directory / 'pre-import.bindings.json'
        contract = json.loads(path.read_text())
        contract['templateVersion'] = '2.0.0'
        path.write_text(json.dumps(contract))
        before = bundle.digest(self.directory / 'station.resonitepackage')
        with self.assertRaisesRegex(ValueError, 'same templateVersion'):
            bundle.prepare(self.directory, self.backups)
        self.assertEqual(bundle.digest(self.directory / 'station.resonitepackage'), before)
        self.assertFalse(self.backups.exists())

    def test_contract_hashes_survive_line_ending_normalization(self):
        path = self.directory / 'station.bindings.json'
        data = json.loads(path.read_text())
        expected = (json.dumps(data, indent=2) + '\n').encode('utf-8')
        path.write_bytes(expected.replace(b'\n', b'\r\n'))
        manifest = bundle.prepare(self.directory, self.backups)
        self.assertEqual(path.read_bytes(), expected)
        self.assertEqual(manifest['templates']['station']['contractSha256'], hashlib.sha256(expected).hexdigest())
        self.assertNotIn(b'\r\n', (self.directory / 'manifest.json').read_bytes())


if __name__ == '__main__':
    unittest.main()
