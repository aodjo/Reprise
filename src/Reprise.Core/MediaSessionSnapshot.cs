namespace Reprise.Core;

/// <summary>
/// Immutable view of a single media player's state at one moment in time.
/// </summary>
/// <remarks>
/// Platform backends translate whatever their native API exposes into this
/// shape, so the desktop layer never has to know whether a track arrived over
/// MPRIS, AppleScript, or a browser extension. Values a backend cannot supply
/// are left null instead of being defaulted, which keeps "no position was
/// reported" distinguishable from "playing from the very start". Text fields
/// use an empty string for the same reason they are not nullable: they are
/// bound straight to the UI, where absent and blank render identically.
/// </remarks>
/// <param name="PlayerId">
/// Backend-scoped identifier used to address the player when sending
/// commands, for example <c>spotify</c>.
/// </param>
/// <param name="PlayerName">
/// Display name for the player, for example <c>Spotify</c>.
/// </param>
/// <param name="Status">Whether the player is playing, paused, or stopped.</param>
/// <param name="Title">Track title, or an empty string when unavailable.</param>
/// <param name="Artist">
/// Artist credit, already joined when there are several.
/// </param>
/// <param name="Album">Album name, or an empty string when unavailable.</param>
/// <param name="Duration">Total track length, or null when not reported.</param>
/// <param name="Position">Elapsed playback time, or null when not reported.</param>
/// <param name="Volume">Player volume from 0 to 1, or null when not reported.</param>
/// <param name="ArtworkUri">
/// Absolute URI of the cover art, or null when absent.
/// </param>
/// <param name="ObservedAt">
/// When the backend sampled this state. Used to prefer the freshest player
/// when several are ranked equally.
/// </param>
/// <example>
/// <code>
/// var sessions = await service.GetSessionsAsync();
/// Console.WriteLine($"{sessions[0].Title} - {sessions[0].Artist}");
/// </code>
/// </example>
public sealed record MediaSessionSnapshot(
    string PlayerId,
    string PlayerName,
    PlaybackStatus Status,
    string Title,
    string Artist,
    string Album,
    TimeSpan? Duration,
    TimeSpan? Position,
    double? Volume,
    Uri? ArtworkUri,
    DateTimeOffset ObservedAt)
{
    /// <summary>
    /// Fraction of the track that has already played, as a progress-bar value.
    /// </summary>
    /// <remarks>
    /// Collapses the nullable position and duration pair into a single number
    /// the UI can bind without null checks. A track with no reported duration
    /// cannot show meaningful progress, so it reads as 0 rather than throwing
    /// or dividing by zero. The result is clamped because some players briefly
    /// report a position past the end of a track while advancing to the next.
    /// </remarks>
    /// <value>
    /// Progress from 0 to 1 inclusive, or 0 when either the position or the
    /// duration is unavailable.
    /// </value>
    /// <example>
    /// <code>
    /// progressBar.Value = snapshot.Progress; // 0.42 at 42% through the track
    /// </code>
    /// </example>
    public double Progress
    {
        get
        {
            if (Duration is not { Ticks: > 0 } duration || Position is null)
            {
                return 0;
            }

            return Math.Clamp(Position.Value.TotalSeconds / duration.TotalSeconds, 0, 1);
        }
    }
}
