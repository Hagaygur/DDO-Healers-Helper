# IDE and local development

Open **HealersHelper.sln** in Visual Studio with the .NET desktop development workload and a .NET 10 SDK. It contains five projects:
- `src\HealersHelper.csproj` (`Any CPU`, `net8.0-windows`, DH 4 `4.2.1.0`)
- `src-dh5\HealersHelper.Dh5.csproj` (`x64`, `net10.0`, DH 5 Beta `5.3.0.0`)
- `tests\HealersHelper.Tests.csproj` (`Any CPU`, `net8.0`, portable behavioral assertions & contract snapshot)
- `tests\WindowsSmoke\WindowsSmoke.csproj` (`Any CPU`, `net8.0-windows`, DH 4 UI & DIB smoke suite)
- `tests\Dh5Smoke\Dh5Smoke.csproj` (`x64`, `net10.0`, cross-platform DH 5 ZIP, trust-gate, PE-token & async smoke suite)

`global.json` selects stable .NET 10 SDKs (`10.0.100`, `latestFeature`). Install the .NET 8 Desktop Runtime alongside the .NET 10 SDK to run local DH 4 tests and `WindowsSmoke`.

## Dual SDK resolution

- **DH 4 (`VoK.Sdk.dll` `4.2.1.0`):** Defaults to `%APPDATA%\Dungeon Helper` (or pass `-p:DungeonHelperDir=...`).
- **DH 5 (`VoK.Sdk.dll` `5.3.0.0`):** Uses `DH5_SDK_DIR`, `-p:DungeonHelper5Dir=...`, or `.sdk-cache\dh5`. Run `./ci/Get-Sdk.ps1` (Windows) or `python3 ci/get_sdk_linux.py` (Linux) to populate SHA-256 verified SDKs without committing binaries.

## Build outputs and signing

- **DH 4 (`ci/PackagePlugin.targets`):** Release leaves only `HealersHelper-<Version>-dh4-plugin.zip` in `src/bin/Release/net8.0-windows/`; Debug retains loose files + `.pdb` + ZIP.
- **DH 5 (`ci/sign_dh5.py`):** Builds to `src-dh5/bin/x64/{Debug,Release}/net10.0/`, then signs `VoK.HealersHelper.dll` (ephemeral Windows `signtool.exe` or Linux `osslsigncode`), generates `Healers Helper.pem`, `plugin.metadata`, `.manifest`, and `.manifest.sig`, and writes `dist/HealersHelper-<Version>-dh5-plugin.zip`. Never place `*.key` or `*.pfx` inside the repo or release archives.

## Linux cross-compilation

Install .NET SDK 10, .NET 8 runtime, Python 3, Git, `msitools`, and `osslsigncode` (`sudo apt-get install msitools osslsigncode` on Ubuntu). From the repository root:

```bash
python3 ci/get_sdk_linux.py --destination .sdk-cache
dotnet build HealersHelper.sln -c Release "-p:DungeonHelperDir=$PWD/.sdk-cache/dh4" "-p:DungeonHelper5Dir=$PWD/.sdk-cache/dh5"
python3 ci/check_build_output.py src/bin/Release/net8.0-windows Release --target dh4
python3 ci/check_build_output.py src-dh5/bin/x64/Release/net10.0 Release --target dh5
python3 ci/sign_dh5.py --dll src-dh5/bin/x64/Release/net10.0/VoK.HealersHelper.dll --version 1.5.1 --output dist/HealersHelper-1.5.1-dh5-plugin.zip --allow-unsigned
dotnet run --project tests/Dh5Smoke/Dh5Smoke.csproj -c Release -p:Platform=x64 --no-build -- dist/HealersHelper-1.5.1-dh5-plugin.zip "$PWD/.sdk-cache/dh5"
dotnet run --project tests/HealersHelper.Tests.csproj -c Release --no-build
python3 -m unittest discover -s ci -p 'test_*.py' -v
```

Agent counterpart: [Development instructions](../agents/DEVELOPMENT.md).
