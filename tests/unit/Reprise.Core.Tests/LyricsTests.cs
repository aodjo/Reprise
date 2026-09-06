using System.Net;
using Reprise.Core;
using Xunit;

namespace Reprise.Core.Tests;

/// <summary>
/// Pins the lyrics model, the LRC parser, and the track query equality.
/// </summary>
public sealed class LyricsModelTests
{
    /// <summary>
    /// Focus follows the last started line; the current line honours end times.
    /// </summary>
    [Fact]
    public void FocusedAndCurrentLinesDiffer()
    {
        var lyrics = new SyncedLyrics(LyricsSource.Vibe,
        [
            new LyricLine(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(12), "first"),
            new LyricLine(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(24), "second"),
        ]);

        Assert.Null(lyrics.FocusedLineIndex(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, lyrics.FocusedLineIndex(TimeSpan.FromSeconds(15)));
        Assert.Null(lyrics.LineIndex(TimeSpan.FromSeconds(15)));
        Assert.Equal(1, lyrics.LineIndex(TimeSpan.FromSeconds(21)));
        Assert.Equal("second", lyrics.Line(TimeSpan.FromSeconds(23.9))?.Text);
        Assert.Null(lyrics.Line(TimeSpan.FromSeconds(24)));
    }

    /// <summary>
    /// LRC lines with several timestamps, an offset, and metadata all parse.
    /// </summary>
    [Fact]
    public void LrcParserHandlesTimestampsOffsetAndMetadata()
    {
        const string lrc = "[ar:Someone]\n[offset:500]\n[00:15.50][00:05.00]Chorus\n[00:10.000]Verse\n[00:12]\n";

        var lines = LrcParser.Parse(lrc, TimeSpan.FromSeconds(20));

        Assert.Equal(["Chorus", "Verse", "Chorus"], lines.Select(l => l.Text));
        Assert.Equal(TimeSpan.FromSeconds(5.5), lines[0].StartTime);
        Assert.Equal(TimeSpan.FromSeconds(10.5), lines[0].EndTime);
        Assert.Equal(TimeSpan.FromSeconds(16), lines[2].StartTime);
        Assert.Equal(TimeSpan.FromSeconds(20), lines[2].EndTime);
    }

    /// <summary>
    /// Without a duration the last line stays open-ended.
    /// </summary>
    [Fact]
    public void LrcParserLeavesLastLineOpenWithoutDuration()
    {
        var lines = LrcParser.Parse("[00:01.00]Only", TimeSpan.Zero);

        Assert.Single(lines);
        Assert.Null(lines[0].EndTime);
    }

    /// <summary>
    /// Queries compare on normalised text and ignore duration.
    /// </summary>
    [Fact]
    public void QueriesCompareLoosely()
    {
        var left = new LyricsTrackQuery("Café D'Amour", "Ｌｉｖｅ!", "Édith", TimeSpan.FromSeconds(200));
        var right = new LyricsTrackQuery("cafe damour", "live", "edith", TimeSpan.FromSeconds(180));

        Assert.Equal("cafedamour", left.NormalizedTitle);
        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.NotEqual(left, new LyricsTrackQuery("Other", "live", "edith", TimeSpan.Zero));
    }

    /// <summary>
    /// A session without a title yields no query.
    /// </summary>
    [Fact]
    public void SessionWithoutTitleHasNoQuery()
    {
        var session = new MediaSessionSnapshot(
            "spotify", "Spotify", PlaybackStatus.Playing, " ", "Artist", "Album",
            TimeSpan.FromSeconds(180), TimeSpan.Zero, 1, null, DateTimeOffset.UtcNow);

        Assert.Null(LyricsTrackQuery.From(session));
    }
}

/// <summary>
/// Covers the VIBE and LRCLIB parsing, scoring, and fallback order.
/// </summary>
public sealed class LyricsServiceTests
{
    private const string VibeSearchXml = """
        <response><result><tracks>
          <trackId>111</trackId><trackTitle>Bridge Song</trackTitle>
          <artists><artists><artistName>Bridge Artist</artistName></artists></artists>
          <album><albumTitle>Bridge Album</albumTitle></album>
          <playTime>3:00</playTime><hasSyncLyric>true</hasSyncLyric><isAdult>false</isAdult>
        </tracks><tracks>
          <trackId>222</trackId><trackTitle>Bridge Song (Live)</trackTitle>
          <artists><artists><artistName>Bridge Artist</artistName></artists></artists>
          <album><albumTitle>Live</albumTitle></album>
          <playTime>3:40</playTime><hasSyncLyric>false</hasSyncLyric><isAdult>false</isAdult>
        </tracks></result></response>
        """;

    private const string VibeLyricXml = """
        <response><result><lyric><hasSyncLyric>true</hasSyncLyric><syncLyric>
          <startTimeIndex><startTimeIndex>12.5</startTimeIndex><startTimeIndex>15.0</startTimeIndex></startTimeIndex>
          <endTimeIndex><endTimeIndex>14.0</endTimeIndex><endTimeIndex>18.0</endTimeIndex></endTimeIndex>
          <contents><contents><languageType>default</languageType><text><text>첫 줄</text><text>둘째 줄</text></text></contents></contents>
        </syncLyric></lyric></result></response>
        """;

    private static readonly LyricsTrackQuery Query =
        new("Bridge Song", "Bridge Album", "Bridge Artist", TimeSpan.FromSeconds(180));

