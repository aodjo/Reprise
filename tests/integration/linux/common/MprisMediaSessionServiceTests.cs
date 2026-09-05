using Reprise.Core;
using Reprise.Platform.Linux.Mpris;
using Tmds.DBus.Protocol;
using Xunit;

namespace Reprise.Platform.Linux.Tests;

/// <summary>
/// Covers the MPRIS bridge from D-Bus property values to Reprise sessions.
/// </summary>
/// <remarks>
/// Runs against a fake <see cref="IMprisBus"/> rather than a live session bus,
/// because CI has no desktop session and no media players. Real
/// <c>VariantValue</c> instances are still used throughout, so the variant
/// unwrapping and type handling that the mapper exists for are genuinely
/// exercised; only the transport is stood in for.
/// </remarks>
public sealed class MprisMediaSessionServiceTests
{
    /// <summary>
    /// Every field a player publishes reaches the snapshot intact.
    /// </summary>
    /// <remarks>
    /// The broad assertion is deliberate: it pins the whole property mapping
    /// in one place, including the microsecond conversion and the derivation
    /// of PlayerId and PlayerName from the bus name.
    /// </remarks>
    [Fact]
    public async Task PlayerPropertiesBecomeLinuxMediaSessions()
    {
        var bus = new FakeMprisBus
        {
            Players =
            {
                ["org.mpris.MediaPlayer2.spotify"] = PlayerProperties(
                    status: "Playing",
                    title: "Bridge Song",
                    artists: ["Bridge Artist"],
                    album: "Bridge Album",
                    lengthMicroseconds: 180_000_000,
                    positionMicroseconds: 12_000_000,
                    volume: 0.64,
                    artUrl: "https://example.com/art.jpg"),
            },
        };
        var service = new MprisMediaSessionService(bus);

        var sessions = await service.GetSessionsAsync();

        var session = Assert.Single(sessions);
        Assert.Equal("spotify", session.PlayerId);
        Assert.Equal("Spotify", session.PlayerName);
        Assert.Equal(PlaybackStatus.Playing, session.Status);
        Assert.Equal("Bridge Song", session.Title);
        Assert.Equal("Bridge Artist", session.Artist);
        Assert.Equal("Bridge Album", session.Album);
        Assert.Equal(TimeSpan.FromSeconds(180), session.Duration);
        Assert.Equal(TimeSpan.FromSeconds(12), session.Position);
        Assert.Equal(0.64, session.Volume);
        Assert.Equal(new Uri("https://example.com/art.jpg"), session.ArtworkUri);
    }

    /// <summary>
    /// An artist array collapses into one display string.
    /// </summary>
    /// <remarks>
    /// Also checks that a track with no artwork yields a null URI rather than
    /// an empty or malformed one, since both fields are optional in the same
    /// metadata dictionary.
    /// </remarks>
    [Fact]
    public async Task MultipleArtistsAreJoined()
    {
        var bus = new FakeMprisBus
        {
            Players =
            {
                ["org.mpris.MediaPlayer2.vlc"] = PlayerProperties(
                    status: "Paused",
                    title: "Duet",
                    artists: ["First Artist", "Second Artist"],
                    album: "Split Album",
                    lengthMicroseconds: 90_000_000,
                    positionMicroseconds: 0,
                    volume: 1.0,
                    artUrl: null),
            },
        };
        var service = new MprisMediaSessionService(bus);

        var session = Assert.Single(await service.GetSessionsAsync());

        Assert.Equal("First Artist, Second Artist", session.Artist);
        Assert.Equal(PlaybackStatus.Paused, session.Status);
        Assert.Null(session.ArtworkUri);
    }

    /// <summary>
    /// A desktop with no media player is an empty result, not a failure.
    /// </summary>
    /// <remarks>
    /// This is the state Reprise starts in on most machines, so it has to
    /// stay on the quiet path rather than reaching the window's error line.
    /// </remarks>
    [Fact]
    public async Task NoMprisServiceMeansNoSessions()
    {
        var service = new MprisMediaSessionService(new FakeMprisBus());

        var sessions = await service.GetSessionsAsync();

        Assert.Empty(sessions);
    }

    /// <summary>
    /// A player that quits mid-poll is dropped instead of reported blank.
    /// </summary>
    /// <remarks>
    /// Reproduces the race the once-a-second refresh runs into whenever a
    /// window is closed: the player is listed, then answers with nothing.
    /// Without the skip it would surface as a session with no track at all.
    /// </remarks>
    [Fact]
    public async Task PlayerThatLeftTheBusIsSkipped()
    {
        var bus = new FakeMprisBus
        {
            Players = { ["org.mpris.MediaPlayer2.ghost"] = [] },
        };
        var service = new MprisMediaSessionService(bus);

        var sessions = await service.GetSessionsAsync();

        Assert.Empty(sessions);
    }

