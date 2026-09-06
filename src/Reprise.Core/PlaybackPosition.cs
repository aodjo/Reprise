namespace Reprise.Core;

/// <summary>
/// Arithmetic for showing and confirming a playback position.
/// </summary>
/// <remarks>
/// Every platform reports position as a sample taken at some moment, while
/// the panel redraws far more often than it polls. These helpers turn a
/// sample into what the position should be now, and decide whether a later
/// sample confirms a seek the user asked for. They are pure so both
/// platforms and the view-model tests share one definition.
/// </remarks>
public static class PlaybackPosition
{
    /// <summary>
    /// How far a reported position may sit from a seek target and still count
    /// as that seek having landed.
    /// </summary>
    /// <remarks>
    /// Players quantise positions and report them a poll late, so an exact
    /// match would leave a completed seek waiting forever.
    /// </remarks>
    public static readonly TimeSpan SeekConfirmationTolerance = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// Confines a position to the playable range of a track.
    /// </summary>
    /// <param name="position">Position to clamp.</param>
    /// <param name="duration">
    /// Track length, or null when the player does not report one.
    /// </param>
    /// <returns>
    /// The position limited to <c>0</c>..<paramref name="duration"/>, or
    /// zero when the duration is missing or not positive, since a position
    /// means nothing without a length to measure it against.
    /// </returns>
    /// <example>
    /// <code>
    /// PlaybackPosition.Clamp(TimeSpan.FromSeconds(400), TimeSpan.FromSeconds(180));
    /// // 00:03:00
    /// </code>
    /// </example>
    public static TimeSpan Clamp(TimeSpan position, TimeSpan? duration)
    {
        if (duration is not { Ticks: > 0 } length)
        {
            return TimeSpan.Zero;
        }

        if (position < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return position > length ? length : position;
    }

    /// <summary>
    /// Projects an observed position forward to the present.
    /// </summary>
    /// <remarks>
    /// Only a playing session advances; a paused or stopped one is reported
    /// as observed. Time is never allowed to run backwards, so a clock
    /// adjustment between poll and redraw cannot make the scrubber jump left.
    /// </remarks>
    /// <param name="observedPosition">Position the player reported.</param>
    /// <param name="status">Playback state at the time of the report.</param>
    /// <param name="observedAt">When the report was taken.</param>
    /// <param name="now">Moment to project to.</param>
    /// <param name="duration">Track length used to clamp the result.</param>
    /// <returns>The estimated current position, clamped to the track.</returns>
    /// <example>
    /// <code>
    /// var shown = PlaybackPosition.Estimate(
    ///     session.Position ?? TimeSpan.Zero,
    ///     session.Status,
    ///     session.ObservedAt,
    ///     DateTimeOffset.UtcNow,
    ///     session.Duration);
    /// </code>
    /// </example>
    public static TimeSpan Estimate(
        TimeSpan observedPosition,
        PlaybackStatus status,
        DateTimeOffset observedAt,
        DateTimeOffset now,
        TimeSpan? duration)
    {
        var elapsed = status == PlaybackStatus.Playing && now > observedAt
            ? now - observedAt
            : TimeSpan.Zero;

        return Clamp(observedPosition + elapsed, duration);
    }

    /// <summary>
    /// Projects a session's reported position forward to the present.
    /// </summary>
    /// <param name="session">Session whose position to estimate.</param>
    /// <param name="now">Moment to project to.</param>
    /// <returns>
    /// The estimated current position, or zero when the session reports no
    /// position.
    /// </returns>
    /// <example>
    /// <code>
    /// var shown = PlaybackPosition.Estimate(session, DateTimeOffset.UtcNow);
    /// </code>
    /// </example>
    public static TimeSpan Estimate(MediaSessionSnapshot session, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(session);

        return Estimate(
            session.Position ?? TimeSpan.Zero,
            session.Status,
            session.ObservedAt,
            now,
            session.Duration);
    }

    /// <summary>
    /// Decides whether a reported position shows that a seek has taken effect.
    /// </summary>
    /// <param name="actual">Position the player now reports.</param>
    /// <param name="target">Position the seek asked for.</param>
    /// <returns>
    /// True when the two are within <see cref="SeekConfirmationTolerance"/>.
    /// </returns>
    /// <example>
    /// <code>
    /// PlaybackPosition.ConfirmsSeek(TimeSpan.FromSeconds(91), TimeSpan.FromSeconds(90));
    /// // true
    /// </code>
    /// </example>
    public static bool ConfirmsSeek(TimeSpan actual, TimeSpan target) =>
        (actual - target).Duration() <= SeekConfirmationTolerance;
}
