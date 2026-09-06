using Reprise.Core;
using Xunit;

namespace Reprise.Core.Tests;

/// <summary>
/// Pins the ordering rules <see cref="ActiveSessionSelector"/> applies.
/// </summary>
/// <remarks>
/// These cover the tie-breaks rather than the happy path, since a single
/// running player needs no selection logic and the rules only become visible
/// when several players compete for the panel.
/// </remarks>
public sealed class ActiveSessionSelectorTests
{
    /// <summary>
    /// Playback status outranks recency.
    /// </summary>
    /// <remarks>
    /// A paused player updating its state must not steal the panel from one
    /// that is actually playing, which is what would happen if the sort led
    /// with the observation timestamp.
    /// </remarks>
    [Fact]
    public void PlayingSessionWinsOverNewerPausedSession()
    {
        var now = DateTimeOffset.UtcNow;
        var selected = ActiveSessionSelector.Select([
            Session("vlc", PlaybackStatus.Playing, now.AddSeconds(-5)),
            Session("spotify", PlaybackStatus.Paused, now),
        ]);

        Assert.Equal("vlc", selected?.PlayerId);
    }

    /// <summary>
    /// The preference list decides between players in the same state.
    /// </summary>
    /// <remarks>
    /// Covers the case the setting exists for: two players both playing,
    /// where only the user's stated priority can pick one.
    /// </remarks>
    [Fact]
    public void PreferredPlayerBreaksEqualPlaybackStateTie()
    {
        var now = DateTimeOffset.UtcNow;
        var selected = ActiveSessionSelector.Select(
            [
                Session("firefox", PlaybackStatus.Playing, now),
                Session("spotify", PlaybackStatus.Playing, now),
            ],
            ["spotify", "firefox"]);

        Assert.Equal("spotify", selected?.PlayerId);
    }

    /// <summary>
    /// No players means no selection, rather than a throw.
    /// </summary>
    [Fact]
    public void EmptySessionListHasNoSelection()
    {
        Assert.Null(ActiveSessionSelector.Select([]));
    }

    /// <summary>
    /// A repeated preference entry keeps its first, strongest position.
    /// </summary>
    /// <remarks>
    /// Guards the <c>TryAdd</c> in the priority map: switching it to an
    /// indexer would let a later duplicate demote the player the user ranked
    /// highest.
    /// </remarks>
    [Fact]
    public void DuplicatePreferenceEntriesKeepTheirFirstPriority()
    {
        var now = DateTimeOffset.UtcNow;
        var selected = ActiveSessionSelector.Select(
            [
                Session("firefox", PlaybackStatus.Playing, now),
                Session("spotify", PlaybackStatus.Playing, now),
            ],
            ["spotify", "spotify", "firefox"]);

        Assert.Equal("spotify", selected?.PlayerId);
    }

    /// <summary>
    /// Progress stays within 0 to 1 even when a player reports out of range.
    /// </summary>
    /// <remarks>
    /// Both ends occur in practice: a negative position while seeking, and a
    /// position past the end while advancing to the next track.
    /// </remarks>
    /// <param name="positionSeconds">Position reported by the player.</param>
    /// <param name="expected">Progress the UI should receive.</param>
    [Theory]
    [InlineData(-5, 0)]
    [InlineData(30, 0.5)]
    [InlineData(90, 1)]
    public void ProgressIsClamped(double positionSeconds, double expected)
    {
        var session = Session("spotify", PlaybackStatus.Playing)
            with
        {
            Duration = TimeSpan.FromSeconds(60),
            Position = TimeSpan.FromSeconds(positionSeconds),
        };

        Assert.Equal(expected, session.Progress, precision: 6);
    }

    /// <summary>
    /// Builds a snapshot carrying only the fields these tests sort on.
    /// </summary>
    /// <remarks>
    /// Everything else is filled with fixed placeholders so a test reads as
    /// the ordering rule it covers rather than a wall of constructor
    /// arguments.
    /// </remarks>
    /// <param name="id">Player id, also used as the display name.</param>
    /// <param name="status">Status to rank the session by.</param>
    /// <param name="observedAt">
    /// Observation time; defaults to now for tests where recency is not the
    /// rule under test.
    /// </param>
    /// <returns>
    /// A snapshot ready to pass to <see cref="ActiveSessionSelector.Select"/>.
    /// </returns>
    private static MediaSessionSnapshot Session(
        string id,
        PlaybackStatus status,
        DateTimeOffset? observedAt = null) => new(
            PlayerId: id,
            PlayerName: id,
            Status: status,
            Title: "Track",
            Artist: "Artist",
            Album: "Album",
            Duration: null,
            Position: null,
            Volume: null,
            ArtworkUri: null,
            ObservedAt: observedAt ?? DateTimeOffset.UtcNow);
}
