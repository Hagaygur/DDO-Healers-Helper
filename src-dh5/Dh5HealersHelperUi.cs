using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using ImGuiNET;

namespace VoK.HealersHelper;

// DH 5 Beta Dear ImGui 1.92 UI (3 shared DDO x Material Design 3 themes):
// - Always-on Center Alert Banner + Compact Status HUD via ForegroundDrawList & dynamic font sizing
// - Interactive 20s banner drag/corner-resize/wheel/Ctrl+wheel opacity & status drag
// - Optional Always-On Triage-Sorted Raid HUD (>=28px WCAG rows, TempHP gold cap, SP sliver,
//   Left-Click targetless InvokeHotbar Heal/Raise, Right-Click select/focus, Middle-Click mute)
// - 5-Tab Settings & Control Window (gated on Dh5Plugin.WindowVisible)
internal sealed class Dh5HealersHelperUi : IDisposable
{
    private const double PreviewDurationSeconds = 8.0;
    private const double EditDurationSeconds = 20.0;

    // Semantic alert colors (identical across all 3 DDO x M3 themes for preattentive consistency)
    private static readonly Vector4 EmeraldHealthy = HexToVec4(0x67, 0xDE, 0x9F, 1.00f);
    private static readonly Vector4 CrimsonAlert = HexToVec4(0xFF, 0x4D, 0x5A, 1.00f);
    private static readonly Vector4 AmberWarn = HexToVec4(0xFF, 0xAE, 0x42, 1.00f);
    private static readonly Vector4 SpectralViolet = HexToVec4(0xA8, 0x7F, 0xFB, 1.00f);

    private static readonly string[] ModeLabels = Enum.GetNames<TrackingMode>();
    private static readonly string[] TargetMethodLabels = Enum.GetNames<TargetMethod>();
    private static readonly string[] ThemeLabels =
    [
        UiThemePalette.EberronGold.DisplayName,
        UiThemePalette.CannithArcane.DisplayName,
        UiThemePalette.FeywildTwilight.DisplayName
    ];

    private UiThemePalette _activePalette = UiThemePalette.EberronGold;
    private Vector4 SurfaceBase => ArgbToVec4(_activePalette.SurfaceBaseArgb, 0.96f);
    private Vector4 SurfaceContainer => ArgbToVec4(_activePalette.SurfaceContainerArgb, 0.96f);
    private Vector4 SurfaceControl => ArgbToVec4(_activePalette.SurfaceControlArgb, 1.00f);
    private Vector4 SurfaceHover => ArgbToVec4(_activePalette.SurfaceHoverArgb, 1.00f);
    private Vector4 HairlineBorder => ArgbToVec4(_activePalette.OutlineArgb, 1.00f);
    private Vector4 SovereignGold => ArgbToVec4(_activePalette.PrimaryArgb, 1.00f);
    private Vector4 SovereignGoldSoft => ArgbToVec4(_activePalette.PrimaryArgb, 0.30f);
    private Vector4 TextPrimary => ArgbToVec4(_activePalette.TextPrimaryArgb, 1.00f);
    private Vector4 TextMuted => ArgbToVec4(_activePalette.TextMutedArgb, 1.00f);

    private readonly Dh5Plugin _plugin;
    private readonly Dh5HealerEngine _engine;

    private Settings _draft;
    private int _draftRevision = -1;
    private bool _wasWindowVisible;
    private bool _draftDirty;
    private bool _pluginEnabledDirty;
    private bool _hudEnabledDirty;
    private bool _modeDirty;
    private bool _raidHudDirty;
    private bool _positionsResetPending;
    private bool _focusEditedInDraft;
    private bool _fontOrOpacityEditedInDraft;
    private string _settingsStatus = "Changes take effect immediately on Save.";
    private int _recordingHotkeyIndex = -1;

    private double _previewStart = double.NegativeInfinity;
    private double _bannerEditUntil = double.NegativeInfinity;
    private double _statusEditUntil = double.NegativeInfinity;
    private double _alertStart;
    private long _alertGeneration = -1;
    private BannerSeverity _alertSeverity = BannerSeverity.None;

    private BannerEditDragMode _bannerDragMode;
    private Vector2 _bannerDragStartMouse;
    private Vector2 _bannerDragStartPos;
    private Vector2 _bannerDragStartSize;
    private int _bannerDragStartFont;

    private bool _statusDragging;
    private Vector2 _statusDragStartMouse;
    private Vector2 _statusDragStartPos;

    // Raid HUD position tracking so ResetUi()/Settings/viewport changes apply cleanly without row clicks overwriting coords.
    private bool _hasAppliedRaidPos;
    private double _appliedRaidHudX;
    private double _appliedRaidHudY;
    private Vector2 _appliedRaidVpPos;
    private Vector2 _appliedRaidVpSize;
    private Vector2 _appliedRaidWinPos;

    private bool _disposed;

    public Dh5HealersHelperUi(Dh5Plugin plugin, Dh5HealerEngine engine)
    {
        _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _draft = _engine.GetSettings();
        _draftRevision = _engine.SettingsRevision;
    }

    public void RenderFrame()
    {
        if (_disposed)
            return;

        // Drain any off-thread clipboard fallback strictly on the ImGui render thread.
        if (_engine.TakePendingUiClipboardText() is { Length: > 0 } pendingClip)
        {
            ImGui.SetClipboardText(pendingClip);
        }

        double now = Dh5HealerEngine.Now;
        var settings = _engine.GetSettings();
        var view = _engine.View();
        var members = _engine.GetMembers();

        bool windowVisible = _plugin.WindowVisible;
        if (windowVisible && !_wasWindowVisible && !_draftDirty)
        {
            _draft = settings.Copy();
            _draftRevision = _engine.SettingsRevision;
        }
        _wasWindowVisible = windowVisible;

        if (!windowVisible)
        {
            _recordingHotkeyIndex = -1;
        }
        else
        {
            SyncDraftWithLive(settings);
        }

        // Suppress live DH 5 hotkey dispatch while actively recording a hotkey chord.
        _engine.SetHotkeyCaptureActive(windowVisible && _recordingHotkeyIndex >= 0);

        _activePalette = UiThemePalette.For(windowVisible ? _draft.Theme : settings.Theme);

        // 1. Always-on overlays (never gated by WindowVisible so HUDs stay active during combat!)
        RenderCenterBanner(view, settings, now);
        RenderStatusHud(view, settings, now);
        if (settings.RaidHudEnabled && settings.PluginEnabled)
        {
            RenderTriageRaidHud(members, settings);
        }

        // 2. Main Control & 5-Tab Settings Window (strictly gated by WindowVisible)
        if (windowVisible)
        {
            RenderMainWindow(view, settings, members, now);
            // Re-sync capture state in case recording started or stopped during this frame.
            _engine.SetHotkeyCaptureActive(_plugin.WindowVisible && _recordingHotkeyIndex >= 0);
        }
    }

    private void SyncDraftWithLive(Settings live)
    {
        int rev = _engine.SettingsRevision;
        if (rev == _draftRevision)
            return;

        if (!_draftDirty && _recordingHotkeyIndex < 0)
        {
            _draft = live.Copy();
            _draftRevision = rev;
            return;
        }

        // Never wipe unsaved edits when background saves/streaks/HUD drags bump SettingsRevision.
        _draft.CopyLiveStatsFrom(live);
        if (!_pluginEnabledDirty) _draft.PluginEnabled = live.PluginEnabled;
        if (!_hudEnabledDirty) _draft.HudEnabled = live.HudEnabled;
        if (!_modeDirty) _draft.Mode = live.Mode;
        if (!_raidHudDirty) _draft.RaidHudEnabled = live.RaidHudEnabled;
        if (!_positionsResetPending)
        {
            _draft.HudX = live.HudX;
            _draft.HudY = live.HudY;
            _draft.StatusHudX = live.StatusHudX;
            _draft.StatusHudY = live.StatusHudY;
            _draft.RaidHudX = live.RaidHudX;
            _draft.RaidHudY = live.RaidHudY;
        }
        if (!_focusEditedInDraft)
        {
            _draft.FocusedPlayerId = live.FocusedPlayerId;
            _draft.FocusedPlayerName = live.FocusedPlayerName;
        }
        if (!_fontOrOpacityEditedInDraft)
        {
            _draft.FontSizePixels = live.FontSizePixels;
            _draft.BannerOpacity = live.BannerOpacity;
        }
        _draftRevision = rev;
    }

