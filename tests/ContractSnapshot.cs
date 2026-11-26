using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using VoK.HealersHelper;

// Observations of the implementation, emitted only after all behavioral assertions pass.
// Removing/changing compatibility observations requests major; adding them minor.
internal static class ContractSnapshot
{
    public static void Write(string path)
    {
        var compatibility = new SortedDictionary<string, object>();
        foreach (var property in typeof(Settings).GetProperties())
            compatibility["settings." + property.Name] = property.PropertyType.ToString();
        foreach (var mode in Enum.GetValues<TrackingMode>())
            compatibility["mode." + mode] = (int)mode;
        foreach (var method in Enum.GetValues<TargetMethod>())
            compatibility["target-method." + method] = (int)method;
        foreach (var severity in Enum.GetValues<BannerSeverity>())
            compatibility["severity." + severity] = (int)severity;
        foreach (var theme in Enum.GetValues<UiTheme>())
            compatibility["theme." + theme] = (int)theme;

        var settings = new Settings();
        var state = new HealerState();
        var party = new[]
        {
            new WatchedMemberSnapshot(1, "Self", 200, 200, 0, true, false),
            new WatchedMemberSnapshot(2, "Alice", 40, 200, 1, true, false),
            new WatchedMemberSnapshot(3, "Bob", 90, 200, 2, true, false)
        };
        var low = state.Evaluate(party, settings, 10.0);
        compatibility["healer.party-lowest-id"] = low.AlertPlayerId!;
        compatibility["healer.party-lowest-count"] = low.LowMemberCount;

        var recoveredCheck = new[]
        {
            new WatchedMemberSnapshot(1, "Self", 200, 200, 0, true, false),
            new WatchedMemberSnapshot(2, "Alice", 104, 200, 1, true, false)
        };
        compatibility["healer.hysteresis-holds-52pct"] =
            state.Evaluate(recoveredCheck, settings, 11.0).Severity == BannerSeverity.LowHp;

        var deadParty = new[]
        {
            new WatchedMemberSnapshot(1, "Self", 200, 200, 0, true, false),
            new WatchedMemberSnapshot(2, "Alice", -5, 200, 1, true, true)
        };
        compatibility["healer.dead-alerts-immediately"] =
            state.Evaluate(deadParty, settings, 20.0).Severity == BannerSeverity.CriticalDown;
        compatibility["healer.dead-suppresses-after-timeout"] =
            state.Evaluate(deadParty, settings, 20.0 + settings.DeadAlertSeconds).Severity == BannerSeverity.None;

        var disabledSettings = new Settings { PluginEnabled = false };
        compatibility["healer.master-off-silent"] =
            state.Evaluate(deadParty, disabledSettings, 21.0).Severity == BannerSeverity.None;

        var savedTracker = new PeopleSavedTracker();
        savedTracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 20, 200, 1, true, false) }, true, false, 1.0, 0.5);
        savedTracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 120, 200, 1, true, false) }, true, false, 2.0, 1.8);
        var saved = savedTracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 30, 200, 1, true, false) }, true, false, 3.0, 1.8);
        compatibility["people-saved.clutch-heal-detects-save"] = saved.Count == 1 && saved[0] == "Alice";

        var sdkDecision = TargetPolicy.Evaluate(2, "Alice", 1, true, true, 0, TargetMethod.Auto, false, 0);
        var fallbackDecision = TargetPolicy.Evaluate(2, "Alice", 1, false, true, 0, TargetMethod.Auto, false, 0);
        var restoreSdkDecision = TargetPolicy.EvaluateRestore(900, "Boss", -1, true, false, false, 2, TargetMethod.Auto);
        var restoreDeadMobDecision = TargetPolicy.EvaluateRestore(900, "Boss", -1, true, true, false, 2, TargetMethod.Auto);
        var restoreTracker = new TargetRestoreTracker();
        restoreTracker.RecordSwitch(900, "Boss", -1, new WatchedMemberSnapshot(2, "Alice", 40, 200, 1, true, false), 53, 1.0);
        var healedAlice = new[] { new WatchedMemberSnapshot(2, "Alice", 180, 200, 1, true, false) };
        _ = restoreTracker.EvaluateRestore(healedAlice, settings, 2, BannerSeverity.None, null, false, true, 1.2);
        var restoredCandidate = restoreTracker.EvaluateRestore(healedAlice, settings, 2, BannerSeverity.None, null, false, true, 1.5);
        var holdTracker = new TargetRestoreTracker();
        holdTracker.RecordSwitch(900, "Boss", -1, new WatchedMemberSnapshot(2, "Alice", 40, 200, 1, true, false), 53, 1.0);
        var aliceHealedBobLow = new[]
        {
            new WatchedMemberSnapshot(2, "Alice", 180, 200, 1, true, false),
            new WatchedMemberSnapshot(3, "Bob", 50, 200, 2, true, false)
        };
        _ = holdTracker.EvaluateRestore(aliceHealedBobLow, settings, 2, BannerSeverity.LowHp, 3, false, true, 1.2);
        bool universalHoldActive = holdTracker.EvaluateRestore(aliceHealedBobLow, new Settings { AutoTargetOnAlert = false }, 2, BannerSeverity.None, null, false, true, 2.0) is null
            && holdTracker.IsActive;
        compatibility["target-policy.sdk-primary"] = sdkDecision.Action.ToString();
        compatibility["target-policy.party-fallback"] = fallbackDecision.Action.ToString();
        compatibility["target-policy.party-action-1"] = TargetPolicy.PartyActionForSlot(1)!;

        var flashOn = BannerPresentation.Compute(low, settings, demo: false, editing: false, 0.0, 0.0);
        var flashOff = BannerPresentation.Compute(low, settings, demo: false, editing: false, settings.FlashIntervalMs / 1000.0, 0.0);
        var statusParty = StatusHudPresentation.Compute(low with { PeopleSaved = 3 }, settings, false, 0.0);
        var statusOff = StatusHudPresentation.Compute(low with { PluginEnabled = false }, disabledSettings, false, 0.0);
        string sampleScorecard = HealerStats.FormatChatScorecard(new Settings
        {
            PeopleSaved = 42,
            CurrentSaveStreak = 7,
            BestSaveStreak = 12,
            OnePercentClubSaves = 3,
            ClosestCallPlayerName = "Tanky",
            ClosestCallPreHealPercent = 2.5,
            ClosestCallPreHealHp = 5,
            BestReflexMs = 180,
            AvgReflexMs = 260,
            ReflexSampleCount = 9,
            BiggestOverkillSaved = 310
        });

        var triageState = new HealerState();
        var triageMembers = new[]
        {
            new WatchedMemberSnapshot(1, "Self", 200, 200, 0, true, false),
            new WatchedMemberSnapshot(2, "Caster", -15, 200, 1, true, true, IsTrulyDead: true),
            new WatchedMemberSnapshot(3, "Rogue", 70, 200, 2, true, false, TempHitPoints: 250),
            new WatchedMemberSnapshot(4, "Tank", -5, 200, 3, true, true, IsTrulyDead: false)
        };
        var incapWins = triageState.Evaluate(triageMembers, settings, 30.0);
        var lowWinsOverDead = triageState.Evaluate(new[] { triageMembers[0], triageMembers[1], triageMembers[2] }, settings, 31.0);
        var soulstoneOnly = triageState.Evaluate(new[] { triageMembers[0], triageMembers[1] }, settings, 32.0);
        var soulstoneFrame = BannerPresentation.Compute(soulstoneOnly, settings, demo: false, editing: false, 0.0, 0.0);
        var mutedLow = new HealerState().Evaluate(new[] { triageMembers[0], triageMembers[2] }, settings, 33.0, isMuted: m => m.Id == 3);
        var raidKeymap = TargetPolicy.EvaluateRaidKeymapReadiness(a => a is "TARGET_FELLOW_7_KEY" or "TARGET_FELLOW_8_KEY");

        var features = new SortedDictionary<string, object>
        {
            ["banner.primary-low"] = low.PrimaryText,
            ["banner.enabled-default"] = settings.HudEnabled,
            ["banner.font-default"] = settings.FontSizePixels,
            ["banner.opacity-default"] = settings.BannerOpacity,
            ["banner.opacity-min"] = BannerPresentation.MinBannerOpacity,
            ["banner.default-low-bg-hex"] = settings.LowHpBgHex,
            ["banner.default-crit-bg-hex"] = settings.CriticalBgHex,
            ["banner.disabled-suppresses-live"] = !BannerPresentation.Compute(low, new Settings { HudEnabled = false }, demo: false, editing: false, 0.0, 0.0).Visible,
            ["banner.disabled-allows-preview"] = BannerPresentation.Compute(low, new Settings { HudEnabled = false }, demo: true, editing: false, 0.0, 0.0).Visible,
            ["banner.edit-hit-corners-and-move"] = BannerPresentation.ClassifyEditHit(500, 130, 4, 4) == BannerEditDragMode.ResizeTopLeft
                && BannerPresentation.ClassifyEditHit(500, 130, 250, 65) == BannerEditDragMode.Move,
            ["banner.flash-enabled-default"] = settings.FlashBanner,
            ["banner.hp-bar-default"] = settings.ShowBannerHpBar,
            ["banner.flash-interval-default-ms"] = settings.FlashIntervalMs,
            ["banner.flash-alternates"] = flashOn.FlashOn && !flashOff.FlashOn,
            ["banner.capture-exclude-default"] = settings.ExcludeFromCapture,
            ["triage.incap-outranks-low-and-soulstone"] = incapWins.AlertPlayerId == 4 && !incapWins.IsSoulstone,
            ["triage.low-hp-outranks-soulstone"] = lowWinsOverDead.AlertPlayerId == 3 && !lowWinsOverDead.IsSoulstone,
            ["triage.soulstone-banner-calm-spectral"] = soulstoneOnly.IsSoulstone && soulstoneFrame.IsSoulstone && !soulstoneFrame.FlashOn && soulstoneFrame.BorderColorArgb == BannerPresentation.SoulstoneBorderArgb,
            ["triage.temp-hp-badge-and-threshold"] = lowWinsOverDead.AlertTempHitPoints == 250 && lowWinsOverDead.SecondaryText.Contains("+250 THP", StringComparison.Ordinal),
            ["triage.session-mute-suppresses-alert"] = mutedLow.Severity == BannerSeverity.None,
            ["triage.raid-keymap-readiness-slots-7-12"] = raidKeymap.BoundCount == 2 && raidKeymap.TotalCount == 6,
            ["theme.default"] = settings.Theme.ToString(),
            ["theme.palette-count"] = UiThemePalette.All.Length,
            ["dh5.quick-cast-disabled-default"] = settings.QuickHealHotbar == 0 && settings.QuickRaiseHotbar == 0,
            ["healer.plugin-enabled-default"] = settings.PluginEnabled,
            ["healer.include-self-default"] = settings.IncludeSelfInPartyMode,
            ["healer.suppress-in-town-default"] = settings.SuppressAlertsInTown,
            ["healer.threshold-default"] = settings.HpThresholdPercent,
            ["healer.hysteresis-default"] = settings.RecoveryHysteresisPercent,
            ["healer.dead-alert-default-seconds"] = settings.DeadAlertSeconds,
            ["healer.max-alert-default-seconds"] = settings.MaxAlertSeconds,
            ["saves.window-default-seconds"] = settings.SaveWindowSeconds,
            ["saves.cast-window-default-seconds"] = settings.SelfCastWindowSeconds,
            ["saves.min-heal-delta-default"] = settings.MinHealDelta,
            ["saves.show-notice-default"] = settings.ShowSavedNotice,
            ["gamification.rank-0"] = HealerStats.RankForSaves(0),
            ["gamification.rank-250"] = HealerStats.RankForSaves(250),
            ["gamification.scorecard-ascii-valid"] = sampleScorecard.Length <= HealerStats.MaxScorecardLength && sampleScorecard.All(c => c is >= (char)0x20 and <= (char)0x7E),
            ["auto-target.enabled-default"] = settings.AutoTargetOnAlert,
            ["auto-target.cooldown-default-seconds"] = settings.AutoTargetCooldownSeconds,
            ["auto-target.restore-previous-default"] = settings.RestorePreviousTargetAfterHeal,
            ["auto-target.restore-delay-default-seconds"] = settings.RestoreTargetDelaySeconds,
            ["target-policy.restore-sdk"] = restoreSdkDecision.Action.ToString(),
            ["target-policy.restore-rejects-dead-mob"] = restoreDeadMobDecision.Action == TargetActionKind.None,
            ["target-restore.healed-restores-original"] = restoredCandidate is { TargetId: 900UL },
            ["target-restore.universal-ally-hold"] = universalHoldActive,
            ["hotkey.toggle-plugin-default"] = settings.TogglePluginHotkey.Format(),
            ["hotkey.toggle-banner-default"] = settings.ToggleBannerHotkey.Format(),
            ["hotkey.focus-default"] = settings.FocusTargetHotkey.Format(),
            ["hotkey.toggle-default"] = settings.ToggleModeHotkey.Format(),
            ["hotkey.critical-default"] = settings.TargetCriticalHotkey.Format(),
            ["hotkey.reset-saved-default"] = settings.ResetSavedHotkey.Format(),
            ["hotkey.copy-scorecard-default"] = settings.CopyScorecardHotkey.Format(),
            ["font.accepts-56"] = SettingsInput.TryFontSize("56", out _),
            ["font.rejects-17"] = !SettingsInput.TryFontSize("17", out _),
            ["status-hud.enabled-default"] = settings.StatusHudEnabled,
            ["status-hud.show-rank-default"] = settings.ShowRankOnStatusHud,
            ["status-hud.font-default"] = settings.StatusFontSizePixels,
            ["status-hud.party-alert-text"] = statusParty.Text,
            ["status-hud.off-text"] = statusOff.Text,
            ["status-hud.accepts-14"] = SettingsInput.TryStatusFontSize("14", out _),
            ["status-hud.rejects-8"] = !SettingsInput.TryStatusFontSize("8", out _)
        };

        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(
            fullPath,
            JsonSerializer.Serialize(new { schema = 1, compatibility, features }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
