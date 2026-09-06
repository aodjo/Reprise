using System.Globalization;
using System.Text;

namespace Reprise.Core;

/// <summary>
/// Where a set of lyrics came from.
/// </summary>
public enum LyricsSource
{
    /// <summary>
    /// NAVER VIBE, which carries line end times and Korean catalogue depth.
    /// </summary>
    Vibe,

    /// <summary>
    /// LRCLIB, which supplies LRC-format lyrics for a broad catalogue.
    /// </summary>
    Lrclib,
}

/// <summary>
/// One timed line of lyrics.
/// </summary>
/// <param name="StartTime">When the line becomes current.</param>
/// <param name="EndTime">
/// When the line stops being current, or null when it lasts until the next
/// line or the end of the track.
/// </param>
/// <param name="Text">The words.</param>
public sealed record LyricLine(TimeSpan StartTime, TimeSpan? EndTime, string Text);

/// <summary>
/// Time-synced lyrics for one track.
/// </summary>
/// <remarks>
/// Lines are kept in start-time order so the current line can be found by
/// binary search, which matters because the panel asks several times a
/// second.
/// </remarks>
/// <param name="Source">Service the lyrics came from.</param>
/// <param name="Lines">Lines in ascending start time.</param>
public sealed record SyncedLyrics(LyricsSource Source, IReadOnlyList<LyricLine> Lines)
{
    /// <summary>
    /// Index of the last line that has started by a position.
    /// </summary>
    /// <remarks>
    /// Ignores end times, so a gap between lines keeps the previous line
    /// focused; that is what the scrolling lyrics view wants, since it has
    /// to keep something centred during an instrumental break.
    /// </remarks>
    /// <param name="position">Playback position.</param>
    /// <returns>The index, or null before the first line.</returns>
    /// <example>
    /// <code>
    /// lyrics.FocusedLineIndex(TimeSpan.FromSeconds(42)); // 7
    /// </code>
    /// </example>
    public int? FocusedLineIndex(TimeSpan position)
    {
        if (Lines.Count == 0)
        {
            return null;
        }

        var lower = 0;
        var upper = Lines.Count;
        while (lower < upper)
        {
            var middle = (lower + upper) / 2;
            if (Lines[middle].StartTime <= position)
            {
                lower = middle + 1;
            }
            else
            {
                upper = middle;
            }
        }

        return lower - 1 >= 0 ? lower - 1 : null;
    }

    /// <summary>
    /// Index of the line that is being sung at a position.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="FocusedLineIndex"/>, a line whose end time has
    /// passed no longer counts, so the menu bar can go quiet between verses.
    /// </remarks>
    /// <param name="position">Playback position.</param>
    /// <returns>The index, or null when no line is current.</returns>
    public int? LineIndex(TimeSpan position)
    {
        if (FocusedLineIndex(position) is not { } index)
        {
            return null;
        }

        return Lines[index].EndTime is { } end && position >= end ? null : index;
    }

    /// <summary>
    /// The line being sung at a position.
    /// </summary>
    /// <param name="position">Playback position.</param>
    /// <returns>The line, or null when no line is current.</returns>
    public LyricLine? Line(TimeSpan position) =>
        LineIndex(position) is { } index ? Lines[index] : null;
}

/// <summary>
/// Identifies a track for lyrics lookup, comparing loosely.
/// </summary>
/// <remarks>
/// Two queries are equal when their title, album, and artist match after
/// normalisation - case, diacritics, width, and punctuation are ignored -
/// because players report the same song with cosmetic differences and each
/// difference would otherwise be a fresh network lookup. Duration is
/// carried for the services to score with but does not take part in
/// equality.
/// </remarks>
public sealed class LyricsTrackQuery : IEquatable<LyricsTrackQuery>
{
    /// <summary>
    /// Creates a query from its parts.
    /// </summary>
    /// <param name="title">Track title.</param>
    /// <param name="album">Album name, possibly empty.</param>
    /// <param name="artist">Artist credit, possibly empty.</param>
    /// <param name="duration">Track length, or zero when unknown.</param>
    public LyricsTrackQuery(string title, string album, string artist, TimeSpan duration)
    {
        Title = title;
        Album = album;
        Artist = artist;
        Duration = duration;
        NormalizedTitle = Normalize(title);
        NormalizedAlbum = Normalize(album);
        NormalizedArtist = Normalize(artist);
    }

    /// <summary>
    /// Track title as the player reported it.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Album name as the player reported it.
    /// </summary>
    public string Album { get; }

    /// <summary>
    /// Artist credit as the player reported it.
    /// </summary>
    public string Artist { get; }

    /// <summary>
    /// Track length, or zero when unknown.
    /// </summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// Title reduced to lower-case alphanumerics.
    /// </summary>
    public string NormalizedTitle { get; }

    /// <summary>
    /// Album reduced to lower-case alphanumerics.
    /// </summary>
    public string NormalizedAlbum { get; }

    /// <summary>
    /// Artist reduced to lower-case alphanumerics.
    /// </summary>
    public string NormalizedArtist { get; }

    /// <summary>
    /// Builds the query for a session's current track.
    /// </summary>
    /// <param name="session">Session to describe.</param>
    /// <returns>A query, or null when the session has no title to look up.</returns>
    /// <example>
    /// <code>
    /// var query = LyricsTrackQuery.From(session);
    /// </code>
    /// </example>
    public static LyricsTrackQuery? From(MediaSessionSnapshot session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(session.Title))
        {
            return null;
        }

        return new LyricsTrackQuery(
            session.Title,
            session.Album,
            session.Artist,
            session.Duration ?? TimeSpan.Zero);
    }

    /// <summary>
    /// Reduces text to the characters that identify a song.
    /// </summary>
    /// <remarks>
    /// Compatibility decomposition folds full-width forms and ligatures,
    /// dropping combining marks removes diacritics, and keeping only letters
    /// and digits removes the punctuation that varies between catalogues.
    /// </remarks>
    /// <param name="value">Text to normalise.</param>
    /// <returns>Lower-case letters and digits only.</returns>
    /// <example>
    /// <code>
    /// LyricsTrackQuery.Normalize("Café Ｄ'Amour!"); // "cafedamour"
    /// </code>
    /// </example>
    public static string Normalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var rune in value.Normalize(NormalizationForm.FormKD).EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark)
            {
                continue;
            }

            if (Rune.IsLetterOrDigit(rune))
            {
                builder.Append(Rune.ToLowerInvariant(rune).ToString());
            }
        }

        return builder.ToString();
    }

    /// <inheritdoc />
    public bool Equals(LyricsTrackQuery? other) =>
        other is not null
        && NormalizedTitle == other.NormalizedTitle
        && NormalizedAlbum == other.NormalizedAlbum
        && NormalizedArtist == other.NormalizedArtist;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as LyricsTrackQuery);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(NormalizedTitle, NormalizedAlbum, NormalizedArtist);
}