    private void RenderCenterBanner(AlertBannerView view, Settings settings, double now)
    {
        if (view.Severity is BannerSeverity.LowHp or BannerSeverity.CriticalDown)
        {
            if (_alertGeneration != view.AlertGeneration || _alertSeverity != view.Severity)
            {
                _alertStart = now;
                _alertGeneration = view.AlertGeneration;
                _alertSeverity = view.Severity;
            }
        }
        else
        {
            _alertSeverity = view.Severity;
            _alertGeneration = view.AlertGeneration;
        }

        bool demo = now - _previewStart < PreviewDurationSeconds;
        bool editing = now < _bannerEditUntil;
        double elapsedAlert = demo ? Math.Max(0.0, now - _previewStart) : Math.Max(0.0, now - _alertStart);
        double editRemaining = editing ? Math.Max(0.0, _bannerEditUntil - now) : 0.0;

        BannerFrame frame = BannerPresentation.Compute(view, settings, demo, editing, elapsedAlert, editRemaining);
        bool modeAllowsLive = settings.PluginEnabled && settings.HudEnabled && view.Mode != TrackingMode.Off;
        if (!frame.Visible || !(demo || editing || modeAllowsLive))
        {
            _bannerDragMode = BannerEditDragMode.None;
            return;
        }

        var viewport = ImGui.GetMainViewport();
        Vector2 vpPos = viewport.Pos;
        Vector2 vpSize = viewport.Size;
        if (vpSize.X <= 1f || vpSize.Y <= 1f)
            return;

        float fontSize = Math.Clamp(frame.FontSizePixels, BannerPresentation.MinFontSizePixels, BannerPresentation.MaxFontSizePixels);
        float scale = fontSize / 56f;
        float subFontSize = Math.Clamp((float)Math.Round(fontSize * 0.42f), 12f, 58f);
        ImFontPtr font = ImGui.GetFont();

        string primary = string.IsNullOrWhiteSpace(frame.PrimaryText) ? " " : frame.PrimaryText;
        string secondary = frame.SecondaryText ?? "";
        bool hasSecondary = !string.IsNullOrWhiteSpace(secondary);

        Vector2 primarySize = MeasureText(font, fontSize, primary);
        Vector2 secondarySize = hasSecondary ? MeasureText(font, subFontSize, secondary) : Vector2.Zero;

        float padX = Math.Clamp(28f * scale, 12f, 64f);
        float padY = Math.Clamp(16f * scale, 8f, 40f);
        float lineGap = Math.Clamp(8f * scale, 4f, 20f);
        float borderThickness = Math.Clamp(4f * scale, 2f, 8f);
        bool hasHpBar = frame.HpBarPercent.HasValue;
        float barHeight = hasHpBar ? Math.Clamp(7f * scale, 4f, 12f) : 0f;
        float barGap = hasHpBar ? Math.Clamp(6f * scale, 3f, 12f) : 0f;

        float minWidth = Math.Clamp(420f * scale, 180f, 960f);
        float width = Math.Max(minWidth, Math.Max(primarySize.X, secondarySize.X) + (padX * 2f));
        float height = primarySize.Y + (padY * 2f) + (hasSecondary ? secondarySize.Y + lineGap : 0f) + (hasHpBar ? barHeight + barGap : 0f);

        var computed = BannerPresentation.ComputePosition(
            (int)vpPos.X,
            (int)vpPos.Y,
            (int)vpSize.X,
            (int)vpSize.Y,
            (int)Math.Ceiling(width),
            (int)Math.Ceiling(height),
            settings.HudX,
            settings.HudY);

        Vector2 topLeft = new(computed.X, computed.Y);
        Vector2 bottomRight = new(topLeft.X + width, topLeft.Y + height);

        ImDrawListPtr dl = ImGui.GetForegroundDrawList();
        uint bgCol = ArgbToImU32(frame.BackgroundColorArgb);
        uint borderCol = ArgbToImU32(frame.BorderColorArgb);
        uint textCol = ArgbToImU32(frame.TextColorArgb);
        uint shadowCol = ArgbToImU32(unchecked((int)0xDC000000));

        dl.AddRectFilled(topLeft, bottomRight, bgCol, 6f);
        dl.AddRect(topLeft, bottomRight, borderCol, 6f, borderThickness);

        Vector2 primaryPos = new(topLeft.X + ((width - primarySize.X) * 0.5f), topLeft.Y + padY);
        DrawOutlinedText(dl, font, fontSize, primaryPos, textCol, shadowCol, primary);

        if (hasSecondary)
        {
            Vector2 subPos = new(topLeft.X + ((width - secondarySize.X) * 0.5f), primaryPos.Y + primarySize.Y + lineGap);
            DrawOutlinedText(dl, font, subFontSize, subPos, textCol, shadowCol, secondary);
        }

        if (hasHpBar)
        {
            float barLeft = topLeft.X + borderThickness + 8f;
            float barRight = bottomRight.X - borderThickness - 8f;
            float barWidth = Math.Max(20f, barRight - barLeft);
            float barBottom = bottomRight.Y - borderThickness - 4f;
            float barTop = barBottom - barHeight;
            float pct = (float)Math.Clamp(frame.HpBarPercent!.Value / 100.0, 0.0, 1.0);

            dl.AddRectFilled(new Vector2(barLeft, barTop), new Vector2(barLeft + barWidth, barBottom), ArgbToImU32(unchecked((int)0xD810131A)), 2f);

            uint fillCol = frame.IsSoulstone
                ? ArgbToImU32(unchecked((int)0xFFA87FFB))
                : pct <= 0.001f
                    ? ArgbToImU32(unchecked((int)0xFFFF3848))
                    : pct <= 0.35f
                        ? ArgbToImU32(unchecked((int)0xFFFF6E38))
                        : ArgbToImU32(unchecked((int)0xFFFFAE42));
            if (pct > 0.001f)
            {
                dl.AddRectFilled(new Vector2(barLeft, barTop), new Vector2(barLeft + (barWidth * pct), barBottom), fillCol, 2f);
            }

            if (frame.TempHitPoints > 0 && !frame.IsSoulstone)
            {
                float capW = Math.Clamp(barWidth * 0.14f, 8f, Math.Max(8f, barWidth * (1f - pct)));
                float capX = Math.Min(barLeft + barWidth - capW, barLeft + (barWidth * pct));
                dl.AddRectFilled(new Vector2(capX, barTop), new Vector2(capX + capW, barBottom), ArgbToImU32(BannerPresentation.TempHpGoldArgb), 2f);
            }
        }

        if (editing)
        {
            DrawBannerResizeGrips(dl, topLeft, bottomRight, borderThickness, _activePalette.PrimaryArgb);
            HandleBannerEditOverlay(topLeft, new Vector2(width, height), vpPos, vpSize, settings, now);
        }
    }

    private void HandleBannerEditOverlay(
        Vector2 topLeft,
        Vector2 size,
        Vector2 vpPos,
        Vector2 vpSize,
        Settings settings,
        double now)
    {
        ImGui.SetNextWindowPos(topLeft, ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground
            | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing;

        try
        {
            if (ImGui.Begin("##HH_BannerEditCapture", flags))
            {
                ImGui.InvisibleButton("##HH_BannerEditBtn", size);
                bool hovered = ImGui.IsItemHovered();
                var io = ImGui.GetIO();
                Vector2 mouse = io.MousePos;

                if (hovered && io.MouseWheel != 0f)
                {
                    int notches = io.MouseWheel > 0 ? 1 : -1;
                    int nextFont = settings.FontSizePixels;
                    int nextOpacity = settings.BannerOpacity;
                    if (io.KeyCtrl)
                        nextOpacity = Math.Clamp(nextOpacity + (notches * 10), BannerPresentation.MinBannerOpacity, BannerPresentation.MaxBannerOpacity);
                    else
                        nextFont = Math.Clamp(nextFont + (notches * 2), BannerPresentation.MinFontSizePixels, BannerPresentation.MaxFontSizePixels);

                    _engine.SaveBannerLayout(settings.HudX, settings.HudY, nextFont, nextOpacity);
                    _bannerEditUntil = Math.Max(_bannerEditUntil, now + 5.0);
                }

                if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    var mode = BannerPresentation.ClassifyEditHit(
                        (int)Math.Max(1, size.X),
                        (int)Math.Max(1, size.Y),
                        (int)(mouse.X - topLeft.X),
                        (int)(mouse.Y - topLeft.Y));
                    _bannerDragMode = mode;
                    _bannerDragStartMouse = mouse;
                    _bannerDragStartPos = topLeft;
                    _bannerDragStartSize = size;
                    _bannerDragStartFont = settings.FontSizePixels;
                    _bannerEditUntil = Math.Max(_bannerEditUntil, now + 5.0);
                }

                if (_bannerDragMode != BannerEditDragMode.None)
                {
                    if (ImGui.IsMouseDragging(ImGuiMouseButton.Left, 0f))
                    {
                        Vector2 delta = mouse - _bannerDragStartMouse;
                        double nextX = settings.HudX;
                        double nextY = settings.HudY;
                        int nextFont = settings.FontSizePixels;

                        if (_bannerDragMode == BannerEditDragMode.Move)
                        {
                            float availW = Math.Max(1f, vpSize.X - size.X);
                            float availH = Math.Max(1f, vpSize.Y - size.Y);
                            nextX = Math.Clamp((_bannerDragStartPos.X + delta.X - vpPos.X) / availW, 0.0, 1.0);
                            nextY = Math.Clamp((_bannerDragStartPos.Y + delta.Y - vpPos.Y) / availH, 0.0, 1.0);
                        }
                        else
                        {
                            bool left = _bannerDragMode is BannerEditDragMode.ResizeLeft or BannerEditDragMode.ResizeTopLeft or BannerEditDragMode.ResizeBottomLeft;
                            bool right = _bannerDragMode is BannerEditDragMode.ResizeRight or BannerEditDragMode.ResizeTopRight or BannerEditDragMode.ResizeBottomRight;
                            bool top = _bannerDragMode is BannerEditDragMode.ResizeTop or BannerEditDragMode.ResizeTopLeft or BannerEditDragMode.ResizeTopRight;
                            bool bottom = _bannerDragMode is BannerEditDragMode.ResizeBottom or BannerEditDragMode.ResizeBottomLeft or BannerEditDragMode.ResizeBottomRight;

                            double rw = left ? (_bannerDragStartSize.X - delta.X) / Math.Max(1f, _bannerDragStartSize.X)
                                : right ? (_bannerDragStartSize.X + delta.X) / Math.Max(1f, _bannerDragStartSize.X)
                                : 1.0;
                            double rh = top ? (_bannerDragStartSize.Y - delta.Y) / Math.Max(1f, _bannerDragStartSize.Y)
                                : bottom ? (_bannerDragStartSize.Y + delta.Y) / Math.Max(1f, _bannerDragStartSize.Y)
                                : 1.0;
                            double ratio = (left || right) && (top || bottom) ? (rw + rh) * 0.5 : (left || right) ? rw : rh;
                            nextFont = Math.Clamp(
                                (int)Math.Round(_bannerDragStartFont * Math.Max(0.1, ratio)),
                                BannerPresentation.MinFontSizePixels,
                                BannerPresentation.MaxFontSizePixels);
                        }

                        _engine.SaveBannerLayout(nextX, nextY, nextFont, settings.BannerOpacity);
                        _bannerEditUntil = Math.Max(_bannerEditUntil, now + 5.0);
                    }
                    else if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
                    {
                        _bannerDragMode = BannerEditDragMode.None;
                    }
                }
            }
        }
        finally
        {
            ImGui.End();
        }
    }

