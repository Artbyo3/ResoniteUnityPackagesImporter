"""Checks for package integrity and privacy before sealing the native UI bundle."""
import hashlib
import importlib.util
import json
import tempfile
import unittest
from unittest import mock
import zipfile
from pathlib import Path

script = Path(__file__).resolve().parents[1] / 'scripts/Prepare-StationTemplates.py'
spec = importlib.util.spec_from_file_location('station_templates', script)
bundle = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bundle)


class StationBundleTests(unittest.TestCase):
    def setUp(self):
        # These tests isolate ZIP sealing; typed native reference fixtures are
        # exercised by UITemplateTool --test-validation using the engine schema.
        patch = mock.patch.object(bundle, 'validate_native_packages')
        self.native_validator = patch.start()
        self.addCleanup(patch.stop)
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

    def test_dangling_native_references_leave_exports_and_manifest_untouched(self):
        self.native_validator.side_effect = ValueError('Image.Sprite references missing object')
        before = {p.name: p.read_bytes() for p in self.directory.iterdir()}
        with self.assertRaisesRegex(ValueError, 'references missing object'):
            bundle.prepare(self.directory, self.backups)
        self.assertEqual(before, {p.name: p.read_bytes() for p in self.directory.iterdir()})
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

    def test_shared_secondary_assets_are_deduplicated_and_rehydrated_on_single_package_reexport(self):
        shared_font = b'shared cjk font payload' * 50
        shared_hash = hashlib.sha256(shared_font).hexdigest()
        station_main = b'station root bson v1'
        station_hash = hashlib.sha256(station_main).hexdigest()
        pre_main = b'pre-import root bson v1'
        pre_hash = hashlib.sha256(pre_main).hexdigest()

        def make_pkg(name, main_bytes, main_hash):
            rec = {
                'id': 'R-Main', 'ownerId': 'private-id', 'ownerName': 'private-name',
                'assetUri': 'packdb:///' + main_hash, 'path': '/private',
                'migrationMetadata': None,
                'assetManifest': [
                    {'hash': main_hash, 'bytes': len(main_bytes)},
                    {'hash': shared_hash, 'bytes': len(shared_font)},
                ],
            }
            with zipfile.ZipFile(self.directory / (name + '.resonitepackage'), 'w') as zf:
                zf.writestr('Assets/' + main_hash, main_bytes)
                zf.writestr('Assets/' + shared_hash, shared_font)
                zf.writestr('R-Main.record', json.dumps(rec))

        make_pkg('station', station_main, station_hash)
        make_pkg('pre-import', pre_main, pre_hash)
        bundle.prepare(self.directory, self.backups)

        with zipfile.ZipFile(self.directory / 'pre-import.ResonitePackage') as pre_zip:
            self.assertIn('Assets/' + pre_hash, pre_zip.namelist())
            self.assertNotIn('Assets/' + shared_hash, pre_zip.namelist())

        # Simulate re-exporting ONLY station.resonitepackage in-world while pre-import is already deduplicated.
        station_main_v2 = b'station root bson v2 after in-world UI edit'
        station_hash_v2 = hashlib.sha256(station_main_v2).hexdigest()
        (self.directory / 'station.ResonitePackage').unlink()
        make_pkg('station', station_main_v2, station_hash_v2)

        def assert_both_standalone_during_native_validation(packages, _game_dir=None):
            for pkg_path in packages:
                with zipfile.ZipFile(pkg_path) as zf:
                    self.assertIn('Assets/' + shared_hash, zf.namelist())

        self.native_validator.side_effect = assert_both_standalone_during_native_validation
        manifest = bundle.prepare(self.directory, self.backups)
        self.assertIn('station', manifest['templates'])
        self.assertIn('pre-import', manifest['templates'])
        with zipfile.ZipFile(self.directory / 'pre-import.ResonitePackage') as pre_zip:
            self.assertIn('Assets/' + pre_hash, pre_zip.namelist())
            self.assertNotIn('Assets/' + shared_hash, pre_zip.namelist())


if __name__ == '__main__':
    unittest.main()
