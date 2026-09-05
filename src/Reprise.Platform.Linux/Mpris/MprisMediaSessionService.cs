using Reprise.Core;
using Tmds.DBus.Protocol;

namespace Reprise.Platform.Linux.Mpris;

/// <summary>
/// Linux implementation of <see cref="IMediaSessionService"/>, backed by MPRIS
/// over D-Bus.
/// </summary>
/// <remarks>
/// Composes the two halves of MPRIS support - the bus transport and the
/// property mapping - into the platform-neutral contract the desktop layer
/// consumes, and is the only public entry point into this namespace.
/// </remarks>
public sealed class MprisMediaSessionService : IMediaSessionService
{
    private readonly IMprisBus _bus;

    /// <summary>
    /// Creates a service bound to the process-wide D-Bus session connection.
    /// </summary>
    /// <remarks>
    /// The constructor the application uses; the bus is not contacted until
    /// the first call, so constructing this outside a desktop session is
    /// safe.
    /// </remarks>
    /// <example>
    /// <code>
    /// RepriseApplication.MediaSessionServiceFactory =
    ///     static () => new MprisMediaSessionService();
    /// </code>
    /// </example>
    public MprisMediaSessionService()
        : this(new DBusMprisBus())
    {
    }

    /// <summary>
    /// Creates a service over a supplied bus, for tests.
    /// </summary>
    /// <remarks>
    /// Internal rather than public because <see cref="IMprisBus"/> exposes
    /// Tmds.DBus types that are an implementation detail; the seam is visible
    /// to the test assembly through <c>InternalsVisibleTo</c>.
    /// </remarks>
    /// <param name="bus">Transport to route every call through.</param>
    internal MprisMediaSessionService(IMprisBus bus)
    {
        _bus = bus;
    }

    /// <summary>
    /// Reads the current state of every MPRIS player on the session bus.
    /// </summary>
    /// <remarks>
    /// Discovers players first, then queries each one in turn. The queries
    /// are sequential on purpose: they share a single bus connection, so
    /// issuing them concurrently would gain little while multiplying the work
    /// a slow player can hold up.
    /// <para>
    /// All players share one timestamp taken before the queries begin, so the
    /// recency tie-break in <see cref="ActiveSessionSelector"/> does not
    /// quietly favour whichever player happened to be polled last.
    /// </para>
    /// <para>
    /// Players that quit mid-sweep are dropped rather than reported as empty
    /// sessions, since a window closing during a poll is routine.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">
    /// Cancels the sweep. Defaults to <c>default</c>.
    /// </param>
    /// <returns>
    /// One snapshot per reachable player, ordered by bus name. Empty when
    /// nothing is running.
    /// </returns>
    /// <exception cref="MprisUnavailableException">
    /// Thrown when the session bus is unreachable.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled.
    /// </exception>
    /// <example>
    /// <code>
    /// var sessions = await service.GetSessionsAsync();
    /// var active = ActiveSessionSelector.Select(sessions);
    /// </code>
    /// </example>
    public async Task<IReadOnlyList<MediaSessionSnapshot>> GetSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        var serviceNames = await _bus.ListPlayerServicesAsync(cancellationToken);
        if (serviceNames.Count == 0)
        {
            return [];
        }

        var observedAt = DateTimeOffset.UtcNow;
        var sessions = new List<MediaSessionSnapshot>(serviceNames.Count);
        foreach (var serviceName in serviceNames)
        {
            var properties = await _bus.GetPlayerPropertiesAsync(
                serviceName,
                cancellationToken);
            if (properties.Count == 0)
            {
                continue;
            }

            sessions.Add(MprisPropertyMapper.ToSnapshot(
                MprisPropertyMapper.ToPlayerId(serviceName),
                properties,
                observedAt));
        }

