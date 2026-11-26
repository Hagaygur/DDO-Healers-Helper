"""Validate the plugin's final build directory and runtime-only installer."""
import argparse
import json
from pathlib import Path
import zipfile


def validate_dh4(directory, configuration, version):
    archive_name = f'HealersHelper-{version}-dh4-plugin.zip'
    runtime = ['VoK.HealersHelper.' + extension for extension in ('dll', 'deps.json', 'metadata')]
    expected = {archive_name}
    if configuration == 'Debug':
        expected.update(runtime)
        expected.add('VoK.HealersHelper.pdb')
    actual = {path.name for path in directory.iterdir()}
    if actual != expected:
        raise RuntimeError(f'DH 4 {configuration} output mismatch: expected {sorted(expected)}, got {sorted(actual)}')
    with zipfile.ZipFile(directory / archive_name) as archive:
        entries = ['plugins/HealersHelper/' + name for name in runtime]
        if archive.namelist() != entries or archive.testzip() is not None:
            raise RuntimeError('DH 4 installer must contain exactly three valid runtime entries, DLL first')
        metadata = json.loads(archive.read(entries[2]).decode('utf-8-sig'))
        if metadata['Version'] != version + '.0':
            raise RuntimeError('DH 4 installer metadata version disagrees with package version')
        if configuration == 'Debug':
            for name, entry in zip(runtime, entries):
                if (directory / name).read_bytes() != archive.read(entry):
                    raise RuntimeError(f'Debug installer differs from loose file: {name}')
    print(f'PASS DH 4 {configuration} output and installer contents: {directory}')


def validate_dh5(directory, configuration):
    expected = {'VoK.HealersHelper.dll', 'VoK.HealersHelper.deps.json', 'VoK.HealersHelper.pdb'}
    actual = {path.name for path in directory.iterdir()}
    if actual != expected:
        raise RuntimeError(f'DH 5 {configuration} output mismatch: expected {sorted(expected)}, got {sorted(actual)}')
    if any(p.name.lower() == 'vok.sdk.dll' or p.suffix.lower() in {'.key', '.pfx'} for p in directory.iterdir()):
        raise RuntimeError(f'Forbidden SDK or secret file in DH 5 build output: {directory}')
    print(f'PASS DH 5 {configuration} output: {directory}')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    parser.add_argument('configuration', choices=('Debug', 'Release'))
    parser.add_argument('--version', default='1.0.0')
    parser.add_argument('--target', choices=('dh4', 'dh5'), default='dh4')
    options = parser.parse_args()
    if options.target == 'dh5':
        validate_dh5(options.directory, options.configuration)
    else:
        validate_dh4(options.directory, options.configuration, options.version)
