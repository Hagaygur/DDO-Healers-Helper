using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoK.Sdk;
using VoK.Sdk.Ddo;
using VoK.Sdk.Ddo.Enums;
using VoK.Sdk.Events;
using VoK.Sdk.Helpers;

namespace VoK.HealersHelper;

// Async, non-blocking DH 5 Beta engine (net10.0, x64, zero WinForms/System.Media).
// Awaits frame-marshaled SDK calls outside lock(_gate), hydrates LifeState property
// 0x10000E0C live, tracks party connection state via eventProvider.Party, supports
// targetless InvokeHotbar quick-heal/quick-raise, and handles /hh chat commands.
internal sealed class Dh5HealerEngine : IHealerEngineView, IDisposable
{
    internal const uint LifeStatePropertyId = 0x10000E0Cu;
    private static readonly uint[] LifeStateProps = [LifeStatePropertyId];

    private const uint MbIconExclamation = 0x00000030u;
    private const uint CfUnicodeText = 13u;
    private const uint GmemMoveable = 0x0002u;
    private const uint StandardChatChannel = 0x04u;

    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;

    private readonly record struct HotkeyEvent(int VirtualKey, bool Ctrl, bool Shift, bool Alt, bool IsDown, double ReceivedAt);

    private sealed record WorldSnapshot(
        ulong? CharacterId,
        bool InTown,
        WatchedMemberSnapshot[] Members,
        int? SelfSpellPoints,
        bool RestoreTargetInRange,
        bool RestoreTargetDeadNonPlayer,
        HashSet<ulong> InRangeEntityIds);

    private readonly IDdoGameDataProvider _data;
    private readonly IDdoEventProvider _events;
    private readonly SettingsStore _store;
    private readonly HealerState _state = new();
    private readonly PeopleSavedTracker _savedTracker = new();
    private readonly StreakTracker _streakTracker = new();
    private readonly ReflexTracker _reflexTracker = new();
    private readonly TargetRestoreTracker _restoreTracker = new();
    private readonly HotkeyMatcher _hotkeys = new();
    private readonly ConcurrentQueue<HotkeyEvent> _hotkeyQueue = new();
    private readonly ConcurrentDictionary<ulong, bool> _partyConnected = new();
    private readonly HashSet<ulong> _mutedIds = new();
    private readonly HashSet<string> _mutedNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly Timer _timer;

    private IntPtr _gameWindow;
    private int _gamePid;
    private Settings _settings;
    private WatchedMemberSnapshot[] _members = Array.Empty<WatchedMemberSnapshot>();
    private HashSet<ulong> _inRangeEntityIds = new();
    private AlertBannerView _view = new(
        BannerSeverity.None,
        TrackingMode.Party,
        "",
        "",
        0,
        null,
        "",
        null,
        0,
        "Starting Healers Helper (DH 5)…",
        "Targeting ready",
        true,
        0);

    internal static readonly bool IsWineRuntime = DetectWineRuntime();

    private volatile bool _disposed;
    private volatile bool _pluginEnabledFlag = true;
    private volatile bool _hotkeyCaptureActive;
    private int _tickRunning;
    private bool _inTown;
    private int _settingsRevision;
    private ulong? _character;
    private long _characterIdBits;
    private long _lastSelfCastAtBits = BitConverter.DoubleToInt64Bits(double.NegativeInfinity);
    private int? _prevSelfSp;
    private long _lastSoundGen;
    private long _lastAutoTargetGen;
    private ulong _lastAutoTargetPlayerId;
    private double _nextAutoAllowedAt;
    private string _lastTargetStatus = "Targeting ready";
    private double _lastErrorLog = -100;
    private string? _pendingUiClipboardText;

    // Pending restore target metadata snapshot for async entity hydration on next tick.
    private ulong _pendingRestoreTargetId;
    private bool _restoreTargetInRange;
    private bool _restoreTargetDeadNonPlayer;

    internal static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    internal string Folder => _store.DirectoryPath;
    public IntPtr GameWindow => Volatile.Read(ref _gameWindow);
    public int SettingsRevision => Volatile.Read(ref _settingsRevision);
    public int PeopleSaved => Volatile.Read(ref _view).PeopleSaved;

    public Dh5HealerEngine(IDdoGameDataProvider data, IDdoEventProvider events, string folder)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _store = new SettingsStore(folder);

        IntPtr initialWindow = SafeGetGameWindowHandle(data);
        Volatile.Write(ref _gameWindow, initialWindow);
        _gamePid = ResolveGamePid(data, initialWindow);

        try
        {
            _settings = _store.Load();
        }
        catch (Exception e)
        {
            _settings = new Settings();
            Log("Settings could not be loaded; defaults in use: " + e.Message);
        }

        _pluginEnabledFlag = _settings.PluginEnabled;
        _view = _view with { PluginEnabled = _settings.PluginEnabled, PeopleSaved = _settings.PeopleSaved };

        // Subscribe to DH 5 events (each callback becomes a no-op once disposed).
        _events.KeyDown?.AddHandler(OnSdkKeyDown);
        _events.KeyUp?.AddHandler(OnSdkKeyUp);
        _events.OnLogin?.AddHandler(_ => OnSessionReset());
        _events.OnLogout?.AddHandler(() => OnSessionReset());
        _events.OnHotbarUsage?.AddHandler(_ => OnHotbarUsed());
        _events.OnRunScript?.AddHandler(OnRunScript);

        if (_events.Party is { } partyEvents)
        {
            partyEvents.OnMemberConnectionChanged?.AddHandler((id, connected) =>
            {
                if (!_disposed && id != 0) _partyConnected[id] = connected;
                return Task.CompletedTask;
            });
            partyEvents.OnMemberJoined?.AddHandler((id, _) =>
            {
                if (!_disposed && id != 0) _partyConnected[id] = true;
                return Task.CompletedTask;
            });
            partyEvents.OnMemberLeft?.AddHandler((id, _) =>
            {
                if (!_disposed && id != 0) _partyConnected.TryRemove(id, out bool _);
                return Task.CompletedTask;
            });
            partyEvents.OnDisbanded?.AddHandler(() =>
            {
                if (!_disposed) _partyConnected.Clear();
                return Task.CompletedTask;
            });
        }

        if (_events.OnChatCommand is { } chatCommands)
        {
            bool hhRegistered = chatCommands.AddHandler("hh", OnChatCommandAsync);
            bool fullRegistered = chatCommands.AddHandler("healershelper", OnChatCommandAsync);
            Log($"Chat command registration: /hh={hhRegistered}, /healershelper={fullRegistered}");
        }

