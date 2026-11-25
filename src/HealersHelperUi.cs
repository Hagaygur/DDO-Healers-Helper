using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    public Tuple<int, int> MinSize => Tuple.Create(_form?.MinimumSize.Width ?? 380, _form?.MinimumSize.Height ?? 320);
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
                try
                {
                    form.BeginInvoke(new Action(form.Dispose));
                }
                catch (Exception)
                {
                    if (!form.IsDisposed && !form.IsHandleCreated)
                    {
                        form.Dispose();
                    }
                }
            }
            else
            {
                form.Dispose();
            }
        }
        _icon?.Dispose();
        _icon = null;
    }
}

internal sealed class HealerControlForm : Form
{
    internal const string CompatibilityNoteText =
        "Tested & working on live DDO + Lossless Scaling (Windows 11); not guaranteed on all configurations. " +
        "Should also work on Linux via Wine/Proton (cross-compiled & CI-tested on Linux, live gameplay untested).";

    private sealed record PlayerChoice(ulong Id, string Name)
    {
        public override string ToString() => string.IsNullOrWhiteSpace(Name) ? $"ID {Id}" : Name;
    }

    private sealed class OfflineSettingsEngineView(string settingsFolder) : IHealerEngineView
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private Settings? _cached;
        private DateTime _cachedWriteUtc = DateTime.MinValue;
        private long _cachedLength = -1;
        private int _revision = 1;

        public string SettingsFolder { get; } = settingsFolder;
        private string ConfigPath => Path.Combine(SettingsFolder, "healers-helper.json");

        public IntPtr GameWindow => IntPtr.Zero;

        public int SettingsRevision
        {
            get
            {
                _ = EnsureLoaded();
                return _revision;
            }
        }

        public int PeopleSaved => EnsureLoaded().PeopleSaved;

        public Settings GetSettings() => EnsureLoaded().Copy();

        public void SaveSettings(Settings settings)
        {
            var copy = settings.Copy();
            var live = EnsureLoaded();
            copy.CopyLiveStatsFrom(live);
            Persist(copy);
        }

        public void SavePosition(double x, double y)
        {
            var copy = EnsureLoaded().Copy();
            copy.HudX = x;
            copy.HudY = y;
            Persist(copy);
        }

        public void SaveBannerLayout(double x, double y, int fontSizePixels, int bannerOpacity)
        {
            var copy = EnsureLoaded().Copy();
            copy.HudX = x;
            copy.HudY = y;
            copy.FontSizePixels = fontSizePixels;
            copy.BannerOpacity = bannerOpacity;
            Persist(copy);
        }

        public void SaveStatusPosition(double x, double y)
        {
            var copy = EnsureLoaded().Copy();
            copy.StatusHudX = x;
            copy.StatusHudY = y;
            Persist(copy);
        }

        public AlertBannerView View()
        {
            var s = EnsureLoaded();
            return new AlertBannerView(
                BannerSeverity.None,
                s.Mode,
                "",
                "",
                0,
                null,
                "",
                null,
                0,
                "Waiting for game connection…",
                "Targeting: idle",
                s.PluginEnabled,
                s.PeopleSaved);
        }

        public WatchedMemberSnapshot[] GetMembers() => Array.Empty<WatchedMemberSnapshot>();

        public void TogglePluginEnabled()
        {
            var copy = EnsureLoaded().Copy();
            copy.PluginEnabled = !copy.PluginEnabled;
            Persist(copy);
        }

        public void ToggleBannerHud()
        {
            var copy = EnsureLoaded().Copy();
            copy.HudEnabled = !copy.HudEnabled;
            Persist(copy);
        }

        public void ResetPeopleSaved()
        {
            var copy = EnsureLoaded().Copy();
            copy.ResetLiveStats();
            Persist(copy);
        }

        public string GetChatScorecard() => HealerStats.FormatChatScorecard(EnsureLoaded());

        public bool CopyChatScorecard() => HealerEngine.TryCopyTextToClipboardSta(GetChatScorecard());

        public bool FocusCurrentTarget() => false;

        public void CycleMode()
        {
            var copy = EnsureLoaded().Copy();
            copy.Mode = copy.Mode == TrackingMode.Party ? TrackingMode.SinglePlayer : TrackingMode.Party;
            Persist(copy);
        }

