# DDO Healer's Helper

[**Download Latest Release**](https://github.com/Hagaygur/DDO-Healers-Helper/releases/latest) • Repository: [**`Hagaygur/DDO-Healers-Helper`**](https://github.com/Hagaygur/DDO-Healers-Helper)

A Dungeon Helper (DH) plugin for Dungeons & Dragons Online (DDO) supporting **both Dungeon Helper 4 (`4.2.x`, `.NET 8 Windows`) and Dungeon Helper 5 Beta (`5.0.x` / `VoK.Sdk 5.3.0`, `.NET 10 x64` in-process ImGui)**. It monitors party, 12-player raid, solo, or focused single-player hit points, distinguishes healable **Incapacitated** allies across DDO's scaling unconscious range from **Dead Soulstones** (using live `LifeState` + `Death_SoulStoneID` in entity range, and sticky confirmed-dead latching -> cached `HitPoints_IncapacitatedBonus` -> configurable `FallbackDeathHp` out of range), tracks **Temporary HP shields (`HitPoints_Temp`)** and healer clutch saves/prestige ranks, and supports both DH 4 auto-targeting with 5-rule target restoration and DH 5 Beta **targetless `InvokeHotbar` quick-healing** without dropping your Boss/Tank target.

> **Choose the right ZIP for your Dungeon Helper install:**
> - **Dungeon Helper 4 (`4.2.x`):** Install `HealersHelper-X.Y.Z-dh4-plugin.zip` (**Settings -> Add plugin from zip file**).
> - **Dungeon Helper 5 Beta (`5.0.x`):** Install `HealersHelper-X.Y.Z-dh5-plugin.zip` (**Plugins -> Install from zip**; Authenticode + community entitlement signed).
> - **Never install `HealersHelper-X.Y.Z-source.zip` inside Dungeon Helper.**

> **Compatibility & Platform Status:** Tested and confirmed working on live **DDO + Dungeon Helper 4 + Lossless Scaling on Windows 11** (not guaranteed to work on any or all OS, GPU, display, or capture configurations). Cross-compiled and CI-tested natively on **Linux**, with Wine/Proton-safe User32/GDI32 fallbacks (`IsWineRuntime`) so DH 4 **should also work on Linux via Wine/Proton** (live Linux gameplay has not yet been tested). The **DH 5 Beta** bundle (`net10.0` `x64` `ImGuiNET`, zero WinForms) is verified locally against DH `5.0.858` (`VoK.Sdk 5.3.0.0`) via `PluginTrustGate`, `AuthenticodeVerifier`, `DungeonHelper.App` ZIP installation, and `Dh5Smoke` (requires DX10 or DX11 in DDO; live DH 5 Beta in-game rendering has not yet been tested here).

## Developer documentation

Open **HealersHelper.sln** in Visual Studio, or open the repository root (`DDO-Healers-Helper`) in VS Code. Start with the [documentation map](docs/README.md), [IDE setup](docs/human/DEVELOPMENT.md), or [AGENTS.md](AGENTS.md) for AI coding assistants.

## Automatic builds

See [GITHUB.md](GITHUB.md) for the GitHub Actions workflow: dual DH 4 (`4.2.1.0`) + DH 5 (`5.3.0.0`) SHA-256 SDK locking, ephemeral Authenticode + manifest signing (`ci/sign_dh5.py`), contract-based semantic versions, parallel Windows and Ubuntu Linux checks, automated `README.md` settings/gamification sync verification, and separate plugin/source release assets.

## Features

- **3-State DDO Life/Death Triage & Temp HP Shields (`HitPoints_Temp`):**
  - **Incapacitated > Living Low HP > Dead Soulstone:** In DDO, unconscious range scales well past `-10 HP` via Diehard, feats, enhancements, Epic Past Lives, and gear. In entity range, live `LifeState` (`0x10000E0C`) and `Death_SoulStoneID` (`0x10001868`) keep unconscious allies (`LifeState.Incapacitated`) healable at any negative HP (`DOWN: <Name>` / `UNCONSCIOUS (HEALABLE)` in urgent crimson-gold), whereas `LifeState.Dead` / Soulstone detection identifies true deaths requiring Raise Dead/Resurrection (`DEAD — RAISE <Name>` in calm, non-flashing Spectral Violet `#2A1538` with Bone-Silver `#D8DCE6` border). Out of entity range (or when live `LifeState` is unavailable), fallback uses latched confirmed death -> cached `HitPoints_IncapacitatedBonus` (`0x10008250`, personal floor `-10 - bonus`) -> configurable `FallbackDeathHp` (default `-200`, `0` = disabled). Incapacitated and living low-HP allies always outrank Soulstones so a dead player never steals auto-targeting from a living tank.
  - **Temporary HP Shields (`HitPoints_Temp`):** Displays `+N THP` (e.g., *Blood Tribute*, *Shining Through*, *Legendary Affirmation*, *Rejuvenation Cocoon*) in Sovereign Gold (`#F5C542`) on the banner secondary line and as a gold shield bar cap on both the DH 4 banner HP strip and the DH 5 ImGui Raid HUD. Optional `IncludeTempHpInThreshold` lets you factor THP into low-HP trigger math.
  - **Per-Session Player Mute/Ignore & Town Suppression:** Mute self-sustaining Warforged, Pale Master undead shrouds, or Dark Apostates for the current session (DH 4 panel **Mute Player** / row double-click; DH 5 middle-click row or `/hh mute <name>`), and optionally silence alerts in public zones (`SuppressAlertsInTown`).
- **3 Shared DDO × Material Design 3 Themes (`UiTheme`):**
  - Switch live across both DH 4 (WinForms panels + status chrome) and DH 5 Beta (`ImGuiNET` windows, tab underlines, cards, and HUD plates) via **Settings -> Theme** or `/hh theme <eberron|cannith|feywild>`. All three palettes avoid crimson UI chrome so Critical/Incap (`#FF4D5A`) alerts pop preattentively:
    1. **`EberronGold` (*Eberron Obsidian & Gold*, default):** Surfaces `#111318` / `#1A1D24` / `#222630` / `#2C313E`, outline `#4A5368`, primary `#E2B75B` (Sovereign Gold), secondary `#56CCF2` (Astral Cyan), text `#ECEEF4` / `#A9B1C3`.
    2. **`CannithArcane` (*House Cannith Arcane Forge*):** Surfaces `#0E141D` / `#151E2B` / `#1D293A` / `#27364B`, outline `#455D7A`, primary `#56CCF2` (Arcane Aether Cyan), secondary `#D4A359` (Forged Bronze), text `#EAF2FA` / `#9EB2C8`.
    3. **`FeywildTwilight` (*Feywild Eldritch Twilight*):** Surfaces `#15111E` / `#1E182B` / `#282039` / `#332949`, outline `#584875`, primary `#CDA4FF` (Eldritch Amethyst), secondary `#6EE7B7` (Sylvan Verdigris), text `#F2ECF9` / `#B8ABC9`.
- **DH 5 Beta Exclusive In-Process Upgrades (`VoK.Sdk 5.3.0` + `ImGuiNET`):**
  - **Targetless Quick-Heal / Quick-Raise (`InvokeHotbar`):** Configure `QuickHealHotbar`/`QuickHealSlot` and `QuickRaiseHotbar`/`QuickRaiseSlot` (disabled at `0` by default for safety) to cast heals or raises directly onto an injured ally via hotkey (`QuickCastCriticalHotkey`), `/hh heal`, or left-clicking a Raid HUD row **without dropping your Boss/Tank target or breaking mouselook**.
  - **Optional Always-On Triage-Sorted Raid HUD (`RaidHudEnabled`):** Compact 1-column party / 2-column 12-player raid grid (`>= 28 px` rows per WCAG 2.5.8) sorted by urgency with HP fill, Sovereign Gold TempHP cap, Astral Blue (`#4DA6FF`) party SP slivers, CVD-safe state badges (`INCAP` / `DEAD` / `LOW`), Left-Click Quick-Cast, Right-Click Select/Focus, and Middle-Click Mute.
  - **Live `LifeState` Hydration, Party Events & `/hh` Chat Commands:** Polls `UpdateProperties([0x10000E0Cu, 0x10001868u, 0x10008250u])` (`Death_LifeState`, `Death_SoulStoneID`, `HitPoints_IncapacitatedBonus`) asynchronously without blocking DDO's frame loop, tracks disconnects via `eventProvider.Party`, and supports `/hh` (`/healershelper`) chat commands.
- **Master ON/OFF Switch, Mode Switching & Guaranteed Last-State Persistence:**
  - **Toggle ON/OFF (`Ctrl+Shift+K`):** Immediately disables or re-enables alerts, auto-targeting, sounds, and save-tracking from inside DDO even while the panel is closed (remains active while OFF).
  - **Offline & Restart Persistence (`OfflineSettingsEngineView`):** Both `PluginEnabled` and `HudEnabled` (`Banner: ON/OFF`) persist immediately to `healers-helper.json` even when toggled before DDO connects, and `Settings...` tracks per-field dirty state so in-game hotkey toggles are never clobbered on save.
  - **Cycle Mode (`Ctrl+Shift+J`):** Switches between **Party** (`GetPartyMembers()` + self via `GetCurrentCharacterId()` enriched with `GetEntity`, supporting solo and 12-player raids) and **Single-Player** (`FocusedPlayerId` / `FocusedPlayerName`).
- **First-Class Center Banner Disable, Interactive Resizing & Custom Colors:**
  - Toggle the center flashing banner OFF via **Banner: ON / OFF**, `ToggleBannerHotkey`, or `HudEnabled` while keeping auto-targeting, post-heal restoration, chimes (`SoundOnAlert`), Status HUD, Raid HUD, and clutch-save gamification 100% active.
  - **Move / Resize Banner (20s):** Drag the interior to move (`HudX`, `HudY`), drag any edge/corner or roll the **mouse wheel** (`±2 px`) to resize (`FontSizePixels` `18..140 px`), and hold **`Ctrl + Mouse Wheel`** (`±10`) to adjust `BannerOpacity` (`20..255`), plus 6 customizable `#RRGGBB` colors (`LowHpBgHex`, `LowHpBorderHex`, `CriticalBgHex`, `CriticalBorderHex`, `BannerTextHex`, `FlashAltBgHex`) and spatial HP bar (`ShowBannerHpBar`).
- **5-Rule Healing-Target Priority, Raid Keymap Readiness & Automatic Target Restoration:**
  - Prefers DH `SelectObject(ulong)` (`Auto` / `SdkSelectOnly`), shows **Raid Keymap Readiness (`TARGET_FELLOW_7_KEY`..`12_KEY`)** in Settings, enforces all 5 `TargetRestoreTracker` safety rules (`ExpectedCurrentTargetId`, `0.8 s` settle grace, universal hold while any living ally needs healing, instant cooldown release via `IsPreviousTargetHealedOrGone`, mutually exclusive alert/restore branches), rejects dead monster corpses, and restores after `RestoreTargetDelaySeconds` (`0.2 s`).
- **Persistent People Saved & Healer Gamification Suite:**
  - Tracks clutch saves (`PeopleSaved`), deathless streaks (`CurrentSaveStreak` / `BestSaveStreak`), `<= 5.0%` **1% Club** saves (`OnePercentClubSaves`), closest calls (`ClosestCallPlayerName` / `ClosestCallPreHealPercent` / `ClosestCallPreHealHp`), biggest overkill absorbed (`BiggestOverkillSaved`), `[50..10000 ms]` alert-to-cast reaction times (`BestReflexMs` / `AvgReflexMs` / `ReflexSampleCount`), 8 prestige ranks, and a 1-click pure ASCII **Copy Chat Card** (`<= 185` chars).
- **Lossless Scaling & Layered HUD Architecture (DH 4) / In-Frame ImGui (DH 5):**
  - DH 4 uses independent top-level click-through layered windows (`WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`) with 32bpp premultiplied DIB surfaces (`UpdateLayeredWindow`), `SetWindowDisplayAffinity(Handle, 0x11)` (`WDA_EXCLUDEFROMCAPTURE` when `ExcludeFromCapture` is enabled, with `IsWineRuntime` fallback on Linux/Wine), and periodic `HWND_TOPMOST` reassertion. DH 5 renders natively inside DDO's DX10/11 frame via `ImGui.GetForegroundDrawList()` and Dear ImGui 1.92 dynamic font sizing.

## Hotkeys & DH 5 `/hh` chat commands

All **8** hotkeys are customizable in **Settings...** (`Hotkeys` section):

| Action | Setting | Default | Behavior |
| --- | --- | --- | --- |
| **Toggle Plugin ON/OFF** | `TogglePluginHotkey` | `Ctrl+Shift+K` | Flips master `PluginEnabled` ON/OFF from inside DDO (remains active while plugin is OFF) |
| **Toggle Center Banner** | `ToggleBannerHotkey` | `None` | Flips `HudEnabled` ON/OFF from inside DDO while keeping targeting, sounds, Status/Raid HUD, and stats active |
| **Focus Current Target** | `FocusTargetHotkey` | `Ctrl+Shift+H` | Locks `SinglePlayer` focus mode onto your currently selected DDO player character |
| **Cycle Mode** | `ToggleModeHotkey` | `Ctrl+Shift+J` | Toggles tracking mode between `Party` (party/raid + self) and `SinglePlayer` |
| **Target Critical Now** | `TargetCriticalHotkey` | `Ctrl+Shift+T` | Immediately selects the prioritized critical/low watched player (bypasses cooldown) |
| **Quick-Cast Critical (DH 5)** | `QuickCastCriticalHotkey` | `None` | DH 5 Beta: targetless `InvokeHotbar` Quick-Heal (or Quick-Raise on Soulstone) onto prioritized ally without dropping Boss target |
| **Reset People Saved & Stats** | `ResetSavedHotkey` | `None` | Zeroes `PeopleSaved`, streaks, closest call, 1% club, overkill, and reflex stats |
| **Copy DDO Chat Scorecard** | `CopyScorecardHotkey` | `None` | Copies your single-line printable ASCII healer scorecard (`<= 185` chars) to clipboard |

- **Recording & conflict protection:** Click any hotkey box in **Settings...** and press your desired chord (`Ctrl` / `Shift` / `Alt` + key). Press `Backspace` (or click **Clear**) to unbind (`None`), press `Esc` to cancel recording, or click **Reset Defaults** to restore defaults. Duplicate non-empty chords across all 8 actions are rejected before saving.
- **DH 5 Beta `/hh` Chat Commands (alias `/healershelper`):** `/hh help`, `/hh on`, `/hh off`, `/hh party`, `/hh focus`, `/hh hp <5-95>`, `/hh deathhp <value|off>`, `/hh banner` (`/hh hud`), `/hh raid`, `/hh theme <eberron|cannith|feywild>`, `/hh heal` (`/hh cast`), `/hh target`, `/hh mute <name|clear>`, `/hh unmute <name>`, `/hh score`, `/hh reset`, `/hh status`.

## Configurable settings reference

Settings persist atomically to `healers-helper.json` (`%APPDATA%\Dungeon Helper\plugins\HealersHelper\` on DH 4; `%LocalAppData%\DungeonHelperData\plugins\Healers Helper\` on DH 5 Beta):

| Group | Setting | Default | Valid range / options | Description |
| --- | --- | --- | --- | --- |
| **HP Alert & Tracking** | `PluginEnabled` | `true` | `true` / `false` | Master ON/OFF switch for alerts, sounds, auto-targeting, and clutch-save tracking (persists across restarts and offline panel toggles) |
| **HP Alert & Tracking** | `Mode` | `Party` | `Off`, `Party`, `SinglePlayer` | Watched player scope (all party/raid members + self, focused player, or silent) |
| **HP Alert & Tracking** | `IncludeSelfInPartyMode` | `true` | `true` / `false` | Include your own character (slot 0) when evaluating `Party` mode alerts |
| **HP Alert & Tracking** | `IncludeTempHpInThreshold` | `false` | `true` / `false` | Include `HitPoints_Temp` shields when computing effective HP% against `HpThresholdPercent` |
| **HP Alert & Tracking** | `FocusedPlayerId` | `0` | `ulong` (`0` = unset) | Instance ID of the focused player in `SinglePlayer` mode (set via **Focus Target** or panel selector) |
| **HP Alert & Tracking** | `FocusedPlayerName` | `""` | Trimmed string | Character name fallback for `SinglePlayer` focus when entity ID changes across instances |
| **HP Alert & Tracking** | `HpThresholdPercent` | `50` | `5`–`95` (`%`) | HP percentage at or below which a `LowHp` alert begins |
| **HP Alert & Tracking** | `RecoveryHysteresisPercent` | `3` | `0`–`20` (`%`) | Extra HP% above `HpThresholdPercent` needed to clear an active `LowHp` alert (`53%` at default) |
| **HP Alert & Tracking** | `DeadAlertSeconds` | `6` | `0`–`60` (`s`) | Duration to show a `CriticalDown` (`INCAP` / `DEAD` Soulstone) banner (`0` suppresses immediately) |
| **HP Alert & Tracking** | `FallbackDeathHp` | `-200` | `-5000`–`0` (`0` = off) | Out-of-range death HP cutoff when live `LifeState` and cached `HitPoints_IncapacitatedBonus` are unavailable (live `LifeState` always overrides in range; `0` disables HP-only death inference) |
| **HP Alert & Tracking** | `MaxAlertSeconds` | `0` | `0`–`120` (`s`, `0` = unlimited) | Optional cap per continuous `LowHp` alert on the same player (`CriticalDown` still escalates) |
| **HP Alert & Tracking** | `SuppressAlertsInTown` | `false` | `true` / `false` | Silence HP/down alerts and sounds in public zones (`InTown()`) while keeping manual notices |
| **Clutch Saves & Gamification** | `SaveWindowSeconds` | `5.0` | `1.0`–`15.0` (`s`) | Post-heal window in which surviving cumulative `damageTaken > PreHealHp` counts a clutch save |
| **Clutch Saves & Gamification** | `SelfCastWindowSeconds` | `3.5` | `1.0`–`15.0` (`s`) | Attribution window after your hotbar use, self script cast, or SP drop to credit incoming heals |
| **Clutch Saves & Gamification** | `MinHealDelta` | `5` | `1`–`200` (`HP`) | Minimum positive HP increase required to open/extend a clutch-heal window or satisfy manual top-off restore |
| **Clutch Saves & Gamification** | `ShowSavedNotice` | `true` | `true` / `false` | Show a brief `SAVED <Name>!` celebration notice when idle after a clutch save |
| **Clutch Saves & Gamification** | `ShowRankOnStatusHud` | `true` | `true` / `false` | Append your prestige rank (`SAVED N (<Rank>)`) on the compact Status HUD |
| **Center Banner, HUDs & Themes** | `Theme` | `EberronGold` | `EberronGold`, `CannithArcane`, `FeywildTwilight` | Shared DDO × Material Design 3 dark tonal UI palette across DH 4 panels/chrome and DH 5 ImGui windows/HUDs |
| **Center Banner, HUDs & Themes** | `HudEnabled` | `true` | `true` / `false` | Enable/disable the center alert banner completely (targeting, restore, chimes, Status/Raid HUD & stats stay active when `false`) |
| **Center Banner, HUDs & Themes** | `FontSizePixels` | `56` | `18`–`140` (`px`, integer) | Exact integer pixel font height of the center banner primary line (also adjustable via edge/corner drag or wheel) |
| **Center Banner, HUDs & Themes** | `BannerOpacity` | `235` | `20`–`255` (alpha) | Background alpha intensity of the center alert banner (also adjustable via `Ctrl + Mouse Wheel` in edit mode) |
| **Center Banner, HUDs & Themes** | `FlashBanner` | `true` | `true` / `false` | Alternate alert banner colors each `FlashIntervalMs` (`false` holds steady primary frame; Soulstone alerts stay calm) |
| **Center Banner, HUDs & Themes** | `ShowBannerHpBar` | `true` | `true` / `false` | Draw a spatial HP progress bar + Sovereign Gold (`#F5C542`) TempHP cap along the bottom of the center banner |
| **Center Banner, HUDs & Themes** | `FlashIntervalMs` | `250` | `100`–`1000` (`ms`) | Flash phase alternation interval for `CriticalDown` (`INCAP`) and `LowHp` banners |
| **Center Banner, HUDs & Themes** | `SoundOnAlert` | `false` | `true` / `false` | Play a system alert chime once per new living/incap `AlertGeneration` |
| **Center Banner, HUDs & Themes** | `LowHpBgHex` | `"#B84200"` | `#RRGGBB` hex | Primary background color for `LowHp` alert banners |
| **Center Banner, HUDs & Themes** | `LowHpBorderHex` | `"#FFE066"` | `#RRGGBB` hex | Primary border accent color for `LowHp` alert banners |
| **Center Banner, HUDs & Themes** | `CriticalBgHex` | `"#960A14"` | `#RRGGBB` hex | Primary background color for `CriticalDown` (`INCAP`) alert banners |
| **Center Banner, HUDs & Themes** | `CriticalBorderHex` | `"#FFD644"` | `#RRGGBB` hex | Primary border accent color for `CriticalDown` (`INCAP`) alert banners |
| **Center Banner, HUDs & Themes** | `BannerTextHex` | `"#FFFFFF"` | `#RRGGBB` hex | Primary text color for `LowHp` and `CriticalDown` alert banners |
| **Center Banner, HUDs & Themes** | `FlashAltBgHex` | `"#0A0A0E"` | `#RRGGBB` hex | Alternate dark flash-off background color when `FlashBanner` alternates |
| **Center Banner, HUDs & Themes** | `HudX` / `HudY` | `0.50` / `0.42` | `0.0`–`1.0` | Normalized monitor-relative coordinates of the center alert banner |
| **Center Banner, HUDs & Themes** | `StatusHudEnabled` | `true` | `true` / `false` | Show/hide the compact click-through Status HUD text line |
| **Center Banner, HUDs & Themes** | `StatusFontSizePixels` | `14` | `9`–`48` (`px`, integer) | Exact integer pixel font height of the compact Status HUD |
| **Center Banner, HUDs & Themes** | `StatusHudX` / `StatusHudY` | `0.50` / `0.76` | `0.0`–`1.0` | Normalized monitor-relative coordinates of the compact Status HUD |
| **Center Banner, HUDs & Themes** | `ExcludeFromCapture` | `true` | `true` / `false` | DH 4: request `SetWindowDisplayAffinity(..., 0x11)` (`WDA_EXCLUDEFROMCAPTURE`) on both HUD windows |
| **Center Banner, HUDs & Themes** | `RaidHudEnabled` | `false` | `true` / `false` | DH 5 Beta: enable the always-on triage-sorted ImGui Raid HUD (`>=28px` rows, click-to-cast) |
| **Center Banner, HUDs & Themes** | `RaidHudX` / `RaidHudY` | `0.02` / `0.25` | `0.0`–`1.0` | DH 5 Beta: normalized screen coordinates of the Triage Raid HUD |
| **Targeting & DH 5 Quick-Cast** | `AutoTargetOnAlert` | `true` | `true` / `false` | Automatically select the prioritized living/incap watched player on a new alert (releases cooldown immediately once healed) |
| **Targeting & DH 5 Quick-Cast** | `TargetingMethod` | `Auto` | `Off`, `Auto`, `SdkSelectOnly`, `PartyKeyOnly` | Selection backend (`Auto` tries `SelectObject(ulong)` first, then bound party keybind fallback) |
| **Targeting & DH 5 Quick-Cast** | `AutoTargetCooldownSeconds` | `3.0` | `0.5`–`30.0` (`s`) | Minimum delay between auto-target switches while the current ally is still unhealed |
| **Targeting & DH 5 Quick-Cast** | `RestorePreviousTargetAfterHeal` | `true` | `true` / `false` | Automatically re-select your prior target (e.g. boss/mob/tank) once all watched living allies are healthy |
| **Targeting & DH 5 Quick-Cast** | `RestoreTargetDelaySeconds` | `0.2` | `0.0`–`5.0` (`s`) | Post-heal settle delay before restoring your previous target (resets if HP dips again) |
| **Targeting & DH 5 Quick-Cast** | `QuickHealHotbar` / `QuickHealSlot` | `0` / `1` | Bar `0..20` (`0` = Off), Slot `1..10` | DH 5 Beta: 1-based hotbar & slot invoked targetlessly via `InvokeHotbar` for Quick-Heal (disabled by default until set) |
| **Targeting & DH 5 Quick-Cast** | `QuickRaiseHotbar` / `QuickRaiseSlot` | `0` / `1` | Bar `0..20` (`0` = Off), Slot `1..10` | DH 5 Beta: 1-based hotbar & slot invoked targetlessly via `InvokeHotbar` for Quick-Raise on Dead Soulstones |

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
- **Deathless Save Streak (`CurrentSaveStreak` / `BestSaveStreak`):** Increments on every clutch save and resets `CurrentSaveStreak` to `0` **only** when a watched player reaches true character death (`LifeState.Dead`) outside town (`!InTown()`). Dropping into DDO's scaling healable unconscious range (`LifeState.Incapacitated`) never resets your streak, and unrevived corpses leaving/re-entering entity range are latched so they never double-reset.
- **Closest Call & 1% Club (`OnePercentClubSaves`, `ClosestCallPlayerName`, `ClosestCallPreHealPercent`, `ClosestCallPreHealHp`):** Clutch saves landed while an ally was at **`<= 5.0%` HP** increment `OnePercentClubSaves`. `ClosestCallPlayerName`, `ClosestCallPreHealPercent`, and `ClosestCallPreHealHp` record your lowest pre-heal HP% save (breaking equal-percentage ties by lower raw `PreHealHp`).
- **Biggest Overkill Absorbed (`BiggestOverkillSaved`):** Records the largest single lethal margin (`damageTaken - PreHealHp`) your clutch heal prevented from killing an ally.
- **Healer Reflex Timer (`BestReflexMs` / `AvgReflexMs` / `ReflexSampleCount`):** Measures reaction time in **`50..10000 ms`** from a new `AlertGeneration` onset to your first self heal/cast event (`< 50 ms` pre-casts already in motion are ignored without consuming the active alert; at most one sample is recorded per alert).
- **DDO-Safe Printable ASCII Chat Scorecard (`Copy Chat Card`):**

```text
[Healer's Helper] Lifeweaver: 18 saved | Streak 6 (Best 11) | 1% Club: 3 | Closest: Tanky 2.4% (19HP) | Reflex 340ms (210ms best) | Max Save +412HP
```

## Install or update

### Dungeon Helper 4 (`4.2.x`)

1. Close DDO. In Dungeon Helper 4, choose **Settings -> Add plugin from zip file** and select `HealersHelper-X.Y.Z-dh4-plugin.zip` directly (never `-source.zip` or `-dh5-plugin.zip`).
2. If prompted because `HealersHelper` already exists, choose **Yes** to overwrite.
3. Fully exit DH 4 from its tray icon, restart it, then launch DDO.
4. Open the **DDO Healer's Helper** panel in DH 4. (Manual fallback: extract `plugins/HealersHelper/*` into `%APPDATA%\Dungeon Helper\plugins\HealersHelper\` while DDO and DH 4 are closed.)

### Dungeon Helper 5 Beta (`5.0.x` / `VoK.Sdk 5.3.0`)

1. Close DDO (DX10 or DX11 required; DH 4 and DH 5 cannot run simultaneously on the same DDO instance).
2. In Dungeon Helper 5 Beta, open **Plugins -> Install from zip** and select `HealersHelper-X.Y.Z-dh5-plugin.zip`.
3. Enable **Healers Helper** (`pluginId: 54b48548-05ff-48cf-bce1-c6270e3d4a3f`, signed with `community` entitlement + author Authenticode + `.manifest.sig`), then launch DDO.
4. Open the **Healers Helper** window from the DH 5 in-game toolbar icon or configure directly in chat using `/hh` commands. Settings persist to `%LocalAppData%\DungeonHelperData\plugins\Healers Helper\healers-helper.json`.

## Design & HCI rationale

Healer's Helper's alert hierarchy and three dark themes are grounded in empirical Human-Computer Interaction and game UI research (see [References](docs/human/REFERENCES.md) for full DOIs):
- **Dedicated peripheral HUDs over diegetic bars:** Players monitor health fastest on compact non-diegetic HUDs anchored near the focal viewport (*Fagerholt & Lorentzon 2009*; *Peacocke et al. 2018*).
- **Proximity Compatibility & Preattentive Pop-Out:** Co-locating name, HP bar, Sovereign Gold TempHP shield cap, and life-state badge in one object avoids split-attention saccades (*Treisman & Gelade 1980*; *Wickens & Carswell 1995*).
- **Triage sorting & Fitts's Law targets:** Priority-sorting raid rows collapses Hick-Hyman choice latency (`log2(n+1)`), while `>= 28 px` interactive raid rows satisfy *WCAG 2.5.8* and *MacKenzie 1992*.
- **Calm-vs-urgent alarm escalation & CVD safety:** Hysteresis, `<= 2 Hz` flash cadence (*WCAG 2.3.1*), calm non-flashing Soulstone banners (*Edworthy & Hellier 2006*), and redundant glyphs + luminance (*Okabe & Ito 2008*; *WCAG 1.4.1 / 1.4.11*) prevent false alarms and color-only confusion.
- **Material Design 3 dark tonal hierarchy:** 4 tonal surface layers without crimson UI chrome keep Critical/Incap alerts unmistakable across all three themes.

## Verification status and limits

- **Windows 11 live gameplay & Lossless Scaling (DH 4):** Tested and confirmed working on live **DDO + Dungeon Helper 4 + Lossless Scaling on Windows 11**. Because hardware, Windows display/capture stacks (WGC vs. DXGI), GPU drivers, and overlay hooks vary, compatibility is **not guaranteed on any or all configurations**.
- **Linux / Wine / Proton:** Cross-compiles and passes portable C#, DH 5 `Dh5Smoke`, and Python CI suites natively on Ubuntu Linux (`EnableWindowsTargeting=true` + `msitools` + `osslsigncode`), and DH 4 includes `IsWineRuntime` User32/GDI32 guards (`wine_get_version` detection). It **should also work on Linux under Wine/Proton**, though **live Linux gameplay has not yet been tested**.
- **DH 5 Beta trust gate & local verification:** Verified locally against DH `5.0.858` (`VoK.Sdk 5.3.0.0`) via `PluginTrustGate.Evaluate`, `AuthenticodeVerifier.IsSignatureValid`, `DungeonHelper.App` ZIP installation (`TrustProbe`), and cross-platform `Dh5Smoke --require-signed`; live in-game DX10/11 DH 5 Beta rendering has not yet been run inside DDO here.
- **Automated binary, unit, smoke & CI verification:** Pinned `VoK.Sdk 4.2.1.0` + `VoK.Sdk 5.3.0.0` verification, **288** portable C# assertions (`HealersHelper.Tests`), **29** Python CI/packaging/signing/semver/README-sync checks (`ci/test_*.py`), **4** `WindowsSmoke` WinForms/layered-HUD/interactive-resize/offline-persistence/3-theme/ZIP suites, **10** `Dh5Smoke --require-signed` checks (cryptographic trust gate, PE token resolution, async `0x10000E0C`/`0x10001868`/`0x10008250` hydration, `-320 HP` live `LifeState.Incapacitated`, `Party` events, targetless `InvokeHotbar`, `/hh` commands incl. `/hh deathhp`, & 3 themes), and Ubuntu/Windows GitHub-hosted Actions (`linux` + `build` + `release`).

## Release history

- **`v1.5.0`:** Fixed DDO scaling unconscious-range triage and added configurable out-of-range death fallback: shared deterministic `LifeStatusEvaluator` keeps live `LifeState.Incapacitated` allies healable at any negative HP (never overridden by HP cutoffs), hydrates `Death_LifeState` (`0x10000E0C`), `Death_SoulStoneID` (`0x10001868`), and `HitPoints_IncapacitatedBonus` (`0x10008250`) together in DH 5 `UpdateProperties`, maintains session-scoped sticky confirmed-dead latching (`_confirmedDeadIds`) and personal unconscious floor caching (`_cachedIncapBonus`, `-10 - bonus`), and adds `Settings.FallbackDeathHp` (default `-200`, clamped `-5000..0`, `0` = disabled) across DH 4 Settings (`FallbackDeathHpNumeric`), DH 5 ImGui Triage tab, and `/hh deathhp <value|off>`.

- **`v1.4.0`:** Added full **Dungeon Helper 5 Beta (`net10.0` `x64`, `VoK.Sdk 5.3.0.0`, `ImGuiNET`)** dual-target support (`src-dh5/`, signed `*-dh5-plugin.zip` via ephemeral `ci/sign_dh5.py` + `community` entitlement + `PluginTrustGate` verification, cross-platform `Dh5Smoke`, dual `ci/sdk-lock.json`), **3 shared DDO × Material Design 3 dark themes (`UiTheme`: `EberronGold`, `CannithArcane`, `FeywildTwilight`)** across DH 4 and DH 5, **3-state DDO life/death triage** (Incapacitated `0..-9 HP` healable > Living Low HP > calm Spectral Violet Dead Soulstone), **Temporary HP shield (`HitPoints_Temp`)** Sovereign Gold badge/bar (`IncludeTempHpInThreshold`, `ShowBannerHpBar`), **per-session player Mute/Ignore**, **Raid Keymap Readiness (`7–12`)**, DH 5 **targetless `InvokeHotbar(bar, slot, targetId)`** Quick-Heal/Quick-Raise (`QuickHealHotbar`/`QuickRaiseHotbar` safely `0`/disabled by default, `QuickCastCriticalHotkey`), optional **always-on Triage Raid HUD (`RaidHudEnabled`)** with Astral Blue SP slivers, `/hh` chat commands, `UpdateProperties([0x10000E0Cu])` live `LifeState` hydration, and `eventProvider.Party` connection tracking.
- **`v1.3.0`:** Added 5-rule strict healing-target priority (expected-target live verification, tight `0.8 s` MMF settle guard, universal zero-alert/zero-unhealed-ally hold, instant post-heal cooldown release, and mutually exclusive alert/restore tick branches), guaranteed offline/restart active & banner state persistence with dirty-tracked settings saves, 1-click/hotkey/settings full center-banner disable (`ToggleBannerButton` / `ToggleBannerHotkey` / `HudEnabled`), interactive banner edge/corner drag resizing + mouse-wheel font sizing + `Ctrl+wheel` opacity (`20..255`) + 6 custom hex colors, Wine/Proton runtime guards (`IsWineRuntime`), expanded gamification options documentation, and automated CI `README.md` sync checks.
- **`v1.2.0`:** Added automatic post-heal previous-target restoration (`TargetRestoreTracker`, `RestorePreviousTargetAfterHeal`, `RestoreTargetDelaySeconds`), multi-injury chaining (`Boss -> Alice -> Bob -> Boss`), manual-override cancellation, and dead non-player corpse rejection.
- **`v1.1.0`:** Added the healer gamification suite (8 prestige ranks, deathless save streak, Closest Call, 1% Club, `[50..10000 ms]` reflex timer, pure ASCII `Copy Chat Card` scorecard) and extended configurability (`IncludeSelfInPartyMode`, `SuppressAlertsInTown`, `MaxAlertSeconds`, `BannerOpacity`, `FlashBanner`, `SaveWindowSeconds`, `SelfCastWindowSeconds`, `MinHealDelta`, `ResetSavedHotkey`, `CopyScorecardHotkey`).
- **`v1.0.1`:** Carbon High audit hardening (cumulative multi-hit clutch-save accounting, MaxHP gear/Rage guards, negative-coordinate secondary-monitor layered HUD positioning, hotkey recorder `Esc`/`Backspace`/duplicate validation, fast-dispose safety).
- **`v1.0.0`:** Initial stable release with party/raid/solo/single-player HP monitoring, click-through 32bpp `UpdateLayeredWindow` flashing center banner + compact Status HUD, `WDA_EXCLUDEFROMCAPTURE` Lossless Scaling support, `SelectObject(ulong)` targeting, hybrid `KeyDown` + `GetAsyncKeyState` hotkeys, and 3-entry runtime ZIP packaging.

## Building from source

On Windows with the .NET 10 SDK, Python 3, OpenSSL, Windows SDK `signtool.exe`, and Dungeon Helper 4 installed:

```powershell
.\ci\Get-Sdk.ps1
.\build.ps1 -Version 1.5.0 -Dh5KeyFile C:\path\to\author.key
# Or unsigned DH 5 local dev build:
.\build.ps1 -Version 1.5.0 -AllowUnsignedDh5
```

See [docs/human/DEVELOPMENT.md](docs/human/DEVELOPMENT.md#linux-cross-compilation) for Linux cross-compilation instructions.

References: [SetWindowDisplayAffinity](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity), [UpdateLayeredWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow), [Extended Window Styles](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles), [Dungeon Helper](https://dungeonhelper.com/), [DH 5 SDK 5.3.0](http://sdk.dungeonhelper.com/5.3.0/).

Unofficial community plugin; not an official Dungeon Helper or Standing Stone Games release.