    /// <summary>
    /// Each command maps to its MPRIS method and addresses the named player.
    /// </summary>
    /// <remarks>
    /// The service name assertion is the important half: a command must reach
    /// the player the user was looking at, not whichever one is active now.
    /// </remarks>
    /// <param name="command">Command passed to the service.</param>
    /// <param name="expectedMember">MPRIS method it should become.</param>
    [Theory]
    [InlineData(PlaybackCommand.Previous, "Previous")]
    [InlineData(PlaybackCommand.PlayPause, "PlayPause")]
    [InlineData(PlaybackCommand.Next, "Next")]
    public async Task PlaybackCommandsTargetTheSelectedPlayer(
        PlaybackCommand command,
        string expectedMember)
    {
        var bus = new FakeMprisBus();
        var service = new MprisMediaSessionService(bus);

        await service.SendCommandAsync("firefox.instance42", command);

        Assert.Equal("org.mpris.MediaPlayer2.firefox.instance42", bus.InvokedService);
        Assert.Equal(expectedMember, bus.InvokedMember);
    }

    /// <summary>
    /// A PlayerId read from a snapshot addresses the same player on the way
    /// back.
    /// </summary>
    /// <remarks>
    /// Uses a browser's dotted instance name, the case where stripping and
    /// restoring the prefix is easiest to get wrong. Also confirms the
    /// display name drops that instance suffix while the id keeps it.
    /// </remarks>
    [Fact]
    public async Task PlayerIdsAreRoundTrippedFromTheServiceName()
    {
        var bus = new FakeMprisBus
        {
            Players =
            {
                ["org.mpris.MediaPlayer2.firefox.instance42"] = PlayerProperties(
                    status: "Playing",
                    title: "Tab Audio",
                    artists: [],
                    album: string.Empty,
                    lengthMicroseconds: 0,
                    positionMicroseconds: 0,
                    volume: 0.5,
                    artUrl: null),
            },
        };
        var service = new MprisMediaSessionService(bus);

        var session = Assert.Single(await service.GetSessionsAsync());
        Assert.Equal("firefox.instance42", session.PlayerId);
        Assert.Equal("Firefox", session.PlayerName);

        await service.SendCommandAsync(session.PlayerId, PlaybackCommand.Next);

        Assert.Equal("org.mpris.MediaPlayer2.firefox.instance42", bus.InvokedService);
    }

    /// <summary>
    /// A seek is addressed to the track the player currently reports.
    /// </summary>
    /// <remarks>
    /// The track id is what makes an MPRIS seek safe against a track change
    /// racing the request, so it has to come from the player rather than be
    /// invented, and the position has to arrive in microseconds.
    /// </remarks>
    [Fact]
    public async Task SeekUsesTheReportedTrackId()
    {
        var bus = new FakeMprisBus
        {
            Players =
            {
                ["org.mpris.MediaPlayer2.spotify"] = PlayerProperties(
                    status: "Playing",
                    title: "Bridge Song",
                    artists: ["Bridge Artist"],
                    album: "Bridge Album",
                    lengthMicroseconds: 180_000_000,
                    positionMicroseconds: 12_000_000,
                    volume: 0.64,
                    artUrl: null),
            },
        };
        var service = new MprisMediaSessionService(bus);

        await service.SeekAsync("spotify", TimeSpan.FromSeconds(90));

        Assert.Equal("org.mpris.MediaPlayer2.spotify", bus.SeekedService);
        Assert.Equal("/org/fake/track/1", bus.SeekedTrackId);
        Assert.Equal(90_000_000, bus.SeekedPositionMicroseconds);
    }

