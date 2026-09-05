namespace Reprise.Core;

/// <summary>
/// Transport state a media player reports for its current track.
/// </summary>
/// <remarks>
/// The names mirror the values of the MPRIS <c>PlaybackStatus</c> property so
/// a backend can map them across without inventing a vocabulary of its own.
/// Values are declared from least to most active, which is the order
/// <see cref="ActiveSessionSelector"/> prefers when several players are on
/// screen at once.
/// </remarks>
public enum PlaybackStatus
{
    /// <summary>The backend reported a state Reprise does not recognise.</summary>
    Unknown,

    /// <summary>Idle, with no meaningful playback position.</summary>
    Stopped,

    /// <summary>A track is loaded and its position is frozen.</summary>
    Paused,

    /// <summary>A track is advancing right now.</summary>
    Playing,
}
