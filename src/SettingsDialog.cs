using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace VoK.HealersHelper;

internal sealed class SettingsDialog : Form
{
    private sealed record HotkeyEntry(TextBox Box, string ActionName, Func<HotkeyBinding> Getter, Action<HotkeyBinding> Setter);

    private readonly IHealerEngineView _engine;
    private readonly Settings _settings;
    private readonly Font _dialogFont = new("Segoe UI", 9);
    private readonly List<HotkeyEntry> _hotkeyEntries = new(7);

    private static readonly Color SlateBg = Color.FromArgb(17, 19, 24);
    private static readonly Color SurfaceBg = Color.FromArgb(26, 29, 36);
    private static readonly Color ButtonBg = Color.FromArgb(34, 38, 48);
    private static readonly Color BorderHairline = Color.FromArgb(62, 68, 86);
    private static readonly Color TextPrimary = Color.FromArgb(236, 238, 242);
    private static readonly Color GoldAccent = Color.FromArgb(226, 183, 91);

    // Group 1: HP Alert & Tracking
    private readonly CheckBox _pluginEnabled = new() { Name = "PluginEnabledCheckBox", Text = "Plugin enabled (master)", AutoSize = true };
    private readonly ComboBox _mode = new() { Name = "ModeCombo", DropDownStyle = ComboBoxStyle.DropDownList, Width = 125 };
    private readonly CheckBox _includeSelf = new() { Name = "IncludeSelfCheckBox", Text = "Include self in Party mode", AutoSize = true };
    private readonly CheckBox _includeTempHp = new() { Name = "IncludeTempHpCheckBox", Text = "Count Temp HP in threshold", AutoSize = true };
    private readonly NumericUpDown _threshold = new() { Name = "HpThresholdNumeric", Minimum = 5, Maximum = 95, Width = 60 };
    private readonly NumericUpDown _hysteresis = new() { Name = "RecoveryHysteresisNumeric", Minimum = 0, Maximum = 20, Width = 60 };
    private readonly NumericUpDown _deadSeconds = new() { Name = "DeadAlertSecondsNumeric", Minimum = 0, Maximum = 60, Width = 60 };
    private readonly NumericUpDown _maxAlertSeconds = new() { Name = "MaxAlertSecondsNumeric", Minimum = 0, Maximum = 120, Width = 65 };
    private readonly CheckBox _suppressInTown = new() { Name = "SuppressInTownCheckBox", Text = "Suppress alerts in town", AutoSize = true };

    // Group 2: Clutch Saves & Gamification
    private readonly NumericUpDown _saveWindowSeconds = new() { Name = "SaveWindowSecondsNumeric", Minimum = 1.0m, Maximum = 15.0m, DecimalPlaces = 1, Increment = 0.5m, Width = 65 };
    private readonly NumericUpDown _selfCastWindowSeconds = new() { Name = "SelfCastWindowSecondsNumeric", Minimum = 1.0m, Maximum = 15.0m, DecimalPlaces = 1, Increment = 0.5m, Width = 65 };
    private readonly NumericUpDown _minHealDelta = new() { Name = "MinHealDeltaNumeric", Minimum = 1, Maximum = 200, Width = 65 };
    private readonly CheckBox _showSavedNotice = new() { Name = "ShowSavedNoticeCheckBox", Text = "Show 'SAVED!' banner notice", AutoSize = true };
    private readonly CheckBox _showRankOnStatusHud = new() { Name = "ShowRankOnStatusHudCheckBox", Text = "Show prestige rank on Status HUD", AutoSize = true };

