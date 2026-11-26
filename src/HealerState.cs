using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VoK.HealersHelper;

// Pure deterministic state machine. All times are monotonic seconds supplied by the caller.
// No SDK, WinForms, Win32, or wall-clock dependencies so tests run on Windows and Linux.
internal sealed class HealerState
{
    private enum TriageTier
    {
        None = 0,
        SoulstoneDead = 1,
        LivingLowHp = 2,
        Incapacitated = 3
    }

    private sealed record DeadTrack(double FirstDownAt, bool Suppressed, bool WasSoulstone);

    private readonly HashSet<ulong> _lowHpActive = new();
    private readonly Dictionary<ulong, double> _lowHpFirstAt = new();
    private readonly Dictionary<ulong, DeadTrack> _deadTracks = new();

    private long _alertGeneration;
    private bool _wasAlerting;
    private ulong? _lastAlertPlayerId;
    private BannerSeverity _lastAlertSeverity;
    private bool _lastAlertSoulstone;

    private string _noticePrimary = "";
    private string _noticeSecondary = "";
    private double _noticeUntil;

    public void Reset()
    {
        _lowHpActive.Clear();
        _lowHpFirstAt.Clear();
        _deadTracks.Clear();
        _wasAlerting = false;
        _lastAlertPlayerId = null;
        _lastAlertSeverity = BannerSeverity.None;
        _lastAlertSoulstone = false;
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
        string targetingStatus = "",
        bool inTown = false,
        Func<WatchedMemberSnapshot, bool>? isMuted = null)
    {
        members ??= Array.Empty<WatchedMemberSnapshot>();
        int peopleSaved = Math.Max(0, settings.PeopleSaved);

        if (!settings.PluginEnabled)
        {
            _lowHpActive.Clear();
            _lowHpFirstAt.Clear();
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
            _lowHpFirstAt.Clear();
            _deadTracks.Clear();
            ClearAlertEpisode();
            string offStatus = string.IsNullOrWhiteSpace(statusLine) ? "Tracking Off" : statusLine;
            return BuildIdleOrNoticeView(settings.Mode, now, offStatus, targetingStatus, true, peopleSaved);
        }

        if (settings.SuppressAlertsInTown && inTown)
        {
            _lowHpActive.Clear();
            _lowHpFirstAt.Clear();
            _deadTracks.Clear();
            ClearAlertEpisode();
            string townStatus = string.IsNullOrWhiteSpace(statusLine) ? "In Town (alerts paused)" : statusLine;
            return BuildIdleOrNoticeView(settings.Mode, now, townStatus, targetingStatus, true, peopleSaved);
        }

        List<WatchedMemberSnapshot> watched = FilterWatched(members, settings, isMuted);
        var presentIds = new HashSet<ulong>(members.Length);
        foreach (var m in members)
        {
            if (m is not null && m.Id != 0) presentIds.Add(m.Id);
        }

        var watchedIds = new HashSet<ulong>(watched.Count);
        foreach (var m in watched)
        {
            if (m.Id != 0) watchedIds.Add(m.Id);
        }

        PruneMissing(_lowHpActive, watchedIds);
        PruneMissing(_lowHpFirstAt, watchedIds);
        PruneMissing(_deadTracks, presentIds);

        WatchedMemberSnapshot? bestCandidate = null;
        TriageTier bestTier = TriageTier.None;
        int lowCount = 0;

        foreach (var member in watched)
        {
            if (!member.Connected)
            {
                _lowHpActive.Remove(member.Id);
                _lowHpFirstAt.Remove(member.Id);
                continue;
            }

            bool isDown = member.IsDeadOrDown || member.IsTrulyDead || (member.MaxHitPoints > 0 && member.HitPoints <= 0);
            if (isDown)
            {
                _lowHpActive.Remove(member.Id);
                _lowHpFirstAt.Remove(member.Id);
                if (!_deadTracks.TryGetValue(member.Id, out var dead))
                {
                    dead = new DeadTrack(now, settings.DeadAlertSeconds <= 0, member.IsTrulyDead);
                    _deadTracks[member.Id] = dead;
                }
                else if (member.IsTrulyDead && !dead.WasSoulstone)
                {
                    // Bleed-out transition from healable Incapacitated to Dead Soulstone triggers a fresh RAISE DEAD alert.
                    dead = new DeadTrack(now, settings.DeadAlertSeconds <= 0, WasSoulstone: true);
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
                    TriageTier tier = member.IsTrulyDead ? TriageTier.SoulstoneDead : TriageTier.Incapacitated;
                    if (IsHigherPriority(member, tier, bestCandidate, bestTier, settings.IncludeTempHpInThreshold))
                    {
                        bestCandidate = member;
                        bestTier = tier;
                    }
                }
                continue;
            }

            // Player is alive with HP > 0 (or no max HP available yet): clear dead suppression.
            _deadTracks.Remove(member.Id);

            if (member.MaxHitPoints <= 0)
            {
                _lowHpActive.Remove(member.Id);
                _lowHpFirstAt.Remove(member.Id);
                continue;
            }

            double pct = member.EffectivePercent(settings.IncludeTempHpInThreshold);
            bool wasLow = _lowHpActive.Contains(member.Id);
            bool isLow = EvaluateLowHpHysteresis(pct, wasLow, settings.HpThresholdPercent, settings.RecoveryHysteresisPercent);

            if (isLow)
            {
                _lowHpActive.Add(member.Id);
                if (!_lowHpFirstAt.TryGetValue(member.Id, out double firstLowAt))
                {
                    firstLowAt = now;
                    _lowHpFirstAt[member.Id] = firstLowAt;
                }

                bool timedOut = settings.MaxAlertSeconds > 0 && (now - firstLowAt) >= settings.MaxAlertSeconds;
                if (!timedOut)
                {
                    lowCount++;
                    if (IsHigherPriority(member, TriageTier.LivingLowHp, bestCandidate, bestTier, settings.IncludeTempHpInThreshold))
                    {
                        bestCandidate = member;
                        bestTier = TriageTier.LivingLowHp;
                    }
                }
            }
            else
            {
                _lowHpActive.Remove(member.Id);
                _lowHpFirstAt.Remove(member.Id);
            }
        }

        if (bestCandidate is null || bestTier == TriageTier.None)
        {
            ClearAlertEpisode();
            string defaultStatus = BuildDefaultStatus(watched, settings, statusLine);
            return BuildIdleOrNoticeView(settings.Mode, now, defaultStatus, targetingStatus, true, peopleSaved);
        }

        BannerSeverity bestSeverity = bestTier == TriageTier.LivingLowHp
            ? BannerSeverity.LowHp
            : BannerSeverity.CriticalDown;
        bool isSoulstone = bestTier == TriageTier.SoulstoneDead;

        if (!_wasAlerting
            || _lastAlertPlayerId != bestCandidate.Id
            || _lastAlertSeverity != bestSeverity
            || _lastAlertSoulstone != isSoulstone)
        {
            _alertGeneration++;
            _wasAlerting = true;
            _lastAlertPlayerId = bestCandidate.Id;
            _lastAlertSeverity = bestSeverity;
            _lastAlertSoulstone = isSoulstone;
        }

        int thp = Math.Max(0, bestCandidate.TempHitPoints);
        string thpDetail = !isSoulstone && thp > 0
            ? string.Create(CultureInfo.InvariantCulture, $" (+{thp} THP)")
            : "";

        string hpDetail = bestCandidate.MaxHitPoints > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{bestCandidate.HitPoints}/{bestCandidate.MaxHitPoints} HP{thpDetail}")
            : isSoulstone
                ? "SOULSTONE"
                : "Incapacitated / Dead";

        string extra = lowCount > 1
            ? string.Create(CultureInfo.InvariantCulture, $" • {lowCount} low")
            : "";

        string primary = bestTier switch
        {
            TriageTier.Incapacitated => $"DOWN: {bestCandidate.Name}",
            TriageTier.SoulstoneDead => $"DEAD — RAISE {bestCandidate.Name}",
            _ => string.Create(CultureInfo.InvariantCulture, $"HEAL {bestCandidate.Name} ({bestCandidate.Percent:0}%)")
        };

        string secondary = bestTier switch
        {
            TriageTier.Incapacitated => $"{hpDetail} — CRITICAL • INCAP (HEALABLE){extra}",
            TriageTier.SoulstoneDead => bestCandidate.MaxHitPoints > 0
                ? $"{hpDetail} — SOULSTONE • RAISE DEAD{extra}"
                : $"SOULSTONE • RAISE DEAD{extra}",
            _ => $"{hpDetail}{extra}"
        };

        string activeStatus = string.IsNullOrWhiteSpace(statusLine)
            ? (isSoulstone
                ? $"{bestCandidate.Name} DEAD (Soulstone)"
                : string.Create(CultureInfo.InvariantCulture, $"{bestCandidate.Name} {bestCandidate.Percent:0}%"))
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
            peopleSaved)
        {
            IsSoulstone = isSoulstone,
            AlertTempHitPoints = isSoulstone ? 0 : thp,
            AlertEffectivePercent = bestCandidate.EffectivePercent(settings.IncludeTempHpInThreshold)
        };
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

