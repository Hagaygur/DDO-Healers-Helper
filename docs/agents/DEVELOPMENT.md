# Agent IDE and development instructions

Human counterpart: [IDE setup](../human/DEVELOPMENT.md).

- `HealersHelper.sln` contains 5 projects: `src\HealersHelper.csproj` (`Any CPU`, `net8.0-windows`), `src-dh5\HealersHelper.Dh5.csproj` (`x64`, `net10.0`), `tests\HealersHelper.Tests.csproj` (`Any CPU`, `net8.0`), `tests\WindowsSmoke\WindowsSmoke.csproj` (`Any CPU`, `net8.0-windows`), and `tests\Dh5Smoke\Dh5Smoke.csproj` (`x64`, `net10.0`).
- `global.json` selects stable .NET 10 SDKs (`10.0.100`, `latestFeature`). CI installs .NET 8 (for DH 4 runtime/tests) and .NET 10 (SDK + DH 5 runtime).
- SDK resolution:
  - DH 4 (`src/`): `DungeonHelperDir`, defaulting to `%APPDATA%\Dungeon Helper`.
  - DH 5 (`src-dh5/`): `DungeonHelper5Dir` or `DH5_SDK_DIR`, defaulting to `.sdk-cache/dh5`. Run `./ci/Get-Sdk.ps1` (Windows) or `python3 ci/get_sdk_linux.py` (Linux) to populate verified SDKs.
- Keep `.editorconfig`, `.gitattributes`, and `.gitignore` project-scoped. Never commit `.build/`, `.sdk-cache/`, `dist/`, `bin/`, `obj/`, `*.key`, `*.pfx`, `signing.*`, or downloaded SDK binaries.
- Linux cross-compiles all 5 projects (`EnableWindowsTargeting=true`), runs `HealersHelper.Tests`, runs `ci/test_*.py`, packages DH 5 via `osslsigncode`, and runs `Dh5Smoke`; only `WindowsSmoke` and live DDO execution require Windows.

## Build outputs

`ci/PackagePlugin.targets` runs automatically on every `src/HealersHelper.csproj` build:
- Release retains ONLY `HealersHelper-<Version>-dh4-plugin.zip` in `src/bin/Release/net8.0-windows/`.
- Debug retains `VoK.HealersHelper.dll`, `.deps.json`, `.metadata`, `.pdb`, and `HealersHelper-<Version>-dh4-plugin.zip` in `src/bin/Debug/net8.0-windows/`.
- DH 5 outputs to `src-dh5/bin/x64/{Debug,Release}/net10.0/` (`VoK.HealersHelper.dll`, `.deps.json`, `.pdb`) and is signed/packaged into `dist/HealersHelper-<Version>-dh5-plugin.zip` by `ci/sign_dh5.py`.
