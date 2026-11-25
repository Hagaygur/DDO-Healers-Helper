using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace VoK.HealersHelper;

internal readonly record struct ClutchSaveEvent(
    ulong PlayerId,
    string PlayerName,
    int PreHealHp,
    int PreHealMaxHp,
    double PreHealPercent,
    int OverkillSaved);

// Pure portable gamification helpers & trackers (streaks, prestige ranks, reflex timer,
// closest call / 1% club records, and ASCII DDO chat scorecard formatting).
internal static class HealerStats
{
    public const int MaxScorecardLength = 185;
    public const int MinReflexMs = 50;
    public const int MaxReflexMs = 10000;
    public const double OnePercentClubThreshold = 5.0;

    public static string RankForSaves(int saves) => saves switch
    {
        <= 0 => "Initiate",
        <= 4 => "Field Medic",
        <= 14 => "Combat Healer",
        <= 29 => "Lifeweaver",
        <= 59 => "Beacon of Hope",
        <= 99 => "Soulguard",
        <= 249 => "Reaper's Bane",
        _ => "Immortal Anchor"
    };

    public static void ApplyClutchSave(Settings settings, ClutchSaveEvent save)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.PeopleSaved = (int)Math.Min(int.MaxValue, (long)Math.Max(0, settings.PeopleSaved) + 1L);
        settings.CurrentSaveStreak = (int)Math.Min(int.MaxValue, (long)Math.Max(0, settings.CurrentSaveStreak) + 1L);
        settings.BestSaveStreak = Math.Max(Math.Max(0, settings.BestSaveStreak), settings.CurrentSaveStreak);

        if (save.OverkillSaved > settings.BiggestOverkillSaved)
        {
            settings.BiggestOverkillSaved = save.OverkillSaved;
        }

        if (save.PreHealMaxHp > 0 && save.PreHealPercent <= OnePercentClubThreshold)
        {
            settings.OnePercentClubSaves = (int)Math.Min(int.MaxValue, (long)Math.Max(0, settings.OnePercentClubSaves) + 1L);
        }

        if (save.PreHealHp > 0 && !string.IsNullOrWhiteSpace(save.PlayerName))
        {
            double roundedPct = Math.Round(Math.Clamp(save.PreHealPercent, 0.0, 100.0), 2);
            bool hasExisting = settings.ClosestCallPreHealHp > 0 && !string.IsNullOrWhiteSpace(settings.ClosestCallPlayerName);
            bool beatsPercent = roundedPct < settings.ClosestCallPreHealPercent - 1e-6;
            bool tiesPercentWithLowerHp = Math.Abs(roundedPct - settings.ClosestCallPreHealPercent) <= 1e-6
                && save.PreHealHp < settings.ClosestCallPreHealHp;

            if (!hasExisting || beatsPercent || tiesPercentWithLowerHp)
            {
                settings.ClosestCallPlayerName = save.PlayerName.Trim();
                settings.ClosestCallPreHealPercent = roundedPct;
                settings.ClosestCallPreHealHp = save.PreHealHp;
            }
        }
    }

    public static string FormatChatScorecard(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        int saved = Math.Max(0, settings.PeopleSaved);
        int currStreak = Math.Max(0, settings.CurrentSaveStreak);
        int bestStreak = Math.Max(Math.Max(0, settings.BestSaveStreak), currStreak);
        string rank = RankForSaves(saved);

        var sb = new StringBuilder(MaxScorecardLength);
        sb.Append(string.Create(
            CultureInfo.InvariantCulture,
            $"[Healer's Helper] {rank}: {saved} saved | Streak {currStreak} (Best {bestStreak})"));

        void TryAppendSegment(string segment)
        {
            string clean = SanitizePrintableAscii(segment, MaxScorecardLength);
            if (clean.Length == 0) return;
            if (sb.Length + 3 + clean.Length <= MaxScorecardLength)
            {
                sb.Append(" | ");
                sb.Append(clean);
            }
        }

        int onePct = Math.Max(0, settings.OnePercentClubSaves);
        if (onePct > 0)
        {
            TryAppendSegment(string.Create(CultureInfo.InvariantCulture, $"1% Club: {onePct}"));
        }

        string asciiClosestName = SanitizePrintableAscii(settings.ClosestCallPlayerName, 12);
        if (settings.ClosestCallPreHealHp > 0 && asciiClosestName.Length > 0)
        {
            double pct = double.IsFinite(settings.ClosestCallPreHealPercent)
                ? Math.Clamp(settings.ClosestCallPreHealPercent, 0.0, 100.0)
                : 0.0;
            TryAppendSegment(string.Create(
                CultureInfo.InvariantCulture,
                $"Closest: {asciiClosestName} {pct:0.#}% ({settings.ClosestCallPreHealHp}HP)"));
        }

        if (settings.ReflexSampleCount > 0 && settings.BestReflexMs > 0 && settings.AvgReflexMs > 0)
        {
            int avg = Math.Clamp(settings.AvgReflexMs, 1, MaxReflexMs);
            int best = Math.Clamp(settings.BestReflexMs, 1, MaxReflexMs);
            TryAppendSegment(string.Create(CultureInfo.InvariantCulture, $"Reflex {avg}ms ({best}ms best)"));
        }

        int overkill = Math.Max(0, settings.BiggestOverkillSaved);
        if (overkill > 0)
        {
            TryAppendSegment(string.Create(CultureInfo.InvariantCulture, $"Max Save +{overkill}HP"));
        }

        string finalCard = SanitizePrintableAscii(sb.ToString(), MaxScorecardLength);
        return finalCard.Length > 0 ? finalCard : "[Healer's Helper] Initiate: 0 saved";
    }

    public static string SanitizePrintableAscii(string? raw, int maxLength)
    {
        if (string.IsNullOrEmpty(raw) || maxLength <= 0) return "";
        var sb = new StringBuilder(Math.Min(raw.Length, maxLength));
        bool lastWasSpace = true;
        foreach (char c in raw)
        {
            if (c is ' ' or '\t' or '\r' or '\n')
            {
                if (!lastWasSpace && sb.Length < maxLength)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
                continue;
            }

            if (c >= 0x21 && c <= 0x7E)
            {
                if (sb.Length >= maxLength) break;
                sb.Append(c);
                lastWasSpace = false;
            }
        }

        while (sb.Length > 0 && sb[^1] == ' ')
        {
            sb.Length--;
        }

        return sb.ToString();
    }
}

