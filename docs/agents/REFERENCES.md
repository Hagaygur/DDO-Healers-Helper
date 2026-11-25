# Agent research guidance

Human counterpart and annotated source table: [References](../human/REFERENCES.md).

## Primary entry points

- Dungeon Helper: https://dungeonhelper.com/ and https://dungeonhelper.com/frequently-asked-questions/
- SDK documentation: https://sdk.dungeonhelper.com/
- SDK package metadata: https://www.nuget.org/packages/VoK.Sdk/
- Windows capture affinity: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity
- Layered drawing: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow
- Window flags: https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles
- Agent Markdown convention: https://agents.md/

## Evidence rules

- Always inspect the pinned local assembly (`VoK.Sdk.dll` `4.2.1.0`, `DungeonHelper.dll` `4.2.0.146`) rather than inferring API semantics from newer NuGet versions (`4.3.x+`).
- Key verified local semantics in `DungeonHelper.dll` `4.2.0.146`:
  - `GetPartyMembers()` excludes the local character and caches `IPartyMember` getters until `Refresh()`.
  - `IEntity` exposes signed `Int32` `HitPoints_Current` (negative when incapacitated), `HitPoints_Max`, `HitPoints_Temp`, and `LifeState` (`Undef=0`, `Dead=2`, `Incapacitated=3`, `Alive=4`).
  - `SelectObject(ulong)` sends an MMF command to `dndpl.dll` with zero subscription checks; `GetSelectedObjectId()` returns `(ulong?)0` when unselected.
  - `EventProvider.KeyDown` requires `HotKeysEnabled`; foreground-gated `GetAsyncKeyState` polling covers the disabled-hotkey case.
  - Local `ddo.keymap` maps `TARGET_FELLOW_1_KEY`..`6_KEY` (`DIK_F1`..`DIK_F6`) and leaves `TARGET_SELF_KEY` and slots 7–12 unbound.
- Keep user logs, character names from live captures, and downloaded SDK binaries out of source control and release archives.