        public bool TargetCriticalNow() => false;

        public void Report(string message) { }

        private Settings EnsureLoaded()
        {
            try
            {
                var info = new FileInfo(ConfigPath);
                if (!info.Exists)
                {
                    if (_cached is null || _cachedLength != -1)
                    {
                        _cached = new Settings();
                        _cachedWriteUtc = DateTime.MinValue;
                        _cachedLength = -1;
                        _revision++;
                    }
                    return _cached;
                }

                if (_cached is not null
                    && info.LastWriteTimeUtc == _cachedWriteUtc
                    && info.Length == _cachedLength)
                {
                    return _cached;
                }

                var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(ConfigPath), Json) ?? new Settings();
                loaded.Validate();
                _cached = loaded;
                _cachedWriteUtc = info.LastWriteTimeUtc;
                _cachedLength = info.Length;
                _revision++;
                return _cached;
            }
            catch
            {
                return _cached ??= new Settings();
            }
        }

        private void Persist(Settings updated)
        {
            updated.Validate();
            _cached = updated.Copy();
            _revision++;
            try
            {
                var store = new SettingsStore(SettingsFolder);
                store.Save(updated);
                var info = new FileInfo(store.ConfigPath);
                if (info.Exists)
                {
                    _cachedWriteUtc = info.LastWriteTimeUtc;
                    _cachedLength = info.Length;
                }
            }
            catch { }
        }
    }

    private readonly HealersHelperPlugin _plugin;
    private readonly Font _headerFont = new("Segoe UI", 9.5f, FontStyle.Bold);
    private readonly ToolTip _toolTip = new() { ShowAlways = true };
    private readonly Label _pluginStateLabel;
    private readonly Label _bannerStateLabel;
    private readonly Label _peopleSavedLabel;
    private readonly Label _gamificationLabel = new() { Name = "GamificationSummaryLabel", AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(2, 0, 2, 4), ForeColor = Color.FromArgb(215, 225, 240) };
    private readonly Label _statusLabel = new() { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(2, 2, 2, 2) };
    private readonly Label _targetingLabel = new() { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(2, 0, 2, 4), ForeColor = Color.FromArgb(180, 210, 235) };
    private readonly Label _compatibilityNoteLabel = new()
    {
        Name = "CompatibilityNoteLabel",
        Text = CompatibilityNoteText,
        AutoSize = true,
        MaximumSize = new Size(348, 0),
        Margin = new Padding(2, 4, 2, 2),
        ForeColor = Color.FromArgb(165, 180, 200)
    };
    private readonly ComboBox _modeCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 115 };
    private readonly ComboBox _playerCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 175 };
    private readonly ListBox _membersList = new() { Dock = DockStyle.Top, Height = 96, IntegralHeight = false, BackColor = Color.FromArgb(18, 22, 29), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 250 };

    private readonly Button _toggleBtn;
    private readonly Button _toggleBannerBtn;
    private readonly Button _cycleBtn;
    private readonly Button _focusBtn;
    private readonly Button _targetCritBtn;
    private readonly Button _moveBannerBtn;
    private readonly Button _resetSavedBtn;
    private readonly Button _copyScorecardBtn;

    private OfflineSettingsEngineView? _offlineView;
    private bool _updatingSelectors;
    private int _lastTooltipRevision = -2;
    private bool _lastTooltipLiveEngine;
    private string _lastMemberSignature = "";
    private string _lastPlayerSignature = "";

    internal ToolTip ButtonToolTip => _toolTip;

    private IHealerEngineView ActiveView
    {
        get
        {
            if (_plugin.Engine is { } live)
                return live;

            string folder = _plugin.SettingsFolder;
            if (_offlineView is null || !string.Equals(_offlineView.SettingsFolder, folder, StringComparison.Ordinal))
            {
                _offlineView = new OfflineSettingsEngineView(folder);
            }

            return _offlineView;
        }
    }

    public HealerControlForm(HealersHelperPlugin plugin)
    {
        _plugin = plugin;
        _pluginStateLabel = new Label
        {
            Name = "PluginStateLabel",
            Text = "Plugin: ON",
            AutoSize = true,
            Margin = new Padding(2, 2, 12, 2),
            Font = _headerFont,
            ForeColor = Color.FromArgb(122, 229, 130)
        };
        _bannerStateLabel = new Label
        {
            Name = "BannerStateLabel",
            Text = "Banner: ON",
            AutoSize = true,
            Margin = new Padding(2, 2, 12, 2),
            Font = _headerFont,
            ForeColor = Color.FromArgb(122, 229, 130)
        };
        _peopleSavedLabel = new Label
        {
            Name = "PeopleSavedLabel",
            Text = "People Saved: 0 (Initiate)",
            AutoSize = true,
            Margin = new Padding(2, 2, 2, 2),
            Font = _headerFont,
            ForeColor = Color.FromArgb(122, 215, 255)
        };

        Text = "DDO Healer's Helper controls";
        FormBorderStyle = FormBorderStyle.None;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(440, 410);
        MinimumSize = new Size(380, 320);
        AutoScroll = true;
        BackColor = Color.FromArgb(24, 28, 36);
        ForeColor = Color.White;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 8,
            Padding = new Padding(6)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 8; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        // Row 0: Prominent live Plugin ON/OFF, Banner ON/OFF, and People Saved + Prestige Rank header
        var summaryRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 2)
        };
        summaryRow.Controls.Add(_pluginStateLabel);
        summaryRow.Controls.Add(_bannerStateLabel);
        summaryRow.Controls.Add(_peopleSavedLabel);
        layout.Controls.Add(summaryRow, 0, 0);

        // Row 1: Compact gamification stats summary
        layout.Controls.Add(_gamificationLabel, 0, 1);

        // Row 2: Primary action buttons in wrapping FlowLayoutPanel
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 4)
        };

        _toggleBtn = MakeActionButton("TogglePluginButton", "Toggle ON/OFF");
        _toggleBannerBtn = MakeActionButton("ToggleBannerButton", "Banner: ON");
        _cycleBtn = MakeActionButton("CycleModeButton", "Cycle Mode");
        _focusBtn = MakeActionButton("FocusTargetButton", "Focus Target");
        _targetCritBtn = MakeActionButton("TargetCriticalButton", "Target Critical");
        var previewBtn = MakeActionButton("PreviewBannerButton", "Preview Banner");
        _moveBannerBtn = MakeActionButton("MoveBannerButton", "Move / Resize Banner");
        var moveStatusBtn = MakeActionButton("MoveStatusButton", "Move Status");
        _copyScorecardBtn = MakeActionButton("CopyScorecardButton", "Copy Chat Card");
        _resetSavedBtn = MakeActionButton("ResetSavedButton", "Reset Saved");
        var settingsBtn = MakeActionButton("SettingsButton", "Settings...");

        _toggleBtn.Click += (_, _) =>
        {
            ActiveView.TogglePluginEnabled();
            RefreshState();
        };
        _toggleBannerBtn.Click += (_, _) =>
        {
            ActiveView.ToggleBannerHud();
            RefreshState();
        };
        _cycleBtn.Click += (_, _) =>
        {
            ActiveView.CycleMode();
            RefreshState();
        };
        _focusBtn.Click += (_, _) =>
        {
            _plugin.Engine?.FocusCurrentTarget();
            RefreshState();
        };
        _targetCritBtn.Click += (_, _) =>
        {
            _plugin.Engine?.TargetCriticalNow();
            RefreshState();
        };
        previewBtn.Click += (_, _) => _plugin.Hud?.Preview();
        _moveBannerBtn.Click += (_, _) => _plugin.Hud?.Move();
        moveStatusBtn.Click += (_, _) => _plugin.Hud?.MoveStatus();
        _copyScorecardBtn.Click += (_, _) =>
        {
            ActiveView.CopyChatScorecard();
            RefreshState();
        };
        _resetSavedBtn.Click += (_, _) =>
        {
            ActiveView.ResetPeopleSaved();
            RefreshState();
        };
        settingsBtn.Click += (_, _) =>
        {
            using var dialog = new SettingsDialog(ActiveView);
            dialog.ShowDialog(this);
            RefreshState();
        };

        buttons.Controls.AddRange(new Control[]
        {
            _toggleBtn, _toggleBannerBtn, _cycleBtn, _focusBtn, _targetCritBtn, previewBtn,
            _moveBannerBtn, moveStatusBtn, _copyScorecardBtn, _resetSavedBtn, settingsBtn
        });
        layout.Controls.Add(buttons, 0, 2);

        // Row 3: Quick mode / player selector
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
        layout.Controls.Add(selectors, 0, 3);

        // Row 4 & 5: Live Status & Targeting Status labels
        layout.Controls.Add(_statusLabel, 0, 4);
        layout.Controls.Add(_targetingLabel, 0, 5);

        // Row 6: Live watched members summary / HP list
        layout.Controls.Add(_membersList, 0, 6);

        // Row 7: Platform & Lossless Scaling compatibility note
        layout.Controls.Add(_compatibilityNoteLabel, 0, 7);

        Controls.Add(layout);

        Resize += (_, _) =>
        {
            int maxNoteWidth = Math.Max(340, ClientSize.Width - 32);
            if (_compatibilityNoteLabel.MaximumSize.Width != maxNoteWidth)
            {
                _compatibilityNoteLabel.MaximumSize = new Size(maxNoteWidth, 0);
            }
        };

        ApplyTooltips(new Settings(), -1, false);

        _refresh.Tick += (_, _) => RefreshState();
        VisibleChanged += (_, _) =>
        {
            if (IsDisposed || Disposing) return;
            _refresh.Enabled = Visible;
            if (Visible)
            {
                FitToolbarWidth();
                RefreshState();
            }
        };

        RefreshState();
    }

    private static Button MakeActionButton(string name, string text) => new()
    {
        Name = name,
        Text = text,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink
    };

    private void ApplyTooltips(Settings settings, int revision, bool isLiveEngine)
    {
        if (revision == _lastTooltipRevision && isLiveEngine == _lastTooltipLiveEngine) return;
        _lastTooltipRevision = revision;
        _lastTooltipLiveEngine = isLiveEngine;

        static string WithChord(string description, HotkeyBinding binding) =>
            binding.IsEmpty ? $"{description} (Hotkey: None)" : $"{description} ({binding.Format()})";

        _toolTip.SetToolTip(_toggleBtn, WithChord("Master ON/OFF switch", settings.TogglePluginHotkey));
        _toolTip.SetToolTip(_toggleBannerBtn, WithChord("Enable or disable center alert banner (targeting & stats stay active)", settings.ToggleBannerHotkey));
        _toolTip.SetToolTip(_cycleBtn, WithChord("Toggle Party / Single-Player mode", settings.ToggleModeHotkey));
        _toolTip.SetToolTip(_focusBtn, WithChord("Focus currently selected DDO player", settings.FocusTargetHotkey));
        _toolTip.SetToolTip(_targetCritBtn, WithChord("Select lowest/critical watched player now", settings.TargetCriticalHotkey));
        _toolTip.SetToolTip(_moveBannerBtn, "Unlock banner for 20s: drag center to move, drag edges/corners or wheel to resize, Ctrl+wheel for opacity");
        _toolTip.SetToolTip(_resetSavedBtn, WithChord("Reset People Saved and all healer records", settings.ResetSavedHotkey));
        _toolTip.SetToolTip(_copyScorecardBtn, WithChord("Copy ASCII healer scorecard for DDO chat", settings.CopyScorecardHotkey));
    }

    private void OnModeChanged()
    {
        if (_updatingSelectors || _modeCombo.SelectedItem is not TrackingMode mode)
        {
            return;
        }

        var engine = ActiveView;
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
        if (_updatingSelectors || _playerCombo.SelectedItem is not PlayerChoice choice)
        {
            return;
        }

        var engine = ActiveView;
        var s = engine.GetSettings();
        if (s.Mode == TrackingMode.SinglePlayer
            && s.FocusedPlayerId == choice.Id
            && string.Equals(s.FocusedPlayerName, choice.Name, StringComparison.Ordinal))
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

    internal void RefreshState()
    {
        if (IsDisposed || Disposing) return;

        var liveEngine = _plugin.Engine;
        var viewEngine = ActiveView;
        int rev = viewEngine.SettingsRevision;
        var settings = viewEngine.GetSettings();
        ApplyTooltips(settings, rev, liveEngine is not null);

        var view = viewEngine.View();
        var members = viewEngine.GetMembers();

        int saved = Math.Max(0, view.PeopleSaved);
        string rank = HealerStats.RankForSaves(saved);
        _pluginStateLabel.Text = settings.PluginEnabled ? "Plugin: ON" : "Plugin: OFF";
        _pluginStateLabel.ForeColor = settings.PluginEnabled
            ? Color.FromArgb(122, 229, 130)
            : Color.FromArgb(255, 130, 130);

        _bannerStateLabel.Text = settings.HudEnabled ? "Banner: ON" : "Banner: OFF";
        _bannerStateLabel.ForeColor = settings.HudEnabled
            ? Color.FromArgb(122, 229, 130)
            : Color.FromArgb(240, 190, 95);

        string bannerBtnText = settings.HudEnabled ? "Banner: ON" : "Banner: OFF";
        if (!string.Equals(_toggleBannerBtn.Text, bannerBtnText, StringComparison.Ordinal))
        {
            _toggleBannerBtn.Text = bannerBtnText;
            _toggleBannerBtn.Parent?.PerformLayout();
        }

        _peopleSavedLabel.Text = string.Create(CultureInfo.InvariantCulture, $"People Saved: {saved} ({rank})");
        _gamificationLabel.Text = FormatGamificationSummary(settings);

        if (liveEngine is null)
        {
            _statusLabel.Text = "Waiting for game connection…";
            _targetingLabel.Text = "Targeting: idle";
        }
        else
        {
            string hudStatus = _plugin.Hud?.Status ?? "HUD starting";
            _statusLabel.Text = string.IsNullOrWhiteSpace(view.StatusLine)
                ? hudStatus
                : $"{view.StatusLine} • {hudStatus}";
            _targetingLabel.Text = string.IsNullOrWhiteSpace(view.TargetingStatus)
                ? $"Targeting: {settings.TargetingMethod}"
                : view.TargetingStatus;
        }

        UpdateSelectors(settings, members);
        UpdateMembersList(members);
    }

    private static string FormatGamificationSummary(Settings s)
    {
        var parts = new List<string>(4)
        {
            string.Create(CultureInfo.InvariantCulture, $"Streak: {s.CurrentSaveStreak} (Best {s.BestSaveStreak})"),
            string.Create(CultureInfo.InvariantCulture, $"1% Club: {s.OnePercentClubSaves}")
        };

        if (s.ClosestCallPreHealHp > 0 && !string.IsNullOrWhiteSpace(s.ClosestCallPlayerName))
        {
            parts.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"Closest: {s.ClosestCallPlayerName} {s.ClosestCallPreHealPercent:0.#}% ({s.ClosestCallPreHealHp} HP)"));
        }

        parts.Add(s.ReflexSampleCount > 0 && s.BestReflexMs > 0
            ? string.Create(CultureInfo.InvariantCulture, $"Reflex: {s.AvgReflexMs}ms avg ({s.BestReflexMs}ms best)")
            : "Reflex: —");

        return string.Join(" • ", parts);
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

            int matchedIndex = -1;
            if (settings.Mode == TrackingMode.SinglePlayer)
            {
                for (int i = 0; i < _playerCombo.Items.Count; i++)
                {
                    if (_playerCombo.Items[i] is PlayerChoice c &&
                        ((settings.FocusedPlayerId != 0 && c.Id == settings.FocusedPlayerId) ||
                         (!string.IsNullOrWhiteSpace(settings.FocusedPlayerName) &&
                          string.Equals(c.Name, settings.FocusedPlayerName, StringComparison.OrdinalIgnoreCase))))
                    {
                        matchedIndex = i;
                        break;
                    }
                }
            }

            if (_playerCombo.SelectedIndex != matchedIndex)
            {
                _playerCombo.SelectedIndex = matchedIndex;
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
            _toolTip.Dispose();
        }

        base.Dispose(disposing);
        if (disposing)
        {
            _headerFont.Dispose();
        }
    }
}
