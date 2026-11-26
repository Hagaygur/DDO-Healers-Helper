"""Extract the hash-pinned DH 4 (MSI) and DH 5 (NuGet) VoK.Sdk binaries on Linux."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import urllib.request
import zipfile


def sha256(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(chunk)
    return digest.hexdigest()


def acquire_dh4(lock_dh4, destination):
    destination = destination.resolve()
    destination.mkdir(parents=True, exist_ok=True)
    # A fresh extraction cannot accidentally select a stale cached SDK.
    with tempfile.TemporaryDirectory(prefix='healers-helper-dh4-sdk-') as temporary:
        root = Path(temporary)
        installer = root / 'DungeonHelper.msi'
        with urllib.request.urlopen(lock_dh4['url'], timeout=120) as response:
            with installer.open('wb') as output:
                shutil.copyfileobj(response, output)
        if sha256(installer) != lock_dh4['installerSha256'].lower():
            raise RuntimeError('Official installer changed; review ci/sdk-lock.json before building.')
        extracted = root / 'extracted'
        extracted.mkdir()
        subprocess.run(['msiextract', '-C', str(extracted), str(installer)],
                       check=True, stdout=subprocess.DEVNULL)
        matches = [path for path in extracted.rglob('VoK.Sdk.dll')
                   if sha256(path) == lock_dh4['sdkSha256'].lower()]
        if not matches:
            raise RuntimeError('Pinned SDK DLL was not found in the installer.')
        shutil.copyfile(matches[0], destination / 'VoK.Sdk.dll')
    return destination


def acquire_dh5(lock_dh5, destination):
    destination = destination.resolve()
    destination.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='healers-helper-dh5-sdk-') as temporary:
        root = Path(temporary)
        nupkg = root / 'vok.sdk.nupkg'
        with urllib.request.urlopen(lock_dh5['url'], timeout=120) as response:
            with nupkg.open('wb') as output:
                shutil.copyfileobj(response, output)
        if sha256(nupkg) != lock_dh5['packageSha256'].lower():
            raise RuntimeError('DH 5 NuGet package changed; review ci/sdk-lock.json before building.')
        sdk_bytes = None
        with zipfile.ZipFile(nupkg) as archive:
            entry_name = lock_dh5['sdkEntry']
            if entry_name not in archive.namelist():
                raise RuntimeError(f'Pinned DH 5 SDK entry missing from NuGet package: {entry_name}')
            sdk_bytes = archive.read(entry_name)
        if hashlib.sha256(sdk_bytes).hexdigest() != lock_dh5['sdkSha256'].lower():
            raise RuntimeError('Pinned DH 5 SDK DLL hash mismatch.')
        (destination / 'VoK.Sdk.dll').write_bytes(sdk_bytes)
    return destination


def acquire(destination, target='all'):
    lock = json.loads(Path(__file__).with_name('sdk-lock.json').read_text(encoding='utf-8'))
    destination = destination.resolve()
    env_lines = []

    if target in ('all', 'dh4'):
        dh4_dir = destination / 'dh4' if target == 'all' else destination
        dh4_resolved = acquire_dh4(lock['dh4'], dh4_dir)
        env_lines.append(f'DH_SDK_DIR={dh4_resolved}\n')
        print(f'Verified DH 4 SDK {lock["dh4"]["sdkVersion"]}: {dh4_resolved}')

    if target in ('all', 'dh5'):
        dh5_dir = destination / 'dh5' if target == 'all' else destination
        dh5_resolved = acquire_dh5(lock['dh5'], dh5_dir)
        env_lines.append(f'DH5_SDK_DIR={dh5_resolved}\n')
        print(f'Verified DH 5 SDK {lock["dh5"]["sdkVersion"]}: {dh5_resolved}')

    if os.environ.get('GITHUB_ENV'):
        with open(os.environ['GITHUB_ENV'], 'a', encoding='utf-8') as environment:
            environment.writelines(env_lines)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--destination', type=Path, default=Path('.sdk-cache'))
    parser.add_argument('--target', choices=('all', 'dh4', 'dh5'), default='all')
    args = parser.parse_args()
    acquire(args.destination, args.target)
