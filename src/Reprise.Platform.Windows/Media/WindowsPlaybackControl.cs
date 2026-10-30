using Reprise.Core;

namespace Reprise.Platform.Windows.Media;

/// <summary>Prefers explicit play or pause over a broker toggle and falls back only when the first request is rejected.</summary>
internal static class WindowsPlaybackControl
{
    /// <summary>A pause fallback may toggle only a playing session, preventing an automatic pause from starting a stopped player.</summary>
    internal static async Task<bool> SendAsync(PlaybackCommand command, bool playing, bool canPlay, bool canPause,
        bool canToggle, Func<Task<bool>> play, Func<Task<bool>> pause, Func<Task<bool>> toggle)
    {
        if (command == PlaybackCommand.Pause && !playing) return true;
        var accepted = playing ? canPause && await pause() : canPlay && await play();
        return accepted || canToggle && await toggle();
    }
}