    // Group 3: Center Banner, Status HUD & Lossless Scaling
    private readonly CheckBox _hudEnabled = new()
    {
        Name = "HudEnabledCheckBox",
        Text = "Enable center alert banner (targeting & stats stay active when off)",
        AutoSize = true
    };
    private readonly TextBox _fontSize = new() { Name = "FontSizeTextBox", Width = 55, MaxLength = 3 };
    private readonly NumericUpDown _bannerOpacity = new() { Name = "BannerOpacityNumeric", Minimum = 20, Maximum = 255, Increment = 5, Width = 65 };
    private readonly CheckBox _flashBanner = new() { Name = "FlashBannerCheckBox", Text = "Flash banner", AutoSize = true };
    private readonly CheckBox _showBannerHpBar = new() { Name = "ShowBannerHpBarCheckBox", Text = "Spatial HP bar", AutoSize = true };
    private readonly NumericUpDown _flashInterval = new() { Name = "FlashIntervalNumeric", Minimum = 100, Maximum = 1000, Increment = 25, Width = 70 };
    private readonly CheckBox _soundOnAlert = new() { Name = "SoundOnAlertCheckBox", Text = "Sound on alert", AutoSize = true };
    private readonly CheckBox _statusHudEnabled = new() { Name = "StatusHudEnabledCheckBox", Text = "Status HUD enabled", AutoSize = true };
    private readonly TextBox _statusFontSize = new() { Name = "StatusFontSizeTextBox", Width = 50, MaxLength = 3 };
    private readonly ComboBox _themeCombo = new() { Name = "UiThemeCombo", DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly CheckBox _excludeCapture = new() { Name = "ExcludeFromCaptureCheckBox", Text = "Exclude from capture (Lossless Scaling)", AutoSize = true };
    private readonly Button _lowHpBgColorBtn = MakeAutoButton("LowHpBgColorButton", "Low BG: #B84200");
    private readonly Button _lowHpBorderColorBtn = MakeAutoButton("LowHpBorderColorButton", "Low Border: #FFE066");
    private readonly Button _criticalBgColorBtn = MakeAutoButton("CriticalBgColorButton", "Crit BG: #960A14");
    private readonly Button _criticalBorderColorBtn = MakeAutoButton("CriticalBorderColorButton", "Crit Border: #FFD644");
    private readonly Button _bannerTextColorBtn = MakeAutoButton("BannerTextColorButton", "Text: #FFFFFF");
    private readonly Button _flashAltBgColorBtn = MakeAutoButton("FlashAltBgColorButton", "Flash Alt: #0A0A0E");
    private readonly Button _resetBannerColorsBtn = MakeAutoButton("ResetBannerColorsButton", "Reset Colors");
    private readonly Label _lsCompatibilityNoteLabel = new()
    {
        Name = "LsCompatibilityNoteLabel",
        Text = HealerControlForm.CompatibilityNoteText,
        AutoSize = true,
        MaximumSize = new Size(530, 0),
        Margin = new Padding(4, 4, 4, 2),
        ForeColor = Color.FromArgb(159, 170, 192)
    };

    // Group 4: Auto-Targeting
    private readonly CheckBox _autoTarget = new() { Name = "AutoTargetCheckBox", Text = "Auto-target critical player on alert", AutoSize = true };
    private readonly ComboBox _targetMethod = new() { Name = "TargetMethodCombo", DropDownStyle = ComboBoxStyle.DropDownList, Width = 135 };
    private readonly NumericUpDown _targetCooldown = new() { Name = "AutoTargetCooldownNumeric", Minimum = 0.5m, Maximum = 30.0m, DecimalPlaces = 1, Increment = 0.5m, Width = 65 };
    private readonly CheckBox _restorePreviousTarget = new() { Name = "RestorePreviousTargetCheckBox", Text = "Restore previous target once healed", AutoSize = true };
    private readonly NumericUpDown _restoreDelay = new() { Name = "RestoreTargetDelayNumeric", Minimum = 0.0m, Maximum = 5.0m, DecimalPlaces = 1, Increment = 0.1m, Width = 60 };
    private readonly Label _raidKeymapLabel = new()
    {
        Name = "RaidKeymapReadinessLabel",
        AutoSize = true,
        MaximumSize = new Size(530, 0),
        Margin = new Padding(4, 4, 4, 2),
        ForeColor = GoldAccent
    };

    // Group 5: Global Hotkeys
    private readonly TextBox _togglePluginHotkeyBox = new() { Name = "TogglePluginHotkeyBox", ReadOnly = true, Width = 140 };
    private readonly TextBox _focusHotkeyBox = new() { Name = "FocusTargetHotkeyBox", ReadOnly = true, Width = 140 };
    private readonly TextBox _toggleHotkeyBox = new() { Name = "ToggleModeHotkeyBox", ReadOnly = true, Width = 140 };
    private readonly TextBox _criticalHotkeyBox = new() { Name = "TargetCriticalHotkeyBox", ReadOnly = true, Width = 140 };
    private readonly TextBox _resetSavedHotkeyBox = new() { Name = "ResetSavedHotkeyBox", ReadOnly = true, Width = 140 };
    private readonly TextBox _copyScorecardHotkeyBox = new() { Name = "CopyScorecardHotkeyBox", ReadOnly = true, Width = 140 };
    private readonly TextBox _toggleBannerHotkeyBox = new() { Name = "ToggleBannerHotkeyBox", ReadOnly = true, Width = 140 };

    private readonly Label _status = new() { Name = "StatusLabel", AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(2, 6, 2, 6), ForeColor = GoldAccent };

    private bool _pluginEnabledDirty;
    private bool _hudEnabledDirty;
    private bool _modeDirty;

    private string _lowHpBgHex;
    private string _lowHpBorderHex;
    private string _criticalBgHex;
    private string _criticalBorderHex;
    private string _bannerTextHex;
    private string _flashAltBgHex;

    private HotkeyBinding _togglePluginHotkey;
    private HotkeyBinding _toggleBannerHotkey;
    private HotkeyBinding _focusHotkey;
    private HotkeyBinding _toggleHotkey;
    private HotkeyBinding _criticalHotkey;
    private HotkeyBinding _resetSavedHotkey;
    private HotkeyBinding _copyScorecardHotkey;

    public SettingsDialog(IHealerEngineView engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _settings = engine.GetSettings().Copy();

        _lowHpBgHex = Settings.NormalizeHexColor(_settings.LowHpBgHex, Settings.DefaultLowHpBgHex);
        _lowHpBorderHex = Settings.NormalizeHexColor(_settings.LowHpBorderHex, Settings.DefaultLowHpBorderHex);
        _criticalBgHex = Settings.NormalizeHexColor(_settings.CriticalBgHex, Settings.DefaultCriticalBgHex);
        _criticalBorderHex = Settings.NormalizeHexColor(_settings.CriticalBorderHex, Settings.DefaultCriticalBorderHex);
        _bannerTextHex = Settings.NormalizeHexColor(_settings.BannerTextHex, Settings.DefaultBannerTextHex);
        _flashAltBgHex = Settings.NormalizeHexColor(_settings.FlashAltBgHex, Settings.DefaultFlashAltBgHex);

        _togglePluginHotkey = _settings.TogglePluginHotkey;
        _toggleBannerHotkey = _settings.ToggleBannerHotkey;
        _focusHotkey = _settings.FocusTargetHotkey;
        _toggleHotkey = _settings.ToggleModeHotkey;
        _criticalHotkey = _settings.TargetCriticalHotkey;
        _resetSavedHotkey = _settings.ResetSavedHotkey;
        _copyScorecardHotkey = _settings.CopyScorecardHotkey;

        Text = "DDO Healer's Helper — Settings";
        Font = _dialogFont;
        BackColor = SlateBg;
        ForeColor = TextPrimary;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(760, 740);
        MinimumSize = new Size(640, 560);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(12),
            BackColor = SlateBg,
            ForeColor = TextPrimary
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 7; i++)
        {
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        // Section 1: HP Alert & Tracking
        var trackingGroup = CreateGroup("HP Alert & Tracking");
        var trackingTable = CreateStackTable(3);

        _pluginEnabled.Checked = _settings.PluginEnabled;
        _includeSelf.Checked = _settings.IncludeSelfInPartyMode;
        _includeTempHp.Checked = _settings.IncludeTempHpInThreshold;
        _suppressInTown.Checked = _settings.SuppressAlertsInTown;
        foreach (var m in Enum.GetValues<TrackingMode>())
        {
            _mode.Items.Add(m);
        }

        _mode.SelectedItem = _settings.Mode;
        _threshold.Value = Math.Clamp(_settings.HpThresholdPercent, 5, 95);
        _hysteresis.Value = Math.Clamp(_settings.RecoveryHysteresisPercent, 0, 20);
        _deadSeconds.Value = Math.Clamp(_settings.DeadAlertSeconds, 0, 60);
        _maxAlertSeconds.Value = Math.Clamp(_settings.MaxAlertSeconds, 0, 120);

        var trackingRow0 = CreateFlow();
        trackingRow0.Controls.AddRange(new Control[] { _pluginEnabled, MakeFieldLabel("Mode"), _mode, _includeSelf });

        var trackingRow1 = CreateFlow();
        trackingRow1.Controls.AddRange(new Control[]
        {
            MakeFieldLabel("Alert HP %"), _threshold,
            MakeFieldLabel("Hysteresis %"), _hysteresis,
            MakeFieldLabel("Dead alert (s)"), _deadSeconds
        });

        var trackingRow2 = CreateFlow();
        trackingRow2.Controls.AddRange(new Control[]
        {
            MakeFieldLabel("Max alert (s, 0=inf)"), _maxAlertSeconds,
            _suppressInTown,
            _includeTempHp
        });

        trackingTable.Controls.Add(trackingRow0, 0, 0);
        trackingTable.Controls.Add(trackingRow1, 0, 1);
        trackingTable.Controls.Add(trackingRow2, 0, 2);
        trackingGroup.Controls.Add(trackingTable);
        root.Controls.Add(trackingGroup, 0, 0);

        // Section 2: Clutch Saves & Gamification
        var gamificationGroup = CreateGroup("Clutch Saves & Gamification");
        var gamificationTable = CreateStackTable(2);

        _saveWindowSeconds.Value = (decimal)Math.Clamp(_settings.SaveWindowSeconds, 1.0, 15.0);
        _selfCastWindowSeconds.Value = (decimal)Math.Clamp(_settings.SelfCastWindowSeconds, 1.0, 15.0);
        _minHealDelta.Value = Math.Clamp(_settings.MinHealDelta, 1, 200);
        _showSavedNotice.Checked = _settings.ShowSavedNotice;
        _showRankOnStatusHud.Checked = _settings.ShowRankOnStatusHud;

        var gamificationRow0 = CreateFlow();
        gamificationRow0.Controls.AddRange(new Control[]
        {
            MakeFieldLabel("Save window (s)"), _saveWindowSeconds,
            MakeFieldLabel("Cast window (s)"), _selfCastWindowSeconds,
            MakeFieldLabel("Min heal HP"), _minHealDelta
        });

        var gamificationRow1 = CreateFlow();
        gamificationRow1.Controls.AddRange(new Control[] { _showSavedNotice, _showRankOnStatusHud });

        gamificationTable.Controls.Add(gamificationRow0, 0, 0);
        gamificationTable.Controls.Add(gamificationRow1, 0, 1);
        gamificationGroup.Controls.Add(gamificationTable);
        root.Controls.Add(gamificationGroup, 0, 1);

        // Section 3: Banner Appearance, Status HUD & Lossless Scaling
        var bannerGroup = CreateGroup("Center Banner, Status HUD & Lossless Scaling");
        var bannerTable = CreateStackTable(6);

        _hudEnabled.Checked = _settings.HudEnabled;
        _fontSize.Text = _settings.FontSizePixels.ToString(CultureInfo.InvariantCulture);
        _bannerOpacity.Value = Math.Clamp(_settings.BannerOpacity, 20, 255);
        _flashBanner.Checked = _settings.FlashBanner;
        _showBannerHpBar.Checked = _settings.ShowBannerHpBar;
        _flashInterval.Value = Math.Clamp(_settings.FlashIntervalMs, 100, 1000);
        _statusHudEnabled.Checked = _settings.StatusHudEnabled;
        _statusFontSize.Text = _settings.StatusFontSizePixels.ToString(CultureInfo.InvariantCulture);
        foreach (var pal in UiThemePalette.All)
        {
            _themeCombo.Items.Add(pal.DisplayName);
        }
        _themeCombo.SelectedIndex = Math.Clamp((int)_settings.Theme, 0, UiThemePalette.All.Length - 1);
        _soundOnAlert.Checked = _settings.SoundOnAlert;
        _excludeCapture.Checked = _settings.ExcludeFromCapture;

        WireColorSwatchButton(_lowHpBgColorBtn, () => _lowHpBgHex, hex => _lowHpBgHex = hex);
        WireColorSwatchButton(_lowHpBorderColorBtn, () => _lowHpBorderHex, hex => _lowHpBorderHex = hex);
        WireColorSwatchButton(_criticalBgColorBtn, () => _criticalBgHex, hex => _criticalBgHex = hex);
        WireColorSwatchButton(_criticalBorderColorBtn, () => _criticalBorderHex, hex => _criticalBorderHex = hex);
        WireColorSwatchButton(_bannerTextColorBtn, () => _bannerTextHex, hex => _bannerTextHex = hex);
        WireColorSwatchButton(_flashAltBgColorBtn, () => _flashAltBgHex, hex => _flashAltBgHex = hex);
        _resetBannerColorsBtn.Click += (_, _) => ResetBannerColorsToDefaults();
        RefreshColorSwatches();

        var bannerRow0 = CreateFlow();
        bannerRow0.Controls.Add(_hudEnabled);

        var bannerRow1 = CreateFlow();
        bannerRow1.Controls.AddRange(new Control[]
        {
            MakeFieldLabel("Banner font (18-140 px)"), _fontSize,
            MakeFieldLabel("Opacity (20-255)"), _bannerOpacity,
            _flashBanner,
            _showBannerHpBar,
            MakeFieldLabel("Flash (ms)"), _flashInterval
        });

        var bannerRow2 = CreateFlow();
        bannerRow2.Controls.AddRange(new Control[]
        {
            _soundOnAlert,
            _statusHudEnabled,
            MakeFieldLabel("Status font (9-48 px)"), _statusFontSize,
            _excludeCapture
        });

        var bannerRow3 = CreateFlow();
        bannerRow3.Controls.AddRange(new Control[]
        {
            _lowHpBgColorBtn,
            _lowHpBorderColorBtn,
            _criticalBgColorBtn,
            _criticalBorderColorBtn
        });

        var bannerRow4 = CreateFlow();
        bannerRow4.Controls.AddRange(new Control[]
        {
            _bannerTextColorBtn,
            _flashAltBgColorBtn,
            _resetBannerColorsBtn,
            MakeFieldLabel("UI Theme"), _themeCombo
        });

        bannerTable.Controls.Add(bannerRow0, 0, 0);
        bannerTable.Controls.Add(bannerRow1, 0, 1);
        bannerTable.Controls.Add(bannerRow2, 0, 2);
        bannerTable.Controls.Add(bannerRow3, 0, 3);
        bannerTable.Controls.Add(bannerRow4, 0, 4);
        bannerTable.Controls.Add(_lsCompatibilityNoteLabel, 0, 5);
        bannerGroup.Controls.Add(bannerTable);
        root.Controls.Add(bannerGroup, 0, 2);

        // Section 4: Auto-Targeting
        var targetGroup = CreateGroup("Automatic Player Targeting");
        var targetTable = CreateStackTable(3);
        foreach (var tm in Enum.GetValues<TargetMethod>())
        {
            _targetMethod.Items.Add(tm);
        }

        _targetMethod.SelectedItem = _settings.TargetingMethod;
        _autoTarget.Checked = _settings.AutoTargetOnAlert;
        _targetCooldown.Value = (decimal)Math.Clamp(_settings.AutoTargetCooldownSeconds, 0.5, 30.0);
        _restorePreviousTarget.Checked = _settings.RestorePreviousTargetAfterHeal;
        _restoreDelay.Value = (decimal)(double.IsFinite(_settings.RestoreTargetDelaySeconds)
            ? Math.Clamp(_settings.RestoreTargetDelaySeconds, 0.0, 5.0)
            : 0.2);
        _raidKeymapLabel.Text = _engine.GetRaidKeymapSummary();

        var targetRow0 = CreateFlow();
        targetRow0.Controls.AddRange(new Control[]
        {
            _autoTarget,
            MakeFieldLabel("Method"), _targetMethod,
            MakeFieldLabel("Cooldown (s)"), _targetCooldown
        });

        var targetRow1 = CreateFlow();
        targetRow1.Controls.AddRange(new Control[]
        {
            _restorePreviousTarget,
            MakeFieldLabel("Restore delay (s)"), _restoreDelay
        });

        targetTable.Controls.Add(targetRow0, 0, 0);
        targetTable.Controls.Add(targetRow1, 0, 1);
        targetTable.Controls.Add(_raidKeymapLabel, 0, 2);
        targetGroup.Controls.Add(targetTable);
        root.Controls.Add(targetGroup, 0, 3);

        // Section 5: Global Hotkeys
        var hotkeyGroup = CreateGroup("Hotkeys (click box and press desired key combo, Backspace clears, Esc cancels)");
        var hotkeyOuter = CreateStackTable(2);

        var hotkeyTable = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 7,
            Padding = new Padding(4)
        };
        hotkeyTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        hotkeyTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        hotkeyTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (int i = 0; i < 7; i++)
        {
            hotkeyTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        AddHotkeyRow(hotkeyTable, 0, "Toggle Plugin ON/OFF", _togglePluginHotkeyBox, () => _togglePluginHotkey, b => _togglePluginHotkey = b);
        AddHotkeyRow(hotkeyTable, 1, "Focus Current Target", _focusHotkeyBox, () => _focusHotkey, b => _focusHotkey = b);
        AddHotkeyRow(hotkeyTable, 2, "Cycle Mode", _toggleHotkeyBox, () => _toggleHotkey, b => _toggleHotkey = b);
        AddHotkeyRow(hotkeyTable, 3, "Target Critical Now", _criticalHotkeyBox, () => _criticalHotkey, b => _criticalHotkey = b);
        AddHotkeyRow(hotkeyTable, 4, "Reset People Saved", _resetSavedHotkeyBox, () => _resetSavedHotkey, b => _resetSavedHotkey = b);
        AddHotkeyRow(hotkeyTable, 5, "Copy Chat Scorecard", _copyScorecardHotkeyBox, () => _copyScorecardHotkey, b => _copyScorecardHotkey = b);
        AddHotkeyRow(hotkeyTable, 6, "Toggle Center Banner", _toggleBannerHotkeyBox, () => _toggleBannerHotkey, b => _toggleBannerHotkey = b);

        var hotkeyFooter = CreateFlow();
        var resetHotkeysBtn = new Button
        {
            Name = "ResetHotkeysButton",
            Text = "Reset Defaults",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(4, 4, 4, 2)
        };
        resetHotkeysBtn.Click += (_, _) => ResetHotkeyDefaults();
        hotkeyFooter.Controls.Add(resetHotkeysBtn);

        hotkeyOuter.Controls.Add(hotkeyTable, 0, 0);
        hotkeyOuter.Controls.Add(hotkeyFooter, 0, 1);
        hotkeyGroup.Controls.Add(hotkeyOuter);
        root.Controls.Add(hotkeyGroup, 0, 4);

        // Section 6: Status line
        _status.Text = "Changes take effect immediately after Save. Drag/resize HUDs via Move / Resize Banner or Move Status.";
        root.Controls.Add(_status, 0, 5);

        // Section 7: Footer buttons
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 6, 18, 0)
        };
        var save = new Button { Name = "SaveButton", Text = "Save", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        var cancel = new Button { Name = "CancelButton", Text = "Cancel", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, DialogResult = DialogResult.Cancel };
        save.Click += (_, _) => SaveAndClose();
        footer.Controls.AddRange(new Control[] { save, cancel });
        root.Controls.Add(footer, 0, 6);

        // Wire dirty tracking strictly AFTER initial value population so background hotkey toggles stay preserved unless edited here.
        _pluginEnabled.CheckedChanged += (_, _) => _pluginEnabledDirty = true;
        _hudEnabled.CheckedChanged += (_, _) => _hudEnabledDirty = true;
        _mode.SelectedIndexChanged += (_, _) => _modeDirty = true;

        AcceptButton = save;
        CancelButton = cancel;
        Controls.Add(root);
        _themeCombo.SelectedIndexChanged += (_, _) =>
        {
            UiTheme selected = (UiTheme)Math.Clamp(_themeCombo.SelectedIndex, 0, UiThemePalette.All.Length - 1);
            ApplyTheme(root, UiThemePalette.For(selected));
        };
        ApplyTheme(root, UiThemePalette.For(_settings.Theme));
    }

