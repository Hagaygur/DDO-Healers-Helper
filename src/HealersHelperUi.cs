using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using VoK.Sdk.Plugins;

namespace VoK.HealersHelper;

internal sealed class HealersHelperUi : IPluginUI, IDisposable
{
    private readonly HealersHelperPlugin _plugin;
    private HealerControlForm? _form;
    private Bitmap? _icon;

    public HealersHelperUi(HealersHelperPlugin plugin) => _plugin = plugin;

    public float? FocusedOpacity => 1f;
    public bool EnabledInCharacterSelection => true;
    public Tuple<int, int> MinSize => Tuple.Create(_form?.MinimumSize.Width ?? 380, _form?.MinimumSize.Height ?? 300);
    public object UserInterfaceForm => _form is null || _form.IsDisposed ? _form = new HealerControlForm(_plugin) : _form;

    public Image ToolbarImage
    {
        get
        {
            if (_icon is not null)
            {
                return _icon;
            }

            _icon = new Bitmap(36, 36);
            using var g = Graphics.FromImage(_icon);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var bg = new SolidBrush(Color.FromArgb(30, 36, 48));
            using var border = new Pen(Color.FromArgb(220, 70, 82), 2.5f);
            using var cross = new SolidBrush(Color.FromArgb(95, 224, 145));
            g.FillEllipse(bg, 3, 3, 30, 30);
            g.DrawEllipse(border, 3, 3, 30, 30);
            g.FillRectangle(cross, 15, 9, 6, 18);
            g.FillRectangle(cross, 9, 15, 18, 6);
            return _icon;
        }
    }

    public void Dispose()
    {
        var form = _form;
        _form = null;
        if (form is not null && !form.IsDisposed)
        {
            if (form.IsHandleCreated && form.InvokeRequired)
            {
                try { form.BeginInvoke(new Action(form.Dispose)); } catch (InvalidOperationException) { }
            }
            else
            {
                form.Dispose();
            }
        }
    }
}

internal sealed class HealerControlForm : Form
{
    private sealed record PlayerChoice(ulong Id, string Name)
    {
        public override string ToString() => string.IsNullOrWhiteSpace(Name) ? $"ID {Id}" : Name;
    }

