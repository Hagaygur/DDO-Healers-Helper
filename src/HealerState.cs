using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VoK.HealersHelper;

// Pure deterministic state machine. All times are monotonic seconds supplied by the caller.
// No SDK, WinForms, Win32, or wall-clock dependencies so tests run on Windows and Linux.
internal sealed class HealerState
{
    private sealed record DeadTrack(double FirstDownAt, bool Suppressed);

    private readonly HashSet<ulong> _lowHpActive = new();
    private readonly Dictionary<ulong, DeadTrack> _deadTracks = new();

    private long _alertGeneration;
    private bool _wasAlerting;
    private ulong? _lastAlertPlayerId;
    private BannerSeverity _lastAlertSeverity;

    private string _noticePrimary = "";
    private string _noticeSecondary = "";
    private double _noticeUntil;

    public void Reset()
    {
        _lowHpActive.Clear();
        _deadTracks.Clear();
        _wasAlerting = false;
        _lastAlertPlayerId = null;
        _lastAlertSeverity = BannerSeverity.None;
        _noticePrimary = "";
        _noticeSecondary = "";
        _noticeUntil = 0;
    }

    public void SetNotice(string primary, string secondary, double durationSeconds, double now)
    {
        _noticePrimary = primary ?? "";
        _noticeSecondary = secondary ?? "";
        _noticeUntil = now + Math.Max(0, durationSeconds);
    }

