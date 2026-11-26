# Build, release and troubleshooting

## Install and use

Choose the release ZIP matching your Dungeon Helper version (never import `-source.zip` into DH):

- **Dungeon Helper 4 (`4.2.x`):** **Settings -> Add plugin from zip file** -> select `HealersHelper-X.Y.Z-dh4-plugin.zip`. Exit DH 4 from tray and restart before launching DDO. Manual fallback: copy `plugins/HealersHelper/*` into `%APPDATA%\Dungeon Helper\plugins\HealersHelper\`.
- **Dungeon Helper 5 Beta (`5.0.x`, DX10/11):** **Plugins -> Install from zip** -> select `HealersHelper-X.Y.Z-dh5-plugin.zip` (Authenticode + community entitlement signed). Enable **Healers Helper** and launch DDO. Manual fallback: `%LocalAppData%\DungeonHelperData\plugins\Healers Helper\`.

Key controls:
- **Toggle ON/OFF (`Ctrl+Shift+K`):** Master enable/disable switch persisting across restarts and offline panel toggles.
- **Banner: ON / OFF (`ToggleBannerHotkey`):** Disables the center alert banner while keeping targeting, chimes, Status HUD, DH 5 Raid HUD, and clutch-save stats active.
- **Theme (`Settings -> Theme` or `/hh theme <eberron|cannith|feywild>`):** Switches between `EberronGold`, `CannithArcane`, and `FeywildTwilight`.
- **Mute Player / `/hh mute <name>`:** Silences self-sustaining Warforged or Pale Master/Dark Apostate shrouds for the session.
- **Targetless Quick-Cast (DH 5 Beta):** Set `QuickHealHotbar`/`QuickHealSlot` and `QuickRaiseHotbar`/`QuickRaiseSlot` (disabled at `0` by default) to cast via `QuickCastCriticalHotkey`, `/hh heal`, or left-clicking a Raid HUD row without dropping your Boss/Tank target.

## Development and release

See [GITHUB.md](../../GITHUB.md) for the CI workflow. Local Windows commands:

```powershell
py -3 -m unittest discover -s ci -p "test_*.py" -v
dotnet run --project tests/HealersHelper.Tests.csproj -c Release -- --contracts dist/contracts.json
.\ci\Get-Sdk.ps1
.\build.ps1 -Version 1.5.0 -Dh5KeyFile C:\path\to\author.key
dotnet run --project tests/WindowsSmoke/WindowsSmoke.csproj -c Release -- dist/HealersHelper-1.5.0-dh4-plugin.zip "$env:DH_SDK_DIR"
dotnet run --project tests/Dh5Smoke/Dh5Smoke.csproj -c Release -p:Platform=x64 -- dist/HealersHelper-1.5.0-dh5-plugin.zip "$env:DH5_SDK_DIR" --require-signed
```

## Diagnose by symptom

| Symptom | First checks |
| --- | --- |
| DH 5 shows `[NOT AUTHORIZED]` or refuses ZIP | Ensure you installed `*-dh5-plugin.zip` (not `*-dh4-plugin.zip` or `-source.zip`) and all 8 root files incl. `.entitlement(.sig)` + `.manifest(.sig)` + `.pem` are intact |
| DH 4 banner missing or duplicated under Lossless Scaling | Check `Capture exclusion: ON`; test `Preview Banner` with LS off, then WGC and DXGI independently with the DH 4 panel closed |
| Self or raid member missing | Verify `Mode` is `Party` (not `SinglePlayer` / `Off`) and member is not session-muted |
| Dead Soulstone not auto-targeted (or out-of-range ally marked Dead) | By design: Incapacitated allies across DDO's scaling unconscious range and Living Low HP allies always outrank Dead Soulstones. If an ally out of entity range is marked Dead before their actual floor, adjust `FallbackDeathHp` (`-5000..0`, default `-200`, `0` = off) or `/hh deathhp <value\|off>` |
| Auto-target not switching | Check `TargetingStatus`; `Auto`/`SdkSelectOnly` uses `SelectObject(ulong)` when in range; `PartyKeyOnly` requires keybinds (`7–12` are unbound by default in `ddo.keymap`) |
| DH 5 Quick-Heal does nothing | Verify `QuickHealHotbar` / `QuickRaiseHotbar` is set `1..20` (`0` = safely disabled by default) |

## Compatibility & regression checklist

DH 4 is tested and confirmed working on live **DDO + Dungeon Helper 4 + Lossless Scaling on Windows 11** (not guaranteed on all configurations), and cross-compiled on Linux with `IsWineRuntime` guards (live Linux gameplay untested). DH 5 Beta trust gate and ALC execution are verified locally via `TrustProbe` and `Dh5Smoke --require-signed` against `VoK.Sdk 5.3.0.0` (live DX10/11 DH 5 gameplay untested here).

Agent counterpart: [Workflow](../agents/WORKFLOW.md).
