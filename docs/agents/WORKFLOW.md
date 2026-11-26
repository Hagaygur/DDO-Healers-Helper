# Agent build, test and release playbook

Human counterpart: [Operations](../human/OPERATIONS.md). Detailed CI reference: [GITHUB.md](../../GITHUB.md). IDE setup: [Development](DEVELOPMENT.md).

1. Read `AGENTS.md` and the guide matching the task. Inspect current code rather than treating docs as newer evidence.
2. Keep changes scoped. Do not upgrade `VoK.Sdk` beyond `4.2.1.0` (DH 4) / `5.3.0.0` (DH 5), recreate plugin IDs, or add secrets/docs/SDKs to plugin or source ZIPs.
3. Run all checks relevant to the change: `py -3 -m unittest discover -s ci -p "test_*.py" -v`, `HealersHelper.Tests`, `check_build_output.py` (`--target dh4` and `--target dh5`), `WindowsSmoke`, and `Dh5Smoke` (`--require-signed` for signed bundles).
4. **Mandatory per-version `README.md` & docs sync:** At the end of every version/release, update `README.md` (features, all `Settings` properties, all 8 prestige ranks, `### Gamification options explainer`, verified test counts, and `## Release history`) AND synchronize both `docs/human/*` and `docs/agents/*` in the same commit (`ci/test_version.py` enforces `README.md` coverage and secret hygiene automatically).
5. Inspect version impact and archive contents. No failing test may be reinterpreted as a successful major release.

## CI file map

- `.github/workflows/build.yml`: parallel `linux` (with `msitools` + `osslsigncode` + `Dh5Smoke`) and `build` (Windows, `WindowsSmoke` + `Dh5Smoke`), plus gated `release` job.
- `ci/sdk-lock.json` + `ci/Get-Sdk.ps1` + `ci/get_sdk_linux.py`: SHA-256 pinned DH 4 (`4.2.1.0` MSI) and DH 5 (`5.3.0.0` NuGet) SDK extraction.
- `ci/Healers Helper.entitlement` + `ci/Healers Helper.entitlement.sig`: public DH 5 community entitlement pair (matching `pluginId: 54b48548-05ff-48cf-bce1-c6270e3d4a3f`).
- `ci/sign_dh5.py`: ephemeral Authenticode + `.pem` + `.manifest(.sig)` + root-ZIP packager using `DH_AUTHOR_KEY_B64` / `DH_AUTHOR_KEY_FILE` / `--key-file` (Windows `signtool.exe` via legacy-compatible empty-password PFX; Linux `osslsigncode`; cleans temp dir in `finally:`; `--allow-unsigned` strictly for untrusted fork PRs).
- `ci/PackagePlugin.targets` + `ci/check_build_output.py`: builds/validates DH 4 (`*-dh4-plugin.zip`) and DH 5 build directories.
- `tests/ContractSnapshot.cs`: emits `dist/contracts.json` (`schema: 1`) after all C# assertions pass.
- `ci/version.py` + `ci/test_version.py`: contract diff classification, `src/**/*.cs` + `src-dh5/**/*.cs` release gating, secret-hygiene tests, and `README.md` sync checks.
- `build.ps1`: stamps both `src/` and `src-dh5/` versions (LF, UTF-8 without BOM), builds Release DH 4 + DH 5, signs/packages `dist/HealersHelper-<ver>-dh5-plugin.zip`, runs `HealersHelper.Tests`, and copies `dist/HealersHelper-<ver>-dh4-plugin.zip`.
- `tests/WindowsSmoke/` & `tests/Dh5Smoke/`: DH 4 WinForms/DIB smoke suite and cross-platform DH 5 trust-gate/PE-token/async-hydration/ImGui-contract smoke suite.
- `ci/package.py` + `ci/release.py`: validates both plugin ZIPs, builds `HealersHelper-<ver>-source.zip`, writes `dist/SHA256SUMS.txt`, and publishes both ZIPs.

## Archive & release rules

- Publication gates on `CODE_PATHSPECS = (':(glob)src/**/*.cs', ':(glob)src-dh5/**/*.cs')`.
- DH 4 ZIP entries in exact order: `plugins/HealersHelper/VoK.HealersHelper.{dll,deps.json,metadata}`.
- DH 5 signed ZIP exact 8 archive-root entries: `Healers Helper.entitlement`, `Healers Helper.entitlement.sig`, `Healers Helper.manifest`, `Healers Helper.manifest.sig`, `Healers Helper.pem`, `VoK.HealersHelper.deps.json`, `VoK.HealersHelper.dll`, `plugin.metadata`.
- Source archive must exclude `bin/`, `obj/`, `__pycache__/`, `.sdk-cache/`, `.build/`, `dist/`, `*.key`, `*.pfx`, `signing.*`.
