"""Validate and seal native UI exports without retaining account metadata."""
import argparse
import hashlib
import json
import shutil
import subprocess
import zipfile
from pathlib import Path

TEMPLATE_NAMES = ('station', 'pre-import', 'error-report')


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def _write_entry(target_zip, info, data):
    zi = zipfile.ZipInfo(info.filename, date_time=info.date_time)
    zi.compress_type = zipfile.ZIP_DEFLATED
    zi.external_attr = info.external_attr
    target_zip.writestr(zi, data, compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)


def _replace_file(temporary, target):
    for attempt in range(5):
        try:
            temporary.replace(target)
            return
        except OSError:
            if attempt == 4:
                raise
            import time
            time.sleep(1.0)


def validate_package(path, shared_from=None):
    shared_sizes = {}
    if shared_from is not None:
        with zipfile.ZipFile(shared_from) as shared_zip:
            for name in shared_zip.namelist():
                if name.startswith('Assets/'):
                    signature = name.removeprefix('Assets/')
                    data = shared_zip.read(name)
                    if hashlib.sha256(data).hexdigest() != signature:
                        raise ValueError(f'{shared_from.name}: asset checksum mismatch.')
                    shared_sizes[name] = len(data)
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
                    if asset_name in names:
                        actual_size = archive.getinfo(asset_name).file_size
                    elif asset_name in shared_sizes:
                        actual_size = shared_sizes[asset_name]
                    else:
                        raise ValueError(f'{path.name}: dependency is missing or has the wrong size.')
                    if actual_size != asset['bytes']:
                        raise ValueError(f'{path.name}: dependency is missing or has the wrong size.')


def rehydrate_to_temporary(package, donor_paths):
    """Restore shared content-addressed assets into a temp archive before validation."""
    with zipfile.ZipFile(package) as archive:
        names = set(archive.namelist())
        if 'R-Main.record' not in names:
            return None
        record = json.loads(archive.read('R-Main.record'))
    missing = [
        ('Assets/' + asset['hash'], asset['hash'], asset['bytes'])
        for asset in (record.get('assetManifest') or [])
        if ('Assets/' + asset['hash']) not in names
    ]
    if not missing:
        return None
    restored = {}
    for asset_name, expected_hash, expected_bytes in missing:
        for donor in donor_paths:
            if donor == package or not donor.exists():
                continue
            try:
                with zipfile.ZipFile(donor) as donor_zip:
                    if asset_name not in donor_zip.namelist():
                        continue
                    info = donor_zip.getinfo(asset_name)
                    data = donor_zip.read(asset_name)
            except (OSError, zipfile.BadZipFile):
                continue
            if len(data) == expected_bytes and hashlib.sha256(data).hexdigest() == expected_hash:
                restored[asset_name] = (info, data)
                break
    if len(restored) != len(missing):
        return None
    temporary = package.with_suffix('.rehydrated.tmp')
    with zipfile.ZipFile(package) as original, zipfile.ZipFile(temporary, 'w') as rehydrated:
        for entry in original.infolist():
            _write_entry(rehydrated, entry, original.read(entry.filename))
        for asset_name, _, _ in missing:
            info, data = restored[asset_name]
            _write_entry(rehydrated, info, data)
    return temporary


def deduplicate_shared_assets(station_path, secondary_path):
    """Omit a secondary template's assets that are already bundled in station."""
    with zipfile.ZipFile(station_path) as station_zip:
        station_assets = {
            name: station_zip.getinfo(name).file_size
            for name in station_zip.namelist()
            if name.startswith('Assets/')
        }
    with zipfile.ZipFile(secondary_path) as pre_zip:
        record = json.loads(pre_zip.read('R-Main.record'))
        primary_asset = 'Assets/' + record.get('assetUri', '').removeprefix('packdb:///')
        removable = {
            name for name in pre_zip.namelist()
            if name.startswith('Assets/')
            and name != primary_asset
            and station_assets.get(name) == pre_zip.getinfo(name).file_size
        }
    if not removable:
        return
    temporary = secondary_path.with_suffix('.dedup.tmp')
    try:
        with zipfile.ZipFile(secondary_path) as original, zipfile.ZipFile(temporary, 'w') as compacted:
            for entry in original.infolist():
                if entry.filename not in removable:
                    _write_entry(compacted, entry, original.read(entry.filename))
        validate_package(temporary, shared_from=station_path)
        _replace_file(temporary, secondary_path)
    finally:
        temporary.unlink(missing_ok=True)


