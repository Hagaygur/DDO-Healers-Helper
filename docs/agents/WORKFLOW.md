# Agent build, test and release playbook

Human counterpart: [Operations](../human/OPERATIONS.md). Detailed CI reference: [GITHUB.md](../../GITHUB.md). IDE setup: [Development](DEVELOPMENT.md).

1. Read `AGENTS.md` and the guide matching the task. Inspect current code rather than treating docs as newer evidence.
2. Keep changes scoped. Do not upgrade `VoK.Sdk` beyond `4.2.1.0`, recreate plugin identity, or add non-runtime files to the plugin ZIP.
3. Run relevant pure tests (`py -3 -m unittest discover -s ci -p "test_*.py" -v`, `HealersHelper.Tests`), `check_build_output.py`, and on Windows also run `WindowsSmoke`. Report unavailable checks explicitly.
4. Update both human and agent documentation when interfaces, behavior, build steps, or verified evidence change.
5. Inspect version impact and archive contents. No failing test may be reinterpreted as a successful major release.

## CI file map

- `.github/workflows/build.yml`: parallel `linux` and `build` (Windows) jobs, plus gated `release` job.
- `ci/Get-Sdk.ps1` + `ci/sdk-lock.json`: Windows MSI extraction and exact SHA-256 + version `4.2.1.0` verification.
- `ci/get_sdk_linux.py`: Linux MSI extraction via `msitools` (`msiextract`) with the same SHA-256 verification.
- `ci/PackagePlugin.targets`: MSBuild packaging target producing `HealersHelper-$(Version)-plugin.zip` (DLL first; Release=ZIP-only, Debug=loose+PDB+ZIP).
- `ci/check_build_output.py`: validates Debug and Release output directories and ZIP contents on Windows and Linux.
- `tests/ContractSnapshot.cs`: emits `dist/contracts.json` (`schema: 1`) after all C# behavioral assertions pass.
- `ci/version.py` + `ci/test_version.py`: contract diff classification (`patch`/`minor`/`major`), `src/**/*.cs` release gating, conventional commit escalation, and same-commit draft retry.
- `build.ps1`: stamps `src/AssemblyInfo.cs` and `src/VoK.HealersHelper.metadata` (only when changed, preserving LF and UTF-8 without BOM), runs Release build, verifies 3-entry ZIP, runs `HealersHelper.Tests`, and copies ZIP to `dist/`.
- `tests/WindowsSmoke/`: loads `VoK.HealersHelper.dll` directly from the Release ZIP, validates `IDdoPlugin` contract, synthetic 720px toolbar / minimum-size dialog layout (no clipping), and native layered banner surface / window styles. Does not attach to DDO or emulate LS.
- `ci/package.py`: validates plugin ZIP and builds separate `HealersHelper-<version>-source.zip` + `dist/SHA256SUMS.txt`.
- `ci/release.py`: verifies tag ownership, uploads draft assets, and publishes when `publish == true`.

## Version classification & packaging rules

- Publication gates on `CODE_PATHSPEC = ':(glob)src/**/*.cs'` between baseline tag and `HEAD`. Docs/tests/CI/metadata changes alone build artifacts with `publish: false`.
- Installer entries in exact order:
  1. `plugins/HealersHelper/VoK.HealersHelper.dll`
  2. `plugins/HealersHelper/VoK.HealersHelper.deps.json`
  3. `plugins/HealersHelper/VoK.HealersHelper.metadata`
- Source archives must include `src/`, `tests/`, `ci/`, `.github/`, `.vscode/`, `docs/`, and root config/docs (`README.md`, `INSTALL.txt`, `GITHUB.md`, `AGENTS.md`, `LICENSE.txt`, `build.ps1`, `.gitignore`, `HealersHelper.sln`, `global.json`, `.vsconfig`, `.editorconfig`, `.gitattributes`), while excluding `bin/`, `obj/`, `__pycache__/`, `dist/`, and `.build/`.
