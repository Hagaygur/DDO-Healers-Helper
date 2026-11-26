"""Validate DH 4 + DH 5 plugin ZIPs, create separate source archive, and write SHA256SUMS.txt."""
import argparse
import hashlib
import json
from pathlib import Path
import zipfile

FORBIDDEN_SECRET_SUFFIXES = {'.key', '.pfx'}
SOURCE_DIRECTORIES = ('src', 'src-dh5', 'tests', 'ci', '.github', '.vscode', 'docs')
SOURCE_ROOT_FILES = (
    'README.md', 'INSTALL.txt', 'GITHUB.md', 'AGENTS.md', 'LICENSE.txt',
    'build.ps1', '.gitignore', 'HealersHelper.sln', 'global.json',
    '.vsconfig', '.editorconfig', '.gitattributes',
)
DH5_SIGNED_ENTRIES = sorted([
    'Healers Helper.entitlement',
    'Healers Helper.entitlement.sig',
    'Healers Helper.manifest',
    'Healers Helper.manifest.sig',
    'Healers Helper.pem',
    'VoK.HealersHelper.deps.json',
    'VoK.HealersHelper.dll',
    'plugin.metadata',
])
DH5_UNSIGNED_ENTRIES = sorted([
    'VoK.HealersHelper.deps.json',
    'VoK.HealersHelper.dll',
    'plugin.metadata',
])


def is_forbidden_source_path(path):
    parts = set(path.parts)
    if parts & {'bin', 'obj', '__pycache__', '.sdk-cache', '.build', 'dist'}:
        return True
    name = path.name.lower()
    if name.startswith('signing.'):
        return True
    if path.suffix.lower() in {'.pyc', '.user', *FORBIDDEN_SECRET_SUFFIXES}:
        return True
    return False


def validate_dh4_zip(dh4_zip, version):
    expected = ['plugins/HealersHelper/VoK.HealersHelper.' + ext for ext in ('dll', 'deps.json', 'metadata')]
    with zipfile.ZipFile(dh4_zip) as archive:
        assert archive.namelist() == expected, 'DH 4 installer must contain exactly three runtime files, DLL first'
        assert archive.testzip() is None
        metadata = json.loads(archive.read(expected[2]).decode('utf-8-sig'))
        assert metadata['Version'] == version + '.0'


def validate_dh5_zip(dh5_zip, version, require_signed):
    with zipfile.ZipFile(dh5_zip) as archive:
        names = archive.namelist()
        assert archive.testzip() is None
        for name in names:
            assert '\\' not in name and not name.startswith('./') and not name.startswith('/'), f'Non-canonical DH 5 entry: {name}'
            assert '/' not in name, f'DH 5 entry must sit at archive root: {name}'
            lower = name.lower()
            assert lower != 'vok.sdk.dll' and not lower.endswith('.pdb'), f'Forbidden runtime artifact in DH 5 ZIP: {name}'
            assert not any(lower.endswith(ext) for ext in FORBIDDEN_SECRET_SUFFIXES), f'Forbidden secret in DH 5 ZIP: {name}'
        if require_signed:
            assert sorted(names) == DH5_SIGNED_ENTRIES, f'DH 5 signed ZIP must contain exact 8 entries: {sorted(names)}'
        else:
            assert sorted(names) in (DH5_SIGNED_ENTRIES, DH5_UNSIGNED_ENTRIES), f'Unexpected DH 5 ZIP entries: {sorted(names)}'
        metadata = json.loads(archive.read('plugin.metadata').decode('utf-8-sig'))
        assert metadata['PluginName'] == 'Healers Helper'
        assert metadata['CurrentVersion'] == version + '.0'
        assert any(v.get('Version') == version + '.0' for v in metadata.get('AllowedVersions', []))


def package_all(require_signed=False):
    version_info = json.loads(Path('dist/version.json').read_text(encoding='utf-8'))
    version = version_info['version']
    must_be_signed = require_signed or bool(version_info.get('publish'))

    dh4_plugin = Path(f'dist/HealersHelper-{version}-dh4-plugin.zip')
    dh5_plugin = Path(f'dist/HealersHelper-{version}-dh5-plugin.zip')
    validate_dh4_zip(dh4_plugin, version)
    validate_dh5_zip(dh5_plugin, version, require_signed=must_be_signed)

    # Explicit source allowlist: never ship downloaded SDKs, build output, private keys, or git internals.
    source = Path(f'dist/HealersHelper-{version}-source.zip')
    with zipfile.ZipFile(source, 'w', zipfile.ZIP_DEFLATED) as archive:
        for directory in SOURCE_DIRECTORIES:
            for path in sorted(Path(directory).rglob('*')):
                if path.is_file() and not is_forbidden_source_path(path):
                    archive.write(path, path.as_posix())
        for name in SOURCE_ROOT_FILES:
            archive.write(name, name)

    files = [dh4_plugin, dh5_plugin, source, Path('dist/contracts.json'), Path('dist/version.json')]
    Path('dist/SHA256SUMS.txt').write_text(
        ''.join(f'{hashlib.sha256(p.read_bytes()).hexdigest()}  {p.name}\n' for p in files),
        encoding='utf-8',
    )
    print('PASS DH 4 + DH 5 plugin contents, metadata versions, ZIP integrity; separate source archive and SHA256SUMS.txt created')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--require-signed', action='store_true')
    args = parser.parse_args()
    package_all(require_signed=args.require_signed)
