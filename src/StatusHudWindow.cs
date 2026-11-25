using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VoK.HealersHelper;

internal sealed class StatusHudWindow : Form
{
    private const double EditDurationSeconds = 20.0;
    private static readonly double TimestampFrequency = Stopwatch.Frequency;

    private readonly IHealerEngineView _engine;
    private readonly HudSurface _surface = new();
    private readonly FontFamily _family = new("Segoe UI");

    private Settings _settings;
    private int _revision = -1;
    private bool _appliedExcludeFromCapture;
    private double _editUntil = double.NegativeInfinity;
    private double _nextRaise;
    private double _nextScreenCheck;
    private double _lastMove;
    private bool _editing;
    private bool _movingProgrammatically;
    private bool _positionDirty;
    private bool _needsPosition = true;
    private string _lastRenderKey = "";
    private Rectangle _screen;
    private IntPtr _foreground;
    private bool _foregroundAllowed;
    private readonly uint _gamePid;

    internal HudSurface Surface => _surface;
    internal StatusHudFrame LastFrame { get; private set; }

    public StatusHudWindow(IHealerEngineView engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _settings = _engine.GetSettings();
        _appliedExcludeFromCapture = _settings.ExcludeFromCapture;

        Text = "DDO Healer's Helper Status";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.Manual;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);

