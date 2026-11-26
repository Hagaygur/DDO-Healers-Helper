using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoK.HealersHelper;

internal sealed class Settings
{
    public const string DefaultLowHpBgHex = "#B84200";
    public const string DefaultLowHpBorderHex = "#FFE066";
    public const string DefaultCriticalBgHex = "#960A14";
    public const string DefaultCriticalBorderHex = "#FFD644";
    public const string DefaultBannerTextHex = "#FFFFFF";
    public const string DefaultFlashAltBgHex = "#0A0A0E";

    public static readonly HotkeyBinding DefaultTogglePluginHotkey = new(0x4B, true, true, false);
    public static readonly HotkeyBinding DefaultFocusTargetHotkey = new(0x48, true, true, false);
    public static readonly HotkeyBinding DefaultToggleModeHotkey = new(0x4A, true, true, false);
    public static readonly HotkeyBinding DefaultTargetCriticalHotkey = new(0x54, true, true, false);

    public bool PluginEnabled { get; set; } = true;
    public TrackingMode Mode { get; set; } = TrackingMode.Party;
    public bool IncludeSelfInPartyMode { get; set; } = true;
    public bool IncludeTempHpInThreshold { get; set; } = false;
    public bool SuppressAlertsInTown { get; set; } = false;
    public ulong FocusedPlayerId { get; set; }
    public string FocusedPlayerName { get; set; } = "";
    public int HpThresholdPercent { get; set; } = 50;
    public int RecoveryHysteresisPercent { get; set; } = 3;
    public int DeadAlertSeconds { get; set; } = 6;
    public int FallbackDeathHp { get; set; } = LifeStatusEvaluator.DefaultFallbackDeathHp;
    public int MaxAlertSeconds { get; set; } = 0;
    public int FontSizePixels { get; set; } = 56;
    public int BannerOpacity { get; set; } = 235;
    public bool FlashBanner { get; set; } = true;
    public bool ShowBannerHpBar { get; set; } = true;
    public int FlashIntervalMs { get; set; } = 250;
    public bool SoundOnAlert { get; set; } = false;
    public bool HudEnabled { get; set; } = true;
    public bool ExcludeFromCapture { get; set; } = true;
    public double HudX { get; set; } = 0.50;
    public double HudY { get; set; } = 0.42;
    public string LowHpBgHex { get; set; } = DefaultLowHpBgHex;
    public string LowHpBorderHex { get; set; } = DefaultLowHpBorderHex;
    public string CriticalBgHex { get; set; } = DefaultCriticalBgHex;
    public string CriticalBorderHex { get; set; } = DefaultCriticalBorderHex;
    public string BannerTextHex { get; set; } = DefaultBannerTextHex;
    public string FlashAltBgHex { get; set; } = DefaultFlashAltBgHex;
    public bool StatusHudEnabled { get; set; } = true;
    public bool ShowRankOnStatusHud { get; set; } = true;
    public int StatusFontSizePixels { get; set; } = 14;
    public double StatusHudX { get; set; } = 0.50;
    public double StatusHudY { get; set; } = 0.76;
    public UiTheme Theme { get; set; } = UiTheme.EberronGold;

