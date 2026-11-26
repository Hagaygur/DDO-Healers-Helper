using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Windows.Forms;

internal static class Program
{
    private const int WsExTransparent = 0x20;
    private const int WsExToolWindow = 0x80;
    private const int WsExLayered = 0x80000;
    private const int WsExNoActivate = 0x08000000;
    private const int GwlExStyle = -20;

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length < 2)
            throw new ArgumentException("Usage: WindowsSmoke <plugin-zip-or-dll> <dungeon-helper-dir>");

        string pluginPath = Path.GetFullPath(args[0]);
        string sdkPath = Path.Combine(Path.GetFullPath(args[1]), "VoK.Sdk.dll");
        AssemblyLoadContext.Default.Resolving += (_, name) => name.Name == "VoK.Sdk"
            ? AssemblyLoadContext.Default.LoadFromAssemblyPath(sdkPath)
            : null;

        Assembly pluginAssembly = LoadPlugin(pluginPath);
        if (pluginAssembly.GetCustomAttribute<SupportedOSPlatformAttribute>()?.PlatformName != "windows7.0")
            throw new Exception("Plugin must declare [SupportedOSPlatform(\"windows7.0\")]");

        Assembly sdk = Assembly.LoadFrom(sdkPath);
        Type ddoPluginInterface = sdk.GetType("VoK.Sdk.Ddo.IDdoPlugin")!;
        Type pluginType = pluginAssembly.GetTypes().Single(t => t.IsPublic && !t.IsAbstract && ddoPluginInterface.IsAssignableFrom(t));
        object plugin = Activator.CreateInstance(pluginType)!;

        Guid pluginId = (Guid)pluginType.GetProperty("PluginId")!.GetValue(plugin)!;
        string pluginKey = (string)pluginType.GetProperty("PluginKey")!.GetValue(plugin)!;
        Version runtimeVersion = (Version)pluginType.GetProperty("Version")!.GetValue(plugin)!;
        if (pluginId != new Guid("9b6c8e42-3d19-4f7a-9c54-7e21d804a6b3"))
            throw new Exception($"Unexpected PluginId: {pluginId}");
        if (pluginKey != "ddo-healers-helper")
            throw new Exception($"Unexpected PluginKey: {pluginKey}");
        if (runtimeVersion != pluginAssembly.GetName().Version)
            throw new Exception("Runtime Version disagrees with assembly version");

        // Verify Wine guard is strictly false on Windows runtime.
        Type hudHostHelperType = pluginAssembly.GetType("VoK.HealersHelper.HudHostHelper")!;
        bool isWine = (bool)hudHostHelperType.GetField("IsWineRuntime", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(null)!;
        if (isWine)
            throw new Exception("HudHostHelper.IsWineRuntime must be false on native Windows");

        string offlineFolder = Path.Combine(Path.GetTempPath(), "healers-helper-offline-smoke-" + Guid.NewGuid());
        Directory.CreateDirectory(offlineFolder);
        try
        {
            PropertyInfo settingsFolderProp = pluginType.GetProperty("SettingsFolder", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
            settingsFolderProp.SetValue(plugin, offlineFolder);

            object ui = pluginType.GetMethod("GetPluginUI")!.Invoke(plugin, null)!;
            Type uiContract = sdk.GetType("VoK.Sdk.Plugins.IPluginUI")!;
            using var form = (Form)uiContract.GetProperty("UserInterfaceForm")!.GetValue(ui)!;

            // 1. Verify true 380px minimum-width layout with visible controls BEFORE host toolbar exists.
            form.Size = form.MinimumSize;
            form.Show();
            Application.DoEvents();
            form.PerformLayout();
            if (form.Width != form.MinimumSize.Width)
                throw new Exception($"Expected initial minimum width {form.MinimumSize.Width}, got {form.Width}");
            int checkedAt380 = CheckLayoutControls(form, form);
            if (checkedAt380 < 12)
                throw new Exception($"Expected >= 12 visible controls checked at 380px, checked {checkedAt380}");

            // 2. Create synthetic 720px toolbar host and verify FitToolbarWidth expansion.
            using var host = new Form { Width = 400, Height = 400 };
            var strip = new Panel { Width = 720, Height = 45 };
            strip.Controls.Add(new PictureBox { Name = (string)pluginType.GetProperty("Name")!.GetValue(plugin)!, Left = 680, Width = 36 });
            host.Controls.Add(strip);
            host.Show();

            form.Hide();
            form.Show();
            Application.DoEvents();
            form.PerformLayout();
            if (form.Width < strip.Width)
                throw new Exception($"Plugin form width {form.Width} < host toolbar strip width {strip.Width}");
            int checkedAt720 = CheckLayoutControls(form, form);
            if (checkedAt720 < 12)
                throw new Exception($"Expected >= 12 visible controls checked at 720px, checked {checkedAt720}");

            var badge = (Label)form.Controls.Find("PluginStateLabel", true).Single();
            var bannerBadge = (Label)form.Controls.Find("BannerStateLabel", true).Single();
            var savedLabel = (Label)form.Controls.Find("PeopleSavedLabel", true).Single();
            var gamificationLabel = (Label)form.Controls.Find("GamificationSummaryLabel", true).Single();
            var compatNote = (Label)form.Controls.Find("CompatibilityNoteLabel", true).Single();
            var togglePluginBtn = (Button)form.Controls.Find("TogglePluginButton", true).Single();
            var toggleBannerBtn = (Button)form.Controls.Find("ToggleBannerButton", true).Single();
            var moveBannerBtn = (Button)form.Controls.Find("MoveBannerButton", true).Single();

            if (!badge.Visible || !bannerBadge.Visible || !savedLabel.Visible || !gamificationLabel.Visible || !compatNote.Visible
                || moveBannerBtn.Text != "Move / Resize Banner"
                || form.Controls.Find("MoveStatusButton", true).Length != 1
                || form.Controls.Find("CopyScorecardButton", true).Length != 1
                || form.Controls.Find("ResetSavedButton", true).Length != 1)
            {
                throw new Exception("Expected PluginStateLabel, BannerStateLabel, PeopleSavedLabel, GamificationSummaryLabel, CompatibilityNoteLabel, ToggleBannerButton, Move / Resize Banner, MoveStatusButton, CopyScorecardButton, and ResetSavedButton on panel");
            }

            VerifyCompatibilityNoteLabel(compatNote, form);

            // 3. Verify offline active/inactive & banner ON/OFF persistence BEFORE DDO/engine connects!
            togglePluginBtn.PerformClick();
            toggleBannerBtn.PerformClick();
            Application.DoEvents();
            form.PerformLayout();
            if (badge.Text != "Plugin: OFF" || bannerBadge.Text != "Banner: OFF" || toggleBannerBtn.Text != "Banner: OFF")
                throw new Exception($"Offline toggle did not update labels: {badge.Text} / {bannerBadge.Text} / {toggleBannerBtn.Text}");
            _ = CheckLayoutControls(form, form);

            string offlineJsonPath = Path.Combine(offlineFolder, "healers-helper.json");
            using (var doc = JsonDocument.Parse(File.ReadAllText(offlineJsonPath)))
            {
                if (doc.RootElement.GetProperty("PluginEnabled").GetBoolean() != false
                    || doc.RootElement.GetProperty("HudEnabled").GetBoolean() != false)
                {
                    throw new Exception("Offline panel toggles did not persist PluginEnabled=false and HudEnabled=false to healers-helper.json");
                }
            }

            // Fresh plugin + UI instance must reload persisted Plugin: OFF and Banner: OFF immediately!
            object pluginReloaded = Activator.CreateInstance(pluginType)!;
            settingsFolderProp.SetValue(pluginReloaded, offlineFolder);
            object uiReloaded = pluginType.GetMethod("GetPluginUI")!.Invoke(pluginReloaded, null)!;
            try
            {
                using var formReloaded = (Form)uiContract.GetProperty("UserInterfaceForm")!.GetValue(uiReloaded)!;
                formReloaded.Show();
                Application.DoEvents();
                var reloadedBadge = (Label)formReloaded.Controls.Find("PluginStateLabel", true).Single();
                var reloadedBanner = (Label)formReloaded.Controls.Find("BannerStateLabel", true).Single();
                if (reloadedBadge.Text != "Plugin: OFF" || reloadedBanner.Text != "Banner: OFF")
                    throw new Exception($"Reloaded offline UI did not restore last state: {reloadedBadge.Text} / {reloadedBanner.Text}");
            }
            finally
            {
                if (uiReloaded is IDisposable dispReloaded) dispReloaded.Dispose();
            }

            // Flip offline back to ON before attaching live engine.
            togglePluginBtn.PerformClick();
            toggleBannerBtn.PerformClick();

            CheckSettingsDialogGamificationAndHuds(pluginAssembly, pluginType, plugin, form);

            if (ui is IDisposable disposableUi)
                disposableUi.Dispose();
        }
        finally
        {
            if (Directory.Exists(offlineFolder))
                Directory.Delete(offlineFolder, true);
        }
    }

    private static void VerifyCompatibilityNoteLabel(Label label, Form topForm)
    {
        string text = label.Text ?? "";
        if (!label.Visible
            || !text.Contains("Tested & working on live DDO + Lossless Scaling", StringComparison.Ordinal)
            || !text.Contains("not guaranteed", StringComparison.Ordinal)
            || !text.Contains("Linux", StringComparison.Ordinal)
            || !text.Contains("untested", StringComparison.Ordinal))
        {
            throw new Exception($"Unexpected compatibility note label '{label.Name}': {text}");
        }

        Rectangle inForm = topForm.RectangleToClient(label.Parent!.RectangleToScreen(label.Bounds));
        if (inForm.Left < 0 || inForm.Right > topForm.ClientSize.Width)
        {
            throw new Exception($"Compatibility label '{label.Name}' clips horizontally ({inForm}) in form width {topForm.ClientSize.Width}");
        }
    }

    private static void CheckSettingsDialogGamificationAndHuds(Assembly assembly, Type pluginType, object plugin, Form controlForm)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        Type engineType = assembly.GetType("VoK.HealersHelper.HealerEngine")!;
        object engine = RuntimeHelpers.GetUninitializedObject(engineType);
        void Set(string name, object value) => engineType.GetField(name, fields)!.SetValue(engine, value);

        string folder = Path.Combine(Path.GetTempPath(), "healers-helper-smoke-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            Type settingsType = assembly.GetType("VoK.HealersHelper.Settings")!;
            object settings = Activator.CreateInstance(settingsType)!;
            settingsType.GetProperty("PeopleSaved")!.SetValue(settings, 5);
            Type storeType = assembly.GetType("VoK.HealersHelper.SettingsStore")!;
            object store = Activator.CreateInstance(storeType, folder)!;

            Set("_gate", new object());
            Set("_settings", settings);
            Set("_pluginEnabledFlag", true);
            Set("_store", store);
            Set("_state", Activator.CreateInstance(assembly.GetType("VoK.HealersHelper.HealerState")!)!);
            Set("_savedTracker", Activator.CreateInstance(assembly.GetType("VoK.HealersHelper.PeopleSavedTracker")!)!);
            Set("_streakTracker", Activator.CreateInstance(assembly.GetType("VoK.HealersHelper.StreakTracker")!)!);
            Set("_reflexTracker", Activator.CreateInstance(assembly.GetType("VoK.HealersHelper.ReflexTracker")!)!);
            Set("_hotkeys", Activator.CreateInstance(assembly.GetType("VoK.HealersHelper.HotkeyMatcher")!, 0.25d)!);
            Set("_selector", Activator.CreateInstance(assembly.GetType("VoK.HealersHelper.TargetSelector")!, new object?[] { null })!);
            Set("_members", Array.CreateInstance(assembly.GetType("VoK.HealersHelper.WatchedMemberSnapshot")!, 0));

            Type viewType = assembly.GetType("VoK.HealersHelper.AlertBannerView")!;
            Type severityEnum = assembly.GetType("VoK.HealersHelper.BannerSeverity")!;
            Type modeEnum = assembly.GetType("VoK.HealersHelper.TrackingMode")!;
            object idleView = Activator.CreateInstance(
                viewType,
                Enum.Parse(severityEnum, "None"),
                Enum.Parse(modeEnum, "Party"),
                "",
                "",
                0,
                null,
                "",
                null,
                0L,
                "Smoke status",
                "Targeting ready",
                true,
                5)!;
            Set("_view", idleView);

            pluginType.GetField("_engine", fields)!.SetValue(plugin, engine);
            Console.WriteLine("PASS Windows plugin ZIP load, IDdoPlugin metadata, offline last-state persistence, and 380px/720px toolbar UI layout");

            // 1. SettingsDialog at minimum size + 7 hotkeys + custom colors + opacity 20..255 + dirty-tracking + round-trip
            Type dialogType = assembly.GetType("VoK.HealersHelper.SettingsDialog")!;
            MethodInfo processCmdKey = dialogType.GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!;
            MethodInfo setBannerColorHex = dialogType.GetMethod("SetBannerColorHex", BindingFlags.Instance | BindingFlags.NonPublic)!;
            bool SendDialogKey(Form dlg, Keys keys)
            {
                object[] callArgs = { new Message(), keys };
                return (bool)processCmdKey.Invoke(dlg, callArgs)!;
            }

            using (var dialog = (Form)Activator.CreateInstance(dialogType, engine)!)
            {
                dialog.Show();
                dialog.Size = dialog.MinimumSize;
                Application.DoEvents();
                dialog.PerformLayout();
                if (CheckLayoutControls(dialog, dialog) < 28)
                    throw new Exception("SettingsDialog checked fewer than 28 visible controls at MinimumSize");

                var lsNote = (Label)dialog.Controls.Find("LsCompatibilityNoteLabel", true).Single();
                VerifyCompatibilityNoteLabel(lsNote, dialog);

                var includeSelfBox = (CheckBox)dialog.Controls.Find("IncludeSelfCheckBox", true).Single();
                var includeTempHpBox = (CheckBox)dialog.Controls.Find("IncludeTempHpCheckBox", true).Single();
                var suppressTownBox = (CheckBox)dialog.Controls.Find("SuppressInTownCheckBox", true).Single();
                var maxAlertNumeric = (NumericUpDown)dialog.Controls.Find("MaxAlertSecondsNumeric", true).Single();
                var fallbackDeathHpNumeric = (NumericUpDown)dialog.Controls.Find("FallbackDeathHpNumeric", true).Single();
                var saveWinNumeric = (NumericUpDown)dialog.Controls.Find("SaveWindowSecondsNumeric", true).Single();
                var castWinNumeric = (NumericUpDown)dialog.Controls.Find("SelfCastWindowSecondsNumeric", true).Single();
                var minHealNumeric = (NumericUpDown)dialog.Controls.Find("MinHealDeltaNumeric", true).Single();
                var showSavedNoticeBox = (CheckBox)dialog.Controls.Find("ShowSavedNoticeCheckBox", true).Single();
                var showRankBox = (CheckBox)dialog.Controls.Find("ShowRankOnStatusHudCheckBox", true).Single();
                var hudEnabledBox = (CheckBox)dialog.Controls.Find("HudEnabledCheckBox", true).Single();
                var bannerOpacityNumeric = (NumericUpDown)dialog.Controls.Find("BannerOpacityNumeric", true).Single();
                var flashBannerBox = (CheckBox)dialog.Controls.Find("FlashBannerCheckBox", true).Single();
                var showHpBarBox = (CheckBox)dialog.Controls.Find("ShowBannerHpBarCheckBox", true).Single();
                var statusHudEnabledBox = (CheckBox)dialog.Controls.Find("StatusHudEnabledCheckBox", true).Single();
                var statusFontBox = (TextBox)dialog.Controls.Find("StatusFontSizeTextBox", true).Single();
                var themeCombo = (ComboBox)dialog.Controls.Find("UiThemeCombo", true).Single();
                var raidKeymapLabel = (Label)dialog.Controls.Find("RaidKeymapReadinessLabel", true).Single();
                var lowBgColorBtn = (Button)dialog.Controls.Find("LowHpBgColorButton", true).Single();
                var resetColorsBtn = (Button)dialog.Controls.Find("ResetBannerColorsButton", true).Single();
                var toggleHotkeyBox = (TextBox)dialog.Controls.Find("TogglePluginHotkeyBox", true).Single();
                var toggleBannerHotkeyBox = (TextBox)dialog.Controls.Find("ToggleBannerHotkeyBox", true).Single();
                var focusHotkeyBox = (TextBox)dialog.Controls.Find("FocusTargetHotkeyBox", true).Single();
                var resetSavedHotkeyBox = (TextBox)dialog.Controls.Find("ResetSavedHotkeyBox", true).Single();
                var copyCardHotkeyBox = (TextBox)dialog.Controls.Find("CopyScorecardHotkeyBox", true).Single();
                var resetHotkeysBtn = (Button)dialog.Controls.Find("ResetHotkeysButton", true).Single();
                var fontBox = (TextBox)dialog.Controls.Find("FontSizeTextBox", true).Single();
                var deadNumeric = (NumericUpDown)dialog.Controls.Find("DeadAlertSecondsNumeric", true).Single();
                var excludeBox = (CheckBox)dialog.Controls.Find("ExcludeFromCaptureCheckBox", true).Single();
                var cooldownNumeric = (NumericUpDown)dialog.Controls.Find("AutoTargetCooldownNumeric", true).Single();
                var restorePrevBox = (CheckBox)dialog.Controls.Find("RestorePreviousTargetCheckBox", true).Single();
                var restoreDelayNumeric = (NumericUpDown)dialog.Controls.Find("RestoreTargetDelayNumeric", true).Single();
                var statusLabel = (Label)dialog.Controls.Find("StatusLabel", true).Single();
                var saveBtn = (Button)dialog.Controls.Find("SaveButton", true).Single();

                if (!hudEnabledBox.Visible || bannerOpacityNumeric.Minimum != 20m)
                    throw new Exception("Expected visible HudEnabledCheckBox and BannerOpacityNumeric Minimum=20");
                if (!fallbackDeathHpNumeric.Visible || fallbackDeathHpNumeric.Minimum != -5000m || fallbackDeathHpNumeric.Maximum != 0m || fallbackDeathHpNumeric.Value != -200m)
                    throw new Exception($"Expected visible FallbackDeathHpNumeric [-5000..0] default -200, got Visible={fallbackDeathHpNumeric.Visible}, Min={fallbackDeathHpNumeric.Minimum}, Max={fallbackDeathHpNumeric.Maximum}, Val={fallbackDeathHpNumeric.Value}");
                if (!restorePrevBox.Visible || !restoreDelayNumeric.Visible || !restorePrevBox.Checked || restoreDelayNumeric.Value != 0.2m)
                    throw new Exception("Expected visible RestorePreviousTargetCheckBox (true) and RestoreTargetDelayNumeric (0.2s) defaults");
                if (toggleHotkeyBox.Text != "Ctrl+Shift+K" || copyCardHotkeyBox.Text != "None" || toggleBannerHotkeyBox.Text != "None")
                    throw new Exception($"Unexpected default hotkey texts: {toggleHotkeyBox.Text} / {copyCardHotkeyBox.Text} / {toggleBannerHotkeyBox.Text}");

                // Escape inside hotkey box cancels recording without closing SettingsDialog.
                dialog.ActiveControl = focusHotkeyBox;
                Application.DoEvents();
                if (!SendDialogKey(dialog, Keys.Escape) || !dialog.Visible || focusHotkeyBox.Text != "Ctrl+Shift+H")
                    throw new Exception("Escape in hotkey recorder closed dialog or failed to restore binding");

                // Unmodified Backspace clears hotkey to None, and Reset Defaults restores all 4 defaults + clears 3 optional hotkeys.
                dialog.ActiveControl = focusHotkeyBox;
                if (!SendDialogKey(dialog, Keys.Back) || focusHotkeyBox.Text != "None")
                    throw new Exception($"Unmodified Backspace must clear hotkey to None, got '{focusHotkeyBox.Text}'");
                dialog.ActiveControl = toggleBannerHotkeyBox;
                SendDialogKey(dialog, Keys.F8 | Keys.Control);
                resetHotkeysBtn.PerformClick();
                if (focusHotkeyBox.Text != "Ctrl+Shift+H" || copyCardHotkeyBox.Text != "None" || resetSavedHotkeyBox.Text != "None" || toggleBannerHotkeyBox.Text != "None")
                    throw new Exception("Reset Defaults button did not restore default chords and clear optional hotkeys");

                // Duplicate hotkey on optional ToggleBannerHotkeyBox (Ctrl+Shift+K == TogglePluginHotkey) must block Save.
                dialog.ActiveControl = toggleBannerHotkeyBox;
                if (!SendDialogKey(dialog, Keys.K | Keys.Control | Keys.Shift) || toggleBannerHotkeyBox.Text != "Ctrl+Shift+K")
                    throw new Exception("Failed to record Ctrl+Shift+K into ToggleBannerHotkeyBox");
                saveBtn.PerformClick();
                if (!dialog.Visible || !statusLabel.Text.Contains("Duplicate hotkey", StringComparison.Ordinal))
                    throw new Exception("Duplicate hotkey on optional ToggleBannerHotkeyBox was not rejected");

                // Assign distinct custom hotkeys for CopyScorecard, ResetSaved, and ToggleBanner.
                dialog.ActiveControl = copyCardHotkeyBox;
                SendDialogKey(dialog, Keys.C | Keys.Control | Keys.Shift);
                dialog.ActiveControl = resetSavedHotkeyBox;
                SendDialogKey(dialog, Keys.R | Keys.Control | Keys.Shift);
                dialog.ActiveControl = toggleBannerHotkeyBox;
                SendDialogKey(dialog, Keys.B | Keys.Control | Keys.Shift);

                // Verify color swatch customization + Reset Colors button + re-apply custom colors without clipping.
                setBannerColorHex.Invoke(dialog, new object[] { "LowHpBgColorButton", "#123456" });
                if (!lowBgColorBtn.Text.Contains("#123456", StringComparison.Ordinal))
                    throw new Exception("SetBannerColorHex did not update LowHpBgColorButton label");
                resetColorsBtn.PerformClick();
                if (!lowBgColorBtn.Text.Contains("#B84200", StringComparison.Ordinal))
                    throw new Exception("Reset Colors button did not restore default #B84200");

                setBannerColorHex.Invoke(dialog, new object[] { "LowHpBgColorButton", "#123456" });
                setBannerColorHex.Invoke(dialog, new object[] { "LowHpBorderColorButton", "#654321" });
                setBannerColorHex.Invoke(dialog, new object[] { "CriticalBgColorButton", "#7A0019" });
                setBannerColorHex.Invoke(dialog, new object[] { "CriticalBorderColorButton", "#FFCC33" });
                setBannerColorHex.Invoke(dialog, new object[] { "BannerTextColorButton", "#F0F4FF" });
                setBannerColorHex.Invoke(dialog, new object[] { "FlashAltBgColorButton", "#10141C" });
                _ = CheckLayoutControls(dialog, dialog);

                // Exercise all 3 DDO x M3 themes in SettingsDialog without clipping.
                if (themeCombo.Items.Count != 3 || !raidKeymapLabel.Visible || !raidKeymapLabel.Text.Contains("Raid F-keys", StringComparison.Ordinal))
                    throw new Exception($"Expected 3 UiTheme options and visible RaidKeymapReadinessLabel, got items={themeCombo.Items.Count}, text='{raidKeymapLabel.Text}'");
                for (int ti = 0; ti < 3; ti++)
                {
                    themeCombo.SelectedIndex = ti;
                    Application.DoEvents();
                    _ = CheckLayoutControls(dialog, dialog);
                }
                themeCombo.SelectedIndex = 1; // CannithArcane for persistence check

                includeSelfBox.Checked = false;
                includeTempHpBox.Checked = true;
                suppressTownBox.Checked = true;
                maxAlertNumeric.Value = 15;
                fallbackDeathHpNumeric.Value = -350;
                saveWinNumeric.Value = 7.5m;
                castWinNumeric.Value = 4.5m;
                minHealNumeric.Value = 12;
                showSavedNoticeBox.Checked = false;
                showRankBox.Checked = true;
                bannerOpacityNumeric.Value = 35; // Exercises 20..255 range below old 80 floor!
                flashBannerBox.Checked = false;
                showHpBarBox.Checked = false;
                statusHudEnabledBox.Checked = true;
                statusFontBox.Text = "16";
                fontBox.Text = "68";
                deadNumeric.Value = 11;
                excludeBox.Checked = false;
                cooldownNumeric.Value = 4.5m;
                restorePrevBox.Checked = false;
                restoreDelayNumeric.Value = 0.8m;

                // Simulate background hotkey toggles of PluginEnabled, HudEnabled, and Mode + live stats while dialog is open!
                // Because those 3 controls were NOT touched in the dialog, SaveAndClose must preserve their live state!
                settingsType.GetProperty("PluginEnabled")!.SetValue(settings, false);
                settingsType.GetProperty("HudEnabled")!.SetValue(settings, false);
                settingsType.GetProperty("Mode")!.SetValue(settings, Enum.Parse(modeEnum, "SinglePlayer"));
                settingsType.GetProperty("PeopleSaved")!.SetValue(settings, 18);
                settingsType.GetProperty("CurrentSaveStreak")!.SetValue(settings, 6);
                settingsType.GetProperty("BestSaveStreak")!.SetValue(settings, 11);
                settingsType.GetProperty("ClosestCallPlayerName")!.SetValue(settings, "Tanky");
                settingsType.GetProperty("ClosestCallPreHealHp")!.SetValue(settings, 4);
                settingsType.GetProperty("ClosestCallPreHealPercent")!.SetValue(settings, 2.0);
                settingsType.GetProperty("BiggestOverkillSaved")!.SetValue(settings, 350);
                settingsType.GetProperty("OnePercentClubSaves")!.SetValue(settings, 3);
                settingsType.GetProperty("BestReflexMs")!.SetValue(settings, 175);
                settingsType.GetProperty("AvgReflexMs")!.SetValue(settings, 240);
                settingsType.GetProperty("ReflexSampleCount")!.SetValue(settings, 4);

                saveBtn.PerformClick();
            }

            using (var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "healers-helper.json"))))
            {
                var root = saved.RootElement;
                if (root.GetProperty("PluginEnabled").GetBoolean() != false
                    || root.GetProperty("HudEnabled").GetBoolean() != false
                    || root.GetProperty("Mode").GetString() != "SinglePlayer"
                    || root.GetProperty("FontSizePixels").GetInt32() != 68
                    || root.GetProperty("StatusFontSizePixels").GetInt32() != 16
                    || root.GetProperty("BannerOpacity").GetInt32() != 35
                    || root.GetProperty("LowHpBgHex").GetString() != "#123456"
                    || root.GetProperty("LowHpBorderHex").GetString() != "#654321"
                    || root.GetProperty("CriticalBgHex").GetString() != "#7A0019"
                    || root.GetProperty("CriticalBorderHex").GetString() != "#FFCC33"
                    || root.GetProperty("BannerTextHex").GetString() != "#F0F4FF"
                    || root.GetProperty("FlashAltBgHex").GetString() != "#10141C"
                    || root.GetProperty("FlashBanner").GetBoolean() != false
                    || root.GetProperty("ShowBannerHpBar").GetBoolean() != false
                    || root.GetProperty("IncludeSelfInPartyMode").GetBoolean() != false
                    || root.GetProperty("IncludeTempHpInThreshold").GetBoolean() != true
                    || root.GetProperty("Theme").GetString() != "CannithArcane"
                    || root.GetProperty("SuppressAlertsInTown").GetBoolean() != true
                    || root.GetProperty("MaxAlertSeconds").GetInt32() != 15
                    || root.GetProperty("FallbackDeathHp").GetInt32() != -350
                    || Math.Abs(root.GetProperty("SaveWindowSeconds").GetDouble() - 7.5) > 0.001
                    || Math.Abs(root.GetProperty("SelfCastWindowSeconds").GetDouble() - 4.5) > 0.001
                    || root.GetProperty("MinHealDelta").GetInt32() != 12
                    || root.GetProperty("ShowSavedNotice").GetBoolean() != false
                    || root.GetProperty("DeadAlertSeconds").GetInt32() != 11
                    || root.GetProperty("ExcludeFromCapture").GetBoolean() != false
                    || root.GetProperty("PeopleSaved").GetInt32() != 18
                    || root.GetProperty("CurrentSaveStreak").GetInt32() != 6
                    || root.GetProperty("BestSaveStreak").GetInt32() != 11
                    || root.GetProperty("ClosestCallPlayerName").GetString() != "Tanky"
                    || root.GetProperty("OnePercentClubSaves").GetInt32() != 3
                    || root.GetProperty("BestReflexMs").GetInt32() != 175
                    || Math.Abs(root.GetProperty("AutoTargetCooldownSeconds").GetDouble() - 4.5) > 0.001
                    || root.GetProperty("RestorePreviousTargetAfterHeal").GetBoolean() != false
                    || Math.Abs(root.GetProperty("RestoreTargetDelaySeconds").GetDouble() - 0.8) > 0.001)
                {
                    throw new Exception("SettingsDialog save did not round-trip extended settings, custom colors, opacity 35, dirty-state preservation, and live gamification stats");
                }
            }

            if (engineType.GetField("_restoreTracker", fields)!.GetValue(engine) is null)
                throw new Exception("HealerEngine.Restore did not lazily initialize _restoreTracker on uninitialized smoke instance");

            // Re-enable PluginEnabled & HudEnabled via engine methods and verify dynamic tooltips!
            engineType.GetMethod("TogglePluginEnabled")!.Invoke(engine, null);
            engineType.GetMethod("ToggleBannerHud")!.Invoke(engine, null);
            controlForm.GetType().GetMethod("RefreshState", fields)!.Invoke(controlForm, null);
            var toolTip = (ToolTip)controlForm.GetType().GetProperty("ButtonToolTip", fields)!.GetValue(controlForm)!;
            var copyBtn = (Button)controlForm.Controls.Find("CopyScorecardButton", true).Single();
            var resetBtn = (Button)controlForm.Controls.Find("ResetSavedButton", true).Single();
            var toggleBannerControlBtn = (Button)controlForm.Controls.Find("ToggleBannerButton", true).Single();
            if (!(toolTip.GetToolTip(copyBtn) ?? string.Empty).Contains("Ctrl+Shift+C", StringComparison.Ordinal)
                || !(toolTip.GetToolTip(resetBtn) ?? string.Empty).Contains("Ctrl+Shift+R", StringComparison.Ordinal)
                || !(toolTip.GetToolTip(toggleBannerControlBtn) ?? string.Empty).Contains("Ctrl+Shift+B", StringComparison.Ordinal))
            {
                throw new Exception("HealerControlForm tooltips did not dynamically reflect saved custom hotkeys (Ctrl+Shift+C/R/B)");
            }

            // Verify GetChatScorecard() and CopyChatScorecard() produce pure printable ASCII <= 185 chars
            string card = (string)engineType.GetMethod("GetChatScorecard")!.Invoke(engine, null)!;
            if (card.Length is < 1 or > 185 || card.Any(c => c is < (char)0x20 or > (char)0x7E) || !card.Contains("Lifeweaver", StringComparison.Ordinal))
                throw new Exception($"Unexpected GetChatScorecard output ({card.Length}): {card}");
            _ = engineType.GetMethod("CopyChatScorecard")!.Invoke(engine, null);

            // Verify ResetPeopleSaved() zeroes all gamification stats
            engineType.GetMethod("ResetPeopleSaved")!.Invoke(engine, null);
            var resetSettings = engineType.GetMethod("GetSettings")!.Invoke(engine, null)!;
            if ((int)settingsType.GetProperty("PeopleSaved")!.GetValue(resetSettings)! != 0
                || (int)settingsType.GetProperty("CurrentSaveStreak")!.GetValue(resetSettings)! != 0
                || (int)settingsType.GetProperty("BestSaveStreak")!.GetValue(resetSettings)! != 0
                || (int)settingsType.GetProperty("OnePercentClubSaves")!.GetValue(resetSettings)! != 0
                || (int)settingsType.GetProperty("BestReflexMs")!.GetValue(resetSettings)! != 0
                || !string.IsNullOrEmpty((string)settingsType.GetProperty("ClosestCallPlayerName")!.GetValue(resetSettings)!))
            {
                throw new Exception("ResetPeopleSaved did not zero all persisted gamification stats");
            }

            Console.WriteLine("PASS SettingsDialog 5-group layout, 6 color pickers, opacity 20..255, dirty-tracking, 7-hotkey validation, dynamic tooltips, and ASCII scorecard");

            // 2. BannerHudWindow layered native surface, styles, flash phases, interactive resize/move/wheel, banner-disable, and disposal
            const long requiredEx = WsExTransparent | WsExToolWindow | WsExLayered | WsExNoActivate;
            Type hudType = assembly.GetType("VoK.HealersHelper.BannerHudWindow")!;
            Type cmdEnum = assembly.GetType("VoK.HealersHelper.HudCommand")!;
            object queue = Activator.CreateInstance(typeof(ConcurrentQueue<>).MakeGenericType(cmdEnum))!;
            string lastHudStatus = "";
            var hud = (Form)Activator.CreateInstance(hudType, engine, queue, new Action<string>(s => lastHudStatus = s))!;
            Size bannerSize;
            try
            {
                IntPtr handle = hud.Handle;
                long exStyle = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
                if ((exStyle & requiredEx) != requiredEx)
                    throw new Exception($"Missing required extended styles on BannerHudWindow: 0x{exStyle:X}");

                Type frameType = assembly.GetType("VoK.HealersHelper.BannerFrame")!;
                MethodInfo renderMethod = hudType.GetMethod("RenderForSmoke", fields)!;
                MethodInfo dragSmoke = hudType.GetMethod("SimulateEditDragForSmoke", fields)!;
                MethodInfo wheelSmoke = hudType.GetMethod("SimulateEditWheelForSmoke", fields)!;
                MethodInfo tickFrame = hudType.GetMethod("TickFrame", fields)!;
                MethodInfo beginPreview = hudType.GetMethod("BeginPreview", fields)!;
                PropertyInfo surfaceProp = hudType.GetProperty("Surface", fields)!;

                Color SampleCorner()
                {
                    object surface = surfaceProp.GetValue(hud)!;
                    var img = (Bitmap)surface.GetType().GetProperty("Image", fields)!.GetValue(surface)!;
                    return img.GetPixel(1, 1);
                }

                object frameOn = Activator.CreateInstance(
                    frameType,
                    true,
                    "DOWN: TANKY",
                    "-12/450 HP — CRITICAL",
                    72,
                    true,
                    unchecked((int)0xFFFFFFFF),
                    unchecked((int)0xF0960A14),
                    unchecked((int)0xFFFFD644))!;
                renderMethod.Invoke(hud, new[] { frameOn, true });
                bannerSize = hud.ClientSize;
                Color onColor = SampleCorner();
                if (bannerSize.Width < 420 || bannerSize.Height < 100)
                    throw new Exception($"Expected huge banner dimensions, got {bannerSize}");
                if (onColor.A == 0 || onColor.R < 100)
                    throw new Exception($"Expected crimson/gold flash-on pixel, got {onColor}");

                object frameOff = Activator.CreateInstance(
                    frameType,
                    true,
                    "DOWN: TANKY",
                    "-12/450 HP — CRITICAL",
                    72,
                    false,
                    unchecked((int)0xFFFF4D5A),
                    unchecked((int)0xEC0A0A0E),
                    unchecked((int)0xFFFF3848))!;
                renderMethod.Invoke(hud, new[] { frameOff, false });
                Color offColor = SampleCorner();
                if (hud.ClientSize != bannerSize || offColor.ToArgb() == onColor.ToArgb())
                    throw new Exception($"Flash off must keep dimensions and transition color ({onColor} -> {offColor})");

                // Exercise interactive corner-drag resize + center move + wheel font resize + Ctrl+wheel opacity!
                int preDragFont = (int)settingsType.GetProperty("FontSizePixels")!.GetValue(engineType.GetMethod("GetSettings")!.Invoke(engine, null)!)!;
                Point bottomRightGrip = new(hud.ClientSize.Width - 5, hud.ClientSize.Height - 5);
                dragSmoke.Invoke(hud, new object[] { bottomRightGrip, 120, 45 });
                var afterCornerDrag = engineType.GetMethod("GetSettings")!.Invoke(engine, null)!;
                int postDragFont = (int)settingsType.GetProperty("FontSizePixels")!.GetValue(afterCornerDrag)!;
                if (postDragFont <= preDragFont)
                    throw new Exception($"Expected corner drag to grow FontSizePixels ({preDragFont} -> {postDragFont})");

                wheelSmoke.Invoke(hud, new object[] { 120, false }); // +2px font
                wheelSmoke.Invoke(hud, new object[] { 240, true });  // +20 opacity (35 -> 55)
                var afterWheel = engineType.GetMethod("GetSettings")!.Invoke(engine, null)!;
                if ((int)settingsType.GetProperty("FontSizePixels")!.GetValue(afterWheel)! != Math.Min(140, postDragFont + 2)
                    || (int)settingsType.GetProperty("BannerOpacity")!.GetValue(afterWheel)! != 55)
                {
                    throw new Exception("Wheel / Ctrl+Wheel gestures did not persist updated FontSizePixels and BannerOpacity");
                }

                // Verify full banner disable suppresses live alert while still permitting Preview Banner!
                hudType.GetField("_editUntil", fields)!.SetValue(hud, double.NegativeInfinity);
                engineType.GetMethod("ToggleBannerHud")!.Invoke(engine, null); // flips HudEnabled -> false
                double liveNow = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
                tickFrame.Invoke(hud, new object[] { liveNow });
                if (hud.Visible || !lastHudStatus.Contains("Banner disabled", StringComparison.Ordinal))
                    throw new Exception($"Disabled banner must hide live window and report Banner disabled, got Visible={hud.Visible}, status='{lastHudStatus}'");
                beginPreview.Invoke(hud, null);
                double previewNow = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
                tickFrame.Invoke(hud, new object[] { previewNow + 0.1 });
                if (!hud.Visible)
                    throw new Exception("Preview Banner must still display on demand even when HudEnabled=false");

                object frameHidden = Activator.CreateInstance(frameType, false, "", "", 72, false, 0, 0, 0)!;
                renderMethod.Invoke(hud, new[] { frameHidden, false });
                Color clearedColor = SampleCorner();
                if (clearedColor.A != 0)
                    throw new Exception($"Hidden frame must clear surface to alpha 0, got {clearedColor}");
            }
            finally
            {
                hudType.GetProperty("AllowClose", fields)!.SetValue(hud, true);
                hud.Dispose();
            }

            if (!hud.IsDisposed)
                throw new Exception("BannerHudWindow did not dispose cleanly");

            Console.WriteLine("PASS BannerHudWindow WS_EX styles, DIB render, interactive drag/resize/wheel/Ctrl+wheel, full banner disable, and disposal");

            // 3. StatusHudWindow compact DIB surface, WS_EX styles, OFF vs PARTY rendering, and disposal
            Type statusHudType = assembly.GetType("VoK.HealersHelper.StatusHudWindow")!;
            var statusHud = (Form)Activator.CreateInstance(statusHudType, engine)!;
            try
            {
                IntPtr statusHandle = statusHud.Handle;
                long statusExStyle = GetWindowLongPtr(statusHandle, GwlExStyle).ToInt64();
                if ((statusExStyle & requiredEx) != requiredEx)
                    throw new Exception($"Missing required extended styles on StatusHudWindow: 0x{statusExStyle:X}");

                Type statusFrameType = assembly.GetType("VoK.HealersHelper.StatusHudFrame")!;
                MethodInfo statusRender = statusHudType.GetMethod("RenderForSmoke", fields)!;
                PropertyInfo statusSurfaceProp = statusHudType.GetProperty("Surface", fields)!;

                object partyStatusFrame = Activator.CreateInstance(
                    statusFrameType,
                    true,
                    "HEALER  PARTY <=50%  •  SAVED 3 (Field Medic)",
                    14,
                    unchecked((int)0xFF7AE582),
                    false,
                    "")!;
                statusRender.Invoke(statusHud, new[] { partyStatusFrame, true });
                Size partySize = statusHud.ClientSize;
                if (partySize.Width >= bannerSize.Width || partySize.Height >= bannerSize.Height / 2)
                    throw new Exception($"StatusHudWindow must be compact compared to BannerHudWindow ({partySize} vs {bannerSize})");

                object statusSurface = statusSurfaceProp.GetValue(statusHud)!;
                var statusBitmap = (Bitmap)statusSurface.GetType().GetProperty("Image", fields)!.GetValue(statusSurface)!;
                if (statusBitmap.GetPixel(0, 0).A != 0)
                    throw new Exception("StatusHudWindow normal mode must keep transparent background");

                object offStatusFrame = Activator.CreateInstance(
                    statusFrameType,
                    true,
                    "HEALER  OFF",
                    14,
                    unchecked((int)0xFF9AA4B2),
                    false,
                    "")!;
                statusRender.Invoke(statusHud, new[] { offStatusFrame, false });
                if (statusHud.ClientSize.Width >= partySize.Width)
                    throw new Exception($"HEALER  OFF should be narrower than PARTY status ({statusHud.ClientSize} vs {partySize})");
            }
            finally
            {
                statusHudType.GetProperty("AllowClose", fields)!.SetValue(statusHud, true);
                statusHud.Dispose();
            }

            if (!statusHud.IsDisposed)
                throw new Exception("StatusHudWindow did not dispose cleanly");

            Console.WriteLine("PASS StatusHudWindow WS_EX styles (incl. TOOLWINDOW), compact DIB size, transparent rendering, and disposal");
        }
        finally
        {
            pluginType.GetField("_engine", fields)!.SetValue(plugin, null);
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    private static Assembly LoadPlugin(string path)
    {
        if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return Assembly.LoadFrom(path);

        using var archive = ZipFile.OpenRead(path);
        var expected = new[] { "dll", "deps.json", "metadata" }
            .Select(ext => "plugins/HealersHelper/VoK.HealersHelper." + ext);
        if (!archive.Entries.Select(e => e.FullName).SequenceEqual(expected))
            throw new Exception("Installer ZIP must contain exactly three runtime entries, DLL first");

        using var source = archive.Entries[0].Open();
        using var ms = new MemoryStream();
        source.CopyTo(ms);
        ms.Position = 0;
        return AssemblyLoadContext.Default.LoadFromStream(ms);
    }

    private static int CheckLayoutControls(Control root, Control topForm)
    {
        int checkedCount = 0;
        foreach (Control control in root.Controls)
        {
            if (control is Button)
            {
                Size preferred = control.GetPreferredSize(Size.Empty);
                if (!root.ClientRectangle.Contains(control.Bounds) ||
                    control.Height < preferred.Height ||
                    control.Width < preferred.Width)
                {
                    throw new Exception($"Clipped button '{control.Text}' bounds={control.Bounds} pref={preferred} parentClient={root.ClientRectangle}");
                }
            }

            if (control.Visible && (control is Button or ComboBox or TextBox or NumericUpDown or CheckBox))
            {
                checkedCount++;
                Rectangle inForm = topForm.RectangleToClient(control.Parent!.RectangleToScreen(control.Bounds));
                if (inForm.Left < 0 || inForm.Right > topForm.ClientSize.Width)
                {
                    throw new Exception($"Control '{control.Name}:{control.Text}' clips horizontally ({inForm}) in form width {topForm.ClientSize.Width}");
                }
            }

            checkedCount += CheckLayoutControls(control, topForm);
        }

        return checkedCount;
    }
}
