using Reprise.Core;
using Reprise.Desktop;
using Xunit;

namespace Reprise.Desktop.Tests;

/// <summary>
/// Covers the panel view model against a scripted media backend.
/// </summary>
/// <remarks>
/// The backend, artwork loader, and clock are all fakes, so these pin the
/// reconciliation rules - what the scrubber shows during a seek, how volume
/// changes coalesce, when artwork reloads - without any display or bus.
/// </remarks>
public sealed class NowPlayingViewModelTests
{
    private static readonly DateTimeOffset Observed =
        new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A poll publishes the selected session and fetches its artwork.
    /// </summary>
    [Fact]
    public async Task RefreshPublishesSessionAndArtwork()
    {
        var art = new Uri("https://example.com/art.jpg");
        var backend = new FakeBackend { Sessions = [Session(art: art)] };
        var loader = new FakeArtworkLoader { [art] = [1, 2, 3] };
        using var viewModel = new NowPlayingViewModel(backend, loader, new FixedClock(Observed));

        await viewModel.RefreshAsync();

        Assert.Equal("spotify", viewModel.ActiveSession?.PlayerId);
        Assert.True(viewModel.IsRunning);
        Assert.Equal(PanelPlayerLogo.Spotify, viewModel.PlayerLogo);
        Assert.Equal(new byte[] { 1, 2, 3 }, viewModel.ArtworkData);
        Assert.Equal(1, loader.Loads);
    }

    /// <summary>
    /// Artwork is fetched once per URI, not once per poll.
    /// </summary>
    [Fact]
    public async Task ArtworkIsNotReloadedWhileTheUriIsUnchanged()
    {
        var art = new Uri("file:///tmp/cover.png");
        var backend = new FakeBackend { Sessions = [Session(art: art)] };
        var loader = new FakeArtworkLoader { [art] = [9] };
        using var viewModel = new NowPlayingViewModel(backend, loader, new FixedClock(Observed));

        await viewModel.RefreshAsync();
        await viewModel.RefreshAsync();
        backend.Sessions = [Session(art: null)];
        await viewModel.RefreshAsync();

        Assert.Equal(1, loader.Loads);
        Assert.Null(viewModel.ArtworkData);
    }

    /// <summary>
    /// A playing session's position moves forward with the clock.
    /// </summary>
    [Fact]
    public async Task DisplayedPositionAdvancesWhilePlaying()
    {
        var clock = new FixedClock(Observed);
        var backend = new FakeBackend { Sessions = [Session(position: 10)] };
        using var viewModel = new NowPlayingViewModel(backend, new FakeArtworkLoader(), clock);
        await viewModel.RefreshAsync();

        clock.Now = Observed.AddSeconds(2.5);

        Assert.Equal(TimeSpan.FromSeconds(12.5), viewModel.DisplayedPosition());
    }

    /// <summary>
    /// The scrubber holds the seek target until the player confirms it.
    /// </summary>
    /// <remarks>
    /// Without this the knob would snap back to the pre-seek position for
    /// the poll or two it takes the player to report the new one.
    /// </remarks>
    [Fact]
    public async Task SeekTargetIsHeldUntilConfirmed()
    {
        var clock = new FixedClock(Observed);
        var backend = new FakeBackend { Sessions = [Session(position: 10)] };
        using var viewModel = new NowPlayingViewModel(backend, new FakeArtworkLoader(), clock);
        await viewModel.RefreshAsync();

        viewModel.BeginSeek(TimeSpan.FromSeconds(10));
        viewModel.UpdateSeek(TimeSpan.FromSeconds(90));
        Assert.True(viewModel.IsSeeking);
        Assert.Equal(TimeSpan.FromSeconds(90), viewModel.DisplayedPosition());

        await viewModel.EndSeekAsync();

        Assert.Equal(("spotify", TimeSpan.FromSeconds(90)), backend.LastSeek);
        Assert.False(viewModel.IsSeeking);
        Assert.Equal(TimeSpan.FromSeconds(90), viewModel.DisplayedPosition());

        backend.Sessions = [Session(position: 90.4)];
        await viewModel.RefreshAsync();
        clock.Now = Observed.AddSeconds(1);

        Assert.Equal(TimeSpan.FromSeconds(91.4), viewModel.DisplayedPosition());
    }

