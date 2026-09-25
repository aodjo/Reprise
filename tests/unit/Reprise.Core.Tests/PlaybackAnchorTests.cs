using Reprise.Core;
using Xunit;

namespace Reprise.Core.Tests;

/// <summary>
/// Pins when a fresh sample re-anchors playback and when it is let pass.
/// </summary>
/// <remarks>
/// These rules decide whether a lyric line lands on the beat, so the cases
/// worth pinning are the ones a player creates in ordinary use: a position
/// rounded to the second, a value left standing between updates, and a seek
/// that has to be picked up at once.
/// </remarks>
public sealed class PlaybackAnchorTests
{
    /// <summary>
    /// Fixed instant the samples in these tests are dated from.
    /// </summary>
    private static readonly DateTimeOffset Origin =
        new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Identity of the track under test, standing in for the caller's own.
    /// </summary>
    private const string Track = "spotify\0Bridge Song\0Reprise\0Junx";

    /// <summary>
    /// Length of the track under test.
    /// </summary>
    private static readonly TimeSpan TrackLength = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Builds a sample for a track whose length the player has reported.
    /// </summary>
    /// <param name="position">Position the player reported, or null.</param>
    /// <param name="observedAt">When it was read.</param>
    /// <param name="status">Playback state, playing by default.</param>
    /// <returns>A snapshot carrying just what the anchor reads.</returns>
    private static MediaSessionSnapshot Sample(
        TimeSpan? position,
        DateTimeOffset observedAt,
        PlaybackStatus status = PlaybackStatus.Playing) =>
        Sample(position, observedAt, status, TrackLength);

    /// <summary>
    /// Builds a sample whose length may be absent.
    /// </summary>
    /// <remarks>
    /// Its own overload because a player often publishes the length a beat
    /// after the rest of the metadata, and an optional parameter could not
    /// tell an omitted length from one deliberately absent.
    /// </remarks>
    /// <param name="position">Position the player reported, or null.</param>
    /// <param name="observedAt">When it was read.</param>
    /// <param name="status">Playback state.</param>
    /// <param name="duration">Track length, or null when unreported.</param>
    /// <returns>A snapshot carrying just what the anchor reads.</returns>
    private static MediaSessionSnapshot Sample(
        TimeSpan? position,
        DateTimeOffset observedAt,
        PlaybackStatus status,
        TimeSpan? duration) =>
        new(
            PlayerId: "spotify",
            PlayerName: "Spotify",
            Status: status,
            Title: "Bridge Song",
            Artist: "Junx",
            Album: "Reprise",
            Duration: duration,
            Position: position,
            Volume: null,
            ArtworkUri: null,
            ObservedAt: observedAt);

    /// <summary>
    /// The first sample becomes the anchor.
    /// </summary>
    [Fact]
    public void FirstSampleAnchorsPlayback()
    {
        var anchor = PlaybackAnchor.Reconcile(
            null,
            Sample(TimeSpan.FromSeconds(10), Origin),
            Track);

        Assert.Equal(TimeSpan.FromSeconds(10), anchor.Position);
        Assert.Equal(Origin, anchor.ObservedAt);
    }

    /// <summary>
    /// A position rounded by the player does not move the anchor.
    /// </summary>
    /// <remarks>
    /// This is the case that mistimed the lyrics: a player publishing whole
    /// seconds disagrees with the truth by up to a second, and adopting each
    /// sample handed that swing to the lyric sheet twice a second.
    /// </remarks>
    [Fact]
    public void RoundedPositionsLeaveTheAnchorAlone()
    {
        var anchor = PlaybackAnchor.Reconcile(
            null,
            Sample(TimeSpan.FromSeconds(10), Origin),
            Track);

        foreach (var step in new[] { 0.5, 1.0, 1.5, 2.0 })
        {
            var at = Origin.AddSeconds(step);
            var rounded = TimeSpan.FromSeconds(Math.Floor(10 + step));
            anchor = PlaybackAnchor.Reconcile(anchor, Sample(rounded, at), Track);
        }

        Assert.Equal(Origin, anchor.ObservedAt);
        Assert.Equal(TimeSpan.FromSeconds(10), anchor.Position);
        Assert.Equal(
            TimeSpan.FromSeconds(12),
            anchor.Estimate(Origin.AddSeconds(2)));
    }

