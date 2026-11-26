using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using VoK.Sdk;
using VoK.Sdk.Ddo;
using VoK.Sdk.Ddo.Enums;
using VoK.Sdk.Events;
using VoK.Sdk.Helpers;
using VoK.Sdk.Properties;

namespace VoK.HealersHelper;

internal sealed class HealerEngine : IHealerEngineView, IDisposable
{
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;

    private readonly record struct HotkeyEvent(int VirtualKey, bool Ctrl, bool Shift, bool Alt, bool IsDown, double ReceivedAt);

    private readonly IDdoGameDataProvider _data;
    private readonly SettingsStore _store;
    private readonly HealerState _state = new();
    private readonly PeopleSavedTracker _savedTracker = new();
    private readonly HotkeyMatcher _hotkeys = new();
    private readonly TargetSelector _selector;
    private readonly ConcurrentQueue<HotkeyEvent> _hotkeyQueue = new();
    private readonly object _gate = new();
    private readonly System.Threading.Timer _timer;

    private StreakTracker? _streakTracker = new();
    private ReflexTracker? _reflexTracker = new();
    private TargetRestoreTracker? _restoreTracker = new();
    private HashSet<ulong>? _mutedPlayerIds = new();
    private HashSet<string>? _mutedPlayerNames = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<ulong>? _confirmedDeadIds = new();
    private Dictionary<ulong, int>? _cachedIncapBonus = new();
    private StreakTracker Streak => _streakTracker ??= new StreakTracker();
    private ReflexTracker Reflex => _reflexTracker ??= new ReflexTracker();
    private TargetRestoreTracker Restore => _restoreTracker ??= new TargetRestoreTracker();
    private HashSet<ulong> MutedIds => _mutedPlayerIds ??= new HashSet<ulong>();
    private HashSet<string> MutedNames => _mutedPlayerNames ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private HashSet<ulong> ConfirmedDeadIds => _confirmedDeadIds ??= new HashSet<ulong>();
    private Dictionary<ulong, int> CachedIncapBonus => _cachedIncapBonus ??= new Dictionary<ulong, int>();

