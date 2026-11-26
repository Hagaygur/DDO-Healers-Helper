"""Ephemeral cross-platform DH 5 Beta Authenticode + manifest + ZIP packager."""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import zipfile

PLUGIN_NAME = 'Healers Helper'
PLUGIN_ID = '54b48548-05ff-48cf-bce1-c6270e3d4a3f'
ASSEMBLY_BASE = 'VoK.HealersHelper'
DEFAULT_PRODUCT_URL = 'https://github.com/Hagaygur/DDO-Healers-Helper'


def find_openssl():
    candidates = [
        r'C:\Program Files\Git\usr\bin\openssl.exe',
        r'C:\Program Files\OpenSSL-Win64\bin\openssl.exe',
        shutil.which('openssl'),
    ]
    for candidate in candidates:
        if candidate and Path(candidate).is_file():
            return str(candidate)
    raise RuntimeError('openssl executable not found.')


def find_signtool():
    found = shutil.which('signtool')
    if found:
        return found
    kits_root = Path(os.environ.get('ProgramFiles(x86)', r'C:\Program Files (x86)')) / 'Windows Kits' / '10' / 'bin'
    if kits_root.is_dir():
        matches = sorted(kits_root.glob('*/x64/signtool.exe'), reverse=True)
        if matches:
            return str(matches[0])
    raise RuntimeError('Windows SDK x64 signtool.exe not found.')


def find_osslsigncode():
    found = shutil.which('osslsigncode')
    if found:
        return found
    raise RuntimeError('osslsigncode executable not found.')


def run_checked(cmd, env=None):
    merged_env = dict(os.environ)
    if env:
        merged_env.update(env)
    result = subprocess.run(cmd, capture_output=True, text=True, env=merged_env)
    if result.returncode != 0:
        raise RuntimeError(
            f'Command failed ({cmd[0]} exit {result.returncode}): {result.stderr.strip() or result.stdout.strip()}'
        )
    return result


def resolve_key_bytes(key_file):
    if key_file:
        path = Path(key_file)
        if not path.is_file():
            raise RuntimeError(f'Signing key file not found: {path}')
        return path.read_bytes()

    env_path = os.environ.get('DH_AUTHOR_KEY_FILE', '').strip()
    if env_path:
        path = Path(env_path)
        if not path.is_file():
            raise RuntimeError(f'DH_AUTHOR_KEY_FILE not found: {path}')
        return path.read_bytes()

    for env_name in ('DH_AUTHOR_KEY_B64', 'DH5_AUTHOR_KEY_B64'):
        raw = os.environ.get(env_name, '').strip()
        if raw:
            if 'BEGIN ' in raw and 'PRIVATE KEY' in raw:
                return raw.encode('utf-8')
            return base64.b64decode(raw, validate=True)

    return None


def compute_spki_sha256(openssl_exe, key_path):
    res = subprocess.run(
        [openssl_exe, 'pkey', '-in', str(key_path), '-pubout', '-outform', 'DER'],
        capture_output=True,
        check=False,
    )
    if res.returncode != 0:
        raise RuntimeError(f'Failed to derive SPKI from private key: {res.stderr.decode(errors="replace").strip()}')
    return hashlib.sha256(res.stdout).hexdigest().upper()


def write_zip_from_stage(stage_dir, output_zip):
    output_zip = Path(output_zip)
    output_zip.parent.mkdir(parents=True, exist_ok=True)
    tmp_zip = output_zip.with_suffix('.zip.tmp')
    try:
        with zipfile.ZipFile(tmp_zip, 'w', zipfile.ZIP_DEFLATED) as archive:
            for item in sorted(stage_dir.iterdir(), key=lambda p: p.name):
                if item.is_file():
                    archive.write(item, item.name)
        os.replace(tmp_zip, output_zip)
    finally:
        if tmp_zip.exists():
            tmp_zip.unlink()


