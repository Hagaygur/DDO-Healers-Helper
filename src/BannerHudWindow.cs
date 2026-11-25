using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VoK.HealersHelper;

internal sealed class BannerHudWindow : Form
{
    private const double PreviewDurationSeconds = 8.0;
    private const double EditDurationSeconds = 20.0;
    private static readonly double TimestampFrequency = Stopwatch.Frequency;

    private readonly IHealerEngineView _engine;
    private readonly Action<string> _status;
    private readonly HudSurface _surface = new();
    private readonly FontFamily _family = new("Segoe UI");

    private Settings _settings;
    private int _revision = -1;
    private bool _appliedExcludeFromCapture;
    private double _previewStart = double.NegativeInfinity;
    private double _editUntil = double.NegativeInfinity;
    private double _alertStart;
    private double _nextRaise;
    private double _nextScreenCheck;
    private double _lastMove;
    private double _lastErrorLog = -100;
    private long _alertGeneration = -1;
    private BannerSeverity _alertSeverity = BannerSeverity.None;
    private bool _editing;
    private bool _movingProgrammatically;
    private bool _positionDirty;
    private bool _needsPosition = true;
    private string _captureStatus = "Capture exclusion not checked";
    private string _lastRenderKey = "";
    private Rectangle _screen;
    private IntPtr _lastGameWindow;
    private IntPtr _foreground;
    private bool _foregroundAllowed;
    private uint _gamePid;

    internal HudSurface Surface => _surface;
    internal BannerFrame LastFrame { get; private set; }
    internal bool AllowClose { get; set; }

    public BannerHudWindow(IHealerEngineView engine, ConcurrentQueue<HudCommand> _ignoredCommands, Action<string> status)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _status = status ?? throw new ArgumentNullException(nameof(status));
        _settings = _engine.GetSettings();
        _appliedExcludeFromCapture = _settings.ExcludeFromCapture;

