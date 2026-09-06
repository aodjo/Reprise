using Tmds.DBus.Protocol;

namespace Reprise.Platform.Linux.Mpris;

/// <summary>
/// Talks to MPRIS players directly over the D-Bus session bus.
/// </summary>
/// <remarks>
/// Reprise used to shell out to <c>playerctl</c> for this, which meant asking
/// every user to install a package before the app would do anything.
/// Tmds.DBus is already in the published output by way of Avalonia's
/// FreeDesktop support, so speaking the protocol here removes that dependency
/// at no size cost - and skips a process launch per poll, which happens once
/// a second.
/// <para>
/// Every call routes through the connection Tmds.DBus owns for this process,
/// so the bus is connected once rather than per request.
/// </para>
/// </remarks>
internal sealed class DBusMprisBus : IMprisBus
{
    /// <summary>
    /// Bus-name prefix that identifies a process as an MPRIS player.
    /// </summary>
    /// <remarks>
    /// Fixed by the MPRIS specification. Also serves as the boundary between
    /// a bus name and the PlayerId Reprise shows, which is why it is visible
    /// to <see cref="MprisPropertyMapper"/> rather than private.
    /// </remarks>
    internal const string ServicePrefix = "org.mpris.MediaPlayer2.";

    /// <summary>
    /// Object path every MPRIS player exports; fixed by the specification.
    /// </summary>
    private const string ObjectPath = "/org/mpris/MediaPlayer2";

    /// <summary>
    /// Interface carrying playback state and the transport methods.
    /// </summary>
    private const string PlayerInterface = "org.mpris.MediaPlayer2.Player";

    /// <summary>
    /// Standard D-Bus interface used to bulk-read the player's properties.
    /// </summary>
    private const string PropertiesInterface = "org.freedesktop.DBus.Properties";

    /// <summary>
    /// How long any single bus call may take before it is abandoned.
    /// </summary>
    /// <remarks>
    /// A wedged player would otherwise stall the UI's one-second refresh
    /// indefinitely, since D-Bus method calls have no timeout of their own.
    /// </remarks>
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Decodes a <c>GetAll</c> reply body into a property dictionary.
    /// </summary>
    /// <remarks>
    /// Cached in a static field because Tmds.DBus invokes it per reply, and
    /// an inline lambda would allocate a new closure on each call.
    /// </remarks>
    private static readonly MessageValueReader<Dictionary<string, VariantValue>>
        PropertiesReader = static (Message message, object? _) =>
        {
            var reader = message.GetBodyReader();
            return reader.ReadDictionaryOfStringToVariantValue();
        };

