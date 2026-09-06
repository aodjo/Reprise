using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;

namespace Reprise.Core;

/// <summary>
/// Looks up time-synced lyrics for a track.
/// </summary>
public interface ILyricsService
{
    /// <summary>
    /// Fetches synced lyrics for a track, if any service has them.
    /// </summary>
    /// <param name="query">Track to look up.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The lyrics, or null when none are available.</returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled.
    /// </exception>
    Task<SyncedLyrics?> FetchSyncedLyricsAsync(
        LyricsTrackQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Fetches time-synced lyrics from VIBE and LRCLIB.
/// </summary>
/// <remarks>
/// A port of the macOS <c>LyricsService</c>. VIBE is tried first because it
/// carries line end times, which LRC cannot express, and covers Korean
/// releases well; LRCLIB is the broader fallback. Every network or parse
/// failure resolves to "no lyrics" rather than an error, since lyrics are
/// an extra and the panel must not report a missing verse as a fault.
/// </remarks>
public sealed class LyricsService : ILyricsService
{
    private static readonly Uri VibeBaseUri = new("https://apis.naver.com/vibeWeb/musicapiweb/");
    private static readonly Uri LrclibBaseUri = new("https://lrclib.net/api/");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;

    /// <summary>
    /// Creates a service that talks to the real endpoints.
    /// </summary>
    public LyricsService()
        : this(new HttpClientHandler())
    {
    }

    /// <summary>
    /// Creates a service over a supplied HTTP stack, for tests.
    /// </summary>
    /// <param name="handler">Handler every request goes through.</param>
    /// <example>
    /// <code>
    /// var service = new LyricsService(new FakeHandler());
    /// </code>
    /// </example>
    public LyricsService(HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Reprise", "2.0"));
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(https://junx.dev)"));
    }

    /// <summary>
    /// Fetches synced lyrics, preferring VIBE and falling back to LRCLIB.
    /// </summary>
    /// <param name="query">Track to look up.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The lyrics, or null when neither service has them.</returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled.
    /// </exception>
    /// <example>
    /// <code>
    /// var lyrics = await service.FetchSyncedLyricsAsync(LyricsTrackQuery.From(session)!);
    /// </code>
    /// </example>
    public async Task<SyncedLyrics?> FetchSyncedLyricsAsync(
        LyricsTrackQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (await FetchVibeAsync(query, cancellationToken) is { } vibe)
        {
            return vibe;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await FetchLrclibAsync(query, cancellationToken);
    }

    /// <summary>
    /// Searches VIBE for the track and reads its synced lyrics.
    /// </summary>
    /// <param name="query">Track to look up.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The lyrics, or null on any miss or failure.</returns>
    private async Task<SyncedLyrics?> FetchVibeAsync(
        LyricsTrackQuery query,
        CancellationToken cancellationToken)
    {
        try
        {
            var searchXml = await GetStringAsync(
                BuildUri(VibeBaseUri, "v3/search/track", new[]
                {
                    ("query", $"{query.Title} {query.Artist}".Trim()),
                    ("display", "10"),
                    ("sort", "RELEVANCE"),
                }),
                cancellationToken);
            var candidate = BestVibeCandidate(query, ParseVibeSearch(searchXml));
            if (candidate is null)
            {
                return null;
            }

            var lyricXml = await GetStringAsync(
                BuildUri(VibeBaseUri, $"vibe/v4/lyric/{Uri.EscapeDataString(candidate.TrackId)}", []),
                cancellationToken);
            return ParseVibeLyrics(lyricXml);
        }
        catch (Exception exception) when (IsRecoverable(exception, cancellationToken))
        {
            return null;
        }
    }

    /// <summary>
    /// Reads lyrics from LRCLIB, by exact match first and then by search.
    /// </summary>
    /// <param name="query">Track to look up.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The lyrics, or null on any miss or failure.</returns>
    private async Task<SyncedLyrics?> FetchLrclibAsync(
        LyricsTrackQuery query,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await GetStringAsync(
                BuildUri(LrclibBaseUri, "get", LrclibQuery(query)),
                cancellationToken);
            var result = JsonSerializer.Deserialize<LrclibResult>(json, JsonOptions);
            if (result is not null && FromLrclib(result, query.Duration) is { } lyrics)
            {
                return lyrics;
            }
        }
        catch (Exception exception) when (IsRecoverable(exception, cancellationToken))
        {
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var json = await GetStringAsync(
                BuildUri(LrclibBaseUri, "search", new[]
                {
                    ("track_name", query.Title),
                    ("artist_name", query.Artist),
                }),
                cancellationToken);
            var results = JsonSerializer.Deserialize<List<LrclibResult>>(json, JsonOptions) ?? [];
            return BestLrclibResult(query, results) is { } best
                ? FromLrclib(best, query.Duration)
                : null;
        }
        catch (Exception exception) when (IsRecoverable(exception, cancellationToken))
        {
            return null;
        }
    }

    /// <summary>
    /// Performs one GET and returns the body.
    /// </summary>
    /// <param name="uri">Address to fetch.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="HttpRequestException">Thrown on a non-success status.</exception>
    private async Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    /// <summary>
    /// Decides whether a failure means "no lyrics" rather than a bug.
    /// </summary>
    /// <param name="exception">Failure to classify.</param>
    /// <param name="cancellationToken">Token the caller passed in.</param>
    /// <returns>True for network, timeout, and parse failures.</returns>
    private static bool IsRecoverable(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested
        && exception is HttpRequestException
            or TaskCanceledException
            or XmlException
            or JsonException
            or FormatException
            or InvalidOperationException;

    /// <summary>
    /// Builds an address with percent-encoded query parameters.
    /// </summary>
    /// <param name="baseUri">Service root, ending in a slash.</param>
    /// <param name="path">Path below the root.</param>
    /// <param name="parameters">Query parameters in order.</param>
    /// <returns>The complete address.</returns>
    internal static Uri BuildUri(Uri baseUri, string path, IEnumerable<(string Name, string Value)> parameters)
    {
        var query = string.Join(
            "&",
            parameters.Select(p => $"{Uri.EscapeDataString(p.Name)}={Uri.EscapeDataString(p.Value)}"));
        return new Uri(baseUri, query.Length == 0 ? path : $"{path}?{query}");
    }

    /// <summary>
    /// Parameters for LRCLIB's exact-match endpoint.
    /// </summary>
    /// <param name="query">Track to look up.</param>
    /// <returns>Title and artist, plus album and duration when known.</returns>
    internal static IReadOnlyList<(string Name, string Value)> LrclibQuery(LyricsTrackQuery query)
    {
        var items = new List<(string, string)>
        {
            ("track_name", query.Title),
            ("artist_name", query.Artist),
        };
        if (query.Album.Length > 0)
        {
            items.Add(("album_name", query.Album));
        }

        if (query.Duration > TimeSpan.Zero)
        {
            items.Add(("duration", Math.Round(query.Duration.TotalSeconds).ToString(CultureInfo.InvariantCulture)));
        }

        return items;
    }

    /// <summary>
    /// A track from a VIBE search reply.
    /// </summary>
    /// <param name="TrackId">VIBE's identifier, used to fetch lyrics.</param>
    /// <param name="Title">Track title.</param>
    /// <param name="Album">Album name, possibly empty.</param>
    /// <param name="Artists">Credited artists.</param>
    /// <param name="Duration">Track length, or zero when unparsable.</param>
    /// <param name="HasSyncedLyrics">Whether VIBE holds timed lyrics for it.</param>
    /// <param name="IsAdult">Whether VIBE flags it as adult-only.</param>
    internal sealed record VibeCandidate(
        string TrackId,
        string Title,
        string Album,
        IReadOnlyList<string> Artists,
        TimeSpan Duration,
        bool HasSyncedLyrics,
        bool IsAdult);

    /// <summary>
    /// Reads the tracks out of a VIBE search reply.
    /// </summary>
    /// <param name="xml">Reply body.</param>
    /// <returns>Every track that carries an id and a title.</returns>
    /// <exception cref="XmlException">Thrown when the body is not XML.</exception>
    internal static IReadOnlyList<VibeCandidate> ParseVibeSearch(string xml)
    {
        var document = XDocument.Parse(xml);
        var candidates = new List<VibeCandidate>();
        foreach (var track in document.Descendants("tracks"))
        {
            var trackId = Text(track.Element("trackId"));
            var title = Text(track.Element("trackTitle"));
            if (trackId is null || title is null)
            {
                continue;
            }

            candidates.Add(new VibeCandidate(
                trackId,
                title,
                Text(track.Element("album")?.Element("albumTitle")) ?? string.Empty,
                track.Element("artists")?.Descendants("artistName")
                    .Select(Text).OfType<string>().ToList() ?? [],
                ParseClock(Text(track.Element("playTime")) ?? string.Empty),
                Text(track.Element("hasSyncLyric")) == "true",
                Text(track.Element("isAdult")) == "true"));
        }

        return candidates;
    }

    /// <summary>
    /// Picks the VIBE track most likely to be the one playing.
    /// </summary>
    /// <remarks>
    /// Only tracks with synced lyrics qualify, and the title must match
    /// loosely. Artist, album, and duration then vote: at least one must
    /// agree, and the more that agree the higher the score.
    /// </remarks>
    /// <param name="query">Track being played.</param>
    /// <param name="candidates">Search results.</param>
    /// <returns>The best candidate, or null when none is plausible.</returns>
    internal static VibeCandidate? BestVibeCandidate(
        LyricsTrackQuery query,
        IReadOnlyList<VibeCandidate> candidates)
    {
        VibeCandidate? best = null;
        var bestScore = int.MinValue;
        foreach (var candidate in candidates)
        {
            if (!candidate.HasSyncedLyrics || candidate.IsAdult)
            {
                continue;
            }

            var title = LyricsTrackQuery.Normalize(candidate.Title);
            if (title.Length == 0 || query.NormalizedTitle.Length == 0 || !Overlaps(title, query.NormalizedTitle))
            {
                continue;
            }

            var artistMatches = query.NormalizedArtist.Length == 0
                || candidate.Artists.Any(artist => Overlaps(LyricsTrackQuery.Normalize(artist), query.NormalizedArtist));
            var albumMatches = query.NormalizedAlbum.Length > 0
                && LyricsTrackQuery.Normalize(candidate.Album) == query.NormalizedAlbum;
            var durationDifference = query.Duration > TimeSpan.Zero && candidate.Duration > TimeSpan.Zero
                ? (query.Duration - candidate.Duration).Duration().TotalSeconds
                : double.PositiveInfinity;
            var durationMatches = durationDifference <= 6;
            if (!artistMatches && !albumMatches && !durationMatches)
            {
                continue;
            }

            var score = title == query.NormalizedTitle ? 100 : 70;
            score += artistMatches ? 50 : 0;
            score += albumMatches ? 20 : 0;
            score += durationMatches ? 25 : durationDifference <= 12 ? 8 : 0;
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    /// Reads timed lines out of a VIBE lyric reply.
    /// </summary>
    /// <param name="xml">Reply body.</param>
    /// <returns>The lyrics, or null when the track has no synced lyrics.</returns>
    /// <exception cref="XmlException">Thrown when the body is not XML.</exception>
    internal static SyncedLyrics? ParseVibeLyrics(string xml)
    {
        var document = XDocument.Parse(xml);
        if (Text(document.Descendants("hasSyncLyric").FirstOrDefault()) != "true"
            || document.Descendants("syncLyric").FirstOrDefault() is not { } sync)
        {
            return null;
        }

        var starts = sync.Element("startTimeIndex")?.Elements("startTimeIndex")
            .Select(ParseSeconds).OfType<double>().ToList() ?? [];
        var ends = sync.Element("endTimeIndex")?.Elements("endTimeIndex")
            .Select(ParseSeconds).OfType<double>().ToList() ?? [];
        var contents = sync.Element("contents")?.Elements("contents").ToList() ?? [];
        var content = contents.FirstOrDefault(c => Text(c.Element("languageType")) == "default")
            ?? contents.FirstOrDefault();
        var texts = content?.Element("text")?.Elements("text")
            .Select(e => e.Value.Trim()).ToList() ?? [];
        if (starts.Count == 0 || starts.Count != texts.Count)
        {
            return null;
        }

        var lines = new List<LyricLine>(starts.Count);
        for (var index = 0; index < starts.Count; index++)
        {
            if (texts[index].Length == 0)
            {
                continue;
            }

            double? end = index < ends.Count
                ? ends[index]
                : index + 1 < starts.Count ? starts[index + 1] : null;
            lines.Add(new LyricLine(
                TimeSpan.FromSeconds(starts[index]),
                end is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
                texts[index]));
        }

        return lines.Count == 0 ? null : new SyncedLyrics(LyricsSource.Vibe, lines);
    }

    /// <summary>
    /// One track from LRCLIB.
    /// </summary>
    /// <param name="TrackName">Track title.</param>
    /// <param name="ArtistName">Artist credit.</param>
    /// <param name="AlbumName">Album name, possibly absent.</param>
    /// <param name="Duration">Track length in seconds.</param>
    /// <param name="SyncedLyrics">LRC text, or null when only plain lyrics exist.</param>
    internal sealed record LrclibResult(
        [property: JsonPropertyName("trackName")] string TrackName,
        [property: JsonPropertyName("artistName")] string ArtistName,
        [property: JsonPropertyName("albumName")] string? AlbumName,
        [property: JsonPropertyName("duration")] double Duration,
        [property: JsonPropertyName("syncedLyrics")] string? SyncedLyrics);

    /// <summary>
    /// Picks the LRCLIB search result closest in length to the track.
    /// </summary>
    /// <param name="query">Track being played.</param>
    /// <param name="results">Search results.</param>
    /// <returns>The best result, or null when none has synced lyrics and matches.</returns>
    internal static LrclibResult? BestLrclibResult(
        LyricsTrackQuery query,
        IReadOnlyList<LrclibResult> results)
    {
        LrclibResult? best = null;
        var bestDifference = double.PositiveInfinity;
        foreach (var result in results)
        {
            if (string.IsNullOrEmpty(result.SyncedLyrics))
            {
                continue;
            }

            var title = LyricsTrackQuery.Normalize(result.TrackName);
            var artist = LyricsTrackQuery.Normalize(result.ArtistName);
            var titleMatches = Overlaps(title, query.NormalizedTitle);
            var artistMatches = query.NormalizedArtist.Length == 0 || Overlaps(artist, query.NormalizedArtist);
            if (!titleMatches || !artistMatches)
            {
                continue;
            }

            var difference = Math.Abs(result.Duration - query.Duration.TotalSeconds);
            if (difference < bestDifference)
            {
                bestDifference = difference;
                best = result;
            }
        }

        return best;
    }

    /// <summary>
    /// Turns an LRCLIB result into lyrics.
    /// </summary>
    /// <param name="result">Result to convert.</param>
    /// <param name="duration">
    /// Length of the playing track, used to close the last line; the
    /// result's own length is used when unknown.
    /// </param>
    /// <returns>The lyrics, or null when the result carries no timed lines.</returns>
    internal static SyncedLyrics? FromLrclib(LrclibResult result, TimeSpan duration)
    {
        if (string.IsNullOrEmpty(result.SyncedLyrics))
        {
            return null;
        }

        var lines = LrcParser.Parse(
            result.SyncedLyrics,
            duration > TimeSpan.Zero ? duration : TimeSpan.FromSeconds(result.Duration));
        return lines.Count == 0 ? null : new SyncedLyrics(LyricsSource.Lrclib, lines);
    }

    /// <summary>
    /// Whether two normalised names are the same or one contains the other.
    /// </summary>
    /// <param name="left">One name.</param>
    /// <param name="right">The other name.</param>
    /// <returns>True when they plausibly refer to the same thing.</returns>
    private static bool Overlaps(string left, string right) =>
        left.Length > 0
        && right.Length > 0
        && (left == right || left.Contains(right, StringComparison.Ordinal) || right.Contains(left, StringComparison.Ordinal));

    /// <summary>
    /// Trimmed element text, or null when the element is absent or blank.
    /// </summary>
    /// <param name="element">Element to read.</param>
    /// <returns>The text, or null.</returns>
    private static string? Text(XElement? element)
    {
        var value = element?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>
    /// Parses an element holding a number of seconds.
    /// </summary>
    /// <param name="element">Element to read.</param>
    /// <returns>The value, or null when not numeric.</returns>
    private static double? ParseSeconds(XElement element) =>
        double.TryParse(element.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    /// <summary>
    /// Parses a <c>m:ss</c> clock into a duration.
    /// </summary>
    /// <param name="value">Clock text.</param>
    /// <returns>The duration, or zero when malformed.</returns>
    private static TimeSpan ParseClock(string value)
    {
        var parts = value.Split(':');
        if (parts.Length != 2
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromSeconds(minutes * 60 + seconds);
    }
}
