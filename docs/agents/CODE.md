# Agent code map and invariants

Human counterpart: [Architecture](../human/ARCHITECTURE.md). All paths below are repository-relative.

| Change | Inspect first | Preserve |
| --- | --- | --- |
| Identity / SDK lifecycle | `src/HealersHelperPlugin.cs`, `src/AssemblyInfo.cs` | Stable GUID/key (`ddo-healers-helper`), `SupportedOSPlatform("windows7.0")`, inert callbacks after disposal |
| Shared types & contracts | `src/Contracts.cs` | `TrackingMode`, `TargetMethod`, `BannerSeverity`, `HotkeyBinding`, `WatchedMemberSnapshot`, `AlertBannerView`, `IHealerEngineView` |
| HP scan & engine loop | `src/HealerEngine.cs` | Merge self (`GetCurrentCharacterId`/`GetCurrentCharacter`) + `GetPartyMembers()` + `GetEntity(id)` enrichment; short callbacks; lock discipline |
| Alert state & hysteresis | `src/HealerState.cs` | Pure portable C#; monotonic timestamps; threshold + hysteresis; `DeadAlertSeconds` suppression until HP > 0 & alive; `Notice` overlay; strict `PluginEnabled=false` silence |
| People Saved tracker | `src/PeopleSavedTracker.cs`, `src/HealerEngine.cs` | Pure portable C#; `SelfCastWindowSeconds=3.5`, `SaveWindowSeconds=5.0`, `MinHealDelta=5`, `damageTaken > PreHealHp`, `InTown()` guard, stale-dialog guard |
| Targeting | `src/TargetSelector.cs` | Guard `id > 0 && id < ulong.MaxValue`, `!IsDeleted`, `GetSelectedObjectId().GetValueOrDefault() != id`, cooldown; never invent unbound keys |
| Hotkeys | `src/HotkeyMatcher.cs`, `src/HealerEngine.cs` | Pure portable matcher + dedup (~250ms); `EventProvider.KeyDown` + foreground-gated `GetAsyncKeyState` poll; `TogglePluginHotkey` stays active when disabled |
| Settings | `src/Settings.cs`, `src/SettingsDialog.cs` | Atomic `.tmp` write; exact integer `FontSizePixels` (`18..140`) & `StatusFontSizePixels` (`9..48`); clamped ranges; `healers-helper.json` |
| Banner & status presentation | `src/BannerPresentation.cs`, `src/StatusHudPresentation.cs` | Pure portable C#; `FlashIntervalMs` alternation; `CriticalDown`/`LowHp`/`Notice`/`Editing` and compact status states |
| HUD STA & native surfaces | `src/BannerHudController.cs`, `src/BannerHudWindow.cs`, `src/StatusHudWindow.cs`, `src/NativeHud.cs` | Single STA thread hosting both windows; `ConcurrentQueue<HudCommand>`; reusable 32bpp DIB; `WDA_EXCLUDEFROMCAPTURE` (`0x11`); `WS_EX_TRANSPARENT`/`NOACTIVATE`; smoke hooks |
| DH panel & settings UI | `src/HealersHelperUi.cs`, `src/SettingsDialog.cs` | `FitToolbarWidth()`, `AutoSize` table/flow rows, `Plugin: ON/OFF` & `People Saved` header, no clipping at minimum host widths |

## Key invariants

- **Portable vs Windows split:** `Contracts.cs`, `Settings.cs`, `HotkeyMatcher.cs`, `PeopleSavedTracker.cs`, `HealerState.cs`, `BannerPresentation.cs`, and `StatusHudPresentation.cs` must remain 100% portable C# (no WinForms or Win32 P/Invoke) so `tests/HealersHelper.Tests.csproj` compiles and runs on both Windows and Linux.
- **Party + Self merging:** `GetPartyMembers()` omits the local player. `HealerEngine` assigns self `PartySlotIndex = 0` and party members `1..N`, enriching with `GetEntity(id)` for `LifeState` (`Dead=2`, `Incapacitated=3`, `Alive=4`) and live `HitPoints_Current` / `HitPoints_Max`.
- **`GetSelectedObjectId()` quirk:** Returns `(ulong?)0` when nothing is selected; use `.GetValueOrDefault()` and reject `0` and `ulong.MaxValue`.
- **HUD thread safety:** Engine publishes immutable `AlertBannerView` snapshots via `Volatile`. `BannerHudWindow` renders on its dedicated STA thread and disposes its `CreateDIBSection` / `HDC` handles cleanly on teardown without blocking DH's UI thread.
- **Capture exclusion & focus:** `SetWindowDisplayAffinity(Handle, settings.ExcludeFromCapture ? 0x11u : 0u)` is read back via `GetWindowDisplayAffinity`. Foreground restriction permits game PID, DH PID, or `LosslessScaling` process name (and skips restriction when `GameWindow == IntPtr.Zero` in synthetic smoke runs).