    private void ApplyTheme(Control root, UiThemePalette pal)
    {
        Color baseBg = Color.FromArgb(pal.SurfaceBaseArgb);
        Color cardBg = Color.FromArgb(pal.SurfaceContainerArgb);
        Color btnBg = Color.FromArgb(pal.SurfaceControlArgb);
        Color hoverBg = Color.FromArgb(pal.SurfaceHoverArgb);
        Color outline = Color.FromArgb(pal.OutlineArgb);
        Color primary = Color.FromArgb(pal.PrimaryArgb);
        Color textPrimary = Color.FromArgb(pal.TextPrimaryArgb);
        Color textMuted = Color.FromArgb(pal.TextMutedArgb);

        BackColor = baseBg;
        ForeColor = textPrimary;
        root.BackColor = baseBg;
        root.ForeColor = textPrimary;
        _status.ForeColor = primary;
        _raidKeymapLabel.ForeColor = primary;
        _lsCompatibilityNoteLabel.ForeColor = textMuted;

        void StyleChildren(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                switch (c)
                {
                    case GroupBox gb:
                        gb.BackColor = cardBg;
                        gb.ForeColor = primary;
                        break;
                    case CheckBox cb:
                        cb.ForeColor = textPrimary;
                        break;
                    case Label lbl when lbl != _status && lbl != _raidKeymapLabel && lbl != _lsCompatibilityNoteLabel:
                        lbl.ForeColor = textPrimary;
                        break;
                    case TextBox tb:
                        tb.BackColor = baseBg;
                        tb.ForeColor = textPrimary;
                        tb.BorderStyle = BorderStyle.FixedSingle;
                        break;
                    case NumericUpDown num:
                        num.BackColor = baseBg;
                        num.ForeColor = textPrimary;
                        break;
                    case ComboBox combo:
                        combo.BackColor = baseBg;
                        combo.ForeColor = textPrimary;
                        combo.FlatStyle = FlatStyle.Flat;
                        break;
                    case Button btn when !btn.Name.EndsWith("ColorButton", StringComparison.Ordinal):
                        btn.UseVisualStyleBackColor = false;
                        btn.FlatStyle = FlatStyle.Flat;
                        btn.BackColor = btnBg;
                        btn.ForeColor = textPrimary;
                        btn.FlatAppearance.BorderColor = outline;
                        btn.FlatAppearance.MouseOverBackColor = hoverBg;
                        break;
                }

                if (c.HasChildren && c is not NumericUpDown)
                {
                    StyleChildren(c);
                }
            }
        }

