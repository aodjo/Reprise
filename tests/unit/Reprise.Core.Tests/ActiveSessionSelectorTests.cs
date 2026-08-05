using Reprise.Core;
using Xunit;

namespace Reprise.Core.Tests;

public sealed class ActiveSessionSelectorTests
{
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

    [Fact]
    public void EmptySessionListHasNoSelection()
    {
        Assert.Null(ActiveSessionSelector.Select([]));
    }

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