    public AlertBannerView Evaluate(
        WatchedMemberSnapshot[] members,
        Settings settings,
        double now,
        string statusLine = "",
        string targetingStatus = "")
    {
        members ??= Array.Empty<WatchedMemberSnapshot>();
        int peopleSaved = Math.Max(0, settings.PeopleSaved);

        if (!settings.PluginEnabled)
        {
            _lowHpActive.Clear();
            _deadTracks.Clear();
            ClearAlertEpisode();
            _noticePrimary = "";
            _noticeSecondary = "";
            _noticeUntil = 0;
            string offStatus = string.IsNullOrWhiteSpace(statusLine) ? "Plugin Off" : statusLine;
            return new AlertBannerView(
                BannerSeverity.None,
                settings.Mode,
                "",
                "",
                0,
                null,
                "",
                null,
                _alertGeneration,
                offStatus,
                targetingStatus ?? "",
                false,
                peopleSaved);
        }

        if (settings.Mode == TrackingMode.Off)
        {
            _lowHpActive.Clear();
            _deadTracks.Clear();
            ClearAlertEpisode();
            string offStatus = string.IsNullOrWhiteSpace(statusLine) ? "Tracking Off" : statusLine;
            return BuildIdleOrNoticeView(settings.Mode, now, offStatus, targetingStatus, true, peopleSaved);
        }

        List<WatchedMemberSnapshot> watched = FilterWatched(members, settings);
        var presentIds = new HashSet<ulong>(members.Length);
        foreach (var m in members)
        {
            if (m.Id != 0) presentIds.Add(m.Id);
        }

        var watchedIds = new HashSet<ulong>(watched.Count);
        foreach (var m in watched)
        {
            if (m.Id != 0) watchedIds.Add(m.Id);
        }

        PruneMissing(_lowHpActive, watchedIds);
        PruneMissing(_deadTracks, presentIds);

        WatchedMemberSnapshot? bestCandidate = null;
        BannerSeverity bestSeverity = BannerSeverity.None;
        int lowCount = 0;

        foreach (var member in watched)
        {
            if (!member.Connected)
            {
                _lowHpActive.Remove(member.Id);
                continue;
            }

            bool isDown = member.IsDeadOrDown || (member.MaxHitPoints > 0 && member.HitPoints <= 0);
            if (isDown)
            {
                _lowHpActive.Remove(member.Id);
                if (!_deadTracks.TryGetValue(member.Id, out var dead))
                {
                    dead = new DeadTrack(now, settings.DeadAlertSeconds <= 0);
                    _deadTracks[member.Id] = dead;
                }
                else if (!dead.Suppressed && (now - dead.FirstDownAt) >= settings.DeadAlertSeconds)
                {
                    dead = dead with { Suppressed = true };
                    _deadTracks[member.Id] = dead;
                }

                if (!dead.Suppressed)
                {
                    lowCount++;
                    if (IsHigherPriority(member, BannerSeverity.CriticalDown, bestCandidate, bestSeverity))
                    {
                        bestCandidate = member;
                        bestSeverity = BannerSeverity.CriticalDown;
                    }
                }
                continue;
            }

            // Player is alive with HP > 0 (or no max HP available yet): clear dead suppression.
            _deadTracks.Remove(member.Id);

            if (member.MaxHitPoints <= 0)
            {
                _lowHpActive.Remove(member.Id);
                continue;
            }

            double pct = member.Percent;
            bool wasLow = _lowHpActive.Contains(member.Id);
            bool isLow = EvaluateLowHpHysteresis(pct, wasLow, settings.HpThresholdPercent, settings.RecoveryHysteresisPercent);

            if (isLow)
            {
                _lowHpActive.Add(member.Id);
                lowCount++;
                if (IsHigherPriority(member, BannerSeverity.LowHp, bestCandidate, bestSeverity))
                {
                    bestCandidate = member;
                    bestSeverity = BannerSeverity.LowHp;
                }
            }
            else
            {
                _lowHpActive.Remove(member.Id);
            }
        }

        if (bestCandidate is null || bestSeverity == BannerSeverity.None)
        {
            ClearAlertEpisode();
            string defaultStatus = BuildDefaultStatus(watched, settings, statusLine);
            return BuildIdleOrNoticeView(settings.Mode, now, defaultStatus, targetingStatus, true, peopleSaved);
        }

        if (!_wasAlerting || _lastAlertPlayerId != bestCandidate.Id || _lastAlertSeverity != bestSeverity)
        {
            _alertGeneration++;
            _wasAlerting = true;
            _lastAlertPlayerId = bestCandidate.Id;
            _lastAlertSeverity = bestSeverity;
        }

        string primary = bestSeverity == BannerSeverity.CriticalDown
            ? $"DOWN: {bestCandidate.Name}"
            : string.Create(CultureInfo.InvariantCulture, $"HEAL {bestCandidate.Name} ({bestCandidate.Percent:0}%)");

        string hpDetail = bestCandidate.MaxHitPoints > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{bestCandidate.HitPoints}/{bestCandidate.MaxHitPoints} HP")
            : "Incapacitated / Dead";

        string extra = lowCount > 1
            ? string.Create(CultureInfo.InvariantCulture, $" • {lowCount} low")
            : "";

        string secondary = bestSeverity == BannerSeverity.CriticalDown
            ? $"{hpDetail} — CRITICAL{extra}"
            : $"{hpDetail}{extra}";

        string activeStatus = string.IsNullOrWhiteSpace(statusLine)
            ? string.Create(CultureInfo.InvariantCulture, $"{bestCandidate.Name} {bestCandidate.Percent:0}%")
            : statusLine;

        return new AlertBannerView(
            bestSeverity,
            settings.Mode,
            primary,
            secondary,
            lowCount,
            bestCandidate.Id,
            bestCandidate.Name,
            bestCandidate.Percent,
            _alertGeneration,
            activeStatus,
            targetingStatus ?? "",
            true,
            peopleSaved);
    }

    private static bool EvaluateLowHpHysteresis(double percent, bool currentlyLow, int thresholdPercent, int hysteresisPercent)
    {
        if (!currentlyLow)
            return percent <= thresholdPercent;

        if (hysteresisPercent <= 0)
            return percent <= thresholdPercent;

        int recoverAt = Math.Min(100, thresholdPercent + hysteresisPercent);
        return percent < recoverAt;
    }

