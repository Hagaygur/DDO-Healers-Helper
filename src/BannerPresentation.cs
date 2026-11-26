using System;
using System.Globalization;

namespace VoK.HealersHelper;

internal enum BannerEditDragMode
{
    None = 0,
    Move = 1,
    ResizeLeft = 2,
    ResizeRight = 3,
    ResizeTop = 4,
    ResizeBottom = 5,
    ResizeTopLeft = 6,
    ResizeTopRight = 7,
    ResizeBottomLeft = 8,
    ResizeBottomRight = 9
}

internal readonly record struct BannerFrame(
    bool Visible,
    string PrimaryText,
    string SecondaryText,
    int FontSizePixels,
    bool FlashOn,
    int TextColorArgb,
    int BackgroundColorArgb,
    int BorderColorArgb)
{
    public bool IsSoulstone { get; init; }
    public double? HpBarPercent { get; init; }
    public int TempHitPoints { get; init; }
}

internal static class BannerPresentation
{
    public const int MinFontSizePixels = 18;
    public const int MaxFontSizePixels = 140;
    public const int MinFlashIntervalMs = 100;
    public const int MaxFlashIntervalMs = 1000;
    public const int MinBannerOpacity = 20;
    public const int MaxBannerOpacity = 255;
    public const int DefaultBannerOpacity = 235;
    public const int SoulstoneBgArgb = unchecked((int)0xEE2A1538);
    public const int SoulstoneBorderArgb = unchecked((int)0xFFD8DCE6);
    public const int SoulstoneTextArgb = unchecked((int)0xFFEFEAFF);
    public const int TempHpGoldArgb = unchecked((int)0xFFF5C542);

    public static BannerFrame Compute(
        AlertBannerView view,
        Settings settings,
        bool demo,
        bool editing,
        double elapsedAlertSeconds,
        double editSecondsRemaining)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(settings);

        int fontSize = Math.Clamp(settings.FontSizePixels, MinFontSizePixels, MaxFontSizePixels);
        int opacity = Math.Clamp(settings.BannerOpacity, MinBannerOpacity, MaxBannerOpacity);

        if (!settings.HudEnabled && !demo && !editing)
        {
            return new BannerFrame(false, "", "", fontSize, false, 0, 0, 0);
        }

        int flashIntervalMs = Math.Clamp(settings.FlashIntervalMs, MinFlashIntervalMs, MaxFlashIntervalMs);
        double elapsedMs = double.IsFinite(elapsedAlertSeconds) && elapsedAlertSeconds > 0
            ? elapsedAlertSeconds * 1000.0
            : 0.0;
        bool flashPhase = !settings.FlashBanner || ((long)Math.Floor(elapsedMs / flashIntervalMs) % 2 == 0);

        BannerSeverity effectiveSeverity = view.Severity;
        string primary = view.PrimaryText ?? "";
        string secondary = view.SecondaryText ?? "";

        if (demo && effectiveSeverity is not (BannerSeverity.LowHp or BannerSeverity.CriticalDown))
        {
            effectiveSeverity = BannerSeverity.CriticalDown;
            if (string.IsNullOrWhiteSpace(primary))
            {
                primary = "HEAL PARTY: PREVIEW (28%)";
            }
            if (string.IsNullOrWhiteSpace(secondary))
            {
                secondary = "PREVIEW ALERT • CENTER BANNER";
            }
        }

        if (editing && effectiveSeverity != BannerSeverity.None)
        {
            string dragSuffix = FormatEditCountdown(editSecondsRemaining, fontSize, opacity);
            secondary = string.IsNullOrWhiteSpace(secondary) ? dragSuffix : $"{secondary} • {dragSuffix}";
        }

        string textHex = Settings.NormalizeHexColor(settings.BannerTextHex, Settings.DefaultBannerTextHex);
        string flashAltHex = Settings.NormalizeHexColor(settings.FlashAltBgHex, Settings.DefaultFlashAltBgHex);

