using Reprise.Core;
using Reprise.Platform.Windows.Media;
using Xunit;

namespace Reprise.Platform.Windows.Tests;

/// <summary>
/// Verifies exact session routing, disappearing players, and native timeline normalization.
/// </summary>
public sealed class WindowsMediaSessionTests
{
    /// <summary>
    /// Multiple sessions from one application keep separate targets for commands and seeks.
    /// </summary>
    [Fact]
    public async Task RoutesCommandsToExactSession()
    {
        var first = new Session("windows:chrome:1");
        var second = new Session("windows:chrome:2");
        var service = new WindowsMediaSessionService(new Source(first, second));
        await service.SendCommandAsync(second.Id, PlaybackCommand.Next);
        await service.SeekAsync(first.Id, TimeSpan.FromSeconds(12));
        Assert.Null(first.Command);
        Assert.Equal(PlaybackCommand.Next, second.Command);
        Assert.Equal(TimeSpan.FromSeconds(12), first.Position);
        Assert.Null(second.Position);
    }

    /// <summary>
    /// Losing one session does not hide the remaining player's real metadata.
    /// </summary>
    [Fact]
    public async Task IsolatesDisappearingPlayer()
    {
        var first = new Session("gone") { Error = new System.Runtime.InteropServices.COMException("closed") };
        var second = new Session("available");
        var sessions = await new WindowsMediaSessionService(new Source(first, second)).GetSessionsAsync();
        Assert.Equal(second.Id, Assert.Single(sessions).PlayerId);
    }

