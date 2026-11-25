# External references and SDK research

External pages can change; the pinned binary (`VoK.Sdk.dll` `4.2.1.0`, `DungeonHelper.dll` `4.2.0.146`) and current code determine actual compatibility.

| Resource | Where to read | How to use it |
| --- | --- | --- |
| DH installation and requirements | [Dungeon Helper home](https://dungeonhelper.com/) | Official installation and troubleshooting guidance |
| DH plugin framework | [FAQ](https://dungeonhelper.com/frequently-asked-questions/) | Host requirements and developer starting points |
| SDK documentation | [SDK root](https://sdk.dungeonhelper.com/) | Official SDK documentation portal |
| SDK package metadata | [VoK.Sdk on NuGet](https://www.nuget.org/packages/VoK.Sdk/) | Release metadata (newer NuGet versions are not automatically compatible with DH 4.2.1.0) |
| Capture exclusion | [SetWindowDisplayAffinity](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity) | Top-level window requirements and `WDA_EXCLUDEFROMCAPTURE` (`0x11`) |
| Alpha HUD drawing | [UpdateLayeredWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow) | Native premultiplied layered-window rendering |
| Window flags | [Extended window styles](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles) | `WS_EX_LAYERED`, `WS_EX_TRANSPARENT`, `WS_EX_NOACTIVATE`, `WS_EX_TOOLWINDOW` |
| Agent documentation | [AGENTS.md format](https://agents.md/) | Open Markdown entry point for coding-agent instructions |

## Local SDK & keymap facts verified against installed binaries

- `VoK.Sdk.dll`: `AssemblyVersion = 4.2.1.0`, `FileVersion = 4.2.1.0`, `SHA256 = de78e565b2f5bb5426f6b7fb81fdb1a84a8a684d77e2d148dcd5fe2867ef5484`.
- `IDdoGameDataProvider` in `DungeonHelper.dll` (`4.2.0.146`):
  - `GetPartyMembers()` excludes `GetCurrentCharacterId()`, supports 12-person raids, and caches `IPartyMember` property values until `Refresh()`.
  - `IEntity` provides `HitPoints_Current`, `HitPoints_Max`, `HitPoints_Temp` (`Int32`, negative when incapacitated), and `LifeState` (`Undef=0`, `Dead=2`, `Incapacitated=3`, `Alive=4`).
  - `SelectObject(ulong)` sends an MMF command to `dndpl.dll` with zero subscription checks; `GetSelectedObjectId()` returns `(ulong?)0` when unselected.
  - `EventProvider.KeyDown` requires `HotKeysEnabled`; local `ddo.keymap` binds `TARGET_FELLOW_1_KEY`..`6_KEY` (`DIK_F1`..`DIK_F6`) and leaves `TARGET_SELF_KEY` and slots 7–12 unbound.

Agent counterpart: [Research guidance](../agents/REFERENCES.md).
