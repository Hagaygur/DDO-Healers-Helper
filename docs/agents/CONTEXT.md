# Agent task context

Human counterpart: [Background](../human/BACKGROUND.md). Repository instructions: [AGENTS.md](../../AGENTS.md).

## Required outcome

Maintain a DH-only, transparent, click-through DDO Healer's Helper HUD and targeting helper that coexists with Lossless Scaling. Preserve exact pixel font settings, non-allocating layered surface rendering, reliable self+party+single-player HP collection, safe `SelectObject(ulong)` targeting, deduplicated hotkey handling, and runtime-only 3-entry ZIP packaging.

## Verified Dungeon Helper 4.2.1.0 SDK findings

Stage 1 IL/metadata inspection against `C:\Users\hagay\AppData\Roaming\Dungeon Helper\VoK.Sdk.dll` (`4.2.1.0`, SHA256 `de78e565b2f5bb5426f6b7fb81fdb1a84a8a684d77e2d148dcd5fe2867ef5484`) and `DungeonHelper.dll` (`4.2.0.146`) established:

- **`GetPartyMembers()` excludes self & caches getters:** `IDdoGameDataProvider.GetPartyMembers()` calls `GetCurrentCharacterId()` and skips the matching ID, has no 5/6/12 party cap (supports 12-person raids), and returns `IPartyMember` snapshots whose getters read cached fields populated by `.ctor`/`Refresh()`, while only `IsValid()` reads memory live. Plugins must re-call `GetPartyMembers()` each tick and merge self via `GetCurrentCharacterId()` / `GetCurrentCharacter()`.
- **`IEntity` provides live HP & `LifeState`:** `HitPoints_Current`, `HitPoints_Max`, and `HitPoints_Temp` are signed `Int32` values (negative when incapacitated), paired with `LifeState` (`Undef=0`, `Dead=2`, `Incapacitated=3`, `Alive=4`). `IPartyMember` omits `LifeState` and `HitPoints_Temp`, so enriching party members with `GetEntity(id)` when `!IsDeleted` gives accurate down/dead detection, falling back to `IPartyMember` when out of entity range.
- **Targeting (`SelectObject(ulong)`):** `SelectObject(ulong)` checks `0 < id && id < ulong.MaxValue` and writes an MMF command (`type = 1, objectId`) to `dndpl.dll` with zero subscription/license checks. `GetSelectedObjectId()` wraps a `ulong` field directly in `new Nullable<ulong>(field)`, returning `(ulong?)0` when unselected rather than `null`. Local `ddo.keymap` binds `TARGET_FELLOW_1_KEY`..`6_KEY` (`DIK_F1`..`DIK_F6`), leaving `TARGET_SELF_KEY` and slots 7–12 unbound (`GetHotkey` returns `null`). Therefore `SelectObject(ulong)` is the primary targeting path.
- **Hotkeys (`KeyDown` + `GetAsyncKeyState`):** `EventProvider.KeyDown` only fires when DH `HotKeysEnabled` (`EnableHotkeys`) is `true`. Combining `EventProvider.KeyDown` with foreground-gated Win32 `GetAsyncKeyState` polling and deduplicating within ~250 ms ensures hotkeys work even when DH hotkeys are disabled, with zero subscription checks.
- **Lossless Scaling & HUD pattern:** Inherited from `AlchPlugin`: dedicated STA thread (`BannerHudController`), reusable 32bpp `Format32bppPArgb` `CreateDIBSection` + `UpdateLayeredWindow`, `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`, `SetWindowDisplayAffinity(Handle, 0x11)` (`WDA_EXCLUDEFROMCAPTURE`), `HWND_TOPMOST` reassertion, and autosized WinForms UI (`FitToolbarWidth`).

## Evidence ledger

- **Verified:** Stage 1 IL/metadata/keymap/SDK SHA-256 verification against DH `4.2.0.146` / `VoK.Sdk 4.2.1.0`, `AlchPlugin` reference patterns, local Windows/WSL Ubuntu 22.04 builds & tests, and live GitHub-hosted Actions (`linux` + `build` + `release` `v1.0.0`).
- **Unverified at authoring time:** Live DDO gameplay integration and live Lossless Scaling capture backends (WGC and DXGI). Do not report live runtime gameplay or LS capture as verified until actually executed and observed.