    /// <summary>
    /// The VIBE search reply is read into candidates with their flags.
    /// </summary>
    [Fact]
    public void VibeSearchParsesCandidates()
    {
        var candidates = LyricsService.ParseVibeSearch(VibeSearchXml);

        Assert.Equal(2, candidates.Count);
        Assert.Equal("111", candidates[0].TrackId);
        Assert.Equal(["Bridge Artist"], candidates[0].Artists);
        Assert.Equal(TimeSpan.FromSeconds(180), candidates[0].Duration);
        Assert.True(candidates[0].HasSyncedLyrics);
        Assert.False(candidates[1].HasSyncedLyrics);
    }

    /// <summary>
    /// Only a synced, plausible candidate is chosen.
    /// </summary>
    [Fact]
    public void BestVibeCandidateRequiresSyncedLyricsAndAMatch()
    {
        var candidates = LyricsService.ParseVibeSearch(VibeSearchXml);

        Assert.Equal("111", LyricsService.BestVibeCandidate(Query, candidates)?.TrackId);
        Assert.Null(LyricsService.BestVibeCandidate(
            new LyricsTrackQuery("Unrelated", "", "Nobody", TimeSpan.Zero),
            candidates));
    }

    /// <summary>
    /// VIBE lyrics carry explicit end times.
    /// </summary>
    [Fact]
    public void VibeLyricsParseWithEndTimes()
    {
        var lyrics = LyricsService.ParseVibeLyrics(VibeLyricXml);

        Assert.NotNull(lyrics);
        Assert.Equal(LyricsSource.Vibe, lyrics.Source);
        Assert.Equal(["첫 줄", "둘째 줄"], lyrics.Lines.Select(l => l.Text));
        Assert.Equal(TimeSpan.FromSeconds(14), lyrics.Lines[0].EndTime);
    }

    /// <summary>
    /// Among matching LRCLIB results the closest duration wins.
    /// </summary>
    [Fact]
    public void BestLrclibResultPrefersClosestDuration()
    {
        var results = new List<LyricsService.LrclibResult>
        {
            new("Bridge Song", "Bridge Artist", null, 240, "[00:01.00]x"),
            new("Bridge Song", "Bridge Artist", null, 182, "[00:01.00]y"),
            new("Bridge Song", "Bridge Artist", null, 180, null),
            new("Other", "Bridge Artist", null, 180, "[00:01.00]z"),
        };

        Assert.Equal(182, LyricsService.BestLrclibResult(Query, results)?.Duration);
    }

    /// <summary>
    /// VIBE is tried first, then LRCLIB's exact match, then its search.
    /// </summary>
    [Fact]
    public async Task FallsBackFromVibeToLrclibSearch()
    {
        var handler = new FakeHandler
        {
            ["apis.naver.com"] = (HttpStatusCode.ServiceUnavailable, ""),
            ["lrclib.net/api/get"] = (HttpStatusCode.NotFound, "{}"),
            ["lrclib.net/api/search"] = (HttpStatusCode.OK,
                """[{"trackName":"Bridge Song","artistName":"Bridge Artist","albumName":null,"duration":181.0,"syncedLyrics":"[00:12.00]Hello\n[00:15.00]World"}]"""),
        };
        var service = new LyricsService(handler);

        var lyrics = await service.FetchSyncedLyricsAsync(Query);

        Assert.NotNull(lyrics);
        Assert.Equal(LyricsSource.Lrclib, lyrics.Source);
        Assert.Equal(["Hello", "World"], lyrics.Lines.Select(l => l.Text));
        Assert.Equal(TimeSpan.FromSeconds(180), lyrics.Lines[1].EndTime);
        Assert.Contains(handler.Requests, r => r.Contains("v3/search/track") && r.Contains("query=Bridge%20Song%20Bridge%20Artist"));
        Assert.Contains(handler.Requests, r => r.Contains("api/get?") && r.Contains("duration=180"));
    }

    /// <summary>
    /// A VIBE hit short-circuits the LRCLIB lookups.
    /// </summary>
    [Fact]
    public async Task VibeHitSkipsLrclib()
    {
        var handler = new FakeHandler
        {
            ["v3/search/track"] = (HttpStatusCode.OK, VibeSearchXml),
            ["vibe/v4/lyric/111"] = (HttpStatusCode.OK, VibeLyricXml),
        };
        var service = new LyricsService(handler);

        var lyrics = await service.FetchSyncedLyricsAsync(Query);

        Assert.Equal(LyricsSource.Vibe, lyrics?.Source);
        Assert.DoesNotContain(handler.Requests, r => r.Contains("lrclib"));
    }

    /// <summary>
    /// Scripted HTTP stack keyed by a substring of the request address.
    /// </summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly List<(string Match, HttpStatusCode Status, string Body)> _routes = [];

        /// <summary>
        /// Addresses requested so far.
        /// </summary>
        public List<string> Requests { get; } = [];

        /// <summary>
        /// Adds a route.
        /// </summary>
        /// <param name="match">Substring of the address.</param>
        public (HttpStatusCode, string) this[string match]
        {
            set => _routes.Add((match, value.Item1, value.Item2));
        }

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var address = request.RequestUri!.AbsoluteUri;
            Requests.Add(address);
            var route = _routes.FirstOrDefault(r => address.Contains(r.Match, StringComparison.Ordinal));
            var response = route.Match is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(route.Status) { Content = new StringContent(route.Body) };
            return Task.FromResult(response);
        }
    }
}
