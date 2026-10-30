using Reprise.Core;

namespace Reprise.Platform.Windows.Media;

/// <summary>
/// A native media session without exposing WinRT types to application logic or tests.
/// </summary>
internal interface IWindowsMediaSession
{
    /// <summary>Stable identifier used to address this session during its lifetime.</summary>
    string Id { get; }

    /// <summary>
    /// Reads player metadata and playback state from the Windows media broker.
    /// </summary>
    /// <param name="cancellationToken">Cancels native asynchronous reads.</param>
    /// <returns>A freshly observed session snapshot.</returns>
    Task<MediaSessionSnapshot> ReadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Sends a transport command and reports whether the player accepted it.
    /// </summary>
    /// <param name="command">Requested transport operation.</param>
    /// <param name="cancellationToken">Cancels the native request.</param>
    /// <returns>Whether the command was accepted.</returns>
    Task<bool> SendAsync(PlaybackCommand command, CancellationToken cancellationToken);

    /// <summary>
    /// Seeks to a track-relative position using the native timeline's origin.
    /// </summary>
    /// <param name="position">Elapsed position in the current track.</param>
    /// <param name="cancellationToken">Cancels the native request.</param>
    /// <returns>Whether the player accepted the seek.</returns>
    Task<bool> SeekAsync(TimeSpan position, CancellationToken cancellationToken);

    /// <summary>Changes only this player's application audio sessions, never the system master volume.</summary>
    Task<bool> SetVolumeAsync(double volume, CancellationToken cancellationToken) => Task.FromResult(false);
}

/// <summary>
/// Enumerates the system's media sessions, preserving identifiers across polls.
/// </summary>
internal interface IWindowsMediaSessionSource
{
    /// <summary>
    /// Gets the current sessions from the Windows media broker.
    /// </summary>
    /// <param name="cancellationToken">Cancels manager initialization.</param>
    /// <returns>All currently advertised media sessions.</returns>
    Task<IReadOnlyList<IWindowsMediaSession>> GetSessionsAsync(CancellationToken cancellationToken);
}