    internal static List<WatchedMemberSnapshot> FilterWatched(
        WatchedMemberSnapshot[] members,
        Settings settings,
        Func<WatchedMemberSnapshot, bool>? isMuted = null)
    {
        var list = new List<WatchedMemberSnapshot>(members.Length);
        if (settings.Mode == TrackingMode.Party)
        {
            var seen = new HashSet<ulong>(members.Length);
            foreach (var m in members)
            {
                if (m is null || m.Id == 0) continue;
                if (!settings.IncludeSelfInPartyMode && m.PartySlotIndex == 0) continue;
                if (isMuted is not null && isMuted(m)) continue;
                if (seen.Add(m.Id)) list.Add(m);
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
                        if (isMuted is null || !isMuted(m)) list.Add(m);
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
                        if (isMuted is null || !isMuted(m)) list.Add(m);
                        return list;
                    }
                }
            }
        }

        return list;
    }

    // Deterministic triage ordering for DH 5 Raid HUD and manual TargetCritical selection:
    // 0: Unmuted Incapacitated -> 1: Unmuted Living Low HP -> 2: Unmuted Living Healthy
    // -> 3: Unmuted Dead Soulstone -> 4: Muted / Disconnected.
    internal static WatchedMemberSnapshot[] SortForTriage(
        WatchedMemberSnapshot[] members,
        Settings settings,
        Func<WatchedMemberSnapshot, bool>? isMuted = null)
    {
        if (members is null || members.Length == 0)
            return Array.Empty<WatchedMemberSnapshot>();

        bool useTempHp = settings?.IncludeTempHpInThreshold ?? false;
        int threshold = settings?.HpThresholdPercent ?? 50;

        int Rank(WatchedMemberSnapshot m)
        {
            bool muted = isMuted is not null && isMuted(m);
            if (!m.Connected || muted) return 4;
            if (m.IsTrulyDead) return 3;
            if (m.IsIncapacitated) return 0;
            if (m.MaxHitPoints > 0 && m.EffectivePercent(useTempHp) <= threshold) return 1;
            return 2;
        }

        return members
            .Where(m => m is not null && m.Id != 0)
            .OrderBy(Rank)
            .ThenBy(m => m.EffectivePercent(useTempHp))
            .ThenBy(m => m.PartySlotIndex)
            .ThenBy(m => m.Id)
            .ToArray();
    }

    private static bool IsHigherPriority(
        WatchedMemberSnapshot candidate,
        TriageTier candidateTier,
        WatchedMemberSnapshot? currentBest,
        TriageTier currentTier,
        bool includeTempHp)
    {
        if (currentBest is null) return true;
        if (candidateTier != currentTier) return candidateTier > currentTier;

        double candPct = candidateTier == TriageTier.LivingLowHp
            ? candidate.EffectivePercent(includeTempHp)
            : candidate.Percent;
        double currPct = currentTier == TriageTier.LivingLowHp
            ? currentBest.EffectivePercent(includeTempHp)
            : currentBest.Percent;

        int pctCmp = candPct.CompareTo(currPct);
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
        _lastAlertSoulstone = false;
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
