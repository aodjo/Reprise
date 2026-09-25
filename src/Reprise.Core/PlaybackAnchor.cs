namespace Reprise.Core;

/// <summary>
/// A playback position pinned to the moment it was observed.
/// </summary>
/// <remarks>
/// Players are not obliged to keep the position they publish up to date. The
/// MPRIS specification has clients assume playback advances linearly and
/// watch for a discontinuity instead, and several players only refresh the
/// value when something happens to it. So a poll is treated as a correction
/// to apply when it disagrees with the clock, rather than as the truth to
/// adopt afresh every time.
/// <para>
/// That distinction is what keeps synced lyrics on the beat. Adopting every
/// sample hands the player's own rounding straight to the lyric sheet, so a
/// line lands early on one verse and late on the next; holding the anchor
/// lets the clock carry the position between the moments it genuinely moves.
/// </para>
/// </remarks>
/// <param name="TrackIdentity">
/// The track this anchor belongs to. An anchor is never carried across a
/// track change, or a new track would start part-way through.
/// </param>
/// <param name="Position">Position the player reported.</param>
/// <param name="ObservedAt">When that position was true.</param>
/// <param name="Status">Playback state at that moment.</param>
/// <param name="Duration">Track length, used to clamp the projection.</param>
public sealed record PlaybackAnchor(
    string TrackIdentity,
    TimeSpan Position,
    DateTimeOffset ObservedAt,
    PlaybackStatus Status,
    TimeSpan? Duration)
{
    /// <summary>
    /// How far a polled position may sit from the projection and still count
    /// as the same uninterrupted playback.
    /// </summary>
    /// <remarks>
    /// Wide enough to absorb a player that reports whole seconds, and one
    /// whose value is a poll or two stale; narrow enough that a real seek,
    /// which moves by far more, re-anchors on the very next poll.
    /// </remarks>
    public static readonly TimeSpan DriftTolerance = TimeSpan.FromSeconds(1.2);

    /// <summary>
    /// Position this anchor implies at a given moment.
    /// </summary>
    /// <param name="now">Moment to project to.</param>
    /// <returns>The estimated position, clamped to the track.</returns>
    /// <example>
    /// <code>
    /// var shown = anchor.Estimate(DateTimeOffset.UtcNow);
    /// </code>
    /// </example>
    public TimeSpan Estimate(DateTimeOffset now) =>
        PlaybackPosition.Estimate(Position, Status, ObservedAt, now, Duration);

    /// <summary>
    /// Chooses the anchor to use once a fresh sample has arrived.
    /// </summary>
    /// <remarks>
    /// Keeps the standing anchor while the sample agrees with it, so the
    /// position advances on the clock rather than stepping with each poll.
    /// A new track, a change of playback state, or a position that has moved
    /// further than <see cref="DriftTolerance"/> all mean playback is no
    /// longer where the anchor says, and start a new one.
    /// <para>
    /// A sample carrying no position cannot correct anything, so it leaves
    /// the anchor alone. A length arriving later than the rest of the
    /// metadata is folded in without disturbing the timing, since the
    /// projection is clamped to it.
    /// </para>
    /// </remarks>
    /// <param name="existing">Anchor in force, or null when there is none.</param>
    /// <param name="session">Sample just polled.</param>
    /// <param name="trackIdentity">
    /// Identity of the track in <paramref name="session"/>, as the caller
    /// already computes it for its own change detection.
    /// </param>
    /// <returns>The anchor to project positions from.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="session"/> is null.
    /// </exception>
    /// <example>
    /// <code>
    /// _anchor = PlaybackAnchor.Reconcile(_anchor, session, identity);
    /// var position = _anchor.Estimate(DateTimeOffset.UtcNow);
    /// </code>
    /// </example>
    public static PlaybackAnchor Reconcile(
        PlaybackAnchor? existing,
        MediaSessionSnapshot session,
        string trackIdentity)
    {
        ArgumentNullException.ThrowIfNull(session);

        var fresh = new PlaybackAnchor(
            trackIdentity,
            session.Position ?? TimeSpan.Zero,
            session.ObservedAt,
            session.Status,
            session.Duration);

        if (existing is null
            || existing.TrackIdentity != trackIdentity
            || existing.Status != session.Status)
        {
            return fresh;
        }

        if (session.Position is not { } sampled)
        {
            return existing.WithDuration(session.Duration);
        }

        var projected = PlaybackPosition.Project(
            existing.Position,
            existing.Status,
            existing.ObservedAt,
            session.ObservedAt);
        return (sampled - projected).Duration() > DriftTolerance
            ? fresh
            : existing.WithDuration(session.Duration);
    }

    /// <summary>
    /// The same anchor carrying a track length, when one has appeared.
    /// </summary>
    /// <param name="duration">Length the latest sample reported.</param>
    /// <returns>
    /// This anchor when the length is unchanged, so holding costs no
    /// allocation; otherwise a copy carrying the new length.
    /// </returns>
    private PlaybackAnchor WithDuration(TimeSpan? duration) =>
        Duration == duration ? this : this with { Duration = duration };
}
