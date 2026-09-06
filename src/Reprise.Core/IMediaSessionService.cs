namespace Reprise.Core;

/// <summary>
/// Platform-specific gateway to the media players running on this machine.
/// </summary>
/// <remarks>
/// One implementation exists per platform - MPRIS over D-Bus on Linux, and
/// the native player APIs elsewhere - and each hides its transport entirely
/// behind these few calls. The desktop layer depends only on this interface,
/// which is also what lets the UI be exercised against a stub in tests.
/// </remarks>
public interface IMediaSessionService
{
    /// <summary>
    /// Reads the current state of every media player the platform exposes.
    /// </summary>
    /// <remarks>
    /// Each call is a fresh sample rather than a cached or pushed value, so
    /// callers poll at whatever rate suits them. An absent player is not an
    /// error condition: a machine with nothing playing yields an empty list.
    /// </remarks>
    /// <param name="cancellationToken">
    /// Cancels the in-flight query. Defaults to <c>default</c>.
    /// </param>
    /// <returns>
    /// Sessions found at the moment of the call, in an order the
    /// implementation defines. Empty when no player is available.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled.
    /// </exception>
    /// <example>
    /// <code>
    /// var sessions = await service.GetSessionsAsync();
    /// var active = ActiveSessionSelector.Select(sessions);
    /// </code>
    /// </example>
    Task<IReadOnlyList<MediaSessionSnapshot>> GetSessionsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a transport command to one specific player.
    /// </summary>
    /// <remarks>
    /// The command targets a player by id rather than acting on whichever one
    /// happens to be active, so a control press cannot land on a different
    /// player than the one the user was looking at. Completion means the
    /// player accepted the command, not that its state has already changed;
    /// callers that need the new state should re-read the sessions afterwards.
    /// </remarks>
    /// <param name="playerId">
    /// <see cref="MediaSessionSnapshot.PlayerId"/> of the target player.
    /// </param>
    /// <param name="command">Transport control to invoke.</param>
    /// <param name="cancellationToken">
    /// Cancels the in-flight command. Defaults to <c>default</c>.
    /// </param>
    /// <returns>A task that completes once the player accepted the command.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="playerId"/> is null, empty, or whitespace.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the player rejects the command or is no longer reachable.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled.
    /// </exception>
    /// <example>
    /// <code>
    /// await service.SendCommandAsync(active.PlayerId, PlaybackCommand.Next);
    /// </code>
    /// </example>
    Task SendCommandAsync(
        string playerId,
        PlaybackCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves playback of one player to an absolute position in its current
    /// track.
    /// </summary>
    /// <remarks>
    /// Positions are absolute rather than relative so the panel can send the
    /// value the user released the scrubber at, without depending on a
    /// possibly stale idea of where playback currently is. Completion means
    /// the player accepted the request; the new position becomes visible on
    /// a later <see cref="GetSessionsAsync"/> read.
    /// </remarks>
    /// <param name="playerId">
    /// <see cref="MediaSessionSnapshot.PlayerId"/> of the target player.
    /// </param>
    /// <param name="position">
    /// Offset from the start of the track. Values past the end are clamped
    /// by the player.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the in-flight request. Defaults to <c>default</c>.
    /// </param>
    /// <returns>A task that completes once the player accepted the seek.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="playerId"/> is null, empty, or whitespace.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="position"/> is negative.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the player cannot seek, rejects the request, or is no
    /// longer reachable.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled.
    /// </exception>
    /// <example>
    /// <code>
    /// await service.SeekAsync(active.PlayerId, TimeSpan.FromSeconds(90));
    /// </code>
    /// </example>
    Task SeekAsync(
        string playerId,
        TimeSpan position,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the output volume of one player.
    /// </summary>
    /// <remarks>
    /// The scale is normalised so the desktop layer never has to know whether
    /// a platform counts in percent, decibels, or fractions. Implementations
    /// clamp out-of-range values rather than reject them, since a slider can
    /// overshoot by rounding.
    /// </remarks>
    /// <param name="playerId">
    /// <see cref="MediaSessionSnapshot.PlayerId"/> of the target player.
    /// </param>
    /// <param name="volume">Level from 0, silent, to 1, full volume.</param>
    /// <param name="cancellationToken">
    /// Cancels the in-flight request. Defaults to <c>default</c>.
    /// </param>
    /// <returns>A task that completes once the player accepted the level.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="playerId"/> is null, empty, or whitespace.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="volume"/> is not a number.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the player rejects the request or is no longer reachable.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled.
    /// </exception>
    /// <example>
    /// <code>
    /// await service.SetVolumeAsync(active.PlayerId, 0.5);
    /// </code>
    /// </example>
    Task SetVolumeAsync(
        string playerId,
        double volume,
        CancellationToken cancellationToken = default);
}