def package_dh5(
    dll_path,
    deps_path,
    version,
    output_zip,
    entitlement_path,
    entitlement_sig_path,
    product_url=DEFAULT_PRODUCT_URL,
    update_source='',
    key_file=None,
    allow_unsigned=False,
):
    dll_path = Path(dll_path)
    deps_path = Path(deps_path)
    if not dll_path.is_file():
        raise FileNotFoundError(f'DH 5 DLL not found: {dll_path}')
    if not deps_path.is_file():
        raise FileNotFoundError(f'DH 5 deps.json not found: {deps_path}')

    key_bytes = resolve_key_bytes(key_file)
    if key_bytes is None and not allow_unsigned:
        raise RuntimeError(
            'DH 5 signing key is required (supply --key-file, DH_AUTHOR_KEY_FILE, DH_AUTHOR_KEY_B64, or DH5_AUTHOR_KEY_B64; '
            'pass --allow-unsigned only for untrusted fork PR validation).'
        )

    metadata = {
        'PluginName': PLUGIN_NAME,
        'ProductInfoUrl': product_url,
        'UpdateSource': update_source,
        'CurrentVersion': f'{version}.0',
        'AllowedVersions': [{'Version': f'{version}.0'}],
    }

    temp_root = tempfile.mkdtemp(prefix='dh5-sign-')
    try:
        root = Path(temp_root)
        stage_dir = root / 'stage'
        stage_dir.mkdir()

        staged_dll = stage_dir / f'{ASSEMBLY_BASE}.dll'
        staged_deps = stage_dir / f'{ASSEMBLY_BASE}.deps.json'
        shutil.copyfile(dll_path, staged_dll)
        shutil.copyfile(deps_path, staged_deps)
        (stage_dir / 'plugin.metadata').write_text(json.dumps(metadata, indent=2) + '\n', encoding='utf-8')

        if key_bytes is None:
            write_zip_from_stage(stage_dir, output_zip)
            print(f'Packaged UNSIGNED DH 5 ZIP (PR/dev only): {output_zip}')
            return False

        entitlement_path = Path(entitlement_path)
        entitlement_sig_path = Path(entitlement_sig_path)
        if not entitlement_path.is_file() or not entitlement_sig_path.is_file():
            raise FileNotFoundError('Public DH 5 entitlement pair not found in ci/.')

        ent_data = json.loads(entitlement_path.read_text(encoding='utf-8'))
        if ent_data.get('pluginName') != PLUGIN_NAME or ent_data.get('pluginId', '').lower() != PLUGIN_ID:
            raise RuntimeError('Entitlement metadata does not match Healers Helper.')
        expected_fp = ent_data['authorKeyFingerprint'].strip().upper()
        author_cn = ent_data.get('authorName', 'Warned').strip() or 'Warned'

        secrets_dir = root / 'ephemeral'
        secrets_dir.mkdir()
        key_path = secrets_dir / 'author.key'
        key_path.write_bytes(key_bytes)
        if os.name != 'nt':
            os.chmod(key_path, 0o600)

        openssl_exe = find_openssl()
        actual_fp = compute_spki_sha256(openssl_exe, key_path)
        if actual_fp != expected_fp:
            raise RuntimeError(f'Author key SPKI fingerprint mismatch: expected {expected_fp}, got {actual_fp}')

        crt_path = secrets_dir / 'author.crt'
        cnf_path = secrets_dir / 'openssl.cnf'
        cnf_path.write_text('[req]\ndistinguished_name = dn\nprompt = no\n[dn]\nCN = ' + author_cn + '\n', encoding='utf-8')
        openssl_env = {'MSYS_NO_PATHCONV': '1', 'OPENSSL_CONF': str(cnf_path)}
        run_checked(
            [
                openssl_exe, 'req', '-x509', '-new',
                '-config', str(cnf_path),
                '-key', str(key_path),
                '-days', '730',
                '-subj', f'/CN={author_cn}',
                '-out', str(crt_path),
            ],
            env=openssl_env,
        )

        if sys.platform == 'win32':
            pfx_path = secrets_dir / 'author.pfx'
            run_checked(
                [
                    openssl_exe, 'pkcs12', '-export',
                    '-keypbe', 'PBE-SHA1-3DES',
                    '-certpbe', 'PBE-SHA1-3DES',
                    '-macalg', 'sha1',
                    '-out', str(pfx_path),
                    '-inkey', str(key_path),
                    '-in', str(crt_path),
                    '-passout', 'pass:',
                ],
                env=openssl_env,
            )
            signtool_exe = find_signtool()
            run_checked([signtool_exe, 'sign', '/fd', 'SHA256', '/f', str(pfx_path), str(staged_dll)])
        else:
            osslsigncode_exe = find_osslsigncode()
            signed_tmp = secrets_dir / f'{ASSEMBLY_BASE}.signed.dll'
            run_checked(
                [
                    osslsigncode_exe, 'sign',
                    '-certs', str(crt_path),
                    '-key', str(key_path),
                    '-h', 'sha256',
                    '-in', str(staged_dll),
                    '-out', str(signed_tmp),
                ]
            )
            shutil.move(signed_tmp, staged_dll)

        staged_pem = stage_dir / f'{PLUGIN_NAME}.pem'
        run_checked([openssl_exe, 'pkey', '-in', str(key_path), '-pubout', '-out', str(staged_pem)], env=openssl_env)
        shutil.copyfile(entitlement_path, stage_dir / f'{PLUGIN_NAME}.entitlement')
        shutil.copyfile(entitlement_sig_path, stage_dir / f'{PLUGIN_NAME}.entitlement.sig')

        entries = []
        for item in sorted(stage_dir.iterdir(), key=lambda p: p.name):
            if item.is_file():
                digest = hashlib.sha256(item.read_bytes()).hexdigest().upper()
                entries.append({'path': item.name, 'sha256': digest})

        manifest_obj = {
            'formatVersion': 1,
            'pluginName': PLUGIN_NAME,
            'entries': entries,
        }
        manifest_path = stage_dir / f'{PLUGIN_NAME}.manifest'
        manifest_sig_path = stage_dir / f'{PLUGIN_NAME}.manifest.sig'
        manifest_path.write_text(json.dumps(manifest_obj, separators=(',', ':')), encoding='utf-8')

        run_checked(
            [
                openssl_exe, 'dgst', '-sha256',
                '-sign', str(key_path),
                '-out', str(manifest_sig_path),
                str(manifest_path),
            ],
            env=openssl_env,
        )
        run_checked(
            [
                openssl_exe, 'dgst', '-sha256',
                '-verify', str(staged_pem),
                '-signature', str(manifest_sig_path),
                str(manifest_path),
            ],
            env=openssl_env,
        )

        write_zip_from_stage(stage_dir, output_zip)
        print(f'Packaged SIGNED DH 5 ZIP: {output_zip}')
        return True
    finally:
        shutil.rmtree(temp_root, ignore_errors=True)


def main():
    ci_dir = Path(__file__).resolve().parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dll', type=Path, required=True)
    parser.add_argument('--deps', type=Path, default=None)
    parser.add_argument('--version', required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--entitlement', type=Path, default=ci_dir / f'{PLUGIN_NAME}.entitlement')
    parser.add_argument('--entitlement-sig', type=Path, default=ci_dir / f'{PLUGIN_NAME}.entitlement.sig')
    parser.add_argument('--product-url', default=DEFAULT_PRODUCT_URL)
    parser.add_argument('--update-source', default='')
    parser.add_argument('--key-file', default=None)
    parser.add_argument('--allow-unsigned', action='store_true')
    args = parser.parse_args()

    deps_path = args.deps or args.dll.with_name(f'{ASSEMBLY_BASE}.deps.json')
    package_dh5(
        dll_path=args.dll,
        deps_path=deps_path,
        version=args.version,
        output_zip=args.output,
        entitlement_path=args.entitlement,
        entitlement_sig_path=args.entitlement_sig,
        product_url=args.product_url,
        update_source=args.update_source,
        key_file=args.key_file,
        allow_unsigned=args.allow_unsigned,
    )


if __name__ == '__main__':
    main()