    /// <summary>
    /// A seek the player never reflects is dropped after a few polls.
    /// </summary>
    [Fact]
    public async Task UnconfirmedSeekIsAbandoned()
    {
        var clock = new FixedClock(Observed);
        var backend = new FakeBackend { Sessions = [Session(position: 10, status: PlaybackStatus.Paused)] };
        using var viewModel = new NowPlayingViewModel(backend, new FakeArtworkLoader(), clock);
        await viewModel.RefreshAsync();

        await viewModel.SeekAsync(TimeSpan.FromSeconds(120));
        Assert.Equal(TimeSpan.FromSeconds(120), viewModel.DisplayedPosition());

        await viewModel.RefreshAsync();
        await viewModel.RefreshAsync();

        Assert.Equal(TimeSpan.FromSeconds(10), viewModel.DisplayedPosition());
    }

    /// <summary>
    /// Volume follows the player, and muting round-trips through the last level.
    /// </summary>
    [Fact]
    public async Task MuteTogglesBetweenSilenceAndTheLastAudibleLevel()
    {
        var backend = new FakeBackend { Sessions = [Session(volume: 0.64)] };
        using var viewModel = new NowPlayingViewModel(backend, new FakeArtworkLoader(), new FixedClock(Observed));
        await viewModel.RefreshAsync();
        Assert.Equal(64, viewModel.Volume);

        await viewModel.ToggleMuteAsync();
        Assert.Equal(0, viewModel.Volume);
        Assert.Equal(0.0, backend.Volumes[^1]);

        await viewModel.ToggleMuteAsync();
        Assert.Equal(64, viewModel.Volume);
        Assert.Equal(0.64, backend.Volumes[^1], precision: 9);
    }

    /// <summary>
    /// Rapid volume changes send only the first and the newest level.
    /// </summary>
    /// <remarks>
    /// A drag emits dozens of values a second; sending each would queue bus
    /// calls faster than the player answers them.
    /// </remarks>
    [Fact]
    public async Task VolumeChangesCoalesceWhileOneIsInFlight()
    {
        var backend = new FakeBackend { Sessions = [Session(volume: 0.5)], HoldVolumeWrites = true };
        using var viewModel = new NowPlayingViewModel(backend, new FakeArtworkLoader(), new FixedClock(Observed));
        await viewModel.RefreshAsync();

        var first = viewModel.SetVolumeAsync(10);
        var second = viewModel.SetVolumeAsync(20);
        var third = viewModel.SetVolumeAsync(30);
        Assert.Equal(30, viewModel.Volume);
        Assert.Equal([0.1], backend.Volumes);

        backend.ReleaseVolumeWrite();
        await Task.WhenAll(first, second, third);

        Assert.Equal([0.1, 0.3], backend.Volumes);
    }

    /// <summary>
    /// A failed command is shown, then cleared by the next successful one.
    /// </summary>
    [Fact]
    public async Task CommandFailureSurfacesAndClears()
    {
        var backend = new FakeBackend { Sessions = [Session()] };
        using var viewModel = new NowPlayingViewModel(backend, new FakeArtworkLoader(), new FixedClock(Observed));
        await viewModel.RefreshAsync();

        backend.CommandFailure = new InvalidOperationException("MPRIS Next failed");
        await viewModel.SendAsync(PlaybackCommand.Next);
        Assert.Equal("MPRIS Next failed", viewModel.ErrorText);

        backend.CommandFailure = null;
        await viewModel.SendAsync(PlaybackCommand.PlayPause);
        Assert.Null(viewModel.ErrorText);
    }

    /// <summary>
    /// A failed poll empties the panel and reports why.
    /// </summary>
    [Fact]
    public async Task PollFailureClearsTheSession()
    {
        var backend = new FakeBackend { Sessions = [Session()] };
        using var viewModel = new NowPlayingViewModel(backend, new FakeArtworkLoader(), new FixedClock(Observed));
        await viewModel.RefreshAsync();

        backend.PollFailure = new InvalidOperationException("bus gone");
        await viewModel.RefreshAsync();

        Assert.Null(viewModel.ActiveSession);
        Assert.Equal("bus gone", viewModel.ErrorText);
    }

    /// <summary>
    /// Player ids map onto the marks the panel can draw.
    /// </summary>
    /// <param name="playerId">MPRIS player id.</param>
    /// <param name="expected">Mark that should be drawn.</param>
    [Theory]
    [InlineData("spotify", PanelPlayerLogo.Spotify)]
    [InlineData("chromium.instance12", PanelPlayerLogo.Generic)]
    [InlineData("YoutubeMusic", PanelPlayerLogo.YouTubeMusic)]
    [InlineData("youtube-music", PanelPlayerLogo.YouTubeMusic)]
    [InlineData(null, PanelPlayerLogo.Generic)]
    public void PlayerIdsMapToLogos(string? playerId, PanelPlayerLogo expected)
    {
        Assert.Equal(expected, NowPlayingViewModel.LogoFor(playerId));
    }