    /// <summary>
    /// A player without a track id is reported as unseekable, not guessed at.
    /// </summary>
    [Fact]
    public async Task SeekWithoutTrackIdFails()
    {
        var bus = new FakeMprisBus
        {
            Players =
            {
                ["org.mpris.MediaPlayer2.stream"] = new Dictionary<string, VariantValue>
                {
                    [MprisPropertyMapper.PlaybackStatusKey] = VariantValue.String("Playing"),
                },
            },
        };
        var service = new MprisMediaSessionService(bus);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SeekAsync("stream", TimeSpan.FromSeconds(1)));
        Assert.Null(bus.SeekedService);
    }

    /// <summary>
    /// Volume writes are clamped into the range MPRIS defines.
    /// </summary>
    /// <param name="requested">Level handed to the service.</param>
    /// <param name="expected">Level that should reach the bus.</param>
    [Theory]
    [InlineData(0.5, 0.5)]
    [InlineData(1.2, 1.0)]
    [InlineData(-0.1, 0.0)]
    public async Task VolumeIsClampedBeforeItIsWritten(double requested, double expected)
    {
        var bus = new FakeMprisBus();
        var service = new MprisMediaSessionService(bus);

        await service.SetVolumeAsync("vlc", requested);

        Assert.Equal("org.mpris.MediaPlayer2.vlc", bus.VolumeService);
        Assert.Equal(expected, bus.WrittenVolume);
    }

    /// <summary>
    /// A rejection that arrives as a stack trace is reduced to its last line.
    /// </summary>
    /// <remarks>
    /// Python-based players reply with the whole traceback; the panel has
    /// room for one line, and the last one is the one that names the fault.
    /// </remarks>
    [Fact]
    public async Task MultiLineBusErrorsAreReducedToTheirLastLine()
    {
        var bus = new FakeMprisBus
        {
            InvokeFailure = new DBusErrorReplyException(
                "org.freedesktop.DBus.Error.UnknownMethod",
                "Traceback (most recent call last):\n  File \"service.py\", line 659\n\nUnknown method: Next is not a valid method\n"),
        };
        var service = new MprisMediaSessionService(bus);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendCommandAsync("vlc", PlaybackCommand.Next));

        Assert.Equal("MPRIS Next failed: Unknown method: Next is not a valid method", exception.Message);
        Assert.IsType<DBusErrorReplyException>(exception.InnerException);
    }

    /// <summary>
    /// Builds a property dictionary shaped the way a real player publishes
    /// one.
    /// </summary>
    /// <remarks>
    /// The nesting is the part that matters: track fields live inside a
    /// Metadata dictionary that is itself a variant, so a flat dictionary
    /// here would let the mapper's unwrapping pass untested.
    /// </remarks>
    /// <param name="status">Value for the PlaybackStatus property.</param>
    /// <param name="title">Track title.</param>
    /// <param name="artists">Artist credits, published as an array.</param>
    /// <param name="album">Album name.</param>
    /// <param name="lengthMicroseconds">Track length in microseconds.</param>
    /// <param name="positionMicroseconds">
    /// Playback position in microseconds.
    /// </param>
    /// <param name="volume">Player volume from 0 to 1.</param>
    /// <param name="artUrl">
    /// Cover art URI, or null to omit the key entirely as a player without
    /// artwork would.
    /// </param>
    /// <returns>Player properties ready to hand to the fake bus.</returns>
    private static Dictionary<string, VariantValue> PlayerProperties(
        string status,
        string title,
        string[] artists,
        string album,
        long lengthMicroseconds,
        long positionMicroseconds,
        double volume,
        string? artUrl)
    {
        var metadata = new Dictionary<string, VariantValue>
        {
            [MprisPropertyMapper.TrackIdKey] =
                VariantValue.ObjectPath(new ObjectPath("/org/fake/track/1")),
            [MprisPropertyMapper.TitleKey] = VariantValue.String(title),
            [MprisPropertyMapper.ArtistKey] = VariantValue.Array(artists),
            [MprisPropertyMapper.AlbumKey] = VariantValue.String(album),
            [MprisPropertyMapper.LengthKey] = VariantValue.Int64(lengthMicroseconds),
        };
        if (artUrl is not null)
        {
            metadata[MprisPropertyMapper.ArtUrlKey] = VariantValue.String(artUrl);
        }

        return new Dictionary<string, VariantValue>
        {
            [MprisPropertyMapper.PlaybackStatusKey] = VariantValue.String(status),
            [MprisPropertyMapper.MetadataKey] =
                new Dict<string, VariantValue>(metadata).AsVariantValue(),
            [MprisPropertyMapper.PositionKey] = VariantValue.Int64(positionMicroseconds),
            [MprisPropertyMapper.VolumeKey] = VariantValue.Double(volume),
        };
    }

    /// <summary>
    /// In-memory stand-in for the session bus.
    /// </summary>
    /// <remarks>
    /// Serves players out of a dictionary and records the last method call,
    /// so a test can assert on what would have gone over the bus. Commands
    /// are accepted for any name, including one with no player behind it,
    /// which is what lets the command tests skip building a session first.
    /// </remarks>
    private sealed class FakeMprisBus : IMprisBus
    {
        /// <summary>Players to serve, keyed by bus name.</summary>
        public Dictionary<string, Dictionary<string, VariantValue>> Players { get; } = [];

        /// <summary>
        /// Bus name from the most recent <see cref="InvokeAsync"/>, or null if
        /// it was never called.
        /// </summary>
        public string? InvokedService { get; private set; }

        /// <summary>
        /// Member name from the most recent <see cref="InvokeAsync"/>, or null
        /// if it was never called.
        /// </summary>
        public string? InvokedMember { get; private set; }

        /// <summary>
        /// Error every <see cref="InvokeAsync"/> replies with, or null to accept.
        /// </summary>
        public DBusErrorReplyException? InvokeFailure { get; set; }

        /// <summary>
        /// Bus name from the most recent <see cref="SetPositionAsync"/>, or
        /// null if it was never called.
        /// </summary>
        public string? SeekedService { get; private set; }

        /// <summary>
        /// Track id from the most recent <see cref="SetPositionAsync"/>.
        /// </summary>
        public string? SeekedTrackId { get; private set; }

        /// <summary>
        /// Position from the most recent <see cref="SetPositionAsync"/>.
        /// </summary>
        public long? SeekedPositionMicroseconds { get; private set; }

        /// <summary>
        /// Bus name from the most recent <see cref="SetVolumeAsync"/>, or
        /// null if it was never called.
        /// </summary>
        public string? VolumeService { get; private set; }

        /// <summary>
        /// Level from the most recent <see cref="SetVolumeAsync"/>.
        /// </summary>
        public double? WrittenVolume { get; private set; }

        /// <summary>
        /// Returns the configured bus names in the order the real bus would.
        /// </summary>
        /// <remarks>
        /// Sorted to match <see cref="DBusMprisBus"/>, so a test that depends
        /// on session order is not passing for the wrong reason.
        /// </remarks>
        /// <param name="cancellationToken">Ignored.</param>
        /// <returns>Configured bus names, sorted ordinally.</returns>
        public Task<IReadOnlyList<string>> ListPlayerServicesAsync(
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<string> services = [.. Players.Keys.OrderBy(key => key, StringComparer.Ordinal)];
            return Task.FromResult(services);
        }

        /// <summary>
        /// Returns one player's configured properties.
        /// </summary>
        /// <remarks>
        /// An unknown name yields an empty dictionary rather than throwing,
        /// mirroring how the real bus reports a player that has quit.
        /// </remarks>
        /// <param name="serviceName">Bus name to look up.</param>
        /// <param name="cancellationToken">Ignored.</param>
        /// <returns>The player's properties, or an empty dictionary.</returns>
        public Task<IReadOnlyDictionary<string, VariantValue>> GetPlayerPropertiesAsync(
            string serviceName,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<string, VariantValue> properties =
                Players.TryGetValue(serviceName, out var found)
                    ? found
                    : new Dictionary<string, VariantValue>();
            return Task.FromResult(properties);
        }

        /// <summary>
        /// Records a command instead of sending it.
        /// </summary>
        /// <param name="serviceName">
        /// Bus name the command was addressed to.
        /// </param>
        /// <param name="member">MPRIS method that was requested.</param>
        /// <param name="cancellationToken">Ignored.</param>
        /// <returns>An already completed task.</returns>
        public Task InvokeAsync(
            string serviceName,
            string member,
            CancellationToken cancellationToken = default)
        {
            InvokedService = serviceName;
            InvokedMember = member;
            return InvokeFailure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
        }

        /// <summary>
        /// Records a seek instead of sending it.
        /// </summary>
        /// <param name="serviceName">Bus name the seek was addressed to.</param>
        /// <param name="trackId">Track id the seek carried.</param>
        /// <param name="positionMicroseconds">Requested position.</param>
        /// <param name="cancellationToken">Ignored.</param>
        /// <returns>An already completed task.</returns>
        public Task SetPositionAsync(
            string serviceName,
            string trackId,
            long positionMicroseconds,
            CancellationToken cancellationToken = default)
        {
            SeekedService = serviceName;
            SeekedTrackId = trackId;
            SeekedPositionMicroseconds = positionMicroseconds;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Records a volume write instead of sending it.
        /// </summary>
        /// <param name="serviceName">Bus name the write was addressed to.</param>
        /// <param name="volume">Requested level.</param>
        /// <param name="cancellationToken">Ignored.</param>
        /// <returns>An already completed task.</returns>
        public Task SetVolumeAsync(
            string serviceName,
            double volume,
            CancellationToken cancellationToken = default)
        {
            VolumeService = serviceName;
            WrittenVolume = volume;
            return Task.CompletedTask;
        }
    }
}
