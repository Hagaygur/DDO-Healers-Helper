using System;
using System.IO;
using System.Linq;
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
        TestPartyThresholdPrioritizationAndTieBreaking();
        TestRecoveryHysteresis();
        TestDeadAndIncapacitatedTimeoutAndSuppression();
        TestSinglePlayerAndSoloAndOffAndMasterToggle();
        TestExtendedAlertConfigurability();
        TestPeopleSavedTracker();
        TestGamificationStreakClosestCallReflexRanksAndScorecard();
        TestHotkeyMatcherAndDeduplication();
        TestTargetPolicyRules();
        TestTargetRestoreTracker();
        TestSettingsValidationPersistenceAndFonts();
        TestBannerPresentationAndPositioning();
        TestStatusHudPresentation();

        Console.WriteLine($"{_count} checks passed.");
        if (_count < 35) throw new Exception($"Expected >= 35 assertions, ran {_count}");
        if (args.Length == 2 && args[0] == "--contracts") ContractSnapshot.Write(args[1]);
    }

    private static void TestPartyThresholdPrioritizationAndTieBreaking()
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
        long gen1 = lowView.AlertGeneration;
        Assert(lowView.Severity == BannerSeverity.LowHp, "low HP member triggers LowHp severity");
        Assert(lowView.AlertPlayerId == 3 && lowView.AlertPlayerName == "Bob", "lowest HP percent member prioritized");
        Assert(lowView.LowMemberCount == 2, "LowMemberCount counts all active low members");
        Assert(lowView.PrimaryText == "HEAL Bob (20%)", "exact primary percent formatting");
        Assert(lowView.SecondaryText == "40/200 HP • 2 low", "secondary text includes HP ratio and low count");

        Assert(state.Evaluate(twoLow, settings, 2.2).AlertGeneration == gen1, "AlertGeneration unchanged across ticks of same alert");
        var switchedLow = new[]
        {
            new WatchedMemberSnapshot(1, "Self", 200, 200, 0, true, false),
            new WatchedMemberSnapshot(2, "Alice", 20, 200, 1, true, false),  // 10%
            new WatchedMemberSnapshot(3, "Bob", 40, 200, 2, true, false)     // 20%
        };
        long gen2 = state.Evaluate(switchedLow, settings, 2.4).AlertGeneration;
        Assert(gen2 == gen1 + 1, "AlertGeneration increments when prioritized low player changes");

        var tieSelfVsParty = new[]
        {
            new WatchedMemberSnapshot(10, "Alice", 60, 200, 1, true, false),
            new WatchedMemberSnapshot(20, "Self", 60, 200, 0, true, false)
        };
        Assert(state.Evaluate(tieSelfVsParty, settings, 2.6).AlertPlayerId == 20, "equal HP% tie breaks by lower PartySlotIndex");

        var tieSameSlot = new[]
        {
            new WatchedMemberSnapshot(55, "CloneB", 60, 200, 1, true, false),
            new WatchedMemberSnapshot(12, "CloneA", 60, 200, 1, true, false)
        };
        Assert(state.Evaluate(tieSameSlot, settings, 2.8).AlertPlayerId == 12, "equal HP% and slot tie breaks by lower Id");

        var withNullAndDup = new WatchedMemberSnapshot[]
        {
            null!,
            new(2, "Alice", 50, 200, 1, true, false),
            new(2, "Alice", 50, 200, 1, true, false)
        };
        var dedupView = state.Evaluate(withNullAndDup, settings, 2.9);
        Assert(dedupView.LowMemberCount == 1 && dedupView.AlertPlayerId == 2, "Evaluate ignores null entries and deduplicates member IDs");

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

        var noMaxDown = new[] { new WatchedMemberSnapshot(9, "Ghost", 0, 0, 1, true, true) };
        var noMaxView = new HealerState().Evaluate(noMaxDown, settings, 10.0);
        Assert(noMaxView.Severity == BannerSeverity.CriticalDown && noMaxView.SecondaryText.Contains("Incapacitated / Dead"),
            "IsDeadOrDown with MaxHitPoints==0 formats Incapacitated / Dead");

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
        Assert(state.Evaluate(solo, partyMode, 5.2).Severity == BannerSeverity.LowHp, "active LowHp alert overrides active Notice");
        var notice = state.Evaluate(new[] { new WatchedMemberSnapshot(1, "Self", 200, 200, 0, true, false) }, partyMode, 5.5);
        Assert(notice.Severity == BannerSeverity.Notice && notice.PrimaryText == "MODE: PARTY", "Notice surfaces while idle");
        var expiredNotice = state.Evaluate(new[] { new WatchedMemberSnapshot(1, "Self", 200, 200, 0, true, false) }, partyMode, 7.5);
        Assert(expiredNotice.Severity == BannerSeverity.None, "Notice clears after duration expires");

        state.SetNotice("SAVED Alice! (Saved: 1)", "Clutch heal", 2.0, 8.0);
        var masterOff = new Settings { PluginEnabled = false, Mode = TrackingMode.Party, PeopleSaved = 4 };
        var masterOffView = state.Evaluate(party, masterOff, 8.5);
        Assert(masterOffView.Severity == BannerSeverity.None && !masterOffView.PluginEnabled && masterOffView.PeopleSaved == 4 && masterOffView.StatusLine == "Plugin Off",
            "master PluginEnabled=false silences alerts and notices while preserving PeopleSaved count");
    }

    private static void TestExtendedAlertConfigurability()
    {
        // 1. IncludeSelfInPartyMode = false skips slot 0 in Party while keeping party allies, yet allows focusing Self in SinglePlayer.
        var state = new HealerState();
        var members = new[]
        {
            new WatchedMemberSnapshot(1, "Self", 20, 200, 0, true, false),
            new WatchedMemberSnapshot(2, "Ally", 80, 200, 1, true, false)
        };
        var excludeSelfParty = new Settings { Mode = TrackingMode.Party, IncludeSelfInPartyMode = false, HpThresholdPercent = 50 };
        var view = state.Evaluate(members, excludeSelfParty, 10.0);
        Assert(view.Severity == BannerSeverity.LowHp && view.AlertPlayerId == 2 && view.LowMemberCount == 1,
            "IncludeSelfInPartyMode=false skips slot 0 Self in Party mode while alerting on low Ally");

        var focusSelf = new Settings { Mode = TrackingMode.SinglePlayer, IncludeSelfInPartyMode = false, FocusedPlayerId = 1, FocusedPlayerName = "Self" };
        Assert(state.Evaluate(members, focusSelf, 11.0).AlertPlayerId == 1,
            "IncludeSelfInPartyMode=false still allows explicitly focusing Self in SinglePlayer mode");

        // 2. SuppressAlertsInTown = true pauses LowHp/CriticalDown in town while preserving Notice.
        var townState = new HealerState();
        var townSettings = new Settings { Mode = TrackingMode.Party, SuppressAlertsInTown = true };
        var critMembers = new[] { new WatchedMemberSnapshot(2, "Ally", -4, 200, 1, true, true) };
        var inTownIdle = townState.Evaluate(critMembers, townSettings, 20.0, inTown: true);
        Assert(inTownIdle.Severity == BannerSeverity.None && inTownIdle.StatusLine == "In Town (alerts paused)",
            "SuppressAlertsInTown=true suppresses alerts when inTown=true");
        townState.SetNotice("SCORECARD COPIED", "Paste into chat", 2.0, 20.5);
        Assert(townState.Evaluate(critMembers, townSettings, 21.0, inTown: true).Severity == BannerSeverity.Notice,
            "SuppressAlertsInTown=true still displays user Notice overlays in town");
        Assert(townState.Evaluate(critMembers, townSettings, 25.0, inTown: false).Severity == BannerSeverity.CriticalDown,
            "SuppressAlertsInTown=true resumes alerts immediately outside town");

        // 3. MaxAlertSeconds times out persistent LowHp, surfaces next low ally, prevents re-spam below hysteresis, escalates on CriticalDown, and re-arms after recovery.
        var maxState = new HealerState();
        var maxSettings = new Settings { HpThresholdPercent = 50, RecoveryHysteresisPercent = 3, MaxAlertSeconds = 4, DeadAlertSeconds = 6 };
        var aliceLow = new[] { new WatchedMemberSnapshot(2, "Alice", 40, 200, 1, true, false) };
        Assert(maxState.Evaluate(aliceLow, maxSettings, 100.0).Severity == BannerSeverity.LowHp, "MaxAlertSeconds starts LowHp alert");
        Assert(maxState.Evaluate(aliceLow, maxSettings, 103.9).Severity == BannerSeverity.LowHp, "MaxAlertSeconds keeps LowHp before timeout");

        var twoWithBob = new[]
        {
            new WatchedMemberSnapshot(2, "Alice", 40, 200, 1, true, false), // timed out at 104.0
            new WatchedMemberSnapshot(3, "Bob", 80, 200, 2, true, false)    // fresh low at 104.0
        };
        var surfacedBob = maxState.Evaluate(twoWithBob, maxSettings, 104.0);
        Assert(surfacedBob.Severity == BannerSeverity.LowHp && surfacedBob.AlertPlayerId == 3 && surfacedBob.LowMemberCount == 1,
            "MaxAlertSeconds suppresses timed-out LowHp member and surfaces next low ally");

        // Fluctuating below hysteresis (52% < 53%) stays latched and suppressed without re-spamming.
        var alice52 = new[] { new WatchedMemberSnapshot(2, "Alice", 104, 200, 1, true, false) };
        Assert(maxState.Evaluate(alice52, maxSettings, 105.0).Severity == BannerSeverity.None,
            "MaxAlertSeconds does not re-trigger while hovering below recovery hysteresis");

        // Escalating to CriticalDown immediately alerts despite prior LowHp timeout!
        var aliceDown = new[] { new WatchedMemberSnapshot(2, "Alice", -2, 200, 1, true, true) };
        Assert(maxState.Evaluate(aliceDown, maxSettings, 106.0).Severity == BannerSeverity.CriticalDown,
            "CriticalDown overrides prior MaxAlertSeconds LowHp timeout");

        // Full recovery above hysteresis (>= 53%) re-arms LowHp for future drops.
        var aliceHealthy = new[] { new WatchedMemberSnapshot(2, "Alice", 150, 200, 1, true, false) };
        Assert(maxState.Evaluate(aliceHealthy, maxSettings, 107.0).Severity == BannerSeverity.None, "healed above hysteresis clears MaxAlertSeconds latch");
        Assert(maxState.Evaluate(aliceLow, maxSettings, 108.0).Severity == BannerSeverity.LowHp, "subsequent drop re-triggers fresh LowHp alert");
    }

    private static void TestPeopleSavedTracker()
    {
        var tracker = new PeopleSavedTracker();

        var s1 = tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 20, 200, 1, true, false) }, true, false, 10.0, 9.5);
        Assert(s1.Count == 0, "first observation does not count save");

        var s2 = tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 120, 200, 1, true, false) }, true, false, 11.0, 9.5);
        Assert(s2.Count == 0, "heal itself does not count save before damage");
        var s3 = tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 40, 200, 1, true, false) }, true, false, 15.5, 9.5);
        Assert(s3.Count == 1 && s3[0] == "Alice", "hit > PreHealHp within 5s leaving player alive counts 1 save");

        var s4 = tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 10, 200, 1, true, false) }, true, false, 15.8, 9.5);
        Assert(s4.Count == 0, "consumed heal window cannot double-count a second hit");

        tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 70, 200, 1, true, false) }, true, false, 20.0, 19.5);
        tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 150, 200, 1, true, false) }, true, false, 21.0, 20.5);
        var sChain = tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 60, 200, 1, true, false) }, true, false, 25.5, 20.5);
        Assert(sChain.Count == 1, "chained heals preserve lowest PreHealHp floor and extend 5s window");

        tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 20, 200, 1, true, false) }, true, false, 26.0, 25.8);
        tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 100, 200, 1, true, false) }, true, false, 26.5, 26.2);
        Assert(tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 80, 200, 1, true, false) }, true, false, 27.0, 26.2).Count == 0,
            "damageTaken == PreHealHp (20 == 20) does not count save yet");
        Assert(tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 79, 200, 1, true, false) }, true, false, 27.2, 26.2).Count == 1,
            "cumulative multi-hit damage across ticks (20 + 1 > 20) counts save");

        tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 15, 200, 1, true, false) }, true, false, 30.0, 29.0);
        tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 120, 200, 1, true, false) }, true, false, 31.0, 30.5);
        var sExpired = tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 20, 200, 1, true, false) }, true, false, 36.1, 30.5);
        Assert(sExpired.Count == 0, "expired heal window (> 5.0s) does not count save");

        tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 15, 200, 1, true, false) }, true, false, 40.0, 39.5);
        tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 100, 200, 1, true, false) }, true, false, 41.0, 40.0);
        var sDied = tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", -5, 200, 1, true, true) }, true, false, 42.0, 40.0);
        Assert(sDied.Count == 0, "lethal hit leaving HP <= 0 does not count save");

        tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 15, 200, 1, true, false) }, true, false, 43.0, 42.8);
        tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 100, 200, 1, true, false) }, true, false, 43.5, 43.2);
        Assert(tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 10, 200, 1, true, true) }, true, false, 44.0, 43.2).Count == 0,
            "IsDeadOrDown == true even with HitPoints > 0 rejects save and clears window");

        var gearTracker = new PeopleSavedTracker();
        gearTracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Barb", 1000, 1000, 1, true, false) }, true, false, 10.0, 9.9);
        gearTracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Barb", 1400, 1400, 1, true, false) }, true, false, 10.5, 10.4);
        Assert(gearTracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Barb", 300, 1400, 1, true, false) }, true, false, 11.0, 10.4).Count == 0,
            "pure MaxHP gear/buff increase does not open a clutch heal window");

        gearTracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Barb", 150, 1400, 1, true, false) }, true, false, 20.0, 19.9);
        gearTracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Barb", 1400, 1400, 1, true, false) }, true, false, 20.5, 20.4);
        Assert(gearTracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Barb", 1000, 1000, 1, true, false) }, true, false, 21.0, 20.4).Count == 0,
            "Rage/CON MaxHP drop (1400/1400 -> 1000/1000) does not falsely count as enemy damage save");
        Assert(gearTracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Barb", 800, 1000, 1, true, false) }, true, false, 22.0, 20.4).Count == 1,
            "real 200 damage after Rage drop still counts against active 150 PreHealHp window");

        var overflowTracker = new PeopleSavedTracker();
        overflowTracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Tank", int.MinValue, 500, 1, true, true) }, true, false, 1.0, 0.9);
        Assert(overflowTracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Tank", 250, 500, 1, true, false) }, true, false, 1.5, 1.4).Count == 0,
            "extreme int.MinValue recovery does not overflow signed int delta");

        // Custom SaveWindowSeconds (10s), SelfCastWindowSeconds (8s), and MinHealDelta (50) honored.
        var customTracker = new PeopleSavedTracker();
        customTracker.ProcessScanDetailed(new[] { new WatchedMemberSnapshot(2, "Tank", 30, 300, 1, true, false) }, true, false, 1.0, 0.5, 10.0, 50, 8.0);
        customTracker.ProcessScanDetailed(new[] { new WatchedMemberSnapshot(2, "Tank", 60, 300, 1, true, false) }, true, false, 7.0, 0.5, 10.0, 50, 8.0); // +30 < 50 rejected
        Assert(customTracker.ProcessScanDetailed(new[] { new WatchedMemberSnapshot(2, "Tank", 10, 300, 1, true, false) }, true, false, 8.0, 0.5, 10.0, 50, 8.0).Count == 0,
            "custom MinHealDelta=50 rejects +30 heal");

        customTracker.ProcessScanDetailed(new[] { new WatchedMemberSnapshot(2, "Tank", 250, 300, 1, true, false) }, true, false, 8.2, 0.5, 10.0, 50, 8.0); // 7.7s <= 8.0s cast window, +240 >= 50
        var customSaved = customTracker.ProcessScanDetailed(new[] { new WatchedMemberSnapshot(2, "Tank", 50, 300, 1, true, false) }, true, false, 17.5, 0.5, 10.0, 50, 8.0); // +9.3s <= 10.0s save window
        Assert(customSaved.Count == 1 && customSaved[0].OverkillSaved == 190,
            "custom SelfCastWindowSeconds=8s and SaveWindowSeconds=10s honored with exact OverkillSaved");

        tracker.Reset();
        tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 20, 200, 1, true, false) }, true, false, 50.0, 40.0);
        tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 120, 200, 1, true, false) }, true, false, 51.0, 40.0);
        Assert(tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 30, 200, 1, true, false) }, true, false, 52.0, 40.0).Count == 0,
            "heal without recent self cast (> 3.5s) does not open heal window");

        tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 34, 200, 1, true, false) }, true, false, 60.0, 59.8);
        Assert(tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 10, 200, 1, true, false) }, true, false, 61.0, 59.8).Count == 0,
            "regen < MinHealDelta (5) does not open heal window");

        tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 15, 200, 1, true, false) }, true, false, 70.0, 69.9);
        tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 150, 200, 1, true, false) }, true, false, 70.5, 70.4);
        tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 150, 200, 1, true, false) }, true, true, 71.0, 70.4);
        Assert(tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 20, 200, 1, true, false) }, true, false, 72.0, 70.4).Count == 0,
            "entering town clears active heal window");

        tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 15, 200, 1, true, false) }, true, false, 80.0, 79.5);
        tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 150, 200, 1, true, false) }, true, false, 81.0, 80.5);
        tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 150, 200, 1, true, false) }, false, false, 81.5, 80.5);
        Assert(tracker.ProcessScan(new[] { new WatchedMemberSnapshot(2, "Alice", 20, 200, 1, true, false) }, true, false, 82.0, 80.5).Count == 0,
            "turning plugin OFF clears active PeopleSaved heal windows");
    }

    private static void TestGamificationStreakClosestCallReflexRanksAndScorecard()
    {
        // 1. Streak & True Death vs Incapacitated vs Corpse Bubble vs Town
        var stats = new Settings();
        var streak = new StreakTracker();

        HealerStats.ApplyClutchSave(stats, new ClutchSaveEvent(2, "Tank", 11, 200, 5.5, 80));
        HealerStats.ApplyClutchSave(stats, new ClutchSaveEvent(3, "Rogue", 10, 200, 5.0, 140));
        Assert(stats.PeopleSaved == 2 && stats.CurrentSaveStreak == 2 && stats.BestSaveStreak == 2,
            "saves increment PeopleSaved, CurrentSaveStreak, and BestSaveStreak");
        Assert(stats.OnePercentClubSaves == 1 && stats.BiggestOverkillSaved == 140,
            "5.0% boundary counts for 1% Club while 5.5% does not, and BiggestOverkillSaved updates");
        Assert(stats.ClosestCallPlayerName == "Rogue" && stats.ClosestCallPreHealHp == 10 && Math.Abs(stats.ClosestCallPreHealPercent - 5.0) < 0.01,
            "ClosestCall selects lower PreHealPercent (5.0% < 5.5%)");

        // Equal percent tie-break by lower PreHealHp (5/100 = 5.0% vs 10/200 = 5.0%).
        HealerStats.ApplyClutchSave(stats, new ClutchSaveEvent(4, "Wizard", 5, 100, 5.0, 90));
        Assert(stats.ClosestCallPlayerName == "Wizard" && stats.ClosestCallPreHealHp == 5 && stats.OnePercentClubSaves == 2,
            "ClosestCall breaks equal PreHealPercent tie by lower PreHealHp");

        // Incapacitated (-5 HP, IsDeadOrDown = true, IsTrulyDead = false) does NOT reset streak!
        var incapParty = new[] { new WatchedMemberSnapshot(2, "Tank", -5, 200, 1, true, IsDeadOrDown: true, IsTrulyDead: false) };
        Assert(!streak.ProcessWatched(incapParty, incapParty, true, false, stats) && stats.CurrentSaveStreak == 3,
            "healable LifeState.Incapacitated (-5 HP) never resets CurrentSaveStreak");

        // True death (LifeState.Dead -> IsTrulyDead = true) resets CurrentSaveStreak to 0 while keeping BestSaveStreak == 3.
        var deadParty = new[] { new WatchedMemberSnapshot(2, "Tank", -15, 200, 1, true, IsDeadOrDown: true, IsTrulyDead: true) };
        Assert(streak.ProcessWatched(deadParty, deadParty, true, false, stats) && stats.CurrentSaveStreak == 0 && stats.BestSaveStreak == 3,
            "IsTrulyDead resets CurrentSaveStreak to 0 while preserving BestSaveStreak");

        // Subsequent save brings streak to 1; corpse staying dead (and leaving/re-entering entity bubble at <= 0 HP) does NOT re-reset!
        HealerStats.ApplyClutchSave(stats, new ClutchSaveEvent(3, "Rogue", 2, 200, 1.0, 200));
        Assert(stats.CurrentSaveStreak == 1 && stats.OnePercentClubSaves == 3 && stats.ClosestCallPlayerName == "Rogue", "1.0% save updates streak and ClosestCall");

        var corpseOutBubble = new[] { new WatchedMemberSnapshot(2, "Tank", -15, 200, 1, true, IsDeadOrDown: true, IsTrulyDead: false) };
        streak.ProcessWatched(corpseOutBubble, corpseOutBubble, true, false, stats);
        streak.ProcessWatched(deadParty, deadParty, true, false, stats);
        Assert(stats.CurrentSaveStreak == 1, "unrevived corpse leaving/re-entering entity bubble does not re-reset streak");

        // Revival (HP > 0 and alive) re-arms death tracking so dying a second time resets streak again.
        var revived = new[] { new WatchedMemberSnapshot(2, "Tank", 150, 200, 1, true, false, false) };
        streak.ProcessWatched(revived, revived, true, false, stats);
        Assert(streak.ProcessWatched(deadParty, deadParty, true, false, stats) && stats.CurrentSaveStreak == 0,
            "revived ally dying again resets CurrentSaveStreak");

        // Town `/death` latches corpse without wiping active streak.
        HealerStats.ApplyClutchSave(stats, new ClutchSaveEvent(4, "Wizard", 15, 100, 15.0, 50));
        var townCorpse = new[] { new WatchedMemberSnapshot(4, "Wizard", 0, 100, 2, true, true, IsTrulyDead: true) };
        Assert(!streak.ProcessWatched(townCorpse, townCorpse, true, inTown: true, stats) && stats.CurrentSaveStreak == 1,
            "death in town latches corpse without resetting active streak");

        // 2. Reflex Timer ([50..10000ms], sub-50ms ignored without burning alert, >10000ms rejected, single sample per AlertGeneration)
        var reflex = new ReflexTracker();
        var reflexSettings = new Settings();
        var alertGen1 = new AlertBannerView(BannerSeverity.LowHp, TrackingMode.Party, "HEAL Tank (20%)", "40/200 HP", 1, 2, "Tank", 20, 1, "", "");

        reflex.OnAlertEvaluated(alertGen1, 100.0);
        Assert(reflex.TryRecordSelfCast(100.03, reflexSettings) is null && reflexSettings.ReflexSampleCount == 0,
            "sub-50ms pre-cast (30ms) ignored without consuming active alert");
        Assert(reflex.TryRecordSelfCast(100.05, reflexSettings) == 50 && reflexSettings.BestReflexMs == 50 && reflexSettings.AvgReflexMs == 50 && reflexSettings.ReflexSampleCount == 1,
            "50ms boundary reflex sample recorded");
        Assert(reflex.TryRecordSelfCast(100.40, reflexSettings) is null && reflexSettings.ReflexSampleCount == 1,
            "second cast during same AlertGeneration does not double-count");

        var alertGen2 = alertGen1 with { AlertGeneration = 2 };
        reflex.OnAlertEvaluated(alertGen2, 200.0);
        Assert(reflex.TryRecordSelfCast(210.0, reflexSettings) == 10000 && reflexSettings.BestReflexMs == 50 && reflexSettings.AvgReflexMs == 5025 && reflexSettings.ReflexSampleCount == 2,
            "10000ms upper boundary recorded and running average computed accurately");

        var alertGen3 = alertGen1 with { AlertGeneration = 3 };
        reflex.OnAlertEvaluated(alertGen3, 300.0);
        Assert(reflex.TryRecordSelfCast(310.01, reflexSettings) is null && reflexSettings.ReflexSampleCount == 2,
            "> 10000ms late cast rejected");

        reflexSettings.PeopleSaved = 9;
        reflexSettings.CurrentSaveStreak = 4;
        reflexSettings.BestSaveStreak = 7;
        reflexSettings.ClosestCallPlayerName = "Tank";
        reflexSettings.ClosestCallPreHealHp = 3;
        reflexSettings.ClosestCallPreHealPercent = 1.5;
        reflexSettings.BiggestOverkillSaved = 250;
        reflexSettings.OnePercentClubSaves = 2;
        reflexSettings.ResetLiveStats();
        Assert(reflexSettings.PeopleSaved == 0 && reflexSettings.CurrentSaveStreak == 0 && reflexSettings.BestSaveStreak == 0
               && reflexSettings.ClosestCallPlayerName == "" && reflexSettings.ClosestCallPreHealHp == 0 && reflexSettings.ClosestCallPreHealPercent == 0.0
               && reflexSettings.BiggestOverkillSaved == 0 && reflexSettings.OnePercentClubSaves == 0
               && reflexSettings.BestReflexMs == 0 && reflexSettings.AvgReflexMs == 0 && reflexSettings.ReflexSampleCount == 0,
            "ResetLiveStats clears all 11 persisted gamification fields");

        // 3. All 8 Prestige Rank boundaries
        Assert(HealerStats.RankForSaves(0) == "Initiate"
               && HealerStats.RankForSaves(1) == "Field Medic" && HealerStats.RankForSaves(4) == "Field Medic"
               && HealerStats.RankForSaves(5) == "Combat Healer" && HealerStats.RankForSaves(14) == "Combat Healer"
               && HealerStats.RankForSaves(15) == "Lifeweaver" && HealerStats.RankForSaves(29) == "Lifeweaver"
               && HealerStats.RankForSaves(30) == "Beacon of Hope" && HealerStats.RankForSaves(59) == "Beacon of Hope"
               && HealerStats.RankForSaves(60) == "Soulguard" && HealerStats.RankForSaves(99) == "Soulguard"
               && HealerStats.RankForSaves(100) == "Reaper's Bane" && HealerStats.RankForSaves(249) == "Reaper's Bane"
               && HealerStats.RankForSaves(250) == "Immortal Anchor",
            "all 8 RankForSaves prestige tier boundaries match spec");

        // 4. Pure printable ASCII Chat Scorecard (fresh, realistic, and adversarial int.MaxValue + Unicode/newlines)
        static bool IsValidScorecard(string s) =>
            s.Length is >= 1 and <= HealerStats.MaxScorecardLength
            && !s.Contains('\n') && !s.Contains('\r')
            && s.All(c => c is >= (char)0x20 and <= (char)0x7E);

        string freshCard = HealerStats.FormatChatScorecard(new Settings());
        Assert(IsValidScorecard(freshCard) && freshCard.Contains("Initiate: 0 saved"), "fresh scorecard is valid printable ASCII <= 185 chars");

        string richCard = HealerStats.FormatChatScorecard(stats);
        Assert(IsValidScorecard(richCard) && richCard.Contains("1% Club:") && richCard.Contains("Closest: Rogue"),
            "populated scorecard includes 1% Club and Closest Call in pure ASCII <= 185 chars");

        string adversarialCard = HealerStats.FormatChatScorecard(new Settings
        {
            PeopleSaved = int.MaxValue,
            CurrentSaveStreak = int.MaxValue,
            BestSaveStreak = int.MaxValue,
            OnePercentClubSaves = int.MaxValue,
            ClosestCallPlayerName = "🔥\r\n" + new string('Z', 90) + " — • ‘Tank’",
            ClosestCallPreHealPercent = 99.99,
            ClosestCallPreHealHp = int.MaxValue,
            BestReflexMs = 9999,
            AvgReflexMs = 9999,
            ReflexSampleCount = int.MaxValue,
            BiggestOverkillSaved = int.MaxValue
        });
        Assert(IsValidScorecard(adversarialCard), $"adversarial scorecard stays pure printable ASCII <= 185 chars (len={adversarialCard.Length})");
    }

    private static void TestHotkeyMatcherAndDeduplication()
    {
        var matcher = new HotkeyMatcher(0.25);
        var togglePlugin = new HotkeyBinding(0x4B, true, true, false);
        var focus = new HotkeyBinding(0x48, true, true, false);
        var toggle = new HotkeyBinding(0x4A, true, true, false);
        var critical = new HotkeyBinding(0x54, true, true, false);

        Assert(togglePlugin.Format() == "Ctrl+Shift+K", "Ctrl+Shift+K format");
        Assert(focus.Format() == "Ctrl+Shift+H", "Ctrl+Shift+H format");
        Assert(toggle.Format() == "Ctrl+Shift+J", "Ctrl+Shift+J format");
        Assert(critical.Format() == "Ctrl+Shift+T", "Ctrl+Shift+T format");
        Assert(new HotkeyBinding(0x70, false, false, true).Format() == "Alt+F1"
               && new HotkeyBinding(0x7B, true, false, false).Format() == "Ctrl+F12"
               && new HotkeyBinding(0x35, true, false, false).Format() == "Ctrl+5"
               && new HotkeyBinding(0x63, false, true, false).Format() == "Shift+Num3",
            "Format supports function keys, digits, and numpad keys");
        Assert(HotkeyBinding.Empty.Format() == "None", "Empty hotkey formats as None");

        Assert(HotkeyMatcher.Matches(focus, 0x48, true, true, false), "exact chord matches");
        Assert(!HotkeyMatcher.Matches(focus, 0x48, true, false, false), "missing Shift modifier rejected");
        Assert(!HotkeyMatcher.Matches(focus, 0x48, true, true, true), "extra Alt modifier rejected");
        Assert(!HotkeyMatcher.Matches(HotkeyBinding.Empty, 0x48, true, true, false), "empty binding never matches");
        Assert(HotkeyMatcher.IsModifierKey(0x11) && HotkeyMatcher.IsModifierKey(0x5B) && !HotkeyMatcher.Matches(new HotkeyBinding(0x11, true, false, false), 0x11, true, false, false),
            "modifier and Win virtual keys rejected");

        Assert(matcher.ShouldTrigger(togglePlugin, 0x4B, true, true, false, 10.0), "first TogglePlugin hotkey trigger succeeds");
        Assert(!matcher.ShouldTrigger(togglePlugin, 0x4B, true, true, false, 10.10), "duplicate trigger within 250ms deduplicated");
        Assert(matcher.ShouldTrigger(togglePlugin, 0x4B, true, true, false, 10.26), "trigger after 250ms dedupe window succeeds");

        Assert(matcher.UpdatePollKeyState(0x48, true), "poll rising edge returns true");
        Assert(!matcher.UpdatePollKeyState(0x48, true), "held key in poll does not re-fire");
        Assert(!matcher.UpdatePollKeyState(0x48, false), "key release clears held state");
        Assert(matcher.UpdatePollKeyState(0x48, true), "pressing again after release triggers rising edge");

        Assert(matcher.UpdateSdkKeyState(0x4B, true), "SDK KeyDown rising edge accepted");
        Assert(!matcher.UpdateSdkKeyState(0x4B, true), "SDK auto-repeat while held suppressed");
        matcher.ReleaseSdkKey(0x4B);
        Assert(matcher.UpdateSdkKeyState(0x4B, true), "SDK KeyUp allows subsequent press");
    }

    private static void TestTargetPolicyRules()
    {
        Assert(TargetPolicy.PartyActionForSlot(0) == "TARGET_SELF_KEY", "slot 0 maps to TARGET_SELF_KEY");
        Assert(TargetPolicy.PartyActionForSlot(1) == "TARGET_FELLOW_1_KEY", "slot 1 maps to TARGET_FELLOW_1_KEY");
        Assert(TargetPolicy.PartyActionForSlot(12) == "TARGET_FELLOW_12_KEY", "slot 12 maps to TARGET_FELLOW_12_KEY");
        Assert(TargetPolicy.PartyActionForSlot(13) is null && TargetPolicy.PartyActionForSlot(-1) is null, "slot -1 and > 12 have no party action");

        Assert(TargetPolicy.Evaluate(42, "Alice", 1, true, true, 0, TargetMethod.Off, true, 0).Action == TargetActionKind.None,
            "TargetMethod.Off always returns None");
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
        Assert(TargetPolicy.Evaluate(99, "Outsider", -1, false, true, 0, TargetMethod.PartyKeyOnly, true, 0).Action == TargetActionKind.None
               && TargetPolicy.Evaluate(99, "Outsider", -1, false, true, 0, TargetMethod.Auto, true, 0).Action == TargetActionKind.None,
            "external focused player (slot -1) out of range has no party key fallback");

        // TargetPolicy.EvaluateRestore rules (cooldown-free, dead-corpse rejection, SDK & party fallback)
        Assert(TargetPolicy.EvaluateRestore(900, "Boss", -1, true, false, false, 2, TargetMethod.Off) is { Action: TargetActionKind.None, Status: "Targeting off" },
            "EvaluateRestore rejects TargetMethod.Off");
        Assert(TargetPolicy.EvaluateRestore(0, "Boss", -1, true, false, false, 2, TargetMethod.Auto) is { Action: TargetActionKind.None, Status: "No valid target ID" }
               && TargetPolicy.EvaluateRestore(ulong.MaxValue, "Boss", -1, true, false, false, 2, TargetMethod.Auto) is { Action: TargetActionKind.None, Status: "No valid target ID" },
            "EvaluateRestore rejects 0 and ulong.MaxValue target IDs");
        Assert(TargetPolicy.EvaluateRestore(900, "Boss", -1, true, false, false, 900, TargetMethod.Auto) is { Action: TargetActionKind.None, Status: "Already targeting Boss" },
            "EvaluateRestore skips already-selected target");
        Assert(TargetPolicy.EvaluateRestore(900, "Boss", -1, true, isDeadNonPlayer: true, false, 2, TargetMethod.Auto) is { Action: TargetActionKind.None, Status: "Original target Boss is dead" },
            "EvaluateRestore rejects dead non-player monster corpse");
        Assert(TargetPolicy.EvaluateRestore(900, "Boss", -1, true, isDeadNonPlayer: false, false, 2, TargetMethod.Auto) is { Action: TargetActionKind.SdkSelectObject, TargetId: 900, Status: "Restored target: Boss" }
               && TargetPolicy.Evaluate(900, "Boss", -1, true, false, 2, TargetMethod.Auto, bypassCooldown: false, cooldownRemainingSeconds: 2.5).Action == TargetActionKind.None,
            "EvaluateRestore selects live entity via SDK immediately without cooldown gating");
        Assert(TargetPolicy.EvaluateRestore(900, "   ", -1, true, false, false, 2, TargetMethod.Auto).Status == "Restored target: previous target",
            "EvaluateRestore falls back to 'previous target' when name is blank");
        Assert(TargetPolicy.EvaluateRestore(42, "Tank", 1, entityInRange: false, isDeadNonPlayer: false, partyKeyBound: true, currentSelectedId: 2, TargetMethod.Auto)
               is { Action: TargetActionKind.PartyKeybind, PartyActionName: "TARGET_FELLOW_1_KEY", Status: "Restored target: Tank (TARGET_FELLOW_1_KEY)" },
            "EvaluateRestore falls back to bound party keybind when ally entity is out of range");
        Assert(TargetPolicy.EvaluateRestore(42, "Tank", 1, entityInRange: false, isDeadNonPlayer: false, partyKeyBound: true, currentSelectedId: 2, TargetMethod.SdkSelectOnly)
               is { Action: TargetActionKind.None, Status: "Original target Tank unavailable" },
            "EvaluateRestore honors SdkSelectOnly when entity is out of range");
        Assert(TargetPolicy.EvaluateRestore(42, "Tank", 2, entityInRange: true, isDeadNonPlayer: false, partyKeyBound: true, currentSelectedId: 3, TargetMethod.PartyKeyOnly)
               is { Action: TargetActionKind.PartyKeybind, PartyActionName: "TARGET_FELLOW_2_KEY", Status: "Restored target: Tank (TARGET_FELLOW_2_KEY)" }
               && TargetPolicy.EvaluateRestore(900, "Boss", -1, entityInRange: true, isDeadNonPlayer: false, partyKeyBound: true, currentSelectedId: 3, TargetMethod.PartyKeyOnly)
               is { Action: TargetActionKind.None, Status: "Original target Boss unavailable" },
            "EvaluateRestore honors PartyKeyOnly for party slots and rejects non-party targets");
    }

    private static void TestTargetRestoreTracker()
    {
        var settings = new Settings
        {
            HpThresholdPercent = 50,
            RecoveryHysteresisPercent = 3,
            MinHealDelta = 5,
            RestorePreviousTargetAfterHeal = true,
            RestoreTargetDelaySeconds = 0.2
        };
        int recoverAt = 53;

        // 1. Invalid prior targets leave tracker inactive; blank prior name defaults cleanly.
        var tracker = new TargetRestoreTracker();
        var aliceLow = new WatchedMemberSnapshot(2, "Alice", 60, 200, 1, true, false);
        tracker.RecordSwitch(0, "None", -1, aliceLow, recoverAt, 1.0);
        Assert(!tracker.IsActive, "priorSelectedId == 0 does not arm TargetRestoreTracker");
        tracker.RecordSwitch(ulong.MaxValue, "Invalid", -1, aliceLow, recoverAt, 1.0);
        Assert(!tracker.IsActive, "priorSelectedId == ulong.MaxValue does not arm TargetRestoreTracker");
        tracker.RecordSwitch(2, "Alice", 1, aliceLow, recoverAt, 1.0);
        Assert(!tracker.IsActive, "priorSelectedId == switchedTo.Id does not arm TargetRestoreTracker");

        // 2. Standard Boss -> Alice -> healed >= threshold+hysteresis -> RestoreTargetDelaySeconds -> Boss (one-shot).
        tracker.RecordSwitch(900, "Raid Boss", -1, aliceLow, recoverAt, 10.0);
        Assert(tracker.IsActive, "valid Boss -> Alice switch arms TargetRestoreTracker");
        Assert(tracker.EvaluateRestore(new[] { aliceLow }, settings, currentSelectedId: 2, BannerSeverity.LowHp, 2, false, true, 10.1) is null,
            "unhealed 30% player does not restore");
        var alice52 = new WatchedMemberSnapshot(2, "Alice", 104, 200, 1, true, false); // 52% < 53%
        Assert(tracker.EvaluateRestore(new[] { alice52 }, settings, currentSelectedId: 2, BannerSeverity.LowHp, 2, false, true, 10.4) is null,
            "52% below 53% recovery hysteresis does not start restore delay");
        var aliceHealed = new WatchedMemberSnapshot(2, "Alice", 120, 200, 1, true, false); // 60% >= 53%
        Assert(tracker.EvaluateRestore(new[] { aliceHealed }, settings, currentSelectedId: 2, BannerSeverity.None, null, false, true, 10.6) is null,
            "healed tick t=10.6 starts 0.2s restore countdown without restoring immediately");
        Assert(tracker.EvaluateRestore(new[] { aliceHealed }, settings, currentSelectedId: 2, BannerSeverity.None, null, false, true, 10.75) is null,
            "before 0.2s delay elapses (0.15s), restore stays pending");
        var restored = tracker.EvaluateRestore(new[] { aliceHealed }, settings, currentSelectedId: 2, BannerSeverity.None, null, false, true, 10.81);
        Assert(restored is { TargetId: 900, TargetName: "Raid Boss", PartySlotIndex: -1, ExpectedCurrentTargetId: 2 } && !tracker.IsActive,
            "once healed and 0.2s delay elapses, returns original Boss candidate with ExpectedCurrentTargetId and resets");
        Assert(tracker.EvaluateRestore(new[] { aliceHealed }, settings, currentSelectedId: 2, BannerSeverity.None, null, false, true, 10.9) is null,
            "completed restore is one-shot and does not re-fire");

        // 3. Multi-injury chaining (Boss -> Alice -> Bob -> Boss), unsettled grace chaining, and direct return to original.
        var bobLow = new WatchedMemberSnapshot(3, "Bob", 30, 200, 2, true, false);
        var bobHealed = new WatchedMemberSnapshot(3, "Bob", 180, 200, 2, true, false);
        tracker.RecordSwitch(900, "", -1, aliceLow, recoverAt, 20.0);
        _ = tracker.EvaluateRestore(new[] { aliceLow, bobLow }, settings, currentSelectedId: 2, BannerSeverity.LowHp, 3, false, true, 20.2);
        tracker.RecordSwitch(2, "Alice", 1, bobLow, recoverAt, 21.0); // chain from Alice -> Bob
        Assert(tracker.EvaluateRestore(new[] { aliceHealed, bobLow }, settings, currentSelectedId: 3, BannerSeverity.LowHp, 3, false, true, 21.5) is null,
            "chained Boss -> Alice -> Bob waits for Bob to be healed even after Alice recovers");
        _ = tracker.EvaluateRestore(new[] { aliceHealed, bobHealed }, settings, currentSelectedId: 3, BannerSeverity.None, null, false, true, 22.0);
        var chainedRestore = tracker.EvaluateRestore(new[] { aliceHealed, bobHealed }, settings, currentSelectedId: 3, BannerSeverity.None, null, false, true, 22.25);
        Assert(chainedRestore is { TargetId: 900, TargetName: "previous target", PartySlotIndex: -1 },
            "chained Boss -> Alice -> Bob restores original Boss (with default name fallback) rather than intermediate Alice");

        // Unsettled MMF grace chaining (priorSelectedId still Boss within <= 0.8s) preserves Boss.
        tracker.RecordSwitch(900, "Boss", -1, aliceLow, recoverAt, 25.0);
        tracker.RecordSwitch(900, "Boss", -1, bobLow, recoverAt, 25.3);
        _ = tracker.EvaluateRestore(new[] { aliceHealed, bobHealed }, settings, currentSelectedId: 3, BannerSeverity.None, null, false, true, 25.5);
        Assert(tracker.EvaluateRestore(new[] { aliceHealed, bobHealed }, settings, currentSelectedId: 3, BannerSeverity.None, null, false, true, 25.75) is { TargetId: 900, TargetName: "Boss" },
            "unsettled chain within SelectionSettleGraceSeconds preserves original Boss target");

        // Switching directly back to _originalTargetId clears tracker cleanly.
        tracker.RecordSwitch(2, "Alice", 1, bobLow, recoverAt, 26.0);
        tracker.RecordSwitch(3, "Bob", 2, aliceLow, recoverAt, 26.5);
        Assert(!tracker.IsActive, "plugin switching back to _originalTargetId resets tracker");

        // 4. Universal healing-priority hold (holds regardless of AutoTargetOnAlert and even if banner timed out while ally needs heal).
        tracker.RecordSwitch(900, "Boss", -1, aliceLow, recoverAt, 30.0);
        _ = tracker.EvaluateRestore(new[] { aliceHealed, bobLow }, settings, currentSelectedId: 2, BannerSeverity.LowHp, 3, false, true, 30.2);
        Assert(tracker.EvaluateRestore(new[] { aliceHealed, bobLow }, settings, currentSelectedId: 2, BannerSeverity.LowHp, 3, false, true, 31.0) is null && tracker.IsActive,
            "holds restore while another watched ally (Bob) is actively alerting with AutoTargetOnAlert=true");
        var manualOnlySettings = settings.Copy();
        manualOnlySettings.AutoTargetOnAlert = false;
        Assert(tracker.EvaluateRestore(new[] { aliceHealed, bobLow }, manualOnlySettings, currentSelectedId: 2, BannerSeverity.LowHp, 3, false, true, 31.1) is null && tracker.IsActive,
            "universal hold blocks restore even when AutoTargetOnAlert=false while any alert or low ally exists");
        Assert(tracker.EvaluateRestore(new[] { aliceHealed, bobLow }, manualOnlySettings, currentSelectedId: 2, BannerSeverity.None, null, false, true, 31.2) is null && tracker.IsActive,
            "universal hold blocks restore when MaxAlertSeconds/DeadAlertSeconds muted banner but watched ally is still <= threshold");
        var bobTrulyDead = new WatchedMemberSnapshot(3, "Bob", -15, 200, 2, true, IsDeadOrDown: true, IsTrulyDead: true);
        Assert(tracker.EvaluateRestore(new[] { aliceHealed, bobTrulyDead }, manualOnlySettings, currentSelectedId: 2, BannerSeverity.None, null, false, true, 31.3) is { TargetId: 900 },
            "truly dead corpse does not block restoring Boss once all living watched allies are healthy");

        // 5. Tight MMF settle grace (only _originalTargetId or _switchedPlayerId valid <= 0.8s) vs manual override.
        tracker.RecordSwitch(900, "Boss", -1, aliceLow, recoverAt, 40.0);
        Assert(tracker.EvaluateRestore(new[] { aliceLow }, settings, currentSelectedId: 900, BannerSeverity.LowHp, 2, false, true, 40.6) is null && tracker.IsActive,
            "unsettled selection still on _originalTargetId within 0.8s grace period stays active");
        Assert(tracker.EvaluateRestore(new[] { aliceLow }, settings, currentSelectedId: 900, BannerSeverity.LowHp, 2, false, true, 40.85) is null && !tracker.IsActive,
            "selection failing to settle on switched player within 0.8s cancels restore");

        tracker.RecordSwitch(900, "Boss", -1, aliceLow, recoverAt, 41.0);
        Assert(tracker.EvaluateRestore(new[] { aliceLow }, settings, currentSelectedId: 0, BannerSeverity.LowHp, 2, false, true, 41.1) is null && !tracker.IsActive,
            "clearing target (0) during unsettled grace window cancels restore immediately");
        tracker.RecordSwitch(900, "Boss", -1, aliceLow, recoverAt, 41.5);
        Assert(tracker.EvaluateRestore(new[] { aliceLow }, settings, currentSelectedId: 777, BannerSeverity.LowHp, 2, false, true, 41.6) is null && !tracker.IsActive,
            "clicking third entity during unsettled grace window cancels restore immediately");

        tracker.RecordSwitch(900, "Boss", -1, aliceLow, recoverAt, 42.0);
        _ = tracker.EvaluateRestore(new[] { aliceLow }, settings, currentSelectedId: 2, BannerSeverity.LowHp, 2, false, true, 42.1); // settled
        Assert(tracker.EvaluateRestore(new[] { aliceHealed }, settings, currentSelectedId: 777, BannerSeverity.None, null, false, true, 42.3) is null && !tracker.IsActive,
            "manual target change after settling cancels restore immediately");

        // 6. Manual hotkey top-off above threshold waits for >= MinHealDelta or full 100% MaxHP.
        var alice80 = new WatchedMemberSnapshot(2, "Alice", 160, 200, 1, true, false); // 80% >= 53%
        var aliceRegen = new WatchedMemberSnapshot(2, "Alice", 164, 200, 1, true, false); // +4 < MinHealDelta(5)
        var aliceTopped = new WatchedMemberSnapshot(2, "Alice", 170, 200, 1, true, false); // +10 >= 5
        tracker.RecordSwitch(900, "Boss", -1, alice80, recoverAt, 50.0);
        Assert(tracker.EvaluateRestore(new[] { alice80 }, settings, currentSelectedId: 2, BannerSeverity.None, null, false, true, 50.5) is null,
            "manual switch to 80% ally does not prematurely restore before heal lands");
        Assert(tracker.EvaluateRestore(new[] { aliceRegen }, settings, currentSelectedId: 2, BannerSeverity.None, null, false, true, 51.0) is null,
            "passive +4 regen (< MinHealDelta 5) does not trigger top-off restore");
        _ = tracker.EvaluateRestore(new[] { aliceTopped }, settings, currentSelectedId: 2, BannerSeverity.None, null, false, true, 51.2);
        Assert(tracker.EvaluateRestore(new[] { aliceTopped }, settings, currentSelectedId: 2, BannerSeverity.None, null, false, true, 51.45) is { TargetId: 900 },
            "manual top-off >= MinHealDelta restores original target after delay");

        var alice99 = new WatchedMemberSnapshot(2, "Alice", 198, 200, 1, true, false);
        var aliceFull = new WatchedMemberSnapshot(2, "Alice", 200, 200, 1, true, false);
        tracker.RecordSwitch(900, "Boss", -1, alice99, recoverAt, 55.0);
        _ = tracker.EvaluateRestore(new[] { aliceFull }, settings, currentSelectedId: 2, BannerSeverity.None, null, false, true, 55.2);
        Assert(tracker.EvaluateRestore(new[] { aliceFull }, settings, currentSelectedId: 2, BannerSeverity.None, null, false, true, 55.45) is { TargetId: 900 },
            "manual top-off reaching 100% MaxHP (+2 < MinHealDelta) restores original target");

        // 7. Damage dip during RestoreTargetDelaySeconds resets countdown; foregroundAllowed=false holds without resetting.
        var delaySettings = settings.Copy();
        delaySettings.RestoreTargetDelaySeconds = 0.5;
        tracker.RecordSwitch(900, "Boss", -1, aliceLow, recoverAt, 60.0);
        _ = tracker.EvaluateRestore(new[] { aliceHealed }, delaySettings, currentSelectedId: 2, BannerSeverity.None, null, false, true, 60.2); // healedSince = 60.2
        Assert(tracker.EvaluateRestore(new[] { aliceLow }, delaySettings, currentSelectedId: 2, BannerSeverity.LowHp, 2, false, true, 60.5) is null && tracker.IsActive,
            "dip back below threshold during delay keeps tracker armed and resets countdown");
        _ = tracker.EvaluateRestore(new[] { aliceHealed }, delaySettings, currentSelectedId: 2, BannerSeverity.None, null, false, true, 60.6); // healedSince = 60.6
        Assert(tracker.EvaluateRestore(new[] { aliceHealed }, delaySettings, currentSelectedId: 2, BannerSeverity.None, null, false, true, 60.9) is null,
            "0.3s after re-heal (< 0.5s delay, despite 0.7s since first heal) does not restore early");
        Assert(tracker.EvaluateRestore(new[] { aliceHealed }, delaySettings, currentSelectedId: 2, BannerSeverity.None, null, false, foregroundAllowed: false, 61.15) is null && tracker.IsActive,
            "foregroundAllowed=false holds ready restore without cancelling");
        Assert(tracker.EvaluateRestore(new[] { aliceHealed }, delaySettings, currentSelectedId: 2, BannerSeverity.None, null, false, foregroundAllowed: true, 61.2) is { TargetId: 900 },
            "foregroundAllowed=true completes held restore");

        // 8. Incapacitated stays armed; town, disconnect, true death, disabled settings, and >30s window cancel.
        var aliceIncap = new WatchedMemberSnapshot(2, "Alice", -5, 200, 1, true, IsDeadOrDown: true, IsTrulyDead: false);
        tracker.RecordSwitch(900, "Boss", -1, aliceIncap, recoverAt, 70.0);
        Assert(tracker.EvaluateRestore(new[] { aliceIncap }, settings, currentSelectedId: 2, BannerSeverity.CriticalDown, 2, false, true, 70.5) is null && tracker.IsActive,
            "incapacitated (-5 HP, IsTrulyDead=false) ally stays armed waiting for heal");

        var aliceDead = new WatchedMemberSnapshot(2, "Alice", -12, 200, 1, true, IsDeadOrDown: true, IsTrulyDead: true);
        Assert(tracker.EvaluateRestore(new[] { aliceDead }, settings, currentSelectedId: 2, BannerSeverity.CriticalDown, 2, false, true, 71.0) is null && !tracker.IsActive,
            "IsTrulyDead cancels active target restore");

        tracker.RecordSwitch(900, "Boss", -1, aliceLow, recoverAt, 72.0);
        var aliceOffline = new WatchedMemberSnapshot(2, "Alice", 60, 200, 1, Connected: false, false);
        Assert(tracker.EvaluateRestore(new[] { aliceOffline }, settings, currentSelectedId: 2, BannerSeverity.None, null, false, true, 72.2) is null && !tracker.IsActive,
            "disconnected player cancels active target restore");

        tracker.RecordSwitch(900, "Boss", -1, aliceLow, recoverAt, 73.0);
        Assert(tracker.EvaluateRestore(new[] { aliceHealed }, settings, currentSelectedId: 2, BannerSeverity.None, null, inTown: true, true, 73.5) is null && !tracker.IsActive,
            "entering town cancels active target restore");

        tracker.RecordSwitch(900, "Boss", -1, aliceLow, recoverAt, 74.0);
        var noRestoreSettings = settings.Copy();
        noRestoreSettings.RestorePreviousTargetAfterHeal = false;
        Assert(tracker.EvaluateRestore(new[] { aliceHealed }, noRestoreSettings, currentSelectedId: 2, BannerSeverity.None, null, false, true, 74.5) is null && !tracker.IsActive,
            "RestorePreviousTargetAfterHeal=false cancels active target restore");

        tracker.RecordSwitch(900, "Boss", -1, aliceLow, recoverAt, 75.0);
        var pluginOffSettings = settings.Copy();
        pluginOffSettings.PluginEnabled = false;
        Assert(tracker.EvaluateRestore(new[] { aliceHealed }, pluginOffSettings, currentSelectedId: 2, BannerSeverity.None, null, false, true, 75.5) is null && !tracker.IsActive,
            "PluginEnabled=false cancels active target restore");

        tracker.RecordSwitch(900, "Boss", -1, aliceLow, recoverAt, 76.0);
        var targetOffSettings = settings.Copy();
        targetOffSettings.TargetingMethod = TargetMethod.Off;
        Assert(tracker.EvaluateRestore(new[] { aliceHealed }, targetOffSettings, currentSelectedId: 2, BannerSeverity.None, null, false, true, 76.5) is null && !tracker.IsActive,
            "TargetingMethod.Off cancels active target restore");

        tracker.RecordSwitch(900, "Boss", -1, aliceLow, recoverAt, 100.0);
        _ = tracker.EvaluateRestore(new[] { aliceLow }, settings, currentSelectedId: 2, BannerSeverity.LowHp, 2, false, true, 100.2);
        Assert(tracker.EvaluateRestore(new[] { aliceHealed }, settings, currentSelectedId: 2, BannerSeverity.None, null, false, true, 130.1) is null && !tracker.IsActive,
            "exceeding 30s MaxRestoreWindowSeconds expires and cancels target restore");

        // 9. Immediate cooldown-release helper IsPreviousTargetHealedOrGone.
        Assert(!TargetRestoreTracker.IsPreviousTargetHealedOrGone(new[] { aliceLow }, 0, recoverAt)
               && !TargetRestoreTracker.IsPreviousTargetHealedOrGone(new[] { aliceLow }, ulong.MaxValue, recoverAt),
            "IsPreviousTargetHealedOrGone rejects invalid 0 and ulong.MaxValue IDs");
        Assert(!TargetRestoreTracker.IsPreviousTargetHealedOrGone(new[] { aliceLow }, 2, recoverAt)
               && !TargetRestoreTracker.IsPreviousTargetHealedOrGone(new[] { aliceIncap }, 2, recoverAt),
            "IsPreviousTargetHealedOrGone stays false while prior ally is still low or incapacitated");
        Assert(TargetRestoreTracker.IsPreviousTargetHealedOrGone(new[] { aliceHealed }, 2, recoverAt)
               && TargetRestoreTracker.IsPreviousTargetHealedOrGone(new[] { aliceOffline }, 2, recoverAt)
               && TargetRestoreTracker.IsPreviousTargetHealedOrGone(new[] { aliceDead }, 2, recoverAt)
               && TargetRestoreTracker.IsPreviousTargetHealedOrGone(Array.Empty<WatchedMemberSnapshot>(), 2, recoverAt),
            "IsPreviousTargetHealedOrGone releases cooldown once prior ally is healed, offline, truly dead, or gone");
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
            MaxAlertSeconds = 999,
            FontSizePixels = 999,
            BannerOpacity = 10,
            FlashIntervalMs = 10,
            LowHpBgHex = "not-hex",
            CriticalBorderHex = "#abc",
            SaveWindowSeconds = double.NaN,
            SelfCastWindowSeconds = 99.0,
            MinHealDelta = 0,
            HudX = double.NaN,
            HudY = 2.5,
            StatusFontSizePixels = 2,
            StatusHudX = -1.0,
            StatusHudY = double.NaN,
            PeopleSaved = -7,
            CurrentSaveStreak = 5,
            BestSaveStreak = 2,
            ClosestCallPlayerName = "Ghost",
            ClosestCallPreHealHp = 0,
            ClosestCallPreHealPercent = 40.0,
            ReflexSampleCount = 0,
            BestReflexMs = 300,
            AvgReflexMs = 300,
            AutoTargetCooldownSeconds = double.PositiveInfinity,
            RestoreTargetDelaySeconds = double.NaN,
            FocusTargetHotkey = new HotkeyBinding(0xFFFF, true, false, false),
            ToggleModeHotkey = new HotkeyBinding(0x11, true, false, false)
        };
        invalid.Validate();
        Assert(invalid.Mode == TrackingMode.Party && invalid.TargetingMethod == TargetMethod.Auto, "invalid enums reset to defaults");
        Assert(invalid.HpThresholdPercent == 5 && invalid.RecoveryHysteresisPercent == 20 && invalid.MaxAlertSeconds == 120,
            "threshold, hysteresis, and MaxAlertSeconds clamped");
        Assert(invalid.DeadAlertSeconds == 0 && invalid.FontSizePixels == 140 && invalid.BannerOpacity == 20 && invalid.FlashIntervalMs == 100,
            "dead alert, font, opacity (min 20), and flash interval clamped");
        Assert(invalid.LowHpBgHex == Settings.DefaultLowHpBgHex && invalid.CriticalBorderHex == "#AABBCC"
               && Settings.NormalizeHexColor("ff00aa", "#000000") == "#FF00AA",
            "NormalizeHexColor expands 3-digit/6-digit hex and falls back on invalid strings");
        invalid.LowHpBgHex = "#112233";
        invalid.ResetBannerColors();
        Assert(invalid.LowHpBgHex == Settings.DefaultLowHpBgHex && invalid.CriticalBorderHex == Settings.DefaultCriticalBorderHex,
            "ResetBannerColors restores all 6 default banner hex colors");
        Assert(invalid.SaveWindowSeconds == 5.0 && invalid.SelfCastWindowSeconds == 15.0 && invalid.MinHealDelta == 1,
            "SaveWindowSeconds, SelfCastWindowSeconds, and MinHealDelta clamped/sanitized");
        Assert(invalid.HudX == 0.50 && invalid.HudY == 1.0 && invalid.AutoTargetCooldownSeconds == 3.0 && invalid.RestoreTargetDelaySeconds == 0.2,
            "HudX, HudY, cooldown, and RestoreTargetDelaySeconds NaN clamped/sanitized");
        var negDelay = new Settings { RestoreTargetDelaySeconds = -2.0 };
        var highDelay = new Settings { RestoreTargetDelaySeconds = 9.5 };
        negDelay.Validate();
        highDelay.Validate();
        Assert(negDelay.RestoreTargetDelaySeconds == 0.0 && highDelay.RestoreTargetDelaySeconds == 5.0,
            "RestoreTargetDelaySeconds clamped to [0.0, 5.0]");
        Assert(invalid.StatusFontSizePixels == 9 && invalid.StatusHudX == 0.0 && invalid.StatusHudY == 0.76 && invalid.PeopleSaved == 0,
            "StatusFontSizePixels, StatusHudX, StatusHudY, and PeopleSaved clamped/sanitized");
        Assert(invalid.CurrentSaveStreak == 5 && invalid.BestSaveStreak == 5 && invalid.ClosestCallPlayerName == "" && invalid.BestReflexMs == 0,
            "Validate promotes BestSaveStreak >= CurrentSaveStreak and clears incomplete ClosestCall/Reflex records");
        Assert(invalid.FocusTargetHotkey.IsEmpty && invalid.ToggleModeHotkey.IsEmpty,
            "NormalizeHotkey clears out-of-range (>0xFE) and modifier-only VKs");

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
            s.IncludeSelfInPartyMode = false;
            s.SuppressAlertsInTown = true;
            s.FocusedPlayerId = 12345;
            s.FocusedPlayerName = "Tanky";
            s.FontSizePixels = 72;
            s.BannerOpacity = 190;
            s.FlashBanner = false;
            s.DeadAlertSeconds = 9;
            s.MaxAlertSeconds = 18;
            s.SaveWindowSeconds = 7.5;
            s.SelfCastWindowSeconds = 4.5;
            s.MinHealDelta = 15;
            s.ShowSavedNotice = false;
            s.ShowRankOnStatusHud = false;
            s.ExcludeFromCapture = false;
            s.HudX = 0.25;
            s.HudY = 0.75;
            s.LowHpBgHex = "#123456";
            s.LowHpBorderHex = "#654321";
            s.CriticalBgHex = "#7A0019";
            s.CriticalBorderHex = "#FFCC33";
            s.BannerTextHex = "#F0F4FF";
            s.FlashAltBgHex = "#10141C";
            s.StatusHudEnabled = false;
            s.StatusFontSizePixels = 19;
            s.StatusHudX = 0.15;
            s.StatusHudY = 0.85;
            s.PeopleSaved = 17;
            s.CurrentSaveStreak = 4;
            s.BestSaveStreak = 9;
            s.ClosestCallPlayerName = "Rogue";
            s.ClosestCallPreHealHp = 3;
            s.ClosestCallPreHealPercent = 1.5;
            s.BiggestOverkillSaved = 420;
            s.OnePercentClubSaves = 5;
            s.BestReflexMs = 140;
            s.AvgReflexMs = 230;
            s.ReflexSampleCount = 6;
            s.AutoTargetCooldownSeconds = 4.5;
            s.RestorePreviousTargetAfterHeal = false;
            s.RestoreTargetDelaySeconds = 0.7;
            s.TogglePluginHotkey = new HotkeyBinding(0x50, true, true, false);
            s.ToggleBannerHotkey = new HotkeyBinding(0x42, true, true, false);
            s.FocusTargetHotkey = new HotkeyBinding(0x46, true, false, true);
            s.ResetSavedHotkey = new HotkeyBinding(0x52, true, true, false);
            s.CopyScorecardHotkey = new HotkeyBinding(0x43, true, true, false);
            store.Save(s);

            var reloaded = store.Load();
            Assert(!reloaded.PluginEnabled && reloaded.PeopleSaved == 17 && reloaded.CurrentSaveStreak == 4 && reloaded.BestSaveStreak == 9,
                "JSON round-trip preserves PluginEnabled, PeopleSaved, and streaks");
            Assert(!reloaded.IncludeSelfInPartyMode && reloaded.SuppressAlertsInTown && reloaded.MaxAlertSeconds == 18 && reloaded.BannerOpacity == 190 && !reloaded.FlashBanner,
                "JSON round-trip preserves extended alert and banner settings");
            Assert(reloaded.SaveWindowSeconds == 7.5 && reloaded.SelfCastWindowSeconds == 4.5 && reloaded.MinHealDelta == 15 && !reloaded.ShowSavedNotice && !reloaded.ShowRankOnStatusHud,
                "JSON round-trip preserves clutch save and rank display settings");
            Assert(reloaded.ClosestCallPlayerName == "Rogue" && reloaded.ClosestCallPreHealHp == 3 && reloaded.OnePercentClubSaves == 5 && reloaded.BestReflexMs == 140 && reloaded.ReflexSampleCount == 6,
                "JSON round-trip preserves ClosestCall, 1% Club, and Reflex records");
            Assert(reloaded.Mode == TrackingMode.SinglePlayer && reloaded.FocusedPlayerId == 12345 && reloaded.FocusedPlayerName == "Tanky",
                "JSON round-trip preserves tracking mode and focus target");
            Assert(reloaded.FontSizePixels == 72 && reloaded.DeadAlertSeconds == 9 && !reloaded.ExcludeFromCapture,
                "JSON round-trip preserves exact FontSizePixels, DeadAlertSeconds, and ExcludeFromCapture");
            Assert(!reloaded.StatusHudEnabled && reloaded.StatusFontSizePixels == 19 && reloaded.StatusHudX == 0.15 && reloaded.StatusHudY == 0.85,
                "JSON round-trip preserves StatusHud settings");
            Assert(reloaded.HudX == 0.25 && reloaded.HudY == 0.75 && reloaded.AutoTargetCooldownSeconds == 4.5
                   && !reloaded.RestorePreviousTargetAfterHeal && Math.Abs(reloaded.RestoreTargetDelaySeconds - 0.7) < 0.001
                   && reloaded.LowHpBgHex == "#123456" && reloaded.LowHpBorderHex == "#654321"
                   && reloaded.CriticalBgHex == "#7A0019" && reloaded.CriticalBorderHex == "#FFCC33"
                   && reloaded.BannerTextHex == "#F0F4FF" && reloaded.FlashAltBgHex == "#10141C",
                "JSON round-trip preserves HudX, HudY, AutoTargetCooldownSeconds, target restore settings, and all 6 banner hex colors");
            Assert(reloaded.TogglePluginHotkey == new HotkeyBinding(0x50, true, true, false)
                   && reloaded.ToggleBannerHotkey == new HotkeyBinding(0x42, true, true, false)
                   && reloaded.FocusTargetHotkey == new HotkeyBinding(0x46, true, false, true)
                   && reloaded.ResetSavedHotkey == new HotkeyBinding(0x52, true, true, false)
                   && reloaded.CopyScorecardHotkey == new HotkeyBinding(0x43, true, true, false),
                "JSON round-trip preserves all 7 hotkey bindings");
            Assert(reloaded.Copy().FontSizePixels == 72 && reloaded.Copy().PeopleSaved == 17 && reloaded.Copy().BestReflexMs == 140
                   && !reloaded.Copy().RestorePreviousTargetAfterHeal && Math.Abs(reloaded.Copy().RestoreTargetDelaySeconds - 0.7) < 0.001
                   && reloaded.Copy().LowHpBgHex == "#123456" && reloaded.Copy().ToggleBannerHotkey == new HotkeyBinding(0x42, true, true, false),
                "Copy preserves exact FontSizePixels, PeopleSaved, gamification stats, target restore, colors, and ToggleBannerHotkey");
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
        Assert(critOn.BackgroundColorArgb == unchecked((int)0xF0960A14) && critOn.BackgroundColorArgb != critOff.BackgroundColorArgb && critOn.FontSizePixels == 64,
            "CriticalDown preserves exact default 235 opacity ARGB, changes colors across flash phases, and keeps integer font");

        // BannerOpacity scales background alpha monotonically (20 < 80 < 235 < 255) and FlashBanner = false keeps steady frame.
        var ultraDimFrame = BannerPresentation.Compute(critView, new Settings { BannerOpacity = 20 }, false, false, 0.0, 0.0);
        var dimFrame = BannerPresentation.Compute(critView, new Settings { BannerOpacity = 80 }, false, false, 0.0, 0.0);
        var fullFrame = BannerPresentation.Compute(critView, new Settings { BannerOpacity = 255 }, false, false, 0.0, 0.0);
        int alphaMin = (ultraDimFrame.BackgroundColorArgb >> 24) & 0xFF;
        int alphaDim = (dimFrame.BackgroundColorArgb >> 24) & 0xFF;
        int alphaDef = (critOn.BackgroundColorArgb >> 24) & 0xFF;
        int alphaFull = (fullFrame.BackgroundColorArgb >> 24) & 0xFF;
        Assert(alphaMin == 20 && alphaMin < alphaDim && alphaDim < alphaDef && alphaDef < alphaFull && alphaFull == 255,
            "BannerOpacity scales background alpha monotonically across 20..255");

        // Custom hex colors apply across both flash-on and flash-off phases while defaults stay exact.
        var customColors = new Settings
        {
            CriticalBgHex = "#220044",
            CriticalBorderHex = "#00FFCC",
            LowHpBgHex = "#004466",
            LowHpBorderHex = "#66FFEE",
            BannerTextHex = "#FFEEAA",
            FlashAltBgHex = "#121824"
        };
        var customCritOn = BannerPresentation.Compute(critView, customColors, false, false, 0.0, 0.0);
        var customCritOff = BannerPresentation.Compute(critView, customColors, false, false, 0.25, 0.0);
        var customLowOn = BannerPresentation.Compute(lowView, customColors, false, false, 0.0, 0.0);
        var customLowOff = BannerPresentation.Compute(lowView, customColors, false, false, 0.25, 0.0);
        Assert((customCritOn.BackgroundColorArgb & 0x00FFFFFF) == 0x220044
               && customCritOn.BorderColorArgb == unchecked((int)0xFF00FFCC)
               && customCritOn.TextColorArgb == unchecked((int)0xFFFFEEAA)
               && (customCritOff.BackgroundColorArgb & 0x00FFFFFF) == 0x121824
               && (customLowOn.BackgroundColorArgb & 0x00FFFFFF) == 0x004466
               && customLowOn.BorderColorArgb == unchecked((int)0xFF66FFEE)
               && (customLowOff.BackgroundColorArgb & 0x00FFFFFF) == 0x121824,
            "custom banner hex colors apply to CriticalDown and LowHp flash-on and flash-off frames");

        // HudEnabled = false suppresses all live severities while still allowing Preview (demo) and Move/Resize (editing).
        var bannerDisabled = new Settings { HudEnabled = false };
        Assert(!BannerPresentation.Compute(critView, bannerDisabled, false, false, 0.0, 0.0).Visible
               && !BannerPresentation.Compute(lowView, bannerDisabled, false, false, 0.0, 0.0).Visible
               && !BannerPresentation.Compute(noticeView, bannerDisabled, false, false, 0.0, 0.0).Visible
               && !BannerPresentation.Compute(idleView, bannerDisabled, false, false, 0.0, 0.0).Visible,
            "HudEnabled=false suppresses CriticalDown, LowHp, Notice, and Idle live banner frames");
        Assert(BannerPresentation.Compute(idleView, bannerDisabled, demo: true, editing: false, 0.0, 0.0).Visible
               && BannerPresentation.Compute(idleView, bannerDisabled, demo: false, editing: true, 0.0, 15.0).Visible,
            "HudEnabled=false still permits explicit Preview Banner and Move/Resize Banner on demand");

        // ClassifyEditHit classifies all 4 corners, all 4 edges, center Move, and outside None.
        Assert(BannerPresentation.ClassifyEditHit(500, 130, 5, 5) == BannerEditDragMode.ResizeTopLeft
               && BannerPresentation.ClassifyEditHit(500, 130, 495, 5) == BannerEditDragMode.ResizeTopRight
               && BannerPresentation.ClassifyEditHit(500, 130, 5, 125) == BannerEditDragMode.ResizeBottomLeft
               && BannerPresentation.ClassifyEditHit(500, 130, 495, 125) == BannerEditDragMode.ResizeBottomRight
               && BannerPresentation.ClassifyEditHit(500, 130, 5, 65) == BannerEditDragMode.ResizeLeft
               && BannerPresentation.ClassifyEditHit(500, 130, 495, 65) == BannerEditDragMode.ResizeRight
               && BannerPresentation.ClassifyEditHit(500, 130, 250, 5) == BannerEditDragMode.ResizeTop
               && BannerPresentation.ClassifyEditHit(500, 130, 250, 125) == BannerEditDragMode.ResizeBottom
               && BannerPresentation.ClassifyEditHit(500, 130, 250, 65) == BannerEditDragMode.Move
               && BannerPresentation.ClassifyEditHit(500, 130, -1, 65) == BannerEditDragMode.None,
            "ClassifyEditHit classifies all 4 corners, 4 edges, center move, and out-of-bounds");

        var steadySettings = new Settings { FlashBanner = false, FlashIntervalMs = 250 };
        var steadyAtHalf = BannerPresentation.Compute(critView, steadySettings, false, false, 0.25, 0.0);
        Assert(steadyAtHalf.FlashOn && steadyAtHalf.BackgroundColorArgb == critOn.BackgroundColorArgb,
            "FlashBanner=false holds steady non-flashing primary frame");

        var lowOn = BannerPresentation.Compute(lowView, settings, false, false, 0.0, 0.0);
        var lowOff = BannerPresentation.Compute(lowView, settings, false, false, 0.25, 0.0);
        Assert(lowOn.FlashOn && !lowOff.FlashOn && lowOn.BackgroundColorArgb != critOn.BackgroundColorArgb, "LowHp flashes with distinct amber/orange palette");

        var notice = BannerPresentation.Compute(noticeView, settings, false, false, 0.0, 0.0);
        Assert(notice.Visible && !notice.FlashOn && notice.PrimaryText == "MODE: PARTY", "Notice renders visible non-flashing banner");

        var editIdle = BannerPresentation.Compute(idleView, settings, false, true, 0.0, 12.2);
        Assert(editIdle.Visible && editIdle.PrimaryText == "DDO HEALER'S HELPER — DRAG TO POSITION"
               && editIdle.SecondaryText.Contains("13s") && editIdle.SecondaryText.Contains("64px") && editIdle.SecondaryText.Contains("235"),
            "Editing while idle renders drag/resize/wheel instructions with font size, opacity, and ceiling countdown");

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
        var settings = new Settings { StatusFontSizePixels = 14, HpThresholdPercent = 50, Mode = TrackingMode.Party, ShowRankOnStatusHud = true };
        var normalView = new AlertBannerView(BannerSeverity.None, TrackingMode.Party, "", "", 0, null, "", null, 0, "", "", true, 3);
        var alertView = new AlertBannerView(BannerSeverity.LowHp, TrackingMode.Party, "HEAL Alice (30%)", "60/200 HP", 1, 2, "Alice", 30, 1, "", "", true, 3);

        var normal = StatusHudPresentation.Compute(normalView, settings, false, 0);
        Assert(normal.Visible && normal.FontSizePixels == 14 && !normal.ShowDragBox
               && normal.Text == "HEALER  PARTY <=50%  •  SAVED 3 (Field Medic)",
            "StatusHud normal Party formatting with prestige rank");

        var noRankSettings = settings.Copy();
        noRankSettings.ShowRankOnStatusHud = false;
        var noRank = StatusHudPresentation.Compute(normalView, noRankSettings, false, 0);
        Assert(noRank.Text == "HEALER  PARTY <=50%  •  SAVED 3",
            "StatusHud ShowRankOnStatusHud=false omits prestige rank cleanly");

        var alerting = StatusHudPresentation.Compute(alertView, settings, false, 0);
        Assert(alerting.Text == "HEALER  PARTY • ALERT  •  SAVED 3 (Field Medic)" && alerting.TextColorArgb != normal.TextColorArgb,
            "StatusHud Party alert formatting and distinct color");

        var focusSettings = new Settings { Mode = TrackingMode.SinglePlayer, FocusedPlayerName = "Tanky", HpThresholdPercent = 45, ShowRankOnStatusHud = true };
        var focusFrame = StatusHudPresentation.Compute(normalView with { PeopleSaved = 7 }, focusSettings, false, 0);
        Assert(focusFrame.Text == "HEALER  FOCUS: Tanky <=45%  •  SAVED 7 (Combat Healer)", "StatusHud SinglePlayer focus formatting");

        var pausedSettings = new Settings { Mode = TrackingMode.Off, ShowRankOnStatusHud = true };
        var pausedFrame = StatusHudPresentation.Compute(normalView with { PeopleSaved = 2 }, pausedSettings, false, 0);
        Assert(pausedFrame.Text == "HEALER  PAUSED  •  SAVED 2 (Field Medic)", "StatusHud paused formatting");

        var offSettings = new Settings { PluginEnabled = false, ShowRankOnStatusHud = true };
        var offFrame = StatusHudPresentation.Compute(normalView with { PluginEnabled = false }, offSettings, false, 0);
        Assert(offFrame.Visible && offFrame.Text == "HEALER  OFF", "StatusHud master-off formatting stays HEALER  OFF");

        var hiddenSettings = new Settings { StatusHudEnabled = false };
        Assert(!StatusHudPresentation.Compute(normalView, hiddenSettings, false, 0).Visible,
            "StatusHud hidden when StatusHudEnabled=false and not editing");
        var editingHidden = StatusHudPresentation.Compute(normalView, hiddenSettings, true, 9.1);
        Assert(editingHidden.Visible && editingHidden.ShowDragBox && editingHidden.DragHint == "Drag status • locks in 10s",
            "StatusHud drag mode forces visibility with drag box and hint");
    }
}
