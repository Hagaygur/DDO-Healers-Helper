import hashlib
import io
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import zipfile

from get_sdk_linux import acquire


def make_nupkg_bytes(entry_name, sdk_bytes):
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, 'w', zipfile.ZIP_DEFLATED) as archive:
        archive.writestr(entry_name, sdk_bytes)
    return buf.getvalue()


class SdkAcquisitionTests(unittest.TestCase):
    def make_lock(self, dh4_installer_hash=None, dh4_sdk_bytes=b'pinned dh4 sdk',
                  dh5_pkg_hash=None, dh5_sdk_bytes=b'pinned dh5 sdk'):
        nupkg = make_nupkg_bytes('lib/net10.0/VoK.Sdk.dll', dh5_sdk_bytes)
        lock = {
            'dh4': {
                'url': 'https://example.invalid/DungeonHelper.msi',
                'installerSha256': dh4_installer_hash or hashlib.sha256(b'installer').hexdigest(),
                'sdkSha256': hashlib.sha256(b'pinned dh4 sdk').hexdigest(),
                'sdkVersion': '4.2.1.0',
            },
            'dh5': {
                'url': 'https://example.invalid/vok.sdk.5.3.0.nupkg',
                'packageSha256': dh5_pkg_hash or hashlib.sha256(nupkg).hexdigest(),
                'sdkEntry': 'lib/net10.0/VoK.Sdk.dll',
                'sdkSha256': hashlib.sha256(b'pinned dh5 sdk').hexdigest(),
                'sdkVersion': '5.3.0.0',
            },
        }
        return lock, nupkg

    def run_acquisition(self, target='all', dh4_installer_hash=None, dh4_sdk_bytes=b'pinned dh4 sdk',
                        dh5_pkg_hash=None, dh5_sdk_bytes=b'pinned dh5 sdk', extraction_error=None):
        lock, nupkg = self.make_lock(dh4_installer_hash, dh4_sdk_bytes, dh5_pkg_hash, dh5_sdk_bytes)

        def urlopen(url, timeout=120):
            if 'nupkg' in url:
                return io.BytesIO(nupkg)
            return io.BytesIO(b'installer')

        def extract(command, **kwargs):
            if extraction_error:
                raise extraction_error
            path = Path(command[2]) / 'Program Files' / 'Dungeon Helper'
            path.mkdir(parents=True)
            (path / 'VoK.Sdk.dll').write_bytes(dh4_sdk_bytes)

        with tempfile.TemporaryDirectory() as temporary:
            destination = Path(temporary) / 'sdk'
            environment = Path(temporary) / 'github-env'
            with patch('get_sdk_linux.json.loads', return_value=lock), \
                 patch('get_sdk_linux.urllib.request.urlopen', side_effect=urlopen), \
                 patch('get_sdk_linux.subprocess.run', side_effect=extract) as extraction, \
                 patch.dict(os.environ, {'GITHUB_ENV': str(environment)}):
                try:
                    acquire(destination, target=target)
                except RuntimeError:
                    self.assertFalse(environment.exists())
                    if dh4_installer_hash:
                        extraction.assert_not_called()
                    raise
                env_text = environment.read_text()
                if target in ('all', 'dh4'):
                    dh4_dir = (destination / 'dh4' if target == 'all' else destination).resolve()
                    self.assertEqual((dh4_dir / 'VoK.Sdk.dll').read_bytes(), dh4_sdk_bytes)
                    self.assertIn(f'DH_SDK_DIR={dh4_dir}\n', env_text)
                if target in ('all', 'dh5'):
                    dh5_dir = (destination / 'dh5' if target == 'all' else destination).resolve()
                    self.assertEqual((dh5_dir / 'VoK.Sdk.dll').read_bytes(), dh5_sdk_bytes)
                    self.assertIn(f'DH5_SDK_DIR={dh5_dir}\n', env_text)

    def test_verified_dual_sdks_and_environment(self):
        self.run_acquisition(target='all')

    def test_changed_dh4_installer_rejected_before_extraction(self):
        with self.assertRaisesRegex(RuntimeError, 'Official installer changed'):
            self.run_acquisition(target='dh4', dh4_installer_hash='0' * 64)

    def test_wrong_dh4_sdk_rejected(self):
        with self.assertRaisesRegex(RuntimeError, 'Pinned SDK DLL'):
            self.run_acquisition(target='dh4', dh4_sdk_bytes=b'wrong dh4 sdk')

    def test_changed_dh5_nupkg_rejected(self):
        with self.assertRaisesRegex(RuntimeError, 'DH 5 NuGet package changed'):
            self.run_acquisition(target='dh5', dh5_pkg_hash='0' * 64)

    def test_wrong_dh5_sdk_rejected(self):
        with self.assertRaisesRegex(RuntimeError, 'Pinned DH 5 SDK DLL'):
            self.run_acquisition(target='dh5', dh5_sdk_bytes=b'wrong dh5 sdk')

    def test_extraction_failure_propagates(self):
        with self.assertRaises(subprocess.CalledProcessError):
            self.run_acquisition(target='dh4', extraction_error=subprocess.CalledProcessError(1, 'msiextract'))


if __name__ == '__main__':
    unittest.main()
