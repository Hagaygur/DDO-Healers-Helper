using System;

namespace VoK.HealersHelper;

internal readonly record struct RestoreCandidate(
    ulong TargetId,
    string TargetName,
    int PartySlotIndex);

// Pure portable state machine that remembers the player's prior target when
// Healer's Helper switches selection to an injured ally, then emits a one-shot
// restore candidate once that ally is healed and the configured delay elapses.
internal sealed class TargetRestoreTracker
{
    public const double SelectionSettleGraceSeconds = 0.8;
    public const double MaxRestoreWindowSeconds = 30.0;

    private bool _active;
    private ulong _originalTargetId;
    private string _originalTargetName = "";
    private int _originalPartySlotIndex = -1;
    private ulong _switchedPlayerId;
    private int _preSwitchHp;
    private bool _wasBelowThreshold;
    private double _switchedAt;
    private bool _settledOnSwitchedPlayer;
    private double? _healedSince;

    public bool IsActive => _active;

    public void Reset()
    {
        _active = false;
        _originalTargetId = 0;
        _originalTargetName = "";
        _originalPartySlotIndex = -1;
        _switchedPlayerId = 0;
        _preSwitchHp = 0;
        _wasBelowThreshold = false;
        _switchedAt = 0;
        _settledOnSwitchedPlayer = false;
        _healedSince = null;
    }

    public void RecordSwitch(
        ulong priorSelectedId,
        string priorTargetName,
        int priorPartySlotIndex,
        WatchedMemberSnapshot switchedTo,
        int recoverThresholdPercent,
        double now)
    {
        if (switchedTo is null || switchedTo.Id == 0 || switchedTo.Id == ulong.MaxValue)
            return;

        bool wasBelow = switchedTo.IsDeadOrDown
            || (switchedTo.MaxHitPoints > 0 && switchedTo.Percent < recoverThresholdPercent);

        if (!_active)
        {
            if (priorSelectedId == 0 || priorSelectedId == ulong.MaxValue || priorSelectedId == switchedTo.Id)
                return;

            _active = true;
            _originalTargetId = priorSelectedId;
            _originalTargetName = string.IsNullOrWhiteSpace(priorTargetName) ? "previous target" : priorTargetName;
            _originalPartySlotIndex = priorPartySlotIndex;
            _switchedPlayerId = switchedTo.Id;
            _preSwitchHp = switchedTo.HitPoints;
            _wasBelowThreshold = wasBelow;
            _switchedAt = now;
            _settledOnSwitchedPlayer = false;
            _healedSince = null;
            return;
        }

        if (switchedTo.Id == _originalTargetId)
        {
            Reset();
            return;
        }

        if (priorSelectedId == _switchedPlayerId
            || (!_settledOnSwitchedPlayer && (now - _switchedAt) <= SelectionSettleGraceSeconds))
        {
            _switchedPlayerId = switchedTo.Id;
            _preSwitchHp = switchedTo.HitPoints;
            _wasBelowThreshold = wasBelow;
            _switchedAt = now;
            _settledOnSwitchedPlayer = false;
            _healedSince = null;
            return;
        }

        if (priorSelectedId > 0 && priorSelectedId < ulong.MaxValue && priorSelectedId != switchedTo.Id)
        {
            _originalTargetId = priorSelectedId;
            _originalTargetName = string.IsNullOrWhiteSpace(priorTargetName) ? "previous target" : priorTargetName;
            _originalPartySlotIndex = priorPartySlotIndex;
            _switchedPlayerId = switchedTo.Id;
            _preSwitchHp = switchedTo.HitPoints;
            _wasBelowThreshold = wasBelow;
            _switchedAt = now;
            _settledOnSwitchedPlayer = false;
            _healedSince = null;
            return;
        }

        Reset();
    }

    public RestoreCandidate? EvaluateRestore(
        WatchedMemberSnapshot[] members,
        Settings settings,
        ulong currentSelectedId,
        BannerSeverity activeSeverity,
        ulong? activeAlertPlayerId,
        bool inTown,
        bool foregroundAllowed,
        double now)
    {
        if (!_active)
            return null;

        if (settings is null
            || !settings.PluginEnabled
            || !settings.RestorePreviousTargetAfterHeal
            || settings.TargetingMethod == TargetMethod.Off
            || settings.Mode == TrackingMode.Off
            || inTown
            || (now - _switchedAt) > MaxRestoreWindowSeconds)
        {
            Reset();
            return null;
        }

        if (currentSelectedId == _switchedPlayerId)
        {
            _settledOnSwitchedPlayer = true;
        }
        else if (_settledOnSwitchedPlayer)
        {
            Reset();
            return null;
        }
        else if ((now - _switchedAt) > SelectionSettleGraceSeconds)
        {
            Reset();
            return null;
        }

        WatchedMemberSnapshot? player = null;
        if (members is not null)
        {
            for (int i = 0; i < members.Length; i++)
            {
                var m = members[i];
                if (m is not null && m.Id == _switchedPlayerId)
                {
                    player = m;
                    break;
                }
            }
        }

        if (player is null || !player.Connected || player.IsTrulyDead)
        {
            Reset();
            return null;
        }

        int recoverAt = Math.Min(100, settings.HpThresholdPercent + settings.RecoveryHysteresisPercent);
        bool isAlive = !player.IsDeadOrDown && player.HitPoints > 0;
        bool isHealed = _wasBelowThreshold
            ? (isAlive && player.MaxHitPoints > 0 && player.Percent >= recoverAt)
            : (isAlive && (((long)player.HitPoints - _preSwitchHp) >= settings.MinHealDelta
                           || (player.MaxHitPoints > 0 && player.HitPoints >= player.MaxHitPoints)));

        if (!isHealed)
        {
            _healedSince = null;
            return null;
        }

        _healedSince ??= now;

        if (settings.AutoTargetOnAlert
            && activeSeverity is BannerSeverity.LowHp or BannerSeverity.CriticalDown
            && activeAlertPlayerId.HasValue
            && activeAlertPlayerId.Value != _switchedPlayerId)
        {
            return null;
        }

        if ((now - _healedSince.Value) < settings.RestoreTargetDelaySeconds)
            return null;

        // Wait until DDO's asynchronous MMF selection actually lands on _switchedPlayerId
        // (up to SelectionSettleGraceSeconds) and game/overlay focus allows input.
        if (!_settledOnSwitchedPlayer || !foregroundAllowed)
            return null;

        var candidate = new RestoreCandidate(_originalTargetId, _originalTargetName, _originalPartySlotIndex);
        Reset();
        return candidate;
    }
}