        switch (effectiveSeverity)
        {
            case BannerSeverity.CriticalDown:
            {
                if (view.IsSoulstone && !(demo && view.Severity != BannerSeverity.CriticalDown))
                {
                    // Spectral Violet + Bone-Silver calm alert (non-flashing so soulstones never trigger urgent heal reflexes).
                    return new BannerFrame(
                        true,
                        primary,
                        secondary,
                        fontSize,
                        false,
                        SoulstoneTextArgb,
                        ScaleBackgroundAlpha(SoulstoneBgArgb, opacity),
                        SoulstoneBorderArgb)
                    {
                        IsSoulstone = true,
                        HpBarPercent = settings.ShowBannerHpBar ? 0.0 : null,
                        TempHitPoints = 0
                    };
                }

                string critBgHex = Settings.NormalizeHexColor(settings.CriticalBgHex, Settings.DefaultCriticalBgHex);
                string critBorderHex = Settings.NormalizeHexColor(settings.CriticalBorderHex, Settings.DefaultCriticalBorderHex);
                bool defaultCritColors = critBgHex == Settings.DefaultCriticalBgHex
                    && critBorderHex == Settings.DefaultCriticalBorderHex
                    && textHex == Settings.DefaultBannerTextHex
                    && flashAltHex == Settings.DefaultFlashAltBgHex;

                int text;
                int bgBase;
                int border;
                if (defaultCritColors)
                {
                    text = flashPhase ? unchecked((int)0xFFFFFFFF) : unchecked((int)0xFFFF4D5A);
                    bgBase = flashPhase ? unchecked((int)0xF0960A14) : unchecked((int)0xEC0A0A0E);
                    border = flashPhase ? unchecked((int)0xFFFFD644) : unchecked((int)0xFFFF3848);
                }
                else
                {
                    Settings.TryParseHexRgb(textHex, out int tr, out int tg, out int tb);
                    Settings.TryParseHexRgb(critBgHex, out int br, out int bg, out int bb);
                    Settings.TryParseHexRgb(critBorderHex, out int cr, out int cg, out int cb);
                    Settings.TryParseHexRgb(flashAltHex, out int ar, out int ag, out int ab);

                    if (flashPhase)
                    {
                        text = MakeArgb(0xFF, tr, tg, tb);
                        bgBase = MakeArgb(0xF0, br, bg, bb);
                        border = MakeArgb(0xFF, cr, cg, cb);
                    }
                    else
                    {
                        text = (critBgHex == Settings.DefaultCriticalBgHex && critBorderHex == Settings.DefaultCriticalBorderHex && textHex == Settings.DefaultBannerTextHex)
                            ? unchecked((int)0xFFFF4D5A)
                            : BlendArgb(0xFF, cr, cg, cb, br, bg, bb, 0.45);
                        bgBase = MakeArgb(0xEC, ar, ag, ab);
                        border = (critBgHex == Settings.DefaultCriticalBgHex && critBorderHex == Settings.DefaultCriticalBorderHex)
                            ? unchecked((int)0xFFFF3848)
                            : BlendArgb(0xFF, cr, cg, cb, br, bg, bb, 0.55);
                    }
                }

                double? critHpPct = settings.ShowBannerHpBar
                    ? Math.Clamp(view.AlertPercent ?? (demo ? 28.0 : 0.0), 0.0, 100.0)
                    : null;
                return new BannerFrame(true, primary, secondary, fontSize, flashPhase, text, ScaleBackgroundAlpha(bgBase, opacity), border)
                {
                    IsSoulstone = false,
                    HpBarPercent = critHpPct,
                    TempHitPoints = Math.Max(0, view.AlertTempHitPoints)
                };
            }
            case BannerSeverity.LowHp:
            {
                string lowBgHex = Settings.NormalizeHexColor(settings.LowHpBgHex, Settings.DefaultLowHpBgHex);
                string lowBorderHex = Settings.NormalizeHexColor(settings.LowHpBorderHex, Settings.DefaultLowHpBorderHex);
                bool defaultLowColors = lowBgHex == Settings.DefaultLowHpBgHex
                    && lowBorderHex == Settings.DefaultLowHpBorderHex
                    && textHex == Settings.DefaultBannerTextHex
                    && flashAltHex == Settings.DefaultFlashAltBgHex;

                int text;
                int bgBase;
                int border;
                if (defaultLowColors)
                {
                    text = flashPhase ? unchecked((int)0xFFFFFFFF) : unchecked((int)0xFFFFBE3B);
                    bgBase = flashPhase ? unchecked((int)0xEEB84200) : unchecked((int)0xEC0B0D12);
                    border = flashPhase ? unchecked((int)0xFFFFE066) : unchecked((int)0xFFFF9E1B);
                }
                else
                {
                    Settings.TryParseHexRgb(textHex, out int tr, out int tg, out int tb);
                    Settings.TryParseHexRgb(lowBgHex, out int br, out int bg, out int bb);
                    Settings.TryParseHexRgb(lowBorderHex, out int cr, out int cg, out int cb);
                    Settings.TryParseHexRgb(flashAltHex, out int ar, out int ag, out int ab);

                    if (flashPhase)
                    {
                        text = MakeArgb(0xFF, tr, tg, tb);
                        bgBase = MakeArgb(0xEE, br, bg, bb);
                        border = MakeArgb(0xFF, cr, cg, cb);
                    }
                    else
                    {
                        text = (lowBgHex == Settings.DefaultLowHpBgHex && lowBorderHex == Settings.DefaultLowHpBorderHex && textHex == Settings.DefaultBannerTextHex)
                            ? unchecked((int)0xFFFFBE3B)
                            : BlendArgb(0xFF, cr, cg, cb, tr, tg, tb, 0.25);
                        bgBase = flashAltHex == Settings.DefaultFlashAltBgHex
                            ? unchecked((int)0xEC0B0D12)
                            : MakeArgb(0xEC, ar, ag, ab);
                        border = (lowBgHex == Settings.DefaultLowHpBgHex && lowBorderHex == Settings.DefaultLowHpBorderHex)
                            ? unchecked((int)0xFFFF9E1B)
                            : BlendArgb(0xFF, cr, cg, cb, br, bg, bb, 0.50);
                    }
                }

                double? lowHpPct = settings.ShowBannerHpBar
                    ? Math.Clamp(view.AlertPercent ?? 0.0, 0.0, 100.0)
                    : null;
                return new BannerFrame(true, primary, secondary, fontSize, flashPhase, text, ScaleBackgroundAlpha(bgBase, opacity), border)
                {
                    IsSoulstone = false,
                    HpBarPercent = lowHpPct,
                    TempHitPoints = Math.Max(0, view.AlertTempHitPoints)
                };
            }
            case BannerSeverity.Notice:
                return new BannerFrame(
                    true,
                    primary,
                    secondary,
                    fontSize,
                    false,
                    unchecked((int)0xFF7AD7FF),
                    ScaleBackgroundAlpha(unchecked((int)0xDF141B26), opacity),
                    unchecked((int)0xFF3E8EDE));
            case BannerSeverity.None:
            default:
                if (editing)
                {
                    return new BannerFrame(
                        true,
                        "DDO HEALER'S HELPER — DRAG TO POSITION",
                        FormatEditCountdown(editSecondsRemaining, fontSize, opacity),
                        fontSize,
                        false,
                        unchecked((int)0xFFFFFFFF),
                        ScaleBackgroundAlpha(unchecked((int)0xD8141820), opacity),
                        unchecked((int)0xFF56A8FF));
                }

                return new BannerFrame(false, "", "", fontSize, false, 0, 0, 0);
        }
    }

    public static BannerEditDragMode ClassifyEditHit(
        int width,
        int height,
        int x,
        int y,
        int cornerGrip = 18,
        int edgeGrip = 14)
    {
        if (width <= 0 || height <= 0 || x < 0 || y < 0 || x >= width || y >= height)
            return BannerEditDragMode.None;

        int cgX = Math.Min(cornerGrip, Math.Max(4, width / 3));
        int cgY = Math.Min(cornerGrip, Math.Max(4, height / 3));
        bool nearCornerLeft = x < cgX;
        bool nearCornerRight = x >= width - cgX;
        bool nearCornerTop = y < cgY;
        bool nearCornerBottom = y >= height - cgY;

        if (nearCornerLeft && nearCornerTop) return BannerEditDragMode.ResizeTopLeft;
        if (nearCornerRight && nearCornerTop) return BannerEditDragMode.ResizeTopRight;
        if (nearCornerLeft && nearCornerBottom) return BannerEditDragMode.ResizeBottomLeft;
        if (nearCornerRight && nearCornerBottom) return BannerEditDragMode.ResizeBottomRight;

        int egX = Math.Min(edgeGrip, Math.Max(3, width / 4));
        int egY = Math.Min(edgeGrip, Math.Max(3, height / 4));
        if (x < egX) return BannerEditDragMode.ResizeLeft;
        if (x >= width - egX) return BannerEditDragMode.ResizeRight;
        if (y < egY) return BannerEditDragMode.ResizeTop;
        if (y >= height - egY) return BannerEditDragMode.ResizeBottom;

        return BannerEditDragMode.Move;
    }

    private static int MakeArgb(int alpha, int r, int g, int b) =>
        unchecked((int)(((uint)(alpha & 0xFF) << 24)
                        | ((uint)(r & 0xFF) << 16)
                        | ((uint)(g & 0xFF) << 8)
                        | (uint)(b & 0xFF)));

    private static int BlendArgb(int alpha, int r1, int g1, int b1, int r2, int g2, int b2, double weight2)
    {
        double w2 = Math.Clamp(weight2, 0.0, 1.0);
        double w1 = 1.0 - w2;
        int r = Math.Clamp((int)Math.Round((r1 * w1) + (r2 * w2)), 0, 255);
        int g = Math.Clamp((int)Math.Round((g1 * w1) + (g2 * w2)), 0, 255);
        int b = Math.Clamp((int)Math.Round((b1 * w1) + (b2 * w2)), 0, 255);
        return MakeArgb(alpha, r, g, b);
    }

    private static int ScaleBackgroundAlpha(int argb, int opacity)
    {
        if (opacity == DefaultBannerOpacity)
        {
            return argb;
        }

        uint raw = unchecked((uint)argb);
        if (opacity >= MaxBannerOpacity)
        {
            return unchecked((int)((raw & 0x00FFFFFFu) | 0xFF000000u));
        }

        int baseAlpha = (int)((raw >> 24) & 0xFFu);
        uint scaled = (uint)Math.Clamp(
            (int)Math.Round(baseAlpha * (opacity / (double)DefaultBannerOpacity)),
            MinBannerOpacity,
            255);
        return unchecked((int)((raw & 0x00FFFFFFu) | (scaled << 24)));
    }

    public static (int X, int Y) ComputePosition(
        int screenLeft,
        int screenTop,
        int screenWidth,
        int screenHeight,
        int windowWidth,
        int windowHeight,
        double hudX,
        double hudY)
    {
        double clampedX = double.IsFinite(hudX) ? Math.Clamp(hudX, 0.0, 1.0) : 0.50;
        double clampedY = double.IsFinite(hudY) ? Math.Clamp(hudY, 0.0, 1.0) : 0.42;
        int availW = Math.Max(0, screenWidth - windowWidth);
        int availH = Math.Max(0, screenHeight - windowHeight);
        int x = screenLeft + (int)Math.Round(availW * clampedX);
        int y = screenTop + (int)Math.Round(availH * clampedY);
        return (x, y);
    }

    private static string FormatEditCountdown(double editSecondsRemaining, int fontSize, int opacity)
    {
        int seconds = double.IsFinite(editSecondsRemaining)
            ? Math.Max(0, (int)Math.Ceiling(editSecondsRemaining))
            : 0;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Drag center to move • drag edges/wheel to resize ({fontSize}px) • Ctrl+wheel opacity ({opacity}) • locks in {seconds}s");
    }
}
