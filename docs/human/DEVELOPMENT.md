# IDE and local development

Open **HealersHelper.sln** in Visual Studio with the .NET desktop development workload and a .NET 10 SDK. It contains the plugin (`src\HealersHelper.csproj`), portable executable checks (`tests\HealersHelper.Tests.csproj`), and the Windows smoke harness (`tests\WindowsSmoke\WindowsSmoke.csproj`), with Debug and Release configurations. The plugin is a class library hosted by DH, so it cannot be launched directly as a standalone program.

`global.json` selects stable .NET 10 SDKs (`10.0.100`, `latestFeature`). Install the .NET 8 Desktop Runtime for local Windows smoke tests and the .NET 8 runtime for portable tests (installing .NET 8 SDK alongside .NET 10 SDK supplies both). DH bundles its own .NET 8 runtime. `.vsconfig` lists the Visual Studio workload, `.editorconfig` defines formatting, `.gitattributes` normalizes line endings, and `.gitignore` excludes `bin/`, `obj/`, `dist/`, `.build/`, `.vs/`, and `*.user`.

## SDK reference

The plugin resolves `VoK.Sdk.dll` (`4.2.1.0`) from `%APPDATA%\Dungeon Helper` by default. For a different location, set `DungeonHelperDir` or pass `-p:DungeonHelperDir=...`:

```powershell
$env:DungeonHelperDir = 'D:\Apps\Dungeon Helper'
dotnet build HealersHelper.sln -c Debug
# Or explicitly:
dotnet build HealersHelper.sln -c Debug '-p:DungeonHelperDir=D:\Apps\Dungeon Helper'
```

Do not copy `VoK.Sdk.dll` into source control.

## Visual Studio & VS Code

- Set **HealersHelper.Tests** as startup project to run or debug portable behavioral checks (console assertions returning exit code 0/1).
- Run **WindowsSmoke** with arguments `<path-to-plugin-zip-or-dll> <DungeonHelperDir>` to verify ZIP assembly loading, `IDdoPlugin` contract, synthetic 720px toolbar / minimum-size dialog layout (zero clipping), and native banner rendering.
- `.vscode/tasks.json` and `.vscode/launch.json` provide **Build solution (Debug)**, **Test DDO Healer's Helper and emit contracts**, **Test version policy**, **Package plugin (Windows)** (`pwsh`), and **Attach to Dungeon Helper (.NET)**.
- For live DH debugging, close DDO and DH, build Debug, copy the three runtime files plus matching `.pdb` into `%APPDATA%\Dungeon Helper\plugins\HealersHelper\`, start DH/DDO, and attach the managed debugger. Never ship PDBs in release installer ZIPs.

## Build outputs

`ci/PackagePlugin.targets` runs automatically on every plugin build:

| Configuration | Plugin output directory | Contents |
| --- | --- | --- |
| Release | `src/bin/Release/net8.0-windows/` | `HealersHelper-<Version>-plugin.zip` only |
| Debug | `src/bin/Debug/net8.0-windows/` | Installer ZIP, `VoK.HealersHelper.dll`, `.deps.json`, `.metadata`, and `.pdb` |

The installer ZIP always contains strictly three runtime entries under `plugins/HealersHelper/`, DLL first. `build.ps1` stamps `src/HealersHelper.csproj`, `src/AssemblyInfo.cs`, and `src/VoK.HealersHelper.metadata` when the requested version differs, builds Release, validates the 3-entry ZIP, runs `HealersHelper.Tests`, and copies the ZIP into `dist/`.

## Linux cross-compilation

Install .NET SDK 10, .NET 8 runtime, Python 3, Git, and `msitools` (`sudo apt-get install msitools` on Ubuntu). From the repository root:

```bash
python3 ci/get_sdk_linux.py --destination .build/dh-sdk
dotnet build HealersHelper.sln -c Release "-p:DungeonHelperDir=$PWD/.build/dh-sdk"
python3 ci/check_build_output.py src/bin/Release/net8.0-windows Release
dotnet run --project tests/HealersHelper.Tests.csproj -c Release --no-build
python3 -m unittest discover -s ci -p 'test_*.py' -v
```

Cross-compilation produces a Windows plugin (`EnableWindowsTargeting=true`); running the plugin or `WindowsSmoke` requires Windows.

Agent counterpart: [Development instructions](../agents/DEVELOPMENT.md).