    private IntPtr _gameWindow;
    private int _gamePid;
    private Settings _settings;
    private WatchedMemberSnapshot[] _members = Array.Empty<WatchedMemberSnapshot>();
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
        "Starting DDO Healer's Helper…",
        "Targeting ready",
        true,
        0);

    private volatile bool _disposed;
    private volatile bool _pluginEnabledFlag = true;
    private bool _inTown;
    private int _settingsRevision;
    private ulong? _character;
    private long _characterIdBits;
    private long _lastSelfCastAtBits = BitConverter.DoubleToInt64Bits(double.NegativeInfinity);
    private int? _prevSelfSp;
    private long _lastSoundGen;
    private long _lastAutoTargetGen;
    private ulong _lastAutoTargetPlayerId;
    private double _lastErrorLog = -100;

    internal static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    internal string Folder => _store.DirectoryPath;
    public IntPtr GameWindow => Volatile.Read(ref _gameWindow);
    public int SettingsRevision => Volatile.Read(ref _settingsRevision);
    public int PeopleSaved => Volatile.Read(ref _view).PeopleSaved;

    public HealerEngine(IDdoGameDataProvider data, string folder)
    {
        _data = data;
        IntPtr initialWindow = SafeGetGameWindowHandle(data);
        Volatile.Write(ref _gameWindow, initialWindow);
        _gamePid = ResolveGamePid(data, initialWindow);
        _store = new SettingsStore(folder);
        _selector = new TargetSelector(data);

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

        // SDK 4.2.1 registrars do not expose unsubscription. Each handler becomes
        // a no-op on disposal; Dungeon Helper owns and destroys the per-client provider.
        var ev = data.EventProvider;
        ev.KeyDown.AddHandler(OnSdkKeyDown);
        ev.KeyUp.AddHandler(OnSdkKeyUp);
        ev.OnLogin.AddHandler(_ => OnSessionReset());
        ev.OnLogout.AddHandler(() => OnSessionReset());
        ev.OnHotbarUsage.AddHandler(_ => OnHotbarUsed());
        ev.OnRunScript.AddHandler(OnRunScript);

        Log($"Started v{typeof(HealersHelperPlugin).Assembly.GetName().Version}; SDK 4.2.1.0; HotKeysEnabled={data.HotKeysEnabled}.");
        _timer = new System.Threading.Timer(_ => Tick(), null, 0, 120);
    }

    private Task OnSdkKeyDown(HotKeyEventArgs args)
    {
        if (_disposed || args is null || args.Key <= 0 || args.KeyUp || args.Win || HotkeyMatcher.IsModifierKey(args.Key))
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
        if (_disposed || !_pluginEnabledFlag || args is null || args.ScriptSourceIid == 0) return Task.CompletedTask;
        ulong activeChar = unchecked((ulong)Volatile.Read(ref _characterIdBits));
        if (activeChar != 0 && args.ScriptSourceIid == activeChar)
            RecordSelfCast(Now);
        return Task.CompletedTask;
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
                Streak.Reset();
                Reflex.Reset();
                Restore.Reset();
                _hotkeys.Reset();
                _selector?.ResetCooldown();
                MutedIds.Clear();
                MutedNames.Clear();
                ConfirmedDeadIds.Clear();
                CachedIncapBonus.Clear();
                _character = null;
                _inTown = false;
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
            // Guard against stale SettingsDialog overwriting live gamification stats.
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
                    Reflex.Reset();
                }

                if (!copy.RestorePreviousTargetAfterHeal || pausedMode || copy.TargetingMethod == TargetMethod.Off)
                {
                    Restore.Reset();
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

    public AlertBannerView View() => Volatile.Read(ref _view);

    public WatchedMemberSnapshot[] GetMembers() => Volatile.Read(ref _members);

    public void Report(string message)
    {
        lock (_gate) Log(message);
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
            bool currentlyMuted = IsPlayerMutedLocked(id, cleanName);
            if (currentlyMuted)
            {
                if (id != 0) MutedIds.Remove(id);
                if (!string.IsNullOrEmpty(cleanName)) MutedNames.Remove(cleanName);
                if (_settings.PluginEnabled)
                    _state.SetNotice($"UNMUTED: {(string.IsNullOrEmpty(cleanName) ? $"ID {id}" : cleanName)}", "Alerts resumed for this player", 1.8, Now);
                EvaluateLocked(Now);
                return false;
            }

            if (id != 0) MutedIds.Add(id);
            if (!string.IsNullOrEmpty(cleanName)) MutedNames.Add(cleanName);
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
            MutedIds.Clear();
            MutedNames.Clear();
            if (_settings.PluginEnabled)
                _state.SetNotice("MUTES CLEARED", "All party members unmuted for this session", 1.8, Now);
            EvaluateLocked(Now);
        }
    }

    public IReadOnlyCollection<string> GetMutedPlayerNames()
    {
        lock (_gate)
        {
            if (MutedNames.Count == 0 && MutedIds.Count == 0)
                return Array.Empty<string>();
            var list = new List<string>(MutedNames);
            foreach (ulong id in MutedIds)
            {
                var found = _members.FirstOrDefault(m => m.Id == id);
                if (found is not null && !string.IsNullOrWhiteSpace(found.Name) && !MutedNames.Contains(found.Name))
                    list.Add(found.Name);
            }
            return list;
        }
    }

    public string GetRaidKeymapSummary() =>
        _selector?.GetRaidKeymapSummary() ?? TargetPolicy.EvaluateRaidKeymapReadiness(null).Summary;

    private bool IsPlayerMutedLocked(ulong id, string? name)
    {
        if (id != 0 && MutedIds.Contains(id)) return true;
        if (!string.IsNullOrWhiteSpace(name) && MutedNames.Contains(name.Trim())) return true;
        return false;
    }

    private bool IsMemberMutedLocked(WatchedMemberSnapshot member) =>
        member is not null && IsPlayerMutedLocked(member.Id, member.Name);

    public void TogglePluginEnabled()
    {
        lock (_gate)
        {
            if (_disposed) return;
            TogglePluginEnabledLocked(Now);
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
        lock (_gate)
        {
            return HealerStats.FormatChatScorecard(_settings);
        }
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

        return TryCopyTextToClipboardSta(card);
    }

    internal static bool TryCopyTextToClipboardSta(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            try
            {
                Clipboard.SetDataObject(text, true, 3, 15);
                return true;
            }
            catch
            {
                return false;
            }
        }

        bool copied = false;
        var sta = new Thread(() =>
        {
            try
            {
                Clipboard.SetDataObject(text, true, 3, 15);
                copied = true;
            }
            catch { }
        })
        {
            IsBackground = true,
            Name = "HealersHelper Clipboard STA"
        };
        sta.SetApartmentState(ApartmentState.STA);
        sta.Start();
        sta.Join(250);
        return copied;
    }

    public bool FocusCurrentTarget()
    {
        lock (_gate)
        {
            if (_disposed || !_settings.PluginEnabled) return false;
            return FocusCurrentTargetLocked(Now);
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

    private void Tick()
    {
        if (_disposed || !Monitor.TryEnter(_gate)) return;
        try
        {
            if (_disposed) return;
            double now = Now;
            RefreshGameWindowAndPidLocked();

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
                    DispatchHotkeyLocked(hk.VirtualKey, hk.Ctrl, hk.Shift, hk.Alt, now);
            }

            PollForegroundHotkeysLocked(now);
            CheckReflexSelfCastLocked();
            ScanMembersLocked(now);
            CheckReflexSelfCastLocked();
            EvaluateLocked(now);
        }
        catch (Exception e)
        {
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
                _selector?.LastStatus ?? "Targeting ready",
                _settings.PluginEnabled,
                _settings.PeopleSaved));
            if (Now - _lastErrorLog > 15)
            {
                Log("SDK scan failed: " + e.Message);
                _lastErrorLog = Now;
            }
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    private void CheckReflexSelfCastLocked()
    {
        if (!_settings.PluginEnabled || _settings.Mode == TrackingMode.Off)
            return;

        double lastCast = ReadLastSelfCast();
        if (!double.IsFinite(lastCast))
            return;

        var copy = _settings.Copy();
        if (Reflex.TryRecordSelfCast(lastCast, copy).HasValue)
        {
            CommitSettingsLocked(copy);
        }
    }

    private void RefreshGameWindowAndPidLocked()
    {
        if (_data is null) return;
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

    private void PollForegroundHotkeysLocked(double now)
    {
        if (!IsGameForeground() && !(HudHostHelper.IsWineRuntime && GetForegroundWindow() == IntPtr.Zero))
        {
            _hotkeys.ClearPollState();
            return;
        }

        if (IsKeyDown(VkLWin) || IsKeyDown(VkRWin))
            return;

        bool ctrl = IsKeyDown(VkControl);
        bool shift = IsKeyDown(VkShift);
        bool alt = IsKeyDown(VkMenu);

        Span<int> keys = stackalloc int[7];
        int count = 0;
        AddUniqueKey(keys, ref count, _settings.TogglePluginHotkey.VirtualKey);
        if (_settings.PluginEnabled)
        {
            AddUniqueKey(keys, ref count, _settings.ToggleBannerHotkey.VirtualKey);
            AddUniqueKey(keys, ref count, _settings.FocusTargetHotkey.VirtualKey);
            AddUniqueKey(keys, ref count, _settings.ToggleModeHotkey.VirtualKey);
            AddUniqueKey(keys, ref count, _settings.TargetCriticalHotkey.VirtualKey);
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
                DispatchHotkeyLocked(vk, ctrl, shift, alt, now);
        }
    }

    private static void AddUniqueKey(Span<int> keys, ref int count, int vk)
    {
        if (vk <= 0 || vk > 0xFE || HotkeyMatcher.IsModifierKey(vk)) return;
        for (int i = 0; i < count; i++)
            if (keys[i] == vk) return;
        keys[count++] = vk;
    }

    private void DispatchHotkeyLocked(int virtualKey, bool ctrl, bool shift, bool alt, double now)
    {
        // Master ON/OFF hotkey remains active even while disabled so the user can re-enable in DDO.
        if (_hotkeys.ShouldTrigger(_settings.TogglePluginHotkey, virtualKey, ctrl, shift, alt, now))
        {
            TogglePluginEnabledLocked(now);
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
        if (_hotkeys.ShouldTrigger(_settings.ResetSavedHotkey, virtualKey, ctrl, shift, alt, now))
        {
            ResetPeopleSavedLocked(now);
            return;
        }
        if (_hotkeys.ShouldTrigger(_settings.CopyScorecardHotkey, virtualKey, ctrl, shift, alt, now))
        {
            string card = HealerStats.FormatChatScorecard(_settings);
            _state.SetNotice("SCORECARD COPIED", "Paste into DDO chat (/p or /raid)", 2.0, now);
            EvaluateLocked(now);
            _ = TryCopyTextToClipboardSta(card);
        }
    }

    private void TogglePluginEnabledLocked(double now)
    {
        var updated = _settings.Copy();
        updated.PluginEnabled = !updated.PluginEnabled;
        CommitSettingsLocked(updated);
        if (!updated.PluginEnabled)
        {
            ResetTransientOnDisableLocked();
        }
        else
        {
            _state.SetNotice("DDO HEALER'S HELPER: ON", "Alerts and tracking resumed", 1.8, now);
        }

        EvaluateLocked(now);
    }

    private void ToggleBannerHudLocked(double now)
    {
        var updated = _settings.Copy();
        updated.HudEnabled = !updated.HudEnabled;
        CommitSettingsLocked(updated);
        if (updated.HudEnabled && updated.PluginEnabled)
        {
            _state.SetNotice("CENTER BANNER: ON", "Flashing center alerts enabled", 1.8, now);
        }

        EvaluateLocked(now);
    }

    private void ResetTransientOnDisableLocked()
    {
        _state.Reset();
        _savedTracker.Reset();
        Streak.Reset();
        Reflex.Reset();
        Restore.Reset();
        _selector?.ResetCooldown();
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

        string name = ResolveTargetName(selectedId);
        if (string.IsNullOrWhiteSpace(name))
        {
            _state.SetNotice("TARGET UNAVAILABLE", "Selected player could not be resolved", 2.0, now);
            EvaluateLocked(now);
            return false;
        }

        if (_settings.Mode != TrackingMode.SinglePlayer
            || _settings.FocusedPlayerId != selectedId
            || !string.Equals(_settings.FocusedPlayerName, name, StringComparison.Ordinal))
        {
            var updated = _settings.Copy();
            updated.FocusedPlayerId = selectedId;
            updated.FocusedPlayerName = name;
            updated.Mode = TrackingMode.SinglePlayer;
            CommitSettingsLocked(updated);
        }

        _state.SetNotice($"WATCHING: {name}", "Single-player focus active", 2.0, now);
        ScanMembersLocked(now);
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

    private bool TargetCriticalLocked(double now, bool bypassCooldown)
    {
        WatchedMemberSnapshot[] current = _members;
        AlertBannerView view = _state.Evaluate(current, _settings, now, targetingStatus: _selector.LastStatus, inTown: _inTown, isMuted: IsMemberMutedLocked);
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

        if (target is null)
        {
            _state.SetNotice("NO WATCHED TARGET", "No connected party or focused player", 2.0, now);
            EvaluateLocked(now);
            return false;
        }

        ulong priorSelected = _data?.GetSelectedObjectId().GetValueOrDefault() ?? 0;
        bool selected = _selector.TrySelect(target.Id, target.Name, target.PartySlotIndex, _settings, now, bypassCooldown);
        if (selected)
        {
            _lastAutoTargetPlayerId = target.Id;
            if (_settings.RestorePreviousTargetAfterHeal)
            {
                RecordRestoreSwitchLocked(priorSelected, current, target, now);
            }
        }
        if (bypassCooldown)
            _state.SetNotice(selected ? $"TARGETED: {target.Name}" : "TARGET FAILED", _selector.LastStatus, 1.5, now);

        EvaluateLocked(now);
        return selected;
    }

    private void ScanMembersLocked(double now)
    {
        ulong? charId = _data.GetCurrentCharacterId();
        if (charId != _character)
        {
            _state.Reset();
            _savedTracker.Reset();
            Streak.Reset();
            Reflex.Reset();
            Restore.Reset();
            _selector?.ResetCooldown();
            MutedIds.Clear();
            MutedNames.Clear();
            ConfirmedDeadIds.Clear();
            CachedIncapBonus.Clear();
            _character = charId;
            _inTown = false;
            Volatile.Write(ref _characterIdBits, unchecked((long)(charId ?? 0)));
            ClearSelfCast();
            _prevSelfSp = null;
            _lastAutoTargetGen = 0;
            _lastAutoTargetPlayerId = 0;
            _lastSoundGen = 0;
        }

        if (!charId.HasValue || charId.Value == 0 || charId.Value == ulong.MaxValue)
        {
            Volatile.Write(ref _members, Array.Empty<WatchedMemberSnapshot>());
            _savedTracker.Reset();
            Streak.Reset();
            Reflex.Reset();
            Restore.Reset();
            ConfirmedDeadIds.Clear();
            CachedIncapBonus.Clear();
            _lastAutoTargetPlayerId = 0;
            _inTown = false;
            return;
        }

        bool inTown = false;
        try { inTown = _data.InTown() == true; }
        catch { }
        _inTown = inTown;

        ulong selfId = charId.Value;
        var list = new List<WatchedMemberSnapshot>(12);
        var seen = new HashSet<ulong>();

        IEntity? selfEntity = _data.GetCurrentCharacter() ?? _data.GetEntity(selfId);
        string selfName = !string.IsNullOrWhiteSpace(selfEntity?.Name)
            ? selfEntity!.Name
            : (_data.GetCurrentCharacterName() ?? "Self");
        if (string.IsNullOrWhiteSpace(selfName)) selfName = "Self";

        if (selfEntity is not null && !selfEntity.IsDeleted)
        {
            var (lifeStateRaw, hasSoulstoneProp, liveIncapBonus) = ReadEntityLifeHints(selfEntity);
            bool explicitDown = lifeStateRaw is LifeStatusEvaluator.LifeStateDead or LifeStatusEvaluator.LifeStateIncapacitated
                || hasSoulstoneProp;
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
            var (isDown, isTrulyDead) = EvaluateAndTrackMemberLifeStatusLocked(
                selfId,
                connected: true,
                hp,
                maxHp,
                hasLiveEntity: true,
                lifeStateRaw,
                hasSoulstoneProp,
                liveIncapBonus);
            list.Add(new WatchedMemberSnapshot(selfId, selfName, hp, maxHp, 0, true, isDown, isTrulyDead, tempHp, sp, maxSp));

            int? currSp = selfEntity.SpellPoints_Current;
            if (_settings.PluginEnabled && _prevSelfSp.HasValue && currSp.HasValue && currSp.Value < _prevSelfSp.Value)
                RecordSelfCast(now);
            _prevSelfSp = currSp;
        }
        else
        {
            list.Add(new WatchedMemberSnapshot(selfId, selfName, 0, 0, 0, false, false, false));
            _prevSelfSp = null;
        }
        seen.Add(selfId);

        IEnumerable<IPartyMember>? party = null;
        try { party = _data.GetPartyMembers(); }
        catch { }

        if (party is not null)
        {
            int slot = 0;
            foreach (var m in party)
            {
                if (m is null || m.Id == 0 || m.Id == ulong.MaxValue || m.Id == selfId || !seen.Add(m.Id)) continue;
                slot++;

                IEntity? ent = null;
                try { ent = _data.GetEntity(m.Id); }
                catch { }

                bool hasLiveEntity = ent is not null && !ent.IsDeleted;
                int hp = (hasLiveEntity ? ent!.HitPoints_Current : null) ?? m.HitPoints;
                int maxHp = (hasLiveEntity ? ent!.HitPoints_Max : null) ?? m.MaxHitPoints;
                int tempHp = Math.Max(0, (hasLiveEntity ? ent!.HitPoints_Temp : null) ?? 0);
                int sp = Math.Max(0, (hasLiveEntity ? ent!.SpellPoints_Current : null) ?? m.SpellPoints);
                int maxSp = Math.Max(0, (hasLiveEntity ? ent!.SpellPoints_Max : null) ?? m.MaxSpellPoints);

                string name = !string.IsNullOrWhiteSpace(m.Name)
                    ? m.Name
                    : (hasLiveEntity && !string.IsNullOrWhiteSpace(ent!.Name) ? ent.Name : $"Member {slot}");

                int? lifeStateRaw = null;
                bool hasSoulstoneProp = false;
                int? liveIncapBonus = null;
                if (hasLiveEntity)
                {
                    (lifeStateRaw, hasSoulstoneProp, liveIncapBonus) = ReadEntityLifeHints(ent!);
                }

                var (isDown, isTrulyDead) = EvaluateAndTrackMemberLifeStatusLocked(
                    m.Id,
                    m.Connected,
                    hp,
                    maxHp,
                    hasLiveEntity,
                    lifeStateRaw,
                    hasSoulstoneProp,
                    liveIncapBonus);

                list.Add(new WatchedMemberSnapshot(m.Id, name, hp, maxHp, slot, m.Connected, isDown, isTrulyDead, tempHp, sp, maxSp));
            }
        }

        if (_settings.Mode == TrackingMode.SinglePlayer)
            TryAddExternalFocusedPlayer(list, seen);

        PruneLifeCachesLocked(seen);

        var snapshot = list.ToArray();
        Volatile.Write(ref _members, snapshot);

        if (!_settings.PluginEnabled || _settings.Mode == TrackingMode.Off)
        {
            _savedTracker.Reset();
            return;
        }

        var watchedList = HealerState.FilterWatched(snapshot, _settings, IsMemberMutedLocked);
        var watchedArray = watchedList.ToArray();

        var updated = _settings.Copy();
        bool settingsChanged = Streak.ProcessWatched(snapshot, watchedList, _settings.PluginEnabled, inTown, updated);

        var saves = _savedTracker.ProcessScanDetailed(
            watchedArray,
            _settings.PluginEnabled,
            inTown,
            now,
            ReadLastSelfCast(),
            _settings.SaveWindowSeconds,
            _settings.MinHealDelta,
            _settings.SelfCastWindowSeconds);

        if (saves.Count > 0)
        {
            foreach (var save in saves)
            {
                HealerStats.ApplyClutchSave(updated, save);
            }
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

    private void TryAddExternalFocusedPlayer(List<WatchedMemberSnapshot> list, HashSet<ulong> seen)
    {
        bool alreadyPresent = list.Any(m =>
            (_settings.FocusedPlayerId > 0 && m.Id == _settings.FocusedPlayerId) ||
            (!string.IsNullOrWhiteSpace(_settings.FocusedPlayerName)
                && string.Equals(m.Name, _settings.FocusedPlayerName, StringComparison.OrdinalIgnoreCase)));
        if (alreadyPresent) return;

        IEntity? ent = null;
        if (_settings.FocusedPlayerId > 0 && EntityExtensions.IsPlayer(_settings.FocusedPlayerId))
        {
            try { ent = _data.GetEntity(_settings.FocusedPlayerId); }
            catch { }
        }

        if ((ent is null || ent.IsDeleted || !ent.IsPlayer()) && !string.IsNullOrWhiteSpace(_settings.FocusedPlayerName))
        {
            IEnumerable<IEntity>? entities = null;
            try { entities = _data.GetEntities(); }
            catch { }

            if (entities is not null)
            {
                foreach (var candidate in entities)
                {
                    if (candidate is null || candidate.IsDeleted || !candidate.IsPlayer()) continue;
                    if (string.Equals(candidate.Name, _settings.FocusedPlayerName, StringComparison.OrdinalIgnoreCase))
                    {
                        ent = candidate;
                        break;
                    }
                }
            }
        }

        if (ent is null || ent.IsDeleted || ent.InstanceId == 0 || !ent.IsPlayer() || !seen.Add(ent.InstanceId)) return;

        var (lifeStateRaw, hasSoulstoneProp, liveIncapBonus) = ReadEntityLifeHints(ent);
        bool explicitDown = lifeStateRaw is LifeStatusEvaluator.LifeStateDead or LifeStatusEvaluator.LifeStateIncapacitated
            || hasSoulstoneProp;
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
        var (isDown, isTrulyDead) = EvaluateAndTrackMemberLifeStatusLocked(
            ent.InstanceId,
            connected: true,
            hp,
            maxHp,
            hasLiveEntity: true,
            lifeStateRaw,
            hasSoulstoneProp,
            liveIncapBonus);
        string name = !string.IsNullOrWhiteSpace(ent.Name)
            ? ent.Name
            : (!string.IsNullOrWhiteSpace(_settings.FocusedPlayerName)
                ? _settings.FocusedPlayerName
                : $"Player 0x{ent.InstanceId:X}");
        list.Add(new WatchedMemberSnapshot(ent.InstanceId, name, hp, maxHp, -1, true, isDown, isTrulyDead, tempHp, sp, maxSp));
    }

    private (bool IsDown, bool IsTrulyDead) EvaluateAndTrackMemberLifeStatusLocked(
        ulong memberId,
        bool connected,
        int hp,
        int maxHp,
        bool hasLiveEntity,
        int? lifeStateRaw,
        bool hasSoulstoneProp,
        int? liveIncapBonus)
    {
        if (hasLiveEntity && liveIncapBonus is >= 0)
            CachedIncapBonus[memberId] = liveIncapBonus.Value;

        if (hasLiveEntity && (lifeStateRaw == LifeStatusEvaluator.LifeStateDead || (hasSoulstoneProp && (maxHp <= 0 || hp <= 0))))
        {
            ConfirmedDeadIds.Add(memberId);
        }
        else if (hp > 0 || (hasLiveEntity && lifeStateRaw is LifeStatusEvaluator.LifeStateAlive or LifeStatusEvaluator.LifeStateIncapacitated))
        {
            ConfirmedDeadIds.Remove(memberId);
        }

        bool wasConfirmedDead = ConfirmedDeadIds.Contains(memberId);
        int? cachedIncapBonus = CachedIncapBonus.TryGetValue(memberId, out int bonus) ? bonus : null;
        return LifeStatusEvaluator.EvaluateMemberLifeStatus(
            connected,
            hp,
            maxHp,
            hasLiveEntity,
            lifeStateRaw,
            hasSoulstoneProp,
            wasConfirmedDead,
            cachedIncapBonus,
            _settings.FallbackDeathHp);
    }

    private void PruneLifeCachesLocked(HashSet<ulong> activeIds)
    {
        if (ConfirmedDeadIds.Count > 0)
        {
            foreach (ulong id in ConfirmedDeadIds.Where(id => !activeIds.Contains(id)).ToArray())
                ConfirmedDeadIds.Remove(id);
        }

        if (CachedIncapBonus.Count > 0)
        {
            foreach (ulong id in CachedIncapBonus.Keys.Where(id => !activeIds.Contains(id)).ToArray())
                CachedIncapBonus.Remove(id);
        }
    }

    private static (int? LifeStateRaw, bool HasSoulstoneProp, int? IncapBonus) ReadEntityLifeHints(IEntity ent)
    {
        int? lifeStateRaw = null;
        try
        {
            if (ent.LifeState is { } ls)
                lifeStateRaw = (int)ls;
        }
        catch { }

        if (!lifeStateRaw.HasValue)
        {
            lifeStateRaw = TryReadLifeStateProperty(ent);
        }

        bool hasSoulstone = IsActiveSoulstoneProperty(TryGetEntityProperty(ent, LifeStatusEvaluator.DeathSoulStoneIdPropertyId));
        int? incapBonus = TryReadNonNegativeIntProperty(TryGetEntityProperty(ent, LifeStatusEvaluator.HitPointsIncapacitatedBonusPropertyId));
        return (lifeStateRaw, hasSoulstone, incapBonus);
    }

    private static IProperty? TryGetEntityProperty(IEntity ent, uint propertyId)
    {
        try
        {
            return ent.GetProperty(propertyId)
                ?? ent.InstanceProperties?.GetProperty(propertyId)
                ?? ent.DetailedProperties?.GetProperty(propertyId);
        }
        catch
        {
            return null;
        }
    }

    private static int? TryReadLifeStateProperty(IEntity ent)
    {
        IProperty? prop = TryGetEntityProperty(ent, LifeStatusEvaluator.DeathLifeStatePropertyId);
        if (prop is null) return null;
        try
        {
            if (prop is IUInt32Property u32 && u32.UInt32Value is { } uv)
                return unchecked((int)uv);
            if (prop is IInt32Property i32 && i32.Int32Value is { } iv)
                return iv;
            return prop.Value switch
            {
                LifeState ls => (int)ls,
                uint v => unchecked((int)v),
                int v => v,
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool IsActiveSoulstoneProperty(IProperty? prop)
    {
        if (prop is null) return false;
        try
        {
            if (prop is IUInt64Property u64 && u64.UInt64Value is { } u64v)
                return u64v != 0 && u64v != ulong.MaxValue;
            if (prop is IInt64Property i64 && i64.Int64Value is { } i64v)
                return i64v != 0 && i64v != -1;
            if (prop is IUInt32Property u32 && u32.UInt32Value is { } u32v)
                return u32v != 0 && u32v != uint.MaxValue;
            if (prop is IInt32Property i32 && i32.Int32Value is { } i32v)
                return i32v != 0 && i32v != -1;

            return prop.Value switch
            {
                ulong v => v != 0 && v != ulong.MaxValue,
                long v => v != 0 && v != -1,
                uint v => v != 0 && v != uint.MaxValue,
                int v => v != 0 && v != -1,
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }

    private static int? TryReadNonNegativeIntProperty(IProperty? prop)
    {
        if (prop is null) return null;
        try
        {
            if (prop is IInt32Property i32 && i32.Int32Value is { } iv)
                return iv is >= 0 and <= 1_000_000 ? iv : null;
            if (prop is IFloatProperty fp && fp.FloatValue is { } fv && float.IsFinite(fv))
                return fv is >= 0f and <= 1_000_000f ? (int)Math.Round(fv) : null;
            if (prop is IDoubleProperty dp && dp.DoubleValue is { } dv && double.IsFinite(dv))
                return dv is >= 0.0 and <= 1_000_000.0 ? (int)Math.Round(dv) : null;
            if (prop is IInt64Property i64 && i64.Int64Value is { } lv)
                return lv is >= 0 and <= 1_000_000 ? (int)lv : null;
            if (prop is IUInt32Property u32 && u32.UInt32Value is { } uv)
                return uv <= 1_000_000 ? (int)uv : null;

            return prop.Value switch
            {
                int v => v is >= 0 and <= 1_000_000 ? v : null,
                float f => float.IsFinite(f) && f is >= 0f and <= 1_000_000f ? (int)Math.Round(f) : null,
                double d => double.IsFinite(d) && d is >= 0.0 and <= 1_000_000.0 ? (int)Math.Round(d) : null,
                long l => l is >= 0 and <= 1_000_000 ? (int)l : null,
                uint u => u <= 1_000_000 ? (int)u : null,
                _ => null
            };
        }
        catch
        {
            return null;
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

        string selectorStatus = _selector?.LastStatus ?? "Targeting ready";
        AlertBannerView view = _state.Evaluate(current, _settings, now, statusLine, selectorStatus, _inTown, IsMemberMutedLocked);

        if (_settings.PluginEnabled
            && (view.Severity is BannerSeverity.LowHp or BannerSeverity.CriticalDown || view.AlertPlayerId.HasValue))
        {
            if (view.Severity is BannerSeverity.LowHp or BannerSeverity.CriticalDown
                && view.AlertPlayerId is { } alertId)
            {
                if (_settings.SoundOnAlert && !view.IsSoulstone && view.AlertGeneration != _lastSoundGen)
                {
                    _lastSoundGen = view.AlertGeneration;
                    try { SystemSounds.Exclamation.Play(); }
                    catch { }
                }

                if (_selector is not null
                    && _settings.AutoTargetOnAlert
                    && !view.IsSoulstone
                    && _settings.TargetingMethod != TargetMethod.Off
                    && view.AlertGeneration != _lastAutoTargetGen
                    && IsGameOrOverlayForeground())
                {
                    var target = current.FirstOrDefault(m => m.Id == alertId);
                    if (target is not null)
                    {
                        int recoverAt = Math.Min(100, _settings.HpThresholdPercent + _settings.RecoveryHysteresisPercent);
                        bool prevHealedBypass = _lastAutoTargetPlayerId != 0
                            && _lastAutoTargetPlayerId != alertId
                            && TargetRestoreTracker.IsPreviousTargetHealedOrGone(current, _lastAutoTargetPlayerId, recoverAt);

                        ulong priorSelected = _data?.GetSelectedObjectId().GetValueOrDefault() ?? 0;
                        bool selected = _selector.TrySelect(
                            target.Id,
                            target.Name,
                            target.PartySlotIndex,
                            _settings,
                            now,
                            bypassCooldown: prevHealedBypass);
                        if (selected && _settings.RestorePreviousTargetAfterHeal)
                        {
                            RecordRestoreSwitchLocked(priorSelected, current, target, now);
                        }
                        ulong currentSelected = _data?.GetSelectedObjectId().GetValueOrDefault() ?? 0;
                        if (selected || currentSelected == target.Id)
                        {
                            _lastAutoTargetGen = view.AlertGeneration;
                            _lastAutoTargetPlayerId = target.Id;
                        }
                        view = view with { TargetingStatus = _selector.LastStatus };
                    }
                }
            }
        }
        else
        {
            ulong latestSelected = _data?.GetSelectedObjectId().GetValueOrDefault() ?? 0;
            if (Restore.EvaluateRestore(
                    current,
                    _settings,
                    latestSelected,
                    view.Severity,
                    view.AlertPlayerId,
                    _inTown,
                    IsGameOrOverlayForeground(),
                    now,
                    IsMemberMutedLocked) is { } candidate)
            {
                _selector?.TryRestore(candidate, _settings);
                view = view with { TargetingStatus = _selector?.LastStatus ?? view.TargetingStatus };
            }
        }

        Reflex.OnAlertEvaluated(view, now);
        Volatile.Write(ref _view, view);
    }

    private void RecordRestoreSwitchLocked(
        ulong priorSelected,
        WatchedMemberSnapshot[] current,
        WatchedMemberSnapshot target,
        double now)
    {
        string priorName = SafeResolveTargetName(priorSelected);
        var priorMember = priorSelected > 0 && priorSelected < ulong.MaxValue
            ? current.FirstOrDefault(m => m.Id == priorSelected)
            : null;
        if (string.IsNullOrWhiteSpace(priorName) && priorMember is not null)
        {
            priorName = priorMember.Name;
        }

        int priorSlot = priorMember?.PartySlotIndex ?? -1;
        int recoverThreshold = Math.Min(100, _settings.HpThresholdPercent + _settings.RecoveryHysteresisPercent);
        Restore.RecordSwitch(priorSelected, priorName, priorSlot, target, recoverThreshold, now);
    }

    private string SafeResolveTargetName(ulong targetId)
    {
        if (_data is null || targetId == 0 || targetId == ulong.MaxValue)
            return "";

        try
        {
            return ResolveTargetName(targetId);
        }
        catch
        {
            return "";
        }
    }

    private string ResolveTargetName(ulong targetId)
    {
        IEntity? ent = _data.GetEntity(targetId);
        if (ent is not null && !ent.IsDeleted && !string.IsNullOrWhiteSpace(ent.Name))
            return ent.Name;

        var party = _data.GetPartyMembers();
        if (party is not null)
        {
            foreach (var m in party)
            {
                if (m is not null && m.Id == targetId && !string.IsNullOrWhiteSpace(m.Name))
                    return m.Name;
            }
        }

        if (_character == targetId)
        {
            string? selfName = _data.GetCurrentCharacterName();
            if (!string.IsNullOrWhiteSpace(selfName)) return selfName;
        }

        string? byId = _data.GetCharacterNameFromId(targetId);
        return string.IsNullOrWhiteSpace(byId) ? "" : byId;
    }

    private bool IsGameForeground()
    {
        IntPtr gw = Volatile.Read(ref _gameWindow);
        int gpid = _gamePid;
        if (gw == IntPtr.Zero && gpid == 0) return false;
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        if (gw != IntPtr.Zero && fg == gw) return true;
        _ = GetWindowThreadProcessId(fg, out uint pid);
        return pid != 0 && gpid != 0 && pid == (uint)gpid;
    }

    private bool IsGameOrOverlayForeground()
    {
        if (IsGameForeground()) return true;
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return HudHostHelper.IsWineRuntime;
        _ = GetWindowThreadProcessId(fg, out uint pid);
        if (pid == 0) return false;
        if (pid == (uint)Environment.ProcessId) return true;
        if (pid > int.MaxValue) return false;
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            return proc.ProcessName.Replace(" ", "", StringComparison.Ordinal)
                .Equals("LosslessScaling", StringComparison.OrdinalIgnoreCase);
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

        if (gameWindow != IntPtr.Zero)
        {
            _ = GetWindowThreadProcessId(gameWindow, out uint pid);
            if (pid != 0 && pid <= int.MaxValue) return (int)pid;
        }
        return 0;
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
            _timer.Dispose();
            while (_hotkeyQueue.TryDequeue(out _)) { }
            _state.Reset();
            _savedTracker.Reset();
            Streak.Reset();
            Reflex.Reset();
            Restore.Reset();
            _hotkeys.Reset();
            ConfirmedDeadIds.Clear();
            CachedIncapBonus.Clear();
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

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