        Text = "DDO Healer's Helper HUD";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.Manual;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);

        try
        {
            RefreshGameWindowBinding();
            _screen = HudHostHelper.GetTargetScreenBounds(_lastGameWindow);
            _ = Handle; // Top-level HWND outside Dungeon Helper's panel host.
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var p = base.CreateParams;
            p.ExStyle |= NativeHud.Layered | NativeHud.NoActivate | NativeHud.ToolWindow;
            if (!_editing)
            {
                p.ExStyle |= NativeHud.Transparent;
            }
            return p;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _lastRenderKey = "";
        _needsPosition = true;
        HudHostHelper.SetClickThrough(Handle, !_editing);
        ApplyCaptureExclusion(_settings.ExcludeFromCapture);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnPaint(PaintEventArgs e) { }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeHud.WmNcHitTest)
        {
            m.Result = _editing ? new IntPtr(NativeHud.HtCaption) : new IntPtr(NativeHud.HtTransparent);
            return;
        }

        if (m.Msg == NativeHud.WmMouseActivate)
        {
            m.Result = new IntPtr(NativeHud.MaNoActivate);
            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        if (_editing && !_movingProgrammatically)
        {
            _positionDirty = true;
            _lastMove = NowSeconds();
        }
    }

    internal void BeginPreview() => _previewStart = NowSeconds();
    internal void BeginMove() => _editUntil = NowSeconds() + EditDurationSeconds;

    internal void FlushPositionIfDirty()
    {
        if (!_positionDirty) return;
        double x = Math.Clamp((Left - _screen.Left) / (double)Math.Max(1, _screen.Width - Width), 0.0, 1.0);
        double y = Math.Clamp((Top - _screen.Top) / (double)Math.Max(1, _screen.Height - Height), 0.0, 1.0);
        _positionDirty = false;
        _settings.HudX = x;
        _settings.HudY = y;
        _engine.SavePosition(x, y);
    }

    internal void RenderForSmoke(BannerFrame frame, bool forceReposition = false)
    {
        RefreshGameWindowBinding();
        _screen = HudHostHelper.GetTargetScreenBounds(_lastGameWindow);
        RenderFrame(frame, forceReposition || _needsPosition);
    }

    private void RefreshGameWindowBinding()
    {
        IntPtr gw = _engine.GameWindow;
        if (gw != _lastGameWindow)
        {
            _lastGameWindow = gw;
            _foreground = IntPtr.Zero;
            if (gw != IntPtr.Zero)
                NativeHud.GetWindowThreadProcessId(gw, out _gamePid);
            else
                _gamePid = 0;
        }
    }

    private void ApplyCaptureExclusion(bool exclude)
    {
        _appliedExcludeFromCapture = exclude;
        _captureStatus = HudHostHelper.ApplyCaptureExclusion(Handle, exclude);
        _status(_captureStatus);
        _engine.Report(_captureStatus);
    }

    internal void TickFrame(double now)
    {
        if (IsDisposed || Disposing) return;

        try
        {
            RefreshGameWindowBinding();
            bool editing = now < _editUntil;
            if (_positionDirty && (!editing || now - _lastMove > 0.5))
            {
                FlushPositionIfDirty();
            }

            if (editing != _editing)
            {
                _editing = editing;
                _lastRenderKey = "";
                if (IsHandleCreated)
                {
                    HudHostHelper.SetClickThrough(Handle, !editing);
                }
            }

            int revision = _engine.SettingsRevision;
            if (revision != _revision)
            {
                _settings = _engine.GetSettings();
                _revision = revision;
                _lastRenderKey = "";
                if (!_editing && !_positionDirty)
                {
                    _needsPosition = true;
                }
                if (IsHandleCreated && _settings.ExcludeFromCapture != _appliedExcludeFromCapture)
                {
                    ApplyCaptureExclusion(_settings.ExcludeFromCapture);
                }
            }

            if (now >= _nextScreenCheck)
            {
                var screen = HudHostHelper.GetTargetScreenBounds(_lastGameWindow);
                if (screen != _screen)
                {
                    _screen = screen;
                    if (!_editing && !_positionDirty)
                    {
                        _needsPosition = true;
                    }
                }

                _nextScreenCheck = now + 1.0;
            }

            bool demo = now - _previewStart < PreviewDurationSeconds;
            var live = _engine.View();

            if (live.Severity is BannerSeverity.LowHp or BannerSeverity.CriticalDown)
            {
                if (_alertGeneration != live.AlertGeneration || _alertSeverity != live.Severity)
                {
                    _alertStart = now;
                    _alertGeneration = live.AlertGeneration;
                    _alertSeverity = live.Severity;
                }
            }
            else
            {
                _alertSeverity = live.Severity;
                _alertGeneration = live.AlertGeneration;
            }

            bool foreground = HudHostHelper.ForegroundAllowed(_lastGameWindow, _gamePid, ref _foreground, ref _foregroundAllowed);
            double elapsedAlert = demo ? Math.Max(0.0, now - _previewStart) : Math.Max(0.0, now - _alertStart);
            double editRemaining = editing ? Math.Max(0.0, _editUntil - now) : 0.0;
            var frame = BannerPresentation.Compute(live, _settings, demo, editing, elapsedAlert, editRemaining);

            bool modeAllowsLive = _settings.PluginEnabled && _settings.HudEnabled && live.Mode != TrackingMode.Off;
            bool display = frame.Visible && (demo || editing || (modeAllowsLive && foreground));

            _status(_captureStatus + (editing ? " • drag banner now" : display ? " • HUD visible" : " • HUD hidden"));

            if (!display)
            {
                if (Visible)
                {
                    Hide();
                }

                return;
            }

            string renderKey = $"{frame.PrimaryText}|{frame.SecondaryText}|{frame.FontSizePixels}|{frame.FlashOn}|{frame.TextColorArgb}|{frame.BackgroundColorArgb}|{frame.BorderColorArgb}";
            if (renderKey != _lastRenderKey || _needsPosition)
            {
                RenderFrame(frame, _needsPosition);
                _lastRenderKey = renderKey;
            }

            if (!Visible)
            {
                Show();
                _nextRaise = 0;
            }

            if (now >= _nextRaise && IsHandleCreated)
            {
                NativeHud.SetWindowPos(Handle, NativeHud.HwndTopMost, 0, 0, 0, 0, NativeHud.SwpTopMostFlags);
                _nextRaise = now + 0.5;
            }
        }
        catch (Exception e)
        {
            _status("HUD error: " + e.Message);
            if (now - _lastErrorLog > 15.0)
            {
                _engine.Report("HUD rendering error: " + e);
                _lastErrorLog = now;
            }
        }
    }

    private void RenderFrame(BannerFrame frame, bool reposition)
    {
        LastFrame = frame;

        if (!frame.Visible)
        {
            _surface.Resize(4, 4);
            var clearGraphics = _surface.Graphics!;
            clearGraphics.CompositingMode = CompositingMode.SourceCopy;
            clearGraphics.Clear(Color.Transparent);
            clearGraphics.Flush();
            if (IsHandleCreated)
            {
                _surface.Present(Handle, Location);
            }

            _needsPosition = false;
            return;
        }

        string primaryText = string.IsNullOrWhiteSpace(frame.PrimaryText) ? " " : frame.PrimaryText;
        string secondaryText = frame.SecondaryText ?? "";
        int primaryPixels = Math.Clamp(frame.FontSizePixels, BannerPresentation.MinFontSizePixels, BannerPresentation.MaxFontSizePixels);
        int secondaryPixels = Math.Clamp((int)Math.Round(primaryPixels * 0.42), 14, 44);
        bool hasSecondary = !string.IsNullOrWhiteSpace(secondaryText);

        using var primaryPath = new GraphicsPath();
        primaryPath.AddString(primaryText, _family, (int)FontStyle.Bold, primaryPixels, PointF.Empty, StringFormat.GenericTypographic);
        var primaryBounds = primaryPath.GetBounds();
        if (primaryBounds.Width <= 0 || primaryBounds.Height <= 0)
        {
            primaryBounds = new RectangleF(0, 0, primaryPixels, primaryPixels);
        }

        GraphicsPath? secondaryPath = null;
        try
        {
            RectangleF secondaryBounds = RectangleF.Empty;
            if (hasSecondary)
            {
                secondaryPath = new GraphicsPath();
                secondaryPath.AddString(secondaryText, _family, (int)FontStyle.Bold, secondaryPixels, PointF.Empty, StringFormat.GenericTypographic);
                secondaryBounds = secondaryPath.GetBounds();
            }

            const int padX = 28;
            const int padY = 16;
            const int lineGap = 8;
            const int borderThickness = 4;

            float contentWidth = Math.Max(primaryBounds.Width, hasSecondary ? secondaryBounds.Width : 0f);
            int minBannerWidth = primaryPixels >= 36 ? 420 : 280;
            int width = Math.Max(minBannerWidth, (int)Math.Ceiling(contentWidth) + (padX * 2));
            int height = (int)Math.Ceiling(primaryBounds.Height) + (padY * 2) + (hasSecondary ? (int)Math.Ceiling(secondaryBounds.Height) + lineGap : 0);

            bool sizeChanged = _surface.Size.Width != width || _surface.Size.Height != height;
            _surface.Resize(width, height);

            float primaryX = ((width - primaryBounds.Width) * 0.5f) - primaryBounds.X;
            float primaryY = padY - primaryBounds.Y;
            using (var shift = new Matrix())
            {
                shift.Translate(primaryX, primaryY);
                primaryPath.Transform(shift);
            }

            if (secondaryPath is not null)
            {
                float secondaryX = ((width - secondaryBounds.Width) * 0.5f) - secondaryBounds.X;
                float secondaryY = padY + primaryBounds.Height + lineGap - secondaryBounds.Y;
                using var shift = new Matrix();
                shift.Translate(secondaryX, secondaryY);
                secondaryPath.Transform(shift);
            }

            _movingProgrammatically = true;
            try
            {
                ClientSize = _surface.Size;
                if (reposition || (sizeChanged && !_editing))
                {
                    var pos = BannerPresentation.ComputePosition(
                        _screen.Left,
                        _screen.Top,
                        _screen.Width,
                        _screen.Height,
                        Size.Width,
                        Size.Height,
                        _settings.HudX,
                        _settings.HudY);
                    Location = new Point(pos.X, pos.Y);
                }
            }
            finally
            {
                _movingProgrammatically = false;
            }

            _needsPosition = false;

            var g = _surface.Graphics!;
            g.CompositingMode = CompositingMode.SourceCopy;
            g.Clear(Color.Transparent);
            g.CompositingMode = CompositingMode.SourceOver;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var bgBrush = new SolidBrush(Color.FromArgb(frame.BackgroundColorArgb)))
            {
                g.FillRectangle(bgBrush, 0, 0, width, height);
            }

            using (var borderPen = new Pen(Color.FromArgb(frame.BorderColorArgb), borderThickness))
            {
                float half = borderThickness * 0.5f;
                g.DrawRectangle(borderPen, half, half, width - borderThickness, height - borderThickness);
            }

            using (var outline = new Pen(Color.FromArgb(225, 0, 0, 0), 3.0f) { LineJoin = LineJoin.Round })
            using (var fill = new SolidBrush(Color.FromArgb(frame.TextColorArgb)))
            {
                g.DrawPath(outline, primaryPath);
                g.FillPath(fill, primaryPath);
                if (secondaryPath is not null)
                {
                    using var subOutline = new Pen(Color.FromArgb(200, 0, 0, 0), 2.0f) { LineJoin = LineJoin.Round };
                    g.DrawPath(subOutline, secondaryPath);
                    g.FillPath(fill, secondaryPath);
                }
            }

            g.Flush();
            if (IsHandleCreated)
            {
                _surface.Present(Handle, Location);
            }
        }
        finally
        {
            secondaryPath?.Dispose();
        }
    }

    private static double NowSeconds() => Stopwatch.GetTimestamp() / TimestampFrequency;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _surface.Dispose();
            _family.Dispose();
        }

        base.Dispose(disposing);
    }
}
