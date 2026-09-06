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
    /// Time between one-character steps while scrolling.
    /// </summary>
    public static readonly TimeSpan StepInterval = TimeSpan.FromMilliseconds(250);

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
    public static string Window(string text, int maxLength, TimeSpan elapsed)
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
        var offset = ScrollOffset(elapsed, ring.Length);
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
    /// <returns>An offset into the ring.</returns>
    private static int ScrollOffset(TimeSpan elapsed, int ringLength)
    {
        if (elapsed <= InitialPause || ringLength <= 0)
        {
            return 0;
        }

        var steps = (long)((elapsed - InitialPause).Ticks / StepInterval.Ticks);
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
