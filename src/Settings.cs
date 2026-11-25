using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoK.HealersHelper;

internal sealed class Settings
{
    public static readonly HotkeyBinding DefaultTogglePluginHotkey = new(0x4B, true, true, false);
    public static readonly HotkeyBinding DefaultFocusTargetHotkey = new(0x48, true, true, false);
    public static readonly HotkeyBinding DefaultToggleModeHotkey = new(0x4A, true, true, false);
    public static readonly HotkeyBinding DefaultTargetCriticalHotkey = new(0x54, true, true, false);

    public bool PluginEnabled { get; set; } = true;
    public TrackingMode Mode { get; set; } = TrackingMode.Party;
    public bool IncludeSelfInPartyMode { get; set; } = true;
    public bool SuppressAlertsInTown { get; set; } = false;
    public ulong FocusedPlayerId { get; set; }
    public string FocusedPlayerName { get; set; } = "";
    public int HpThresholdPercent { get; set; } = 50;
    public int RecoveryHysteresisPercent { get; set; } = 3;
    public int DeadAlertSeconds { get; set; } = 6;
    public int MaxAlertSeconds { get; set; } = 0;
    public int FontSizePixels { get; set; } = 56;
    public int BannerOpacity { get; set; } = 235;
    public bool FlashBanner { get; set; } = true;
    public int FlashIntervalMs { get; set; } = 250;
    public bool SoundOnAlert { get; set; } = false;
    public bool HudEnabled { get; set; } = true;
    public bool ExcludeFromCapture { get; set; } = true;
    public double HudX { get; set; } = 0.50;
    public double HudY { get; set; } = 0.42;
    public bool StatusHudEnabled { get; set; } = true;
    public bool ShowRankOnStatusHud { get; set; } = true;
    public int StatusFontSizePixels { get; set; } = 14;
    public double StatusHudX { get; set; } = 0.50;
    public double StatusHudY { get; set; } = 0.76;

    // Clutch save tuning
    public double SaveWindowSeconds { get; set; } = 5.0;
    public double SelfCastWindowSeconds { get; set; } = 3.5;
    public int MinHealDelta { get; set; } = 5;
    public bool ShowSavedNotice { get; set; } = true;

    // Persisted live gamification stats (reset together via ResetLiveStats)
    public int PeopleSaved { get; set; }
    public int CurrentSaveStreak { get; set; }
    public int BestSaveStreak { get; set; }
    public string ClosestCallPlayerName { get; set; } = "";
    public double ClosestCallPreHealPercent { get; set; }
    public int ClosestCallPreHealHp { get; set; }
    public int BiggestOverkillSaved { get; set; }
    public int OnePercentClubSaves { get; set; }
    public int BestReflexMs { get; set; }
    public int AvgReflexMs { get; set; }
    public int ReflexSampleCount { get; set; }

    public bool AutoTargetOnAlert { get; set; } = true;
    public TargetMethod TargetingMethod { get; set; } = TargetMethod.Auto;
    public double AutoTargetCooldownSeconds { get; set; } = 3.0;
    public HotkeyBinding TogglePluginHotkey { get; set; } = DefaultTogglePluginHotkey;
    public HotkeyBinding FocusTargetHotkey { get; set; } = DefaultFocusTargetHotkey;
    public HotkeyBinding ToggleModeHotkey { get; set; } = DefaultToggleModeHotkey;
    public HotkeyBinding TargetCriticalHotkey { get; set; } = DefaultTargetCriticalHotkey;
    public HotkeyBinding ResetSavedHotkey { get; set; } = HotkeyBinding.Empty;
    public HotkeyBinding CopyScorecardHotkey { get; set; } = HotkeyBinding.Empty;