    private void RenderStatusHud(AlertBannerView view, Settings settings, double now)
    {
        bool editing = now < _statusEditUntil;
        double editRemaining = editing ? Math.Max(0.0, _statusEditUntil - now) : 0.0;
        StatusHudFrame frame = StatusHudPresentation.Compute(view, settings, editing, editRemaining);
        if (!frame.Visible)
        {
            _statusDragging = false;
            return;
        }

        var viewport = ImGui.GetMainViewport();
        Vector2 vpPos = viewport.Pos;
        Vector2 vpSize = viewport.Size;
        if (vpSize.X <= 1f || vpSize.Y <= 1f)
            return;

        ImFontPtr font = ImGui.GetFont();
        float fontSize = Math.Clamp(frame.FontSizePixels, StatusHudPresentation.MinFontSizePixels, StatusHudPresentation.MaxFontSizePixels);
        Vector2 textSize = MeasureText(font, fontSize, frame.Text);
        bool hasHint = frame.ShowDragBox && !string.IsNullOrWhiteSpace(frame.DragHint);
        Vector2 hintSize = hasHint ? MeasureText(font, 11f, frame.DragHint) : Vector2.Zero;

        const float padX = 10f;
        const float padY = 4f;
        const float hintGap = 3f;
        float width = Math.Max(64f, Math.Max(textSize.X, hintSize.X) + (padX * 2f));
        float height = textSize.Y + (padY * 2f) + (hasHint ? hintSize.Y + hintGap : 0f);

        var computed = BannerPresentation.ComputePosition(
            (int)vpPos.X,
            (int)vpPos.Y,
            (int)vpSize.X,
            (int)vpSize.Y,
            (int)Math.Ceiling(width),
            (int)Math.Ceiling(height),
            settings.StatusHudX,
            settings.StatusHudY);

        Vector2 topLeft = new(computed.X, computed.Y);
        Vector2 bottomRight = new(topLeft.X + width, topLeft.Y + height);

        ImDrawListPtr dl = ImGui.GetForegroundDrawList();
        if (frame.ShowDragBox)
        {
            int dragBg = unchecked((int)((0xD0u << 24) | ((uint)_activePalette.SurfaceBaseArgb & 0x00FFFFFFu)));
            dl.AddRectFilled(topLeft, bottomRight, ArgbToImU32(dragBg), 4f);
            dl.AddRect(topLeft, bottomRight, ArgbToImU32(_activePalette.PrimaryArgb), 4f, 1.5f);
        }

        Vector2 textPos = new(topLeft.X + ((width - textSize.X) * 0.5f), topLeft.Y + padY);
        DrawOutlinedText(dl, font, fontSize, textPos, ArgbToImU32(frame.TextColorArgb), ArgbToImU32(unchecked((int)0xE0000000)), frame.Text);

        if (hasHint)
        {
            Vector2 hintPos = new(topLeft.X + ((width - hintSize.X) * 0.5f), textPos.Y + textSize.Y + hintGap);
            dl.AddText(font, 11f, hintPos, ArgbToImU32(unchecked((int)0xFFDCE8F8)), frame.DragHint);
        }

        if (editing)
        {
            ImGui.SetNextWindowPos(topLeft, ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Vector2(width, height), ImGuiCond.Always);
            ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground
                | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing;

            try
            {
                if (ImGui.Begin("##HH_StatusEditCapture", flags))
                {
                    ImGui.InvisibleButton("##HH_StatusEditBtn", new Vector2(width, height));
                    Vector2 mouse = ImGui.GetIO().MousePos;
                    if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    {
                        _statusDragging = true;
                        _statusDragStartMouse = mouse;
                        _statusDragStartPos = topLeft;
                        _statusEditUntil = Math.Max(_statusEditUntil, now + 5.0);
                    }

                    if (_statusDragging)
                    {
                        if (ImGui.IsMouseDragging(ImGuiMouseButton.Left, 0f))
                        {
                            Vector2 delta = mouse - _statusDragStartMouse;
                            float availW = Math.Max(1f, vpSize.X - width);
                            float availH = Math.Max(1f, vpSize.Y - height);
                            double nextX = Math.Clamp((_statusDragStartPos.X + delta.X - vpPos.X) / availW, 0.0, 1.0);
                            double nextY = Math.Clamp((_statusDragStartPos.Y + delta.Y - vpPos.Y) / availH, 0.0, 1.0);
                            _engine.SaveStatusPosition(nextX, nextY);
                            _statusEditUntil = Math.Max(_statusEditUntil, now + 5.0);
                        }
                        else if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
                        {
                            _statusDragging = false;
                        }
                    }
                }
            }
            finally
            {
                ImGui.End();
            }
        }
    }

    private void RenderTriageRaidHud(WatchedMemberSnapshot[] rawMembers, Settings settings)
    {
        var sorted = HealerState.SortForTriage(
            rawMembers,
            settings,
            m => _engine.IsPlayerMuted(m.Id, m.Name));

        var viewport = ImGui.GetMainViewport();
        Vector2 vpPos = viewport.Pos;
        Vector2 vpSize = viewport.Size;
        if (vpSize.X <= 1f || vpSize.Y <= 1f)
            return;

        bool twoCols = sorted.Length > 6;
        float rowHeight = 32f; // Strictly >= 28px WCAG 2.5.8 target size
        float cardWidth = twoCols ? 196f : 230f;
        int rows = twoCols ? (sorted.Length + 1) / 2 : Math.Max(1, sorted.Length);
        float panelWidth = (twoCols ? (cardWidth * 2f) + 18f : cardWidth + 14f);
        float panelHeight = 30f + (rows * (rowHeight + 4f)) + 8f;

        var computed = BannerPresentation.ComputePosition(
            (int)vpPos.X,
            (int)vpPos.Y,
            (int)vpSize.X,
            (int)vpSize.Y,
            (int)panelWidth,
            (int)panelHeight,
            settings.RaidHudX,
            settings.RaidHudY);
        Vector2 targetPos = new(computed.X, computed.Y);

        bool coordsOrViewportChanged = !_hasAppliedRaidPos
            || Math.Abs(settings.RaidHudX - _appliedRaidHudX) > 0.0005
            || Math.Abs(settings.RaidHudY - _appliedRaidHudY) > 0.0005
            || _appliedRaidVpPos != vpPos
            || _appliedRaidVpSize != vpSize;

        if (coordsOrViewportChanged)
        {
            ImGui.SetNextWindowPos(targetPos, ImGuiCond.Always);
            _hasAppliedRaidPos = true;
            _appliedRaidHudX = settings.RaidHudX;
            _appliedRaidHudY = settings.RaidHudY;
            _appliedRaidVpPos = vpPos;
            _appliedRaidVpSize = vpSize;
            _appliedRaidWinPos = targetPos;
        }

        ImGui.SetNextWindowSize(new Vector2(panelWidth, panelHeight), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.88f);

        PushEberronTheme();
        try
        {
            ImGuiWindowFlags flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize
                | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoSavedSettings;
            string title = sorted.Length > 6
                ? $"Triage Raid ({sorted.Length})###HH_RaidHud"
                : $"Triage Party ({sorted.Length})###HH_RaidHud";

            try
            {
                if (ImGui.Begin(title, flags))
                {
                    Vector2 curPos = ImGui.GetWindowPos();
                    bool actuallyMoved = Math.Abs(curPos.X - _appliedRaidWinPos.X) > 1.0f
                        || Math.Abs(curPos.Y - _appliedRaidWinPos.Y) > 1.0f;
                    if (actuallyMoved && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
                    {
                        float availW = Math.Max(1f, vpSize.X - panelWidth);
                        float availH = Math.Max(1f, vpSize.Y - panelHeight);
                        double nx = Math.Clamp((curPos.X - vpPos.X) / availW, 0.0, 1.0);
                        double ny = Math.Clamp((curPos.Y - vpPos.Y) / availH, 0.0, 1.0);
                        _appliedRaidHudX = nx;
                        _appliedRaidHudY = ny;
                        _appliedRaidWinPos = curPos;
                        _engine.SaveRaidHudPosition(nx, ny);
                    }

                    if (sorted.Length == 0)
                    {
                        ImGui.TextColored(TextMuted, "Waiting for party...");
                    }
                    else
                    {
                        ImDrawListPtr dl = ImGui.GetWindowDrawList();
                        float pulse = 0.5f + (0.5f * (float)Math.Sin(Dh5HealerEngine.Now * Math.PI * 3.0)); // 1.5 Hz <= 3 Hz WCAG

                        for (int i = 0; i < sorted.Length; i++)
                        {
                            if (twoCols && (i % 2 == 1))
                                ImGui.SameLine(0f, 6f);

                            ImGui.PushID(i);
                            try
                            {
                                DrawTriageRow(dl, sorted[i], settings, cardWidth, rowHeight, pulse);
                            }
                            finally
                            {
                                ImGui.PopID();
                            }
                        }
                    }
                }
            }
            finally
            {
                ImGui.End();
            }
        }
        finally
        {
            PopEberronTheme();
        }
    }

    private void DrawTriageRow(
        ImDrawListPtr dl,
        WatchedMemberSnapshot member,
        Settings settings,
        float width,
        float height,
        float pulse)
    {
        bool muted = _engine.IsPlayerMuted(member.Id, member.Name);
        bool low = !muted && member.Connected && !member.IsDeadOrDown && !member.IsTrulyDead
            && member.MaxHitPoints > 0 && member.EffectivePercent(settings.IncludeTempHpInThreshold) <= settings.HpThresholdPercent;

        Vector2 topLeft = ImGui.GetCursorScreenPos();
        Vector2 bottomRight = new(topLeft.X + width, topLeft.Y + height);

        ImGui.InvisibleButton("##HH_RowBtn", new Vector2(width, height));
        bool hovered = ImGui.IsItemHovered();

        // Click-to-cast / select / mute interaction (zero target loss on Left-Click!)
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            if (!_engine.QuickCastOnMember(member, out _))
            {
                _engine.SelectMember(member);
            }
        }
        else if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            if (ImGui.GetIO().KeyShift)
                _engine.FocusMember(member);
            else
                _engine.SelectMember(member);
        }
        else if (ImGui.IsItemClicked(ImGuiMouseButton.Middle))
        {
            _engine.ToggleMutePlayer(member.Id, member.Name);
        }

