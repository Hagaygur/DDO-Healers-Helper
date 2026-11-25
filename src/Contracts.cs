using System;
using System.Collections.Generic;
using System.Globalization;

namespace VoK.HealersHelper;

public enum TrackingMode
{
    Party = 0,
    SinglePlayer = 1,
    Off = 2
}

public enum TargetMethod
{
    Off = 0,
    Auto = 1,
    SdkSelectOnly = 2,
    PartyKeyOnly = 3
}

public enum BannerSeverity
{
    None = 0,
    Notice = 1,
    LowHp = 2,
    CriticalDown = 3
}

public sealed record HotkeyBinding(int VirtualKey, bool Ctrl, bool Shift, bool Alt)
{
    public static readonly HotkeyBinding Empty = new(0, false, false, false);

    public bool IsEmpty => VirtualKey <= 0;

    public string Format()
    {
        if (IsEmpty) return "None";
        var parts = new List<string>(4);
        if (Ctrl) parts.Add("Ctrl");
        if (Shift) parts.Add("Shift");
        if (Alt) parts.Add("Alt");
        parts.Add(FormatVirtualKey(VirtualKey));
        return string.Join("+", parts);
    }

    private static string FormatVirtualKey(int vk)
    {
        if (vk is >= 0x41 and <= 0x5A) return ((char)vk).ToString();
        if (vk is >= 0x30 and <= 0x39) return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return $"F{vk - 0x6F}";
        if (vk is >= 0x60 and <= 0x69) return $"Num{vk - 0x60}";
        return vk switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x1B => "Esc",
            0x20 => "Space",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2D => "Insert",
            0x2E => "Delete",
            0x6A => "Num*",
            0x6B => "Num+",
            0x6D => "Num-",
            0x6E => "Num.",
            0x6F => "Num/",
            0xBA => ";",
            0xBB => "=",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xDE => "'",
            _ => string.Create(CultureInfo.InvariantCulture, $"VK_0x{vk:X2}")
        };
    }
}

internal sealed record WatchedMemberSnapshot(
    ulong Id,
    string Name,
    int HitPoints,
    int MaxHitPoints,
    int PartySlotIndex,
    bool Connected,
    bool IsDeadOrDown,
    bool IsTrulyDead = false)
{
    public double Percent => MaxHitPoints > 0
        ? Math.Clamp(HitPoints * 100.0 / MaxHitPoints, -999, 100)
        : 100;
}

internal sealed record AlertBannerView(
    BannerSeverity Severity,
    TrackingMode Mode,
    string PrimaryText,
    string SecondaryText,
    int LowMemberCount,
    ulong? AlertPlayerId,
    string AlertPlayerName,
    double? AlertPercent,
    long AlertGeneration,
    string StatusLine,
    string TargetingStatus,
    bool PluginEnabled = true,
    int PeopleSaved = 0);

internal interface IHealerEngineView
{
    IntPtr GameWindow { get; }
    int SettingsRevision { get; }
    int PeopleSaved { get; }
    Settings GetSettings();
    void SaveSettings(Settings settings);
    void SavePosition(double x, double y);
    void SaveBannerLayout(double x, double y, int fontSizePixels, int bannerOpacity);
    void SaveStatusPosition(double x, double y);
    AlertBannerView View();
    WatchedMemberSnapshot[] GetMembers();
    void TogglePluginEnabled();
    void ToggleBannerHud();
    void ResetPeopleSaved();
    string GetChatScorecard();
    bool CopyChatScorecard();
    bool FocusCurrentTarget();
    void CycleMode();
    bool TargetCriticalNow();
    void Report(string message);
}

internal enum TargetActionKind
{
    None = 0,
    SdkSelectObject = 1,
    PartyKeybind = 2
}

internal sealed record TargetDecision(
    TargetActionKind Action,
    ulong TargetId,
    int PartySlotIndex,
    string PartyActionName,
    string Status);

// Pure portable decision rules for target selection so both TargetSelector and
// cross-platform unit tests evaluate the exact same validation & cooldown logic.
internal static class TargetPolicy
{
    public static string? PartyActionForSlot(int partySlotIndex) => partySlotIndex switch
    {
        0 => "TARGET_SELF_KEY",
        >= 1 and <= 12 => string.Create(CultureInfo.InvariantCulture, $"TARGET_FELLOW_{partySlotIndex}_KEY"),
        _ => null
    };

