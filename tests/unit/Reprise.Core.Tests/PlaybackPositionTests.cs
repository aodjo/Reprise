using Reprise.Core;
using Xunit;

namespace Reprise.Core.Tests;

/// <summary>
/// Pins how a sampled position is projected and confirmed.
/// </summary>
public sealed class PlaybackPositionTests
{
    /// <summary>
    /// A playing session advances by the time since it was observed.
    /// </summary>
    [Fact]
    public void PlayingSessionAdvancesWithTime()
    {
        var observedAt = new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

        var estimated = PlaybackPosition.Estimate(
            TimeSpan.FromSeconds(10),
            PlaybackStatus.Playing,
            observedAt,
            observedAt.AddSeconds(2.5),
            TimeSpan.FromSeconds(180));

        Assert.Equal(TimeSpan.FromSeconds(12.5), estimated);
    }

    /// <summary>
    /// A paused session stays where it was reported.
    /// </summary>
    [Fact]
    public void PausedSessionDoesNotAdvance()
    {
        var observedAt = DateTimeOffset.UtcNow;

        var estimated = PlaybackPosition.Estimate(
            TimeSpan.FromSeconds(10),
            PlaybackStatus.Paused,
            observedAt,
            observedAt.AddSeconds(30),
            TimeSpan.FromSeconds(180));

        Assert.Equal(TimeSpan.FromSeconds(10), estimated);
    }

    /// <summary>
    /// Projection never runs past the end of the track.
    /// </summary>
    /// <remarks>
    /// Without the clamp a track that ended between polls would show a
    /// position longer than its duration until the next poll corrected it.
    /// </remarks>
    [Fact]
    public void EstimateIsClampedToDuration()
    {
        var observedAt = DateTimeOffset.UtcNow;

        var estimated = PlaybackPosition.Estimate(
            TimeSpan.FromSeconds(175),
            PlaybackStatus.Playing,
            observedAt,
            observedAt.AddSeconds(20),
            TimeSpan.FromSeconds(180));

        Assert.Equal(TimeSpan.FromSeconds(180), estimated);
    }

    /// <summary>
    /// Without a duration there is no range, so the position reads as zero.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-5L)]
    public void MissingDurationClampsToZero(long? durationSeconds)
    {
        TimeSpan? duration = durationSeconds is { } seconds
            ? TimeSpan.FromSeconds(seconds)
            : null;

        Assert.Equal(TimeSpan.Zero, PlaybackPosition.Clamp(TimeSpan.FromSeconds(30), duration));
    }

    /// <summary>
    /// A seek counts as landed within the tolerance and not beyond it.
    /// </summary>
    [Theory]
    [InlineData(90.0, 90.0, true)]
    [InlineData(91.4, 90.0, true)]
    [InlineData(88.6, 90.0, true)]
    [InlineData(92.0, 90.0, false)]
    public void SeekConfirmationUsesTolerance(double actual, double target, bool expected)
    {
        Assert.Equal(
            expected,
            PlaybackPosition.ConfirmsSeek(
                TimeSpan.FromSeconds(actual),
                TimeSpan.FromSeconds(target)));
    }
}
