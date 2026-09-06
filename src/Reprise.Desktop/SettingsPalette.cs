using Avalonia.Media;

namespace Reprise.Desktop;

/// <summary>
/// The colours the settings window paints itself with.
/// </summary>
/// <remarks>
/// Defined here rather than taken from the Fluent theme's resource keys:
/// those keys are an implementation detail of the control set, and a key
/// that disappears leaves a control with no brush at all rather than a
/// wrong one, which is hard to notice. Resolving the five colours we
/// actually use from the desktop's own light or dark setting keeps the
/// window in step with the panel, which does the same.
/// </remarks>
/// <param name="Ground">Window background, behind the cards.</param>
/// <param name="Card">Fill of a settings card.</param>
/// <param name="Well">Sunken fill: segmented pickers, selected tab, badges.</param>
/// <param name="Primary">Row labels and values.</param>
/// <param name="Secondary">Captions, footnotes, and inactive tabs.</param>
/// <param name="Line">Card borders and the hairlines between rows.</param>
/// <param name="Accent">Selected tab and links.</param>
public sealed record SettingsPalette(
    Color Ground,
    Color Card,
    Color Well,
    Color Primary,
    Color Secondary,
    Color Line,
    Color Accent)
{
    /// <summary>
    /// Resource key each colour is published under, in declaration order.
    /// </summary>
    /// <remarks>
    /// Controls bind to these so a change of desktop theme repaints the
    /// whole window without rebuilding it.
    /// </remarks>
    public static readonly string[] ResourceKeys =
    [
        "SettingsGroundBrush",
        "SettingsCardBrush",
        "SettingsWellBrush",
        "SettingsPrimaryBrush",
        "SettingsSecondaryBrush",
        "SettingsLineBrush",
        "SettingsAccentBrush",
    ];

    /// <summary>
    /// Resolves the palette for the desktop's current appearance.
    /// </summary>
    /// <param name="isDark">Whether the desktop is in its dark mode.</param>
    /// <param name="accent">Desktop accent colour.</param>
    /// <returns>The colours to paint with.</returns>
    /// <example>
    /// <code>
    /// var palette = SettingsPalette.Resolve(isDark: false, Colors.DodgerBlue);
    /// </code>
    /// </example>
    public static SettingsPalette Resolve(bool isDark, Color accent) => isDark
        ? new SettingsPalette(
            Ground: Color.FromRgb(0x1E, 0x1E, 0x20),
            Card: Color.FromRgb(0x2A, 0x2A, 0x2D),
            Well: Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF),
            Primary: Color.FromRgb(0xF2, 0xF2, 0xF4),
            Secondary: Color.FromArgb(0x9E, 0xFF, 0xFF, 0xFF),
            Line: Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF),
            Accent: accent)
        : new SettingsPalette(
            Ground: Color.FromRgb(0xF2, 0xF2, 0xF4),
            Card: Colors.White,
            Well: Color.FromArgb(0x14, 0x00, 0x00, 0x00),
            Primary: Color.FromRgb(0x1A, 0x1A, 0x1C),
            Secondary: Color.FromArgb(0x99, 0x00, 0x00, 0x00),
            Line: Color.FromArgb(0x1A, 0x00, 0x00, 0x00),
            Accent: accent);

    /// <summary>
    /// The colours in the order of <see cref="ResourceKeys"/>.
    /// </summary>
    /// <returns>One brush per key.</returns>
    public IReadOnlyList<IBrush> Brushes =>
    [
        new SolidColorBrush(Ground),
        new SolidColorBrush(Card),
        new SolidColorBrush(Well),
        new SolidColorBrush(Primary),
        new SolidColorBrush(Secondary),
        new SolidColorBrush(Line),
        new SolidColorBrush(Accent),
    ];
}