        return sessions;
    }

    /// <summary>
    /// Sends a transport command to one MPRIS player.
    /// </summary>
    /// <remarks>
    /// Translates the command into the matching method on the player's Player
    /// interface and calls it on the addressed player only.
    /// <para>
    /// A D-Bus error reply is rewritten as an
    /// <see cref="InvalidOperationException"/> so the failure crosses the
    /// <see cref="IMediaSessionService"/> boundary without leaking a Tmds.DBus
    /// type into the desktop layer, which has no reference to it. The bus
    /// error text is folded into the message because it is what distinguishes
    /// a player that refuses a command - a stream with no next track, say -
    /// from one that has quit.
    /// </para>
    /// </remarks>
    /// <param name="playerId">
    /// <see cref="MediaSessionSnapshot.PlayerId"/> of the target player.
    /// </param>
    /// <param name="command">Transport control to invoke.</param>
    /// <param name="cancellationToken">
    /// Cancels the call. Defaults to <c>default</c>.
    /// </param>
    /// <returns>A task that completes once the player accepted the command.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="playerId"/> is null, empty, or whitespace.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="command"/> is not a defined value.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the player rejects the command or has left the bus.
    /// </exception>
    /// <exception cref="MprisUnavailableException">
    /// Thrown when the session bus is unreachable.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled.
    /// </exception>
    /// <example>
    /// <code>
    /// await service.SendCommandAsync("spotify", PlaybackCommand.Next);
    /// </code>
    /// </example>
    public async Task SendCommandAsync(
        string playerId,
        PlaybackCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerId);
        var member = command switch
        {
            PlaybackCommand.Previous => "Previous",
            PlaybackCommand.PlayPause => "PlayPause",
            PlaybackCommand.Next => "Next",
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };

        try
        {
            await _bus.InvokeAsync(
                MprisPropertyMapper.ToServiceName(playerId),
                member,
                cancellationToken);
        }
        catch (DBusErrorReplyException exception)
        {
            throw Rejected(member, exception);
        }
    }

    /// <summary>
    /// Moves one MPRIS player to an absolute position in its current track.
    /// </summary>
    /// <remarks>
    /// MPRIS requires the id of the track being seeked, which the panel does
    /// not carry, so the player's properties are read first. That extra round
    /// trip is confined to seeks, which are rare next to the once-a-second
    /// poll. A player that publishes no track id cannot be seeked
    /// absolutely and is reported as such rather than guessed at.
    /// </remarks>
    /// <param name="playerId">
    /// <see cref="MediaSessionSnapshot.PlayerId"/> of the target player.
    /// </param>
    /// <param name="position">Offset from the start of the track.</param>
    /// <param name="cancellationToken">
    /// Cancels the request. Defaults to <c>default</c>.
    /// </param>
    /// <returns>A task that completes once the player accepted the seek.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="playerId"/> is null, empty, or whitespace.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="position"/> is negative.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the player has left the bus, publishes no track id, or
    /// rejects the seek.
    /// </exception>
    /// <exception cref="MprisUnavailableException">
    /// Thrown when the session bus is unreachable.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled.
    /// </exception>
    /// <example>
    /// <code>
    /// await service.SeekAsync("spotify", TimeSpan.FromSeconds(90));
    /// </code>
    /// </example>
    public async Task SeekAsync(
        string playerId,
        TimeSpan position,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerId);
        ArgumentOutOfRangeException.ThrowIfLessThan(position, TimeSpan.Zero);

        var serviceName = MprisPropertyMapper.ToServiceName(playerId);
        var properties = await _bus.GetPlayerPropertiesAsync(serviceName, cancellationToken);
        if (properties.Count == 0)
        {
            throw new InvalidOperationException(
                $"MPRIS player {playerId} is no longer running.");
        }

        var trackId = MprisPropertyMapper.ReadTrackId(properties)
            ?? throw new InvalidOperationException(
                $"MPRIS player {playerId} does not report a track id, so it cannot be seeked.");

        try
        {
            await _bus.SetPositionAsync(
                serviceName,
                trackId,
                (long)Math.Round(position.TotalMicroseconds),
                cancellationToken);
        }
        catch (DBusErrorReplyException exception)
        {
            throw Rejected("SetPosition", exception);
        }
    }

    /// <summary>
    /// Sets the output volume of one MPRIS player.
    /// </summary>
    /// <remarks>
    /// The level is clamped into the 0..1 range MPRIS defines before it is
    /// written, so a slider that overshoots by rounding cannot trip a
    /// player's validation.
    /// </remarks>
    /// <param name="playerId">
    /// <see cref="MediaSessionSnapshot.PlayerId"/> of the target player.
    /// </param>
    /// <param name="volume">Level from 0 to 1.</param>
    /// <param name="cancellationToken">
    /// Cancels the request. Defaults to <c>default</c>.
    /// </param>
    /// <returns>A task that completes once the player accepted the level.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="playerId"/> is null, empty, or whitespace.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="volume"/> is not a number.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the player rejects the write or has left the bus.
    /// </exception>
    /// <exception cref="MprisUnavailableException">
    /// Thrown when the session bus is unreachable.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled.
    /// </exception>
    /// <example>
    /// <code>
    /// await service.SetVolumeAsync("spotify", 0.5);
    /// </code>
    /// </example>
    public async Task SetVolumeAsync(
        string playerId,
        double volume,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerId);
        if (double.IsNaN(volume))
        {
            throw new ArgumentOutOfRangeException(nameof(volume), "Volume must be a number.");
        }

        try
        {
            await _bus.SetVolumeAsync(
                MprisPropertyMapper.ToServiceName(playerId),
                Math.Clamp(volume, 0, 1),
                cancellationToken);
        }
        catch (DBusErrorReplyException exception)
        {
            throw Rejected("Volume", exception);
        }
    }

    /// <summary>
    /// Wraps a D-Bus rejection in the exception the desktop layer expects.
    /// </summary>
    /// <remarks>
    /// Players written against scripting bindings reply with a whole stack
    /// trace as the error text. Only its last line says what went wrong, so
    /// that is what the panel gets, capped to a length that fits a banner.
    /// The full reply survives as the inner exception for logs.
    /// </remarks>
    /// <param name="operation">MPRIS member or property that was rejected.</param>
    /// <param name="exception">The bus error reply.</param>
    /// <returns>An exception carrying a one-line description.</returns>
    /// <example>
    /// <code>
    /// throw Rejected("SetPosition", exception);
    /// // "MPRIS SetPosition failed: Unknown method: SetPosition is not a valid method"
    /// </code>
    /// </example>
    private static InvalidOperationException Rejected(
        string operation,
        DBusErrorReplyException exception)
    {
        var detail = exception.ErrorMessage?
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();
        if (string.IsNullOrEmpty(detail))
        {
            detail = exception.ErrorName ?? "the player rejected the request";
        }

        const int maximumLength = 200;
        if (detail.Length > maximumLength)
        {
            detail = detail[..(maximumLength - 1)] + "…";
        }

        return new InvalidOperationException($"MPRIS {operation} failed: {detail}", exception);
    }
}
