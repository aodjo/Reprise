using Avalonia.Animation.Easings;
using Reprise.Desktop;

namespace Reprise.Platform.Windows.Taskbar;

/// <summary>Matches macOS's 280 ms upward lyric push with ease-in/ease-out timing.</summary>
internal static class TaskbarLyricMotion
{
    /// <summary>Duration shared with MenuBarMarquee.lyricsTransitionDuration on macOS.</summary>
    internal static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(280);
    /// <summary>Core Animation's standard ease-in/ease-out control points.</summary>
    private static readonly SplineEasing Easing = new() { X1 = 0.42, Y1 = 0, X2 = 0.58, Y2 = 1 };

    /// <summary>Animates lyric advances within a track, never first appearance, hiding, or track changes.</summary>
    internal static bool ShouldPush(MenuBarState? previous, MenuBarState current) =>
        previous is { IsRunning: true, ShowsLyrics: true } && current.IsRunning && current.ShowsLyrics &&
        !string.IsNullOrWhiteSpace(current.LyricText) && !string.IsNullOrEmpty(current.TrackKey) &&
        previous.TrackKey == current.TrackKey && TaskbarLyricsLayout.Text(previous).Length > 0 &&
        TaskbarLyricsLayout.Text(previous) != TaskbarLyricsLayout.Text(current);

    /// <summary>Returns the outgoing and incoming offsets for a clipped viewport in device-independent pixels.</summary>
    internal static (double Outgoing, double Incoming) Offsets(TimeSpan elapsed, double height)
    {
        var progress = Easing.Ease(Math.Clamp(elapsed.TotalMilliseconds / Duration.TotalMilliseconds, 0, 1));
        return (-progress * height, (1 - progress) * height);
    }
}
