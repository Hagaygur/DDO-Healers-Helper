# DDO Healer's Helper

[**Download Latest Release (`HealersHelper-X.Y.Z-plugin.zip`)**](https://github.com/Hagaygur/DDO-Healers-Helper/releases/latest) • Repository: [**`Hagaygur/DDO-Healers-Helper`**](https://github.com/Hagaygur/DDO-Healers-Helper)

A Dungeon Helper (DH) plugin for Dungeons & Dragons Online (DDO) that monitors party, raid, solo, or single-player HP, flashes a high-contrast center-screen alert banner when a watched player drops below threshold or goes down, tracks clutch saves and healer prestige stats, and optionally targets the injured player automatically or via hotkeys.

The plugin runs entirely inside Dungeon Helper; no external overlay executable or network service is required. The installer ZIP contains only the three runtime plugin files (`plugins/HealersHelper/VoK.HealersHelper.{dll,deps.json,metadata}`). Full source, tests, and documentation are delivered separately in `HealersHelper-<version>-source.zip`.

## Developer documentation

Open **HealersHelper.sln** in Visual Studio, or open the repository root (`DDO-Healers-Helper`) in VS Code. Start with the [documentation map](docs/README.md), [IDE setup](docs/human/DEVELOPMENT.md), or [AGENTS.md](AGENTS.md) for AI coding assistants.

## Automatic builds

See [GITHUB.md](GITHUB.md) for the included GitHub Actions workflow: first stable release `1.0.0`, contract-based semantic versions, parallel Windows and Ubuntu Linux checks, and separate plugin/source release assets.

## Features

- **Master ON/OFF Switch & Mode Switching:**
  - **Toggle ON/OFF (`Ctrl+Shift+K`):** Immediately disables or re-enables alerts, auto-targeting, sounds, and save-tracking from inside DDO even while the DH panel is closed. The master toggle hotkey stays active while disabled so you can toggle back on at any time.
  - **Cycle Mode (`Ctrl+Shift+J`):** Switches between **Party** (`GetPartyMembers()` + self via `GetCurrentCharacterId()` / `GetCurrentCharacter()` enriched with `GetEntity(id)`, supporting solo and 12-player raids) and **Single-Player** (focused player by ID or name). `Off` remains available in settings/panel.
- **Persistent People Saved & Healer Gamification Suite:**
  - **Clutch Saves (`PeopleSavedTracker`):** Detects heals attributed to your casts (`OnHotbarUsage`, self `OnRunScript`, or self `SpellPoints_Current` drop within configurable `SelfCastWindowSeconds`, default `3.5 s`, `>= MinHealDelta` HP, default `5`) when the ally survives real damage within `SaveWindowSeconds` (default `5.0 s`) exceeding their pre-heal HP (`damageTaken > PreHealHp`, ignoring MaxHP gear/buff changes and `InTown()`).
  - **Save Streak, Closest Call, 1% Club & Reflex Timer:** Tracks consecutive deathless clutch saves, lowest pre-heal HP% records, `<= 5.0%` HP saves, biggest overkill absorbed, and `[50..10000 ms]` alert-to-cast reaction times.
  - **Prestige Rank Titles & 1-Click DDO Chat Scorecard:** 8 prestige ranks from *Initiate* (`0`) up to *Immortal Anchor* (`250+`), optionally shown on the Status HUD, plus **Copy Chat Card** producing a single-line pure printable ASCII (`<= 185` chars) summary ready to paste into `/p` or `/raid`. All live stats reset together via **Reset Saved**.
- **High-Contrast Flashing Center Banner, Extended Controls & Compact Status HUD:**
  - **Center Alert Banner:** Independent top-level, click-through layered window (`WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`) using a reusable 32bpp premultiplied DIB and `UpdateLayeredWindow`, with exact pixel font sizing (`FontSizePixels`), configurable `BannerOpacity`, `FlashBanner`, `FlashIntervalMs`, `DeadAlertSeconds`, `MaxAlertSeconds`, `IncludeSelfInPartyMode`, and `SuppressAlertsInTown`.
  - **Compact Status HUD:** Separate lightweight click-through transparent text overlay (`StatusHudWindow`) on the same STA thread at `StatusFontSizePixels` (`9..48 px`, default `14 px`), showing `HEALER  OFF`, `HEALER  PARTY <=50%  •  SAVED N (<Rank>)`, `HEALER  PARTY • ALERT  •  SAVED N (<Rank>)`, or `HEALER  FOCUS: <Name> <=50%  •  SAVED N (<Rank>)`, repositionable via **Move Status**.
- **Lossless Scaling Compatibility Design:**
  - Requests Windows capture exclusion (`SetWindowDisplayAffinity(Handle, 0x11)` for `WDA_EXCLUDEFROMCAPTURE` when enabled), verifies affinity readback, periodically reasserts `HWND_TOPMOST` without stealing focus, and permits visibility while DDO, DH, or `LosslessScaling` is the foreground process.
  - *Note:* Windows API acceptance (`Capture exclusion: ON`) does not prove live Lossless Scaling compatibility; live testing under WGC and DXGI on Windows is still required.
- **Player Targeting & Hotkeys:**
  - **Auto-target / Target Critical:** Prefers DH `SelectObject(ulong)` (`Auto` / `SdkSelectOnly`), which communicates directly with `dndpl.dll` via MMF without relying on party keybinds. Optional `PartyKeyOnly` mode uses `IKeymap.GetHotkey` + `SendKey` (`TARGET_SELF_KEY` / `TARGET_FELLOW_{slot}_KEY`, noting that default `ddo.keymap` typically binds only F1–F6 and leaves self and slots 7–12 unbound).
- **Directly Installable Runtime ZIP:**
  - `HealersHelper-<version>-plugin.zip` contains strictly the three runtime files under `plugins/HealersHelper/`, DLL first.

## Hotkeys

All 6 hotkeys are customizable in **Settings...** (`Hotkeys` section):

| Action | Setting | Default | Behavior |
| --- | --- | --- | --- |
| **Toggle Plugin ON/OFF** | `TogglePluginHotkey` | `Ctrl+Shift+K` | Flips master `PluginEnabled` ON/OFF from inside DDO (remains active while plugin is OFF) |
| **Focus Current Target** | `FocusTargetHotkey` | `Ctrl+Shift+H` | Locks `SinglePlayer` focus mode onto your currently selected DDO player character |
| **Cycle Mode** | `ToggleModeHotkey` | `Ctrl+Shift+J` | Toggles tracking mode between `Party` (party/raid + self) and `SinglePlayer` |
| **Target Critical Now** | `TargetCriticalHotkey` | `Ctrl+Shift+T` | Immediately selects the prioritized critical/low watched player (bypasses cooldown) |
| **Reset People Saved & Stats** | `ResetSavedHotkey` | `None` | Zeroes `PeopleSaved`, streaks, closest call, 1% club, overkill, and reflex stats |
| **Copy DDO Chat Scorecard** | `CopyScorecardHotkey` | `None` | Copies your single-line printable ASCII healer scorecard (`<= 185` chars) to clipboard |

- **Recording & conflict protection:** Click any hotkey box in **Settings...** and press your desired chord (`Ctrl` / `Shift` / `Alt` + key). Press `Backspace` (or click **Clear**) to unbind (`None`), press `Esc` to cancel recording, or click **Reset Defaults** to restore the defaults above. Duplicate non-empty chords across the 6 actions are rejected before saving, and panel button tooltips dynamically display your bound chords.
- **Reliable in-game capture:** Combines `EventProvider.KeyDown`/`KeyUp` with foreground-gated Win32 `GetAsyncKeyState` rising-edge polling and a 250 ms deduplication window so hotkeys work even when Dungeon Helper's global hotkey toggle is turned off.

## Configurable settings reference

Settings persist to `%APPDATA%\Dungeon Helper\plugins\HealersHelper\healers-helper.json` (atomic `.tmp` write + `Validate()` clamping) and are grouped into 5 sections in **Settings...**:

| Group | Setting | Default | Valid range / options | Description |
| --- | --- | --- | --- | --- |
| **HP Alert & Tracking** | `PluginEnabled` | `true` | `true` / `false` | Master ON/OFF switch for alerts, sounds, auto-targeting, and clutch-save tracking |
| **HP Alert & Tracking** | `Mode` | `Party` | `Off`, `Party`, `SinglePlayer` | Watched player scope (all party/raid members + self, focused player, or silent) |
| **HP Alert & Tracking** | `IncludeSelfInPartyMode` | `true` | `true` / `false` | Include your own character (slot 0) when evaluating `Party` mode alerts |
| **HP Alert & Tracking** | `HpThresholdPercent` | `50` | `5`–`95` (`%`) | HP percentage at or below which a `LowHp` alert begins |
| **HP Alert & Tracking** | `RecoveryHysteresisPercent` | `3` | `0`–`20` (`%`) | Extra HP% above `HpThresholdPercent` needed to clear an active `LowHp` alert (`53%` at default) |
| **HP Alert & Tracking** | `DeadAlertSeconds` | `6` | `0`–`60` (`s`) | Duration to show a `CriticalDown` (`0` / negative HP / incapacitated / dead) banner (`0` suppresses immediately) |
| **HP Alert & Tracking** | `MaxAlertSeconds` | `0` | `0`–`120` (`s`, `0` = unlimited) | Optional cap per continuous `LowHp` alert on the same player (`CriticalDown` still escalates) |
| **HP Alert & Tracking** | `SuppressAlertsInTown` | `false` | `true` / `false` | Silence HP/down alerts and sounds in public zones (`InTown()`) while keeping manual notices |
| **Clutch Saves & Gamification** | `SaveWindowSeconds` | `5.0` | `1.0`–`15.0` (`s`) | Post-heal window in which surviving cumulative `damageTaken > PreHealHp` counts a clutch save |
| **Clutch Saves & Gamification** | `SelfCastWindowSeconds` | `3.5` | `1.0`–`15.0` (`s`) | Attribution window after your hotbar use, self script cast, or SP drop to credit incoming heals |
| **Clutch Saves & Gamification** | `MinHealDelta` | `5` | `1`–`200` (`HP`) | Minimum positive HP increase required to open/extend a clutch-heal window (ignores passive regen) |
| **Clutch Saves & Gamification** | `ShowSavedNotice` | `true` | `true` / `false` | Show a brief green `SAVED <Name>!` celebration notice when idle after a clutch save |
| **Clutch Saves & Gamification** | `ShowRankOnStatusHud` | `true` | `true` / `false` | Append your prestige rank (`SAVED N (<Rank>)`) on the compact Status HUD |
| **Center Banner, Status HUD & LS** | `FontSizePixels` | `56` | `18`–`140` (`px`, integer) | Exact integer pixel font height of the center alert banner primary line |
| **Center Banner, Status HUD & LS** | `BannerOpacity` | `235` | `80`–`255` (alpha) | Background alpha intensity of the center alert banner |
| **Center Banner, Status HUD & LS** | `HudEnabled` | `true` | `true` / `false` | Show/hide the center flashing alert banner window |
| **Center Banner, Status HUD & LS** | `FlashBanner` | `true` | `true` / `false` | Alternate alert banner colors each `FlashIntervalMs` (`false` holds steady primary frame) |
| **Center Banner, Status HUD & LS** | `FlashIntervalMs` | `250` | `100`–`1000` (`ms`) | Flash phase alternation interval for `CriticalDown` and `LowHp` banners |
| **Center Banner, Status HUD & LS** | `SoundOnAlert` | `false` | `true` / `false` | Play a system alert chime once per new `AlertGeneration` |
| **Center Banner, Status HUD & LS** | `StatusHudEnabled` | `true` | `true` / `false` | Show/hide the compact click-through Status HUD text line |
| **Center Banner, Status HUD & LS** | `StatusFontSizePixels` | `14` | `9`–`48` (`px`, integer) | Exact integer pixel font height of the compact Status HUD |
| **Center Banner, Status HUD & LS** | `ExcludeFromCapture` | `true` | `true` / `false` | Request `SetWindowDisplayAffinity(..., 0x11)` (`WDA_EXCLUDEFROMCAPTURE`) on both HUD windows |
| **Automatic Player Targeting** | `AutoTargetOnAlert` | `true` | `true` / `false` | Automatically select the prioritized critical/low watched player on a new alert |
| **Automatic Player Targeting** | `TargetingMethod` | `Auto` | `Off`, `Auto`, `SdkSelectOnly`, `PartyKeyOnly` | Selection backend (`Auto` tries `SelectObject(ulong)` first, then bound party keybind fallback) |
| **Automatic Player Targeting** | `AutoTargetCooldownSeconds` | `3.0` | `0.5`–`30.0` (`s`) | Minimum delay between automatic target switches (manual `Target Critical` bypasses cooldown) |

*(HUD screen positions `HudX`/`HudY` default to `0.50`/`0.42` and `StatusHudX`/`StatusHudY` default to `0.50`/`0.76`, clamped to `0.0`–`1.0`, and are updated interactively via **Move Banner** and **Move Status**.)*

## Healer gamification & prestige ranks

Every clutch save updates your persisted healer profile and unlocks prestige titles based on `PeopleSaved`:

| Clutch saves (`PeopleSaved`) | Prestige rank title |
| --- | --- |
| `0` | **Initiate** |
| `1–4` | **Field Medic** |
| `5–14` | **Combat Healer** |
| `15–29` | **Lifeweaver** |
| `30–59` | **Beacon of Hope** |
| `60–99` | **Soulguard** |
| `100–249` | **Reaper's Bane** |
| `250+` | **Immortal Anchor** |

- **Deathless Save Streak (`CurrentSaveStreak` / `BestSaveStreak`):** Increments on every clutch save and resets `CurrentSaveStreak` to `0` **only** when a watched player reaches true character death (`LifeState.Dead`) outside town (`!InTown()`). Dropping into DDO's healable unconscious range (`-1..-9 HP` with `LifeState.Incapacitated`) never resets your streak, and unrevived corpses leaving/re-entering entity range are latched so they never double-reset.
- **Closest Call & 1% Club (`OnePercentClubSaves`, `BiggestOverkillSaved`):** Clutch saves landed while an ally was at **`<= 5.0%` HP** count toward your **1% Club** total. `ClosestCall*` records your lowest pre-heal HP% save (breaking ties by lower raw `PreHealHp`), and `BiggestOverkillSaved` records the largest excess lethal damage absorbed (`damageTaken - PreHealHp`).
- **Healer Reflex Timer (`BestReflexMs` / `AvgReflexMs` / `ReflexSampleCount`):** Measures reaction time in **`50..10000 ms`** from a new `AlertGeneration` onset to your first self heal/cast event (`< 50 ms` pre-casts already in motion are ignored without consuming the active alert; at most one sample is recorded per alert).
- **DDO-Safe Printable ASCII Chat Scorecard (`Copy Chat Card`):** Generates a single-line summary using strictly printable ASCII (`0x20..0x7E`, `<= 185` characters) so DDO's chat font never renders Unicode `?` boxes when pasted into `/p`, `/raid`, or `/g`:

```text
[Healer's Helper] Lifeweaver: 18 saved | Streak 6 (Best 11) | 1% Club: 3 | Closest: Tanky 2.4% (19HP) | Reflex 340ms (210ms best) | Max Save +412HP
```

## Install or update

1. Close DDO. In Dungeon Helper, choose **Settings -> Add plugin from zip file** and select `HealersHelper-X.Y.Z-plugin.zip` directly (never the `-source.zip`).
2. If prompted because `HealersHelper` already exists, choose **Yes** to overwrite.
3. Fully exit DH from its tray icon, restart it, then launch DDO.
4. Open the **DDO Healer's Helper** panel in DH.

If DH refuses the update or reports a file lock, close DDO and DH completely, extract `plugins/HealersHelper/*` into `%APPDATA%\Dungeon Helper\plugins\HealersHelper\`, and restart DH.

Compiled against **VoK.Sdk 4.2.1.0**, matching Dungeon Helper 4.2.0.146's installed SDK. No SDK DLL or .NET runtime is redistributed.

## Configure and play

- Use the DH control panel for **Toggle ON/OFF**, **Cycle Mode**, **Focus Target**, **Target Critical**, **Preview Banner**, **Move Banner**, **Move Status**, **Copy Chat Card**, **Reset Saved**, and **Settings...**.
- **Move Banner** and **Move Status** temporarily enable dragging (`HTCAPTION`) for 20 seconds, then restore click-through (`HTTRANSPARENT`) and save normalized monitor-relative coordinates (`HudX`/`HudY` and `StatusHudX`/`StatusHudY`).
- Close the DH control panel during play: both HUD windows stay active on their STA thread while DDO, DH, or Lossless Scaling is focused.

## Verification status and limits

- **Verified by IL/binary inspection, unit/smoke suites & CI:** Dungeon Helper 4.2.0.146 (`VoK.Sdk 4.2.1.0`) behavior for `GetPartyMembers()` (excludes self, cached getters, no 6/12 cap), `IEntity` (`HitPoints_Current`, `LifeState`, `EntityExtensions.IsPlayer`), `SelectObject(ulong)` (MMF, no subscription check), `GetSelectedObjectId()` (`0` when unselected), `EventProvider.KeyDown`/`KeyUp` (`HotKeysEnabled`), local `ddo.keymap` bindings, **175** portable C# assertions (`HealersHelper.Tests`), **24** Python CI/packaging/semver checks, **4** `WindowsSmoke` WinForms/layered-HUD/ZIP suites, WSL Ubuntu 22.04 cross-compilation, and GitHub-hosted Actions (`linux` + `build` + `release`, verified across `v1.0.0`–`v1.1.0`).
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
