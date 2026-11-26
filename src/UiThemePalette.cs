using System;

namespace VoK.HealersHelper;

public enum UiTheme
{
    EberronGold = 0,
    CannithArcane = 1,
    FeywildTwilight = 2
}

// Pure portable DDO x Material Design 3 dark tonal surface palette table shared
// across DH 4 (WinForms/GDI+) and DH 5 Beta (Dear ImGui). Deliberately avoids
// crimson chrome so Critical/Incap (#FF4D5A) alerts pop preattentively in all 3 themes.
internal sealed record UiThemePalette(
    UiTheme Theme,
    string Key,
    string DisplayName,
    int SurfaceBaseArgb,
    int SurfaceContainerArgb,
    int SurfaceControlArgb,
    int SurfaceHoverArgb,
    int OutlineArgb,
    int PrimaryArgb,
    int SecondaryArgb,
    int TextPrimaryArgb,
    int TextMutedArgb)
{
    public static readonly UiThemePalette EberronGold = new(
        UiTheme.EberronGold,
        "eberron",
        "Eberron Obsidian & Gold",
        SurfaceBaseArgb: unchecked((int)0xFF111318),
        SurfaceContainerArgb: unchecked((int)0xFF1A1D24),
        SurfaceControlArgb: unchecked((int)0xFF222630),
        SurfaceHoverArgb: unchecked((int)0xFF2C313E),
        OutlineArgb: unchecked((int)0xFF4A5368),
        PrimaryArgb: unchecked((int)0xFFE2B75B),
        SecondaryArgb: unchecked((int)0xFF56CCF2),
        TextPrimaryArgb: unchecked((int)0xFFECEEF4),
        TextMutedArgb: unchecked((int)0xFFA9B1C3));

    public static readonly UiThemePalette CannithArcane = new(
        UiTheme.CannithArcane,
        "cannith",
        "House Cannith Arcane Forge",
        SurfaceBaseArgb: unchecked((int)0xFF0E141D),
        SurfaceContainerArgb: unchecked((int)0xFF151E2B),
        SurfaceControlArgb: unchecked((int)0xFF1D293A),
        SurfaceHoverArgb: unchecked((int)0xFF27364B),
        OutlineArgb: unchecked((int)0xFF455D7A),
        PrimaryArgb: unchecked((int)0xFF56CCF2),
        SecondaryArgb: unchecked((int)0xFFD4A359),
        TextPrimaryArgb: unchecked((int)0xFFEAF2FA),
        TextMutedArgb: unchecked((int)0xFF9EB2C8));

    public static readonly UiThemePalette FeywildTwilight = new(
        UiTheme.FeywildTwilight,
        "feywild",
        "Feywild Eldritch Twilight",
        SurfaceBaseArgb: unchecked((int)0xFF15111E),
        SurfaceContainerArgb: unchecked((int)0xFF1E182B),
        SurfaceControlArgb: unchecked((int)0xFF282039),
        SurfaceHoverArgb: unchecked((int)0xFF332949),
        OutlineArgb: unchecked((int)0xFF584875),
        PrimaryArgb: unchecked((int)0xFFCDA4FF),
        SecondaryArgb: unchecked((int)0xFF6EE7B7),
        TextPrimaryArgb: unchecked((int)0xFFF2ECF9),
        TextMutedArgb: unchecked((int)0xFFB8ABC9));

    public static readonly UiThemePalette[] All = [EberronGold, CannithArcane, FeywildTwilight];

    public static UiThemePalette For(UiTheme theme) => theme switch
    {
        UiTheme.CannithArcane => CannithArcane,
        UiTheme.FeywildTwilight => FeywildTwilight,
        _ => EberronGold
    };

    public static bool TryParse(string? raw, out UiTheme theme)
    {
        string normalized = (raw ?? "").Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "eberron":
            case "eberrongold":
            case "eberron-gold":
            case "gold":
            case "0":
                theme = UiTheme.EberronGold;
                return true;
            case "cannith":
            case "cannitharcane":
            case "cannith-arcane":
            case "arcane":
            case "1":
                theme = UiTheme.CannithArcane;
                return true;
            case "feywild":
            case "feywildtwilight":
            case "feywild-twilight":
            case "twilight":
            case "2":
                theme = UiTheme.FeywildTwilight;
                return true;
            default:
                theme = UiTheme.EberronGold;
                return false;
        }
    }
}