        uint cardBg = ImGui.ColorConvertFloat4ToU32(hovered ? SurfaceHover : SurfaceContainer);
        dl.AddRectFilled(topLeft, bottomRight, cardBg, 4f);

        // HP bar + Sovereign Gold TempHP shield cap
        float hpPct = member.MaxHitPoints > 0 ? (float)Math.Clamp(member.HitPoints / (double)member.MaxHitPoints, 0.0, 1.0) : 0f;
        float barBottom = bottomRight.Y - (member.MaxSpellPoints > 0 ? 4f : 1f);
        float fillRight = topLeft.X + (width * hpPct);

        uint fillCol = muted || !member.Connected
            ? ArgbToImU32(unchecked((int)0x882A303C))
            : member.IsTrulyDead
                ? ArgbToImU32(unchecked((int)0xCC2A1538))
                : member.IsIncapacitated
                    ? ArgbToImU32(unchecked((int)0xCC960A14))
                    : low
                        ? ArgbToImU32(unchecked((int)0xCCB84200))
                        : ArgbToImU32(unchecked((int)0x99235E42));

        if (hpPct > 0.001f)
            dl.AddRectFilled(topLeft, new Vector2(fillRight, barBottom), fillCol, 4f);

        if (member.TempHitPoints > 0 && member.MaxHitPoints > 0 && !member.IsTrulyDead)
        {
            float thpFrac = (float)Math.Clamp(member.TempHitPoints / (double)member.MaxHitPoints, 0.04, 0.35);
            float capW = Math.Min(width * thpFrac, Math.Max(6f, width - (fillRight - topLeft.X)));
            float capX = Math.Min(bottomRight.X - capW, fillRight);
            dl.AddRectFilled(new Vector2(capX, topLeft.Y + 2f), new Vector2(capX + capW, barBottom - 2f), ArgbToImU32(unchecked((int)0xDDF5C542)), 2f);
        }

        // Astral Blue SP micro-sliver along bottom of card
        if (member.MaxSpellPoints > 0)
        {
            float spPct = (float)Math.Clamp(member.SpellPoints / (double)member.MaxSpellPoints, 0.0, 1.0);
            dl.AddRectFilled(new Vector2(topLeft.X + 2f, bottomRight.Y - 3f), new Vector2(bottomRight.X - 2f, bottomRight.Y - 1f), ArgbToImU32(unchecked((int)0xAA10131A)));
            if (spPct > 0.001f)
                dl.AddRectFilled(new Vector2(topLeft.X + 2f, bottomRight.Y - 3f), new Vector2(topLeft.X + 2f + ((width - 4f) * spPct), bottomRight.Y - 1f), ArgbToImU32(unchecked((int)0xFF4DA6FF)));
        }