    public Settings Copy()
    {
        var copy = new Settings
        {
            PluginEnabled = PluginEnabled,
            Mode = Mode,
            IncludeSelfInPartyMode = IncludeSelfInPartyMode,
            SuppressAlertsInTown = SuppressAlertsInTown,
            FocusedPlayerId = FocusedPlayerId,
            FocusedPlayerName = FocusedPlayerName,
            HpThresholdPercent = HpThresholdPercent,
            RecoveryHysteresisPercent = RecoveryHysteresisPercent,
            DeadAlertSeconds = DeadAlertSeconds,
            MaxAlertSeconds = MaxAlertSeconds,
            FontSizePixels = FontSizePixels,
            BannerOpacity = BannerOpacity,
            FlashBanner = FlashBanner,
            FlashIntervalMs = FlashIntervalMs,
            SoundOnAlert = SoundOnAlert,
            HudEnabled = HudEnabled,
            ExcludeFromCapture = ExcludeFromCapture,
            HudX = HudX,
            HudY = HudY,
            StatusHudEnabled = StatusHudEnabled,
            ShowRankOnStatusHud = ShowRankOnStatusHud,
            StatusFontSizePixels = StatusFontSizePixels,
            StatusHudX = StatusHudX,
            StatusHudY = StatusHudY,
            SaveWindowSeconds = SaveWindowSeconds,
            SelfCastWindowSeconds = SelfCastWindowSeconds,
            MinHealDelta = MinHealDelta,
            ShowSavedNotice = ShowSavedNotice,
            AutoTargetOnAlert = AutoTargetOnAlert,
            TargetingMethod = TargetingMethod,
            AutoTargetCooldownSeconds = AutoTargetCooldownSeconds,
            TogglePluginHotkey = NormalizeHotkey(TogglePluginHotkey),
            FocusTargetHotkey = NormalizeHotkey(FocusTargetHotkey),
            ToggleModeHotkey = NormalizeHotkey(ToggleModeHotkey),
            TargetCriticalHotkey = NormalizeHotkey(TargetCriticalHotkey),
            ResetSavedHotkey = NormalizeHotkey(ResetSavedHotkey),
            CopyScorecardHotkey = NormalizeHotkey(CopyScorecardHotkey)
        };
        copy.CopyLiveStatsFrom(this);
        return copy;
    }

    public void CopyLiveStatsFrom(Settings live)
    {
        ArgumentNullException.ThrowIfNull(live);
        PeopleSaved = live.PeopleSaved;
        CurrentSaveStreak = live.CurrentSaveStreak;
        BestSaveStreak = live.BestSaveStreak;
        ClosestCallPlayerName = live.ClosestCallPlayerName ?? "";
        ClosestCallPreHealPercent = live.ClosestCallPreHealPercent;
        ClosestCallPreHealHp = live.ClosestCallPreHealHp;
        BiggestOverkillSaved = live.BiggestOverkillSaved;
        OnePercentClubSaves = live.OnePercentClubSaves;
        BestReflexMs = live.BestReflexMs;
        AvgReflexMs = live.AvgReflexMs;
        ReflexSampleCount = live.ReflexSampleCount;
    }

    public void ResetLiveStats()
    {
        PeopleSaved = 0;
        CurrentSaveStreak = 0;
        BestSaveStreak = 0;
        ClosestCallPlayerName = "";
        ClosestCallPreHealPercent = 0.0;
        ClosestCallPreHealHp = 0;
        BiggestOverkillSaved = 0;
        OnePercentClubSaves = 0;
        BestReflexMs = 0;
        AvgReflexMs = 0;
        ReflexSampleCount = 0;
    }

