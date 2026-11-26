using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoK.HealersHelper;

// Portable file-backed IHealerEngineView used when the UI is opened before the
// live game engine connects (e.g., character select or offline configuration).
internal sealed class OfflineSettingsEngineView(string settingsFolder, Func<string, bool>? copyClipboard = null) : IHealerEngineView
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private Settings? _cached;
    private DateTime _cachedWriteUtc = DateTime.MinValue;
    private long _cachedLength = -1;
    private int _revision = 1;

    public string SettingsFolder { get; } = settingsFolder;
    private string ConfigPath => Path.Combine(SettingsFolder, "healers-helper.json");

    public IntPtr GameWindow => IntPtr.Zero;

    public int SettingsRevision
    {
        get
        {
            _ = EnsureLoaded();
            return _revision;
        }
    }

    public int PeopleSaved => EnsureLoaded().PeopleSaved;

    public Settings GetSettings() => EnsureLoaded().Copy();

    public void SaveSettings(Settings settings)
    {
        var copy = settings.Copy();
        var live = EnsureLoaded();
        copy.CopyLiveStatsFrom(live);
        Persist(copy);
    }

    public void SavePosition(double x, double y)
    {
        var copy = EnsureLoaded().Copy();
        copy.HudX = x;
        copy.HudY = y;
        Persist(copy);
    }

    public void SaveBannerLayout(double x, double y, int fontSizePixels, int bannerOpacity)
    {
        var copy = EnsureLoaded().Copy();
        copy.HudX = x;
        copy.HudY = y;
        copy.FontSizePixels = fontSizePixels;
        copy.BannerOpacity = bannerOpacity;
        Persist(copy);
    }

    public void SaveStatusPosition(double x, double y)
    {
        var copy = EnsureLoaded().Copy();
        copy.StatusHudX = x;
        copy.StatusHudY = y;
        Persist(copy);
    }

    public AlertBannerView View()
    {
        var s = EnsureLoaded();
        return new AlertBannerView(
            BannerSeverity.None,
            s.Mode,
            "",
            "",
            0,
            null,
            "",
            null,
            0,
            "Waiting for game connection…",
            "Targeting: idle",
            s.PluginEnabled,
            s.PeopleSaved);
    }

    public WatchedMemberSnapshot[] GetMembers() => Array.Empty<WatchedMemberSnapshot>();

    public void TogglePluginEnabled()
    {
        var copy = EnsureLoaded().Copy();
        copy.PluginEnabled = !copy.PluginEnabled;
        Persist(copy);
    }

    public void ToggleBannerHud()
    {
        var copy = EnsureLoaded().Copy();
        copy.HudEnabled = !copy.HudEnabled;
        Persist(copy);
    }

    public void ResetPeopleSaved()
    {
        var copy = EnsureLoaded().Copy();
        copy.ResetLiveStats();
        Persist(copy);
    }

    public string GetChatScorecard() => HealerStats.FormatChatScorecard(EnsureLoaded());

    public bool CopyChatScorecard() => copyClipboard?.Invoke(GetChatScorecard()) ?? false;

    public bool FocusCurrentTarget() => false;

    public void CycleMode()
    {
        var copy = EnsureLoaded().Copy();
        copy.Mode = copy.Mode == TrackingMode.Party ? TrackingMode.SinglePlayer : TrackingMode.Party;
        Persist(copy);
    }

    public bool TargetCriticalNow() => false;

    public void Report(string message) { }

    public bool IsPlayerMuted(ulong id, string? name) => false;

    public bool ToggleMutePlayer(ulong id, string? name) => false;

    public void ClearMutedPlayers() { }

    public IReadOnlyCollection<string> GetMutedPlayerNames() => Array.Empty<string>();

    public string GetRaidKeymapSummary() => TargetPolicy.EvaluateRaidKeymapReadiness(null).Summary;

    private Settings EnsureLoaded()
    {
        try
        {
            var info = new FileInfo(ConfigPath);
            if (!info.Exists)
            {
                if (_cached is null || _cachedLength != -1)
                {
                    _cached = new Settings();
                    _cachedWriteUtc = DateTime.MinValue;
                    _cachedLength = -1;
                    _revision++;
                }
                return _cached;
            }

            if (_cached is not null
                && info.LastWriteTimeUtc == _cachedWriteUtc
                && info.Length == _cachedLength)
            {
                return _cached;
            }

            var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(ConfigPath), Json) ?? new Settings();
            loaded.Validate();
            _cached = loaded;
            _cachedWriteUtc = info.LastWriteTimeUtc;
            _cachedLength = info.Length;
            _revision++;
            return _cached;
        }
        catch
        {
            return _cached ??= new Settings();
        }
    }

    private void Persist(Settings updated)
    {
        updated.Validate();
        _cached = updated.Copy();
        _revision++;
        try
        {
            var store = new SettingsStore(SettingsFolder);
            store.Save(updated);
            var info = new FileInfo(store.ConfigPath);
            if (info.Exists)
            {
                _cachedWriteUtc = info.LastWriteTimeUtc;
                _cachedLength = info.Length;
            }
        }
        catch { }
    }
}
