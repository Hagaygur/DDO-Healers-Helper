using System;

namespace VoK.HealersHelper;

internal readonly record struct BannerFrame(
    bool Visible,
    string PrimaryText,
    string SecondaryText,
    int FontSizePixels,
    bool FlashOn,
    int TextColorArgb,
    int BackgroundColorArgb,
    int BorderColorArgb);

internal static class BannerPresentation
{
    public const int MinFontSizePixels = 18;
    public const int MaxFontSizePixels = 140;
    public const int MinFlashIntervalMs = 100;
    public const int MaxFlashIntervalMs = 1000;
    public const int MinBannerOpacity = 80;
    public const int MaxBannerOpacity = 255;
    public const int DefaultBannerOpacity = 235;

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
        int flashIntervalMs = Math.Clamp(settings.FlashIntervalMs, MinFlashIntervalMs, MaxFlashIntervalMs);
        int opacity = Math.Clamp(settings.BannerOpacity, MinBannerOpacity, MaxBannerOpacity);
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
            string dragSuffix = FormatEditCountdown(editSecondsRemaining);
            secondary = string.IsNullOrWhiteSpace(secondary) ? dragSuffix : $"{secondary} • {dragSuffix}";
        }

        switch (effectiveSeverity)
        {
            case BannerSeverity.CriticalDown:
            {
                int text = flashPhase ? unchecked((int)0xFFFFFFFF) : unchecked((int)0xFFFF4D5A);
                int bgBase = flashPhase ? unchecked((int)0xF0960A14) : unchecked((int)0xEC0A0A0E);
                int border = flashPhase ? unchecked((int)0xFFFFD644) : unchecked((int)0xFFFF3848);
                return new BannerFrame(true, primary, secondary, fontSize, flashPhase, text, ScaleBackgroundAlpha(bgBase, opacity), border);
            }
            case BannerSeverity.LowHp:
            {
                int text = flashPhase ? unchecked((int)0xFFFFFFFF) : unchecked((int)0xFFFFBE3B);
                int bgBase = flashPhase ? unchecked((int)0xEEB84200) : unchecked((int)0xEC0B0D12);
                int border = flashPhase ? unchecked((int)0xFFFFE066) : unchecked((int)0xFFFF9E1B);
                return new BannerFrame(true, primary, secondary, fontSize, flashPhase, text, ScaleBackgroundAlpha(bgBase, opacity), border);
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
                        FormatEditCountdown(editSecondsRemaining),
                        fontSize,
                        false,
                        unchecked((int)0xFFFFFFFF),
                        ScaleBackgroundAlpha(unchecked((int)0xD8141820), opacity),
                        unchecked((int)0xFF56A8FF));
                }

                return new BannerFrame(false, "", "", fontSize, false, 0, 0, 0);
        }
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
        uint scaled = (uint)Math.Clamp((int)Math.Round(baseAlpha * (opacity / (double)DefaultBannerOpacity)), 40, 255);
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

    private static string FormatEditCountdown(double editSecondsRemaining)
    {
        int seconds = double.IsFinite(editSecondsRemaining)
            ? Math.Max(0, (int)Math.Ceiling(editSecondsRemaining))
            : 0;
        return $"Drag to move • locks in {seconds}s";
    }
}