    public void Validate()
    {
        if (!Enum.IsDefined(Mode)) Mode = TrackingMode.Party;
        if (!Enum.IsDefined(TargetingMethod)) TargetingMethod = TargetMethod.Auto;
        FocusedPlayerName = (FocusedPlayerName ?? "").Trim();
        HpThresholdPercent = Math.Clamp(HpThresholdPercent, 5, 95);
        RecoveryHysteresisPercent = Math.Clamp(RecoveryHysteresisPercent, 0, 20);
        DeadAlertSeconds = Math.Clamp(DeadAlertSeconds, 0, 60);
        MaxAlertSeconds = Math.Clamp(MaxAlertSeconds, 0, 120);
        FontSizePixels = Math.Clamp(FontSizePixels, 18, 140);
        BannerOpacity = Math.Clamp(BannerOpacity, 80, 255);
        FlashIntervalMs = Math.Clamp(FlashIntervalMs, 100, 1000);
        HudX = double.IsFinite(HudX) ? Math.Clamp(HudX, 0.0, 1.0) : 0.50;
        HudY = double.IsFinite(HudY) ? Math.Clamp(HudY, 0.0, 1.0) : 0.42;
        StatusFontSizePixels = Math.Clamp(StatusFontSizePixels, 9, 48);
        StatusHudX = double.IsFinite(StatusHudX) ? Math.Clamp(StatusHudX, 0.0, 1.0) : 0.50;
        StatusHudY = double.IsFinite(StatusHudY) ? Math.Clamp(StatusHudY, 0.0, 1.0) : 0.76;

        SaveWindowSeconds = double.IsFinite(SaveWindowSeconds)
            ? Math.Clamp(SaveWindowSeconds, 1.0, 15.0)
            : 5.0;
        SelfCastWindowSeconds = double.IsFinite(SelfCastWindowSeconds)
            ? Math.Clamp(SelfCastWindowSeconds, 1.0, 15.0)
            : 3.5;
        MinHealDelta = Math.Clamp(MinHealDelta, 1, 200);

        PeopleSaved = Math.Max(0, PeopleSaved);
        CurrentSaveStreak = Math.Max(0, CurrentSaveStreak);
        BestSaveStreak = Math.Max(Math.Max(0, BestSaveStreak), CurrentSaveStreak);

        ClosestCallPlayerName = (ClosestCallPlayerName ?? "").Trim();
        ClosestCallPreHealHp = Math.Max(0, ClosestCallPreHealHp);
        if (ClosestCallPreHealHp <= 0 || string.IsNullOrWhiteSpace(ClosestCallPlayerName))
        {
            ClosestCallPlayerName = "";
            ClosestCallPreHealPercent = 0.0;
            ClosestCallPreHealHp = 0;
        }
        else
        {
            ClosestCallPreHealPercent = double.IsFinite(ClosestCallPreHealPercent)
                ? Math.Clamp(ClosestCallPreHealPercent, 0.0, 100.0)
                : 0.0;
        }

        BiggestOverkillSaved = Math.Max(0, BiggestOverkillSaved);
        OnePercentClubSaves = Math.Max(0, OnePercentClubSaves);
        ReflexSampleCount = Math.Max(0, ReflexSampleCount);
        if (ReflexSampleCount <= 0)
        {
            BestReflexMs = 0;
            AvgReflexMs = 0;
        }
        else
        {
            BestReflexMs = Math.Clamp(BestReflexMs, 0, 10000);
            AvgReflexMs = Math.Clamp(AvgReflexMs, 0, 10000);
        }

        AutoTargetCooldownSeconds = double.IsFinite(AutoTargetCooldownSeconds)
            ? Math.Clamp(AutoTargetCooldownSeconds, 0.5, 30.0)
            : 3.0;
        TogglePluginHotkey = NormalizeHotkey(TogglePluginHotkey);
        FocusTargetHotkey = NormalizeHotkey(FocusTargetHotkey);
        ToggleModeHotkey = NormalizeHotkey(ToggleModeHotkey);
        TargetCriticalHotkey = NormalizeHotkey(TargetCriticalHotkey);
        ResetSavedHotkey = NormalizeHotkey(ResetSavedHotkey);
        CopyScorecardHotkey = NormalizeHotkey(CopyScorecardHotkey);
    }

    private static HotkeyBinding NormalizeHotkey(HotkeyBinding? binding) =>
        binding is null || binding.VirtualKey <= 0 || binding.VirtualKey > 0xFE || HotkeyMatcher.IsModifierKey(binding.VirtualKey)
            ? HotkeyBinding.Empty
            : binding with { };
}

internal sealed class SettingsStore
{
    public string DirectoryPath { get; }
    public string ConfigPath => Path.Combine(DirectoryPath, "healers-helper.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public SettingsStore(string directory)
    {
        DirectoryPath = directory;
        Directory.CreateDirectory(directory);
    }

    public Settings Load()
    {
        if (!File.Exists(ConfigPath))
        {
            var defaults = new Settings();
            Save(defaults);
            return defaults;
        }
        var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(ConfigPath), Json) ?? new Settings();
        settings.Validate();
        return settings;
    }

    public void Save(Settings settings)
    {
        settings.Validate();
        string temporary = ConfigPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Json));
        File.Move(temporary, ConfigPath, overwrite: true);
    }
}

internal static class SettingsInput
{
    public static bool TryFontSize(string text, out int value) =>
        TryIntRange(text, 18, 140, out value);

    public static bool TryStatusFontSize(string text, out int value) =>
        TryIntRange(text, 9, 48, out value);

    public static bool TryIntRange(string text, int min, int max, out int value) =>
        int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
        && value >= min && value <= max;

    public static bool TryDoubleRange(string text, double min, double max, out double value) =>
        double.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value) && value >= min && value <= max;
}