    // DH 5 Beta Triage Raid HUD & Targetless Quick-Cast (ignored cleanly in DH 4)
    public bool RaidHudEnabled { get; set; } = false;
    public double RaidHudX { get; set; } = 0.02;
    public double RaidHudY { get; set; } = 0.25;
    public int QuickHealHotbar { get; set; } = 0;
    public int QuickHealSlot { get; set; } = 1;
    public int QuickRaiseHotbar { get; set; } = 0;
    public int QuickRaiseSlot { get; set; } = 1;

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
    public bool RestorePreviousTargetAfterHeal { get; set; } = true;
    public double RestoreTargetDelaySeconds { get; set; } = 0.2;
    public HotkeyBinding TogglePluginHotkey { get; set; } = DefaultTogglePluginHotkey;
    public HotkeyBinding ToggleBannerHotkey { get; set; } = HotkeyBinding.Empty;
    public HotkeyBinding FocusTargetHotkey { get; set; } = DefaultFocusTargetHotkey;
    public HotkeyBinding ToggleModeHotkey { get; set; } = DefaultToggleModeHotkey;
    public HotkeyBinding TargetCriticalHotkey { get; set; } = DefaultTargetCriticalHotkey;
    public HotkeyBinding QuickCastCriticalHotkey { get; set; } = HotkeyBinding.Empty;
    public HotkeyBinding ResetSavedHotkey { get; set; } = HotkeyBinding.Empty;
    public HotkeyBinding CopyScorecardHotkey { get; set; } = HotkeyBinding.Empty;

