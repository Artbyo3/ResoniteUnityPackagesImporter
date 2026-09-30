"""Validate and seal native UI exports without retaining account metadata."""
import argparse
import hashlib
import json
import shutil
import zipfile
from pathlib import Path


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def validate_package(path):
    with zipfile.ZipFile(path) as archive:
        names = archive.namelist()
        if len(names) != len(set(names)) or 'R-Main.record' not in names:
            raise ValueError(f'{path.name}: missing main record or duplicate ZIP entries.')
        if archive.testzip() is not None:
            raise ValueError(f'{path.name}: damaged ZIP data.')
        for name in names:
            if name.startswith('Assets/'):
                signature = name.removeprefix('Assets/')
                if hashlib.sha256(archive.read(name)).hexdigest() != signature:
                    raise ValueError(f'{path.name}: asset checksum mismatch.')
            elif name.endswith('.record'):
                record = json.loads(archive.read(name))
                uri = record.get('assetUri', '')
                if not uri.startswith('packdb:///') or ('Assets/' + uri.removeprefix('packdb:///')) not in names:
                    raise ValueError(f'{path.name}: record asset is not bundled.')
                for asset in record.get('assetManifest') or []:
                    asset_name = 'Assets/' + asset['hash']
                    if asset_name not in names or archive.getinfo(asset_name).file_size != asset['bytes']:
                        raise ValueError(f'{path.name}: dependency is missing or has the wrong size.')


def strip_account_metadata(path, backup_directory):
    replacements = {}
    with zipfile.ZipFile(path) as archive:
        for name in archive.namelist():
            if not name.endswith('.record'):
                continue
            record = json.loads(archive.read(name))
            changed = False
            for field in ('ownerId', 'ownerName', 'path', 'migrationMetadata'):
                if record.get(field) is not None:
                    record[field] = None
                    changed = True
            if changed:
                replacements[name] = json.dumps(record, ensure_ascii=False, separators=(',', ':')).encode('utf-8')
    if not replacements:
        return
    backup_directory.mkdir(parents=True, exist_ok=True)
    backup = backup_directory / (path.stem + '-' + digest(path) + path.suffix)
    if backup.exists():
        if digest(backup) != digest(path):
            raise ValueError('Native export backup failed verification.')
    else:
        shutil.copyfile(path, backup)
        if digest(backup) != digest(path):
            raise ValueError('Native export backup failed verification.')
    temporary = path.with_suffix('.prepared.tmp')
    try:
        with zipfile.ZipFile(path) as original, zipfile.ZipFile(temporary, 'w') as prepared:
            for entry in original.infolist():
                prepared.writestr(entry, replacements.get(entry.filename, original.read(entry)))
        validate_package(temporary)
        temporary.replace(path)
    finally:
        temporary.unlink(missing_ok=True)


def prepare(directory, backup_directory):
    directory = directory.resolve()
    manifest = {'schemaVersion': 1, 'version': '1.0.0', 'templates': {}}
    versions = set()
    inputs = []
    for name in ('station', 'pre-import'):
        expected = name + '.ResonitePackage'
        matches = [p for p in directory.iterdir() if p.name.lower() == expected.lower()]
        if len(matches) != 1 or not matches[0].is_file() or matches[0].stat().st_size == 0:
            raise ValueError(f'Missing native export: {expected}. Export the approved item through Resonite Files.')
        package = matches[0]
        contract = directory / (name + '.bindings.json')
        data = json.loads(contract.read_text(encoding='utf-8'))
        if data.get('schemaVersion') != 1 or not data.get('bindings') or not data.get('templateVersion'):
            raise ValueError(f'Invalid binding contract: {contract.name}')
        versions.add(data['templateVersion'])
        validate_package(package)
        inputs.append((package, contract, expected))
    if len(versions) != 1:
        raise ValueError('Both UI contracts must have the same templateVersion before promotion.')
    for package, contract, expected in inputs:
        strip_account_metadata(package, backup_directory)
        if package.name != expected:
            package = package.rename(directory / expected)
        # Match the repository's LF attributes before hashing. A checkout must
        # not change contract bytes and invalidate the embedded manifest.
        original = contract.read_bytes()
        normalized = original.replace(b'\r\n', b'\n')
        if normalized != original:
            contract.write_bytes(normalized)
        manifest['templates'][package.stem] = {'packageSha256': digest(package), 'contractSha256': digest(contract)}
    manifest['version'] = versions.pop()
    target = directory / 'manifest.json'
    temporary = target.with_suffix('.tmp')
    temporary.write_bytes((json.dumps(manifest, indent=2) + '\n').encode('utf-8'))
    temporary.replace(target)
    return manifest


if __name__ == '__main__':
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser()
    parser.add_argument('--directory', type=Path, default=root / 'UnityPackageImporter/UI/Templates')
    parser.add_argument('--backup-directory', type=Path, default=root / 'private/ui-export-originals')
    args = parser.parse_args()
    try:
        manifest = prepare(args.directory, args.backup_directory)
    except (ValueError, OSError, zipfile.BadZipFile) as error:
        raise SystemExit(str(error))
    print('Prepared station and pre-import UI bundle ' + manifest['version'])
