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
using VoK.Sdk;
using VoK.Sdk.Ddo;
using VoK.Sdk.Ddo.Enums;
using VoK.Sdk.Events;
using VoK.Sdk.Helpers;

namespace VoK.HealersHelper;

internal sealed class HealerEngine : IHealerEngineView, IDisposable
{
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;

    private readonly record struct HotkeyEvent(int VirtualKey, bool Ctrl, bool Shift, bool Alt, double ReceivedAt);

    private readonly IDdoGameDataProvider _data;
    private readonly SettingsStore _store;
    private readonly HealerState _state = new();
    private readonly PeopleSavedTracker _savedTracker = new();
    private readonly HotkeyMatcher _hotkeys = new();
    private readonly TargetSelector _selector;
    private readonly ConcurrentQueue<HotkeyEvent> _hotkeyQueue = new();
    private readonly object _gate = new();
    private readonly System.Threading.Timer _timer;
    private readonly int _gamePid;

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
    private int _settingsRevision;
    private ulong? _character;
    private long _characterIdBits;
    private long _lastSelfCastAtBits = BitConverter.DoubleToInt64Bits(double.NegativeInfinity);
    private int? _prevSelfSp;
    private long _lastSoundGen;
    private long _lastAutoTargetGen;
    private double _lastErrorLog = -100;

    internal static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    internal string Folder => _store.DirectoryPath;
    public IntPtr GameWindow { get; }
    public int SettingsRevision => Volatile.Read(ref _settingsRevision);
    public int PeopleSaved => Volatile.Read(ref _view).PeopleSaved;

    public HealerEngine(IDdoGameDataProvider data, string folder)
    {
        _data = data;
        GameWindow = data.GetGameWindowHandle();
        _gamePid = ResolveGamePid(data, GameWindow);
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

        _view = _view with { PluginEnabled = _settings.PluginEnabled, PeopleSaved = _settings.PeopleSaved };

        // SDK 4.2.1 registrars do not expose unsubscription. Each handler becomes
        // a no-op on disposal; Dungeon Helper owns and destroys the per-client provider.
        var ev = data.EventProvider;
        ev.KeyDown.AddHandler(OnSdkKeyDown);
        ev.OnLogin.AddHandler(_ => OnSessionReset());
        ev.OnLogout.AddHandler(() => OnSessionReset());
        ev.OnHotbarUsage.AddHandler(_ => OnHotbarUsed());
        ev.OnRunScript.AddHandler(OnRunScript);

        _timer = new System.Threading.Timer(_ => Tick(), null, 0, 120);
        Log($"Started v{typeof(HealersHelperPlugin).Assembly.GetName().Version}; SDK 4.2.1.0; HotKeysEnabled={data.HotKeysEnabled}.");
    }

    private Task OnSdkKeyDown(HotKeyEventArgs args)
    {
        if (_disposed || args.Key <= 0 || args.KeyUp) return Task.CompletedTask;
        if (_hotkeyQueue.Count < 128)
            _hotkeyQueue.Enqueue(new HotkeyEvent(args.Key, args.Control, args.Shift, args.Alt, Now));
        return Task.CompletedTask;
    }

    private Task OnHotbarUsed()
    {
        if (!_disposed) RecordSelfCast(Now);
        return Task.CompletedTask;
    }

    private Task OnRunScript(ScriptEventArgs args)
    {
        if (_disposed || args is null || args.ScriptSourceIid == 0) return Task.CompletedTask;
        ulong activeChar = unchecked((ulong)Volatile.Read(ref _characterIdBits));
        if (activeChar != 0 && args.ScriptSourceIid == activeChar)
            RecordSelfCast(Now);
        return Task.CompletedTask;
    }

