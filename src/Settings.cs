using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoK.HealersHelper;

internal sealed class Settings
{
    public bool PluginEnabled { get; set; } = true;
    public TrackingMode Mode { get; set; } = TrackingMode.Party;
    public ulong FocusedPlayerId { get; set; }
    public string FocusedPlayerName { get; set; } = "";
    public int HpThresholdPercent { get; set; } = 50;
    public int RecoveryHysteresisPercent { get; set; } = 3;
    public int DeadAlertSeconds { get; set; } = 6;
    public int FontSizePixels { get; set; } = 56;
    public int FlashIntervalMs { get; set; } = 250;
    public bool SoundOnAlert { get; set; } = false;
    public bool HudEnabled { get; set; } = true;
    public bool ExcludeFromCapture { get; set; } = true;
    public double HudX { get; set; } = 0.50;
    public double HudY { get; set; } = 0.42;
    public bool StatusHudEnabled { get; set; } = true;
    public int StatusFontSizePixels { get; set; } = 14;
    public double StatusHudX { get; set; } = 0.50;
    public double StatusHudY { get; set; } = 0.76;
    public int PeopleSaved { get; set; }
    public bool AutoTargetOnAlert { get; set; } = true;
    public TargetMethod TargetingMethod { get; set; } = TargetMethod.Auto;
    public double AutoTargetCooldownSeconds { get; set; } = 3.0;
    public HotkeyBinding TogglePluginHotkey { get; set; } = new(0x4B, true, true, false);
    public HotkeyBinding FocusTargetHotkey { get; set; } = new(0x48, true, true, false);
    public HotkeyBinding ToggleModeHotkey { get; set; } = new(0x4A, true, true, false);
    public HotkeyBinding TargetCriticalHotkey { get; set; } = new(0x54, true, true, false);

    public Settings Copy() => new()
    {
        PluginEnabled = PluginEnabled,
        Mode = Mode,
        FocusedPlayerId = FocusedPlayerId,
        FocusedPlayerName = FocusedPlayerName,
        HpThresholdPercent = HpThresholdPercent,
        RecoveryHysteresisPercent = RecoveryHysteresisPercent,
        DeadAlertSeconds = DeadAlertSeconds,
        FontSizePixels = FontSizePixels,
        FlashIntervalMs = FlashIntervalMs,
        SoundOnAlert = SoundOnAlert,
        HudEnabled = HudEnabled,
        ExcludeFromCapture = ExcludeFromCapture,
        HudX = HudX,
        HudY = HudY,
        StatusHudEnabled = StatusHudEnabled,
        StatusFontSizePixels = StatusFontSizePixels,
        StatusHudX = StatusHudX,
        StatusHudY = StatusHudY,
        PeopleSaved = PeopleSaved,
        AutoTargetOnAlert = AutoTargetOnAlert,
        TargetingMethod = TargetingMethod,
        AutoTargetCooldownSeconds = AutoTargetCooldownSeconds,
        TogglePluginHotkey = NormalizeHotkey(TogglePluginHotkey),
        FocusTargetHotkey = NormalizeHotkey(FocusTargetHotkey),
        ToggleModeHotkey = NormalizeHotkey(ToggleModeHotkey),
        TargetCriticalHotkey = NormalizeHotkey(TargetCriticalHotkey)
    };

    public void Validate()
    {
        if (!Enum.IsDefined(Mode)) Mode = TrackingMode.Party;
        if (!Enum.IsDefined(TargetingMethod)) TargetingMethod = TargetMethod.Auto;
        FocusedPlayerName = (FocusedPlayerName ?? "").Trim();
        HpThresholdPercent = Math.Clamp(HpThresholdPercent, 5, 95);
        RecoveryHysteresisPercent = Math.Clamp(RecoveryHysteresisPercent, 0, 20);
        DeadAlertSeconds = Math.Clamp(DeadAlertSeconds, 0, 60);
        FontSizePixels = Math.Clamp(FontSizePixels, 18, 140);
        FlashIntervalMs = Math.Clamp(FlashIntervalMs, 100, 1000);
        HudX = double.IsFinite(HudX) ? Math.Clamp(HudX, 0.0, 1.0) : 0.50;
        HudY = double.IsFinite(HudY) ? Math.Clamp(HudY, 0.0, 1.0) : 0.42;
        StatusFontSizePixels = Math.Clamp(StatusFontSizePixels, 9, 48);
        StatusHudX = double.IsFinite(StatusHudX) ? Math.Clamp(StatusHudX, 0.0, 1.0) : 0.50;
        StatusHudY = double.IsFinite(StatusHudY) ? Math.Clamp(StatusHudY, 0.0, 1.0) : 0.76;
        PeopleSaved = Math.Max(0, PeopleSaved);
        AutoTargetCooldownSeconds = double.IsFinite(AutoTargetCooldownSeconds)
            ? Math.Clamp(AutoTargetCooldownSeconds, 0.5, 30.0)
            : 3.0;
        TogglePluginHotkey = NormalizeHotkey(TogglePluginHotkey);
        FocusTargetHotkey = NormalizeHotkey(FocusTargetHotkey);
        ToggleModeHotkey = NormalizeHotkey(ToggleModeHotkey);
        TargetCriticalHotkey = NormalizeHotkey(TargetCriticalHotkey);
    }

    private static HotkeyBinding NormalizeHotkey(HotkeyBinding? binding) =>
        binding is null || binding.VirtualKey <= 0
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
