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
    private readonly List<HotkeyEntry> _hotkeyEntries = new(4);

    private readonly CheckBox _pluginEnabled = new() { Name = "PluginEnabledCheckBox", Text = "Plugin enabled (master switch)", AutoSize = true };
    private readonly ComboBox _mode = new() { Name = "ModeCombo", DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly NumericUpDown _threshold = new() { Name = "HpThresholdNumeric", Minimum = 5, Maximum = 95, Width = 65 };
    private readonly NumericUpDown _hysteresis = new() { Name = "RecoveryHysteresisNumeric", Minimum = 0, Maximum = 20, Width = 65 };
    private readonly NumericUpDown _deadSeconds = new() { Name = "DeadAlertSecondsNumeric", Minimum = 0, Maximum = 60, Width = 65 };
    private readonly TextBox _fontSize = new() { Name = "FontSizeTextBox", Width = 60, MaxLength = 3 };
    private readonly NumericUpDown _flashInterval = new() { Name = "FlashIntervalNumeric", Minimum = 100, Maximum = 1000, Increment = 25, Width = 70 };
    private readonly CheckBox _hudEnabled = new() { Name = "HudEnabledCheckBox", Text = "HUD banner enabled", AutoSize = true };
    private readonly CheckBox _statusHudEnabled = new() { Name = "StatusHudEnabledCheckBox", Text = "Status HUD enabled", AutoSize = true };
    private readonly TextBox _statusFontSize = new() { Name = "StatusFontSizeTextBox", Width = 55, MaxLength = 3 };
    private readonly CheckBox _soundOnAlert = new() { Name = "SoundOnAlertCheckBox", Text = "Sound on alert", AutoSize = true };
    private readonly CheckBox _excludeCapture = new() { Name = "ExcludeFromCaptureCheckBox", Text = "Exclude from capture (Lossless Scaling)", AutoSize = true };

    private readonly CheckBox _autoTarget = new() { Name = "AutoTargetCheckBox", Text = "Auto-target critical player on alert", AutoSize = true };
    private readonly ComboBox _targetMethod = new() { Name = "TargetMethodCombo", DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly NumericUpDown _targetCooldown = new() { Name = "AutoTargetCooldownNumeric", Minimum = 0.5m, Maximum = 30.0m, DecimalPlaces = 1, Increment = 0.5m, Width = 65 };

    private readonly TextBox _togglePluginHotkeyBox = new() { Name = "TogglePluginHotkeyBox", ReadOnly = true, Width = 145 };
    private readonly TextBox _focusHotkeyBox = new() { Name = "FocusTargetHotkeyBox", ReadOnly = true, Width = 145 };
    private readonly TextBox _toggleHotkeyBox = new() { Name = "ToggleModeHotkeyBox", ReadOnly = true, Width = 145 };
    private readonly TextBox _criticalHotkeyBox = new() { Name = "TargetCriticalHotkeyBox", ReadOnly = true, Width = 145 };

    private readonly Label _status = new() { Name = "StatusLabel", AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(2, 6, 2, 6), ForeColor = Color.FromArgb(40, 75, 130) };

    private HotkeyBinding _togglePluginHotkey;
    private HotkeyBinding _focusHotkey;
    private HotkeyBinding _toggleHotkey;
    private HotkeyBinding _criticalHotkey;

    public SettingsDialog(IHealerEngineView engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _settings = engine.GetSettings().Copy();
        _togglePluginHotkey = _settings.TogglePluginHotkey;
        _focusHotkey = _settings.FocusTargetHotkey;
        _toggleHotkey = _settings.ToggleModeHotkey;
        _criticalHotkey = _settings.TargetCriticalHotkey;

        Text = "DDO Healer's Helper — Settings";
        Font = _dialogFont;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(720, 620);
        MinimumSize = new Size(640, 560);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(12)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 6; i++)
        {
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        // Section 1: Alert Thresholds & Mode
        var trackingGroup = CreateGroup("HP Alert & Tracking");
        var trackingTable = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2
        };
        trackingTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        trackingTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _pluginEnabled.Checked = _settings.PluginEnabled;
        foreach (var m in Enum.GetValues<TrackingMode>())
        {
            _mode.Items.Add(m);
        }

        _mode.SelectedItem = _settings.Mode;
        _threshold.Value = Math.Clamp(_settings.HpThresholdPercent, 5, 95);
        _hysteresis.Value = Math.Clamp(_settings.RecoveryHysteresisPercent, 0, 20);
        _deadSeconds.Value = Math.Clamp(_settings.DeadAlertSeconds, 0, 60);

        var trackingRow0 = CreateFlow();
        trackingRow0.Controls.AddRange(new Control[] { _pluginEnabled, MakeFieldLabel("Mode"), _mode });

        var trackingRow1 = CreateFlow();
        trackingRow1.Controls.AddRange(new Control[]
        {
            MakeFieldLabel("Alert HP %"), _threshold,
            MakeFieldLabel("Hysteresis %"), _hysteresis,
            MakeFieldLabel("Dead alert (s)"), _deadSeconds
        });

        trackingTable.Controls.Add(trackingRow0, 0, 0);
        trackingTable.Controls.Add(trackingRow1, 0, 1);
        trackingGroup.Controls.Add(trackingTable);
        root.Controls.Add(trackingGroup, 0, 0);

        // Section 2: Banner Appearance, Status HUD & Lossless Scaling
        var bannerGroup = CreateGroup("Center Banner, Status HUD & Lossless Scaling");
        var bannerTable = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3
        };
        bannerTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bannerTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bannerTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _fontSize.Text = _settings.FontSizePixels.ToString(CultureInfo.InvariantCulture);
        _flashInterval.Value = Math.Clamp(_settings.FlashIntervalMs, 100, 1000);
        _hudEnabled.Checked = _settings.HudEnabled;
        _statusHudEnabled.Checked = _settings.StatusHudEnabled;
        _statusFontSize.Text = _settings.StatusFontSizePixels.ToString(CultureInfo.InvariantCulture);
        _soundOnAlert.Checked = _settings.SoundOnAlert;
        _excludeCapture.Checked = _settings.ExcludeFromCapture;

        var bannerRow1 = CreateFlow();
        bannerRow1.Controls.AddRange(new Control[]
        {
            MakeFieldLabel("Banner font (18-140 px)"), _fontSize,
            MakeFieldLabel("Flash interval (ms)"), _flashInterval,
            _hudEnabled
        });

        var bannerRow2 = CreateFlow();
        bannerRow2.Controls.AddRange(new Control[]
        {
            _statusHudEnabled,
            MakeFieldLabel("Status font (9-48 px)"), _statusFontSize,
            _soundOnAlert
        });

        var bannerRow3 = CreateFlow();
        bannerRow3.Controls.AddRange(new Control[] { _excludeCapture });

        bannerTable.Controls.Add(bannerRow1, 0, 0);
        bannerTable.Controls.Add(bannerRow2, 0, 1);
        bannerTable.Controls.Add(bannerRow3, 0, 2);
        bannerGroup.Controls.Add(bannerTable);
        root.Controls.Add(bannerGroup, 0, 1);

        // Section 3: Auto-Targeting
        var targetGroup = CreateGroup("Automatic Player Targeting");
        var targetFlow = CreateFlow();
        foreach (var tm in Enum.GetValues<TargetMethod>())
        {
            _targetMethod.Items.Add(tm);
        }

        _targetMethod.SelectedItem = _settings.TargetingMethod;
        _autoTarget.Checked = _settings.AutoTargetOnAlert;
        _targetCooldown.Value = (decimal)Math.Clamp(_settings.AutoTargetCooldownSeconds, 0.5, 30.0);

        targetFlow.Controls.AddRange(new Control[]
        {
            _autoTarget,
            MakeFieldLabel("Method"), _targetMethod,
            MakeFieldLabel("Cooldown (s)"), _targetCooldown
        });
        targetGroup.Controls.Add(targetFlow);
        root.Controls.Add(targetGroup, 0, 2);

        // Section 4: Global Hotkeys
        var hotkeyGroup = CreateGroup("Hotkeys (click box and press desired key combo, Backspace clears, Esc cancels)");
        var hotkeyTable = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 4,
            Padding = new Padding(4)
        };
        hotkeyTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        hotkeyTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        hotkeyTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (int i = 0; i < 4; i++)
        {
            hotkeyTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        AddHotkeyRow(hotkeyTable, 0, "Toggle Plugin ON/OFF", _togglePluginHotkeyBox, () => _togglePluginHotkey, b => _togglePluginHotkey = b);
        AddHotkeyRow(hotkeyTable, 1, "Focus Current Target", _focusHotkeyBox, () => _focusHotkey, b => _focusHotkey = b);
        AddHotkeyRow(hotkeyTable, 2, "Cycle Mode", _toggleHotkeyBox, () => _toggleHotkey, b => _toggleHotkey = b);
        AddHotkeyRow(hotkeyTable, 3, "Target Critical Now", _criticalHotkeyBox, () => _criticalHotkey, b => _criticalHotkey = b);

        hotkeyGroup.Controls.Add(hotkeyTable);
        root.Controls.Add(hotkeyGroup, 0, 3);

        // Section 5: Status line
        _status.Text = "Changes take effect immediately after Save. Drag HUDs via Move Banner / Move Status on the plugin panel.";
        root.Controls.Add(_status, 0, 4);

        // Section 6: Footer buttons
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 6, 0, 0)
        };
        var save = new Button { Name = "SaveButton", Text = "Save", AutoSize = true };
        var cancel = new Button { Name = "CancelButton", Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        save.Click += (_, _) => SaveAndClose();
        footer.Controls.AddRange(new Control[] { save, cancel });
        root.Controls.Add(footer, 0, 5);

        AcceptButton = save;
        CancelButton = cancel;
        Controls.Add(root);
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

            var seenHotkeys = new Dictionary<HotkeyBinding, string>(4);
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

            // Preserve HUD positions, focused player, and live PeopleSaved counter.
            var live = _engine.GetSettings();
            _settings.HudX = live.HudX;
            _settings.HudY = live.HudY;
            _settings.StatusHudX = live.StatusHudX;
            _settings.StatusHudY = live.StatusHudY;
            _settings.FocusedPlayerId = live.FocusedPlayerId;
            _settings.FocusedPlayerName = live.FocusedPlayerName;
            _settings.PeopleSaved = live.PeopleSaved;

            _settings.PluginEnabled = _pluginEnabled.Checked;
            _settings.Mode = _mode.SelectedItem is TrackingMode mode ? mode : TrackingMode.Party;
            _settings.HpThresholdPercent = (int)_threshold.Value;
            _settings.RecoveryHysteresisPercent = (int)_hysteresis.Value;
            _settings.DeadAlertSeconds = (int)_deadSeconds.Value;
            _settings.FontSizePixels = fontPixels;
            _settings.FlashIntervalMs = (int)_flashInterval.Value;
            _settings.HudEnabled = _hudEnabled.Checked;
            _settings.StatusHudEnabled = _statusHudEnabled.Checked;
            _settings.StatusFontSizePixels = statusFontPixels;
            _settings.SoundOnAlert = _soundOnAlert.Checked;
            _settings.ExcludeFromCapture = _excludeCapture.Checked;
            _settings.AutoTargetOnAlert = _autoTarget.Checked;
            _settings.TargetingMethod = _targetMethod.SelectedItem is TargetMethod tm ? tm : TargetMethod.Auto;
            _settings.AutoTargetCooldownSeconds = (double)_targetCooldown.Value;
            _settings.TogglePluginHotkey = _togglePluginHotkey;
            _settings.FocusTargetHotkey = _focusHotkey;
            _settings.ToggleModeHotkey = _toggleHotkey;
            _settings.TargetCriticalHotkey = _criticalHotkey;

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

        var clear = new Button { Text = "Clear", AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
        clear.Click += (_, _) =>
        {
            setter(HotkeyBinding.Empty);
            box.Text = HotkeyBinding.Empty.Format();
        };

        table.Controls.Add(label, 0, row);
        table.Controls.Add(box, 1, row);
        table.Controls.Add(clear, 2, row);
    }

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
        Margin = new Padding(0, 0, 0, 8)
    };

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