    /// <summary>
    /// Lists the bus names of every MPRIS player currently on the session bus.
    /// </summary>
    /// <remarks>
    /// Discovery works by filtering all owned bus names, because MPRIS has no
    /// registry of its own. Results are ordered by name so repeated polls
    /// present players in a stable sequence rather than whatever order the
    /// bus happens to return.
    /// </remarks>
    /// <param name="cancellationToken">
    /// Cancels the query. Defaults to <c>default</c>.
    /// </param>
    /// <returns>
    /// Bus names beginning with the MPRIS prefix, sorted ordinally. Empty
    /// when no player is running.
    /// </returns>
    /// <exception cref="MprisUnavailableException">
    /// Thrown when the session bus is unreachable.
    /// </exception>
    /// <exception cref="TimeoutException">
    /// Thrown when the bus does not reply within <see cref="CallTimeout"/>.
    /// </exception>
    /// <example>
    /// <code>
    /// var names = await bus.ListPlayerServicesAsync();
    /// // ["org.mpris.MediaPlayer2.firefox.instance42", "org.mpris.MediaPlayer2.spotify"]
    /// </code>
    /// </example>
    public async Task<IReadOnlyList<string>> ListPlayerServicesAsync(
        CancellationToken cancellationToken = default)
    {
        var connection = await ConnectAsync(cancellationToken);
        var services = await connection
            .ListServicesAsync()
            .WaitAsync(CallTimeout, cancellationToken);

        return
        [
            .. services
                .Where(service => service.StartsWith(ServicePrefix, StringComparison.Ordinal))
                .OrderBy(service => service, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Reads every property one player exposes on its Player interface.
    /// </summary>
    /// <remarks>
    /// Uses a single <c>GetAll</c> rather than one call per property, so a
    /// snapshot costs one round trip and cannot capture a track title and
    /// position from two different moments.
    /// <para>
    /// A player quitting between being listed and being queried is routine
    /// rather than exceptional - the poll loop races every closing window -
    /// so that case returns empty instead of throwing.
    /// </para>
    /// </remarks>
    /// <param name="serviceName">
    /// Bus name from <see cref="ListPlayerServicesAsync"/>.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the query. Defaults to <c>default</c>.
    /// </param>
    /// <returns>
    /// Properties keyed by MPRIS property name, or an empty dictionary when
    /// the player has left the bus.
    /// </returns>
    /// <exception cref="MprisUnavailableException">
    /// Thrown when the session bus is unreachable.
    /// </exception>
    /// <exception cref="TimeoutException">
    /// Thrown when the player does not reply within <see cref="CallTimeout"/>.
    /// </exception>
    /// <example>
    /// <code>
    /// var properties = await bus.GetPlayerPropertiesAsync(
    ///     "org.mpris.MediaPlayer2.spotify");
    /// </code>
    /// </example>
    public async Task<IReadOnlyDictionary<string, VariantValue>> GetPlayerPropertiesAsync(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        var connection = await ConnectAsync(cancellationToken);
        try
        {
            return await connection
                .CallMethodAsync(CreateGetAllMessage(connection, serviceName), PropertiesReader)
                .WaitAsync(CallTimeout, cancellationToken);
        }
        catch (DBusErrorReplyException)
        {
            return new Dictionary<string, VariantValue>();
        }
    }

    /// <summary>
    /// Calls a no-argument method on one player's Player interface.
    /// </summary>
    /// <remarks>
    /// Unlike a property read, a rejected command is worth surfacing: it
    /// means a control the user just pressed did nothing. The D-Bus error is
    /// left to propagate so the caller can turn it into a message.
    /// </remarks>
    /// <param name="serviceName">Bus name of the target player.</param>
    /// <param name="member">MPRIS method name, such as <c>PlayPause</c>.</param>
    /// <param name="cancellationToken">
    /// Cancels the call. Defaults to <c>default</c>.
    /// </param>
    /// <returns>A task that completes once the player has replied.</returns>
    /// <exception cref="MprisUnavailableException">
    /// Thrown when the session bus is unreachable.
    /// </exception>
    /// <exception cref="DBusErrorReplyException">
    /// Thrown when the player rejects the call, which includes the case where
    /// it has already quit.
    /// </exception>
    /// <exception cref="TimeoutException">
    /// Thrown when the player does not reply within <see cref="CallTimeout"/>.
    /// </exception>
    /// <example>
    /// <code>
    /// await bus.InvokeAsync("org.mpris.MediaPlayer2.spotify", "Next");
    /// </code>
    /// </example>
    public async Task InvokeAsync(
        string serviceName,
        string member,
        CancellationToken cancellationToken = default)
    {
        var connection = await ConnectAsync(cancellationToken);
        await connection
            .CallMethodAsync(CreatePlayerCallMessage(connection, serviceName, member))
            .WaitAsync(CallTimeout, cancellationToken);
    }

    /// <summary>
    /// Moves one player to an absolute position within a track.
    /// </summary>
    /// <remarks>
    /// Goes through <c>SetPosition</c> rather than the relative <c>Seek</c>
    /// because the panel knows where the user let go of the scrubber, not
    /// how far that is from wherever playback has drifted to since the last
    /// poll. Rejections propagate for the same reason as
    /// <see cref="InvokeAsync"/>.
    /// </remarks>
    /// <param name="serviceName">Bus name of the target player.</param>
    /// <param name="trackId">Object path of the track being seeked.</param>
    /// <param name="positionMicroseconds">
    /// Offset from the start of the track, in microseconds.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the call. Defaults to <c>default</c>.
    /// </param>
    /// <returns>A task that completes once the player has replied.</returns>
    /// <exception cref="MprisUnavailableException">
    /// Thrown when the session bus is unreachable.
    /// </exception>
    /// <exception cref="DBusErrorReplyException">
    /// Thrown when the player rejects the call, which includes a player that
    /// cannot seek or has already quit.
    /// </exception>
    /// <exception cref="TimeoutException">
    /// Thrown when the player does not reply within <see cref="CallTimeout"/>.
    /// </exception>
    /// <example>
    /// <code>
    /// await bus.SetPositionAsync(name, "/com/spotify/track/1", 90_000_000);
    /// </code>
    /// </example>
    public async Task SetPositionAsync(
        string serviceName,
        string trackId,
        long positionMicroseconds,
        CancellationToken cancellationToken = default)
    {
        var connection = await ConnectAsync(cancellationToken);
        await connection
            .CallMethodAsync(CreateSetPositionMessage(
                connection,
                serviceName,
                trackId,
                positionMicroseconds))
            .WaitAsync(CallTimeout, cancellationToken);
    }

    /// <summary>
    /// Writes the <c>Volume</c> property of one player.
    /// </summary>
    /// <remarks>
    /// Uses the standard <c>Properties.Set</c> call, since MPRIS exposes
    /// volume as a writable property rather than a method.
    /// </remarks>
    /// <param name="serviceName">Bus name of the target player.</param>
    /// <param name="volume">Level from 0 to 1.</param>
    /// <param name="cancellationToken">
    /// Cancels the call. Defaults to <c>default</c>.
    /// </param>
    /// <returns>A task that completes once the player has replied.</returns>
    /// <exception cref="MprisUnavailableException">
    /// Thrown when the session bus is unreachable.
    /// </exception>
    /// <exception cref="DBusErrorReplyException">
    /// Thrown when the player rejects the write.
    /// </exception>
    /// <exception cref="TimeoutException">
    /// Thrown when the player does not reply within <see cref="CallTimeout"/>.
    /// </exception>
    /// <example>
    /// <code>
    /// await bus.SetVolumeAsync("org.mpris.MediaPlayer2.spotify", 0.5);
    /// </code>
    /// </example>
    public async Task SetVolumeAsync(
        string serviceName,
        double volume,
        CancellationToken cancellationToken = default)
    {
        var connection = await ConnectAsync(cancellationToken);
        await connection
            .CallMethodAsync(CreateSetVolumeMessage(connection, serviceName, volume))
            .WaitAsync(CallTimeout, cancellationToken);
    }

    /// <summary>
    /// Builds the <c>GetAll</c> request for a player's Player interface.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="GetPlayerPropertiesAsync"/> only because
    /// <c>MessageWriter</c> is a ref struct and so cannot live across an
    /// await; the compiler rejects it inside an async method.
    /// </remarks>
    /// <param name="connection">
    /// Connection whose writer allocates the buffer and assigns the message
    /// serial.
    /// </param>
    /// <param name="serviceName">Bus name of the target player.</param>
    /// <returns>An encoded method call ready to send.</returns>
    private static MessageBuffer CreateGetAllMessage(
        DBusConnection connection,
        string serviceName)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            destination: serviceName,
            path: ObjectPath,
            @interface: PropertiesInterface,
            member: "GetAll",
            signature: "s");
        writer.WriteString(PlayerInterface);
        return writer.CreateMessage();
    }

    /// <summary>
    /// Builds a no-argument method call against a player's Player interface.
    /// </summary>
    /// <remarks>
    /// Split out for the same ref struct reason as
    /// <see cref="CreateGetAllMessage"/>.
    /// </remarks>
    /// <param name="connection">
    /// Connection whose writer allocates the buffer and assigns the message
    /// serial.
    /// </param>
    /// <param name="serviceName">Bus name of the target player.</param>
    /// <param name="member">MPRIS method name to invoke.</param>
    /// <returns>An encoded method call ready to send.</returns>
    private static MessageBuffer CreatePlayerCallMessage(
        DBusConnection connection,
        string serviceName,
        string member)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            destination: serviceName,
            path: ObjectPath,
            @interface: PlayerInterface,
            member: member);
        return writer.CreateMessage();
    }

    /// <summary>
    /// Builds the <c>SetPosition</c> call for a player's Player interface.
    /// </summary>
    /// <remarks>
    /// Split out for the same ref struct reason as
    /// <see cref="CreateGetAllMessage"/>.
    /// </remarks>
    /// <param name="connection">
    /// Connection whose writer allocates the buffer and assigns the message
    /// serial.
    /// </param>
    /// <param name="serviceName">Bus name of the target player.</param>
    /// <param name="trackId">Object path of the track being seeked.</param>
    /// <param name="positionMicroseconds">Target offset in microseconds.</param>
    /// <returns>An encoded method call ready to send.</returns>
    private static MessageBuffer CreateSetPositionMessage(
        DBusConnection connection,
        string serviceName,
        string trackId,
        long positionMicroseconds)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            destination: serviceName,
            path: ObjectPath,
            @interface: PlayerInterface,
            member: "SetPosition",
            signature: "ox");
        writer.WriteObjectPath(trackId);
        writer.WriteInt64(positionMicroseconds);
        return writer.CreateMessage();
    }

    /// <summary>
    /// Builds the <c>Properties.Set</c> call that writes a player's volume.
    /// </summary>
    /// <remarks>
    /// Split out for the same ref struct reason as
    /// <see cref="CreateGetAllMessage"/>.
    /// </remarks>
    /// <param name="connection">
    /// Connection whose writer allocates the buffer and assigns the message
    /// serial.
    /// </param>
    /// <param name="serviceName">Bus name of the target player.</param>
    /// <param name="volume">Level from 0 to 1.</param>
    /// <returns>An encoded method call ready to send.</returns>
    private static MessageBuffer CreateSetVolumeMessage(
        DBusConnection connection,
        string serviceName,
        double volume)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            destination: serviceName,
            path: ObjectPath,
            @interface: PropertiesInterface,
            member: "Set",
            signature: "ssv");
        writer.WriteString(PlayerInterface);
        writer.WriteString(MprisPropertyMapper.VolumeKey);
        writer.WriteVariantDouble(volume);
        return writer.CreateMessage();
    }

    /// <summary>
    /// Returns the process-wide session bus connection, opening it on demand.
    /// </summary>
    /// <remarks>
    /// <c>DBusConnection.Session</c> is shared and owned by Tmds.DBus, so
    /// connecting an already connected instance completes synchronously. That
    /// makes this cheap enough to call at the head of every operation rather
    /// than tracking connection state here.
    /// <para>
    /// Any transport failure is translated into
    /// <see cref="MprisUnavailableException"/>, which turns a low-level socket
    /// or address error into something the UI can explain. Cancellation is
    /// deliberately excluded from that translation so it stays
    /// distinguishable from a genuine failure.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">
    /// Cancels before and during the connection attempt.
    /// </param>
    /// <returns>A connected session bus.</returns>
    /// <exception cref="MprisUnavailableException">
    /// Thrown when the session bus cannot be reached.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled.
    /// </exception>
    private static async ValueTask<DBusConnection> ConnectAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var connection = DBusConnection.Session;
            await connection.ConnectAsync();
            return connection;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new MprisUnavailableException(exception);
        }
    }
}
