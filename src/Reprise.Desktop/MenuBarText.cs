using System.Globalization;
using Reprise.Core;

namespace Reprise.Desktop;

/// <summary>
/// Composes the text the tray label shows beside the icon.
/// </summary>
/// <remarks>
/// The rules are the macOS status item's: nothing when the format is
/// hidden, the current lyric line when lyrics are shown and available, and
/// otherwise the title in the chosen format.
/// <para>
/// The text is never shortened. A tray label cannot animate, so the only
/// way to imitate the panel's scrolling title would be to publish a moving
/// slice of the text, which means showing part of a word at every step;
/// showing the line whole and letting the entry be as wide as it needs is
/// the honest alternative. <see cref="Reserve"/> is the one adjustment,
/// and it only ever adds width.
/// </para>
/// </remarks>
public static class MenuBarText
{
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
    /// Pads a label out to a fixed width so the tray entry stops resizing.
    /// </summary>
    /// <remarks>
    /// A tray label is a string, not a measurement, so the only way to hold
    /// a width is to make every label the same length. Trailing spaces do
    /// that, and unlike a separate width hint they cannot overstate the
    /// width: the label is exactly as wide as it reads.
    /// </remarks>
    /// <param name="text">Label text.</param>
    /// <param name="length">Width to hold, in text elements.</param>
    /// <returns>
    /// The text padded with spaces to <paramref name="length"/>, or
    /// unchanged when it is already that long.
    /// </returns>
    /// <example>
    /// <code>
    /// MenuBarText.Reserve("ab", 5); // "ab   "
    /// </code>
    /// </example>
    public static string Reserve(string text, int length)
    {
        ArgumentNullException.ThrowIfNull(text);
        var elements = TextElements(text).Length;
        return elements >= length ? text : text + new string(' ', length - elements);
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
