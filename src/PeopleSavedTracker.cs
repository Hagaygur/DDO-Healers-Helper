using System;
using System.Collections.Generic;
using System.Linq;

namespace VoK.HealersHelper;

// Pure portable tracker for clutch saves: detects heals attributed to a recent
// self cast followed within SaveWindowSeconds by real damage (single or multi-hit)
// that exceeds PreHealHp yet leaves the target alive, while ignoring MaxHP gear/buff caps.
internal sealed class PeopleSavedTracker
{
    public const double SelfCastWindowSeconds = 3.5;
    public const double SaveWindowSeconds = 5.0;
    public const int MinHealDelta = 5;

    private readonly record struct PrevHpState(int Hp, int MaxHp);
    private sealed record HealWindow(int PreHealHp, int PreHealMaxHp, long CumulativeDamage, double ExpiresAt);

    private readonly Dictionary<ulong, PrevHpState> _previous = new();
    private readonly Dictionary<ulong, HealWindow> _healWindows = new();

    public void Reset()
    {
        _previous.Clear();
        _healWindows.Clear();
    }

    public List<string> ProcessScan(
        WatchedMemberSnapshot[] members,
        bool pluginEnabled,
        bool inTown,
        double now,
        double lastSelfCastAt) =>
        ProcessScanDetailed(members, pluginEnabled, inTown, now, lastSelfCastAt)
            .Select(e => e.PlayerName)
            .ToList();

    public List<ClutchSaveEvent> ProcessScanDetailed(
        WatchedMemberSnapshot[] members,
        bool pluginEnabled,
        bool inTown,
        double now,
        double lastSelfCastAt,
        double saveWindowSeconds = SaveWindowSeconds,
        int minHealDelta = MinHealDelta,
        double selfCastWindowSeconds = SelfCastWindowSeconds)
    {
        var savedEvents = new List<ClutchSaveEvent>();
        if (!pluginEnabled)
        {
            Reset();
            return savedEvents;
        }

        double effectiveSaveWindow = double.IsFinite(saveWindowSeconds)
            ? Math.Clamp(saveWindowSeconds, 1.0, 15.0)
            : SaveWindowSeconds;
        double effectiveCastWindow = double.IsFinite(selfCastWindowSeconds)
            ? Math.Clamp(selfCastWindowSeconds, 1.0, 15.0)
            : SelfCastWindowSeconds;
        int effectiveMinDelta = Math.Clamp(minHealDelta, 1, 200);

        members ??= Array.Empty<WatchedMemberSnapshot>();
        var connectedIds = new HashSet<ulong>(members.Length);
        foreach (var member in members)
        {
            if (member is not null && member.Id != 0 && member.Connected)
            {
                connectedIds.Add(member.Id);
            }
        }

        PruneMissing(_previous, connectedIds);
        PruneMissing(_healWindows, connectedIds);

        bool selfCastRecent = double.IsFinite(lastSelfCastAt) && (now - lastSelfCastAt) <= effectiveCastWindow;

        foreach (var member in members)
        {
            if (member is null || member.Id == 0 || !member.Connected)
            {
                continue;
            }

            if (inTown)
            {
                _healWindows.Remove(member.Id);
                _previous[member.Id] = new PrevHpState(member.HitPoints, member.MaxHitPoints);
                continue;
            }

            if (!_previous.TryGetValue(member.Id, out var prev))
            {
                _previous[member.Id] = new PrevHpState(member.HitPoints, member.MaxHitPoints);
                continue;
            }

            if (member.IsDeadOrDown || member.HitPoints <= 0)
            {
                _healWindows.Remove(member.Id);
                _previous[member.Id] = new PrevHpState(Math.Min(0, member.HitPoints), member.MaxHitPoints);
                continue;
            }

            if (member.HitPoints > prev.Hp)
            {
                long maxHpGain = prev.MaxHp > 0 && member.MaxHitPoints > prev.MaxHp
                    ? (long)member.MaxHitPoints - prev.MaxHp
                    : 0L;
                long effectiveHeal = ((long)member.HitPoints - prev.Hp) - maxHpGain;

                if (prev.Hp > 0
                    && (prev.MaxHp <= 0 || prev.Hp < prev.MaxHp)
                    && effectiveHeal >= effectiveMinDelta
                    && selfCastRecent)
                {
                    int preHealHp;
                    int preHealMaxHp;
                    int currentMaxRef = prev.MaxHp > 0 ? prev.MaxHp : member.MaxHitPoints;

                    if (_healWindows.TryGetValue(member.Id, out var existing) && now <= existing.ExpiresAt)
                    {
                        int effectiveFloor = (int)Math.Max(1L, existing.PreHealHp - existing.CumulativeDamage);
                        if (prev.Hp <= effectiveFloor)
                        {
                            preHealHp = prev.Hp;
                            preHealMaxHp = currentMaxRef;
                        }
                        else
                        {
                            preHealHp = effectiveFloor;
                            preHealMaxHp = existing.PreHealMaxHp > 0 ? existing.PreHealMaxHp : currentMaxRef;
                        }
                    }
                    else
                    {
                        preHealHp = prev.Hp;
                        preHealMaxHp = currentMaxRef;
                    }

                    _healWindows[member.Id] = new HealWindow(preHealHp, preHealMaxHp, 0L, now + effectiveSaveWindow);
                }
                else if (!selfCastRecent
                         && effectiveHeal > 0
                         && _healWindows.TryGetValue(member.Id, out var openWin)
                         && now <= openWin.ExpiresAt)
                {
                    _healWindows[member.Id] = openWin with
                    {
                        CumulativeDamage = Math.Max(0L, openWin.CumulativeDamage - effectiveHeal)
                    };
                }
            }
            else if (member.HitPoints < prev.Hp)
            {
                long maxHpDrop = prev.MaxHp > 0 && member.MaxHitPoints > 0 && member.MaxHitPoints < prev.MaxHp
                    ? (long)prev.MaxHp - member.MaxHitPoints
                    : 0L;
                long effectiveDamage = Math.Max(0L, ((long)prev.Hp - member.HitPoints) - maxHpDrop);

                if (_healWindows.TryGetValue(member.Id, out var window))
                {
                    if (now > window.ExpiresAt)
                    {
                        _healWindows.Remove(member.Id);
                    }
                    else if (effectiveDamage > 0)
                    {
                        long totalDamage = window.CumulativeDamage + effectiveDamage;
                        if (totalDamage > window.PreHealHp)
                        {
                            double preHealPct = window.PreHealMaxHp > 0
                                ? Math.Clamp(window.PreHealHp * 100.0 / window.PreHealMaxHp, 0.0, 100.0)
                                : 100.0;
                            int overkill = (int)Math.Min(int.MaxValue, totalDamage - window.PreHealHp);
                            savedEvents.Add(new ClutchSaveEvent(
                                member.Id,
                                member.Name,
                                window.PreHealHp,
                                window.PreHealMaxHp,
                                preHealPct,
                                overkill));
                            _healWindows.Remove(member.Id);
                        }
                        else
                        {
                            _healWindows[member.Id] = window with { CumulativeDamage = totalDamage };
                        }
                    }
                }
            }

            _previous[member.Id] = new PrevHpState(member.HitPoints, member.MaxHitPoints);
        }

        return savedEvents;
    }

    private static void PruneMissing<TValue>(Dictionary<ulong, TValue> dict, HashSet<ulong> keep)
    {
        foreach (ulong id in dict.Keys.Where(id => !keep.Contains(id)).ToArray())
        {
            dict.Remove(id);
        }
    }
}