    private void RecordSelfCast(double now) =>
        Interlocked.Exchange(ref _lastSelfCastAtBits, BitConverter.DoubleToInt64Bits(now));

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
                _hotkeys.Reset();
                _selector.ResetCooldown();
                _character = null;
                Volatile.Write(ref _characterIdBits, 0L);
                _prevSelfSp = null;
                _lastAutoTargetGen = 0;
                _lastSoundGen = 0;
            }
        }
        return Task.CompletedTask;
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
            // Guard against stale SettingsDialog overwriting live PeopleSaved increments.
            copy.PeopleSaved = _settings.PeopleSaved;
            bool turnedOff = _settings.PluginEnabled && !copy.PluginEnabled;
            _store.Save(copy);
            _settings = copy;
            if (turnedOff) ResetTransientOnDisableLocked();
            Interlocked.Increment(ref _settingsRevision);
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
            _store.Save(copy);
            _settings = copy;
            Interlocked.Increment(ref _settingsRevision);
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
            _store.Save(copy);
            _settings = copy;
            Interlocked.Increment(ref _settingsRevision);
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
            TogglePluginEnabledLocked(Now);
        }
    }

    public void ResetPeopleSaved()
    {
        lock (_gate)
        {
            if (_disposed) return;
            var copy = _settings.Copy();
            copy.PeopleSaved = 0;
            _store.Save(copy);
            _settings = copy;
            Interlocked.Increment(ref _settingsRevision);
            if (_settings.PluginEnabled)
                _state.SetNotice("PEOPLE SAVED RESET", "People Saved reset to 0", 1.8, Now);
            EvaluateLocked(Now);
        }
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

            while (_hotkeyQueue.TryDequeue(out var hk))
                DispatchHotkeyLocked(hk.VirtualKey, hk.Ctrl, hk.Shift, hk.Alt, hk.ReceivedAt);

            PollForegroundHotkeysLocked(now);
            ScanMembersLocked(now);
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
                _selector.LastStatus,
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

    private void PollForegroundHotkeysLocked(double now)
    {
        if (!IsGameForeground())
        {
            _hotkeys.ClearPollState();
            return;
        }

        bool ctrl = IsKeyDown(VkControl);
        bool shift = IsKeyDown(VkShift);
        bool alt = IsKeyDown(VkMenu);

        Span<int> keys = stackalloc int[4];
        int count = 0;
        AddUniqueKey(keys, ref count, _settings.TogglePluginHotkey.VirtualKey);
        if (_settings.PluginEnabled)
        {
            AddUniqueKey(keys, ref count, _settings.FocusTargetHotkey.VirtualKey);
            AddUniqueKey(keys, ref count, _settings.ToggleModeHotkey.VirtualKey);
            AddUniqueKey(keys, ref count, _settings.TargetCriticalHotkey.VirtualKey);
        }

        for (int i = 0; i < count; i++)
        {
            int vk = keys[i];
            if (_hotkeys.UpdatePollKeyState(vk, IsKeyDown(vk)))
                DispatchHotkeyLocked(vk, ctrl, shift, alt, now);
        }
    }

    private static void AddUniqueKey(Span<int> keys, ref int count, int vk)
    {
        if (vk <= 0) return;
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
        }
    }

    private void TogglePluginEnabledLocked(double now)
    {
        var updated = _settings.Copy();
        updated.PluginEnabled = !updated.PluginEnabled;
        _store.Save(updated);
        _settings = updated;
        if (!updated.PluginEnabled)
        {
            ResetTransientOnDisableLocked();
        }
        else
        {
            _state.SetNotice("DDO HEALER'S HELPER: ON", "Alerts and tracking resumed", 1.8, now);
        }

        Interlocked.Increment(ref _settingsRevision);
        EvaluateLocked(now);
    }

    private void ResetTransientOnDisableLocked()
    {
        _state.Reset();
        _savedTracker.Reset();
        _selector.ResetCooldown();
        _prevSelfSp = null;
        _lastAutoTargetGen = 0;
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

        string name = ResolveTargetName(selectedId);
        if (string.IsNullOrWhiteSpace(name))
        {
            _state.SetNotice("TARGET UNAVAILABLE", "Selected entity could not be resolved", 2.0, now);
            EvaluateLocked(now);
            return false;
        }

        var updated = _settings.Copy();
        updated.FocusedPlayerId = selectedId;
        updated.FocusedPlayerName = name;
        updated.Mode = TrackingMode.SinglePlayer;
        _store.Save(updated);
        _settings = updated;
        Interlocked.Increment(ref _settingsRevision);

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
        _store.Save(updated);
        _settings = updated;
        Interlocked.Increment(ref _settingsRevision);

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
        AlertBannerView view = _state.Evaluate(current, _settings, now, targetingStatus: _selector.LastStatus);

        WatchedMemberSnapshot? target = null;
        if (view.AlertPlayerId is { } alertId)
            target = current.FirstOrDefault(m => m.Id == alertId);

        target ??= current
            .Where(m => m.Connected && m.Id != 0)
            .OrderBy(m => m.Percent)
            .ThenBy(m => m.PartySlotIndex)
            .FirstOrDefault();

        if (target is null)
        {
            _state.SetNotice("NO WATCHED TARGET", "No connected party or focused player", 2.0, now);
            EvaluateLocked(now);
            return false;
        }

        bool selected = _selector.TrySelect(target.Id, target.Name, target.PartySlotIndex, _settings, now, bypassCooldown);
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
            _selector.ResetCooldown();
            _character = charId;
            Volatile.Write(ref _characterIdBits, unchecked((long)(charId ?? 0)));
            _prevSelfSp = null;
            _lastAutoTargetGen = 0;
            _lastSoundGen = 0;
        }

        if (!charId.HasValue || charId.Value == 0)
        {
            Volatile.Write(ref _members, Array.Empty<WatchedMemberSnapshot>());
            _savedTracker.Reset();
            return;
        }

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
            int hp = selfEntity.HitPoints_Current ?? 0;
            int maxHp = selfEntity.HitPoints_Max ?? 0;
            bool isDown = selfEntity.LifeState is LifeState.Dead or LifeState.Incapacitated
                || (maxHp > 0 && hp <= 0);
            list.Add(new WatchedMemberSnapshot(selfId, selfName, hp, maxHp, 0, true, isDown));

            int? currSp = selfEntity.SpellPoints_Current;
            if (_settings.PluginEnabled && _prevSelfSp.HasValue && currSp.HasValue && currSp.Value < _prevSelfSp.Value)
                RecordSelfCast(now);
            _prevSelfSp = currSp;
        }
        else
        {
            list.Add(new WatchedMemberSnapshot(selfId, selfName, 0, 0, 0, false, false));
            _prevSelfSp = null;
        }
        seen.Add(selfId);

        var party = _data.GetPartyMembers();
        if (party is not null)
        {
            int slot = 0;
            foreach (var m in party)
            {
                if (m is null || m.Id == 0 || m.Id == selfId || !seen.Add(m.Id)) continue;
                slot++;

                IEntity? ent = _data.GetEntity(m.Id);
                bool hasLiveEntity = ent is not null && !ent.IsDeleted;
                int hp = (hasLiveEntity ? ent!.HitPoints_Current : null) ?? m.HitPoints;
                int maxHp = (hasLiveEntity ? ent!.HitPoints_Max : null) ?? m.MaxHitPoints;

                string name = !string.IsNullOrWhiteSpace(m.Name)
                    ? m.Name
                    : (hasLiveEntity && !string.IsNullOrWhiteSpace(ent!.Name) ? ent.Name : $"Member {slot}");

                bool isDown = (hasLiveEntity && ent!.LifeState is LifeState.Dead or LifeState.Incapacitated)
                    || (m.Connected && maxHp > 0 && hp <= 0);

                list.Add(new WatchedMemberSnapshot(m.Id, name, hp, maxHp, slot, m.Connected, isDown));
            }
        }

        if (_settings.Mode == TrackingMode.SinglePlayer)
            TryAddExternalFocusedPlayer(list, seen);

        var snapshot = list.ToArray();
        Volatile.Write(ref _members, snapshot);

        if (!_settings.PluginEnabled)
        {
            _savedTracker.Reset();
            return;
        }

        bool inTown = _data.InTown() == true;
        var saved = _savedTracker.ProcessScan(snapshot, _settings.PluginEnabled, inTown, now, ReadLastSelfCast());
        if (saved.Count > 0)
        {
            var updated = _settings.Copy();
            updated.PeopleSaved += saved.Count;
            _store.Save(updated);
            _settings = updated;
            Interlocked.Increment(ref _settingsRevision);
            _state.SetNotice(
                $"SAVED {saved[0]}! (Saved: {updated.PeopleSaved})",
                "Clutch heal kept ally alive",
                2.0,
                now);
        }
    }

    private void TryAddExternalFocusedPlayer(List<WatchedMemberSnapshot> list, HashSet<ulong> seen)
    {
        bool alreadyPresent = list.Any(m =>
            (_settings.FocusedPlayerId > 0 && m.Id == _settings.FocusedPlayerId) ||
            (!string.IsNullOrWhiteSpace(_settings.FocusedPlayerName)
                && string.Equals(m.Name, _settings.FocusedPlayerName, StringComparison.OrdinalIgnoreCase)));
        if (alreadyPresent) return;

        IEntity? ent = _settings.FocusedPlayerId > 0 ? _data.GetEntity(_settings.FocusedPlayerId) : null;
        if ((ent is null || ent.IsDeleted) && !string.IsNullOrWhiteSpace(_settings.FocusedPlayerName))
        {
            var entities = _data.GetEntities();
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

        if (ent is null || ent.IsDeleted || ent.InstanceId == 0 || !seen.Add(ent.InstanceId)) return;

        int hp = ent.HitPoints_Current ?? 0;
        int maxHp = ent.HitPoints_Max ?? 0;
        bool isDown = ent.LifeState is LifeState.Dead or LifeState.Incapacitated || (maxHp > 0 && hp <= 0);
        string name = !string.IsNullOrWhiteSpace(ent.Name) ? ent.Name : _settings.FocusedPlayerName;
        list.Add(new WatchedMemberSnapshot(ent.InstanceId, name, hp, maxHp, -1, true, isDown));
    }

    private void EvaluateLocked(double now)
    {
        WatchedMemberSnapshot[] current = _members;
        string statusLine = !_settings.PluginEnabled
            ? "Plugin Off"
            : !_character.HasValue || _character == 0
                ? "Waiting for character login"
                : "";

        AlertBannerView view = _state.Evaluate(current, _settings, now, statusLine, _selector.LastStatus);

        if (_settings.PluginEnabled
            && view.Severity is BannerSeverity.LowHp or BannerSeverity.CriticalDown
            && view.AlertPlayerId is { } alertId)
        {
            if (_settings.SoundOnAlert && view.AlertGeneration != _lastSoundGen)
            {
                _lastSoundGen = view.AlertGeneration;
                try { SystemSounds.Exclamation.Play(); }
                catch { }
            }

            if (_settings.AutoTargetOnAlert
                && _settings.TargetingMethod != TargetMethod.Off
                && view.AlertGeneration != _lastAutoTargetGen
                && IsGameOrOverlayForeground())
            {
                var target = current.FirstOrDefault(m => m.Id == alertId);
                if (target is not null)
                {
                    _lastAutoTargetGen = view.AlertGeneration;
                    _selector.TrySelect(target.Id, target.Name, target.PartySlotIndex, _settings, now, bypassCooldown: false);
                    view = view with { TargetingStatus = _selector.LastStatus };
                }
            }
        }

        Volatile.Write(ref _view, view);
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
        if (GameWindow == IntPtr.Zero && _gamePid == 0) return false;
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        _ = GetWindowThreadProcessId(fg, out uint pid);
        return pid != 0 && pid == (uint)_gamePid;
    }

    private bool IsGameOrOverlayForeground()
    {
        if (IsGameForeground()) return true;
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        _ = GetWindowThreadProcessId(fg, out uint pid);
        if (pid == 0) return false;
        if (pid == (uint)Environment.ProcessId) return true;
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            return string.Equals(proc.ProcessName, "LosslessScaling", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
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
            if (pid != 0) return (int)pid;
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
            _timer.Dispose();
            while (_hotkeyQueue.TryDequeue(out _)) { }
            _state.Reset();
            _savedTracker.Reset();
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