def validate_native_packages(packages, game_directory=None):
    """Check typed object references; ZIP hashes cannot detect missing providers."""
    root = Path(__file__).resolve().parents[1]
    game_directory = game_directory or Path(r'C:\Program Files (x86)\Steam\steamapps\common\Resonite')
    command = ['dotnet', 'run', '--project', str(root / 'Tools/UITemplateTool'),
               '--property:GamePath=' + str(game_directory).rstrip('/\\') + '/',
               '--', '--validate', str(game_directory), *(str(path) for path in packages)]
    try:
        result = subprocess.run(command, capture_output=True, text=True, check=False)
    except OSError as error:
        raise ValueError('Native UI validation requires dotnet and the installed Resonite assemblies.') from error
    if result.returncode:
        raise ValueError((result.stderr or result.stdout).strip() or 'Native UI reference validation failed.')


def strip_account_metadata(path, backup_directory):
    replacements = {}
    needs_deflate = False
    with zipfile.ZipFile(path) as archive:
        for entry in archive.infolist():
            if entry.compress_type != zipfile.ZIP_DEFLATED:
                needs_deflate = True
            name = entry.filename
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
    if not replacements and not needs_deflate:
        return
    if replacements:
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
                data = replacements.get(entry.filename, original.read(entry.filename))
                _write_entry(prepared, entry, data)
        validate_package(temporary)
        _replace_file(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def prepare(directory, backup_directory, game_directory=None):
    directory = directory.resolve()
    manifest = {'schemaVersion': 1, 'version': '1.0.0', 'templates': {}}
    versions = set()
    inputs = []
    for name in TEMPLATE_NAMES:
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
        inputs.append((package, contract, expected))
    if len(versions) != 1:
        raise ValueError('All UI contracts must have the same templateVersion before promotion.')
    donor_paths = [package for package, _, _ in inputs]
    if backup_directory and backup_directory.exists():
        donor_paths.extend(
            p for p in sorted(backup_directory.iterdir())
            if p.is_file() and p.suffix.lower() == '.resonitepackage'
        )
    rehydrated_temps = {}
    try:
        validation_paths = []
        for package, _, _ in inputs:
            temp = rehydrate_to_temporary(package, donor_paths)
            if temp is not None:
                rehydrated_temps[package] = temp
                validation_paths.append(temp)
            else:
                validation_paths.append(package)
        for path in validation_paths:
            validate_package(path)
        # Fail before metadata, contract bytes, or the manifest are changed.
        validate_native_packages(validation_paths, game_directory)
        for package, temp in list(rehydrated_temps.items()):
            _replace_file(temp, package)
            del rehydrated_temps[package]
    finally:
        for temp in rehydrated_temps.values():
            temp.unlink(missing_ok=True)
    prepared_inputs = []
    for package, contract, expected in inputs:
        strip_account_metadata(package, backup_directory)
        if package.name != expected:
            package = package.rename(directory / expected)
        prepared_inputs.append((package, contract))
    for name in TEMPLATE_NAMES[1:]:
        deduplicate_shared_assets(directory / 'station.ResonitePackage', directory / (name + '.ResonitePackage'))
    for package, contract in prepared_inputs:
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
    parser.add_argument('--game-directory', type=Path, help='Resonite installation containing FrooxEngine.dll')
    args = parser.parse_args()
    try:
        manifest = prepare(args.directory, args.backup_directory, args.game_directory)
    except (ValueError, OSError, zipfile.BadZipFile) as error:
        raise SystemExit(str(error))
    print('Prepared station, pre-import and error-report UI bundle ' + manifest['version'])
