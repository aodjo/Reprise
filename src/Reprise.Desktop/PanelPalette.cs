using Avalonia.Media;

namespace Reprise.Desktop;

/// <summary>
/// How much of the desktop a window can show through itself.
/// </summary>
/// <remarks>
/// Reported by the windowing system after the window is shown, and used to
/// pick how opaque the Liquid theme has to be to stay readable.
/// </remarks>
public enum PanelTranslucency
{
    /// <summary>
    /// The window is always opaque.
    /// </summary>
    None,

    /// <summary>
    /// The window can be see-through, but nothing behind it is blurred.
    /// </summary>
    Transparent,

    /// <summary>
    /// The compositor blurs whatever lies behind the window.
    /// </summary>
    Blurred,
}

/// <summary>
/// The colours one <see cref="PanelTheme"/> resolves to.
/// </summary>
/// <remarks>
/// Resolved once per theme change rather than looked up per control, so
/// every part of the panel agrees on the same six values. Derived colours
/// are exposed as properties to keep the opacity choices in one place.
/// </remarks>
/// <param name="IsDark">Whether text is light on a dark ground.</param>
/// <param name="Background">Fill behind the whole panel.</param>
/// <param name="Primary">Titles, glyphs, and the scrubber knob edge.</param>
/// <param name="Secondary">Artist, times, footer text, and footer glyphs.</param>
/// <param name="Accent">Scrubber fill and artwork placeholder tint.</param>
public sealed record PanelPalette(
    bool IsDark,
    Color Background,
    Color Primary,
    Color Secondary,
    Color Accent)
{
    /// <summary>
    /// Hairline between the card and the footer.
    /// </summary>
    public Color Separator => WithAlpha(Primary, 0.14);

    /// <summary>
    /// Transport glyphs at rest.
    /// </summary>
    public Color Control => WithAlpha(Primary, 0.72);

    /// <summary>
    /// Transport glyphs while pressed.
    /// </summary>
    public Color ControlPressed => WithAlpha(Primary, 0.55);

    /// <summary>
    /// Unfilled part of a slider track.
    /// </summary>
    public Color Track => WithAlpha(Primary, IsDark ? 0.22 : 0.16);

    /// <summary>
    /// Ground of the floating volume slider.
    /// </summary>
    public Color FloatingBackground => LiquidBackground(IsDark, PanelTranslucency.Transparent);

    /// <summary>
    /// Resolves the palette for a theme in the current environment.
    /// </summary>
    /// <remarks>
    /// White and Dark ignore the desktop setting entirely, which is their
    /// point; Liquid and System follow it. The accent is taken from the
    /// desktop where available so the scrubber matches the user's other
    /// controls.
    /// </remarks>
    /// <param name="theme">Theme the user chose.</param>
    /// <param name="systemIsDark">Whether the desktop is in its dark mode.</param>
    /// <param name="accent">Desktop accent colour.</param>
    /// <param name="translucency">What the window can show through.</param>
    /// <returns>The resolved colours.</returns>
    /// <example>
    /// <code>
    /// var palette = PanelPalette.Resolve(
    ///     PanelTheme.Liquid, systemIsDark: true, accent, PanelTranslucency.Blurred);
    /// </code>
    /// </example>
    public static PanelPalette Resolve(
        PanelTheme theme,
        bool systemIsDark,
        Color accent,
        PanelTranslucency translucency)
    {
        var isDark = theme switch
        {
            PanelTheme.White => false,
            PanelTheme.Dark => true,
            _ => systemIsDark,
        };
        var primary = isDark ? Colors.White : Colors.Black;
        var secondary = isDark
            ? Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x80, 0x00, 0x00, 0x00);
        var background = theme switch
        {
            PanelTheme.White => Colors.White,
            PanelTheme.Dark => Color.FromRgb(0x1F, 0x1F, 0x1F),
            PanelTheme.Liquid => LiquidBackground(isDark, translucency),
            _ => isDark ? Color.FromRgb(0x32, 0x32, 0x32) : Color.FromRgb(0xEC, 0xEC, 0xEC),
        };

        return new PanelPalette(isDark, background, primary, secondary, accent);
    }

    /// <summary>
    /// Ground colour of the Liquid theme for a given translucency.
    /// </summary>
    /// <remarks>
    /// Behind a blur the ground can be quite open, since the blur supplies
    /// the contrast. With plain transparency whatever sits behind the panel
    /// would show through sharply, so it is nearly opaque; with none it is
    /// fully opaque.
    /// </remarks>
    /// <param name="isDark">Whether the desktop is in its dark mode.</param>
    /// <param name="translucency">What the window can show through.</param>
    /// <returns>A colour with the appropriate alpha.</returns>
    public static Color LiquidBackground(bool isDark, PanelTranslucency translucency)
    {
        var alpha = translucency switch
        {
            PanelTranslucency.Blurred => 0.66,
            PanelTranslucency.Transparent => 0.93,
            _ => 1.0,
        };
        var ground = isDark ? Color.FromRgb(0x1C, 0x1C, 0x1E) : Color.FromRgb(0xF4, 0xF4, 0xF6);
        return WithAlpha(ground, alpha);
    }

    /// <summary>
    /// Copies a colour with a different opacity.
    /// </summary>
    /// <param name="color">Colour to copy.</param>
    /// <param name="alpha">Opacity from 0 to 1.</param>
    /// <returns>The colour with the new alpha channel.</returns>
    public static Color WithAlpha(Color color, double alpha)
    {
        var channel = (byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255);
        return Color.FromArgb(channel, color.R, color.G, color.B);
    }
}
