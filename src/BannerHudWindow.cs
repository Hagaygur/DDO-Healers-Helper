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
    private bool _layoutDirty;
    private bool _needsPosition = true;
    private BannerEditDragMode _dragMode;
    private Point _dragStartScreenPt;
    private Rectangle _dragStartBounds;
    private int _dragStartFontPixels;
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

    internal static BannerEditDragMode ClassifyEditHit(Size size, Point clientPt, int cornerGrip = 18, int edgeGrip = 14) =>
        BannerPresentation.ClassifyEditHit(size.Width, size.Height, clientPt.X, clientPt.Y, cornerGrip, edgeGrip);

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
            m.Result = _editing ? new IntPtr(NativeHud.HtClient) : new IntPtr(NativeHud.HtTransparent);
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
            _layoutDirty = true;
            _lastMove = NowSeconds();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!_editing || e.Button != MouseButtons.Left)
            return;

        var mode = ClassifyEditHit(ClientSize, e.Location);
        if (mode == BannerEditDragMode.None)
            return;

        _dragMode = mode;
        _dragStartScreenPt = PointToScreen(e.Location);
        _dragStartBounds = Bounds;
        _dragStartFontPixels = Math.Clamp(
            _settings.FontSizePixels,
            BannerPresentation.MinFontSizePixels,
            BannerPresentation.MaxFontSizePixels);
        Capture = true;
        Cursor = CursorForHit(mode);
        ExtendEditWindow(NowSeconds());
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_editing)
        {
            if (Cursor != Cursors.Default) Cursor = Cursors.Default;
            return;
        }

        if (_dragMode == BannerEditDragMode.None)
        {
            Cursor = CursorForHit(ClassifyEditHit(ClientSize, e.Location));
            return;
        }

        ApplyDragDelta(PointToScreen(e.Location));
        ExtendEditWindow(NowSeconds());
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragMode != BannerEditDragMode.None && e.Button == MouseButtons.Left)
        {
            _dragMode = BannerEditDragMode.None;
            Capture = false;
            Cursor = _editing ? CursorForHit(ClassifyEditHit(ClientSize, e.Location)) : Cursors.Default;
            FlushLayoutIfDirty();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_dragMode == BannerEditDragMode.None && Cursor != Cursors.Default)
        {
            Cursor = Cursors.Default;
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!_editing || e.Delta == 0)
            return;

        ApplyEditWheel(e.Delta, (ModifierKeys & Keys.Control) != 0);
    }

    private void ExtendEditWindow(double now)
    {
        _editUntil = Math.Max(_editUntil, now + 5.0);
        _lastMove = now;
    }

    private static Cursor CursorForHit(BannerEditDragMode mode) => mode switch
    {
        BannerEditDragMode.ResizeTopLeft or BannerEditDragMode.ResizeBottomRight => Cursors.SizeNWSE,
        BannerEditDragMode.ResizeTopRight or BannerEditDragMode.ResizeBottomLeft => Cursors.SizeNESW,
        BannerEditDragMode.ResizeLeft or BannerEditDragMode.ResizeRight => Cursors.SizeWE,
        BannerEditDragMode.ResizeTop or BannerEditDragMode.ResizeBottom => Cursors.SizeNS,
        BannerEditDragMode.Move => Cursors.SizeAll,
        _ => Cursors.Default
    };

    private void ApplyDragDelta(Point currentScreenPt)
    {
        int dx = currentScreenPt.X - _dragStartScreenPt.X;
        int dy = currentScreenPt.Y - _dragStartScreenPt.Y;

        if (_dragMode == BannerEditDragMode.Move)
        {
            int maxLeft = Math.Max(_screen.Left, _screen.Right - Width);
            int maxTop = Math.Max(_screen.Top, _screen.Bottom - Height);
            int nextX = Math.Clamp(_dragStartBounds.Left + dx, _screen.Left, maxLeft);
            int nextY = Math.Clamp(_dragStartBounds.Top + dy, _screen.Top, maxTop);

            _movingProgrammatically = true;
            try
            {
                Location = new Point(nextX, nextY);
            }
            finally
            {
                _movingProgrammatically = false;
            }

            UpdateNormalizedPositionFromBounds();
            if (IsHandleCreated)
            {
                _surface.Present(Handle, Location);
            }

            _positionDirty = true;
            _layoutDirty = true;
            return;
        }

        int w0 = Math.Max(1, _dragStartBounds.Width);
        int h0 = Math.Max(1, _dragStartBounds.Height);
        bool dragsLeft = _dragMode is BannerEditDragMode.ResizeLeft or BannerEditDragMode.ResizeTopLeft or BannerEditDragMode.ResizeBottomLeft;
        bool dragsRight = _dragMode is BannerEditDragMode.ResizeRight or BannerEditDragMode.ResizeTopRight or BannerEditDragMode.ResizeBottomRight;
        bool dragsTop = _dragMode is BannerEditDragMode.ResizeTop or BannerEditDragMode.ResizeTopLeft or BannerEditDragMode.ResizeTopRight;
        bool dragsBottom = _dragMode is BannerEditDragMode.ResizeBottom or BannerEditDragMode.ResizeBottomLeft or BannerEditDragMode.ResizeBottomRight;

        double ratioW = dragsLeft ? (w0 - dx) / (double)w0 : dragsRight ? (w0 + dx) / (double)w0 : 1.0;
        double ratioH = dragsTop ? (h0 - dy) / (double)h0 : dragsBottom ? (h0 + dy) / (double)h0 : 1.0;
        double scaleRatio = (dragsLeft || dragsRight) && (dragsTop || dragsBottom)
            ? (ratioW + ratioH) * 0.5
            : (dragsLeft || dragsRight)
                ? ratioW
                : ratioH;

        int nextFont = Math.Clamp(
            (int)Math.Round(_dragStartFontPixels * Math.Max(0.1, scaleRatio)),
            BannerPresentation.MinFontSizePixels,
            BannerPresentation.MaxFontSizePixels);

        _settings.FontSizePixels = nextFont;
        ReRenderCurrentEditFrame(reposition: false);

        int anchorX = dragsLeft
            ? _dragStartBounds.Right - Width
            : dragsRight
                ? _dragStartBounds.Left
                : _dragStartBounds.Left + ((_dragStartBounds.Width - Width) / 2);

        int anchorY = dragsTop
            ? _dragStartBounds.Bottom - Height
            : dragsBottom
                ? _dragStartBounds.Top
                : _dragStartBounds.Top + ((_dragStartBounds.Height - Height) / 2);

        int clampedX = Math.Clamp(anchorX, _screen.Left, Math.Max(_screen.Left, _screen.Right - Width));
        int clampedY = Math.Clamp(anchorY, _screen.Top, Math.Max(_screen.Top, _screen.Bottom - Height));

        _movingProgrammatically = true;
        try
        {
            Location = new Point(clampedX, clampedY);
        }
        finally
        {
            _movingProgrammatically = false;
        }

        UpdateNormalizedPositionFromBounds();
        if (IsHandleCreated)
        {
            _surface.Present(Handle, Location);
        }

        _positionDirty = true;
        _layoutDirty = true;
    }

    private void ApplyEditWheel(int wheelDelta, bool ctrlDown)
    {
        int notches = wheelDelta / 120;
        if (notches == 0) notches = Math.Sign(wheelDelta);

        double now = NowSeconds();
        if (ctrlDown)
        {
            _settings.BannerOpacity = Math.Clamp(
                _settings.BannerOpacity + (notches * 10),
                BannerPresentation.MinBannerOpacity,
                BannerPresentation.MaxBannerOpacity);
            ReRenderCurrentEditFrame(reposition: false);
            _layoutDirty = true;
            ExtendEditWindow(now);
            return;
        }

        Point center = new(Left + (Width / 2), Top + (Height / 2));
        _settings.FontSizePixels = Math.Clamp(
            _settings.FontSizePixels + (notches * 2),
            BannerPresentation.MinFontSizePixels,
            BannerPresentation.MaxFontSizePixels);
        ReRenderCurrentEditFrame(reposition: false);

        int nextX = Math.Clamp(center.X - (Width / 2), _screen.Left, Math.Max(_screen.Left, _screen.Right - Width));
        int nextY = Math.Clamp(center.Y - (Height / 2), _screen.Top, Math.Max(_screen.Top, _screen.Bottom - Height));
        _movingProgrammatically = true;
        try
        {
            Location = new Point(nextX, nextY);
        }
        finally
        {
            _movingProgrammatically = false;
        }

        UpdateNormalizedPositionFromBounds();
        if (IsHandleCreated)
        {
            _surface.Present(Handle, Location);
        }

        _positionDirty = true;
        _layoutDirty = true;
        ExtendEditWindow(now);
    }

    private void ReRenderCurrentEditFrame(bool reposition)
    {
        double now = NowSeconds();
        bool demo = now - _previewStart < PreviewDurationSeconds;
        double elapsedAlert = demo ? Math.Max(0.0, now - _previewStart) : Math.Max(0.0, now - _alertStart);
        double editRemaining = _editing ? Math.Max(0.0, _editUntil - now) : 0.0;
        var frame = BannerPresentation.Compute(_engine.View(), _settings, demo, _editing, elapsedAlert, editRemaining);
        RenderFrame(frame, reposition);
        _lastRenderKey = BuildRenderKey(frame, _editing);
    }

    private void UpdateNormalizedPositionFromBounds()
    {
        _settings.HudX = Math.Clamp((Left - _screen.Left) / (double)Math.Max(1, _screen.Width - Width), 0.0, 1.0);
        _settings.HudY = Math.Clamp((Top - _screen.Top) / (double)Math.Max(1, _screen.Height - Height), 0.0, 1.0);
    }

    internal void BeginPreview() => _previewStart = NowSeconds();
    internal void BeginMove() => _editUntil = NowSeconds() + EditDurationSeconds;

    internal void FlushPositionIfDirty() => FlushLayoutIfDirty();

    internal void FlushLayoutIfDirty()
    {
        if (!_layoutDirty && !_positionDirty) return;
        UpdateNormalizedPositionFromBounds();
        _layoutDirty = false;
        _positionDirty = false;
        _engine.SaveBannerLayout(_settings.HudX, _settings.HudY, _settings.FontSizePixels, _settings.BannerOpacity);
        _revision = _engine.SettingsRevision;
    }

    internal void RenderForSmoke(BannerFrame frame, bool forceReposition = false)
    {
        RefreshGameWindowBinding();
        _screen = HudHostHelper.GetTargetScreenBounds(_lastGameWindow);
        RenderFrame(frame, forceReposition || _needsPosition);
    }

    internal void SimulateEditDragForSmoke(Point clientStart, int deltaX, int deltaY)
    {
        RefreshGameWindowBinding();
        _screen = HudHostHelper.GetTargetScreenBounds(_lastGameWindow);
        _editing = true;
        _editUntil = NowSeconds() + EditDurationSeconds;
        if (IsHandleCreated)
        {
            HudHostHelper.SetClickThrough(Handle, false);
        }

        if (_surface.Size.IsEmpty)
        {
            ReRenderCurrentEditFrame(reposition: true);
        }

        _dragMode = ClassifyEditHit(ClientSize, clientStart);
        if (_dragMode == BannerEditDragMode.None)
            return;

        _dragStartScreenPt = new Point(Left + clientStart.X, Top + clientStart.Y);
        _dragStartBounds = Bounds;
        _dragStartFontPixels = Math.Clamp(
            _settings.FontSizePixels,
            BannerPresentation.MinFontSizePixels,
            BannerPresentation.MaxFontSizePixels);

        ApplyDragDelta(new Point(_dragStartScreenPt.X + deltaX, _dragStartScreenPt.Y + deltaY));
        _dragMode = BannerEditDragMode.None;
        FlushLayoutIfDirty();
    }

    internal void SimulateEditWheelForSmoke(int wheelDelta, bool ctrlDown)
    {
        RefreshGameWindowBinding();
        _screen = HudHostHelper.GetTargetScreenBounds(_lastGameWindow);
        _editing = true;
        _editUntil = NowSeconds() + EditDurationSeconds;
        if (IsHandleCreated)
        {
            HudHostHelper.SetClickThrough(Handle, false);
        }

        if (_surface.Size.IsEmpty)
        {
            ReRenderCurrentEditFrame(reposition: true);
        }

        ApplyEditWheel(wheelDelta, ctrlDown);
        FlushLayoutIfDirty();
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

    private void ClearSurfaceToTransparent()
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

        _lastRenderKey = "";
    }

    private static string BuildRenderKey(BannerFrame frame, bool editing) =>
        $"{frame.PrimaryText}|{frame.SecondaryText}|{frame.FontSizePixels}|{frame.FlashOn}|{frame.TextColorArgb}|{frame.BackgroundColorArgb}|{frame.BorderColorArgb}|{editing}";

    internal void TickFrame(double now)
    {
        if (IsDisposed || Disposing) return;

        try
        {
            RefreshGameWindowBinding();
            bool editing = now < _editUntil;
            if ((_layoutDirty || _positionDirty)
                && _dragMode == BannerEditDragMode.None
                && (!editing || now - _lastMove > 0.5))
            {
                FlushLayoutIfDirty();
            }

            if (editing != _editing)
            {
                _editing = editing;
                if (!editing && _dragMode != BannerEditDragMode.None)
                {
                    _dragMode = BannerEditDragMode.None;
                    Capture = false;
                    Cursor = Cursors.Default;
                    FlushLayoutIfDirty();
                }

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
                if (!_editing && !_positionDirty && !_layoutDirty)
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
                    if (!_editing && !_positionDirty && !_layoutDirty)
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

            string stateSuffix = editing
                ? " • drag/resize banner now"
                : display
                    ? " • HUD visible"
                    : (!_settings.HudEnabled && !demo)
                        ? " • Banner disabled"
                        : " • HUD hidden";
            _status(_captureStatus + stateSuffix);

            if (!display)
            {
                if (Visible)
                {
                    ClearSurfaceToTransparent();
                    Hide();
                }

                return;
            }

            string renderKey = BuildRenderKey(frame, _editing);
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
            ClearSurfaceToTransparent();
            _needsPosition = false;
            return;
        }

        string primaryText = string.IsNullOrWhiteSpace(frame.PrimaryText) ? " " : frame.PrimaryText;
        string secondaryText = frame.SecondaryText ?? "";
        int primaryPixels = Math.Clamp(frame.FontSizePixels, BannerPresentation.MinFontSizePixels, BannerPresentation.MaxFontSizePixels);
        double scale = primaryPixels / 56.0;
        int secondaryPixels = Math.Clamp((int)Math.Round(primaryPixels * 0.42), 12, 58);
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

            int padX = Math.Clamp((int)Math.Round(28 * scale), 12, 64);
            int padY = Math.Clamp((int)Math.Round(16 * scale), 8, 40);
            int lineGap = Math.Clamp((int)Math.Round(8 * scale), 4, 20);
            int borderThickness = Math.Clamp((int)Math.Round(4 * scale), 2, 8);

            float contentWidth = Math.Max(primaryBounds.Width, hasSecondary ? secondaryBounds.Width : 0f);
            int minBannerWidth = Math.Clamp((int)Math.Round(420 * scale), 180, 960);
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

            if (_editing)
            {
                DrawResizeGrips(g, width, height, borderThickness);
            }

            float primaryOutlineWidth = Math.Clamp((float)(3.0 * scale), 1.8f, 5.5f);
            float subOutlineWidth = Math.Clamp((float)(2.0 * scale), 1.4f, 3.8f);
            using (var outline = new Pen(Color.FromArgb(225, 0, 0, 0), primaryOutlineWidth) { LineJoin = LineJoin.Round })
            using (var fill = new SolidBrush(Color.FromArgb(frame.TextColorArgb)))
            {
                g.DrawPath(outline, primaryPath);
                g.FillPath(fill, primaryPath);
                if (secondaryPath is not null)
                {
                    using var subOutline = new Pen(Color.FromArgb(200, 0, 0, 0), subOutlineWidth) { LineJoin = LineJoin.Round };
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

    private static void DrawResizeGrips(Graphics g, int width, int height, int borderThickness)
    {
        float inset = borderThickness + 3f;
        float arm = Math.Clamp(Math.Min(width, height) / 5f, 10f, 18f);
        float right = width - inset;
        float bottom = height - inset;

        using var gripPen = new Pen(Color.FromArgb(245, 165, 225, 255), 2.5f) { LineJoin = LineJoin.Miter };
        g.DrawLines(gripPen, new[] { new PointF(inset, inset + arm), new PointF(inset, inset), new PointF(inset + arm, inset) });
        g.DrawLines(gripPen, new[] { new PointF(right - arm, inset), new PointF(right, inset), new PointF(right, inset + arm) });
        g.DrawLines(gripPen, new[] { new PointF(inset, bottom - arm), new PointF(inset, bottom), new PointF(inset + arm, bottom) });
        g.DrawLines(gripPen, new[] { new PointF(right - arm, bottom), new PointF(right, bottom), new PointF(right, bottom - arm) });
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
