# DDO Healer's Helper 1.0.0

Repository: [**`Hagaygur/DDO-Healers-Helper`**](https://github.com/Hagaygur/DDO-Healers-Helper) (for **DDO Healer's Helper**).

A Dungeon Helper (DH) plugin for Dungeons & Dragons Online (DDO) that monitors party, raid, solo, or single-player HP, flashes a high-contrast center-screen alert banner when a watched player drops below threshold or goes down, and optionally targets the injured player automatically or via hotkeys.

The plugin runs entirely inside Dungeon Helper; no external overlay executable or network service is required. The installer ZIP contains only the three runtime plugin files (`plugins/HealersHelper/VoK.HealersHelper.{dll,deps.json,metadata}`). Full source, tests, and documentation are delivered separately in `HealersHelper-<version>-source.zip`.

## Developer documentation

Open **HealersHelper.sln** in Visual Studio, or open the repository root (`DDO-Healers-Helper`) in VS Code. Start with the [documentation map](docs/README.md), [IDE setup](docs/human/DEVELOPMENT.md), or [AGENTS.md](AGENTS.md) for AI coding assistants.

## Automatic builds

See [GITHUB.md](GITHUB.md) for the included GitHub Actions workflow: first stable release 1.0.0, contract-based semantic versions, parallel Windows and Ubuntu Linux checks, and separate plugin/source release assets.

## Features

- **Master ON/OFF Switch & Mode Switching:**
  - **Toggle ON/OFF (`Ctrl+Shift+K`):** Immediately disables or re-enables alerts, auto-targeting, sounds, and save-tracking from inside DDO even while the DH panel is closed. The master toggle hotkey stays active while disabled so you can toggle back on at any time.
  - **Cycle Mode (`Ctrl+Shift+J`):** Switches between **Party** (`GetPartyMembers()` + self via `GetCurrentCharacterId()` / `GetCurrentCharacter()` enriched with `GetEntity(id)`, supporting solo and 12-player raids) and **Single-Player** (focused player by ID or name). `Off` remains available in settings/panel.
- **Persistent People Saved & Healer Gamification Suite:**
  - **Clutch Saves (`PeopleSavedTracker`):** Detects heals attributed to your casts (`OnHotbarUsage`, self `OnRunScript`, or self `SpellPoints_Current` drop within configurable `SelfCastWindowSeconds`, default `3.5s`, `>= MinHealDelta` HP, default `5`) when the ally survives real damage within `SaveWindowSeconds` (default `5.0s`) exceeding their pre-heal HP (`damageTaken > PreHealHp`, ignoring MaxHP gear swaps and `InTown()`).
  - **Save Streak & Best Streak:** Tracks consecutive clutch saves and resets `CurrentSaveStreak` only on true character death (`LifeState.Dead` outside town, never on healable `-1..-9 HP` `LifeState.Incapacitated`).
  - **Closest Call & 1% Club:** Records your lowest pre-heal HP% clutch save (`ClosestCall*`) and counts `<= 5%` HP saves (`OnePercentClubSaves`) plus `BiggestOverkillSaved`.
  - **Healer Reflex Timer:** Measures `[50..10000 ms]` reaction time from alert onset to your first heal cast (`BestReflexMs`, `AvgReflexMs`).
  - **Prestige Rank Titles & 1-Click DDO Chat Scorecard:** Ranks from *Initiate* (`0`) up to *Immortal Anchor* (`250+`), optionally shown on the Status HUD, plus **Copy Chat Card** producing a pure printable ASCII (`<= 185` chars) summary ready to paste into `/p` or `/raid`. All stats reset together via **Reset Saved**.
- **High-Contrast Flashing Center Banner, Extended Controls & Compact Status HUD:**
  - **Center Alert Banner:** Independent top-level, click-through layered window (`WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`) using a reusable 32bpp premultiplied DIB and `UpdateLayeredWindow`, configurable `FontSizePixels` (`18..140`, default `56 px`), `BannerOpacity` (`80..255`, default `235`), `FlashBanner` toggle, `FlashIntervalMs` (`100..1000 ms`, default `250 ms`), `DeadAlertSeconds`, `MaxAlertSeconds` (`0..120s`), `IncludeSelfInPartyMode`, and `SuppressAlertsInTown`.
  - **Compact Status HUD:** Separate lightweight click-through transparent text overlay (`StatusHudWindow`) on the same STA thread at `StatusFontSizePixels` (`9..48`, default `14 px`), showing `HEALER  OFF`, `HEALER  PARTY <=50%  •  SAVED N (<Rank>)`, `HEALER  PARTY • ALERT  •  SAVED N (<Rank>)`, or `HEALER  FOCUS: <Name> <=50%  •  SAVED N (<Rank>)`, repositionable via **Move Status**.
- **Lossless Scaling Compatibility Design:**
  - Requests Windows capture exclusion (`SetWindowDisplayAffinity(Handle, 0x11)` for `WDA_EXCLUDEFROMCAPTURE` when enabled), verifies affinity readback, periodically reasserts `HWND_TOPMOST` without stealing focus, and permits focus while DDO, DH, or `LosslessScaling` is the foreground process.
  - *Note:* Windows API acceptance (`Capture exclusion: ON`) does not prove live Lossless Scaling compatibility; live testing under WGC and DXGI on Windows is still required.
- **Player Targeting & Hotkeys:**
  - **Auto-target / Target Critical:** Prefers DH `SelectObject(ulong)` (`Auto` / `SdkSelectOnly`), which communicates directly with `dndpl.dll` via MMF without relying on party keybinds. Optional `PartyKeyOnly` mode uses `IKeymap.GetHotkey` + `SendKey` (`TARGET_SELF_KEY` / `TARGET_FELLOW_{slot}_KEY`, noting that default `ddo.keymap` typically binds only F1–F6 and leaves self and slots 7–12 unbound).
  - **Hotkeys:** Default `Ctrl+Shift+K` (Master ON/OFF toggle), `Ctrl+Shift+H` (Focus current player target), `Ctrl+Shift+J` (Cycle Party/SinglePlayer), `Ctrl+Shift+T` (Target lowest/critical watched player), plus optional `ResetSavedHotkey` and `CopyScorecardHotkey` (with `Reset Defaults` and conflict detection in `Settings...`). Combines `EventProvider.KeyDown`/`KeyUp` with foreground-gated `GetAsyncKeyState` edge polling and deduplication so hotkeys work even when DH's global hotkey toggle is off.
- **Directly Installable Runtime ZIP:**
  - `HealersHelper-<version>-plugin.zip` contains strictly the three runtime files under `plugins/HealersHelper/`, DLL first.

## Install or update

1. Close DDO. In Dungeon Helper, choose **Settings -> Add plugin from zip file** and select `HealersHelper-X.Y.Z-plugin.zip` directly (never the `-source.zip`).
2. If prompted because `HealersHelper` already exists, choose **Yes** to overwrite.
3. Fully exit DH from its tray icon, restart it, then launch DDO.
4. Open the **DDO Healer's Helper** panel in DH.

If DH refuses the update or reports a file lock, close DDO and DH completely, extract `plugins/HealersHelper/*` into `%APPDATA%\Dungeon Helper\plugins\HealersHelper\`, and restart DH.

Compiled against **VoK.Sdk 4.2.1.0**, matching Dungeon Helper 4.2.0.146's installed SDK. No SDK DLL or .NET runtime is redistributed.

## Configure and play

- Use the DH control panel for **Toggle ON/OFF**, **Cycle Mode**, **Focus Target**, **Target Critical**, **Preview Banner**, **Move Banner**, **Move Status**, **Reset Saved**, and **Settings...**.
- **Move Banner** and **Move Status** temporarily enable dragging (`HTCAPTION`) for 20 seconds, then restore click-through (`HTTRANSPARENT`) and save normalized monitor-relative coordinates (`HudX`/`HudY` and `StatusHudX`/`StatusHudY`).
- Close the DH control panel during play: both HUD windows stay active on their STA thread while DDO, DH, or Lossless Scaling is focused.

## Verification status and limits

- **Verified by IL/binary inspection & CI:** Dungeon Helper 4.2.0.146 (`VoK.Sdk 4.2.1.0`) behavior for `GetPartyMembers()` (excludes self, cached getters, no 6/12 cap), `IEntity` (`HitPoints_Current`, `LifeState`, `EntityExtensions.IsPlayer`), `SelectObject(ulong)` (MMF, no subscription check), `GetSelectedObjectId()` (`0` when unselected), `EventProvider.KeyDown`/`KeyUp` (`HotKeysEnabled`), local `ddo.keymap` bindings, and GitHub-hosted Actions (`linux` + `build` + `release`, verified on `v1.0.0`).
- **Unverified at authoring time:** Live DDO gameplay and live Lossless Scaling capture modes (WGC/DXGI). See [docs/human/OPERATIONS.md](docs/human/OPERATIONS.md) for the live verification checklist.

## Building from source

On Windows with the .NET 10 SDK, Python 3, and Dungeon Helper installed:

```powershell
.\build.ps1
# For a nonstandard DH installation:
.\build.ps1 -DungeonHelperDir 'D:\Apps\Dungeon Helper'
```

See [docs/human/DEVELOPMENT.md](docs/human/DEVELOPMENT.md#linux-cross-compilation) for Linux cross-compilation instructions. The plugin runtime itself is Windows-only.

References: [SetWindowDisplayAffinity](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity), [UpdateLayeredWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow), [Extended Window Styles](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles), [Dungeon Helper](https://dungeonhelper.com/).

Unofficial community plugin; not an official Dungeon Helper or Standing Stone Games release.
