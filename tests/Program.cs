using System;
using System.IO;
using VoK.HealersHelper;

internal static class Program
{
    private static int _count;

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new Exception("FAILED: " + name);
        ++_count;
        Console.WriteLine("PASS " + name);
    }

    private static void Main(string[] args)
    {
        TestPartyThresholdAndPrioritization();
        TestRecoveryHysteresis();
        TestDeadAndIncapacitatedTimeoutAndSuppression();
        TestSinglePlayerAndSoloAndOffAndMasterToggle();
        TestPeopleSavedTracker();
        TestHotkeyMatcherAndDeduplication();
        TestTargetPolicyRules();
        TestSettingsValidationPersistenceAndFonts();
        TestBannerPresentationAndPositioning();
        TestStatusHudPresentation();

        Console.WriteLine($"{_count} checks passed.");
        if (_count < 35) throw new Exception($"Expected >= 35 assertions, ran {_count}");
        if (args.Length == 2 && args[0] == "--contracts") ContractSnapshot.Write(args[1]);
    }

    private static void TestPartyThresholdAndPrioritization()
    {
        var state = new HealerState();
        var settings = new Settings { HpThresholdPercent = 50, RecoveryHysteresisPercent = 3 };

        var healthy = new[]
        {
            new WatchedMemberSnapshot(1, "Self", 180, 200, 0, true, false),
            new WatchedMemberSnapshot(2, "Alice", 102, 200, 1, true, false)
        };
        var idle = state.Evaluate(healthy, settings, 1.0);
        Assert(idle.Severity == BannerSeverity.None && idle.LowMemberCount == 0, "healthy party produces no alert");

        var twoLow = new[]
        {
            new WatchedMemberSnapshot(1, "Self", 200, 200, 0, true, false),
            new WatchedMemberSnapshot(2, "Alice", 90, 200, 1, true, false),  // 45%
            new WatchedMemberSnapshot(3, "Bob", 40, 200, 2, true, false)     // 20%
        };
        var lowView = state.Evaluate(twoLow, settings, 2.0);
        Assert(lowView.Severity == BannerSeverity.LowHp, "low HP member triggers LowHp severity");
        Assert(lowView.AlertPlayerId == 3 && lowView.AlertPlayerName == "Bob", "lowest HP percent member prioritized");
        Assert(lowView.LowMemberCount == 2, "LowMemberCount counts all active low members");
        Assert(lowView.PrimaryText == "HEAL Bob (20%)", "exact primary percent formatting");
        Assert(lowView.SecondaryText == "40/200 HP • 2 low", "secondary text includes HP ratio and low count");

        // Disconnected member is ignored even with low HP.
        var offline = new[]
        {
            new WatchedMemberSnapshot(1, "Self", 200, 200, 0, true, false),
            new WatchedMemberSnapshot(2, "Alice", 10, 200, 1, false, false)
        };
        Assert(state.Evaluate(offline, settings, 3.0).Severity == BannerSeverity.None, "disconnected member does not trigger alert");
    }

    private static void TestRecoveryHysteresis()
    {
        var state = new HealerState();
        var settings = new Settings { HpThresholdPercent = 50, RecoveryHysteresisPercent = 3 };

        var at52 = new[] { new WatchedMemberSnapshot(2, "Alice", 104, 200, 1, true, false) };
        Assert(state.Evaluate(at52, settings, 10.0).Severity == BannerSeverity.None, "52% above 50% threshold does not trigger initially");

        var at50 = new[] { new WatchedMemberSnapshot(2, "Alice", 100, 200, 1, true, false) };
        Assert(state.Evaluate(at50, settings, 11.0).Severity == BannerSeverity.LowHp, "50% triggers alert at threshold boundary");
        Assert(state.Evaluate(at52, settings, 12.0).Severity == BannerSeverity.LowHp, "hysteresis keeps alert active at 52% below 53% recovery");

        var at53 = new[] { new WatchedMemberSnapshot(2, "Alice", 106, 200, 1, true, false) };
        Assert(state.Evaluate(at53, settings, 13.0).Severity == BannerSeverity.None, "alert clears once HP reaches threshold + hysteresis (53%)");
        Assert(state.Evaluate(at52, settings, 14.0).Severity == BannerSeverity.None, "after recovery, 52% does not re-trigger until <= 50%");
    }

    private static void TestDeadAndIncapacitatedTimeoutAndSuppression()
    {
        var state = new HealerState();
        var settings = new Settings { HpThresholdPercent = 50, DeadAlertSeconds = 6 };

        var mixed = new[]
        {
            new WatchedMemberSnapshot(1, "Self", 200, 200, 0, true, false),
            new WatchedMemberSnapshot(2, "Tank", -8, 200, 1, true, true),
            new WatchedMemberSnapshot(3, "Rogue", 40, 200, 2, true, false)
        };
        var crit = state.Evaluate(mixed, settings, 100.0);
        Assert(crit.Severity == BannerSeverity.CriticalDown && crit.AlertPlayerId == 2, "CriticalDown prioritizes incapacitated negative HP over LowHp");
        Assert(crit.PrimaryText == "DOWN: Tank" && crit.SecondaryText.Contains("CRITICAL"), "CriticalDown banner text formatted");
        Assert(state.Evaluate(mixed, settings, 105.9).Severity == BannerSeverity.CriticalDown, "CriticalDown remains active before DeadAlertSeconds elapses");

        var afterTimeout = state.Evaluate(mixed, settings, 106.0);
        Assert(afterTimeout.Severity == BannerSeverity.LowHp && afterTimeout.AlertPlayerId == 3, "dead alert times out after DeadAlertSeconds allowing LowHp to surface");

        var tankStillDown = new[] { new WatchedMemberSnapshot(2, "Tank", 0, 200, 1, true, false) };
        Assert(state.Evaluate(tankStillDown, settings, 120.0).Severity == BannerSeverity.None, "0-HP dead member stays suppressed after timeout");

        var tankAlive = new[] { new WatchedMemberSnapshot(2, "Tank", 150, 200, 1, true, false) };
        Assert(state.Evaluate(tankAlive, settings, 121.0).Severity == BannerSeverity.None, "alive HP > 0 clears dead suppression");
        var tankDownAgain = new[] { new WatchedMemberSnapshot(2, "Tank", -3, 200, 1, true, true) };
        Assert(state.Evaluate(tankDownAgain, settings, 122.0).Severity == BannerSeverity.CriticalDown, "member dying again after raise alerts again");

        var zeroState = new HealerState();
        var zeroSettings = new Settings { DeadAlertSeconds = 0 };
        Assert(zeroState.Evaluate(tankDownAgain, zeroSettings, 10.0).Severity == BannerSeverity.None, "DeadAlertSeconds = 0 suppresses dead alert immediately");
    }

    private static void TestSinglePlayerAndSoloAndOffAndMasterToggle()
    {
        var state = new HealerState();
        var party = new[]
        {
            new WatchedMemberSnapshot(1, "Self", 30, 200, 0, true, false),
            new WatchedMemberSnapshot(2, "Tank", 160, 200, 1, true, false),
            new WatchedMemberSnapshot(3, "Wizard", 10, 200, 2, true, true)
        };

        var byId = new Settings { Mode = TrackingMode.SinglePlayer, FocusedPlayerId = 2, FocusedPlayerName = "Tank" };
        Assert(state.Evaluate(party, byId, 1.0).Severity == BannerSeverity.None, "SinglePlayer focus by ID ignores other low/dead party members");

        var byName = new Settings { Mode = TrackingMode.SinglePlayer, FocusedPlayerId = 0, FocusedPlayerName = "wizard" };
        var wizAlert = state.Evaluate(party, byName, 2.0);
        Assert(wizAlert.Severity == BannerSeverity.CriticalDown && wizAlert.AlertPlayerId == 3, "SinglePlayer focus matches by case-insensitive name");

        var solo = new[] { new WatchedMemberSnapshot(1, "Self", 40, 200, 0, true, false) };
        var partyMode = new Settings { Mode = TrackingMode.Party, HpThresholdPercent = 50 };
        var selfAlert = state.Evaluate(solo, partyMode, 3.0);
        Assert(selfAlert.Severity == BannerSeverity.LowHp && selfAlert.AlertPlayerId == 1, "Party mode tracks solo self-HP");

        var offMode = new Settings { Mode = TrackingMode.Off };
        var offView = state.Evaluate(party, offMode, 4.0);
        Assert(offView.Severity == BannerSeverity.None && offView.StatusLine == "Tracking Off", "Off mode stays silent");

        state.SetNotice("MODE: PARTY", "Watching self and party/raid", 2.0, 5.0);
        var notice = state.Evaluate(new[] { new WatchedMemberSnapshot(1, "Self", 200, 200, 0, true, false) }, partyMode, 5.5);
        Assert(notice.Severity == BannerSeverity.Notice && notice.PrimaryText == "MODE: PARTY", "Notice surfaces while idle");
        var expiredNotice = state.Evaluate(new[] { new WatchedMemberSnapshot(1, "Self", 200, 200, 0, true, false) }, partyMode, 7.5);
        Assert(expiredNotice.Severity == BannerSeverity.None, "Notice clears after duration expires");

        // Master PluginEnabled = false silences alerts, notices, and reports PluginEnabled == false.
        state.SetNotice("SAVED Alice! (Saved: 1)", "Clutch heal", 2.0, 8.0);
        var masterOff = new Settings { PluginEnabled = false, Mode = TrackingMode.Party, PeopleSaved = 4 };
        var masterOffView = state.Evaluate(party, masterOff, 8.5);
        Assert(masterOffView.Severity == BannerSeverity.None && !masterOffView.PluginEnabled && masterOffView.PeopleSaved == 4 && masterOffView.StatusLine == "Plugin Off",
            "master PluginEnabled=false silences alerts and notices while preserving PeopleSaved count");
    }

    private static void TestPeopleSavedTracker()
    {
        var tracker = new PeopleSavedTracker();

        // 1. First observation records previousHp without opening a heal window.
        var s1 = tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 20, 200, 1, true, false) }, true, false, 10.0, 9.5);
        Assert(s1.Count == 0, "first observation does not count save");

        // 2. Heal within 3.5s self-cast window opens 5s heal window (PreHealHp = 20); hit of 80 leaves 40 HP > 0 -> SAVE!
        var s2 = tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 120, 200, 1, true, false) }, true, false, 11.0, 9.5);
        Assert(s2.Count == 0, "heal itself does not count save before damage");
        var s3 = tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 40, 200, 1, true, false) }, true, false, 15.5, 9.5);
        Assert(s3.Count == 1 && s3[0] == "Alice", "hit > PreHealHp within 5s leaving player alive counts 1 save");

        // 3. Single-save consumption: second hit right after cannot re-consume same heal window.
        var s4 = tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 10, 200, 1, true, false) }, true, false, 15.8, 9.5);
        Assert(s4.Count == 0, "consumed heal window cannot double-count a second hit");

        // 4. Chained heal keeps lowest PreHealHp floor (10 -> 70 -> 150, then 90 damage (150 -> 60) > 10 counts save).
        tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 70, 200, 1, true, false) }, true, false, 20.0, 19.5);
        tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 150, 200, 1, true, false) }, true, false, 21.0, 20.5);
        var sChain = tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 60, 200, 1, true, false) }, true, false, 25.5, 20.5);
        Assert(sChain.Count == 1, "chained heals preserve lowest PreHealHp floor and extend 5s window");

        // 5. Window expires after 5.0s: damage at +5.1s does not count.
        tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 15, 200, 1, true, false) }, true, false, 30.0, 29.0);
        tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 120, 200, 1, true, false) }, true, false, 31.0, 30.5);
        var sExpired = tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 20, 200, 1, true, false) }, true, false, 36.1, 30.5);
        Assert(sExpired.Count == 0, "expired heal window (> 5.0s) does not count save");

        // 6. Player dying from the hit (HitPoints <= 0) does NOT count and clears window.
        tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 15, 200, 1, true, false) }, true, false, 40.0, 39.5);
        tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 100, 200, 1, true, false) }, true, false, 41.0, 40.0);
        var sDied = tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", -5, 200, 1, true, true) }, true, false, 42.0, 40.0);
        Assert(sDied.Count == 0, "lethal hit leaving HP <= 0 does not count save");

        // 7. Guards: no recent self-cast (> 3.5s) and tiny regen (< 5 HP) do not open heal window.
        tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 20, 200, 1, true, false) }, true, false, 50.0, 40.0);
        tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 120, 200, 1, true, false) }, true, false, 51.0, 40.0); // stale cast
        Assert(tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 30, 200, 1, true, false) }, true, false, 52.0, 40.0).Count == 0,
            "heal without recent self cast (> 3.5s) does not open heal window");

        tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 34, 200, 1, true, false) }, true, false, 60.0, 59.8); // +4 < MinHealDelta(5)
        Assert(tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 10, 200, 1, true, false) }, true, false, 61.0, 59.8).Count == 0,
            "regen < MinHealDelta (5) does not open heal window");

        // 8. Town guard ignores heals and hits while InTown() == true.
        tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 15, 200, 1, true, false) }, true, true, 70.0, 69.9);
        tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 150, 200, 1, true, false) }, true, true, 71.0, 70.9);
        Assert(tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 20, 200, 1, true, false) }, true, false, 72.0, 70.9).Count == 0,
            "heals in town do not open heal window");

        // 9. Master-off clears active heal windows.
        tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 15, 200, 1, true, false) }, true, false, 80.0, 79.5);
        tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 150, 200, 1, true, false) }, true, false, 81.0, 80.5);
        tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 150, 200, 1, true, false) }, false, false, 81.5, 80.5); // toggled OFF
        Assert(tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 20, 200, 1, true, false) }, true, false, 82.0, 80.5).Count == 0,
            "turning plugin OFF clears active PeopleSaved heal windows");
    }

    private static void TestHotkeyMatcherAndDeduplication()
    {
        var matcher = new HotkeyMatcher(0.25);
        var togglePlugin = new HotkeyBinding(0x4B, true, true, false); // Ctrl+Shift+K
        var focus = new HotkeyBinding(0x48, true, true, false);        // Ctrl+Shift+H
        var toggle = new HotkeyBinding(0x4A, true, true, false);       // Ctrl+Shift+J
        var critical = new HotkeyBinding(0x54, true, true, false);     // Ctrl+Shift+T

        Assert(togglePlugin.Format() == "Ctrl+Shift+K", "Ctrl+Shift+K format");
        Assert(focus.Format() == "Ctrl+Shift+H", "Ctrl+Shift+H format");
        Assert(toggle.Format() == "Ctrl+Shift+J", "Ctrl+Shift+J format");
        Assert(critical.Format() == "Ctrl+Shift+T", "Ctrl+Shift+T format");
        Assert(HotkeyBinding.Empty.Format() == "None", "Empty hotkey formats as None");

        Assert(HotkeyMatcher.Matches(focus, 0x48, true, true, false), "exact chord matches");
        Assert(!HotkeyMatcher.Matches(focus, 0x48, true, false, false), "missing Shift modifier rejected");
        Assert(!HotkeyMatcher.Matches(focus, 0x48, true, true, true), "extra Alt modifier rejected");
        Assert(!HotkeyMatcher.Matches(HotkeyBinding.Empty, 0x48, true, true, false), "empty binding never matches");

        Assert(matcher.ShouldTrigger(togglePlugin, 0x4B, true, true, false, 10.0), "first TogglePlugin hotkey trigger succeeds");
        Assert(!matcher.ShouldTrigger(togglePlugin, 0x4B, true, true, false, 10.10), "duplicate trigger within 250ms deduplicated");
        Assert(matcher.ShouldTrigger(togglePlugin, 0x4B, true, true, false, 10.26), "trigger after 250ms dedupe window succeeds");

        Assert(matcher.UpdatePollKeyState(0x48, true), "poll rising edge returns true");
        Assert(!matcher.UpdatePollKeyState(0x48, true), "held key in poll does not re-fire");
        Assert(!matcher.UpdatePollKeyState(0x48, false), "key release clears held state");
        Assert(matcher.UpdatePollKeyState(0x48, true), "pressing again after release triggers rising edge");
    }

    private static void TestTargetPolicyRules()
    {
        Assert(TargetPolicy.PartyActionForSlot(0) == "TARGET_SELF_KEY", "slot 0 maps to TARGET_SELF_KEY");
        Assert(TargetPolicy.PartyActionForSlot(1) == "TARGET_FELLOW_1_KEY", "slot 1 maps to TARGET_FELLOW_1_KEY");
        Assert(TargetPolicy.PartyActionForSlot(12) == "TARGET_FELLOW_12_KEY", "slot 12 maps to TARGET_FELLOW_12_KEY");
        Assert(TargetPolicy.PartyActionForSlot(13) is null, "slot > 12 has no party action");

        Assert(TargetPolicy.Evaluate(0, "Invalid", 1, true, true, 0, TargetMethod.Auto, true, 0).Action == TargetActionKind.None,
            "target ID 0 rejected");
        Assert(TargetPolicy.Evaluate(ulong.MaxValue, "Invalid", 1, true, true, 0, TargetMethod.Auto, true, 0).Action == TargetActionKind.None,
            "target ID ulong.MaxValue rejected");
        Assert(TargetPolicy.Evaluate(42, "Alice", 1, true, true, 42, TargetMethod.Auto, true, 0).Action == TargetActionKind.None,
            "already selected target skipped");
        Assert(TargetPolicy.Evaluate(42, "Alice", 1, true, true, 0, TargetMethod.Auto, false, 1.5).Action == TargetActionKind.None,
            "cooldown blocks non-bypassed auto-targeting");
        Assert(TargetPolicy.Evaluate(42, "Alice", 1, true, true, 0, TargetMethod.Auto, true, 1.5).Action == TargetActionKind.SdkSelectObject,
            "bypassCooldown overrides remaining cooldown");
        Assert(TargetPolicy.Evaluate(42, "Alice", 1, false, false, 0, TargetMethod.SdkSelectOnly, true, 0).Action == TargetActionKind.None,
            "missing/deleted entity rejected in SdkSelectOnly");
        Assert(TargetPolicy.Evaluate(42, "Alice", 1, false, true, 0, TargetMethod.Auto, false, 0).Action == TargetActionKind.PartyKeybind,
            "Auto falls back to bound party keybind when entity out of range");
        Assert(TargetPolicy.Evaluate(42, "Alice", 7, false, false, 0, TargetMethod.PartyKeyOnly, true, 0).Action == TargetActionKind.None,
            "unbound party key slot rejected in PartyKeyOnly");
    }

    private static void TestSettingsValidationPersistenceAndFonts()
    {
        var invalid = new Settings
        {
            Mode = (TrackingMode)99,
            TargetingMethod = (TargetMethod)99,
            HpThresholdPercent = 1,
            RecoveryHysteresisPercent = 99,
            DeadAlertSeconds = -5,
            FontSizePixels = 999,
            FlashIntervalMs = 10,
            HudX = double.NaN,
            HudY = 2.5,
            StatusFontSizePixels = 2,
            StatusHudX = -1.0,
            StatusHudY = double.NaN,
            PeopleSaved = -7,
            AutoTargetCooldownSeconds = double.PositiveInfinity
        };
        invalid.Validate();
        Assert(invalid.Mode == TrackingMode.Party && invalid.TargetingMethod == TargetMethod.Auto, "invalid enums reset to defaults");
        Assert(invalid.HpThresholdPercent == 5 && invalid.RecoveryHysteresisPercent == 20, "threshold and hysteresis clamped");
        Assert(invalid.DeadAlertSeconds == 0 && invalid.FontSizePixels == 140 && invalid.FlashIntervalMs == 100, "dead alert, font, and flash interval clamped");
        Assert(invalid.HudX == 0.50 && invalid.HudY == 1.0 && invalid.AutoTargetCooldownSeconds == 3.0, "HudX, HudY, and cooldown clamped/sanitized");
        Assert(invalid.StatusFontSizePixels == 9 && invalid.StatusHudX == 0.0 && invalid.StatusHudY == 0.76 && invalid.PeopleSaved == 0,
            "StatusFontSizePixels, StatusHudX, StatusHudY, and PeopleSaved clamped/sanitized");

        foreach (int px in new[] { 18, 56, 140 })
            Assert(SettingsInput.TryFontSize(px.ToString(), out int parsed) && parsed == px, $"exact integer font size {px}px accepted");
        foreach (string bad in new[] { "", "17", "141", "36.5", "abc", "999999999999" })
            Assert(!SettingsInput.TryFontSize(bad, out _), $"invalid font size rejected: {bad}");

        foreach (int spx in new[] { 9, 14, 48 })
            Assert(SettingsInput.TryStatusFontSize(spx.ToString(), out int parsed) && parsed == spx, $"exact integer status font size {spx}px accepted");
        foreach (string bad in new[] { "", "8", "49", "14.5", "abc" })
            Assert(!SettingsInput.TryStatusFontSize(bad, out _), $"invalid status font size rejected: {bad}");

        string tempDir = Path.Combine(Path.GetTempPath(), "healers-helper-tests-" + Guid.NewGuid());
        try
        {
            var store = new SettingsStore(tempDir);
            var s = store.Load();
            s.PluginEnabled = false;
            s.Mode = TrackingMode.SinglePlayer;
            s.FocusedPlayerId = 12345;
            s.FocusedPlayerName = "Tanky";
            s.FontSizePixels = 72;
            s.DeadAlertSeconds = 9;
            s.ExcludeFromCapture = false;
            s.HudX = 0.25;
            s.HudY = 0.75;
            s.StatusHudEnabled = false;
            s.StatusFontSizePixels = 19;
            s.StatusHudX = 0.15;
            s.StatusHudY = 0.85;
            s.PeopleSaved = 17;
            s.AutoTargetCooldownSeconds = 4.5;
            s.TogglePluginHotkey = new HotkeyBinding(0x50, true, true, false);
            s.FocusTargetHotkey = new HotkeyBinding(0x46, true, false, true);
            store.Save(s);

            var reloaded = store.Load();
            Assert(!reloaded.PluginEnabled && reloaded.PeopleSaved == 17, "JSON round-trip preserves PluginEnabled and PeopleSaved");
            Assert(reloaded.Mode == TrackingMode.SinglePlayer && reloaded.FocusedPlayerId == 12345 && reloaded.FocusedPlayerName == "Tanky",
                "JSON round-trip preserves tracking mode and focus target");
            Assert(reloaded.FontSizePixels == 72 && reloaded.DeadAlertSeconds == 9 && !reloaded.ExcludeFromCapture,
                "JSON round-trip preserves exact FontSizePixels, DeadAlertSeconds, and ExcludeFromCapture");
            Assert(!reloaded.StatusHudEnabled && reloaded.StatusFontSizePixels == 19 && reloaded.StatusHudX == 0.15 && reloaded.StatusHudY == 0.85,
                "JSON round-trip preserves StatusHud settings");
            Assert(reloaded.HudX == 0.25 && reloaded.HudY == 0.75 && reloaded.AutoTargetCooldownSeconds == 4.5,
                "JSON round-trip preserves HudX, HudY, and AutoTargetCooldownSeconds");
            Assert(reloaded.TogglePluginHotkey == new HotkeyBinding(0x50, true, true, false) && reloaded.FocusTargetHotkey == new HotkeyBinding(0x46, true, false, true),
                "JSON round-trip preserves TogglePluginHotkey and FocusTargetHotkey");
            Assert(reloaded.Copy().FontSizePixels == 72 && reloaded.Copy().PeopleSaved == 17, "Copy preserves exact FontSizePixels and PeopleSaved");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private static void TestBannerPresentationAndPositioning()
    {
        var settings = new Settings { FontSizePixels = 64, FlashIntervalMs = 250 };
        var critView = new AlertBannerView(BannerSeverity.CriticalDown, TrackingMode.Party, "DOWN: Tank", "-5/200 HP — CRITICAL", 1, 2, "Tank", -2.5, 1, "", "");
        var lowView = new AlertBannerView(BannerSeverity.LowHp, TrackingMode.Party, "HEAL Alice (30%)", "60/200 HP", 1, 3, "Alice", 30.0, 2, "", "");
        var noticeView = new AlertBannerView(BannerSeverity.Notice, TrackingMode.Party, "MODE: PARTY", "Watching self and party/raid", 0, null, "", null, 2, "", "");
        var idleView = new AlertBannerView(BannerSeverity.None, TrackingMode.Party, "", "", 0, null, "", null, 2, "", "");

        var critOn = BannerPresentation.Compute(critView, settings, false, false, 0.0, 0.0);
        var critOff = BannerPresentation.Compute(critView, settings, false, false, 0.25, 0.0);
        var critOn2 = BannerPresentation.Compute(critView, settings, false, false, 0.50, 0.0);
        Assert(critOn.Visible && critOn.FlashOn && !critOff.FlashOn && critOn2.FlashOn, "CriticalDown alternates flash phase at FlashIntervalMs");
        Assert(critOn.BackgroundColorArgb != critOff.BackgroundColorArgb && critOn.FontSizePixels == 64, "CriticalDown changes colors across flash phases and keeps exact integer font");

        var lowOn = BannerPresentation.Compute(lowView, settings, false, false, 0.0, 0.0);
        var lowOff = BannerPresentation.Compute(lowView, settings, false, false, 0.25, 0.0);
        Assert(lowOn.FlashOn && !lowOff.FlashOn && lowOn.BackgroundColorArgb != critOn.BackgroundColorArgb, "LowHp flashes with distinct amber/orange palette");

        var notice = BannerPresentation.Compute(noticeView, settings, false, false, 0.0, 0.0);
        Assert(notice.Visible && !notice.FlashOn && notice.PrimaryText == "MODE: PARTY", "Notice renders visible non-flashing banner");

        var editIdle = BannerPresentation.Compute(idleView, settings, false, true, 0.0, 12.2);
        Assert(editIdle.Visible && editIdle.PrimaryText == "DDO HEALER'S HELPER — DRAG TO POSITION" && editIdle.SecondaryText.Contains("13s"),
            "Editing while idle renders drag placeholder with ceiling countdown");

        var hiddenIdle = BannerPresentation.Compute(idleView, settings, false, false, 0.0, 0.0);
        Assert(!hiddenIdle.Visible, "Idle non-editing non-demo frame is hidden");

        var demo = BannerPresentation.Compute(idleView, settings, true, false, 0.0, 0.0);
        Assert(demo.Visible && demo.FlashOn && demo.PrimaryText.Contains("PREVIEW"), "Demo preview renders flashing preview banner");

        Assert(BannerPresentation.ComputePosition(-1920, 0, 1920, 1080, 400, 120, 0.0, 0.0) == (-1920, 0),
            "ComputePosition supports negative monitor origin");
        Assert(BannerPresentation.ComputePosition(-1920, 0, 1920, 1080, 400, 120, 1.0, 1.0) == (-400, 960),
            "ComputePosition keeps banner inside bottom-right bounds");
    }

    private static void TestStatusHudPresentation()
    {
        var settings = new Settings { StatusFontSizePixels = 14, HpThresholdPercent = 50, Mode = TrackingMode.Party };
        var normalView = new AlertBannerView(BannerSeverity.None, TrackingMode.Party, "", "", 0, null, "", null, 0, "", "", true, 3);
        var alertView = new AlertBannerView(BannerSeverity.LowHp, TrackingMode.Party, "HEAL Alice (30%)", "60/200 HP", 1, 2, "Alice", 30, 1, "", "", true, 3);

        var normal = StatusHudPresentation.Compute(normalView, settings, false, 0);
        Assert(normal.Visible && normal.FontSizePixels == 14 && !normal.ShowDragBox && normal.Text == "HEALER  PARTY <=50%  •  SAVED 3",
            "StatusHud normal Party formatting");

        var alerting = StatusHudPresentation.Compute(alertView, settings, false, 0);
        Assert(alerting.Text == "HEALER  PARTY • ALERT  •  SAVED 3" && alerting.TextColorArgb != normal.TextColorArgb,
            "StatusHud Party alert formatting and distinct color");

        var focusSettings = new Settings { Mode = TrackingMode.SinglePlayer, FocusedPlayerName = "Tanky", HpThresholdPercent = 45 };
        var focusFrame = StatusHudPresentation.Compute(normalView with { PeopleSaved = 7 }, focusSettings, false, 0);
        Assert(focusFrame.Text == "HEALER  FOCUS: Tanky <=45%  •  SAVED 7", "StatusHud SinglePlayer focus formatting");

        var pausedSettings = new Settings { Mode = TrackingMode.Off };
        var pausedFrame = StatusHudPresentation.Compute(normalView with { PeopleSaved = 2 }, pausedSettings, false, 0);
        Assert(pausedFrame.Text == "HEALER  PAUSED  •  SAVED 2", "StatusHud paused formatting");

        var offSettings = new Settings { PluginEnabled = false };
        var offFrame = StatusHudPresentation.Compute(normalView with { PluginEnabled = false }, offSettings, false, 0);
        Assert(offFrame.Visible && offFrame.Text == "HEALER  OFF", "StatusHud master-off formatting");

        var hiddenSettings = new Settings { StatusHudEnabled = false };
        Assert(!StatusHudPresentation.Compute(normalView, hiddenSettings, false, 0).Visible,
            "StatusHud hidden when StatusHudEnabled=false and not editing");
        var editingHidden = StatusHudPresentation.Compute(normalView, hiddenSettings, true, 9.1);
        Assert(editingHidden.Visible && editingHidden.ShowDragBox && editingHidden.DragHint == "Drag status • locks in 10s",
            "StatusHud drag mode forces visibility with drag box and hint");
    }
}
