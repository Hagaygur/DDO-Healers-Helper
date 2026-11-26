# Repository instructions for coding agents

This file applies to the entire repository. Read the task-specific documents below before changing the associated code. Explicit user instructions take precedence over these project guidelines. Human documentation starts at [docs/README.md](docs/README.md).

## Objective and dual-target baseline

Build and maintain **DDO Healer's Helper** (`VoK.HealersHelper`, repo slug `DDO-Healers-Helper`) for Dungeons & Dragons Online (DDO) across both Dungeon Helper hosts:
- **DH 4 (`src/`, `net8.0-windows`, `AnyCPU`)**: Out-of-process WinForms + click-through 32bpp DIB HUD (`Lossless Scaling` compatible) targeting pinned **VoK.Sdk 4.2.1.0**, plugin ID `9b6c8e42-3d19-4f7a-9c54-7e21d804a6b3`, plugin key `ddo-healers-helper`.
- **DH 5 Beta (`src-dh5/`, `net10.0`, `x64`)**: In-process DX10/11 `ImGuiNET` plugin targeting pinned **VoK.Sdk 5.3.0.0**, `[PluginId("54b48548-05ff-48cf-bce1-c6270e3d4a3f")]`, `[PluginName("Healers Helper")]`, `[AssemblyCompany("Warned")]`, `[PluginIcon("Icon.png")]` (36x36 embedded PNG).
- **Shared Core (`src/*.cs` linked into `src-dh5/` and `tests/HealersHelper.Tests.csproj`)**: `Contracts.cs`, `UiThemePalette.cs`, `Settings.cs`, `OfflineSettingsEngineView.cs`, `HotkeyMatcher.cs`, `HealerStats.cs`, `PeopleSavedTracker.cs`, `TargetRestoreTracker.cs`, `HealerState.cs`, `BannerPresentation.cs`, and `StatusHudPresentation.cs` must stay 100% portable C# with zero WinForms/Win32 P/Invoke.

Build with the .NET 10 SDK (`global.json`); do not merge `src/` and `src-dh5/` into one ZIP (archive layouts, TFMs, SDK contracts, and trust gates are mutually incompatible).

## Read by task

| Task | Required agent guide | Human counterpart |
| --- | --- | --- |
| Understand requirements and prior evidence | [Context](docs/agents/CONTEXT.md) | [Background](docs/human/BACKGROUND.md) |
| Change C# behavior, HUD, targeting, or UI | [Code map and invariants](docs/agents/CODE.md) | [Architecture](docs/human/ARCHITECTURE.md) |
| Configure solution, IDE tasks, or debugging | [Development](docs/agents/DEVELOPMENT.md) | [IDE setup](docs/human/DEVELOPMENT.md) |
| Build, test, package, version, or troubleshoot | [Execution playbook](docs/agents/WORKFLOW.md) | [Operations](docs/human/OPERATIONS.md) |
| Research DH, SDK, keymaps, or Windows APIs | [Research guidance](docs/agents/REFERENCES.md) | [References](docs/human/REFERENCES.md) |

## Essential constraints

- **Dual plugin ZIPs & strict secret hygiene**:
  - DH 4 ZIP (`HealersHelper-<ver>-dh4-plugin.zip`): exactly 3 runtime files under `plugins/HealersHelper/`, DLL first (`VoK.HealersHelper.dll`, `VoK.HealersHelper.deps.json`, `VoK.HealersHelper.metadata`).
  - DH 5 ZIP (`HealersHelper-<ver>-dh5-plugin.zip`): exact 8 signed files at archive root (`Healers Helper.entitlement`, `Healers Helper.entitlement.sig`, `Healers Helper.manifest`, `Healers Helper.manifest.sig`, `Healers Helper.pem`, `VoK.HealersHelper.deps.json`, `VoK.HealersHelper.dll`, `plugin.metadata`).
  - **NEVER** commit, print, or package `*.key`, `*.pfx`, `signing.*`, `.sdk-cache/`, PDBs, docs, or `VoK.Sdk.dll` inside plugin or source ZIPs. `ci/sign_dh5.py` must clean temporary signing directories in `finally:`.
