using System.Globalization;
using Reprise.Core;

namespace Reprise.Desktop;

/// <summary>
/// Composes the text the tray label shows beside the icon.
/// </summary>
/// <remarks>
/// The rules are the macOS status item's: nothing when the format is
/// hidden, the current lyric line when lyrics are shown and available, and
/// otherwise the title in the chosen format. Because a tray label cannot
/// animate, an overlong text is scrolled by rotating a fixed-width window
/// through it at a steady step, which <see cref="Window"/> computes.
/// </remarks>
public static class MenuBarText
{
    /// <summary>
    /// Separator inserted between the end of a scrolling text and its restart.
    /// </summary>
    public const string ScrollGap = "   ·   ";

    /// <summary>
    /// Rest before a long label starts scrolling.
    /// </summary>
    public static readonly TimeSpan InitialPause = TimeSpan.FromSeconds(1.4);

    /// <summary>
    /// Time between one-character steps at the normal marquee speed.
    /// </summary>
    public static readonly TimeSpan StepInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Converts the panel marquee speed into a label step interval.
    /// </summary>
    /// <remarks>
    /// Calibrated so the normal speed of 30 points per second gives one
    /// character every quarter second, and the slow and fast settings scale
    /// in proportion.
    /// </remarks>
    /// <param name="pointsPerSecond">Panel marquee speed.</param>
    /// <returns>Time between one-character steps.</returns>
    /// <example>
    /// <code>
    /// MenuBarText.StepIntervalFor(45); // ~167 ms
    /// </code>
    /// </example>
    public static TimeSpan StepIntervalFor(double pointsPerSecond) =>
        TimeSpan.FromSeconds(7.5 / Math.Max(pointsPerSecond, 1));

    /// <summary>
    /// Text for the tray label given the current state.
    /// </summary>
    /// <param name="session">Session on display, or null.</param>
    /// <param name="lyricLine">Lyric line being sung, or null.</param>
    /// <param name="preferences">Label settings.</param>
    /// <returns>The label text, possibly empty.</returns>
    /// <example>
    /// <code>
    /// MenuBarText.Compose(session, null, preferences); // "Bridge Song"
    /// </code>
    /// </example>
    public static string Compose(
        MediaSessionSnapshot? session,
        string? lyricLine,
        DesktopPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        if (preferences.MenuBarTitleFormat == MenuBarTitleFormat.Hidden || session is null)
        {
            return string.Empty;
        }

        if (preferences.MenuBarShowsLyrics && !string.IsNullOrWhiteSpace(lyricLine))
        {
            return lyricLine.Trim();
        }

        return FormatTitle(preferences.MenuBarTitleFormat, session.Title, session.Artist);
    }

    /// <summary>
    /// Joins a title and artist the way a format asks.
    /// </summary>
    /// <param name="format">Arrangement to use.</param>
    /// <param name="title">Track title.</param>
    /// <param name="artist">Artist credit.</param>
    /// <returns>The joined text; empty parts are dropped along with their dash.</returns>
    /// <example>
    /// <code>
    /// MenuBarText.FormatTitle(MenuBarTitleFormat.ArtistTitle, "Song", "Band"); // "Band - Song"
    /// </code>
    /// </example>
    public static string FormatTitle(MenuBarTitleFormat format, string title, string artist)
    {
        title = title.Trim();
        artist = artist.Trim();
        return format switch
        {
            MenuBarTitleFormat.TitleOnly => title.Length > 0 ? title : artist,
            MenuBarTitleFormat.TitleArtist => Join(title, artist),
            MenuBarTitleFormat.ArtistTitle => Join(artist, title),
            _ => string.Empty,
        };
    }

    /// <summary>
    /// The slice of a text that is visible at a moment of scrolling.
    /// </summary>
    /// <remarks>
    /// Text that fits is returned whole. Otherwise the text plus
    /// <see cref="ScrollGap"/> is treated as a ring; after
    /// <see cref="InitialPause"/> the window advances one character every
    /// <see cref="StepInterval"/> and wraps, so the text scrolls past and
    /// returns. Characters are counted as text elements, so an emoji or a
    /// combining sequence is never split.
    /// </remarks>
    /// <param name="text">Full label text.</param>
    /// <param name="maxLength">Widest label to show, in text elements.</param>
    /// <param name="elapsed">Time since the text was first shown.</param>
    /// <returns>At most <paramref name="maxLength"/> text elements.</returns>
    /// <example>
    /// <code>
    /// MenuBarText.Window("abcdef", 4, TimeSpan.Zero);          // "abcd"
    /// MenuBarText.Window("abcdef", 4, TimeSpan.FromSeconds(2)); // "cdef"
    /// </code>
    /// </example>
    public static string Window(string text, int maxLength, TimeSpan elapsed) =>
        Window(text, maxLength, elapsed, StepInterval);

    /// <summary>
    /// The slice of a text visible at a moment, at a chosen scrolling speed.
    /// </summary>
    /// <param name="text">Full label text.</param>
    /// <param name="maxLength">Widest label to show, in text elements.</param>
    /// <param name="elapsed">Time since the text was first shown.</param>
    /// <param name="stepInterval">Time between one-character steps.</param>
    /// <returns>At most <paramref name="maxLength"/> text elements.</returns>
    public static string Window(string text, int maxLength, TimeSpan elapsed, TimeSpan stepInterval)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (maxLength <= 0)
        {
            return string.Empty;
        }

        var elements = TextElements(text);
        if (elements.Length <= maxLength)
        {
            return text;
        }

        var ring = TextElements(text + ScrollGap);
        var offset = ScrollOffset(elapsed, ring.Length, stepInterval);
        var visible = new string[maxLength];
        for (var index = 0; index < maxLength; index++)
        {
            visible[index] = ring[(offset + index) % ring.Length];
        }

        return string.Concat(visible);
    }

    /// <summary>
    /// Whether a text is long enough to scroll.
    /// </summary>
    /// <param name="text">Label text.</param>
    /// <param name="maxLength">Widest label to show, in text elements.</param>
    /// <returns>True when <see cref="Window"/> will move over time.</returns>
    public static bool Scrolls(string text, int maxLength) =>
        maxLength > 0 && TextElements(text).Length > maxLength;

    /// <summary>
    /// How many characters the window has advanced at a moment.
    /// </summary>
    /// <param name="elapsed">Time since the text was first shown.</param>
    /// <param name="ringLength">Length of the text ring being scrolled.</param>
    /// <param name="stepInterval">Time between one-character steps.</param>
    /// <returns>An offset into the ring.</returns>
    private static int ScrollOffset(TimeSpan elapsed, int ringLength, TimeSpan stepInterval)
    {
        if (elapsed <= InitialPause || ringLength <= 0 || stepInterval <= TimeSpan.Zero)
        {
            return 0;
        }

        var steps = (long)((elapsed - InitialPause).Ticks / stepInterval.Ticks);
        return (int)(steps % ringLength);
    }

    /// <summary>
    /// Splits text into user-perceived characters.
    /// </summary>
    /// <param name="text">Text to split.</param>
    /// <returns>One string per text element.</returns>
    private static string[] TextElements(string text)
    {
        var elements = new List<string>(text.Length);
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            elements.Add(enumerator.GetTextElement());
        }

        return [.. elements];
    }

    /// <summary>
    /// Joins two parts with a dash, omitting empty parts.
    /// </summary>
    /// <param name="first">Leading part.</param>
    /// <param name="second">Trailing part.</param>
    /// <returns>The joined text.</returns>
    private static string Join(string first, string second) =>
        string.Join(" - ", new[] { first, second }.Where(part => part.Length > 0));
}
