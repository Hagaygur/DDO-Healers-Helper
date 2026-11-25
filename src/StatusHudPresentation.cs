using System;
using System.Globalization;

namespace VoK.HealersHelper;

internal readonly record struct StatusHudFrame(
    bool Visible,
    string Text,
    int FontSizePixels,
    int TextColorArgb,
    bool ShowDragBox,
    string DragHint);

internal static class StatusHudPresentation
{
    public const int MinFontSizePixels = 9;
    public const int MaxFontSizePixels = 48;

    public static StatusHudFrame Compute(
        AlertBannerView view,
        Settings settings,
        bool editing,
        double editSecondsRemaining)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(settings);

        int fontSize = Math.Clamp(settings.StatusFontSizePixels, MinFontSizePixels, MaxFontSizePixels);
        if (!settings.StatusHudEnabled && !editing)
        {
            return new StatusHudFrame(false, "", fontSize, 0, false, "");
        }

        int saved = Math.Max(0, view.PeopleSaved);
        string savedPart = settings.ShowRankOnStatusHud
            ? string.Create(CultureInfo.InvariantCulture, $"SAVED {saved} ({HealerStats.RankForSaves(saved)})")
            : string.Create(CultureInfo.InvariantCulture, $"SAVED {saved}");
        string text;
        int colorArgb;

        if (!settings.PluginEnabled)
        {
            text = "HEALER  OFF";
            colorArgb = unchecked((int)0xFF9AA4B2);
        }
        else if (settings.Mode == TrackingMode.Off)
        {
            text = string.Create(CultureInfo.InvariantCulture, $"HEALER  PAUSED  •  {savedPart}");
            colorArgb = unchecked((int)0xFFD4B46A);
        }
        else if (settings.Mode == TrackingMode.SinglePlayer)
        {
            string target = string.IsNullOrWhiteSpace(settings.FocusedPlayerName) ? "None" : settings.FocusedPlayerName;
            text = string.Create(
                CultureInfo.InvariantCulture,
                $"HEALER  FOCUS: {target} <={settings.HpThresholdPercent}%  •  {savedPart}");
            colorArgb = unchecked((int)0xFF7AD7FF);
        }
        else if (view.Severity is BannerSeverity.LowHp or BannerSeverity.CriticalDown)
        {
            text = string.Create(CultureInfo.InvariantCulture, $"HEALER  PARTY • ALERT  •  {savedPart}");
            colorArgb = unchecked((int)0xFFFFAE42);
        }
        else
        {
            text = string.Create(
                CultureInfo.InvariantCulture,
                $"HEALER  PARTY <={settings.HpThresholdPercent}%  •  {savedPart}");
            colorArgb = unchecked((int)0xFF7AE582);
        }

        string dragHint = editing ? FormatDragHint(editSecondsRemaining) : "";
        return new StatusHudFrame(true, text, fontSize, colorArgb, editing, dragHint);
    }

    private static string FormatDragHint(double editSecondsRemaining)
    {
        int seconds = double.IsFinite(editSecondsRemaining)
            ? Math.Max(0, (int)Math.Ceiling(editSecondsRemaining))
            : 0;
        return $"Drag status • locks in {seconds}s";
    }
}
