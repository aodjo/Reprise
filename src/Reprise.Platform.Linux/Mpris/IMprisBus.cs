using Tmds.DBus.Protocol;

namespace Reprise.Platform.Linux.Mpris;

/// <summary>
/// The three D-Bus operations MPRIS support is built from.
/// </summary>
/// <remarks>
/// Exists to keep <see cref="MprisMediaSessionService"/> testable: a real
/// session bus needs a live desktop session and running media players,
/// neither of which CI has. Narrowing the transport to these calls lets the
/// service's session-building and command-routing logic run against an
/// in-memory fake, while everything that genuinely touches D-Bus stays in
/// <see cref="DBusMprisBus"/>.
/// </remarks>
internal interface IMprisBus
{
    /// <summary>
    /// Lists the bus names of every MPRIS player currently on the session bus.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancels the query. Defaults to <c>default</c>.
    /// </param>
    /// <returns>
    /// Fully qualified bus names, each beginning with
    /// <c>org.mpris.MediaPlayer2.</c>. Empty when no player is running.
    /// </returns>
    /// <exception cref="MprisUnavailableException">
    /// Thrown when the session bus is unreachable.
    /// </exception>
    /// <example>
    /// <code>
    /// var names = await bus.ListPlayerServicesAsync();
    /// // ["org.mpris.MediaPlayer2.spotify"]
    /// </code>
    /// </example>
    Task<IReadOnlyList<string>> ListPlayerServicesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads every property one player exposes on its Player interface.
    /// </summary>
    /// <param name="serviceName">
    /// Bus name from <see cref="ListPlayerServicesAsync"/>.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the query. Defaults to <c>default</c>.
    /// </param>
    /// <returns>
    /// The player's properties keyed by MPRIS property name, or an empty
    /// dictionary when the player is no longer reachable.
    /// </returns>
    /// <exception cref="MprisUnavailableException">
    /// Thrown when the session bus is unreachable.
    /// </exception>
    /// <example>
    /// <code>
    /// var properties = await bus.GetPlayerPropertiesAsync(name);
    /// var status = properties["PlaybackStatus"].GetString(); // "Playing"
    /// </code>
    /// </example>
    Task<IReadOnlyDictionary<string, VariantValue>> GetPlayerPropertiesAsync(
        string serviceName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calls a no-argument method on one player's Player interface.
    /// </summary>
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
    /// Thrown when the player rejects the call.
    /// </exception>
    /// <example>
    /// <code>
    /// await bus.InvokeAsync("org.mpris.MediaPlayer2.spotify", "Next");
    /// </code>
    /// </example>
    Task InvokeAsync(
        string serviceName,
        string member,
        CancellationToken cancellationToken = default);
}
