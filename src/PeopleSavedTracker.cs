using System;
using System.Collections.Generic;
using System.Linq;

namespace VoK.HealersHelper;

// Pure portable tracker for clutch saves: detects heals attributed to a recent
// self cast followed within SaveWindowSeconds by a hit that exceeds PreHealHp
// yet leaves the target alive.
internal sealed class PeopleSavedTracker
{
    public const double SelfCastWindowSeconds = 3.5;
    public const double SaveWindowSeconds = 5.0;
    public const int MinHealDelta = 5;

    private sealed record HealWindow(int PreHealHp, double ExpiresAt);

    private readonly Dictionary<ulong, int> _previousHp = new();
    private readonly Dictionary<ulong, HealWindow> _healWindows = new();

    public void Reset()
    {
        _previousHp.Clear();
        _healWindows.Clear();
    }

    public List<string> ProcessScan(
        WatchedMemberSnapshot[] members,
        bool pluginEnabled,
        bool inTown,
        double now,
        double lastSelfCastAt)
    {
        var savedNames = new List<string>();
        if (!pluginEnabled)
        {
            Reset();
            return savedNames;
        }

        members ??= Array.Empty<WatchedMemberSnapshot>();
        var connectedIds = new HashSet<ulong>(members.Length);
        foreach (var member in members)
        {
            if (member is not null && member.Id != 0 && member.Connected)
            {
                connectedIds.Add(member.Id);
            }
        }

        PruneMissing(_previousHp, connectedIds);
        PruneMissing(_healWindows, connectedIds);

        bool selfCastRecent = double.IsFinite(lastSelfCastAt) && (now - lastSelfCastAt) <= SelfCastWindowSeconds;

        foreach (var member in members)
        {
            if (member is null || member.Id == 0 || !member.Connected)
            {
                continue;
            }

            if (inTown)
            {
                _previousHp[member.Id] = member.HitPoints;
                continue;
            }

            if (!_previousHp.TryGetValue(member.Id, out int previousHp))
            {
                _previousHp[member.Id] = member.HitPoints;
                continue;
            }

            if (member.HitPoints > previousHp)
            {
                int delta = member.HitPoints - previousHp;
                if (previousHp > 0 && !member.IsDeadOrDown && delta >= MinHealDelta && selfCastRecent)
                {
                    int preHealHp = (_healWindows.TryGetValue(member.Id, out var existing) && now <= existing.ExpiresAt)
                        ? Math.Min(existing.PreHealHp, previousHp)
                        : previousHp;
                    _healWindows[member.Id] = new HealWindow(preHealHp, now + SaveWindowSeconds);
                }
            }
            else if (member.HitPoints < previousHp)
            {
                int damageTaken = previousHp - member.HitPoints;
                if (_healWindows.TryGetValue(member.Id, out var window))
                {
                    if (now > window.ExpiresAt)
                    {
                        _healWindows.Remove(member.Id);
                    }
                    else if (damageTaken > window.PreHealHp && member.HitPoints > 0 && !member.IsDeadOrDown)
                    {
                        savedNames.Add(member.Name);
                        _healWindows.Remove(member.Id);
                    }
                    else if (member.HitPoints <= 0 || member.IsDeadOrDown)
                    {
                        _healWindows.Remove(member.Id);
                    }
                }
            }

            _previousHp[member.Id] = member.HitPoints;
        }

        return savedNames;
    }

    private static void PruneMissing<TValue>(Dictionary<ulong, TValue> dict, HashSet<ulong> keep)
    {
        foreach (ulong id in dict.Keys.Where(id => !keep.Contains(id)).ToArray())
        {
            dict.Remove(id);
        }
    }
}