internal sealed class StreakTracker
{
    private readonly HashSet<ulong> _trulyDeadIds = new();

    public void Reset() => _trulyDeadIds.Clear();

    public bool ProcessWatched(
        IReadOnlyList<WatchedMemberSnapshot> allMembers,
        IReadOnlyList<WatchedMemberSnapshot> watched,
        bool pluginEnabled,
        bool inTown,
        Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!pluginEnabled)
        {
            Reset();
            return false;
        }

        var presentIds = new HashSet<ulong>(allMembers.Count);
        foreach (var m in allMembers)
        {
            if (m is not null && m.Id != 0)
            {
                presentIds.Add(m.Id);
            }
        }

        foreach (ulong id in _trulyDeadIds.Where(id => !presentIds.Contains(id)).ToArray())
        {
            _trulyDeadIds.Remove(id);
        }

        bool streakReset = false;
        foreach (var member in watched)
        {
            if (member is null || member.Id == 0 || !member.Connected)
            {
                continue;
            }

            if (member.IsTrulyDead)
            {
                if (_trulyDeadIds.Add(member.Id) && !inTown && settings.CurrentSaveStreak > 0)
                {
                    settings.CurrentSaveStreak = 0;
                    streakReset = true;
                }
            }
            else if (!member.IsDeadOrDown && member.HitPoints > 0)
            {
                _trulyDeadIds.Remove(member.Id);
            }
        }

        return streakReset;
    }
}

internal sealed class ReflexTracker
{
    private bool _hasActiveAlert;
    private long _activeAlertGeneration;
    private double _alertStartAt = double.NegativeInfinity;
    private bool _respondedToCurrentAlert;

    public void Reset()
    {
        _hasActiveAlert = false;
        _activeAlertGeneration = 0;
        _alertStartAt = double.NegativeInfinity;
        _respondedToCurrentAlert = false;
    }

    public void OnAlertEvaluated(AlertBannerView view, double now)
    {
        if (view is null
            || !view.PluginEnabled
            || view.Mode == TrackingMode.Off
            || view.Severity is not (BannerSeverity.LowHp or BannerSeverity.CriticalDown))
        {
            _hasActiveAlert = false;
            _respondedToCurrentAlert = false;
            _alertStartAt = double.NegativeInfinity;
            return;
        }

        if (!_hasActiveAlert || view.AlertGeneration != _activeAlertGeneration)
        {
            _hasActiveAlert = true;
            _activeAlertGeneration = view.AlertGeneration;
            _alertStartAt = now;
            _respondedToCurrentAlert = false;
        }
    }

    public int? TryRecordSelfCast(double castAt, Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!_hasActiveAlert
            || _respondedToCurrentAlert
            || !double.IsFinite(castAt)
            || !double.IsFinite(_alertStartAt)
            || castAt < _alertStartAt)
        {
            return null;
        }

        double deltaMs = (castAt - _alertStartAt) * 1000.0;
        if (deltaMs > HealerStats.MaxReflexMs)
        {
            _respondedToCurrentAlert = true;
            return null;
        }

        int reflexMs = (int)Math.Round(deltaMs);
        if (reflexMs < HealerStats.MinReflexMs)
        {
            // Sub-50ms pre-cast does not latch _respondedToCurrentAlert so actual reaction still records.
            return null;
        }

        _respondedToCurrentAlert = true;
        int prevCount = Math.Max(0, settings.ReflexSampleCount);
        long nextCount = (long)prevCount + 1L;
        settings.BestReflexMs = (prevCount == 0 || settings.BestReflexMs <= 0)
            ? reflexMs
            : Math.Min(settings.BestReflexMs, reflexMs);
        long prevAvg = Math.Clamp(settings.AvgReflexMs, 0, HealerStats.MaxReflexMs);
        settings.AvgReflexMs = (int)Math.Clamp(
            (long)Math.Round((prevAvg * prevCount + reflexMs) / (double)nextCount),
            HealerStats.MinReflexMs,
            HealerStats.MaxReflexMs);
        settings.ReflexSampleCount = (int)Math.Min(int.MaxValue, nextCount);
        return reflexMs;
    }
}
