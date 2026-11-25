# Agent IDE and development instructions

Human counterpart: [IDE setup](../human/DEVELOPMENT.md).

- `HealersHelper.sln` contains `src\HealersHelper.csproj`, `tests\HealersHelper.Tests.csproj`, and `tests\WindowsSmoke\WindowsSmoke.csproj`; keep project paths and GUID configurations aligned when adding projects.
- `.vsconfig` declares the Visual Studio managed desktop workload.
- `global.json` selects stable .NET 10 SDKs (`10.0.100`, `latestFeature`). Runtime targets remain `net8.0-windows` (plugin/smoke) and `net8.0` (portable tests) to match DH's bundled .NET 8 runtime. CI installs SDKs 8 and 10 so .NET 8 tests can run; do not retarget to `net10.0` until the DH host supports it.
- The plugin is a DH-loaded class library. Never invent a direct executable launch configuration for it. Use `HealersHelper.Tests`, `WindowsSmoke`, or attach to the DH process.
- SDK resolution uses `DungeonHelperDir`, otherwise `%APPDATA%\Dungeon Helper`. For a nonstandard install, set `DungeonHelperDir` or pass `-p:DungeonHelperDir=...`.
- Tests are console executables returning non-zero exit codes on failure, not xUnit/MSTest adapters. Preserve the documented `dotnet run` checks.
- `.vscode/tasks.json` and `.vscode/launch.json` configure Debug builds, portable C#/Python checks, `WindowsSmoke`, and `build.ps1` packaging via `pwsh`. Never auto-launch DDO or overwrite locked plugin DLLs.
- Keep `.editorconfig`, `.gitattributes`, and `.gitignore` project-scoped. Never commit `.build/`, `dist/`, `bin/`, `obj/`, or downloaded SDK binaries.
- Linux can cross-compile the complete solution (`EnableWindowsTargeting=true`) and run `HealersHelper.Tests` + Python CI tests; `WindowsSmoke` and live DH execution require Windows.

## Platform metadata and build outputs

`src/AssemblyInfo.cs` explicitly declares `[assembly: SupportedOSPlatform("windows7.0")]` and `[assembly: TargetFramework(".NETCoreApp,Version=v8.0", FrameworkDisplayName = ".NET 8.0")]` because `GenerateAssemblyInfo` and `GenerateTargetFrameworkAttribute` are `false` in `src/HealersHelper.csproj`. Keep both attributes to prevent CA1416 warnings-as-errors.

`ci/PackagePlugin.targets` runs after every non-design-time build of `src/HealersHelper.csproj`:
- Release retains ONLY `HealersHelper-<Version>-plugin.zip` in `src/bin/Release/net8.0-windows/`.
- Debug retains `VoK.HealersHelper.dll`, `VoK.HealersHelper.deps.json`, `VoK.HealersHelper.metadata`, `VoK.HealersHelper.pdb`, and `HealersHelper-<Version>-plugin.zip` in `src/bin/Debug/net8.0-windows/`.
- The ZIP always contains strictly the three runtime entries under `plugins/HealersHelper/`, DLL first; PDBs stay outside it.