    /// <summary>
    /// A removed target must not fall back to controlling another active application.
    /// </summary>
    [Fact]
    public async Task MissingTargetDoesNotControlAnotherPlayer()
    {
        var session = new Session("available");
        var service = new WindowsMediaSessionService(new Source(session));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendCommandAsync("gone", PlaybackCommand.PlayPause));
        Assert.Null(session.Command);
    }

    /// <summary>Volume changes target the selected player and reject invalid values before reaching native audio.</summary>
    [Fact]
    public async Task RoutesAndValidatesApplicationVolume()
    {
        var first = new Session("first");
        var second = new Session("second");
        var service = new WindowsMediaSessionService(new Source(first, second));
        await service.SetVolumeAsync(second.Id, 0.4);
        Assert.Null(first.Volume);
        Assert.Equal(0.4, second.Volume);
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, -0.1, 1.1 })
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SetVolumeAsync(second.Id, invalid));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetVolumeAsync("gone", 0.5));
    }

    /// <summary>Spotify mixer matching never selects another app or grants all browser tabs a shared volume control.</summary>
    [Theory]
    [InlineData("Spotify.exe", "Spotify", true)]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify", true)]
    [InlineData("Spotify.exe", "Chrome", false)]
    [InlineData("chrome.exe", "Chrome", false)]
    [InlineData("fake-spotify.exe", "Spotify", false)]
    [InlineData("com.kakaoent.melon.player", "Melon Player", true)]
    public void MatchesOnlyDedicatedPlayerProcesses(string appId, string process, bool expected) =>
        Assert.Equal(expected, WindowsApplicationVolume.MatchesProcess(appId, process));

    /// <summary>
    /// Unsupported controls report failure and negative seeks are rejected before reaching Windows.
    /// </summary>
    [Fact]
    public async Task RejectsUnsupportedControls()
    {
        var session = new Session("available") { Accepted = false };
        var service = new WindowsMediaSessionService(new Source(session));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendCommandAsync(session.Id, PlaybackCommand.Next));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SeekAsync(session.Id, TimeSpan.FromSeconds(2)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SeekAsync(session.Id, TimeSpan.FromSeconds(-1)));
    }

    /// <summary>
    /// Cancellation remains cancellation rather than being mistaken for a disappearing player.
    /// </summary>
    [Fact]
    public async Task PropagatesCancellation()
    {
        var service = new WindowsMediaSessionService(new Source(new Session("cancelled") { Error = new OperationCanceledException() }));
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.GetSessionsAsync());
    }

    /// <summary>
    /// The Windows timeline's nonzero origin is removed before advancing at the reported speed.
    /// </summary>
    [Fact]
    public void NormalizesAndAdvancesTimeline()
    {
        var now = DateTimeOffset.UtcNow;
        var (duration, position) = WindowsPlaybackTimeline.Observe(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(110),
            TimeSpan.FromSeconds(20), now.AddSeconds(-2), now, true, 1.5);
        Assert.Equal(TimeSpan.FromSeconds(100), duration);
        Assert.Equal(TimeSpan.FromSeconds(13), position);
    }

    /// <summary>
    /// Missing timestamps do not incorrectly jump a newly observed track to its end.
    /// </summary>
    [Fact]
    public void IgnoresMissingUpdateTimeAndClampsPositions()
    {
        var now = DateTimeOffset.UtcNow;
        var (_, position) = WindowsPlaybackTimeline.Observe(TimeSpan.Zero, TimeSpan.FromSeconds(100),
            TimeSpan.FromSeconds(10), DateTimeOffset.MinValue, now, true, 1);
        Assert.Equal(TimeSpan.FromSeconds(10), position);
        (_, position) = WindowsPlaybackTimeline.Observe(TimeSpan.Zero, TimeSpan.FromSeconds(100),
            TimeSpan.FromSeconds(99), now.AddSeconds(-5), now, true, 1);
        Assert.Equal(TimeSpan.FromSeconds(100), position);
    }

    /// <summary>
    /// Packaged and classic application identifiers resolve to useful names.
    /// </summary>
    [Theory]
    [InlineData("SpotifyAB.SpotifyMusic_abc!Spotify", "Spotify")]
    [InlineData("chrome.exe", "Google Chrome")]
    [InlineData("MSEdge", "Microsoft Edge")]
    [InlineData("Microsoft.ZuneMusic_abc!App", "Media Player")]
    public void NamesPlayers(string id, string expected) => Assert.Equal(expected, WindowsPlayerNames.DisplayName(id));

    /// <summary>
    /// Test session preserving only the command boundary, with no WinRT substitution in product code.
    /// </summary>
    private sealed class Session(string id) : IWindowsMediaSession
    {
        public string Id => id;
        public Exception? Error { get; init; }
        public bool Accepted { get; init; } = true;
        public PlaybackCommand? Command { get; private set; }
        public TimeSpan? Position { get; private set; }
        public double? Volume { get; private set; }

        /// <inheritdoc />
        public Task<bool> SetVolumeAsync(double volume, CancellationToken cancellationToken)
        {
            Volume = volume;
            return Task.FromResult(Accepted);
        }

        /// <inheritdoc />
        public Task<MediaSessionSnapshot> ReadAsync(CancellationToken cancellationToken) => Error is not null
            ? Task.FromException<MediaSessionSnapshot>(Error)
            : Task.FromResult(new MediaSessionSnapshot(Id, "Test player", PlaybackStatus.Playing,
                "Track", "Artist", "Album", TimeSpan.FromMinutes(3), TimeSpan.Zero, null, null, DateTimeOffset.UtcNow));

        /// <inheritdoc />
        public Task<bool> SendAsync(PlaybackCommand command, CancellationToken cancellationToken)
        {
            Command = command;
            return Task.FromResult(Accepted);
        }

        /// <inheritdoc />
        public Task<bool> SeekAsync(TimeSpan position, CancellationToken cancellationToken)
        {
            Position = position;
            return Task.FromResult(Accepted);
        }
    }

    /// <summary>Source retaining the supplied test sessions across polls.</summary>
    private sealed class Source(params IWindowsMediaSession[] sessions) : IWindowsMediaSessionSource
    {
        /// <inheritdoc />
        public Task<IReadOnlyList<IWindowsMediaSession>> GetSessionsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IWindowsMediaSession>>(sessions);
    }
}