    public Settings Copy()
    {
        var copy = new Settings
        {
            PluginEnabled = PluginEnabled,
            Mode = Mode,
            IncludeSelfInPartyMode = IncludeSelfInPartyMode,
            IncludeTempHpInThreshold = IncludeTempHpInThreshold,
            SuppressAlertsInTown = SuppressAlertsInTown,
            FocusedPlayerId = FocusedPlayerId,
            FocusedPlayerName = FocusedPlayerName,
            HpThresholdPercent = HpThresholdPercent,
            RecoveryHysteresisPercent = RecoveryHysteresisPercent,
            DeadAlertSeconds = DeadAlertSeconds,
            FallbackDeathHp = FallbackDeathHp,
            MaxAlertSeconds = MaxAlertSeconds,
            FontSizePixels = FontSizePixels,
            BannerOpacity = BannerOpacity,
            FlashBanner = FlashBanner,
            ShowBannerHpBar = ShowBannerHpBar,
            FlashIntervalMs = FlashIntervalMs,
            SoundOnAlert = SoundOnAlert,
            HudEnabled = HudEnabled,
            ExcludeFromCapture = ExcludeFromCapture,
            HudX = HudX,
            HudY = HudY,
            LowHpBgHex = NormalizeHexColor(LowHpBgHex, DefaultLowHpBgHex),
            LowHpBorderHex = NormalizeHexColor(LowHpBorderHex, DefaultLowHpBorderHex),
            CriticalBgHex = NormalizeHexColor(CriticalBgHex, DefaultCriticalBgHex),
            CriticalBorderHex = NormalizeHexColor(CriticalBorderHex, DefaultCriticalBorderHex),
            BannerTextHex = NormalizeHexColor(BannerTextHex, DefaultBannerTextHex),
            FlashAltBgHex = NormalizeHexColor(FlashAltBgHex, DefaultFlashAltBgHex),
            StatusHudEnabled = StatusHudEnabled,
            ShowRankOnStatusHud = ShowRankOnStatusHud,
            StatusFontSizePixels = StatusFontSizePixels,
            StatusHudX = StatusHudX,
            StatusHudY = StatusHudY,
            Theme = Theme,
            RaidHudEnabled = RaidHudEnabled,
            RaidHudX = RaidHudX,
            RaidHudY = RaidHudY,
            QuickHealHotbar = QuickHealHotbar,
            QuickHealSlot = QuickHealSlot,
            QuickRaiseHotbar = QuickRaiseHotbar,
            QuickRaiseSlot = QuickRaiseSlot,
            SaveWindowSeconds = SaveWindowSeconds,
            SelfCastWindowSeconds = SelfCastWindowSeconds,
            MinHealDelta = MinHealDelta,
            ShowSavedNotice = ShowSavedNotice,
            AutoTargetOnAlert = AutoTargetOnAlert,
            TargetingMethod = TargetingMethod,
            AutoTargetCooldownSeconds = AutoTargetCooldownSeconds,
            RestorePreviousTargetAfterHeal = RestorePreviousTargetAfterHeal,
            RestoreTargetDelaySeconds = RestoreTargetDelaySeconds,
            TogglePluginHotkey = NormalizeHotkey(TogglePluginHotkey),
            ToggleBannerHotkey = NormalizeHotkey(ToggleBannerHotkey),
            FocusTargetHotkey = NormalizeHotkey(FocusTargetHotkey),
            ToggleModeHotkey = NormalizeHotkey(ToggleModeHotkey),
            TargetCriticalHotkey = NormalizeHotkey(TargetCriticalHotkey),
            QuickCastCriticalHotkey = NormalizeHotkey(QuickCastCriticalHotkey),
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

    public void ResetBannerColors()
    {
        LowHpBgHex = DefaultLowHpBgHex;
        LowHpBorderHex = DefaultLowHpBorderHex;
        CriticalBgHex = DefaultCriticalBgHex;
        CriticalBorderHex = DefaultCriticalBorderHex;
        BannerTextHex = DefaultBannerTextHex;
        FlashAltBgHex = DefaultFlashAltBgHex;
    }

    public void Validate()
    {
        if (!Enum.IsDefined(Mode)) Mode = TrackingMode.Party;
        if (!Enum.IsDefined(TargetingMethod)) TargetingMethod = TargetMethod.Auto;
        if (!Enum.IsDefined(Theme)) Theme = UiTheme.EberronGold;
        FocusedPlayerName = (FocusedPlayerName ?? "").Trim();
        HpThresholdPercent = Math.Clamp(HpThresholdPercent, 5, 95);
        RecoveryHysteresisPercent = Math.Clamp(RecoveryHysteresisPercent, 0, 20);
        DeadAlertSeconds = Math.Clamp(DeadAlertSeconds, 0, 60);
        FallbackDeathHp = Math.Clamp(FallbackDeathHp, LifeStatusEvaluator.MinFallbackDeathHp, LifeStatusEvaluator.MaxFallbackDeathHp);
        MaxAlertSeconds = Math.Clamp(MaxAlertSeconds, 0, 120);
        FontSizePixels = Math.Clamp(FontSizePixels, 18, 140);
        BannerOpacity = Math.Clamp(BannerOpacity, 20, 255);
        FlashIntervalMs = Math.Clamp(FlashIntervalMs, 100, 1000);
        HudX = double.IsFinite(HudX) ? Math.Clamp(HudX, 0.0, 1.0) : 0.50;
        HudY = double.IsFinite(HudY) ? Math.Clamp(HudY, 0.0, 1.0) : 0.42;
        LowHpBgHex = NormalizeHexColor(LowHpBgHex, DefaultLowHpBgHex);
        LowHpBorderHex = NormalizeHexColor(LowHpBorderHex, DefaultLowHpBorderHex);
        CriticalBgHex = NormalizeHexColor(CriticalBgHex, DefaultCriticalBgHex);
        CriticalBorderHex = NormalizeHexColor(CriticalBorderHex, DefaultCriticalBorderHex);
        BannerTextHex = NormalizeHexColor(BannerTextHex, DefaultBannerTextHex);
        FlashAltBgHex = NormalizeHexColor(FlashAltBgHex, DefaultFlashAltBgHex);
        StatusFontSizePixels = Math.Clamp(StatusFontSizePixels, 9, 48);
        StatusHudX = double.IsFinite(StatusHudX) ? Math.Clamp(StatusHudX, 0.0, 1.0) : 0.50;
        StatusHudY = double.IsFinite(StatusHudY) ? Math.Clamp(StatusHudY, 0.0, 1.0) : 0.76;
        RaidHudX = double.IsFinite(RaidHudX) ? Math.Clamp(RaidHudX, 0.0, 1.0) : 0.02;
        RaidHudY = double.IsFinite(RaidHudY) ? Math.Clamp(RaidHudY, 0.0, 1.0) : 0.25;
        QuickHealHotbar = Math.Clamp(QuickHealHotbar, 0, 20);
        QuickHealSlot = Math.Clamp(QuickHealSlot, 1, 10);
        QuickRaiseHotbar = Math.Clamp(QuickRaiseHotbar, 0, 20);
        QuickRaiseSlot = Math.Clamp(QuickRaiseSlot, 1, 10);

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
        RestoreTargetDelaySeconds = double.IsFinite(RestoreTargetDelaySeconds)
            ? Math.Clamp(RestoreTargetDelaySeconds, 0.0, 5.0)
            : 0.2;
        TogglePluginHotkey = NormalizeHotkey(TogglePluginHotkey);
        ToggleBannerHotkey = NormalizeHotkey(ToggleBannerHotkey);
        FocusTargetHotkey = NormalizeHotkey(FocusTargetHotkey);
        ToggleModeHotkey = NormalizeHotkey(ToggleModeHotkey);
        TargetCriticalHotkey = NormalizeHotkey(TargetCriticalHotkey);
        QuickCastCriticalHotkey = NormalizeHotkey(QuickCastCriticalHotkey);
        ResetSavedHotkey = NormalizeHotkey(ResetSavedHotkey);
        CopyScorecardHotkey = NormalizeHotkey(CopyScorecardHotkey);
    }

    public static string NormalizeHexColor(string? value, string fallback)
    {
        if (TryParseHexRgb(value, out int r, out int g, out int b))
        {
            return string.Create(CultureInfo.InvariantCulture, $"#{r:X2}{g:X2}{b:X2}");
        }

        return TryParseHexRgb(fallback, out int fr, out int fg, out int fb)
            ? string.Create(CultureInfo.InvariantCulture, $"#{fr:X2}{fg:X2}{fb:X2}")
            : "#FFFFFF";
    }

    public static bool TryParseHexRgb(string? value, out int r, out int g, out int b)
    {
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        ReadOnlySpan<char> span = value.AsSpan().Trim();
        if (span.Length > 0 && span[0] == '#')
            span = span[1..];

        if (span.Length == 3)
        {
            if (!TryHexNibble(span[0], out int rn)
                || !TryHexNibble(span[1], out int gn)
                || !TryHexNibble(span[2], out int bn))
            {
                return false;
            }

            r = (rn << 4) | rn;
            g = (gn << 4) | gn;
            b = (bn << 4) | bn;
            return true;
        }

        if (span.Length == 6
            && int.TryParse(span[0..2], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out r)
            && int.TryParse(span[2..4], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out g)
            && int.TryParse(span[4..6], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out b))
        {
            return true;
        }

        r = g = b = 0;
        return false;
    }

    private static bool TryHexNibble(char c, out int value)
    {
        if (c is >= '0' and <= '9') { value = c - '0'; return true; }
        if (c is >= 'a' and <= 'f') { value = c - 'a' + 10; return true; }
        if (c is >= 'A' and <= 'F') { value = c - 'A' + 10; return true; }
        value = 0;
        return false;
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

    public static bool TryParseFallbackDeathHp(string? text, out int value)
    {
        value = 0;
        string trimmed = (text ?? "").Trim();
        if (string.IsNullOrEmpty(trimmed))
            return false;

        if (string.Equals(trimmed, "off", StringComparison.OrdinalIgnoreCase))
        {
            value = 0;
            return true;
        }

        if (!long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
            return false;

        if (parsed == 0)
        {
            value = 0;
            return true;
        }

        long normalized = parsed > 0 ? -parsed : parsed;
        value = (int)Math.Clamp(normalized, LifeStatusEvaluator.MinFallbackDeathHp, LifeStatusEvaluator.MaxFallbackDeathHp);
        return true;
    }

    public static bool TryIntRange(string text, int min, int max, out int value) =>
        int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
        && value >= min && value <= max;

    public static bool TryDoubleRange(string text, double min, double max, out double value) =>
        double.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value) && value >= min && value <= max;
}
