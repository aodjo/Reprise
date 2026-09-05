namespace Reprise.Core;

/// <summary>
/// Playback control Reprise can send to a media player.
/// </summary>
/// <remarks>
/// The set is deliberately narrow: these three are the only transport
/// controls every supported backend is able to carry out, so a command never
/// has to be feature-detected before it is dispatched.
/// </remarks>
public enum PlaybackCommand
{
    /// <summary>Skip back to the previous track.</summary>
    Previous,

    /// <summary>Toggle between playing and paused.</summary>
    PlayPause,

    /// <summary>Skip forward to the next track.</summary>
    Next,
}