        try
        {
            if (_engine.GameWindow != IntPtr.Zero)
            {
                NativeHud.GetWindowThreadProcessId(_engine.GameWindow, out _gamePid);
            }

            _screen = HudHostHelper.GetTargetScreenBounds(_engine.GameWindow);
            _ = Handle;
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
            p.ExStyle |= NativeHud.Layered | NativeHud.Transparent | NativeHud.NoActivate | NativeHud.ToolWindow;
            return p;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _appliedExcludeFromCapture = _settings.ExcludeFromCapture;
        HudHostHelper.ApplyCaptureExclusion(Handle, _settings.ExcludeFromCapture);
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

    internal void BeginMove() => _editUntil = NowSeconds() + EditDurationSeconds;

    internal void RenderForSmoke(StatusHudFrame frame, bool forceReposition = false)
    {
        _screen = HudHostHelper.GetTargetScreenBounds(_engine.GameWindow);
        RenderFrame(frame, forceReposition || _needsPosition);
    }

    internal void TickFrame(double now)
    {
        if (_positionDirty && now - _lastMove > 0.5)
        {
            double x = (Left - _screen.Left) / (double)Math.Max(1, _screen.Width - Width);
            double y = (Top - _screen.Top) / (double)Math.Max(1, _screen.Height - Height);
            _engine.SaveStatusPosition(Math.Clamp(x, 0.0, 1.0), Math.Clamp(y, 0.0, 1.0));
            _positionDirty = false;
        }

        int revision = _engine.SettingsRevision;
        if (revision != _revision)
        {
            _settings = _engine.GetSettings();
            _revision = revision;
            _lastRenderKey = "";
            _needsPosition = true;
            if (IsHandleCreated && _settings.ExcludeFromCapture != _appliedExcludeFromCapture)
            {
                _appliedExcludeFromCapture = _settings.ExcludeFromCapture;
                HudHostHelper.ApplyCaptureExclusion(Handle, _settings.ExcludeFromCapture);
            }
        }

        bool editing = now < _editUntil;
        if (editing != _editing)
        {
            _editing = editing;
            _lastRenderKey = "";
            if (IsHandleCreated)
            {
                HudHostHelper.SetClickThrough(Handle, !editing);
            }
        }

        if (now >= _nextScreenCheck)
        {
            var screen = HudHostHelper.GetTargetScreenBounds(_engine.GameWindow);
            if (screen != _screen)
            {
                _screen = screen;
                _needsPosition = true;
            }

            _nextScreenCheck = now + 1.0;
        }

        var live = _engine.View();
        bool foreground = HudHostHelper.ForegroundAllowed(_engine.GameWindow, _gamePid, ref _foreground, ref _foregroundAllowed);
        double editRemaining = editing ? Math.Max(0.0, _editUntil - now) : 0.0;
        var frame = StatusHudPresentation.Compute(live, _settings, editing, editRemaining);

        bool display = frame.Visible && (editing || foreground);
        if (!display)
        {
            if (Visible)
            {
                Hide();
            }

            return;
        }

        string renderKey = $"{frame.Text}|{frame.FontSizePixels}|{frame.TextColorArgb}|{frame.ShowDragBox}|{frame.DragHint}";
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

    private void RenderFrame(StatusHudFrame frame, bool reposition)
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

        string text = string.IsNullOrWhiteSpace(frame.Text) ? " " : frame.Text;
        int fontPixels = Math.Clamp(frame.FontSizePixels, StatusHudPresentation.MinFontSizePixels, StatusHudPresentation.MaxFontSizePixels);

        using var path = new GraphicsPath();
        path.AddString(text, _family, (int)FontStyle.Bold, fontPixels, PointF.Empty, StringFormat.GenericTypographic);
        var bounds = path.GetBounds();
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            bounds = new RectangleF(0, 0, fontPixels, fontPixels);
        }

        GraphicsPath? hintPath = null;
        RectangleF hintBounds = RectangleF.Empty;
        if (frame.ShowDragBox && !string.IsNullOrWhiteSpace(frame.DragHint))
        {
            hintPath = new GraphicsPath();
            hintPath.AddString(frame.DragHint, _family, (int)FontStyle.Regular, 11, PointF.Empty, StringFormat.GenericTypographic);
            hintBounds = hintPath.GetBounds();
        }

        try
        {
            const int padX = 8;
            const int padY = 4;
            const int hintGap = 3;

            float contentW = Math.Max(bounds.Width, hintPath is not null ? hintBounds.Width : 0f);
            int width = Math.Max(48, (int)Math.Ceiling(contentW) + (padX * 2));
            int height = (int)Math.Ceiling(bounds.Height) + (padY * 2) + (hintPath is not null ? (int)Math.Ceiling(hintBounds.Height) + hintGap : 0);

            bool sizeChanged = _surface.Size.Width != width || _surface.Size.Height != height;
            _surface.Resize(width, height);

            float textX = ((width - bounds.Width) * 0.5f) - bounds.X;
            float textY = padY - bounds.Y;
            using (var shift = new Matrix())
            {
                shift.Translate(textX, textY);
                path.Transform(shift);
            }

            if (hintPath is not null)
            {
                float hintX = ((width - hintBounds.Width) * 0.5f) - hintBounds.X;
                float hintY = padY + bounds.Height + hintGap - hintBounds.Y;
                using var shift = new Matrix();
                shift.Translate(hintX, hintY);
                hintPath.Transform(shift);
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
                        _settings.StatusHudX,
                        _settings.StatusHudY);
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

            if (frame.ShowDragBox)
            {
                using var plate = new SolidBrush(Color.FromArgb(205, 16, 20, 28));
                using var border = new Pen(Color.FromArgb(255, 86, 168, 255), 1.5f);
                g.FillRectangle(plate, 0, 0, width, height);
                g.DrawRectangle(border, 0.75f, 0.75f, width - 1.5f, height - 1.5f);
            }

            using (var outline = new Pen(Color.FromArgb(230, 0, 0, 0), 2.0f) { LineJoin = LineJoin.Round })
            using (var fill = new SolidBrush(Color.FromArgb(frame.TextColorArgb)))
            {
                g.DrawPath(outline, path);
                g.FillPath(fill, path);
                if (hintPath is not null)
                {
                    using var hintFill = new SolidBrush(Color.FromArgb(235, 220, 232, 248));
                    g.FillPath(hintFill, hintPath);
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
            hintPath?.Dispose();
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
