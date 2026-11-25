# Build, release and troubleshooting

## Install and use

Install the release asset named `HealersHelper-X.Y.Z-plugin.zip` directly through Dungeon Helper (**Settings -> Add plugin from zip file**). It contains strictly `VoK.HealersHelper.dll`, `VoK.HealersHelper.deps.json`, and `VoK.HealersHelper.metadata` under `plugins/HealersHelper/`, DLL first. Never import `HealersHelper-X.Y.Z-source.zip` into DH.

Close DDO before updating and restart DH completely from the system tray afterward. If files are locked, close both DDO and DH and copy the three runtime files manually into `%APPDATA%\Dungeon Helper\plugins\HealersHelper\`, preserving your settings file (`healers-helper.json`).

In the **DDO Healer's Helper** control panel:
- **Toggle ON/OFF (`Ctrl+Shift+K`):** Master enable/disable switch. Persists immediately across restarts (even when toggled offline before DDO connects). When OFF, all alerts, auto-targeting, sounds, and save windows pause, while `Ctrl+Shift+K` remains active so you can re-enable from inside DDO.
- **Banner: ON / OFF (`ToggleBannerHotkey`):** Disables or re-enables the center flashing alert banner (`HudEnabled`) while keeping auto-targeting, post-heal target restoration, alert chimes, the compact Status HUD, and clutch-save gamification active.
- **Mode (`Party`, `SinglePlayer`, `Off`):** `Party` watches all party/raid members plus yourself (`GetPartyMembers()` + self); `SinglePlayer` watches one focused player; `Off` silences alerts.
- **Focus Target (`Ctrl+Shift+H`):** Locks `SinglePlayer` mode onto your currently selected DDO target (`GetSelectedObjectId()`), flashing a brief `WATCHING: <Name>` notice.
- **Cycle Mode (`Ctrl+Shift+J`):** Toggles between `Party` and `SinglePlayer` when enabled.
- **Target Critical (`Ctrl+Shift+T`):** Immediately selects the lowest/down watched player via `SelectObject(ulong)` (or `PartyKeyOnly` keybind if configured), and automatically restores your previous target once all watched living allies are healthy when `RestorePreviousTargetAfterHeal` is enabled.
- **Preview Banner / Move / Resize Banner / Move Status (20s):** Previews the flashing center banner and lets you interactively move (`HudX`, `HudY`), edge/corner-drag or mouse-wheel resize (`FontSizePixels`), and `Ctrl+wheel` adjust opacity (`BannerOpacity` `20..255`) on the center banner, or drag the compact status HUD (`StatusHudX`, `StatusHudY`). Close the DH panel during play; both HUD windows remain active.
- **Copy Chat Card:** Copies a single-line pure ASCII (`<= 185` chars) healer scorecard (rank, saves, streak, 1% club, closest call, reflex) to the clipboard for pasting into `/p` or `/raid`.
- **Reset Saved:** Resets `People Saved` and all persisted gamification stats (streaks, closest call, 1% club, overkill, reflex) back to `0`.

## Development and release

See [GITHUB.md](../../GITHUB.md) for the CI workflow. Local Windows commands:

```powershell
py -3 -m unittest discover -s ci -p "test_*.py" -v
dotnet run --project tests/HealersHelper.Tests.csproj -c Release -- --contracts dist/contracts.json
.\build.ps1 -DungeonHelperDir "$env:APPDATA\Dungeon Helper" -Version 1.0.0
```

First stable release is `1.0.0`. Publication triggers only when tracked C# plugin source (`src/**/*.cs`) changes relative to the latest reachable stable tag, evaluated across the full unreleased range. Docs/tests/CI/metadata changes alone build artifacts with `publish: false`. Any failing test blocks release.

## Diagnose by symptom

| Symptom | First checks |
| --- | --- |
| Installed but no plugin icon | Check DH logs, verify `VoK.Sdk.dll` is `4.2.1.0`, remove duplicate DLL copies, confirm 3-entry runtime ZIP |
| Toolbar or settings buttons clipped | Record Windows display scaling and version; run `WindowsSmoke` to verify `FitToolbarWidth` and autosized rows |
| Banner missing or duplicated under Lossless Scaling | Check `Capture exclusion: ON` in status; test `Preview Banner` with LS off, then WGC and DXGI independently with DH panel closed |
| Self or raid member missing | Confirm DH `GetCurrentCharacterId()` / `GetPartyMembers()` status in panel; verify `Mode` is `Party` (not `SinglePlayer` / `Off`) |
| Auto-target not switching | Check `TargetingStatus` in panel; `Auto` / `SdkSelectOnly` uses `SelectObject(ulong)` when target is in entity range (`!IsDeleted`); `PartyKeyOnly` requires keybinds in `ddo.keymap` (slots 7–12 and self are unbound by default) |
| Hotkeys not firing when DH hotkeys are off | Ensure DDO, DH, or Lossless Scaling is the foreground window so the `GetAsyncKeyState` poll is active |
| CI fails fetching SDK | Verify upstream `DungeonHelper.msi` and `ci/sdk-lock.json` hashes deliberately; never bypass hash checks |

## Compatibility & regression checklist

Tested and confirmed working on live **DDO + Dungeon Helper + Lossless Scaling on Windows 11** (not guaranteed to work on any or all OS, GPU, display, or capture configurations). Cross-compiled and CI-tested natively on **Linux** with `IsWineRuntime` User32/GDI32 fallbacks so it **should also work on Linux via Wine/Proton** (live Linux gameplay untested). When validating a new display, OS, or Wine/Proton setup:
1. With LS off, verify `Preview Banner`, `Move / Resize Banner` (drag center, drag corners/edges, wheel resize, `Ctrl+wheel` opacity), `Move Status (20s)`, `Banner: ON / OFF`, and exact integer `FontSizePixels` / `StatusFontSizePixels` save/reopen.
2. Test solo HP drop, party/raid HP drop below threshold, recovery above `Threshold + Hysteresis`, `CriticalDown` (incapacitated negative HP / dead) timeout and recovery, and clutch `People Saved` detection + `Reset Saved`.
3. Test `Toggle ON/OFF` (`Ctrl+Shift+K`, including re-enabling while OFF), `Focus Target` (`Ctrl+Shift+H`, confirming monsters/NPCs are rejected), `Cycle Mode` (`Ctrl+Shift+J`), and `Target Critical` (`Ctrl+Shift+T`) with DH `HotKeysEnabled` both on and off.
4. Close the DH panel and test both `BannerHudWindow` (`Preview Banner`) and `StatusHudWindow` with Lossless Scaling on under both **WGC** and **DXGI** capture modes; confirm unscaled crisp overlays above LS and click-through input to DDO.

Agent counterpart: [Workflow](../agents/WORKFLOW.md).
