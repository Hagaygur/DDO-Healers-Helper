# DDO Healer's Helper

[**Download Latest Release (`HealersHelper-X.Y.Z-plugin.zip`)**](https://github.com/Hagaygur/DDO-Healers-Helper/releases/latest) • Repository: [**`Hagaygur/DDO-Healers-Helper`**](https://github.com/Hagaygur/DDO-Healers-Helper)

A Dungeon Helper (DH) plugin for Dungeons & Dragons Online (DDO) that monitors party, raid, solo, or single-player HP, displays a customizable click-through center-screen alert banner (or runs in silent banner-off targeting/stats mode), tracks clutch saves and healer prestige stats, and automatically targets injured allies and restores your original target once everyone is healed.

The plugin runs entirely inside Dungeon Helper; no external overlay executable or network service is required. The installer ZIP contains only the three runtime plugin files (`plugins/HealersHelper/VoK.HealersHelper.{dll,deps.json,metadata}`). Full source, tests, and documentation are delivered separately in `HealersHelper-<version>-source.zip`.

> **Compatibility & Platform Status:** Tested and confirmed working on live **DDO + Dungeon Helper + Lossless Scaling on Windows 11** (not guaranteed to work on any or all OS, GPU, display, or capture configurations). Cross-compiled and CI-tested natively on **Linux**, with Wine/Proton-safe User32/GDI32 fallbacks (`IsWineRuntime`) so it **should also work on Linux via Wine/Proton** (live Linux gameplay has not yet been tested).

## Developer documentation

Open **HealersHelper.sln** in Visual Studio, or open the repository root (`DDO-Healers-Helper`) in VS Code. Start with the [documentation map](docs/README.md), [IDE setup](docs/human/DEVELOPMENT.md), or [AGENTS.md](AGENTS.md) for AI coding assistants.

## Automatic builds

See [GITHUB.md](GITHUB.md) for the included GitHub Actions workflow: first stable release `1.0.0`, contract-based semantic versions, parallel Windows and Ubuntu Linux checks, automated `README.md` settings/gamification sync verification, and separate plugin/source release assets.

## Features

- **Master ON/OFF Switch, Mode Switching & Guaranteed Last-State Persistence:**
  - **Toggle ON/OFF (`Ctrl+Shift+K`):** Immediately disables or re-enables alerts, auto-targeting, sounds, and save-tracking from inside DDO even while the DH panel is closed. The master toggle hotkey stays active while disabled so you can toggle back on at any time.
  - **Offline & Restart Persistence:** Both `PluginEnabled` and `HudEnabled` (`Banner: ON/OFF`) persist immediately to `%APPDATA%\Dungeon Helper\plugins\HealersHelper\healers-helper.json` even when toggled in the DH panel before DDO connects (`OfflineSettingsEngineView`), and `Settings...` tracks per-field dirty state so in-game hotkey toggles are never clobbered on save.
  - **Cycle Mode (`Ctrl+Shift+J`):** Switches between **Party** (`GetPartyMembers()` + self via `GetCurrentCharacterId()` / `GetCurrentCharacter()` enriched with `GetEntity(id)`, supporting solo and 12-player raids) and **Single-Player** (`FocusedPlayerId` / `FocusedPlayerName`). `Off` remains available in settings/panel.
- **First-Class Option to Disable the Center Banner Entirely:**
  - Toggle the center flashing banner OFF in one click via **Banner: ON / OFF** on the DH panel, via the optional **Toggle Center Banner** hotkey (`ToggleBannerHotkey`), or via **Enable center alert banner** (`HudEnabled`) in **Settings...**.
  - When `HudEnabled` is `false`, all live center banner frames (`CriticalDown`, `LowHp`, `Notice`) are completely suppressed and cleared, while **auto-targeting, post-heal target restoration, alert chimes (`SoundOnAlert`), the compact Status HUD, and clutch-save gamification remain 100% active** (and **Preview Banner** / **Move / Resize Banner** still work on demand).
- **Interactive Banner Resizing, Custom Colors & Wide Opacity (`20–255`):**
  - **Move / Resize Banner (20s):** Unlocks the center banner (`WM_NCHITTEST -> HTCLIENT` with `MA_NOACTIVATE` so DDO keeps focus) with corner L-bracket grips: drag the center to move (`HudX`, `HudY`), drag any edge or corner (anchored on the opposite edge/corner) or scroll the **mouse wheel** (`±2 px`) to resize (`FontSizePixels` `18..140 px`) with proportional box scaling, and hold **`Ctrl + Mouse Wheel`** (`±10`) to adjust `BannerOpacity` (`20..255`) live on screen.
  - **6 Custom Hex Colors + Reset:** Customize `LowHpBgHex`, `LowHpBorderHex`, `CriticalBgHex`, `CriticalBorderHex`, `BannerTextHex`, and `FlashAltBgHex` via color-picker swatches in **Settings...** (with one-click **Reset Colors**).
- **5-Rule Healing-Target Priority & Automatic Target Restoration:**
  - **Auto-target / Target Critical:** Prefers DH `SelectObject(ulong)` (`Auto` / `SdkSelectOnly`) directly over MMF, with `PartyKeyOnly` (`IKeymap.GetHotkey` + `SendKey`) fallback.
  - **Strict Healing Priority & Post-Heal Restoration (`TargetRestoreTracker`):** Remembers your prior target (e.g. raid boss or main tank) when switching to an injured ally, chains multi-injury switches (`Boss -> Alice -> Bob -> Boss`), enforces a strict 5-rule safety contract (expected-target verification before/during `TryRestore`, tight `0.8 s` MMF settle guard, universal hold whenever any alert or watched living ally is `<= HpThresholdPercent` or incapacitated even if `AutoTargetOnAlert` is off or banners timed out, instant cooldown release via `IsPreviousTargetHealedOrGone` once the previous ally recovers so the next injured ally is targeted immediately, and mutually exclusive alert vs. restore tick branches), rejects dead monster corpses, and restores after `RestoreTargetDelaySeconds` (`0.2 s`).
- **Persistent People Saved & Healer Gamification Suite:**
  - Tracks clutch saves (`PeopleSaved`), deathless streaks (`CurrentSaveStreak` / `BestSaveStreak`), `<= 5.0%` **1% Club** saves (`OnePercentClubSaves`), closest calls (`ClosestCallPlayerName` / `ClosestCallPreHealPercent` / `ClosestCallPreHealHp`), biggest overkill absorbed (`BiggestOverkillSaved`), `[50..10000 ms]` alert-to-cast reaction times (`BestReflexMs` / `AvgReflexMs` / `ReflexSampleCount`), 8 prestige ranks, and a 1-click pure ASCII **Copy Chat Card** (`<= 185` chars).
- **Lossless Scaling & Layered HUD Architecture:**
  - Independent top-level click-through layered windows (`WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`) using reusable 32bpp premultiplied DIB surfaces (`UpdateLayeredWindow`), `SetWindowDisplayAffinity(Handle, 0x11)` (`WDA_EXCLUDEFROMCAPTURE` when `ExcludeFromCapture` is enabled, with graceful `IsWineRuntime` fallback on Linux/Wine), and periodic `HWND_TOPMOST` reassertion.

## Hotkeys

All **7** hotkeys are customizable in **Settings...** (`Hotkeys` section):

| Action | Setting | Default | Behavior |
| --- | --- | --- | --- |
| **Toggle Plugin ON/OFF** | `TogglePluginHotkey` | `Ctrl+Shift+K` | Flips master `PluginEnabled` ON/OFF from inside DDO (remains active while plugin is OFF) |
| **Toggle Center Banner** | `ToggleBannerHotkey` | `None` | Flips `HudEnabled` ON/OFF from inside DDO while keeping targeting, sounds, Status HUD, and stats active |
| **Focus Current Target** | `FocusTargetHotkey` | `Ctrl+Shift+H` | Locks `SinglePlayer` focus mode onto your currently selected DDO player character |
| **Cycle Mode** | `ToggleModeHotkey` | `Ctrl+Shift+J` | Toggles tracking mode between `Party` (party/raid + self) and `SinglePlayer` |
| **Target Critical Now** | `TargetCriticalHotkey` | `Ctrl+Shift+T` | Immediately selects the prioritized critical/low watched player (bypasses cooldown) |
| **Reset People Saved & Stats** | `ResetSavedHotkey` | `None` | Zeroes `PeopleSaved`, streaks, closest call, 1% club, overkill, and reflex stats |
| **Copy DDO Chat Scorecard** | `CopyScorecardHotkey` | `None` | Copies your single-line printable ASCII healer scorecard (`<= 185` chars) to clipboard |

- **Recording & conflict protection:** Click any hotkey box in **Settings...** and press your desired chord (`Ctrl` / `Shift` / `Alt` + key). Press `Backspace` (or click **Clear**) to unbind (`None`), press `Esc` to cancel recording, or click **Reset Defaults** to restore defaults. Duplicate non-empty chords across all 7 actions are rejected before saving, and DH panel tooltips dynamically display your bound chords.
- **Reliable in-game capture:** Combines `EventProvider.KeyDown`/`KeyUp` with foreground-gated Win32 `GetAsyncKeyState` rising-edge polling and a 250 ms deduplication window so hotkeys work even when Dungeon Helper's global hotkey toggle is turned off.

## Configurable settings reference

Settings persist to `%APPDATA%\Dungeon Helper\plugins\HealersHelper\healers-helper.json` (atomic `.tmp` write + `Validate()` clamping) across 5 sections in **Settings...** plus interactive HUD placement and live stats:

| Group | Setting | Default | Valid range / options | Description |
| --- | --- | --- | --- | --- |
| **HP Alert & Tracking** | `PluginEnabled` | `true` | `true` / `false` | Master ON/OFF switch for alerts, sounds, auto-targeting, and clutch-save tracking (persists across restarts and offline panel toggles) |
| **HP Alert & Tracking** | `Mode` | `Party` | `Off`, `Party`, `SinglePlayer` | Watched player scope (all party/raid members + self, focused player, or silent) |
| **HP Alert & Tracking** | `IncludeSelfInPartyMode` | `true` | `true` / `false` | Include your own character (slot 0) when evaluating `Party` mode alerts |
| **HP Alert & Tracking** | `FocusedPlayerId` | `0` | `ulong` (`0` = unset) | Instance ID of the focused player in `SinglePlayer` mode (set via **Focus Target** or panel selector) |
| **HP Alert & Tracking** | `FocusedPlayerName` | `""` | Trimmed string | Character name fallback for `SinglePlayer` focus when entity ID changes across instances |
| **HP Alert & Tracking** | `HpThresholdPercent` | `50` | `5`–`95` (`%`) | HP percentage at or below which a `LowHp` alert begins |
| **HP Alert & Tracking** | `RecoveryHysteresisPercent` | `3` | `0`–`20` (`%`) | Extra HP% above `HpThresholdPercent` needed to clear an active `LowHp` alert (`53%` at default) |
| **HP Alert & Tracking** | `DeadAlertSeconds` | `6` | `0`–`60` (`s`) | Duration to show a `CriticalDown` (`0` / negative HP / incapacitated / dead) banner (`0` suppresses immediately) |
| **HP Alert & Tracking** | `MaxAlertSeconds` | `0` | `0`–`120` (`s`, `0` = unlimited) | Optional cap per continuous `LowHp` alert on the same player (`CriticalDown` still escalates) |
| **HP Alert & Tracking** | `SuppressAlertsInTown` | `false` | `true` / `false` | Silence HP/down alerts and sounds in public zones (`InTown()`) while keeping manual notices |
| **Clutch Saves & Gamification** | `SaveWindowSeconds` | `5.0` | `1.0`–`15.0` (`s`) | Post-heal window in which surviving cumulative `damageTaken > PreHealHp` counts a clutch save |
| **Clutch Saves & Gamification** | `SelfCastWindowSeconds` | `3.5` | `1.0`–`15.0` (`s`) | Attribution window after your hotbar use, self script cast, or SP drop to credit incoming heals |
| **Clutch Saves & Gamification** | `MinHealDelta` | `5` | `1`–`200` (`HP`) | Minimum positive HP increase required to open/extend a clutch-heal window or satisfy manual top-off restore |
| **Clutch Saves & Gamification** | `ShowSavedNotice` | `true` | `true` / `false` | Show a brief `SAVED <Name>!` celebration notice when idle after a clutch save |
| **Clutch Saves & Gamification** | `ShowRankOnStatusHud` | `true` | `true` / `false` | Append your prestige rank (`SAVED N (<Rank>)`) on the compact Status HUD |
| **Center Banner, Status HUD & LS** | `HudEnabled` | `true` | `true` / `false` | Enable/disable the center alert banner completely (targeting, restore, chimes, Status HUD & stats stay active when `false`) |
| **Center Banner, Status HUD & LS** | `FontSizePixels` | `56` | `18`–`140` (`px`, integer) | Exact integer pixel font height of the center banner primary line (also adjustable via edge/corner drag or wheel) |
| **Center Banner, Status HUD & LS** | `BannerOpacity` | `235` | `20`–`255` (alpha) | Background alpha intensity of the center alert banner (also adjustable via `Ctrl + Mouse Wheel` in edit mode) |
| **Center Banner, Status HUD & LS** | `FlashBanner` | `true` | `true` / `false` | Alternate alert banner colors each `FlashIntervalMs` (`false` holds steady primary frame) |
| **Center Banner, Status HUD & LS** | `FlashIntervalMs` | `250` | `100`–`1000` (`ms`) | Flash phase alternation interval for `CriticalDown` and `LowHp` banners |
| **Center Banner, Status HUD & LS** | `SoundOnAlert` | `false` | `true` / `false` | Play a system alert chime once per new `AlertGeneration` |
| **Center Banner, Status HUD & LS** | `LowHpBgHex` | `"#B84200"` | `#RRGGBB` hex | Primary background color for `LowHp` alert banners |
| **Center Banner, Status HUD & LS** | `LowHpBorderHex` | `"#FFE066"` | `#RRGGBB` hex | Primary border accent color for `LowHp` alert banners |
| **Center Banner, Status HUD & LS** | `CriticalBgHex` | `"#960A14"` | `#RRGGBB` hex | Primary background color for `CriticalDown` (`DOWN`) alert banners |
| **Center Banner, Status HUD & LS** | `CriticalBorderHex` | `"#FFD644"` | `#RRGGBB` hex | Primary border accent color for `CriticalDown` (`DOWN`) alert banners |
| **Center Banner, Status HUD & LS** | `BannerTextHex` | `"#FFFFFF"` | `#RRGGBB` hex | Primary text color for `LowHp` and `CriticalDown` alert banners |
| **Center Banner, Status HUD & LS** | `FlashAltBgHex` | `"#0A0A0E"` | `#RRGGBB` hex | Alternate dark flash-off background color when `FlashBanner` alternates |
| **Center Banner, Status HUD & LS** | `HudX` / `HudY` | `0.50` / `0.42` | `0.0`–`1.0` | Normalized monitor-relative coordinates of the center alert banner (updated via **Move / Resize Banner**) |
| **Center Banner, Status HUD & LS** | `StatusHudEnabled` | `true` | `true` / `false` | Show/hide the compact click-through Status HUD text line |
| **Center Banner, Status HUD & LS** | `StatusFontSizePixels` | `14` | `9`–`48` (`px`, integer) | Exact integer pixel font height of the compact Status HUD |
| **Center Banner, Status HUD & LS** | `StatusHudX` / `StatusHudY` | `0.50` / `0.76` | `0.0`–`1.0` | Normalized monitor-relative coordinates of the compact Status HUD (updated via **Move Status**) |
| **Center Banner, Status HUD & LS** | `ExcludeFromCapture` | `true` | `true` / `false` | Request `SetWindowDisplayAffinity(..., 0x11)` (`WDA_EXCLUDEFROMCAPTURE`) on both HUD windows |
| **Automatic Player Targeting** | `AutoTargetOnAlert` | `true` | `true` / `false` | Automatically select the prioritized critical/low watched player on a new alert (releases cooldown immediately once the prior ally heals) |
| **Automatic Player Targeting** | `TargetingMethod` | `Auto` | `Off`, `Auto`, `SdkSelectOnly`, `PartyKeyOnly` | Selection backend (`Auto` tries `SelectObject(ulong)` first, then bound party keybind fallback) |
| **Automatic Player Targeting** | `AutoTargetCooldownSeconds` | `3.0` | `0.5`–`30.0` (`s`) | Minimum delay between auto-target switches while the current ally is still unhealed |
| **Automatic Player Targeting** | `RestorePreviousTargetAfterHeal` | `true` | `true` / `false` | Automatically re-select your prior target (e.g. boss/mob/tank) once all watched living allies are healthy |
| **Automatic Player Targeting** | `RestoreTargetDelaySeconds` | `0.2` | `0.0`–`5.0` (`s`) | Post-heal settle delay before restoring your previous target (resets if HP dips again) |

*(Persisted live gamification fields stored in `healers-helper.json` and reset together via **Reset Saved**: `PeopleSaved`, `CurrentSaveStreak`, `BestSaveStreak`, `ClosestCallPlayerName`, `ClosestCallPreHealPercent`, `ClosestCallPreHealHp`, `BiggestOverkillSaved`, `OnePercentClubSaves`, `BestReflexMs`, `AvgReflexMs`, and `ReflexSampleCount`.)*

## Healer gamification, options & prestige ranks

### Gamification options explainer

All gamification behaviors can be tuned in **Settings... -> Clutch Saves & Gamification** (plus optional hotkeys in **Hotkeys**) to match your class, spell rotation, and difficulty tier (Reaper skulls vs. raids):

- **`SaveWindowSeconds` (`1.0`–`15.0 s`, default `5.0 s` — *Save window*):** Controls how long after your attributed heal an ally remains inside an active "clutch-save protection window." If the ally takes cumulative enemy damage during this window exceeding their pre-heal HP (`damageTaken > PreHealHp`) and survives (`HitPoints > 0`, `!IsDeadOrDown`), you earn `+1` clutch save (and the window closes so one heal never double-counts). Raise toward `7–10 s` for slower boss tankbusters or heal-over-time (Aura / Positive Energy Burst / Vigorous) builds; lower toward `3–4 s` for strict burst-only Reaper credit.
- **`SelfCastWindowSeconds` (`1.0`–`15.0 s`, default `3.5 s` — *Cast window*):** Attribution window after your character activates a hotbar action (`OnHotbarUsage`), runs a self script (`OnRunScript`), or spends spell points (`SpellPoints_Current` drop). Only positive HP jumps arriving within `SelfCastWindowSeconds` of your own cast open or extend a save window, filtering out another healer's heals or self-potions when you haven't cast. Raise to `5–8 s` if you cast long-travel projectile heals (e.g. Alchemist curative admixtures) or lingering HoTs; lower to `2.0–2.5 s` for instant Quicken rays.
- **`MinHealDelta` (`1`–`200 HP`, default `5 HP` — *Min heal HP*):** Minimum positive HP gain required to count as a real heal rather than passive health regeneration, temp-HP noise, or minor vampirism ticks (also used by `TargetRestoreTracker` when verifying manual top-offs above threshold). Increase to `25–80 HP` at legendary cap (`3,000+ HP` pools) so fast passive regen ticks after a buff cast cannot open a save window; keep `5–15 HP` during heroic leveling.
- **`ShowSavedNotice` (`true` / `false`, default `true` — *Show 'SAVED!' banner notice*):** Displays a brief non-flashing `SAVED <Name>! (Saved: N)` notice banner for `2.0 s` when no active `LowHp`/`CriticalDown` alert is competing for attention. Turn `false` if you want silent background tracking with zero celebration popups (or disable the entire center banner via `HudEnabled = false`).
- **`ShowRankOnStatusHud` (`true` / `false`, default `true` — *Show prestige rank on Status HUD*):** Toggles whether the compact click-through Status HUD appends your earned prestige title (`HEALER  PARTY <=50%  •  SAVED 18 (Lifeweaver)`) or renders a minimal counter (`SAVED 18`).
- **`CopyScorecardHotkey` / **Copy Chat Card** (`None` by default):** Copies a single-line, strictly printable ASCII (`0x20..0x7E`, `<= 185` chars) summary of your rank, total saves, streak, 1% Club count, closest call, reflex speed, and max overkill saved to the Windows clipboard so you can paste directly into DDO `/p`, `/raid`, or `/g` chat with zero `?` Unicode glyph boxes.
- **`ResetSavedHotkey` / **Reset Saved** (`None` by default):** Atomically zeroes all 11 persisted live gamification fields (`PeopleSaved`, `CurrentSaveStreak`, `BestSaveStreak`, `ClosestCallPlayerName`, `ClosestCallPreHealPercent`, `ClosestCallPreHealHp`, `BiggestOverkillSaved`, `OnePercentClubSaves`, `BestReflexMs`, `AvgReflexMs`, `ReflexSampleCount`) at the start of a raid night, Reaper push, or static group run without altering any of your alert, HUD, color, or targeting settings.

### Tracked stats, mechanics & prestige ranks

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

- **Clutch Saves (`PeopleSaved`):** Increments when an ally inside your attributed `SaveWindowSeconds` window absorbs cumulative real enemy damage strictly greater than their pre-heal HP floor (`damageTaken > PreHealHp`) and remains alive (ignoring pure MaxHP gear/Rage swaps and `InTown()` zones). Chained heals preserve the lowest `PreHealHp` floor while extending the window.
- **Deathless Save Streak (`CurrentSaveStreak` / `BestSaveStreak`):** Increments on every clutch save and resets `CurrentSaveStreak` to `0` **only** when a watched player reaches true character death (`LifeState.Dead`) outside town (`!InTown()`). Dropping into DDO's healable unconscious range (`-1..-9 HP` with `LifeState.Incapacitated`) never resets your streak, and unrevived corpses leaving/re-entering entity range are latched so they never double-reset.
- **Closest Call & 1% Club (`OnePercentClubSaves`, `ClosestCallPlayerName`, `ClosestCallPreHealPercent`, `ClosestCallPreHealHp`):** Clutch saves landed while an ally was at **`<= 5.0%` HP** increment `OnePercentClubSaves`. `ClosestCallPlayerName`, `ClosestCallPreHealPercent`, and `ClosestCallPreHealHp` record your lowest pre-heal HP% save (breaking equal-percentage ties by lower raw `PreHealHp`).
- **Biggest Overkill Absorbed (`BiggestOverkillSaved`):** Records the largest single lethal margin (`damageTaken - PreHealHp`) your clutch heal prevented from killing an ally.
- **Healer Reflex Timer (`BestReflexMs` / `AvgReflexMs` / `ReflexSampleCount`):** Measures reaction time in **`50..10000 ms`** from a new `AlertGeneration` onset to your first self heal/cast event (`< 50 ms` pre-casts already in motion are ignored without consuming the active alert; at most one sample is recorded per alert).
- **DDO-Safe Printable ASCII Chat Scorecard (`Copy Chat Card`):**

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

- Use the DH control panel for **Toggle ON/OFF**, **Banner: ON / OFF**, **Cycle Mode**, **Focus Target**, **Target Critical**, **Preview Banner**, **Move / Resize Banner**, **Move Status**, **Copy Chat Card**, **Reset Saved**, and **Settings...**.
- **Move / Resize Banner** unlocks the center banner for 20 seconds (extending automatically while you drag or scroll): drag the interior to move (`HudX`, `HudY`), drag any corner/edge or roll the mouse wheel to resize (`FontSizePixels`), and hold `Ctrl + Mouse Wheel` to adjust `BannerOpacity` (`20..255`), then restores click-through (`HTTRANSPARENT`). **Move Status** unlocks the compact status HUD for 20 seconds (`StatusHudX`, `StatusHudY`).
- Close the DH control panel during play: both HUD windows stay active on their STA thread while DDO, DH, or Lossless Scaling is focused.

## Verification status and limits

- **Windows 11 live gameplay & Lossless Scaling:** Tested and confirmed working on live **DDO + Dungeon Helper + Lossless Scaling on Windows 11**. Because hardware, Windows display/capture stacks (WGC vs. DXGI), GPU drivers, and overlay hooks vary, compatibility is **not guaranteed on any or all configurations**.
- **Linux / Wine / Proton:** Cross-compiles and passes portable C# and Python CI suites natively on Ubuntu Linux (`EnableWindowsTargeting=true` + `msitools`), and includes `IsWineRuntime` User32/GDI32 guards (`wine_get_version` detection for capture-affinity and Wayland/Gamescope `GetForegroundWindow` quirks). It **should also work on Linux under Wine/Proton**, though **live Linux gameplay has not yet been tested**.
- **Automated binary, unit, smoke & CI verification:** Dungeon Helper 4.2.0.146 (`VoK.Sdk 4.2.1.0`) IL/metadata inspection, **233** portable C# assertions (`HealersHelper.Tests`), **25** Python CI/packaging/semver/README-sync checks (`ci/test_*.py`), **4** `WindowsSmoke` WinForms/layered-HUD/interactive-resize/offline-persistence/ZIP suites, WSL Ubuntu 22.04 cross-compilation, and GitHub-hosted Actions (`linux` + `build` + `release`).

## Release history

- **`v1.3.0`:** Added 5-rule strict healing-target priority (expected-target live verification, tight `0.8 s` MMF settle guard, universal zero-alert/zero-unhealed-ally hold, instant post-heal cooldown release, and mutually exclusive alert/restore tick branches), guaranteed offline/restart active & banner state persistence with dirty-tracked settings saves, 1-click/hotkey/settings full center-banner disable (`ToggleBannerButton` / `ToggleBannerHotkey` / `HudEnabled`), interactive banner edge/corner drag resizing + mouse-wheel font sizing + `Ctrl+wheel` opacity (`20..255`) + 6 custom hex colors, Wine/Proton runtime guards (`IsWineRuntime`), expanded gamification options documentation, and automated CI `README.md` sync checks.
- **`v1.2.0`:** Added automatic post-heal previous-target restoration (`TargetRestoreTracker`, `RestorePreviousTargetAfterHeal`, `RestoreTargetDelaySeconds`), multi-injury chaining (`Boss -> Alice -> Bob -> Boss`), manual-override cancellation, and dead non-player corpse rejection.
- **`v1.1.0`:** Added the healer gamification suite (8 prestige ranks, deathless save streak, Closest Call, 1% Club, `[50..10000 ms]` reflex timer, pure ASCII `Copy Chat Card` scorecard) and extended configurability (`IncludeSelfInPartyMode`, `SuppressAlertsInTown`, `MaxAlertSeconds`, `BannerOpacity`, `FlashBanner`, `SaveWindowSeconds`, `SelfCastWindowSeconds`, `MinHealDelta`, `ResetSavedHotkey`, `CopyScorecardHotkey`).
- **`v1.0.1`:** Carbon High audit hardening (cumulative multi-hit clutch-save accounting, MaxHP gear/Rage guards, negative-coordinate secondary-monitor layered HUD positioning, hotkey recorder `Esc`/`Backspace`/duplicate validation, fast-dispose safety).
- **`v1.0.0`:** Initial stable release with party/raid/solo/single-player HP monitoring, click-through 32bpp `UpdateLayeredWindow` flashing center banner + compact Status HUD, `WDA_EXCLUDEFROMCAPTURE` Lossless Scaling support, `SelectObject(ulong)` targeting, hybrid `KeyDown` + `GetAsyncKeyState` hotkeys, and 3-entry runtime ZIP packaging.

## Building from source

On Windows with the .NET 10 SDK, Python 3, and Dungeon Helper installed:

```powershell
.\build.ps1
# For a nonstandard DH installation:
.\build.ps1 -DungeonHelperDir 'D:\Apps\Dungeon Helper'
```

See [docs/human/DEVELOPMENT.md](docs/human/DEVELOPMENT.md#linux-cross-compilation) for Linux cross-compilation instructions.

References: [SetWindowDisplayAffinity](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity), [UpdateLayeredWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow), [Extended Window Styles](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles), [Dungeon Helper](https://dungeonhelper.com/).

Unofficial community plugin; not an official Dungeon Helper or Standing Stone Games release.
