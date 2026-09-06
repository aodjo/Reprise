using Avalonia.Media;

namespace Reprise.Desktop;

/// <summary>
/// The typeface and sizes every Reprise surface shares.
/// </summary>
/// <remarks>
/// Pretendard is bundled so the panel looks the same on every distribution
/// and covers Hangul without depending on which CJK fonts happen to be
/// installed; glyphs it lacks fall back to the system through Avalonia's
/// per-character matching. Sizes sit a step below the macOS point sizes,
/// since the same numbers render larger against Linux's 96 DPI baseline.
/// </remarks>
public static class PanelTypography
{
    /// <summary>
    /// Resource address of the bundled font family.
    /// </summary>
    public const string FamilyName = "avares://Reprise.Desktop/Assets/Fonts#Pretendard";

    /// <summary>
    /// The bundled family.
    /// </summary>
    public static readonly FontFamily Family = new(FamilyName);

    /// <summary>Track title in the card and the empty-state title.</summary>
    public const double Title = 12.5;

    /// <summary>Artist line under the title.</summary>
    public const double Subtitle = 10.5;

    /// <summary>Time labels, the footer, and the error banner.</summary>
    public const double Caption = 9.5;

    /// <summary>Lyric lines in the panel strip.</summary>
    public const double Lyric = 14;

    /// <summary>Empty-state heading.</summary>
    public const double Heading = 14;

    /// <summary>Row labels in the settings window.</summary>
    public const double Body = 12;

    /// <summary>Captions, footnotes, and segmented labels in the settings window.</summary>
    public const double Small = 11;
}