    public static TargetDecision Evaluate(
        ulong targetId,
        string targetName,
        int partySlotIndex,
        bool entityInRange,
        bool partyKeyBound,
        ulong currentSelectedId,
        TargetMethod method,
        bool bypassCooldown,
        double cooldownRemainingSeconds)
    {
        if (method == TargetMethod.Off)
            return new(TargetActionKind.None, 0, partySlotIndex, "", "Targeting off");

        if (targetId == 0 || targetId == ulong.MaxValue)
            return new(TargetActionKind.None, 0, partySlotIndex, "", "No valid target ID");

        if (currentSelectedId == targetId)
            return new(TargetActionKind.None, targetId, partySlotIndex, "", $"Already targeting {targetName}");

        if (!bypassCooldown && cooldownRemainingSeconds > 0)
            return new(TargetActionKind.None, targetId, partySlotIndex, "",
                string.Create(CultureInfo.InvariantCulture, $"Cooldown ({cooldownRemainingSeconds:0.0}s)"));

        string? actionName = PartyActionForSlot(partySlotIndex);

        switch (method)
        {
            case TargetMethod.SdkSelectOnly:
                if (!entityInRange)
                    return new(TargetActionKind.None, targetId, partySlotIndex, "", $"{targetName} out of entity range");
                return new(TargetActionKind.SdkSelectObject, targetId, partySlotIndex, "",
                    $"Selected {targetName} (SDK)");

            case TargetMethod.PartyKeyOnly:
                if (actionName is null)
                    return new(TargetActionKind.None, targetId, partySlotIndex, "", $"No party key slot for {targetName}");
                if (!partyKeyBound)
                    return new(TargetActionKind.None, targetId, partySlotIndex, actionName,
                        $"{actionName} unbound in ddo.keymap");
                return new(TargetActionKind.PartyKeybind, targetId, partySlotIndex, actionName,
                    $"Selected {targetName} ({actionName})");

            case TargetMethod.Auto:
                if (entityInRange)
                    return new(TargetActionKind.SdkSelectObject, targetId, partySlotIndex, "",
                        $"Selected {targetName} (SDK)");
                if (actionName is not null && partyKeyBound)
                    return new(TargetActionKind.PartyKeybind, targetId, partySlotIndex, actionName,
                        $"Selected {targetName} ({actionName})");
                string reason = actionName is null
                    ? $"{targetName} out of entity range"
                    : $"{targetName} out of range ({actionName} unbound)";
                return new(TargetActionKind.None, targetId, partySlotIndex, actionName ?? "", reason);

            default:
                return new(TargetActionKind.None, 0, partySlotIndex, "", "Targeting off");
        }
    }

    public static TargetDecision EvaluateRestore(
        ulong targetId,
        string targetName,
        int partySlotIndex,
        bool entityInRange,
        bool isDeadNonPlayer,
        bool partyKeyBound,
        ulong currentSelectedId,
        TargetMethod method)
    {
        if (method == TargetMethod.Off)
            return new(TargetActionKind.None, 0, partySlotIndex, "", "Targeting off");

        if (targetId == 0 || targetId == ulong.MaxValue)
            return new(TargetActionKind.None, 0, partySlotIndex, "", "No valid target ID");

        string displayName = string.IsNullOrWhiteSpace(targetName) ? "previous target" : targetName;

        if (currentSelectedId == targetId)
            return new(TargetActionKind.None, targetId, partySlotIndex, "", $"Already targeting {displayName}");

        if (isDeadNonPlayer)
            return new(TargetActionKind.None, targetId, partySlotIndex, "", $"Original target {displayName} is dead");

        string? actionName = PartyActionForSlot(partySlotIndex);

        switch (method)
        {
            case TargetMethod.SdkSelectOnly:
                if (entityInRange)
                    return new(TargetActionKind.SdkSelectObject, targetId, partySlotIndex, "", $"Restored target: {displayName}");
                return new(TargetActionKind.None, targetId, partySlotIndex, "", $"Original target {displayName} unavailable");

            case TargetMethod.PartyKeyOnly:
                if (actionName is not null && partyKeyBound)
                    return new(TargetActionKind.PartyKeybind, targetId, partySlotIndex, actionName,
                        $"Restored target: {displayName} ({actionName})");
                return new(TargetActionKind.None, targetId, partySlotIndex, actionName ?? "",
                    $"Original target {displayName} unavailable");

            case TargetMethod.Auto:
                if (entityInRange)
                    return new(TargetActionKind.SdkSelectObject, targetId, partySlotIndex, "", $"Restored target: {displayName}");
                if (actionName is not null && partyKeyBound)
                    return new(TargetActionKind.PartyKeybind, targetId, partySlotIndex, actionName,
                        $"Restored target: {displayName} ({actionName})");
                return new(TargetActionKind.None, targetId, partySlotIndex, actionName ?? "",
                    $"Original target {displayName} unavailable");

            default:
                return new(TargetActionKind.None, 0, partySlotIndex, "", "Targeting off");
        }
    }
}