- **Keep SDK callbacks short and deadlock-free**:
  - DH 4: keep callbacks short; poll `GetPartyMembers()` + self (`GetCurrentCharacterId()` / `GetCurrentCharacter()`) and enrich with `GetEntity(id)` for `LifeState` and `HitPoints_Temp`.
  - DH 5: **NEVER** call `.Result` / `.GetAwaiter().GetResult()` on frame-marshaled SDK calls inside `ClientFrameStep()`, `ImGui_Render()`, or `lock`. Hydrate live `LifeState`, `Death_SoulStoneID`, and `HitPoints_IncapacitatedBonus` via `await entity.UpdateProperties([0x10000E0Cu, 0x10001868u, 0x10008250u])` in `Dh5HealerEngine.TickAsync()`, track connection state via `eventProvider.Party`, and override `IPlugin.WindowVisible` (`false` by default). Zero `System.Windows.Forms` or `System.Media` references in `src-dh5/`.
- **3-State Life/Death Triage & Targetless Quick-Cast**:
  - Incapacitated across DDO's scaling unconscious range (via `LifeStatusEvaluator`: live `LifeState` + `Death_SoulStoneID` in range; sticky confirmed death -> cached `HitPoints_IncapacitatedBonus` -> `FallbackDeathHp` out of range) outranks Living Low HP, which outranks Dead Soulstone (calm Spectral Violet `DEAD — RAISE`, never steals auto-target from living allies).
  - Guard `GetSelectedObjectId()` properly (`(ulong?)0` when unselected; require `targetId > 0 && targetId < ulong.MaxValue`, `!IsDeleted`, `GetSelectedObjectId().GetValueOrDefault() != targetId`).
  - In DH 5, `InvokeHotbar(bar, slot, targetId)` defaults to disabled (`QuickHealHotbar = 0`, `QuickRaiseHotbar = 0`) until configured.
- **Hotkeys, HUD, Themes & Compatibility**:
  - Deduplicate hotkeys in `HotkeyMatcher`; keep `TogglePluginHotkey` (`Ctrl+Shift+K`) active when `PluginEnabled` is `false`; preserve offline persistence (`OfflineSettingsEngineView`) and dirty-tracked saves.
  - DH 4 HUDs keep `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`, `WDA_EXCLUDEFROMCAPTURE` (`0x11`), `IsWineRuntime` guards, and `HWND_TOPMOST`. Font sizes are exact integer pixels (`FontSizePixels`: `18..140`, `StatusFontSizePixels`: `9..48`); `BannerOpacity` clamps `20..255`.
  - Share `UiTheme` (`EberronGold = 0`, `CannithArcane = 1`, `FeywildTwilight = 2`) from `src/UiThemePalette.cs` across DH 4 and DH 5 while keeping `v1.3.0` custom banner colors/opacity/disable and `13/8/6`-arg presentation constructors intact (preserving `minor` SemVer).
  - State compatibility accurately: DH 4 is tested live on DDO + DH + Lossless Scaling on Windows 11 (not guaranteed on all configs), cross-compiled on Linux with `IsWineRuntime` guards (live Linux gameplay untested); DH 5 Beta trust gate and ALC loading are verified locally via `TrustProbe` and `Dh5Smoke` against `VoK.Sdk 5.3.0.0` (live DH 5 DX10/11 gameplay untested here).

## Commands from repository root

```powershell
py -3 -m unittest discover -s ci -p "test_*.py" -v
dotnet run --project tests/HealersHelper.Tests.csproj -c Release -- --contracts dist/contracts.json
.\ci\Get-Sdk.ps1
.\build.ps1 -Version 1.5.0 -Dh5KeyFile C:\path\to\author.key
dotnet run --project tests/WindowsSmoke/WindowsSmoke.csproj -c Release -- dist/HealersHelper-1.5.0-dh4-plugin.zip "$env:DH_SDK_DIR"
dotnet run --project tests/Dh5Smoke/Dh5Smoke.csproj -c Release -p:Platform=x64 -- dist/HealersHelper-1.5.0-dh5-plugin.zip "$env:DH5_SDK_DIR" --require-signed
```

## Documentation and completion

- **Mandatory per-version `README.md` & docs sync:** At the end of every version/release, update `README.md` (features, all `Settings` properties, all 8 prestige ranks, `### Gamification options explainer`, verified test counts, and `## Release history`) and synchronize `docs/{agents,human}/*` in the same commit. `ci/test_version.py` enforces 100% `Settings.cs` coverage and secret hygiene automatically.