    private readonly HealersHelperPlugin _plugin;
    private readonly Label _pluginStateLabel = new() { Name = "PluginStateLabel", Text = "Plugin: ON", AutoSize = true, Margin = new Padding(2, 2, 14, 2), Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), ForeColor = Color.FromArgb(122, 229, 130) };
    private readonly Label _peopleSavedLabel = new() { Name = "PeopleSavedLabel", Text = "People Saved: 0", AutoSize = true, Margin = new Padding(2, 2, 2, 2), Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), ForeColor = Color.FromArgb(122, 215, 255) };
    private readonly Label _statusLabel = new() { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(2, 4, 2, 2) };
    private readonly Label _targetingLabel = new() { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(2, 0, 2, 4), ForeColor = Color.FromArgb(180, 210, 235) };
    private readonly ComboBox _modeCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 115 };
    private readonly ComboBox _playerCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 175 };
    private readonly ListBox _membersList = new() { Dock = DockStyle.Top, Height = 96, IntegralHeight = false, BackColor = Color.FromArgb(18, 22, 29), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 250 };

    private bool _updatingSelectors;
    private string _lastMemberSignature = "";
    private string _lastPlayerSignature = "";

    public HealerControlForm(HealersHelperPlugin plugin)
    {
        _plugin = plugin;
        Text = "DDO Healer's Helper controls";
        FormBorderStyle = FormBorderStyle.None;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(440, 360);
        MinimumSize = new Size(380, 300);
        AutoScroll = true;
        BackColor = Color.FromArgb(24, 28, 36);
        ForeColor = Color.White;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(6)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 6; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        // Row 0: Prominent live Plugin ON/OFF and People Saved header
        var summaryRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 4)
        };
        summaryRow.Controls.Add(_pluginStateLabel);
        summaryRow.Controls.Add(_peopleSavedLabel);
        layout.Controls.Add(summaryRow, 0, 0);

        // Row 1: Primary action buttons in wrapping FlowLayoutPanel
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 4)
        };

        var toggleBtn = new Button { Name = "TogglePluginButton", Text = "Toggle ON/OFF", AutoSize = true };
        var cycleBtn = new Button { Text = "Cycle Mode", AutoSize = true };
        var focusBtn = new Button { Text = "Focus Target", AutoSize = true };
        var targetCritBtn = new Button { Text = "Target Critical", AutoSize = true };
        var previewBtn = new Button { Text = "Preview Banner", AutoSize = true };
        var moveBannerBtn = new Button { Text = "Move Banner", AutoSize = true };
        var moveStatusBtn = new Button { Name = "MoveStatusButton", Text = "Move Status", AutoSize = true };
        var resetSavedBtn = new Button { Name = "ResetSavedButton", Text = "Reset Saved", AutoSize = true };
        var settingsBtn = new Button { Text = "Settings...", AutoSize = true };

        toggleBtn.Click += (_, _) =>
        {
            _plugin.Engine?.TogglePluginEnabled();
            RefreshState();
        };
        cycleBtn.Click += (_, _) =>
        {
            _plugin.Engine?.CycleMode();
            RefreshState();
        };
        focusBtn.Click += (_, _) =>
        {
            _plugin.Engine?.FocusCurrentTarget();
            RefreshState();
        };
        targetCritBtn.Click += (_, _) =>
        {
            _plugin.Engine?.TargetCriticalNow();
            RefreshState();
        };
        previewBtn.Click += (_, _) => _plugin.Hud?.Preview();
        moveBannerBtn.Click += (_, _) => _plugin.Hud?.Move();
        moveStatusBtn.Click += (_, _) => _plugin.Hud?.MoveStatus();
        resetSavedBtn.Click += (_, _) =>
        {
            _plugin.Engine?.ResetPeopleSaved();
            RefreshState();
        };
        settingsBtn.Click += (_, _) =>
        {
            if (_plugin.Engine is not { } engine)
            {
                return;
            }

            using var dialog = new SettingsDialog(engine);
            dialog.ShowDialog(this);
            RefreshState();
        };

        buttons.Controls.AddRange(new Control[]
        {
            toggleBtn, cycleBtn, focusBtn, targetCritBtn, previewBtn,
            moveBannerBtn, moveStatusBtn, resetSavedBtn, settingsBtn
        });
        layout.Controls.Add(buttons, 0, 1);

        // Row 2: Quick mode / player selector
        var selectors = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 4)
        };
        foreach (var mode in Enum.GetValues<TrackingMode>())
        {
            _modeCombo.Items.Add(mode);
        }

        _modeCombo.SelectedIndexChanged += (_, _) => OnModeChanged();
        _playerCombo.SelectedIndexChanged += (_, _) => OnPlayerSelected();

        selectors.Controls.Add(new Label { Text = "Mode:", AutoSize = true, Margin = new Padding(2, 6, 4, 2) });
        selectors.Controls.Add(_modeCombo);
        selectors.Controls.Add(new Label { Text = "Focus:", AutoSize = true, Margin = new Padding(8, 6, 4, 2) });
        selectors.Controls.Add(_playerCombo);
        layout.Controls.Add(selectors, 0, 2);

        // Row 3 & 4: Live Status & Targeting Status labels
        layout.Controls.Add(_statusLabel, 0, 3);
        layout.Controls.Add(_targetingLabel, 0, 4);

        // Row 5: Live watched members summary / HP list
        layout.Controls.Add(_membersList, 0, 5);

        Controls.Add(layout);

        _refresh.Tick += (_, _) => RefreshState();
        VisibleChanged += (_, _) =>
        {
            _refresh.Enabled = Visible;
            if (Visible)
            {
                FitToolbarWidth();
                RefreshState();
            }
        };

        RefreshState();
    }

    private void OnModeChanged()
    {
        if (_updatingSelectors || _plugin.Engine is not { } engine || _modeCombo.SelectedItem is not TrackingMode mode)
        {
            return;
        }

        var s = engine.GetSettings();
        if (s.Mode == mode)
        {
            return;
        }

        s.Mode = mode;
        engine.SaveSettings(s);
        RefreshState();
    }

    private void OnPlayerSelected()
    {
        if (_updatingSelectors || _plugin.Engine is not { } engine || _playerCombo.SelectedItem is not PlayerChoice choice)
        {
            return;
        }

        var s = engine.GetSettings();
        if (s.FocusedPlayerId == choice.Id && string.Equals(s.FocusedPlayerName, choice.Name, StringComparison.Ordinal))
        {
            return;
        }

        s.FocusedPlayerId = choice.Id;
        s.FocusedPlayerName = choice.Name;
        s.Mode = TrackingMode.SinglePlayer;
        engine.SaveSettings(s);
        RefreshState();
    }

    private void FitToolbarWidth()
    {
        foreach (Form host in Application.OpenForms)
        {
            if (host == this || host.IsDisposed || host.InvokeRequired)
            {
                continue;
            }

            var icon = FindNamedControl(host, _plugin.Name);
            if (icon?.Parent is not { } strip || !strip.IsHandleCreated)
            {
                continue;
            }

            int right = host.PointToClient(strip.PointToScreen(new Point(strip.Width, 0))).X;
            int allowance = (int)Math.Ceiling(36 * host.DeviceDpi / 96.0);
            int required = Math.Max(MinimumSize.Width, right + allowance);
            MinimumSize = new Size(required, MinimumSize.Height);
            Width = Math.Max(Width, required);
            break;
        }
    }

    private static Control? FindNamedControl(Control root, string name)
    {
        foreach (Control child in root.Controls)
        {
            if (child.Name == name)
            {
                return child;
            }

            if (FindNamedControl(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void RefreshState()
    {
        var engine = _plugin.Engine;
        if (engine is null)
        {
            _pluginStateLabel.Text = "Plugin: ON";
            _pluginStateLabel.ForeColor = Color.FromArgb(122, 229, 130);
            _peopleSavedLabel.Text = "People Saved: 0";
            _statusLabel.Text = "Waiting for game connection…";
            _targetingLabel.Text = "Targeting: idle";
            return;
        }

        var settings = engine.GetSettings();
        var view = engine.View();
        var members = engine.GetMembers();
        string hudStatus = _plugin.Hud?.Status ?? "HUD starting";

        _pluginStateLabel.Text = settings.PluginEnabled ? "Plugin: ON" : "Plugin: OFF";
        _pluginStateLabel.ForeColor = settings.PluginEnabled
            ? Color.FromArgb(122, 229, 130)
            : Color.FromArgb(255, 130, 130);
        _peopleSavedLabel.Text = string.Create(CultureInfo.InvariantCulture, $"People Saved: {view.PeopleSaved}");

        _statusLabel.Text = string.IsNullOrWhiteSpace(view.StatusLine)
            ? hudStatus
            : $"{view.StatusLine} • {hudStatus}";
        _targetingLabel.Text = string.IsNullOrWhiteSpace(view.TargetingStatus)
            ? $"Targeting: {settings.TargetingMethod}"
            : view.TargetingStatus;

        UpdateSelectors(settings, members);
        UpdateMembersList(members);
    }

    private void UpdateSelectors(Settings settings, WatchedMemberSnapshot[] members)
    {
        _updatingSelectors = true;
        try
        {
            if (_modeCombo.SelectedItem is not TrackingMode currentMode || currentMode != settings.Mode)
            {
                _modeCombo.SelectedItem = settings.Mode;
            }

            var choices = new List<PlayerChoice>(members.Length + 1);
            var sigParts = new List<string>(members.Length + 1);
            bool hasFocused = false;
            foreach (var m in members)
            {
                choices.Add(new PlayerChoice(m.Id, m.Name));
                sigParts.Add($"{m.Id}:{m.Name}");
                if ((settings.FocusedPlayerId != 0 && m.Id == settings.FocusedPlayerId) ||
                    (!string.IsNullOrWhiteSpace(settings.FocusedPlayerName) &&
                     string.Equals(m.Name, settings.FocusedPlayerName, StringComparison.OrdinalIgnoreCase)))
                {
                    hasFocused = true;
                }
            }

            if (!hasFocused && (settings.FocusedPlayerId != 0 || !string.IsNullOrWhiteSpace(settings.FocusedPlayerName)))
            {
                choices.Add(new PlayerChoice(settings.FocusedPlayerId, settings.FocusedPlayerName));
                sigParts.Add($"{settings.FocusedPlayerId}:{settings.FocusedPlayerName}");
            }

            string playerSig = string.Join("|", sigParts);
            if (!string.Equals(playerSig, _lastPlayerSignature, StringComparison.Ordinal))
            {
                _playerCombo.BeginUpdate();
                _playerCombo.Items.Clear();
                foreach (var c in choices)
                {
                    _playerCombo.Items.Add(c);
                }

                _playerCombo.EndUpdate();
                _lastPlayerSignature = playerSig;
            }

            for (int i = 0; i < _playerCombo.Items.Count; i++)
            {
                if (_playerCombo.Items[i] is PlayerChoice c &&
                    ((settings.FocusedPlayerId != 0 && c.Id == settings.FocusedPlayerId) ||
                     (!string.IsNullOrWhiteSpace(settings.FocusedPlayerName) &&
                      string.Equals(c.Name, settings.FocusedPlayerName, StringComparison.OrdinalIgnoreCase))))
                {
                    if (_playerCombo.SelectedIndex != i)
                    {
                        _playerCombo.SelectedIndex = i;
                    }

                    break;
                }
            }
        }
        finally
        {
            _updatingSelectors = false;
        }
    }

    private void UpdateMembersList(WatchedMemberSnapshot[] members)
    {
        if (members.Length == 0)
        {
            if (!string.Equals(_lastMemberSignature, "(none)", StringComparison.Ordinal))
            {
                _membersList.BeginUpdate();
                _membersList.Items.Clear();
                _membersList.Items.Add("No party or character data yet.");
                _membersList.EndUpdate();
                _lastMemberSignature = "(none)";
            }

            return;
        }

        var lines = new string[members.Length];
        for (int i = 0; i < members.Length; i++)
        {
            var m = members[i];
            string slot = m.PartySlotIndex == 0 ? "Self" : $"F{m.PartySlotIndex}";
            string state = m.IsDeadOrDown
                ? "DOWN"
                : !m.Connected
                    ? "OFFLINE"
                    : $"{m.Percent.ToString("0", CultureInfo.InvariantCulture)}%";
            lines[i] = $"[{slot}] {m.Name}: {m.HitPoints}/{m.MaxHitPoints} ({state})";
        }

        string sig = string.Join("\n", lines);
        if (string.Equals(sig, _lastMemberSignature, StringComparison.Ordinal))
        {
            return;
        }

        _membersList.BeginUpdate();
        _membersList.Items.Clear();
        foreach (var line in lines)
        {
            _membersList.Items.Add(line);
        }

        _membersList.EndUpdate();
        _lastMemberSignature = sig;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refresh.Stop();
            _refresh.Dispose();
        }

        base.Dispose(disposing);
    }
}
