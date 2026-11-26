# Background and requirements

DDO Healer's Helper monitors Dungeons & Dragons Online (DDO) party, 12-player raid, solo, and focused single-player hit points across **Dungeon Helper 4 (`4.2.x`)** and **Dungeon Helper 5 Beta (`5.0.x`)**. It distinguishes healable **Incapacitated** allies across DDO's scaling unconscious range from **Dead Soulstones** (using live `LifeState` + `Death_SoulStoneID` in range, and sticky confirmed-dead latching -> cached `HitPoints_IncapacitatedBonus` -> configurable `FallbackDeathHp` out of range), tracks **Temporary HP shields (`HitPoints_Temp`)** and healer clutch saves/prestige ranks, and lets healers recover injured allies without losing awareness or boss targets.

The plugin uses DH's local game-data provider and selection/hotbar APIs. It never requires an external overlay executable like RTSS and never makes network calls during gameplay.

## Why this architecture exists

| Verified constraint / requirement | Resulting design decision |
| --- | --- |
| DH 4 (`net8.0-windows`, `4.2.1.0`, `plugins/HealersHelper/`) and DH 5 Beta (`net10.0` `x64`, `5.3.0.0`, root ZIP + `PluginTrustGate`) are binary- and layout-incompatible | Maintain one shared portable core (`src/*.cs`) linked into `src/` (DH 4) and `src-dh5/` (DH 5 Beta), shipping separate `*-dh4-plugin.zip` and signed `*-dh5-plugin.zip` artifacts |
| DDO unconscious range scales past `-10 HP` via Diehard/feats/enhancements/gear (`LifeState.Incapacitated` vs `LifeState.Dead` Soulstones), while THP shields (`HitPoints_Temp`) alter real danger | Use live `LifeState` + `Death_SoulStoneID` in range and sticky confirmed-dead -> cached `HitPoints_IncapacitatedBonus` -> `FallbackDeathHp` (`-200`, `0`=off) out of range; prioritize Incapacitated > Living Low HP > calm Spectral Violet Soulstones, surface `+N THP` Sovereign Gold shields, and support per-session Mute/Ignore |
| DH 4 `GetPartyMembers()` excludes self and caches getters; DH 5 `LifeState` (`0x10000E0C`) only hydrates once unless refreshed | Poll fresh each tick, merge self + `GetEntity` (DH 4) or `UpdateProperties([0x10000E0Cu, 0x10001868u, 0x10008250u])` + `eventProvider.Party` (DH 5) asynchronously |
| DH 4 hosted WinForms panels vanish/duplicate under Lossless Scaling; DH 5 injects directly into DX10/11 | DH 4 renders on independent STA layered windows (`WDA_EXCLUDEFROMCAPTURE` + `IsWineRuntime`); DH 5 draws natively inside DDO's frame via `ImGuiNET` |
| Raid healers hold Boss/Tank hard targets in mouselook | DH 4 uses `SelectObject(ulong)` + 5-rule `TargetRestoreTracker`; DH 5 adds targetless `InvokeHotbar(bar, slot, targetId)` Quick-Heal/Raise (safely disabled by default) |
| DH 5 `PluginTrustGate` rejects unsigned or non-entitled bundles | Commit only public `ci/Healers Helper.entitlement(.sig)`, sign `VoK.HealersHelper.dll` + `.manifest` ephemerally in `ci/sign_dh5.py`, and keep `*.key`/`*.pfx` strictly out of git |

## Evidence and platform status

- **Windows 11 live verification (DH 4):** Tested and confirmed working on live DDO + Dungeon Helper 4 + Lossless Scaling on Windows 11 (not guaranteed on any or all OS, GPU, display, or capture configurations).
- **DH 5 Beta verification:** Verified locally against DH `5.0.858` / `VoK.Sdk 5.3.0.0` via `PluginTrustGate.Evaluate`, `AuthenticodeVerifier.IsSignatureValid`, `DungeonHelper.App` ZIP installation (`TrustProbe`), and `Dh5Smoke --require-signed`; live DX10/11 gameplay inside DH 5 Beta has not yet been tested here.
- **Linux / Wine / Proton:** Cross-compiles and passes portable C#, DH 5 `Dh5Smoke`, and Python CI suites natively on Ubuntu in GitHub Actions CI (`EnableWindowsTargeting=true` + `msitools` + `osslsigncode`), with DH 4 `IsWineRuntime` guards (`wine_get_version`) so DH 4 should also work via Wine/Proton (live Linux gameplay untested).

See [Operations](OPERATIONS.md) for verification checklists and [References](REFERENCES.md) for peer-reviewed HCI citations and SDK docs. Agent counterpart: [Context](../agents/CONTEXT.md).
