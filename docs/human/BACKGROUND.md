# Background and requirements

DDO Healer's Helper monitors Dungeons & Dragons Online (DDO) party, raid, solo, and focused single-player hit points inside Dungeon Helper (DH). When a watched player drops below a configured HP percentage or goes incapacitated/dead, it flashes a large, high-contrast, click-through banner near the center of the screen and can optionally target that player so the healer can heal immediately.

The plugin uses DH's local game-data provider and keymap/selection APIs. It does not automate spell casting, install external overlay services like RTSS, or make network calls.

## Why this architecture exists

| Verified constraint / requirement | Resulting design decision |
| --- | --- |
| Installed DH supplies `VoK.Sdk.dll` `4.2.1.0` (`DungeonHelper.dll` `4.2.0.146`) | Pin and hash-verify `VoK.Sdk 4.2.1.0` in `ci/sdk-lock.json` rather than upgrading to newer NuGet packages |
| `GetPartyMembers()` excludes the local player and caches `IPartyMember` getters | Re-poll `GetPartyMembers()` fresh each scan, merge `GetCurrentCharacterId()` / `GetCurrentCharacter()`, and enrich with `GetEntity(id)` |
| `IPartyMember` omits `LifeState` and temp HP, whereas `IEntity` exposes `LifeState` and signed `Int32` negative HP | Enrich party members via `GetEntity(id)` when in range (`!IsDeleted`), falling back to `IPartyMember` HP when out of range |
| `SelectObject(ulong)` writes directly to `dndpl.dll` MMF with zero subscription checks, while `ddo.keymap` leaves self and raid slots 7–12 unbound | Prefer `SelectObject(ulong)` (`Auto` / `SdkSelectOnly`); guard `GetSelectedObjectId().GetValueOrDefault() != id` because DH returns `(ulong?)0` when unselected |
| `EventProvider.KeyDown` stops firing when DH `HotKeysEnabled` is false | Combine `EventProvider.KeyDown` with foreground-gated Win32 `GetAsyncKeyState` edge polling and deduplicate in `HotkeyMatcher` |
| Hosted DH panels vanish under WGC and duplicate under DXGI in Lossless Scaling | Render in an independent top-level STA window (`WS_EX_LAYERED \| WS_EX_TRANSPARENT \| WS_EX_NOACTIVATE \| WS_EX_TOOLWINDOW`), request `WDA_EXCLUDEFROMCAPTURE` (`0x11`), and reassert `HWND_TOPMOST` |
| Hosted WinForms panels can clip toolbar icons and buttons at narrow DH widths | Use `FitToolbarWidth()`, `AutoSize` `TableLayoutPanel` rows, and `FlowLayoutPanel` button rows |
| DH plugin installer requires runtime-only archives | Enforce a 3-entry ZIP (`plugins/HealersHelper/VoK.HealersHelper.{dll,deps.json,metadata}`), DLL first; ship source separately |

## Evidence and remaining verification

- **Verified:** Stage 1 IL/metadata/SHA-256 verification of `VoK.Sdk.dll` (`4.2.1.0`), `DungeonHelper.dll` (`4.2.0.146`), and local `ddo.keymap`, `AlchPlugin` Win32/CI reference patterns, local Windows & WSL builds/tests, and live GitHub-hosted Actions (`linux` + `build` + `release` `v1.0.0`).
- **Unverified at authoring time:** Live DDO gameplay behavior and live Lossless Scaling capture modes (WGC and DXGI).

See [Operations](OPERATIONS.md) for the live verification checklist and [References](REFERENCES.md) for external documentation. Agent counterpart: [Context](../agents/CONTEXT.md).
