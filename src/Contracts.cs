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
    bool IsTrulyDead = false,
    int TempHitPoints = 0,
    int SpellPoints = 0,
    int MaxSpellPoints = 0)
{
    public double Percent => MaxHitPoints > 0
        ? Math.Clamp(HitPoints * 100.0 / MaxHitPoints, -999, 100)
        : 100;

    public bool IsIncapacitated => (IsDeadOrDown || (MaxHitPoints > 0 && HitPoints <= 0)) && !IsTrulyDead;

    public double EffectivePercent(bool includeTempHp)
    {
        if (!includeTempHp || TempHitPoints <= 0 || HitPoints <= 0 || IsDeadOrDown || MaxHitPoints <= 0)
            return Percent;

        return Math.Clamp(((long)HitPoints + TempHitPoints) * 100.0 / MaxHitPoints, -999, 100);
    }
}

// Pure deterministic life/death status evaluation shared by DH 4, DH 5, and unit tests.
// In DDO, unconscious range scales well past -10 HP via Diehard, feats, enhancements,
// Epic Past Lives, and gear. Authoritative live LifeState (Incapacitated vs Dead) always
// takes precedence when in entity range; out-of-bubble fallback uses sticky confirmed-dead
// tracking, cached HitPoints_IncapacitatedBonus (-10 - bonus), or configurable FallbackDeathHp.
internal static class LifeStatusEvaluator
{
    public const int LifeStateUndef = 0;
    public const int LifeStateDead = 2;
    public const int LifeStateIncapacitated = 3;
    public const int LifeStateAlive = 4;

    public const uint DeathLifeStatePropertyId = 0x10000E0Cu;
    public const uint DeathSoulStoneIdPropertyId = 0x10001868u;
    public const uint HitPointsIncapacitatedBonusPropertyId = 0x10008250u;

    public const int DefaultFallbackDeathHp = -200;
    public const int MinFallbackDeathHp = -5000;
    public const int MaxFallbackDeathHp = 0;

    public static (bool IsDown, bool IsTrulyDead) EvaluateMemberLifeStatus(
        bool connected,
        int hp,
        int maxHp,
        bool hasLiveEntity,
        int? entityLifeStateRaw,
        bool hasSoulstoneProperty,
        bool wasConfirmedDead,
        int? cachedIncapBonus,
        int fallbackDeathHp)
    {
        if (!connected)
            return (false, false);

        if (hasLiveEntity && entityLifeStateRaw is int rawLifeState)
        {
            switch (rawLifeState)
            {
                case LifeStateDead:
                    return (true, true);
                case LifeStateIncapacitated:
                    return (true, false);
                case LifeStateAlive:
                    return (maxHp > 0 && hp <= 0, false);
            }
        }

        if (hasLiveEntity && hasSoulstoneProperty && (maxHp <= 0 || hp <= 0))
            return (true, true);

        if (maxHp <= 0 || hp > 0)
            return (false, false);

        if (wasConfirmedDead)
            return (true, true);

        if (cachedIncapBonus is >= 0)
        {
            long deathCutoff = -10L - cachedIncapBonus.Value;
            return (true, hp <= deathCutoff);
        }

        if (fallbackDeathHp < 0)
            return (true, hp <= fallbackDeathHp);

        return (true, false);
    }
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
    int PeopleSaved = 0)
{
    public bool IsSoulstone { get; init; }
    public int AlertTempHitPoints { get; init; }
    public double? AlertEffectivePercent { get; init; }
}

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
    bool IsPlayerMuted(ulong id, string? name) => false;
    bool ToggleMutePlayer(ulong id, string? name) => false;
    void ClearMutedPlayers() { }
    IReadOnlyCollection<string> GetMutedPlayerNames() => Array.Empty<string>();
    string GetRaidKeymapSummary() => "Raid F-keys (7-12): offline (SDK SelectObject handles all 12)";
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

    public static (int BoundCount, int TotalCount, string Summary) EvaluateRaidKeymapReadiness(Func<string, bool>? isActionBound)
    {
        const int total = 6; // Slots 7..12
        if (isActionBound is null)
            return (0, total, "Raid F-keys (7-12): offline (SDK SelectObject handles all 12)");

        int bound = 0;
        for (int slot = 7; slot <= 12; slot++)
        {
            string action = PartyActionForSlot(slot)!;
            if (isActionBound(action))
                bound++;
        }

        string summary = bound == total
            ? $"Raid F-keys (7-12): {bound}/{total} bound (full keymap fallback ready)"
            : string.Create(CultureInfo.InvariantCulture,
                $"Raid F-keys (7-12): {bound}/{total} bound (SDK SelectObject covers out-of-keymap slots in range)");
        return (bound, total, summary);
    }

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
