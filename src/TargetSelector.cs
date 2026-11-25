using System;
using VoK.Sdk;
using VoK.Sdk.Ddo;
using VoK.Sdk.Ddo.Enums;
using VoK.Sdk.Events;
using VoK.Sdk.Helpers;

namespace VoK.HealersHelper;

// Executes target selection via VoK.Sdk SelectObject(ulong) (primary) or
// Keymap.GetHotkey + SendKey (party keybind fallback/override).
internal sealed class TargetSelector
{
    private readonly IDdoGameDataProvider _data;
    private double _nextAutoAllowedAt;

    public string LastStatus { get; private set; } = "Targeting ready";

    public TargetSelector(IDdoGameDataProvider data)
    {
        _data = data;
    }

    public bool TrySelect(
        ulong targetId,
        string targetName,
        int partySlotIndex,
        Settings settings,
        double now,
        bool bypassCooldown)
    {
        IEntity? entity = targetId > 0 && targetId < ulong.MaxValue
            ? _data.GetEntity(targetId)
            : null;
        bool entityInRange = entity is not null && !entity.IsDeleted;

        string? partyAction = TargetPolicy.PartyActionForSlot(partySlotIndex);
        HotKeyEventArgs? hotkey = partyAction is not null
            ? _data.Keymap?.GetHotkey(partyAction)
            : null;

        ulong currentSelected = _data.GetSelectedObjectId().GetValueOrDefault();
        double cooldownRemaining = Math.Max(0, _nextAutoAllowedAt - now);

        TargetDecision decision = TargetPolicy.Evaluate(
            targetId,
            targetName,
            partySlotIndex,
            entityInRange,
            hotkey is not null,
            currentSelected,
            settings.TargetingMethod,
            bypassCooldown,
            cooldownRemaining);

        LastStatus = decision.Status;
        if (decision.Action == TargetActionKind.None)
            return false;

        if (decision.Action == TargetActionKind.SdkSelectObject)
        {
            _data.SelectObject(decision.TargetId);
            _nextAutoAllowedAt = now + settings.AutoTargetCooldownSeconds;
            return true;
        }

        if (decision.Action == TargetActionKind.PartyKeybind && hotkey is not null)
        {
            _data.SendKey(hotkey);
            _nextAutoAllowedAt = now + settings.AutoTargetCooldownSeconds;
            return true;
        }

        return false;
    }

    public bool TryRestore(RestoreCandidate candidate, Settings settings)
    {
        if (_data is null)
        {
            LastStatus = "Game data unavailable";
            return false;
        }

        IEntity? entity = candidate.TargetId > 0 && candidate.TargetId < ulong.MaxValue
            ? _data.GetEntity(candidate.TargetId)
            : null;
        bool entityInRange = entity is not null && !entity.IsDeleted;
        bool isDeadNonPlayer = entity is not null
            && !entity.IsDeleted
            && !entity.IsPlayer()
            && (entity.LifeState == LifeState.Dead || (entity.HitPoints_Max > 0 && entity.HitPoints_Current <= 0));

        string? partyAction = TargetPolicy.PartyActionForSlot(candidate.PartySlotIndex);
        HotKeyEventArgs? hotkey = partyAction is not null
            ? _data.Keymap?.GetHotkey(partyAction)
            : null;

        ulong currentSelected = _data.GetSelectedObjectId().GetValueOrDefault();
        if (candidate.ExpectedCurrentTargetId != 0 && currentSelected != candidate.ExpectedCurrentTargetId)
        {
            LastStatus = "Restore skipped (target changed)";
            return false;
        }

        TargetDecision decision = TargetPolicy.EvaluateRestore(
            candidate.TargetId,
            candidate.TargetName,
            candidate.PartySlotIndex,
            entityInRange,
            isDeadNonPlayer,
            hotkey is not null,
            currentSelected,
            settings.TargetingMethod);

        LastStatus = decision.Status;
        if (decision.Action == TargetActionKind.None)
            return false;

        if (candidate.ExpectedCurrentTargetId != 0
            && _data.GetSelectedObjectId().GetValueOrDefault() != candidate.ExpectedCurrentTargetId)
        {
            LastStatus = "Restore skipped (target changed)";
            return false;
        }

        if (decision.Action == TargetActionKind.SdkSelectObject)
        {
            _data.SelectObject(decision.TargetId);
            return true;
        }

        if (decision.Action == TargetActionKind.PartyKeybind && hotkey is not null)
        {
            _data.SendKey(hotkey);
            return true;
        }

        return false;
    }

    public void ResetCooldown() => _nextAutoAllowedAt = 0;
}