    private static List<WatchedMemberSnapshot> FilterWatched(WatchedMemberSnapshot[] members, Settings settings)
    {
        var list = new List<WatchedMemberSnapshot>(members.Length);
        if (settings.Mode == TrackingMode.Party)
        {
            foreach (var m in members)
            {
                if (m is not null && m.Id != 0) list.Add(m);
            }
            return list;
        }

        if (settings.Mode == TrackingMode.SinglePlayer)
        {
            if (settings.FocusedPlayerId > 0)
            {
                foreach (var m in members)
                {
                    if (m is not null && m.Id == settings.FocusedPlayerId)
                    {
                        list.Add(m);
                        return list;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(settings.FocusedPlayerName))
            {
                foreach (var m in members)
                {
                    if (m is not null && string.Equals(m.Name, settings.FocusedPlayerName, StringComparison.OrdinalIgnoreCase))
                    {
                        list.Add(m);
                        return list;
                    }
                }
            }
        }

        return list;
    }

    private static bool IsHigherPriority(
        WatchedMemberSnapshot candidate,
        BannerSeverity candidateSeverity,
        WatchedMemberSnapshot? currentBest,
        BannerSeverity currentSeverity)
    {
        if (currentBest is null) return true;
        if (candidateSeverity != currentSeverity) return candidateSeverity > currentSeverity;

        int pctCmp = candidate.Percent.CompareTo(currentBest.Percent);
        if (pctCmp != 0) return pctCmp < 0;

        int slotCmp = candidate.PartySlotIndex.CompareTo(currentBest.PartySlotIndex);
        if (slotCmp != 0) return slotCmp < 0;

        return candidate.Id < currentBest.Id;
    }

    private void ClearAlertEpisode()
    {
        _wasAlerting = false;
        _lastAlertPlayerId = null;
        _lastAlertSeverity = BannerSeverity.None;
    }

    private AlertBannerView BuildIdleOrNoticeView(
        TrackingMode mode,
        double now,
        string statusLine,
        string targetingStatus,
        bool pluginEnabled,
        int peopleSaved)
    {
        if (now < _noticeUntil && !string.IsNullOrWhiteSpace(_noticePrimary))
        {
            return new AlertBannerView(
                BannerSeverity.Notice,
                mode,
                _noticePrimary,
                _noticeSecondary,
                0,
                null,
                "",
                null,
                _alertGeneration,
                statusLine,
                targetingStatus ?? "",
                pluginEnabled,
                peopleSaved);
        }

        return new AlertBannerView(
            BannerSeverity.None,
            mode,
            "",
            "",
            0,
            null,
            "",
            null,
            _alertGeneration,
            statusLine,
            targetingStatus ?? "",
            pluginEnabled,
            peopleSaved);
    }

    private static string BuildDefaultStatus(List<WatchedMemberSnapshot> watched, Settings settings, string statusLine)
    {
        if (!string.IsNullOrWhiteSpace(statusLine)) return statusLine;
        if (settings.Mode == TrackingMode.SinglePlayer)
        {
            if (watched.Count == 0)
            {
                return string.IsNullOrWhiteSpace(settings.FocusedPlayerName)
                    ? "Single-Player: no focus target set"
                    : $"Waiting for {settings.FocusedPlayerName}";
            }
            return string.Create(CultureInfo.InvariantCulture,
                $"Watching {watched[0].Name} (<= {settings.HpThresholdPercent}%)");
        }
        return string.Create(CultureInfo.InvariantCulture,
            $"Party ({watched.Count}) • Alert <= {settings.HpThresholdPercent}%");
    }

    private static void PruneMissing(HashSet<ulong> set, HashSet<ulong> keep)
    {
        foreach (ulong id in set.Where(id => !keep.Contains(id)).ToArray())
            set.Remove(id);
    }

    private static void PruneMissing<TValue>(Dictionary<ulong, TValue> dict, HashSet<ulong> keep)
    {
        foreach (ulong id in dict.Keys.Where(id => !keep.Contains(id)).ToArray())
            dict.Remove(id);
    }
}