        StyleChildren(root);
    }

    internal void SetBannerColorHex(string buttonName, string hex)
    {
        switch (buttonName)
        {
            case "LowHpBgColorButton":
                _lowHpBgHex = Settings.NormalizeHexColor(hex, Settings.DefaultLowHpBgHex);
                break;
            case "LowHpBorderColorButton":
                _lowHpBorderHex = Settings.NormalizeHexColor(hex, Settings.DefaultLowHpBorderHex);
                break;
            case "CriticalBgColorButton":
                _criticalBgHex = Settings.NormalizeHexColor(hex, Settings.DefaultCriticalBgHex);
                break;
            case "CriticalBorderColorButton":
                _criticalBorderHex = Settings.NormalizeHexColor(hex, Settings.DefaultCriticalBorderHex);
                break;
            case "BannerTextColorButton":
                _bannerTextHex = Settings.NormalizeHexColor(hex, Settings.DefaultBannerTextHex);
                break;
            case "FlashAltBgColorButton":
                _flashAltBgHex = Settings.NormalizeHexColor(hex, Settings.DefaultFlashAltBgHex);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(buttonName), buttonName, "Unknown color swatch button");
        }

        RefreshColorSwatches();
    }

    private void WireColorSwatchButton(Button button, Func<string> getter, Action<string> setter)
    {
        button.Click += (_, _) =>
        {
            Settings.TryParseHexRgb(getter(), out int r, out int g, out int b);
            using var picker = new ColorDialog
            {
                FullOpen = true,
                Color = Color.FromArgb(r, g, b)
            };
            if (picker.ShowDialog(this) == DialogResult.OK)
            {
                setter(string.Create(CultureInfo.InvariantCulture, $"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}"));
                RefreshColorSwatches();
            }
        };
    }

    private void ResetBannerColorsToDefaults()
    {
        _lowHpBgHex = Settings.DefaultLowHpBgHex;
        _lowHpBorderHex = Settings.DefaultLowHpBorderHex;
        _criticalBgHex = Settings.DefaultCriticalBgHex;
        _criticalBorderHex = Settings.DefaultCriticalBorderHex;
        _bannerTextHex = Settings.DefaultBannerTextHex;
        _flashAltBgHex = Settings.DefaultFlashAltBgHex;
        RefreshColorSwatches();
        _status.Text = "Banner colors restored to defaults. Click Save to apply.";
    }

    private void RefreshColorSwatches()
    {
        ApplySwatchStyle(_lowHpBgColorBtn, "Low BG", _lowHpBgHex);
        ApplySwatchStyle(_lowHpBorderColorBtn, "Low Border", _lowHpBorderHex);
        ApplySwatchStyle(_criticalBgColorBtn, "Crit BG", _criticalBgHex);
        ApplySwatchStyle(_criticalBorderColorBtn, "Crit Border", _criticalBorderHex);
        ApplySwatchStyle(_bannerTextColorBtn, "Text", _bannerTextHex);
        ApplySwatchStyle(_flashAltBgColorBtn, "Flash Alt", _flashAltBgHex);
        _lowHpBgColorBtn.Parent?.PerformLayout();
        _bannerTextColorBtn.Parent?.PerformLayout();
    }

    private static void ApplySwatchStyle(Button btn, string label, string hex)
    {
        string clean = Settings.NormalizeHexColor(hex, "#FFFFFF");
        Settings.TryParseHexRgb(clean, out int r, out int g, out int b);
        double luminance = (0.299 * r) + (0.587 * g) + (0.114 * b);
        btn.UseVisualStyleBackColor = false;
        btn.FlatStyle = FlatStyle.Flat;
        btn.BackColor = Color.FromArgb(r, g, b);
        btn.ForeColor = luminance > 150.0 ? Color.FromArgb(20, 24, 30) : Color.White;
        btn.Text = $"{label}: {clean}";
    }

    private void ResetHotkeyDefaults()
    {
        _togglePluginHotkey = Settings.DefaultTogglePluginHotkey;
        _toggleBannerHotkey = HotkeyBinding.Empty;
        _focusHotkey = Settings.DefaultFocusTargetHotkey;
        _toggleHotkey = Settings.DefaultToggleModeHotkey;
        _criticalHotkey = Settings.DefaultTargetCriticalHotkey;
        _resetSavedHotkey = HotkeyBinding.Empty;
        _copyScorecardHotkey = HotkeyBinding.Empty;

        foreach (var entry in _hotkeyEntries)
        {
            entry.Box.Text = entry.Getter().Format();
        }

        _status.Text = "Hotkeys restored to defaults (Ctrl+Shift+K/H/J/T; optional hotkeys cleared). Click Save to apply.";
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (ActiveControl is TextBox activeBox)
        {
            HotkeyEntry? entry = null;
            foreach (var candidate in _hotkeyEntries)
            {
                if (ReferenceEquals(candidate.Box, activeBox))
                {
                    entry = candidate;
                    break;
                }
            }

            if (entry is not null)
            {
                Keys keyCode = keyData & Keys.KeyCode;
                Keys modifiers = keyData & Keys.Modifiers;

                if (keyCode == Keys.Tab && (modifiers & (Keys.Control | Keys.Alt)) == 0)
                {
                    return base.ProcessCmdKey(ref msg, keyData);
                }

                if (keyCode == Keys.Escape)
                {
                    entry.Box.Text = entry.Getter().Format();
                    SelectNextControl(entry.Box, true, true, true, true);
                    return true;
                }

                if (keyCode == Keys.Back && modifiers == Keys.None)
                {
                    entry.Setter(HotkeyBinding.Empty);
                    entry.Box.Text = HotkeyBinding.Empty.Format();
                    return true;
                }

                if (IsModifierOrIgnoredKey(keyCode) || (int)keyCode > 0xFE)
                {
                    return true;
                }

                var updated = new HotkeyBinding(
                    (int)keyCode,
                    (modifiers & Keys.Control) != 0,
                    (modifiers & Keys.Shift) != 0,
                    (modifiers & Keys.Alt) != 0);
                entry.Setter(updated);
                entry.Box.Text = updated.Format();
                return true;
            }
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void SaveAndClose()
    {
        try
        {
            if (!SettingsInput.TryFontSize(_fontSize.Text, out int fontPixels))
            {
                _status.Text = "Banner font size must be a whole number from 18 to 140 pixels. No changes saved.";
                _fontSize.Focus();
                return;
            }

            if (!SettingsInput.TryStatusFontSize(_statusFontSize.Text, out int statusFontPixels))
            {
                _status.Text = "Status font size must be a whole number from 9 to 48 pixels. No changes saved.";
                _statusFontSize.Focus();
                return;
            }

            // Preserve HUD positions, focused player, DH 5-only settings, live gamification stats, and untouched states.
            var live = _engine.GetSettings();

            var seenHotkeys = new Dictionary<HotkeyBinding, string>(8);
            if (!live.QuickCastCriticalHotkey.IsEmpty)
            {
                seenHotkeys[live.QuickCastCriticalHotkey] = "Quick-Cast Critical (DH 5)";
            }

            foreach (var entry in _hotkeyEntries)
            {
                var binding = entry.Getter();
                if (binding.IsEmpty) continue;
                if (seenHotkeys.TryGetValue(binding, out string? existingAction))
                {
                    _status.Text = $"Duplicate hotkey '{binding.Format()}' assigned to both '{existingAction}' and '{entry.ActionName}'. No changes saved.";
                    entry.Box.Focus();
                    return;
                }
                seenHotkeys[binding] = entry.ActionName;
            }

            _settings.HudX = live.HudX;
            _settings.HudY = live.HudY;
            _settings.StatusHudX = live.StatusHudX;
            _settings.StatusHudY = live.StatusHudY;
            _settings.RaidHudEnabled = live.RaidHudEnabled;
            _settings.RaidHudX = live.RaidHudX;
            _settings.RaidHudY = live.RaidHudY;
            _settings.QuickHealHotbar = live.QuickHealHotbar;
            _settings.QuickHealSlot = live.QuickHealSlot;
            _settings.QuickRaiseHotbar = live.QuickRaiseHotbar;
            _settings.QuickRaiseSlot = live.QuickRaiseSlot;
            _settings.QuickCastCriticalHotkey = live.QuickCastCriticalHotkey;
            _settings.FocusedPlayerId = live.FocusedPlayerId;
            _settings.FocusedPlayerName = live.FocusedPlayerName;
            _settings.CopyLiveStatsFrom(live);

            _settings.PluginEnabled = _pluginEnabledDirty ? _pluginEnabled.Checked : live.PluginEnabled;
            _settings.Mode = _modeDirty
                ? (_mode.SelectedItem is TrackingMode mode ? mode : TrackingMode.Party)
                : live.Mode;
            _settings.IncludeSelfInPartyMode = _includeSelf.Checked;
            _settings.IncludeTempHpInThreshold = _includeTempHp.Checked;
            _settings.SuppressAlertsInTown = _suppressInTown.Checked;
            _settings.HpThresholdPercent = (int)_threshold.Value;
            _settings.RecoveryHysteresisPercent = (int)_hysteresis.Value;
            _settings.DeadAlertSeconds = (int)_deadSeconds.Value;
            _settings.MaxAlertSeconds = (int)_maxAlertSeconds.Value;

            _settings.SaveWindowSeconds = (double)_saveWindowSeconds.Value;
            _settings.SelfCastWindowSeconds = (double)_selfCastWindowSeconds.Value;
            _settings.MinHealDelta = (int)_minHealDelta.Value;
            _settings.ShowSavedNotice = _showSavedNotice.Checked;
            _settings.ShowRankOnStatusHud = _showRankOnStatusHud.Checked;

            _settings.HudEnabled = _hudEnabledDirty ? _hudEnabled.Checked : live.HudEnabled;
            _settings.FontSizePixels = fontPixels;
            _settings.BannerOpacity = (int)_bannerOpacity.Value;
            _settings.FlashBanner = _flashBanner.Checked;
            _settings.ShowBannerHpBar = _showBannerHpBar.Checked;
            _settings.FlashIntervalMs = (int)_flashInterval.Value;
            _settings.StatusHudEnabled = _statusHudEnabled.Checked;
            _settings.StatusFontSizePixels = statusFontPixels;
            _settings.Theme = (UiTheme)Math.Clamp(_themeCombo.SelectedIndex, 0, UiThemePalette.All.Length - 1);
            _settings.SoundOnAlert = _soundOnAlert.Checked;
            _settings.ExcludeFromCapture = _excludeCapture.Checked;
            _settings.LowHpBgHex = _lowHpBgHex;
            _settings.LowHpBorderHex = _lowHpBorderHex;
            _settings.CriticalBgHex = _criticalBgHex;
            _settings.CriticalBorderHex = _criticalBorderHex;
            _settings.BannerTextHex = _bannerTextHex;
            _settings.FlashAltBgHex = _flashAltBgHex;

            _settings.AutoTargetOnAlert = _autoTarget.Checked;
            _settings.TargetingMethod = _targetMethod.SelectedItem is TargetMethod tm ? tm : TargetMethod.Auto;
            _settings.AutoTargetCooldownSeconds = (double)_targetCooldown.Value;
            _settings.RestorePreviousTargetAfterHeal = _restorePreviousTarget.Checked;
            _settings.RestoreTargetDelaySeconds = (double)_restoreDelay.Value;

            _settings.TogglePluginHotkey = _togglePluginHotkey;
            _settings.ToggleBannerHotkey = _toggleBannerHotkey;
            _settings.FocusTargetHotkey = _focusHotkey;
            _settings.ToggleModeHotkey = _toggleHotkey;
            _settings.TargetCriticalHotkey = _criticalHotkey;
            _settings.ResetSavedHotkey = _resetSavedHotkey;
            _settings.CopyScorecardHotkey = _copyScorecardHotkey;

            _engine.SaveSettings(_settings);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            _status.Text = "Could not save settings: " + ex.Message;
        }
    }

    private void AddHotkeyRow(
        TableLayoutPanel table,
        int row,
        string labelText,
        TextBox box,
        Func<HotkeyBinding> getter,
        Action<HotkeyBinding> setter)
    {
        _hotkeyEntries.Add(new HotkeyEntry(box, labelText, getter, setter));

        var label = MakeFieldLabel(labelText);
        box.Text = getter().Format();
        box.Margin = new Padding(4, 4, 6, 4);
        box.GotFocus += (_, _) => box.Text = "Press keys...";
        box.LostFocus += (_, _) => box.Text = getter().Format();
        box.KeyPress += (_, e) => e.Handled = true;

        var clear = new Button
        {
            Text = "Clear",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 2, 0, 2)
        };
        clear.Click += (_, _) =>
        {
            setter(HotkeyBinding.Empty);
            box.Text = HotkeyBinding.Empty.Format();
        };

        table.Controls.Add(label, 0, row);
        table.Controls.Add(box, 1, row);
        table.Controls.Add(clear, 2, row);
    }

    private static Button MakeAutoButton(string name, string text) => new()
    {
        Name = name,
        Text = text,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Margin = new Padding(3, 3, 3, 3)
    };

    private static bool IsModifierOrIgnoredKey(Keys key) => key is
        Keys.None or Keys.ProcessKey or Keys.Packet or Keys.Return or
        Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or
        Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or
        Keys.Menu or Keys.LMenu or Keys.RMenu or
        Keys.LWin or Keys.RWin;

    private static GroupBox CreateGroup(string title) => new()
    {
        Text = title,
        Dock = DockStyle.Top,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(8, 6, 8, 8),
        Margin = new Padding(0, 0, 18, 8)
    };

    private static TableLayoutPanel CreateStackTable(int rows)
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = rows
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < rows; i++)
        {
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
        return table;
    }

    private static FlowLayoutPanel CreateFlow() => new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        WrapContents = true
    };

    private static Label MakeFieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = TextPrimary,
        Padding = new Padding(4, 6, 4, 0)
    };

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _dialogFont.Dispose();
        }
    }
}