    /// <summary>
    /// A seek is picked up on the next sample.
    /// </summary>
    [Fact]
    public void SeekReanchorsImmediately()
    {
        var anchor = PlaybackAnchor.Reconcile(
            null,
            Sample(TimeSpan.FromSeconds(10), Origin),
            Track);

        var seeked = Origin.AddSeconds(1);
        anchor = PlaybackAnchor.Reconcile(
            anchor,
            Sample(TimeSpan.FromSeconds(95), seeked),
            Track);

        Assert.Equal(TimeSpan.FromSeconds(95), anchor.Position);
        Assert.Equal(seeked, anchor.ObservedAt);
    }

    /// <summary>
    /// A new track never inherits the previous one's anchor.
    /// </summary>
    [Fact]
    public void TrackChangeStartsANewAnchor()
    {
        var anchor = PlaybackAnchor.Reconcile(
            null,
            Sample(TimeSpan.FromSeconds(100), Origin),
            Track);

        anchor = PlaybackAnchor.Reconcile(
            anchor,
            Sample(TimeSpan.Zero, Origin.AddSeconds(1)),
            "spotify\0Another Song\0Reprise\0Junx");

        Assert.Equal(TimeSpan.Zero, anchor.Position);
    }

    /// <summary>
    /// Pausing and resuming re-anchors, so a pause does not advance.
    /// </summary>
    [Fact]
    public void StateChangeStartsANewAnchor()
    {
        var anchor = PlaybackAnchor.Reconcile(
            null,
            Sample(TimeSpan.FromSeconds(10), Origin),
            Track);

        var pausedAt = Origin.AddSeconds(1);
        anchor = PlaybackAnchor.Reconcile(
            anchor,
            Sample(TimeSpan.FromSeconds(11), pausedAt, PlaybackStatus.Paused),
            Track);

        Assert.Equal(PlaybackStatus.Paused, anchor.Status);
        Assert.Equal(
            TimeSpan.FromSeconds(11),
            anchor.Estimate(pausedAt.AddSeconds(30)));
    }

    /// <summary>
    /// A sample with no position cannot correct the anchor.
    /// </summary>
    [Fact]
    public void MissingPositionLeavesTheAnchorAlone()
    {
        var anchor = PlaybackAnchor.Reconcile(
            null,
            Sample(TimeSpan.FromSeconds(10), Origin),
            Track);

        anchor = PlaybackAnchor.Reconcile(
            anchor,
            Sample(null, Origin.AddSeconds(1)),
            Track);

        Assert.Equal(TimeSpan.FromSeconds(10), anchor.Position);
        Assert.Equal(Origin, anchor.ObservedAt);
    }

    /// <summary>
    /// A length arriving after the rest of the metadata is taken up without
    /// disturbing the timing.
    /// </summary>
    /// <remarks>
    /// The projection is clamped to the length, so an anchor that never
    /// learned one would report zero for the whole track.
    /// </remarks>
    [Fact]
    public void LateDurationIsAdoptedWithoutMovingTheAnchor()
    {
        var anchor = PlaybackAnchor.Reconcile(
            null,
            Sample(TimeSpan.FromSeconds(10), Origin, PlaybackStatus.Playing, null),
            Track);

        Assert.Equal(TimeSpan.Zero, anchor.Estimate(Origin.AddSeconds(2)));

        anchor = PlaybackAnchor.Reconcile(
            anchor,
            Sample(TimeSpan.FromSeconds(11), Origin.AddSeconds(1)),
            Track);

        Assert.Equal(Origin, anchor.ObservedAt);
        Assert.Equal(
            TimeSpan.FromSeconds(12),
            anchor.Estimate(Origin.AddSeconds(2)));
    }

    /// <summary>
    /// A position left standing by the player does not drag playback back.
    /// </summary>
    /// <remarks>
    /// Several players refresh the value only when something happens to it,
    /// which the specification allows; a client is expected to assume linear
    /// playback in between rather than believe the stale number.
    /// </remarks>
    [Fact]
    public void StalePositionDoesNotDragPlaybackBackwards()
    {
        var anchor = PlaybackAnchor.Reconcile(
            null,
            Sample(TimeSpan.FromSeconds(10), Origin),
            Track);

        foreach (var step in new[] { 0.5, 1.0 })
        {
            anchor = PlaybackAnchor.Reconcile(
                anchor,
                Sample(TimeSpan.FromSeconds(10), Origin.AddSeconds(step)),
                Track);
        }

        Assert.Equal(
            TimeSpan.FromSeconds(11),
            anchor.Estimate(Origin.AddSeconds(1)));
    }
}