        Log($"Started v{typeof(Dh5Plugin).Assembly.GetName().Version}; DH 5 Beta (VoK.Sdk 5.3.0.0).");
        _timer = new Timer(_ => _ = TriggerTickAsync(), null, 0, 120);
    }

    internal string? TakePendingUiClipboardText() =>
        Interlocked.Exchange(ref _pendingUiClipboardText, null);

    internal void SetHotkeyCaptureActive(bool active)
    {
        bool previous = _hotkeyCaptureActive;
        _hotkeyCaptureActive = active;
        if (active && !previous)
        {
            while (_hotkeyQueue.TryDequeue(out _)) { }
        }
    }

    private Task OnSdkKeyDown(HotKeyEventArgs args)
    {
        if (_disposed || _hotkeyCaptureActive || args is null || args.Key <= 0 || args.KeyUp || args.Win || HotkeyMatcher.IsModifierKey(args.Key))
            return Task.CompletedTask;
        if (_hotkeyQueue.Count < 128)
            _hotkeyQueue.Enqueue(new HotkeyEvent(args.Key, args.Control, args.Shift, args.Alt, true, Now));
        return Task.CompletedTask;
    }

    private Task OnSdkKeyUp(HotKeyEventArgs args)
    {
        if (_disposed || args is null || args.Key <= 0 || HotkeyMatcher.IsModifierKey(args.Key))
            return Task.CompletedTask;
        if (_hotkeyQueue.Count < 128)
            _hotkeyQueue.Enqueue(new HotkeyEvent(args.Key, args.Control, args.Shift, args.Alt, false, Now));
        return Task.CompletedTask;
    }

    private Task OnHotbarUsed()
    {
        if (!_disposed && _pluginEnabledFlag) RecordSelfCast(Now);
        return Task.CompletedTask;
    }

    private Task OnRunScript(ScriptEventArgs args)
    {
        if (_disposed || !_pluginEnabledFlag || args is null || args.ScriptSourceIid == 0)
            return Task.CompletedTask;
        ulong activeChar = unchecked((ulong)Volatile.Read(ref _characterIdBits));
        if (activeChar != 0 && args.ScriptSourceIid == activeChar)
            RecordSelfCast(Now);
        return Task.CompletedTask;
    }

    private Task OnChatCommandAsync(IChatCommand command)
    {
        if (_disposed || command is null)
            return Task.CompletedTask;
        return HandleChatCommandAsync(command.Rest ?? "");
    }

    internal Task HandleChatCommandAsync(string rest)
    {
        if (_disposed)
            return Task.CompletedTask;

        string trimmed = (rest ?? "").Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            PrintChat("Commands: /hh on|off | party | focus | hp <5-95> | banner | raid | theme <eberron|cannith|feywild> | heal | target | mute <name|clear> | score | reset | status");
            return Task.CompletedTask;
        }

        string[] parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        string sub = parts[0].ToLowerInvariant();
        string arg = parts.Length > 1 ? parts[1].Trim() : "";

        switch (sub)
        {
            case "on":
                SetPluginEnabled(true);
                PrintChat("Plugin enabled.");
                break;

            case "off":
                SetPluginEnabled(false);
                PrintChat("Plugin disabled.");
                break;

            case "party":
            {
                var s = GetSettings();
                s.Mode = TrackingMode.Party;
                SaveSettings(s);
                PrintChat($"Tracking mode: Party (Alert <= {s.HpThresholdPercent}%).");
                break;
            }

            case "focus":
            {
                bool ok = FocusCurrentTarget();
                var s = GetSettings();
                PrintChat(ok
                    ? $"Focused player: {s.FocusedPlayerName} (Single-Player mode)."
                    : "Select a player character in DDO first, then run /hh focus.");
                break;
            }

            case "hp":
                if (int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pct) && pct is >= 5 and <= 95)
                {
                    var s = GetSettings();
                    s.HpThresholdPercent = pct;
                    SaveSettings(s);
                    PrintChat($"Low-HP alert threshold set to {pct}%.");
                }
                else
                {
                    PrintChat("Usage: /hh hp <5-95>");
                }
                break;

            case "banner":
            case "hud":
            {
                ToggleBannerHud();
                var s = GetSettings();
                PrintChat($"Center alert banner: {(s.HudEnabled ? "ON" : "OFF")}.");
                break;
            }

            case "raid":
            {
                var s = GetSettings();
                s.RaidHudEnabled = !s.RaidHudEnabled;
                SaveSettings(s);
                PrintChat($"Triage Raid HUD: {(s.RaidHudEnabled ? "ON" : "OFF")}.");
                break;
            }

            case "theme":
                if (string.IsNullOrEmpty(arg))
                {
                    var cur = UiThemePalette.For(GetSettings().Theme);
                    PrintChat($"Active theme: {cur.DisplayName} ({cur.Key}). Options: eberron, cannith, feywild");
                }
                else if (UiThemePalette.TryParse(arg, out UiTheme parsedTheme))
                {
                    var s = GetSettings();
                    s.Theme = parsedTheme;
                    SaveSettings(s);
                    var pal = UiThemePalette.For(parsedTheme);
                    PrintChat($"UI theme set to {pal.DisplayName} ({pal.Key}).");
                }
                else
                {
                    PrintChat("Usage: /hh theme <eberron|cannith|feywild>");
                }
                break;

            case "score":
            case "scorecard":
            {
                string card = GetChatScorecard();
                _ = CopyChatScorecard();
                PrintChat(card);
                break;
            }

            case "mute":
                if (string.IsNullOrEmpty(arg))
                {
                    var muted = GetMutedPlayerNames();
                    PrintChat(muted.Count == 0
                        ? "No muted players this session. Usage: /hh mute <name> or /hh mute clear"
                        : "Muted this session: " + string.Join(", ", muted));
                }
                else if (arg.Equals("clear", StringComparison.OrdinalIgnoreCase))
                {
                    ClearMutedPlayers();
                    PrintChat("Cleared all session mutes.");
                }
                else
                {
                    var match = GetMembers().FirstOrDefault(m => string.Equals(m.Name, arg, StringComparison.OrdinalIgnoreCase));
                    bool nowMuted = ToggleMutePlayer(match?.Id ?? 0UL, match?.Name ?? arg);
                    PrintChat(nowMuted
                        ? $"Muted '{match?.Name ?? arg}' for this session."
                        : $"Unmuted '{match?.Name ?? arg}'.");
                }
                break;

            case "unmute":
                if (string.IsNullOrEmpty(arg))
                {
                    PrintChat("Usage: /hh unmute <name>");
                }
                else
                {
                    var match = GetMembers().FirstOrDefault(m => string.Equals(m.Name, arg, StringComparison.OrdinalIgnoreCase));
                    if (IsPlayerMuted(match?.Id ?? 0UL, match?.Name ?? arg))
                    {
                        ToggleMutePlayer(match?.Id ?? 0UL, match?.Name ?? arg);
                        PrintChat($"Unmuted '{match?.Name ?? arg}'.");
                    }
                    else
                    {
                        PrintChat($"'{arg}' is not currently muted.");
                    }
                }
                break;

            case "heal":
            case "cast":
            {
                bool castOk = QuickCastCriticalNow(out string summary);
                PrintChat(castOk ? summary : $"Quick-Cast skipped: {summary}");
                break;
            }

            case "target":
            {
                bool ok = TargetCriticalNow();
                PrintChat(ok ? $"Targeted critical ally ({_lastTargetStatus})." : $"Target Critical: {_lastTargetStatus}");
                break;
            }

            case "reset":
                ResetPeopleSaved();
                PrintChat("People Saved and healer records reset to 0.");
                break;

            case "status":
            {
                var s = GetSettings();
                var muted = GetMutedPlayerNames();
                string mutedPart = muted.Count > 0 ? $" | Muted: {string.Join(", ", muted)}" : "";
                PrintChat($"Plugin={(s.PluginEnabled ? "ON" : "OFF")} | Mode={s.Mode} | HP<={s.HpThresholdPercent}% | Theme={UiThemePalette.For(s.Theme).Key} | Banner={(s.HudEnabled ? "ON" : "OFF")} | RaidHUD={(s.RaidHudEnabled ? "ON" : "OFF")} | {GetRaidKeymapSummary()}{mutedPart}");
                break;
            }

            default:
                PrintChat($"Unknown subcommand '{sub}'. Try /hh help");
                break;
        }

        return Task.CompletedTask;
    }

    private void PrintChat(string message)
    {
        if (_disposed || string.IsNullOrWhiteSpace(message))
            return;
        try
        {
            _data.Print($"<RGB=#E2B75B>[Healer's Helper]</RGB> {message}", StandardChatChannel);
        }
        catch { }
    }

    private void RecordSelfCast(double now) =>
        Interlocked.Exchange(ref _lastSelfCastAtBits, BitConverter.DoubleToInt64Bits(now));

    private void ClearSelfCast() =>
        Interlocked.Exchange(ref _lastSelfCastAtBits, BitConverter.DoubleToInt64Bits(double.NegativeInfinity));

    private double ReadLastSelfCast() =>
        BitConverter.Int64BitsToDouble(Volatile.Read(ref _lastSelfCastAtBits));

    private Task OnSessionReset()
    {
        if (!_disposed)
        {
            lock (_gate)
            {
                if (_disposed) return Task.CompletedTask;
                _state.Reset();
                _savedTracker.Reset();
                _streakTracker.Reset();
                _reflexTracker.Reset();
                _restoreTracker.Reset();
                _hotkeys.Reset();
                _partyConnected.Clear();
                _mutedIds.Clear();
                _mutedNames.Clear();
                _nextAutoAllowedAt = 0;
                _character = null;
                _inTown = false;
                _pendingRestoreTargetId = 0;
                Volatile.Write(ref _characterIdBits, 0L);
                ClearSelfCast();
                _prevSelfSp = null;
                _lastAutoTargetGen = 0;
                _lastAutoTargetPlayerId = 0;
                _lastSoundGen = 0;
            }
        }
        return Task.CompletedTask;
    }

    private void CommitSettingsLocked(Settings updated)
    {
        updated.Validate();
        _settings = updated;
        _pluginEnabledFlag = updated.PluginEnabled;
        Interlocked.Increment(ref _settingsRevision);
        try
        {
            _store.Save(updated);
        }
        catch (Exception e)
        {
            Log("Failed to persist settings: " + e.Message);
        }
    }

    public Settings GetSettings()
    {
        lock (_gate) return _settings.Copy();
    }

    public void SaveSettings(Settings settings)
    {
        lock (_gate)
        {
            if (_disposed) return;
            var copy = settings.Copy();
            copy.CopyLiveStatsFrom(_settings);
            bool turnedOff = _settings.PluginEnabled && !copy.PluginEnabled;
            bool pausedMode = copy.Mode == TrackingMode.Off;
            CommitSettingsLocked(copy);
            if (turnedOff)
            {
                ResetTransientOnDisableLocked();
            }
            else
            {
                if (pausedMode)
                {
                    _savedTracker.Reset();
                    _reflexTracker.Reset();
                }

                if (!copy.RestorePreviousTargetAfterHeal || pausedMode || copy.TargetingMethod == TargetMethod.Off)
                {
                    _restoreTracker.Reset();
                    _pendingRestoreTargetId = 0;
                }
            }
            EvaluateLocked(Now);
        }
    }

    public void SavePosition(double x, double y)
    {
        lock (_gate)
        {
            if (_disposed) return;
            var copy = _settings.Copy();
            copy.HudX = x;
            copy.HudY = y;
            CommitSettingsLocked(copy);
        }
    }

    public void SaveBannerLayout(double x, double y, int fontSizePixels, int bannerOpacity)
    {
        lock (_gate)
        {
            if (_disposed) return;
            var copy = _settings.Copy();
            copy.HudX = x;
            copy.HudY = y;
            copy.FontSizePixels = fontSizePixels;
            copy.BannerOpacity = bannerOpacity;
            CommitSettingsLocked(copy);
        }
    }

    public void SaveStatusPosition(double x, double y)
    {
        lock (_gate)
        {
            if (_disposed) return;
            var copy = _settings.Copy();
            copy.StatusHudX = x;
            copy.StatusHudY = y;
            CommitSettingsLocked(copy);
        }
    }

    public void SaveRaidHudPosition(double x, double y)
    {
        lock (_gate)
        {
            if (_disposed) return;
            var copy = _settings.Copy();
            copy.RaidHudX = x;
            copy.RaidHudY = y;
            CommitSettingsLocked(copy);
        }
    }

    public AlertBannerView View() => Volatile.Read(ref _view);

    public WatchedMemberSnapshot[] GetMembers() => Volatile.Read(ref _members);

    public void Report(string message)
    {
        lock (_gate) Log(message);
    }

    public void TogglePluginEnabled()
    {
        lock (_gate)
        {
            if (_disposed) return;
            SetPluginEnabledLocked(!_settings.PluginEnabled, Now);
        }
    }

    public void SetPluginEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (_disposed) return;
            SetPluginEnabledLocked(enabled, Now);
        }
    }

    public void ToggleBannerHud()
    {
        lock (_gate)
        {
            if (_disposed) return;
            ToggleBannerHudLocked(Now);
        }
    }

    public void ResetPeopleSaved()
    {
        lock (_gate)
        {
            if (_disposed) return;
            ResetPeopleSavedLocked(Now);
        }
    }

    private void ResetPeopleSavedLocked(double now)
    {
        var copy = _settings.Copy();
        copy.ResetLiveStats();
        CommitSettingsLocked(copy);
        if (_settings.PluginEnabled)
            _state.SetNotice("STATS RESET", "People Saved and healer records reset to 0", 1.8, now);
        EvaluateLocked(now);
    }

    public string GetChatScorecard()
    {
        lock (_gate) return HealerStats.FormatChatScorecard(_settings);
    }

    public bool CopyChatScorecard()
    {
        string card;
        lock (_gate)
        {
            if (_disposed) return false;
            card = HealerStats.FormatChatScorecard(_settings);
            if (_settings.PluginEnabled)
            {
                _state.SetNotice("SCORECARD COPIED", "Paste into DDO chat (/p or /raid)", 2.0, Now);
                EvaluateLocked(Now);
            }
        }

        return TryCopyTextToClipboard(card);
    }

    internal bool TryCopyTextToClipboard(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        // Never invoke single-threaded ImGui.SetClipboardText off the render thread:
        // prefer Win32 clipboard directly, and fall back to a volatile handoff drained on ImGui_Render().
        if (TryCopyTextToWin32Clipboard(text))
            return true;

        Interlocked.Exchange(ref _pendingUiClipboardText, text);
        return true;
    }

    internal static bool TryCopyTextToWin32Clipboard(string text)
    {
        if (string.IsNullOrEmpty(text) || !OperatingSystem.IsWindows())
            return false;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (!OpenClipboard(IntPtr.Zero))
            {
                Thread.Sleep(10);
                continue;
            }

            try
            {
                if (!EmptyClipboard())
                    return false;

                byte[] bytes = System.Text.Encoding.Unicode.GetBytes(text + "\0");
                IntPtr hGlobal = GlobalAlloc(GmemMoveable, (UIntPtr)bytes.Length);
                if (hGlobal == IntPtr.Zero)
                    return false;

                IntPtr locked = GlobalLock(hGlobal);
                if (locked == IntPtr.Zero)
                {
                    GlobalFree(hGlobal);
                    return false;
                }

                try
                {
                    Marshal.Copy(bytes, 0, locked, bytes.Length);
                }
                finally
                {
                    GlobalUnlock(hGlobal);
                }

                if (SetClipboardData(CfUnicodeText, hGlobal) == IntPtr.Zero)
                {
                    GlobalFree(hGlobal);
                    return false;
                }

                // Ownership transferred to the OS clipboard on success.
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                CloseClipboard();
            }
        }

        return false;
    }

    public bool IsPlayerMuted(ulong id, string? name)
    {
        lock (_gate) return IsPlayerMutedLocked(id, name);
    }

    public bool ToggleMutePlayer(ulong id, string? name)
    {
        lock (_gate)
        {
            if (_disposed) return false;
            string cleanName = (name ?? "").Trim();
            if (IsPlayerMutedLocked(id, cleanName))
            {
                if (id != 0) _mutedIds.Remove(id);
                if (!string.IsNullOrEmpty(cleanName)) _mutedNames.Remove(cleanName);
                if (_settings.PluginEnabled)
                    _state.SetNotice($"UNMUTED: {(string.IsNullOrEmpty(cleanName) ? $"ID {id}" : cleanName)}", "Alerts resumed for this player", 1.8, Now);
                EvaluateLocked(Now);
                return false;
            }

            if (id != 0) _mutedIds.Add(id);
            if (!string.IsNullOrEmpty(cleanName)) _mutedNames.Add(cleanName);
            if (_settings.PluginEnabled)
                _state.SetNotice($"MUTED: {(string.IsNullOrEmpty(cleanName) ? $"ID {id}" : cleanName)}", "Session alerts suppressed (Pale Master / ignore)", 1.8, Now);
            EvaluateLocked(Now);
            return true;
        }
    }

    public void ClearMutedPlayers()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _mutedIds.Clear();
            _mutedNames.Clear();
            if (_settings.PluginEnabled)
                _state.SetNotice("MUTES CLEARED", "All party members unmuted for this session", 1.8, Now);
            EvaluateLocked(Now);
        }
    }

    public IReadOnlyCollection<string> GetMutedPlayerNames()
    {
        lock (_gate)
        {
            if (_mutedNames.Count == 0 && _mutedIds.Count == 0)
                return Array.Empty<string>();
            var list = new List<string>(_mutedNames);
            foreach (ulong id in _mutedIds)
            {
                var found = _members.FirstOrDefault(m => m.Id == id);
                if (found is not null && !string.IsNullOrWhiteSpace(found.Name) && !_mutedNames.Contains(found.Name))
                    list.Add(found.Name);
            }
            return list;
        }
    }

    public string GetRaidKeymapSummary()
    {
        if (_data.Keymap is null)
            return TargetPolicy.EvaluateRaidKeymapReadiness(null).Summary;

        return TargetPolicy.EvaluateRaidKeymapReadiness(action =>
        {
            try { return _data.Keymap.GetHotkey(action) is not null; }
            catch { return false; }
        }).Summary;
    }

    public bool FocusCurrentTarget()
    {
        lock (_gate)
        {
            if (_disposed || !_settings.PluginEnabled) return false;
            return FocusCurrentTargetLocked(Now);
        }
    }

    public bool FocusMember(WatchedMemberSnapshot member)
    {
        if (member is null || member.Id == 0)
            return false;

        lock (_gate)
        {
            if (_disposed) return false;
            var updated = _settings.Copy();
            updated.FocusedPlayerId = member.Id;
            updated.FocusedPlayerName = member.Name;
            updated.Mode = TrackingMode.SinglePlayer;
            CommitSettingsLocked(updated);
            if (_settings.PluginEnabled)
                _state.SetNotice($"WATCHING: {member.Name}", "Single-player focus active", 2.0, Now);
            EvaluateLocked(Now);
            return true;
        }
    }

    public void CycleMode()
    {
        lock (_gate)
        {
            if (_disposed || !_settings.PluginEnabled) return;
            CycleModeLocked(Now);
        }
    }

    public bool TargetCriticalNow()
    {
        lock (_gate)
        {
            if (_disposed || !_settings.PluginEnabled) return false;
            return TargetCriticalLocked(Now, bypassCooldown: true);
        }
    }

    public bool SelectMember(WatchedMemberSnapshot member)
    {
        if (member is null || member.Id == 0)
            return false;

        lock (_gate)
        {
            if (_disposed || !_settings.PluginEnabled) return false;
            return TrySelectTargetLocked(member.Id, member.Name, member.PartySlotIndex, Now, bypassCooldown: true, recordRestore: false);
        }
    }

    // Targetless DH 5 InvokeHotbar Quick-Heal (living/INCAP) or Quick-Raise (DEAD)
    // without dropping the healer's current Boss or Tank hard target!
    public bool QuickCastOnMember(WatchedMemberSnapshot member, out string summary)
    {
        if (member is null || member.Id == 0 || member.Id == ulong.MaxValue)
        {
            summary = "Invalid target";
            return false;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                summary = "Engine stopped";
                return false;
            }

            return QuickCastOnMemberLocked(member, Now, out summary);
        }
    }

    public bool QuickCastCriticalNow(out string summary)
    {
        lock (_gate)
        {
            if (_disposed || !_settings.PluginEnabled)
            {
                summary = "Plugin disabled";
                return false;
            }

            WatchedMemberSnapshot? target = ResolveHighestPriorityMemberLocked(Now);
            if (target is null)
            {
                summary = "No watched connected ally";
                _state.SetNotice("NO QUICK-CAST TARGET", summary, 1.8, Now);
                EvaluateLocked(Now);
                return false;
            }

            return QuickCastOnMemberLocked(target, Now, out summary);
        }
    }

    private bool QuickCastOnMemberLocked(WatchedMemberSnapshot member, double now, out string summary)
    {
        bool isRaise = member.IsTrulyDead;
        int bar = isRaise ? _settings.QuickRaiseHotbar : _settings.QuickHealHotbar;
        int slot = isRaise ? _settings.QuickRaiseSlot : _settings.QuickHealSlot;
        string kind = isRaise ? "Quick-Raise" : "Quick-Heal";

        if (bar is < 1 or > 20 || slot is < 1 or > 10)
        {
            summary = $"{kind} hotbar not configured (set in Targeting & Quick-Cast)";
            return false;
        }

        bool ok = false;
        try
        {
            ok = _data.InvokeHotbar((uint)(bar - 1), (uint)(slot - 1), member.Id);
        }
        catch (Exception ex)
        {
            Log($"{kind} InvokeHotbar failed: {ex.Message}");
        }

        if (ok)
        {
            RecordSelfCast(now);
            summary = $"{kind} (Bar {bar} Slot {slot}) -> {member.Name} [targetless]";
            _lastTargetStatus = summary;
            if (_settings.PluginEnabled)
            {
                _state.SetNotice($"{kind.ToUpperInvariant()}: {member.Name}", $"Bar {bar} Slot {slot} • Targetless cast", 1.5, now);
                EvaluateLocked(now);
            }
            return true;
        }

        summary = $"{kind} (Bar {bar} Slot {slot}) -> {member.Name} declined by client";
        _lastTargetStatus = summary;
        return false;
    }

    private async Task TriggerTickAsync()
    {
        if (_disposed || Interlocked.CompareExchange(ref _tickRunning, 1, 0) != 0)
            return;

        try
        {
            await TickAsync().ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _tickRunning, 0);
        }
    }

    internal async Task RunTickOnceForTestAsync()
    {
        if (_disposed) return;
        await TickAsync().ConfigureAwait(false);
    }

    private async Task TickAsync()
    {
        if (_disposed)
            return;

        try
        {
            double now = Now;
            Settings settingsSnapshot;
            ulong restoreIdToCheck;
            string? deferredClipboard = null;
            lock (_gate)
            {
                if (_disposed) return;
                RefreshGameWindowAndPidLocked();
                DrainHotkeysLocked(now, ref deferredClipboard);
                PollForegroundHotkeysLocked(now, ref deferredClipboard);
                CheckReflexSelfCastLocked();
                settingsSnapshot = _settings.Copy();
                restoreIdToCheck = _pendingRestoreTargetId;
            }

            if (!string.IsNullOrEmpty(deferredClipboard))
            {
                _ = TryCopyTextToClipboard(deferredClipboard);
            }

            // Await all frame-marshaled DH 5 SDK calls strictly OUTSIDE lock(_gate)!
            WorldSnapshot world = await CollectWorldSnapshotAsync(settingsSnapshot, restoreIdToCheck).ConfigureAwait(false);

            lock (_gate)
            {
                if (_disposed) return;
                ApplyWorldSnapshotLocked(world, Now);
                CheckReflexSelfCastLocked();
                EvaluateLocked(Now);
            }
        }
        catch (Exception e)
        {
            lock (_gate)
            {
                if (_disposed) return;
                Volatile.Write(ref _view, new AlertBannerView(
                    BannerSeverity.None,
                    _settings.Mode,
                    "",
                    "",
                    0,
                    null,
                    "",
                    null,
                    _view.AlertGeneration,
                    _settings.PluginEnabled ? "Game data unavailable • retrying" : "Plugin Off",
                    _lastTargetStatus,
                    _settings.PluginEnabled,
                    _settings.PeopleSaved));
                if (Now - _lastErrorLog > 15)
                {
                    Log("DH 5 SDK scan failed: " + e.Message);
                    _lastErrorLog = Now;
                }
            }
        }
    }

    private async Task<WorldSnapshot> CollectWorldSnapshotAsync(Settings settings, ulong restoreTargetId)
    {
        ulong? charId = _data.GetCurrentCharacterId();
        var inRange = new HashSet<ulong>();

        if (!charId.HasValue || charId.Value == 0 || charId.Value == ulong.MaxValue)
        {
            return new WorldSnapshot(null, false, Array.Empty<WatchedMemberSnapshot>(), null, false, false, inRange);
        }

        bool inTown = false;
        try
        {
            inTown = (await _data.InTownAsync().ConfigureAwait(false)) == true;
        }
        catch { }

        ulong selfId = charId.Value;
        var list = new List<WatchedMemberSnapshot>(12);
        var seen = new HashSet<ulong>();

        IEntity? selfEntity = null;
        try { selfEntity = await _data.GetCurrentCharacterAsync().ConfigureAwait(false); }
        catch { }
        if (selfEntity is null)
        {
            try { selfEntity = await _data.GetEntityAsync(selfId).ConfigureAwait(false); }
            catch { }
        }

        string? charName = null;
        if (string.IsNullOrWhiteSpace(selfEntity?.Name))
        {
            try { charName = await _data.GetCurrentCharacterNameAsync().ConfigureAwait(false); }
            catch { }
        }

        string selfName = !string.IsNullOrWhiteSpace(selfEntity?.Name)
            ? selfEntity!.Name
            : (!string.IsNullOrWhiteSpace(charName) ? charName : "Self");

        int? selfSp = null;
        if (selfEntity is not null && !selfEntity.IsDeleted)
        {
            await HydrateLifeStateAsync(selfEntity).ConfigureAwait(false);
        }

        if (selfEntity is not null && !selfEntity.IsDeleted)
        {
            inRange.Add(selfId);
            LifeState? lifeState = SafeGetLifeState(selfEntity);
            bool isTrulyDead = lifeState == LifeState.Dead;
            bool explicitDown = isTrulyDead || lifeState == LifeState.Incapacitated;

            int hp;
            int maxHp;
            if (!selfEntity.HitPoints_Current.HasValue && !explicitDown)
            {
                hp = 0;
                maxHp = 0;
            }
            else
            {
                hp = selfEntity.HitPoints_Current ?? 0;
                maxHp = selfEntity.HitPoints_Max ?? 0;
            }

            int tempHp = Math.Max(0, selfEntity.HitPoints_Temp ?? 0);
            int sp = Math.Max(0, selfEntity.SpellPoints_Current ?? 0);
            int maxSp = Math.Max(0, selfEntity.SpellPoints_Max ?? 0);
            bool isDown = explicitDown || (maxHp > 0 && hp <= 0);
            list.Add(new WatchedMemberSnapshot(selfId, selfName, hp, maxHp, 0, true, isDown, isTrulyDead, tempHp, sp, maxSp));
            selfSp = selfEntity.SpellPoints_Current;
        }
        else
        {
            list.Add(new WatchedMemberSnapshot(selfId, selfName, 0, 0, 0, false, false, false));
        }
        seen.Add(selfId);

        IReadOnlyList<IPartyMember>? party = null;
        try { party = _data.Party?.GetMembers(); }
        catch { }

        if (party is not null)
        {
            int slot = 0;
            foreach (var m in party)
            {
                if (m is null || m.Id == 0 || m.Id == ulong.MaxValue || m.Id == selfId || !seen.Add(m.Id))
                    continue;
                slot++;

                bool connected = _partyConnected.TryGetValue(m.Id, out bool knownConn) ? knownConn : true;

                IEntity? ent = null;
                try { ent = await _data.GetEntityAsync(m.Id).ConfigureAwait(false); }
                catch { }

                if (ent is not null && !ent.IsDeleted)
                {
                    await HydrateLifeStateAsync(ent).ConfigureAwait(false);
                }

                // Re-check IsDeleted strictly AFTER awaiting frame-marshaled UpdateProperties.
                bool hasLiveEntity = ent is not null && !ent.IsDeleted;
                LifeState? entLife = null;
                if (hasLiveEntity)
                {
                    inRange.Add(m.Id);
                    entLife = SafeGetLifeState(ent!);
                }

                int hp = (hasLiveEntity ? ent!.HitPoints_Current : null) ?? m.HitPoints;
                int maxHp = (hasLiveEntity ? ent!.HitPoints_Max : null) ?? m.MaxHitPoints;
                int tempHp = Math.Max(0, (hasLiveEntity ? ent!.HitPoints_Temp : null) ?? 0);
                int sp = Math.Max(0, (hasLiveEntity ? ent!.SpellPoints_Current : null) ?? m.SpellPoints);
                int maxSp = Math.Max(0, (hasLiveEntity ? ent!.SpellPoints_Max : null) ?? m.MaxSpellPoints);

                string name = !string.IsNullOrWhiteSpace(m.Name)
                    ? m.Name
                    : (hasLiveEntity && !string.IsNullOrWhiteSpace(ent!.Name) ? ent.Name : $"Member {slot}");

                bool isTrulyDead = connected && hasLiveEntity && entLife == LifeState.Dead;
                bool isDown = (hasLiveEntity && entLife is LifeState.Dead or LifeState.Incapacitated)
                    || (connected && maxHp > 0 && hp <= 0);

                list.Add(new WatchedMemberSnapshot(m.Id, name, hp, maxHp, slot, connected, isDown, isTrulyDead, tempHp, sp, maxSp));
            }
        }

        if (settings.Mode == TrackingMode.SinglePlayer)
        {
            await TryAddExternalFocusedPlayerAsync(settings, list, seen, inRange).ConfigureAwait(false);
        }

        bool restoreInRange = false;
        bool restoreDeadNonPlayer = false;
        if (restoreTargetId > 0 && restoreTargetId < ulong.MaxValue)
        {
            IEntity? restoreEnt = null;
            try { restoreEnt = await _data.GetEntityAsync(restoreTargetId).ConfigureAwait(false); }
            catch { }

            if (restoreEnt is not null && !restoreEnt.IsDeleted)
            {
                await HydrateLifeStateAsync(restoreEnt).ConfigureAwait(false);
                if (!restoreEnt.IsDeleted)
                {
                    restoreInRange = true;
                    inRange.Add(restoreTargetId);
                    LifeState? rLife = SafeGetLifeState(restoreEnt);
                    restoreDeadNonPlayer = !EntityExtensions.IsPlayer(restoreEnt)
                        && (rLife == LifeState.Dead || (restoreEnt.HitPoints_Max > 0 && restoreEnt.HitPoints_Current <= 0));
                }
            }
        }

        return new WorldSnapshot(charId, inTown, list.ToArray(), selfSp, restoreInRange, restoreDeadNonPlayer, inRange);
    }

    private async Task TryAddExternalFocusedPlayerAsync(
        Settings settings,
        List<WatchedMemberSnapshot> list,
        HashSet<ulong> seen,
        HashSet<ulong> inRange)
    {
        bool alreadyPresent = list.Any(m =>
            (settings.FocusedPlayerId > 0 && m.Id == settings.FocusedPlayerId) ||
            (!string.IsNullOrWhiteSpace(settings.FocusedPlayerName)
                && string.Equals(m.Name, settings.FocusedPlayerName, StringComparison.OrdinalIgnoreCase)));
        if (alreadyPresent) return;

        IEntity? ent = null;
        if (settings.FocusedPlayerId > 0 && EntityExtensions.IsPlayer(settings.FocusedPlayerId))
        {
            try { ent = await _data.GetEntityAsync(settings.FocusedPlayerId).ConfigureAwait(false); }
            catch { }
        }

        if ((ent is null || ent.IsDeleted || !EntityExtensions.IsPlayer(ent)) && !string.IsNullOrWhiteSpace(settings.FocusedPlayerName))
        {
            IReadOnlyList<IEntity>? entities = null;
            try { entities = await _data.GetEntitiesAsync().ConfigureAwait(false); }
            catch { }

            if (entities is not null)
            {
                foreach (var candidate in entities)
                {
                    if (candidate is null || candidate.IsDeleted || !EntityExtensions.IsPlayer(candidate)) continue;
                    if (string.Equals(candidate.Name, settings.FocusedPlayerName, StringComparison.OrdinalIgnoreCase))
                    {
                        ent = candidate;
                        break;
                    }
                }
            }
        }

        if (ent is null || ent.IsDeleted || ent.InstanceId == 0 || !EntityExtensions.IsPlayer(ent))
            return;

        await HydrateLifeStateAsync(ent).ConfigureAwait(false);
        if (ent.IsDeleted || !seen.Add(ent.InstanceId))
            return;

        inRange.Add(ent.InstanceId);
        LifeState? life = SafeGetLifeState(ent);
        bool isTrulyDead = life == LifeState.Dead;
        bool explicitDown = isTrulyDead || life == LifeState.Incapacitated;

        int hp;
        int maxHp;
        if (!ent.HitPoints_Current.HasValue && !explicitDown)
        {
            hp = 0;
            maxHp = 0;
        }
        else
        {
            hp = ent.HitPoints_Current ?? 0;
            maxHp = ent.HitPoints_Max ?? 0;
        }

        int tempHp = Math.Max(0, ent.HitPoints_Temp ?? 0);
        int sp = Math.Max(0, ent.SpellPoints_Current ?? 0);
        int maxSp = Math.Max(0, ent.SpellPoints_Max ?? 0);
        bool isDown = explicitDown || (maxHp > 0 && hp <= 0);
        string name = !string.IsNullOrWhiteSpace(ent.Name)
            ? ent.Name
            : (!string.IsNullOrWhiteSpace(settings.FocusedPlayerName)
                ? settings.FocusedPlayerName
                : $"Player 0x{ent.InstanceId:X}");
        list.Add(new WatchedMemberSnapshot(ent.InstanceId, name, hp, maxHp, -1, true, isDown, isTrulyDead, tempHp, sp, maxSp));
    }

    private static async Task HydrateLifeStateAsync(IEntity? entity)
    {
        if (entity is null || entity.IsDeleted)
            return;

        try
        {
            await entity.UpdateProperties(LifeStateProps).ConfigureAwait(false);
        }
        catch { }
    }

    private static LifeState? SafeGetLifeState(IEntity entity)
    {
        try { return entity.LifeState(); }
        catch { return null; }
    }

    private void ApplyWorldSnapshotLocked(WorldSnapshot world, double now)
    {
        if (world.CharacterId != _character)
        {
            _state.Reset();
            _savedTracker.Reset();
            _streakTracker.Reset();
            _reflexTracker.Reset();
            _restoreTracker.Reset();
            _partyConnected.Clear();
            _mutedIds.Clear();
            _mutedNames.Clear();
            _nextAutoAllowedAt = 0;
            _pendingRestoreTargetId = 0;
            _character = world.CharacterId;
            _inTown = false;
            Volatile.Write(ref _characterIdBits, unchecked((long)(world.CharacterId ?? 0)));
            ClearSelfCast();
            _prevSelfSp = null;
            _lastAutoTargetGen = 0;
            _lastAutoTargetPlayerId = 0;
            _lastSoundGen = 0;
        }

        _inTown = world.InTown;
        _inRangeEntityIds = world.InRangeEntityIds;
        _restoreTargetInRange = world.RestoreTargetInRange;
        _restoreTargetDeadNonPlayer = world.RestoreTargetDeadNonPlayer;
        Volatile.Write(ref _members, world.Members);

        if (!world.CharacterId.HasValue || world.CharacterId.Value == 0 || world.CharacterId.Value == ulong.MaxValue)
        {
            _savedTracker.Reset();
            _streakTracker.Reset();
            _reflexTracker.Reset();
            _restoreTracker.Reset();
            _pendingRestoreTargetId = 0;
            _lastAutoTargetPlayerId = 0;
            _prevSelfSp = null;
            return;
        }

        if (_settings.PluginEnabled && _prevSelfSp.HasValue && world.SelfSpellPoints.HasValue && world.SelfSpellPoints.Value < _prevSelfSp.Value)
        {
            RecordSelfCast(now);
        }
        _prevSelfSp = world.SelfSpellPoints;

        if (!_settings.PluginEnabled || _settings.Mode == TrackingMode.Off)
        {
            _savedTracker.Reset();
            return;
        }

        var watchedList = HealerState.FilterWatched(world.Members, _settings, IsMemberMutedLocked);
        var watchedArray = watchedList.ToArray();

        var updated = _settings.Copy();
        bool settingsChanged = _streakTracker.ProcessWatched(world.Members, watchedList, _settings.PluginEnabled, world.InTown, updated);

        var saves = _savedTracker.ProcessScanDetailed(
            watchedArray,
            _settings.PluginEnabled,
            world.InTown,
            now,
            ReadLastSelfCast(),
            _settings.SaveWindowSeconds,
            _settings.MinHealDelta,
            _settings.SelfCastWindowSeconds);

        if (saves.Count > 0)
        {
            foreach (var save in saves)
                HealerStats.ApplyClutchSave(updated, save);
            settingsChanged = true;

            if (_settings.ShowSavedNotice)
            {
                _state.SetNotice(
                    $"SAVED {saves[0].PlayerName}! (Saved: {updated.PeopleSaved})",
                    $"Streak {updated.CurrentSaveStreak} • {HealerStats.RankForSaves(updated.PeopleSaved)}",
                    2.0,
                    now);
            }
        }

        if (settingsChanged)
        {
            CommitSettingsLocked(updated);
        }
    }

    private void EvaluateLocked(double now)
    {
        WatchedMemberSnapshot[] current = _members;
        string statusLine = !_settings.PluginEnabled
            ? "Plugin Off"
            : !_character.HasValue || _character == 0
                ? "Waiting for character login"
                : "";

        AlertBannerView view = _state.Evaluate(current, _settings, now, statusLine, _lastTargetStatus, _inTown, IsMemberMutedLocked);

        if (_settings.PluginEnabled
            && (view.Severity is BannerSeverity.LowHp or BannerSeverity.CriticalDown || view.AlertPlayerId.HasValue))
        {
            if (view.Severity is BannerSeverity.LowHp or BannerSeverity.CriticalDown
                && view.AlertPlayerId is { } alertId)
            {
                if (_settings.SoundOnAlert && !view.IsSoulstone && view.AlertGeneration != _lastSoundGen)
                {
                    _lastSoundGen = view.AlertGeneration;
                    PlayAlertBeep();
                }

                if (_settings.AutoTargetOnAlert
                    && !view.IsSoulstone
                    && _settings.TargetingMethod != TargetMethod.Off
                    && view.AlertGeneration != _lastAutoTargetGen
                    && IsGameForegroundOrActive())
                {
                    var target = current.FirstOrDefault(m => m.Id == alertId);
                    if (target is not null)
                    {
                        int recoverAt = Math.Min(100, _settings.HpThresholdPercent + _settings.RecoveryHysteresisPercent);
                        bool prevHealedBypass = _lastAutoTargetPlayerId != 0
                            && _lastAutoTargetPlayerId != alertId
                            && TargetRestoreTracker.IsPreviousTargetHealedOrGone(current, _lastAutoTargetPlayerId, recoverAt);

                        bool selected = TrySelectTargetLocked(
                            target.Id,
                            target.Name,
                            target.PartySlotIndex,
                            now,
                            bypassCooldown: prevHealedBypass,
                            recordRestore: _settings.RestorePreviousTargetAfterHeal);
                        ulong currentSelected = _data.GetSelectedObjectId().GetValueOrDefault();
                        if (selected || currentSelected == target.Id)
                        {
                            _lastAutoTargetGen = view.AlertGeneration;
                            _lastAutoTargetPlayerId = target.Id;
                        }
                        view = view with { TargetingStatus = _lastTargetStatus };
                    }
                }
            }
        }
        else
        {
            ulong latestSelected = _data.GetSelectedObjectId().GetValueOrDefault();
            if (_restoreTracker.EvaluateRestore(
                    current,
                    _settings,
                    latestSelected,
                    view.Severity,
                    view.AlertPlayerId,
                    _inTown,
                    IsGameForegroundOrActive(),
                    now,
                    IsMemberMutedLocked) is { } candidate)
            {
                TryRestoreTargetLocked(candidate);
                _pendingRestoreTargetId = 0;
                view = view with { TargetingStatus = _lastTargetStatus };
            }
            else if (!_restoreTracker.IsActive)
            {
                _pendingRestoreTargetId = 0;
            }
        }

        _reflexTracker.OnAlertEvaluated(view, now);
        Volatile.Write(ref _view, view);
    }

    private bool TrySelectTargetLocked(
        ulong targetId,
        string targetName,
        int partySlotIndex,
        double now,
        bool bypassCooldown,
        bool recordRestore)
    {
        bool entityInRange = _inRangeEntityIds.Contains(targetId);
        string? partyAction = TargetPolicy.PartyActionForSlot(partySlotIndex);
        HotKeyEventArgs? hotkey = partyAction is not null ? SafeGetKeymapHotkey(partyAction) : null;
        ulong currentSelected = _data.GetSelectedObjectId().GetValueOrDefault();
        double cooldownRemaining = Math.Max(0, _nextAutoAllowedAt - now);

        TargetDecision decision = TargetPolicy.Evaluate(
            targetId,
            targetName,
            partySlotIndex,
            entityInRange,
            hotkey is not null,
            currentSelected,
            _settings.TargetingMethod,
            bypassCooldown,
            cooldownRemaining);

        _lastTargetStatus = decision.Status;
        if (decision.Action == TargetActionKind.None)
            return false;

        bool executed = false;
        if (decision.Action == TargetActionKind.SdkSelectObject)
        {
            _data.SelectObject(decision.TargetId);
            _nextAutoAllowedAt = now + _settings.AutoTargetCooldownSeconds;
            executed = true;
        }
        else if (decision.Action == TargetActionKind.PartyKeybind && hotkey is not null)
        {
            _data.SendKey(hotkey);
            _nextAutoAllowedAt = now + _settings.AutoTargetCooldownSeconds;
            executed = true;
        }

        if (executed && recordRestore)
        {
            var targetMember = _members.FirstOrDefault(m => m.Id == targetId);
            if (targetMember is not null)
            {
                RecordRestoreSwitchLocked(currentSelected, _members, targetMember, now);
            }
        }

        return executed;
    }

    private void TryRestoreTargetLocked(RestoreCandidate candidate)
    {
        ulong currentSelected = _data.GetSelectedObjectId().GetValueOrDefault();
        if (candidate.ExpectedCurrentTargetId != 0 && currentSelected != candidate.ExpectedCurrentTargetId)
        {
            _lastTargetStatus = "Restore skipped (target changed)";
            return;
        }

        bool inRange = candidate.TargetId == _pendingRestoreTargetId
            ? _restoreTargetInRange
            : _inRangeEntityIds.Contains(candidate.TargetId);
        bool deadNonPlayer = candidate.TargetId == _pendingRestoreTargetId && _restoreTargetDeadNonPlayer;

        string? partyAction = TargetPolicy.PartyActionForSlot(candidate.PartySlotIndex);
        HotKeyEventArgs? hotkey = partyAction is not null ? SafeGetKeymapHotkey(partyAction) : null;

        TargetDecision decision = TargetPolicy.EvaluateRestore(
            candidate.TargetId,
            candidate.TargetName,
            candidate.PartySlotIndex,
            inRange,
            deadNonPlayer,
            hotkey is not null,
            currentSelected,
            _settings.TargetingMethod);

        _lastTargetStatus = decision.Status;
        if (decision.Action == TargetActionKind.SdkSelectObject)
        {
            _data.SelectObject(decision.TargetId);
        }
        else if (decision.Action == TargetActionKind.PartyKeybind && hotkey is not null)
        {
            _data.SendKey(hotkey);
        }
    }

    private void RecordRestoreSwitchLocked(
        ulong priorSelected,
        WatchedMemberSnapshot[] current,
        WatchedMemberSnapshot target,
        double now)
    {
        if (priorSelected == 0 || priorSelected == ulong.MaxValue || priorSelected == target.Id)
            return;

        var priorMember = current.FirstOrDefault(m => m.Id == priorSelected);
        string priorName = priorMember?.Name ?? SafeResolveNameSync(priorSelected);
        int priorSlot = priorMember?.PartySlotIndex ?? -1;
        int recoverThreshold = Math.Min(100, _settings.HpThresholdPercent + _settings.RecoveryHysteresisPercent);
        _restoreTracker.RecordSwitch(priorSelected, priorName, priorSlot, target, recoverThreshold, now);
        if (_restoreTracker.IsActive)
        {
            _pendingRestoreTargetId = priorSelected;
        }
    }

    private string SafeResolveNameSync(ulong targetId)
    {
        var member = _members.FirstOrDefault(m => m.Id == targetId);
        if (member is not null && !string.IsNullOrWhiteSpace(member.Name))
            return member.Name;

        try
        {
            string? name = _data.GetCharacterNameFromId(targetId);
            if (!string.IsNullOrWhiteSpace(name)) return name;
        }
        catch { }

        return "";
    }

    private HotKeyEventArgs? SafeGetKeymapHotkey(string action)
    {
        try { return _data.Keymap?.GetHotkey(action); }
        catch { return null; }
    }

    private void DrainHotkeysLocked(double now, ref string? deferredClipboard)
    {
        if (_hotkeyCaptureActive)
        {
            while (_hotkeyQueue.TryDequeue(out _)) { }
            _hotkeys.ClearPollState();
            return;
        }

        while (_hotkeyQueue.TryDequeue(out var hk))
        {
            if (!hk.IsDown)
            {
                _hotkeys.ReleaseSdkKey(hk.VirtualKey);
                _hotkeys.UpdatePollKeyState(hk.VirtualKey, false);
                continue;
            }

            if (now - hk.ReceivedAt > 1.0)
                continue;

            _hotkeys.UpdatePollKeyState(hk.VirtualKey, true);
            if (_hotkeys.UpdateSdkKeyState(hk.VirtualKey, true))
                DispatchHotkeyLocked(hk.VirtualKey, hk.Ctrl, hk.Shift, hk.Alt, now, ref deferredClipboard);
        }
    }

    private void PollForegroundHotkeysLocked(double now, ref string? deferredClipboard)
    {
        if (!OperatingSystem.IsWindows())
            return;

        if (_hotkeyCaptureActive)
        {
            _hotkeys.ClearPollState();
            return;
        }

        if (!IsGameForeground() && !(IsWineRuntime && GetForegroundWindow() == IntPtr.Zero))
        {
            _hotkeys.ClearPollState();
            return;
        }

        if (IsKeyDown(VkLWin) || IsKeyDown(VkRWin))
            return;

        bool ctrl = IsKeyDown(VkControl);
        bool shift = IsKeyDown(VkShift);
        bool alt = IsKeyDown(VkMenu);

        Span<int> keys = stackalloc int[8];
        int count = 0;
        AddUniqueKey(keys, ref count, _settings.TogglePluginHotkey.VirtualKey);
        if (_settings.PluginEnabled)
        {
            AddUniqueKey(keys, ref count, _settings.ToggleBannerHotkey.VirtualKey);
            AddUniqueKey(keys, ref count, _settings.FocusTargetHotkey.VirtualKey);
            AddUniqueKey(keys, ref count, _settings.ToggleModeHotkey.VirtualKey);
            AddUniqueKey(keys, ref count, _settings.TargetCriticalHotkey.VirtualKey);
            AddUniqueKey(keys, ref count, _settings.QuickCastCriticalHotkey.VirtualKey);
            AddUniqueKey(keys, ref count, _settings.ResetSavedHotkey.VirtualKey);
            AddUniqueKey(keys, ref count, _settings.CopyScorecardHotkey.VirtualKey);
        }

        for (int i = 0; i < count; i++)
        {
            int vk = keys[i];
            bool down = IsKeyDown(vk);
            if (!down)
                _hotkeys.ReleaseSdkKey(vk);
            if (_hotkeys.UpdatePollKeyState(vk, down))
                DispatchHotkeyLocked(vk, ctrl, shift, alt, now, ref deferredClipboard);
        }
    }

    private static void AddUniqueKey(Span<int> keys, ref int count, int vk)
    {
        if (vk <= 0 || vk > 0xFE || HotkeyMatcher.IsModifierKey(vk)) return;
        for (int i = 0; i < count; i++)
            if (keys[i] == vk) return;
        keys[count++] = vk;
    }

    private void DispatchHotkeyLocked(int virtualKey, bool ctrl, bool shift, bool alt, double now, ref string? deferredClipboard)
    {
        if (_hotkeys.ShouldTrigger(_settings.TogglePluginHotkey, virtualKey, ctrl, shift, alt, now))
        {
            SetPluginEnabledLocked(!_settings.PluginEnabled, now);
            return;
        }

        if (!_settings.PluginEnabled)
            return;

        if (_hotkeys.ShouldTrigger(_settings.ToggleBannerHotkey, virtualKey, ctrl, shift, alt, now))
        {
            ToggleBannerHudLocked(now);
            return;
        }
        if (_hotkeys.ShouldTrigger(_settings.FocusTargetHotkey, virtualKey, ctrl, shift, alt, now))
        {
            FocusCurrentTargetLocked(now);
            return;
        }
        if (_hotkeys.ShouldTrigger(_settings.ToggleModeHotkey, virtualKey, ctrl, shift, alt, now))
        {
            CycleModeLocked(now);
            return;
        }
        if (_hotkeys.ShouldTrigger(_settings.TargetCriticalHotkey, virtualKey, ctrl, shift, alt, now))
        {
            TargetCriticalLocked(now, bypassCooldown: true);
            return;
        }
        if (_hotkeys.ShouldTrigger(_settings.QuickCastCriticalHotkey, virtualKey, ctrl, shift, alt, now))
        {
            WatchedMemberSnapshot? target = ResolveHighestPriorityMemberLocked(now);
            if (target is not null)
                QuickCastOnMemberLocked(target, now, out _);
            return;
        }
        if (_hotkeys.ShouldTrigger(_settings.ResetSavedHotkey, virtualKey, ctrl, shift, alt, now))
        {
            ResetPeopleSavedLocked(now);
            return;
        }
        if (_hotkeys.ShouldTrigger(_settings.CopyScorecardHotkey, virtualKey, ctrl, shift, alt, now))
        {
            deferredClipboard = HealerStats.FormatChatScorecard(_settings);
            _state.SetNotice("SCORECARD COPIED", "Paste into DDO chat (/p or /raid)", 2.0, now);
            EvaluateLocked(now);
        }
    }

    private void SetPluginEnabledLocked(bool enabled, double now)
    {
        var updated = _settings.Copy();
        updated.PluginEnabled = enabled;
        CommitSettingsLocked(updated);
        if (!updated.PluginEnabled)
        {
            ResetTransientOnDisableLocked();
        }
        else
        {
            _state.SetNotice("HEALERS HELPER: ON", "Alerts and tracking resumed", 1.8, now);
        }
        EvaluateLocked(now);
    }

    private void ToggleBannerHudLocked(double now)
    {
        var updated = _settings.Copy();
        updated.HudEnabled = !updated.HudEnabled;
        CommitSettingsLocked(updated);
        if (updated.HudEnabled && updated.PluginEnabled)
            _state.SetNotice("CENTER BANNER: ON", "Flashing center alerts enabled", 1.8, now);
        EvaluateLocked(now);
    }

    private void ResetTransientOnDisableLocked()
    {
        _state.Reset();
        _savedTracker.Reset();
        _streakTracker.Reset();
        _reflexTracker.Reset();
        _restoreTracker.Reset();
        _pendingRestoreTargetId = 0;
        _nextAutoAllowedAt = 0;
        ClearSelfCast();
        _prevSelfSp = null;
        _lastAutoTargetGen = 0;
        _lastAutoTargetPlayerId = 0;
        _lastSoundGen = 0;
    }

    private bool FocusCurrentTargetLocked(double now)
    {
        ulong selectedId = _data.GetSelectedObjectId().GetValueOrDefault();
        if (selectedId == 0 || selectedId == ulong.MaxValue)
        {
            _state.SetNotice("NO TARGET SELECTED", "Select a player in DDO first", 2.0, now);
            EvaluateLocked(now);
            return false;
        }

        if (!EntityExtensions.IsPlayer(selectedId))
        {
            _state.SetNotice("NOT A PLAYER", "Select a player character in DDO first", 2.0, now);
            EvaluateLocked(now);
            return false;
        }

        string name = SafeResolveNameSync(selectedId);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"Player 0x{selectedId:X}";
        }

        var updated = _settings.Copy();
        updated.FocusedPlayerId = selectedId;
        updated.FocusedPlayerName = name;
        updated.Mode = TrackingMode.SinglePlayer;
        CommitSettingsLocked(updated);

        _state.SetNotice($"WATCHING: {name}", "Single-player focus active", 2.0, now);
        EvaluateLocked(now);
        return true;
    }

    private void CycleModeLocked(double now)
    {
        var next = _settings.Mode == TrackingMode.Party
            ? TrackingMode.SinglePlayer
            : TrackingMode.Party;

        var updated = _settings.Copy();
        updated.Mode = next;
        CommitSettingsLocked(updated);

        string primary = next == TrackingMode.Party
            ? "MODE: PARTY"
            : string.IsNullOrWhiteSpace(updated.FocusedPlayerName)
                ? "MODE: SINGLE PLAYER"
                : $"MODE: SINGLE ({updated.FocusedPlayerName})";
        string secondary = next == TrackingMode.Party
            ? "Watching self and party/raid"
            : string.IsNullOrWhiteSpace(updated.FocusedPlayerName)
                ? "Select a player and press Focus"
                : "Single-player focus active";

        _state.SetNotice(primary, secondary, 2.0, now);
        EvaluateLocked(now);
    }

    private WatchedMemberSnapshot? ResolveHighestPriorityMemberLocked(double now)
    {
        WatchedMemberSnapshot[] current = _members;
        AlertBannerView view = _state.Evaluate(current, _settings, now, targetingStatus: _lastTargetStatus, inTown: _inTown, isMuted: IsMemberMutedLocked);
        var watched = HealerState.FilterWatched(current, _settings, IsMemberMutedLocked);

        WatchedMemberSnapshot? target = null;
        if (view.AlertPlayerId is { } alertId)
            target = watched.FirstOrDefault(m => m.Id == alertId);

        target ??= watched
            .Where(m => m.Connected && m.Id != 0)
            .OrderBy(m => m.IsIncapacitated ? 0 : m.IsTrulyDead ? 2 : 1)
            .ThenBy(m => m.EffectivePercent(_settings.IncludeTempHpInThreshold))
            .ThenBy(m => m.PartySlotIndex)
            .FirstOrDefault();

        return target;
    }

    private bool TargetCriticalLocked(double now, bool bypassCooldown)
    {
        WatchedMemberSnapshot? target = ResolveHighestPriorityMemberLocked(now);
        if (target is null)
        {
            _state.SetNotice("NO WATCHED TARGET", "No connected party or focused player", 2.0, now);
            EvaluateLocked(now);
            return false;
        }

        bool selected = TrySelectTargetLocked(
            target.Id,
            target.Name,
            target.PartySlotIndex,
            now,
            bypassCooldown,
            recordRestore: _settings.RestorePreviousTargetAfterHeal);
        if (selected)
            _lastAutoTargetPlayerId = target.Id;

        if (bypassCooldown)
            _state.SetNotice(selected ? $"TARGETED: {target.Name}" : "TARGET FAILED", _lastTargetStatus, 1.5, now);

        EvaluateLocked(now);
        return selected;
    }

    private void CheckReflexSelfCastLocked()
    {
        if (!_settings.PluginEnabled || _settings.Mode == TrackingMode.Off)
            return;

        double lastCast = ReadLastSelfCast();
        if (!double.IsFinite(lastCast))
            return;

        var copy = _settings.Copy();
        if (_reflexTracker.TryRecordSelfCast(lastCast, copy).HasValue)
            CommitSettingsLocked(copy);
    }

    private bool IsPlayerMutedLocked(ulong id, string? name)
    {
        if (id != 0 && _mutedIds.Contains(id)) return true;
        if (!string.IsNullOrWhiteSpace(name) && _mutedNames.Contains(name.Trim())) return true;
        return false;
    }

    private bool IsMemberMutedLocked(WatchedMemberSnapshot member) =>
        member is not null && IsPlayerMutedLocked(member.Id, member.Name);

    private void RefreshGameWindowAndPidLocked()
    {
        IntPtr latestWindow = SafeGetGameWindowHandle(_data);
        if (latestWindow != IntPtr.Zero && latestWindow != Volatile.Read(ref _gameWindow))
        {
            Volatile.Write(ref _gameWindow, latestWindow);
            int resolved = ResolveGamePid(_data, latestWindow);
            if (resolved != 0) _gamePid = resolved;
        }
        else if (_gamePid == 0)
        {
            _gamePid = ResolveGamePid(_data, Volatile.Read(ref _gameWindow));
        }
    }

    private bool IsGameForeground()
    {
        if (!OperatingSystem.IsWindows())
            return true;

        IntPtr gw = Volatile.Read(ref _gameWindow);
        int gpid = _gamePid;
        if (gw == IntPtr.Zero && gpid == 0) return false;
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        if (gw != IntPtr.Zero && fg == gw) return true;
        _ = GetWindowThreadProcessId(fg, out uint pid);
        return pid != 0 && gpid != 0 && pid == (uint)gpid;
    }

    private bool IsGameForegroundOrActive()
    {
        if (!OperatingSystem.IsWindows())
            return true;
        if (Volatile.Read(ref _gameWindow) == IntPtr.Zero && _gamePid == 0)
            return true;
        if (IsGameForeground())
            return true;
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return IsWineRuntime;
        _ = GetWindowThreadProcessId(fg, out uint pid);
        return pid == (uint)Environment.ProcessId;
    }

    private static bool DetectWineRuntime()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        try
        {
            IntPtr ntdll = GetModuleHandle("ntdll.dll");
            return ntdll != IntPtr.Zero && GetProcAddress(ntdll, "wine_get_version") != IntPtr.Zero;
        }
        catch
        {
            return false;
        }
    }

    private static IntPtr SafeGetGameWindowHandle(IDdoGameDataProvider data)
    {
        try { return data.GetGameWindowHandle(); }
        catch { return IntPtr.Zero; }
    }

    private static int ResolveGamePid(IDdoGameDataProvider data, IntPtr gameWindow)
    {
        try
        {
            if (data.GameProcess is { HasExited: false } proc)
                return proc.Id;
        }
        catch { }

        if (OperatingSystem.IsWindows() && gameWindow != IntPtr.Zero)
        {
            _ = GetWindowThreadProcessId(gameWindow, out uint pid);
            if (pid != 0 && pid <= int.MaxValue) return (int)pid;
        }
        return 0;
    }

    private static void PlayAlertBeep()
    {
        if (!OperatingSystem.IsWindows()) return;
        try { _ = MessageBeep(MbIconExclamation); }
        catch { }
    }

    private static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private void Log(string message)
    {
        try
        {
            string path = Path.Combine(Folder, "healers-helper.log");
            if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024)
                File.Move(path, path + ".previous", true);
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch { }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _pluginEnabledFlag = false;
            _hotkeyCaptureActive = false;
            Interlocked.Exchange(ref _pendingUiClipboardText, null);
            _timer.Dispose();
            try
            {
                _events.OnChatCommand?.RemoveHandler("hh");
                _events.OnChatCommand?.RemoveHandler("healershelper");
            }
            catch { }

            while (_hotkeyQueue.TryDequeue(out _)) { }
            _state.Reset();
            _savedTracker.Reset();
            _streakTracker.Reset();
            _reflexTracker.Reset();
            _restoreTracker.Reset();
            _hotkeys.Reset();
            _partyConnected.Clear();
            _mutedIds.Clear();
            _mutedNames.Clear();
            _pendingRestoreTargetId = 0;
            _lastAutoTargetPlayerId = 0;
            Volatile.Write(ref _members, Array.Empty<WatchedMemberSnapshot>());
            Volatile.Write(ref _view, new AlertBannerView(
                BannerSeverity.None,
                _settings.Mode,
                "",
                "",
                0,
                null,
                "",
                null,
                _view.AlertGeneration,
                "Stopped",
                "",
                _settings.PluginEnabled,
                _settings.PeopleSaved));
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MessageBeep(uint uType);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);
}