    /// <summary>
    /// Builds a playing Spotify session observed at the fixed test time.
    /// </summary>
    /// <param name="position">Position in seconds.</param>
    /// <param name="status">Playback status.</param>
    /// <param name="volume">Volume fraction.</param>
    /// <param name="art">Artwork URI.</param>
    /// <returns>A snapshot for the fake backend to serve.</returns>
    private static MediaSessionSnapshot Session(
        double position = 10,
        PlaybackStatus status = PlaybackStatus.Playing,
        double? volume = 0.64,
        Uri? art = null) => new(
            PlayerId: "spotify",
            PlayerName: "Spotify",
            Status: status,
            Title: "Bridge Song",
            Artist: "Bridge Artist",
            Album: "Bridge Album",
            Duration: TimeSpan.FromSeconds(180),
            Position: TimeSpan.FromSeconds(position),
            Volume: volume,
            ArtworkUri: art,
            ObservedAt: Observed);

    /// <summary>
    /// Clock the tests advance by hand.
    /// </summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        /// <summary>
        /// The time the clock reports.
        /// </summary>
        public DateTimeOffset Now { get; set; } = now;

        /// <summary>
        /// Returns <see cref="Now"/>.
        /// </summary>
        /// <returns>The fixed time.</returns>
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>
    /// Artwork loader serving bytes from a dictionary.
    /// </summary>
    private sealed class FakeArtworkLoader : Dictionary<Uri, byte[]>, IArtworkLoader
    {
        /// <summary>
        /// Number of loads requested.
        /// </summary>
        public int Loads { get; private set; }

        /// <summary>
        /// Returns the configured bytes, or null.
        /// </summary>
        /// <param name="uri">Artwork location.</param>
        /// <param name="cancellationToken">Ignored.</param>
        /// <returns>The bytes, or null when unconfigured.</returns>
        public Task<byte[]?> LoadAsync(Uri uri, CancellationToken cancellationToken)
        {
            Loads++;
            return Task.FromResult(TryGetValue(uri, out var bytes) ? bytes : null);
        }
    }

    /// <summary>
    /// Scripted media backend that records every command.
    /// </summary>
    private sealed class FakeBackend : IMediaSessionService
    {
        private TaskCompletionSource _volumeGate = new();

        /// <summary>
        /// Sessions the next poll returns.
        /// </summary>
        public IReadOnlyList<MediaSessionSnapshot> Sessions { get; set; } = [];

        /// <summary>
        /// Exception the next poll throws, or null.
        /// </summary>
        public Exception? PollFailure { get; set; }

        /// <summary>
        /// Exception every command throws, or null.
        /// </summary>
        public Exception? CommandFailure { get; set; }

        /// <summary>
        /// Whether volume writes block until <see cref="ReleaseVolumeWrite"/>.
        /// </summary>
        public bool HoldVolumeWrites { get; set; }

        /// <summary>
        /// Most recent seek, as player id and position.
        /// </summary>
        public (string PlayerId, TimeSpan Position)? LastSeek { get; private set; }

        /// <summary>
        /// Every volume written, in order.
        /// </summary>
        public List<double> Volumes { get; } = [];

        /// <summary>
        /// Lets held volume writes complete.
        /// </summary>
        public void ReleaseVolumeWrite()
        {
            var gate = _volumeGate;
            _volumeGate = new TaskCompletionSource();
            HoldVolumeWrites = false;
            gate.SetResult();
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<MediaSessionSnapshot>> GetSessionsAsync(
            CancellationToken cancellationToken = default) =>
            PollFailure is { } failure
                ? Task.FromException<IReadOnlyList<MediaSessionSnapshot>>(failure)
                : Task.FromResult(Sessions);

        /// <inheritdoc />
        public Task SendCommandAsync(
            string playerId,
            PlaybackCommand command,
            CancellationToken cancellationToken = default) =>
            CommandFailure is { } failure ? Task.FromException(failure) : Task.CompletedTask;

        /// <inheritdoc />
        public Task SeekAsync(
            string playerId,
            TimeSpan position,
            CancellationToken cancellationToken = default)
        {
            LastSeek = (playerId, position);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task SetVolumeAsync(
            string playerId,
            double volume,
            CancellationToken cancellationToken = default)
        {
            Volumes.Add(volume);
            if (HoldVolumeWrites)
            {
                await _volumeGate.Task;
            }
        }
    }
}
