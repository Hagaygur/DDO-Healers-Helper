# Repository instructions for coding agents

This file applies to the entire repository. Read the task-specific documents below before changing the associated code. Explicit user instructions take precedence over these project guidelines. Human documentation starts at [docs/README.md](docs/README.md).

## Objective and current baseline

Build a Dungeon Helper (DH) healer alert plugin (`VoK.HealersHelper`, display name **DDO Healer's Helper**, repo slug `DDO-Healers-Helper`) for Dungeons & Dragons Online (DDO) that watches party, raid, solo, or single-player HP, flashes a high-contrast center-screen banner when a watched player drops below threshold or goes down, and optionally targets that player automatically or via hotkeys. The HUD must remain click-through and compatible with Lossless Scaling (LS). The stable release baseline is 1.0.0; GitHub Actions chooses later versions. Build with the .NET 10 SDK; the runtime target remains .NET 8 Windows and the pinned **VoK.Sdk 4.2.1.0**, not the newest NuGet package.

## Read by task

| Task | Required agent guide | Human counterpart |
| --- | --- | --- |
| Understand requirements and prior evidence | [Context](docs/agents/CONTEXT.md) | [Background](docs/human/BACKGROUND.md) |
| Change C# behavior, HUD, targeting, or UI | [Code map and invariants](docs/agents/CODE.md) | [Architecture](docs/human/ARCHITECTURE.md) |
| Configure solution, IDE tasks, or debugging | [Development](docs/agents/DEVELOPMENT.md) | [IDE setup](docs/human/DEVELOPMENT.md) |
| Build, test, package, version, or troubleshoot | [Execution playbook](docs/agents/WORKFLOW.md) | [Operations](docs/human/OPERATIONS.md) |
| Research DH, SDK, keymaps, or Windows APIs | [Research guidance](docs/agents/REFERENCES.md) | [References](docs/human/REFERENCES.md) |

## Essential constraints

- Installer ZIP: exactly three runtime files under `plugins/HealersHelper/`, DLL first (`VoK.HealersHelper.dll`, `VoK.HealersHelper.deps.json`, `VoK.HealersHelper.metadata`). Never add documentation, source, tests, PDBs, or SDK DLLs to that ZIP. Put source and docs in the separate source archive.
- Keep SDK callbacks short. `HealerState`, `PeopleSavedTracker`, `TargetRestoreTracker`, `HealerStats` (`StreakTracker`, `ReflexTracker`, `RankForSaves`, `FormatChatScorecard`), `HotkeyMatcher`, `BannerPresentation`, `StatusHudPresentation`, and `Settings` are pure portable C#; SDK data collection and targeting belong in `HealerEngine` / `TargetSelector`. Native windows (`BannerHudWindow`, `StatusHudWindow`) share the HUD STA thread (`BannerHudController`).
- Keep `GetPartyMembers()` + self (`GetCurrentCharacterId()` / `GetCurrentCharacter()`) merged: DH `GetPartyMembers()` excludes the local player and caches its property getters, so poll fresh each tick and enrich with `GetEntity(id)` for `LifeState` and live HP.
- Guard `GetSelectedObjectId()` properly: DH 4.2.1.0 returns `(ulong?)0` rather than `null` when nothing is selected. Require `targetId > 0 && targetId < ulong.MaxValue`, non-deleted entity, and `GetSelectedObjectId().GetValueOrDefault() != targetId` before calling `SelectObject(targetId)`.
- Support hotkeys through both `EventProvider.KeyDown` (when DH `HotKeysEnabled` is true) and foreground-gated `GetAsyncKeyState` polling, deduplicated in `HotkeyMatcher` so neither missed events nor double-fires occur.
- Keep the HUD separate from DH's hosted configuration panel. Retain `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`, `WDA_EXCLUDEFROMCAPTURE` (`0x11`), `IsWineRuntime` graceful fallbacks, and `HWND_TOPMOST` reassertion.
- Font sizes are exact integer pixel values (`FontSizePixels`: `18..140`; `StatusFontSizePixels`: `9..48`); `BannerOpacity` clamps to `20..255`. Retain the stable plugin ID (`9b6c8e42-3d19-4f7a-9c54-7e21d804a6b3`) and plugin key (`ddo-healers-helper`). Keep `TogglePluginHotkey` (`Ctrl+Shift+K`) active even when `PluginEnabled` is `false`, preserve offline/last-state `PluginEnabled`/`HudEnabled` persistence, and guard `PeopleSaved` plus untouched `PluginEnabled`/`HudEnabled`/`Mode` states against stale dialog saves.
- A failing test blocks release; it does not justify bumping a version or weakening an assertion. Version contracts are observations of passing behavior, not a universal feature detector.
- State compatibility accurately: tested and confirmed working on live DDO + Dungeon Helper + Lossless Scaling on Windows 11 (not guaranteed to work on any or all configurations), and cross-compiled + CI-tested natively on Linux with Wine/Proton guards (`IsWineRuntime`) so it should work via Wine/Proton while noting live Linux gameplay is untested. Never claim universal hardware/capture guarantees or claim live Linux gameplay has been tested unless actually executed.

## Commands from repository root

```powershell
py -3 -m unittest discover -s ci -p "test_*.py" -v
dotnet run --project tests/HealersHelper.Tests.csproj -c Release -- --contracts dist/contracts.json
.\build.ps1 -DungeonHelperDir "$env:APPDATA\Dungeon Helper" -Version 1.0.0
dotnet run --project tests/WindowsSmoke/WindowsSmoke.csproj -c Release -- src/bin/Release/net8.0-windows/HealersHelper-1.0.0-plugin.zip "$env:APPDATA\Dungeon Helper"
```

Run checks relevant to the change. The solution cross-compiles on Linux with the compatible SDK; only Windows smoke and live DH execution require Windows. Every plugin build creates the installer: Release leaves only the ZIP in `src/bin/Release/net8.0-windows/`, while Debug retains DLL, deps.json, metadata, and PDB alongside the ZIP. `build.ps1` only modifies `src/HealersHelper.csproj`, `src/AssemblyInfo.cs`, and `src/VoK.HealersHelper.metadata` when the requested version differs from the current stamp (preserving LF and UTF-8 without BOM).

## Documentation and completion

- **Mandatory per-version `README.md` & docs sync:** At the end of every version/release, you MUST update `README.md` (features, all `Settings` properties and hotkeys, gamification/options explainer, verified test counts, and `## Release history`) in the same commit, and synchronize `docs/{agents,human}/*`. `ci/test_version.py` (`test_readme_documents_all_settings_hotkeys_and_gamification`) enforces that 100% of public `Settings` properties, all 8 prestige ranks, `### Gamification options explainer`, and `## Release history` remain documented in `README.md`.
- `ci/package.py` must include `docs/` and `AGENTS.md` in source packages while keeping plugin packages runtime-only. State what changed, what ran, what remains untested, and the expected version impact in handoffs. Do not introduce environment-specific local paths, user logs, or downloaded SDK binaries into the repository.
