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
            throw new InvalidOperationException(
                $"MPRIS {member} failed: {exception.ErrorMessage}",
                exception);
        }
    }
}