        // Border with calm-vs-urgent <=1.5Hz pulse for Incap / Low HP
        uint borderCol;
        float borderThick = 1f;
        if (member.IsIncapacitated && !muted)
        {
            borderCol = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.25f + (0.55f * pulse), 0.28f, 1f));
            borderThick = 2f;
        }
        else if (member.IsTrulyDead && !muted)
        {
            borderCol = ArgbToImU32(BannerPresentation.SoulstoneBorderArgb);
            borderThick = 1.5f;
        }
        else if (low)
        {
            borderCol = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.72f + (0.18f * pulse), 0.28f, 1f));
            borderThick = 1.5f;
        }
        else
        {
            borderCol = ImGui.ColorConvertFloat4ToU32(HairlineBorder);
        }
        dl.AddRect(topLeft, bottomRight, borderCol, 4f, borderThick);

        // CVD-safe badge + name + HP/THP text
        string badge = muted ? "[MUTE] "
            : !member.Connected ? "[OFF] "
            : member.IsTrulyDead ? "[X DEAD] "
            : member.IsIncapacitated ? "[+ INCAP] "
            : low ? "[v LOW] "
            : "";

        string displayName = member.Name.Length > 14 ? member.Name[..14] : member.Name;
        string leftLabel = $"{badge}{displayName}";
        string rightLabel = member.IsTrulyDead
            ? "RAISE"
            : member.TempHitPoints > 0
                ? string.Create(CultureInfo.InvariantCulture, $"{member.Percent:0}% +{member.TempHitPoints}")
                : string.Create(CultureInfo.InvariantCulture, $"{member.Percent:0}%");

        uint labelCol = muted || !member.Connected ? ImGui.ColorConvertFloat4ToU32(TextMuted) : ImGui.ColorConvertFloat4ToU32(TextPrimary);
        ImFontPtr font = ImGui.GetFont();
        Vector2 rightSize = MeasureText(font, 13f, rightLabel);

        dl.AddText(font, 13f, new Vector2(topLeft.X + 7f, topLeft.Y + 7f), labelCol, leftLabel);
        dl.AddText(font, 13f, new Vector2(bottomRight.X - rightSize.X - 7f, topLeft.Y + 7f), labelCol, rightLabel);

        if (hovered)
        {
            string slotText = member.PartySlotIndex == 0 ? "Self" : member.PartySlotIndex > 0 ? $"Slot F{member.PartySlotIndex}" : "Focus";
            string thpLine = member.TempHitPoints > 0 ? $" (+{member.TempHitPoints} THP)" : "";
            string spLine = member.MaxSpellPoints > 0 ? $"\nSP: {member.SpellPoints}/{member.MaxSpellPoints}" : "";
            string stateLine = member.IsTrulyDead ? "Dead (Soulstone - Raise Dead)"
                : member.IsIncapacitated ? "Incapacitated (Healable!)"
                : !member.Connected ? "Disconnected"
                : muted ? "Muted (session)"
                : "Alive";
            ImGui.SetTooltip(
                $"{member.Name} [{slotText}] — {stateLine}\nHP: {member.HitPoints}/{member.MaxHitPoints}{thpLine}{spLine}\nLeft-Click: Targetless Heal/Raise • Right-Click: Select (Shift: Focus) • Middle-Click: Mute");
        }
    }

    private void RenderMainWindow(AlertBannerView view, Settings live, WatchedMemberSnapshot[] members, double now)
    {
        PushEberronTheme();
        try
        {
            ImGui.SetNextWindowSize(new Vector2(640, 560), ImGuiCond.FirstUseEver);
            bool visible = _plugin.WindowVisible;
            try
            {
                if (!ImGui.Begin("Healers Helper — Eberron Triage & Settings###HH_MainWindow", ref visible, ImGuiWindowFlags.NoCollapse))
                {
                    _plugin.WindowVisible = visible;
                    return;
                }
                _plugin.WindowVisible = visible;

                // Live status strip
                int saved = Math.Max(0, view.PeopleSaved);
                string rank = HealerStats.RankForSaves(saved);
                ImGui.TextColored(live.PluginEnabled ? EmeraldHealthy : CrimsonAlert, live.PluginEnabled ? "Plugin: ON" : "Plugin: OFF");
                ImGui.SameLine(0f, 16f);
                ImGui.TextColored(live.HudEnabled ? EmeraldHealthy : AmberWarn, live.HudEnabled ? "Banner: ON" : "Banner: OFF");
                ImGui.SameLine(0f, 16f);
                ImGui.TextColored(live.RaidHudEnabled ? EmeraldHealthy : TextMuted, live.RaidHudEnabled ? "Raid HUD: ON" : "Raid HUD: OFF");
                ImGui.SameLine(0f, 16f);
                ImGui.TextColored(SovereignGold, $"Saved: {saved} ({rank})");

                ImGui.TextColored(TextMuted, FormatGamificationSummary(live));
                if (!string.IsNullOrWhiteSpace(view.StatusLine))
                    ImGui.TextColored(TextPrimary, $"{view.StatusLine}  |  {view.TargetingStatus}");

                ImGui.Separator();

                // Quick action row
                if (ImGui.Button(live.PluginEnabled ? "Disable Plugin" : "Enable Plugin"))
                {
                    _engine.TogglePluginEnabled();
                    _draft.PluginEnabled = _engine.GetSettings().PluginEnabled;
                    _pluginEnabledDirty = false;
                }
                ImGui.SameLine();
                if (ImGui.Button(live.HudEnabled ? "Banner: ON" : "Banner: OFF"))
                {
                    _engine.ToggleBannerHud();
                    _draft.HudEnabled = _engine.GetSettings().HudEnabled;
                    _hudEnabledDirty = false;
                }
                ImGui.SameLine();
                if (ImGui.Button("Cycle Mode"))
                {
                    _engine.CycleMode();
                    _draft.Mode = _engine.GetSettings().Mode;
                    _modeDirty = false;
                }
                ImGui.SameLine();
                if (ImGui.Button("Focus Target")) _engine.FocusCurrentTarget();
                ImGui.SameLine();
                if (ImGui.Button("Target Critical")) _engine.TargetCriticalNow();
                ImGui.SameLine();
                if (ImGui.Button("Quick-Cast"))
                {
                    _engine.QuickCastCriticalNow(out string res);
                    _settingsStatus = res;
                }

                if (ImGui.Button("Preview Banner")) _previewStart = now;
                ImGui.SameLine();
                if (ImGui.Button("Move / Resize Banner")) _bannerEditUntil = now + EditDurationSeconds;
                ImGui.SameLine();
                if (ImGui.Button("Move Status")) _statusEditUntil = now + EditDurationSeconds;
                ImGui.SameLine();
                if (ImGui.Button("Copy Chat Card"))
                {
                    _engine.CopyChatScorecard();
                    _settingsStatus = "Copied ASCII scorecard to clipboard.";
                }
                ImGui.SameLine();
                if (ImGui.Button("Reset Saved"))
                {
                    _engine.ResetPeopleSaved();
                    _settingsStatus = "Reset People Saved and healer records to 0.";
                }

                ImGui.Separator();

                // 5 Accent-Underlined Settings Tabs
                if (ImGui.BeginTabBar("HH_SettingsTabs"))
                {
                    try
                    {
                        if (ImGui.BeginTabItem("Triage"))
                        {
                            try { RenderTriageTab(members); }
                            finally { ImGui.EndTabItem(); }
                        }
                        if (ImGui.BeginTabItem("Saves"))
                        {
                            try { RenderSavesTab(live); }
                            finally { ImGui.EndTabItem(); }
                        }
                        if (ImGui.BeginTabItem("HUD & Colors"))
                        {
                            try { RenderHudAndColorsTab(); }
                            finally { ImGui.EndTabItem(); }
                        }
                        if (ImGui.BeginTabItem("Targeting & Quick-Cast"))
                        {
                            try { RenderTargetingAndQuickCastTab(); }
                            finally { ImGui.EndTabItem(); }
                        }
                        if (ImGui.BeginTabItem("Hotkeys"))
                        {
                            try { RenderHotkeysTab(); }
                            finally { ImGui.EndTabItem(); }
                        }
                    }
                    finally
                    {
                        ImGui.EndTabBar();
                    }
                }

                ImGui.Separator();
                ImGui.TextColored(SovereignGold, _settingsStatus);

                if (ImGui.Button("Save Settings", new Vector2(130, 28)))
                {
                    SaveDraftSettings();
                }
                ImGui.SameLine();
                if (ImGui.Button("Revert", new Vector2(90, 28)))
                {
                    _draft = _engine.GetSettings();
                    _draftRevision = _engine.SettingsRevision;
                    ClearDirtyFlags();
                    _recordingHotkeyIndex = -1;
                    _settingsStatus = "Reverted unsaved changes.";
                }
            }
            finally
            {
                ImGui.End();
            }
        }
        finally
        {
            PopEberronTheme();
        }
    }

    private void ClearDirtyFlags()
    {
        _draftDirty = false;
        _pluginEnabledDirty = false;
        _hudEnabledDirty = false;
        _modeDirty = false;
        _raidHudDirty = false;
        _positionsResetPending = false;
        _focusEditedInDraft = false;
        _fontOrOpacityEditedInDraft = false;
    }

    private void RenderTriageTab(WatchedMemberSnapshot[] members)
    {
        bool enabled = _draft.PluginEnabled;
        if (ImGui.Checkbox("Plugin enabled (master)", ref enabled))
        {
            _draft.PluginEnabled = enabled;
            _pluginEnabledDirty = true;
            _draftDirty = true;
        }
        ImGui.SameLine(0f, 18f);
        int modeIdx = (int)_draft.Mode;
        ImGui.PushItemWidth(140f);
        try
        {
            if (ImGui.Combo("Tracking Mode", ref modeIdx, ModeLabels, ModeLabels.Length))
            {
                _draft.Mode = (TrackingMode)modeIdx;
                _modeDirty = true;
                _draftDirty = true;
            }
        }
        finally
        {
            ImGui.PopItemWidth();
        }

        bool includeSelf = _draft.IncludeSelfInPartyMode;
        if (ImGui.Checkbox("Include self in Party mode", ref includeSelf))
        {
            _draft.IncludeSelfInPartyMode = includeSelf;
            _draftDirty = true;
        }
        ImGui.SameLine(0f, 16f);
        bool includeTempHp = _draft.IncludeTempHpInThreshold;
        if (ImGui.Checkbox("Count Temp HP in alert threshold", ref includeTempHp))
        {
            _draft.IncludeTempHpInThreshold = includeTempHp;
            _draftDirty = true;
        }

        bool suppressTown = _draft.SuppressAlertsInTown;
        if (ImGui.Checkbox("Suppress alerts in town", ref suppressTown))
        {
            _draft.SuppressAlertsInTown = suppressTown;
            _draftDirty = true;
        }

        ImGui.PushItemWidth(160f);
        try
        {
            int hpPct = _draft.HpThresholdPercent;
            if (ImGui.SliderInt("Alert HP %", ref hpPct, 5, 95))
            {
                _draft.HpThresholdPercent = hpPct;
                _draftDirty = true;
            }

            int hyst = _draft.RecoveryHysteresisPercent;
            if (ImGui.SliderInt("Recovery Hysteresis %", ref hyst, 0, 20))
            {
                _draft.RecoveryHysteresisPercent = hyst;
                _draftDirty = true;
            }

            int deadSec = _draft.DeadAlertSeconds;
            if (ImGui.SliderInt("Dead / Soulstone Alert (s)", ref deadSec, 0, 60))
            {
                _draft.DeadAlertSeconds = deadSec;
                _draftDirty = true;
            }

            int maxAlert = _draft.MaxAlertSeconds;
            if (ImGui.SliderInt("Max Alert Timeout (s, 0=inf)", ref maxAlert, 0, 120))
            {
                _draft.MaxAlertSeconds = maxAlert;
                _draftDirty = true;
            }
        }
        finally
        {
            ImGui.PopItemWidth();
        }

        ImGui.Separator();
        ImGui.TextColored(SovereignGold, "Party / Raid Roster (click Mute for Pale Master undead shrouds / self-sufficient tanks):");
        var mutedNames = _engine.GetMutedPlayerNames();
        if (mutedNames.Count > 0)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear All Mutes"))
                _engine.ClearMutedPlayers();
        }

        if (members.Length == 0)
        {
            ImGui.TextColored(TextMuted, "No party or character data yet.");
            return;
        }

        for (int i = 0; i < members.Length; i++)
        {
            var m = members[i];
            ImGui.PushID(i);
            try
            {
                bool isMuted = _engine.IsPlayerMuted(m.Id, m.Name);
                string slot = m.PartySlotIndex == 0 ? "Self" : m.PartySlotIndex > 0 ? $"F{m.PartySlotIndex}" : "Ext";
                string state = m.IsTrulyDead ? "DEAD (Soulstone)"
                    : m.IsIncapacitated ? "INCAP (Healable)"
                    : !m.Connected ? "OFFLINE"
                    : string.Create(CultureInfo.InvariantCulture, $"{m.Percent:0}%");
                string thp = m.TempHitPoints > 0 ? $" +{m.TempHitPoints} THP" : "";

                Vector4 col = isMuted ? TextMuted
                    : m.IsTrulyDead ? SpectralViolet
                    : m.IsIncapacitated ? CrimsonAlert
                    : EmeraldHealthy;

                if (ImGui.SmallButton(isMuted ? "Unmute" : "Mute"))
                    _engine.ToggleMutePlayer(m.Id, m.Name);
                ImGui.SameLine();
                if (ImGui.SmallButton("Focus"))
                {
                    _engine.FocusMember(m);
                    _draft.Mode = TrackingMode.SinglePlayer;
                    _draft.FocusedPlayerId = m.Id;
                    _draft.FocusedPlayerName = m.Name;
                    _focusEditedInDraft = true;
                    _modeDirty = true;
                }
                ImGui.SameLine();
                ImGui.TextColored(col, $"[{slot}] {m.Name}: {m.HitPoints}/{m.MaxHitPoints}{thp} ({state}){(isMuted ? " [MUTED]" : "")}");
            }
            finally
            {
                ImGui.PopID();
            }
        }
    }

    private void RenderSavesTab(Settings live)
    {
        ImGui.PushItemWidth(160f);
        try
        {
            float saveWin = (float)_draft.SaveWindowSeconds;
            if (ImGui.SliderFloat("Save Window (s)", ref saveWin, 1.0f, 15.0f, "%.1f"))
            {
                _draft.SaveWindowSeconds = Math.Round(saveWin, 1);
                _draftDirty = true;
            }

            float castWin = (float)_draft.SelfCastWindowSeconds;
            if (ImGui.SliderFloat("Self-Cast Attribution Window (s)", ref castWin, 1.0f, 15.0f, "%.1f"))
            {
                _draft.SelfCastWindowSeconds = Math.Round(castWin, 1);
                _draftDirty = true;
            }

            int minHeal = _draft.MinHealDelta;
            if (ImGui.SliderInt("Min Heal Delta (HP)", ref minHeal, 1, 200))
            {
                _draft.MinHealDelta = minHeal;
                _draftDirty = true;
            }
        }
        finally
        {
            ImGui.PopItemWidth();
        }

        bool showNotice = _draft.ShowSavedNotice;
        if (ImGui.Checkbox("Show 'SAVED!' banner notice", ref showNotice))
        {
            _draft.ShowSavedNotice = showNotice;
            _draftDirty = true;
        }

        bool showRank = _draft.ShowRankOnStatusHud;
        if (ImGui.Checkbox("Show prestige rank on Status HUD", ref showRank))
        {
            _draft.ShowRankOnStatusHud = showRank;
            _draftDirty = true;
        }

        ImGui.Separator();
        ImGui.TextColored(SovereignGold, "Live Healer Records:");
        ImGui.Text($"People Saved: {live.PeopleSaved} ({HealerStats.RankForSaves(live.PeopleSaved)})");
        ImGui.Text($"Current Streak: {live.CurrentSaveStreak}  |  Best Streak: {live.BestSaveStreak}  |  1% Club: {live.OnePercentClubSaves}");
        if (live.ClosestCallPreHealHp > 0 && !string.IsNullOrWhiteSpace(live.ClosestCallPlayerName))
            ImGui.Text($"Closest Call: {live.ClosestCallPlayerName} at {live.ClosestCallPreHealPercent:0.#}% ({live.ClosestCallPreHealHp} HP)");
        if (live.ReflexSampleCount > 0 && live.BestReflexMs > 0)
            ImGui.Text($"Reflex: {live.AvgReflexMs} ms avg ({live.BestReflexMs} ms best, {live.ReflexSampleCount} samples)");
        if (live.BiggestOverkillSaved > 0)
            ImGui.Text($"Biggest Overkill Prevented: +{live.BiggestOverkillSaved} HP");
    }

    private void RenderHudAndColorsTab()
    {
        bool hudEnabled = _draft.HudEnabled;
        if (ImGui.Checkbox("Enable center alert banner", ref hudEnabled))
        {
            _draft.HudEnabled = hudEnabled;
            _hudEnabledDirty = true;
            _draftDirty = true;
        }
        ImGui.SameLine(0f, 16f);
        bool raidHud = _draft.RaidHudEnabled;
        if (ImGui.Checkbox("Enable Triage Raid HUD (always-on)", ref raidHud))
        {
            _draft.RaidHudEnabled = raidHud;
            _raidHudDirty = true;
            _draftDirty = true;
        }

        bool flash = _draft.FlashBanner;
        if (ImGui.Checkbox("Flash banner", ref flash))
        {
            _draft.FlashBanner = flash;
            _draftDirty = true;
        }
        ImGui.SameLine(0f, 16f);
        bool hpBar = _draft.ShowBannerHpBar;
        if (ImGui.Checkbox("Show spatial HP bar strip", ref hpBar))
        {
            _draft.ShowBannerHpBar = hpBar;
            _draftDirty = true;
        }
        ImGui.SameLine(0f, 16f);
        bool sound = _draft.SoundOnAlert;
        if (ImGui.Checkbox("Sound on alert", ref sound))
        {
            _draft.SoundOnAlert = sound;
            _draftDirty = true;
        }

        bool statusHud = _draft.StatusHudEnabled;
        if (ImGui.Checkbox("Status HUD enabled", ref statusHud))
        {
            _draft.StatusHudEnabled = statusHud;
            _draftDirty = true;
        }

        ImGui.PushItemWidth(240f);
        try
        {
            int themeIdx = Math.Clamp((int)_draft.Theme, 0, ThemeLabels.Length - 1);
            if (ImGui.Combo("UI Theme (DDO x M3)", ref themeIdx, ThemeLabels, ThemeLabels.Length))
            {
                _draft.Theme = (UiTheme)themeIdx;
                _activePalette = UiThemePalette.For(_draft.Theme);
                _draftDirty = true;
            }
        }
        finally
        {
            ImGui.PopItemWidth();
        }

        ImGui.PushItemWidth(160f);
        try
        {
            int fontPx = _draft.FontSizePixels;
            if (ImGui.SliderInt("Banner Font (18-140 px)", ref fontPx, 18, 140))
            {
                _draft.FontSizePixels = fontPx;
                _fontOrOpacityEditedInDraft = true;
                _draftDirty = true;
            }

            int opacity = _draft.BannerOpacity;
            if (ImGui.SliderInt("Banner Opacity (20-255)", ref opacity, 20, 255))
            {
                _draft.BannerOpacity = opacity;
                _fontOrOpacityEditedInDraft = true;
                _draftDirty = true;
            }

            int flashMs = _draft.FlashIntervalMs;
            if (ImGui.SliderInt("Flash Interval (ms)", ref flashMs, 100, 1000))
            {
                _draft.FlashIntervalMs = flashMs;
                _draftDirty = true;
            }

            int statusPx = _draft.StatusFontSizePixels;
            if (ImGui.SliderInt("Status HUD Font (9-48 px)", ref statusPx, 9, 48))
            {
                _draft.StatusFontSizePixels = statusPx;
                _draftDirty = true;
            }
        }
        finally
        {
            ImGui.PopItemWidth();
        }

        ImGui.Separator();
        ImGui.TextColored(SovereignGold, "Banner Colors (6 Swatches):");
        _draft.LowHpBgHex = RenderHexColorPicker("Low HP Background", _draft.LowHpBgHex, Settings.DefaultLowHpBgHex);
        _draft.LowHpBorderHex = RenderHexColorPicker("Low HP Border", _draft.LowHpBorderHex, Settings.DefaultLowHpBorderHex);
        _draft.CriticalBgHex = RenderHexColorPicker("Critical / Incap Background", _draft.CriticalBgHex, Settings.DefaultCriticalBgHex);
        _draft.CriticalBorderHex = RenderHexColorPicker("Critical / Incap Border", _draft.CriticalBorderHex, Settings.DefaultCriticalBorderHex);
        _draft.BannerTextHex = RenderHexColorPicker("Banner Text", _draft.BannerTextHex, Settings.DefaultBannerTextHex);
        _draft.FlashAltBgHex = RenderHexColorPicker("Flash Alternate Background", _draft.FlashAltBgHex, Settings.DefaultFlashAltBgHex);

        if (ImGui.Button("Reset Colors"))
        {
            _draft.ResetBannerColors();
            _draftDirty = true;
            _settingsStatus = "Banner colors restored to defaults. Click Save to apply.";
        }
        ImGui.SameLine();
        if (ImGui.Button("Reset HUD Positions"))
        {
            _draft.HudX = 0.50;
            _draft.HudY = 0.42;
            _draft.StatusHudX = 0.50;
            _draft.StatusHudY = 0.76;
            _draft.RaidHudX = 0.02;
            _draft.RaidHudY = 0.25;
            _positionsResetPending = true;
            _draftDirty = true;
            _settingsStatus = "HUD positions reset to defaults. Click Save to apply.";
        }
    }

    private void RenderTargetingAndQuickCastTab()
    {
        bool autoTarget = _draft.AutoTargetOnAlert;
        if (ImGui.Checkbox("Auto-target critical player on alert", ref autoTarget))
        {
            _draft.AutoTargetOnAlert = autoTarget;
            _draftDirty = true;
        }

        ImGui.PushItemWidth(160f);
        try
        {
            int methodIdx = (int)_draft.TargetingMethod;
            if (ImGui.Combo("Targeting Method", ref methodIdx, TargetMethodLabels, TargetMethodLabels.Length))
            {
                _draft.TargetingMethod = (TargetMethod)methodIdx;
                _draftDirty = true;
            }

            float cd = (float)_draft.AutoTargetCooldownSeconds;
            if (ImGui.SliderFloat("Auto-Target Cooldown (s)", ref cd, 0.5f, 30.0f, "%.1f"))
            {
                _draft.AutoTargetCooldownSeconds = Math.Round(cd, 1);
                _draftDirty = true;
            }

            bool restore = _draft.RestorePreviousTargetAfterHeal;
            if (ImGui.Checkbox("Restore previous target once healed", ref restore))
            {
                _draft.RestorePreviousTargetAfterHeal = restore;
                _draftDirty = true;
            }

            float delay = (float)_draft.RestoreTargetDelaySeconds;
            if (ImGui.SliderFloat("Restore Delay (s)", ref delay, 0.0f, 5.0f, "%.1f"))
            {
                _draft.RestoreTargetDelaySeconds = Math.Round(delay, 1);
                _draftDirty = true;
            }
        }
        finally
        {
            ImGui.PopItemWidth();
        }

        ImGui.TextColored(SovereignGold, _engine.GetRaidKeymapSummary());

        ImGui.Separator();
        ImGui.TextColored(SovereignGold, "DH 5 Targetless Quick-Cast (InvokeHotbar — never drops Boss/Tank target):");
        ImGui.TextColored(TextMuted, "Set Bar to 0 to disable. Left-click any Triage Raid HUD row or use /hh heal.");

        ImGui.PushItemWidth(130f);
        try
        {
            int healBar = _draft.QuickHealHotbar;
            if (ImGui.SliderInt("Quick-Heal Bar (0=Off, 1-20)", ref healBar, 0, 20))
            {
                _draft.QuickHealHotbar = healBar;
                _draftDirty = true;
            }
            ImGui.SameLine(0f, 12f);
            int healSlot = _draft.QuickHealSlot;
            if (ImGui.SliderInt("Heal Slot (1-10)", ref healSlot, 1, 10))
            {
                _draft.QuickHealSlot = healSlot;
                _draftDirty = true;
            }

            int raiseBar = _draft.QuickRaiseHotbar;
            if (ImGui.SliderInt("Quick-Raise Bar (0=Off, 1-20)", ref raiseBar, 0, 20))
            {
                _draft.QuickRaiseHotbar = raiseBar;
                _draftDirty = true;
            }
            ImGui.SameLine(0f, 12f);
            int raiseSlot = _draft.QuickRaiseSlot;
            if (ImGui.SliderInt("Raise Slot (1-10)", ref raiseSlot, 1, 10))
            {
                _draft.QuickRaiseSlot = raiseSlot;
                _draftDirty = true;
            }
        }
        finally
        {
            ImGui.PopItemWidth();
        }
    }

    private void RenderHotkeysTab()
    {
        ImGui.TextColored(TextMuted, "Click a hotkey button, press the new key chord (Esc cancels, Backspace clears).");

        RenderHotkeyRow(0, "Toggle Plugin ON/OFF", _draft.TogglePluginHotkey, b => _draft.TogglePluginHotkey = b);
        RenderHotkeyRow(1, "Focus Current Target", _draft.FocusTargetHotkey, b => _draft.FocusTargetHotkey = b);
        RenderHotkeyRow(2, "Cycle Mode", _draft.ToggleModeHotkey, b => _draft.ToggleModeHotkey = b);
        RenderHotkeyRow(3, "Target Critical Now", _draft.TargetCriticalHotkey, b => _draft.TargetCriticalHotkey = b);
        RenderHotkeyRow(4, "Quick-Cast Critical (Targetless)", _draft.QuickCastCriticalHotkey, b => _draft.QuickCastCriticalHotkey = b);
        RenderHotkeyRow(5, "Reset People Saved", _draft.ResetSavedHotkey, b => _draft.ResetSavedHotkey = b);
        RenderHotkeyRow(6, "Copy Chat Scorecard", _draft.CopyScorecardHotkey, b => _draft.CopyScorecardHotkey = b);
        RenderHotkeyRow(7, "Toggle Center Banner", _draft.ToggleBannerHotkey, b => _draft.ToggleBannerHotkey = b);

        if (ImGui.Button("Reset Hotkey Defaults"))
        {
            _draft.TogglePluginHotkey = Settings.DefaultTogglePluginHotkey;
            _draft.FocusTargetHotkey = Settings.DefaultFocusTargetHotkey;
            _draft.ToggleModeHotkey = Settings.DefaultToggleModeHotkey;
            _draft.TargetCriticalHotkey = Settings.DefaultTargetCriticalHotkey;
            _draft.QuickCastCriticalHotkey = HotkeyBinding.Empty;
            _draft.ResetSavedHotkey = HotkeyBinding.Empty;
            _draft.CopyScorecardHotkey = HotkeyBinding.Empty;
            _draft.ToggleBannerHotkey = HotkeyBinding.Empty;
            _recordingHotkeyIndex = -1;
            _draftDirty = true;
            _settingsStatus = "Hotkeys restored to defaults. Click Save to apply.";
        }

        ImGui.Separator();
        ImGui.TextColored(SovereignGold, "In-Game Chat Commands (/hh or /healershelper):");
        ImGui.TextColored(TextMuted, "/hh on|off  •  /hh party  •  /hh focus  •  /hh hp <pct>  •  /hh banner  •  /hh raid");
        ImGui.TextColored(TextMuted, "/hh theme <eberron|cannith|feywild>  •  /hh heal  •  /hh target  •  /hh mute <name|clear>  •  /hh score");
    }

    private void RenderHotkeyRow(int index, string label, HotkeyBinding current, Action<HotkeyBinding> setter)
    {
        bool recording = _recordingHotkeyIndex == index;
        string buttonText = recording ? "Press keys... (Esc cancels)" : current.Format();

        ImGui.AlignTextToFramePadding();
        ImGui.Text(label);
        ImGui.SameLine(230f);
        if (ImGui.Button($"{buttonText}###hk_{index}", new Vector2(180f, 0f)))
        {
            _recordingHotkeyIndex = recording ? -1 : index;
        }
        ImGui.SameLine();
        if (ImGui.Button($"Clear###hk_clear_{index}"))
        {
            setter(HotkeyBinding.Empty);
            _draftDirty = true;
            if (recording) _recordingHotkeyIndex = -1;
        }

        if (recording)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.Escape, false))
            {
                _recordingHotkeyIndex = -1;
            }
            else if (ImGui.IsKeyPressed(ImGuiKey.Backspace, false) && !ImGui.GetIO().KeyCtrl && !ImGui.GetIO().KeyShift && !ImGui.GetIO().KeyAlt)
            {
                setter(HotkeyBinding.Empty);
                _draftDirty = true;
                _recordingHotkeyIndex = -1;
            }
            else if (TryCaptureImGuiHotkey(out var captured))
            {
                setter(captured);
                _draftDirty = true;
                _recordingHotkeyIndex = -1;
            }
        }
    }

    private void SaveDraftSettings()
    {
        var allHotkeys = new (string Name, HotkeyBinding Binding)[]
        {
            ("Toggle Plugin ON/OFF", _draft.TogglePluginHotkey),
            ("Focus Current Target", _draft.FocusTargetHotkey),
            ("Cycle Mode", _draft.ToggleModeHotkey),
            ("Target Critical Now", _draft.TargetCriticalHotkey),
            ("Quick-Cast Critical", _draft.QuickCastCriticalHotkey),
            ("Reset People Saved", _draft.ResetSavedHotkey),
            ("Copy Chat Scorecard", _draft.CopyScorecardHotkey),
            ("Toggle Center Banner", _draft.ToggleBannerHotkey)
        };

        var seen = new Dictionary<HotkeyBinding, string>(allHotkeys.Length);
        foreach (var (name, binding) in allHotkeys)
        {
            if (binding.IsEmpty) continue;
            if (seen.TryGetValue(binding, out string? existing))
            {
                _settingsStatus = $"Duplicate hotkey '{binding.Format()}' on '{existing}' and '{name}'. Not saved.";
                return;
            }
            seen[binding] = name;
        }

        // Preserve live stats, HUD coordinates, focus target, wheel-edited font/opacity, and untouched toggles.
        var live = _engine.GetSettings();
        var toSave = _draft.Copy();
        toSave.CopyLiveStatsFrom(live);
        toSave.PluginEnabled = _pluginEnabledDirty ? _draft.PluginEnabled : live.PluginEnabled;
        toSave.HudEnabled = _hudEnabledDirty ? _draft.HudEnabled : live.HudEnabled;
        toSave.Mode = _modeDirty ? _draft.Mode : live.Mode;
        toSave.RaidHudEnabled = _raidHudDirty ? _draft.RaidHudEnabled : live.RaidHudEnabled;
        if (!_positionsResetPending)
        {
            toSave.HudX = live.HudX;
            toSave.HudY = live.HudY;
            toSave.StatusHudX = live.StatusHudX;
            toSave.StatusHudY = live.StatusHudY;
            toSave.RaidHudX = live.RaidHudX;
            toSave.RaidHudY = live.RaidHudY;
        }
        if (!_focusEditedInDraft)
        {
            toSave.FocusedPlayerId = live.FocusedPlayerId;
            toSave.FocusedPlayerName = live.FocusedPlayerName;
        }
        if (!_fontOrOpacityEditedInDraft)
        {
            toSave.FontSizePixels = live.FontSizePixels;
            toSave.BannerOpacity = live.BannerOpacity;
        }

        _engine.SaveSettings(toSave);
        _draft = _engine.GetSettings();
        _draftRevision = _engine.SettingsRevision;
        ClearDirtyFlags();
        _recordingHotkeyIndex = -1;
        _settingsStatus = "Settings saved.";
    }

    private string RenderHexColorPicker(string label, string currentHex, string fallbackHex)
    {
        string clean = Settings.NormalizeHexColor(currentHex, fallbackHex);
        Settings.TryParseHexRgb(clean, out int r, out int g, out int b);
        Vector3 col = new(r / 255f, g / 255f, b / 255f);
        ImGui.PushItemWidth(180f);
        try
        {
            // Use ### so dynamic hex text does not mutate ImGuiID mid-drag!
            if (ImGui.ColorEdit3($"{label} ({clean})###hex_{label}", ref col))
            {
                int nr = Math.Clamp((int)Math.Round(col.X * 255f), 0, 255);
                int ng = Math.Clamp((int)Math.Round(col.Y * 255f), 0, 255);
                int nb = Math.Clamp((int)Math.Round(col.Z * 255f), 0, 255);
                clean = string.Create(CultureInfo.InvariantCulture, $"#{nr:X2}{ng:X2}{nb:X2}");
                _draftDirty = true;
            }
        }
        finally
        {
            ImGui.PopItemWidth();
        }
        return clean;
    }

    private static bool TryCaptureImGuiHotkey(out HotkeyBinding binding)
    {
        var io = ImGui.GetIO();
        bool ctrl = io.KeyCtrl;
        bool shift = io.KeyShift;
        bool alt = io.KeyAlt;

        for (int i = 0; i < 26; i++)
        {
            if (ImGui.IsKeyPressed((ImGuiKey)((int)ImGuiKey.A + i), false))
            {
                binding = new HotkeyBinding(0x41 + i, ctrl, shift, alt);
                return true;
            }
        }
        for (int i = 0; i < 10; i++)
        {
            if (ImGui.IsKeyPressed((ImGuiKey)((int)ImGuiKey._0 + i), false))
            {
                binding = new HotkeyBinding(0x30 + i, ctrl, shift, alt);
                return true;
            }
            if (ImGui.IsKeyPressed((ImGuiKey)((int)ImGuiKey.Keypad0 + i), false))
            {
                binding = new HotkeyBinding(0x60 + i, ctrl, shift, alt);
                return true;
            }
        }
        for (int i = 0; i < 12; i++)
        {
            if (ImGui.IsKeyPressed((ImGuiKey)((int)ImGuiKey.F1 + i), false))
            {
                binding = new HotkeyBinding(0x70 + i, ctrl, shift, alt);
                return true;
            }
        }

        (ImGuiKey Key, int Vk)[] extras =
        [
            (ImGuiKey.Space, 0x20), (ImGuiKey.Insert, 0x2D), (ImGuiKey.Delete, 0x2E),
            (ImGuiKey.Home, 0x24), (ImGuiKey.End, 0x23), (ImGuiKey.PageUp, 0x21), (ImGuiKey.PageDown, 0x22),
            (ImGuiKey.Semicolon, 0xBA), (ImGuiKey.Equal, 0xBB), (ImGuiKey.Comma, 0xBC),
            (ImGuiKey.Minus, 0xBD), (ImGuiKey.Period, 0xBE), (ImGuiKey.Slash, 0xBF),
            (ImGuiKey.GraveAccent, 0xC0), (ImGuiKey.LeftBracket, 0xDB), (ImGuiKey.Backslash, 0xDC),
            (ImGuiKey.RightBracket, 0xDD), (ImGuiKey.Apostrophe, 0xDE)
        ];

        foreach (var (key, vk) in extras)
        {
            if (ImGui.IsKeyPressed(key, false))
            {
                binding = new HotkeyBinding(vk, ctrl, shift, alt);
                return true;
            }
        }

        binding = HotkeyBinding.Empty;
        return false;
    }

    private static string FormatGamificationSummary(Settings s)
    {
        var parts = new List<string>(4)
        {
            string.Create(CultureInfo.InvariantCulture, $"Streak: {s.CurrentSaveStreak} (Best {s.BestSaveStreak})"),
            string.Create(CultureInfo.InvariantCulture, $"1% Club: {s.OnePercentClubSaves}")
        };
        if (s.ClosestCallPreHealHp > 0 && !string.IsNullOrWhiteSpace(s.ClosestCallPlayerName))
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"Closest: {s.ClosestCallPlayerName} {s.ClosestCallPreHealPercent:0.#}% ({s.ClosestCallPreHealHp} HP)"));
        parts.Add(s.ReflexSampleCount > 0 && s.BestReflexMs > 0
            ? string.Create(CultureInfo.InvariantCulture, $"Reflex: {s.AvgReflexMs}ms avg ({s.BestReflexMs}ms best)")
            : "Reflex: --");
        return string.Join("  |  ", parts);
    }

    private static void DrawBannerResizeGrips(ImDrawListPtr dl, Vector2 tl, Vector2 br, float borderThickness, int gripArgb)
    {
        float inset = borderThickness + 4f;
        float arm = Math.Clamp(Math.Min(br.X - tl.X, br.Y - tl.Y) / 5f, 10f, 18f);
        uint col = ArgbToImU32(gripArgb);

        dl.AddLine(new Vector2(tl.X + inset, tl.Y + inset + arm), new Vector2(tl.X + inset, tl.Y + inset), col, 2.5f);
        dl.AddLine(new Vector2(tl.X + inset, tl.Y + inset), new Vector2(tl.X + inset + arm, tl.Y + inset), col, 2.5f);

        dl.AddLine(new Vector2(br.X - inset - arm, tl.Y + inset), new Vector2(br.X - inset, tl.Y + inset), col, 2.5f);
        dl.AddLine(new Vector2(br.X - inset, tl.Y + inset), new Vector2(br.X - inset, tl.Y + inset + arm), col, 2.5f);

        dl.AddLine(new Vector2(tl.X + inset, br.Y - inset - arm), new Vector2(tl.X + inset, br.Y - inset), col, 2.5f);
        dl.AddLine(new Vector2(tl.X + inset, br.Y - inset), new Vector2(tl.X + inset + arm, br.Y - inset), col, 2.5f);

        dl.AddLine(new Vector2(br.X - inset - arm, br.Y - inset), new Vector2(br.X - inset, br.Y - inset), col, 2.5f);
        dl.AddLine(new Vector2(br.X - inset, br.Y - inset - arm), new Vector2(br.X - inset, br.Y - inset), col, 2.5f);
    }

    private static Vector2 MeasureText(ImFontPtr font, float pixelSize, string text)
    {
        ImGui.PushFont(font, pixelSize);
        try
        {
            return ImGui.CalcTextSize(text);
        }
        finally
        {
            ImGui.PopFont();
        }
    }

    private static void DrawOutlinedText(ImDrawListPtr dl, ImFontPtr font, float size, Vector2 pos, uint col, uint shadowCol, string text)
    {
        float o = Math.Clamp(size * 0.045f, 1f, 3f);
        dl.AddText(font, size, new Vector2(pos.X - o, pos.Y), shadowCol, text);
        dl.AddText(font, size, new Vector2(pos.X + o, pos.Y), shadowCol, text);
        dl.AddText(font, size, new Vector2(pos.X, pos.Y - o), shadowCol, text);
        dl.AddText(font, size, new Vector2(pos.X, pos.Y + o), shadowCol, text);
        dl.AddText(font, size, pos, col, text);
    }

    private void PushEberronTheme()
    {
        ImGui.PushStyleColor(ImGuiCol.WindowBg, SurfaceBase);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, SurfaceContainer);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, SurfaceBase);
        ImGui.PushStyleColor(ImGuiCol.Border, HairlineBorder);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, SurfaceContainer);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, SurfaceControl);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, SurfaceHover);
        ImGui.PushStyleColor(ImGuiCol.TitleBg, SurfaceBase);
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, SurfaceContainer);
        ImGui.PushStyleColor(ImGuiCol.Button, SurfaceControl);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, SurfaceHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, SovereignGoldSoft);
        ImGui.PushStyleColor(ImGuiCol.Header, SurfaceControl);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, SurfaceHover);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, SovereignGoldSoft);
        ImGui.PushStyleColor(ImGuiCol.Tab, SurfaceContainer);
        ImGui.PushStyleColor(ImGuiCol.TabHovered, SurfaceHover);
        ImGui.PushStyleColor(ImGuiCol.TabSelected, SurfaceControl);
        ImGui.PushStyleColor(ImGuiCol.TabSelectedOverline, SovereignGold);
        ImGui.PushStyleColor(ImGuiCol.CheckMark, SovereignGold);
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, SovereignGold);
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, SovereignGold);
        ImGui.PushStyleColor(ImGuiCol.Text, TextPrimary);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.TabBarOverlineSize, 2f);
    }

    private static void PopEberronTheme()
    {
        ImGui.PopStyleVar(4);
        ImGui.PopStyleColor(23);
    }

    private static Vector4 HexToVec4(byte r, byte g, byte b, float a) =>
        new(r / 255f, g / 255f, b / 255f, a);

    private static Vector4 ArgbToVec4(int argb, float alphaOverride)
    {
        uint u = unchecked((uint)argb);
        float r = ((u >> 16) & 0xFFu) / 255f;
        float g = ((u >> 8) & 0xFFu) / 255f;
        float b = (u & 0xFFu) / 255f;
        return new Vector4(r, g, b, alphaOverride);
    }

    // Converts 0xAARRGGBB into Dear ImGui's packed 0xAABBGGRR format.
    private static uint ArgbToImU32(int argb)
    {
        uint u = unchecked((uint)argb);
        uint a = (u >> 24) & 0xFFu;
        uint r = (u >> 16) & 0xFFu;
        uint g = (u >> 8) & 0xFFu;
        uint b = u & 0xFFu;
        return (a << 24) | (b << 16) | (g << 8) | r;
    }

    public void Dispose()
    {
        _disposed = true;
        _recordingHotkeyIndex = -1;
        _engine.SetHotkeyCaptureActive(false);
    }
}
