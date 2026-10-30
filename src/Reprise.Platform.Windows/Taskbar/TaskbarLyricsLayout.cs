using Reprise.Desktop;

namespace Reprise.Platform.Windows.Taskbar;

/// <summary>Pure placement and content rules for the Windows taskbar lyric strip.</summary>
internal static class TaskbarLyricsLayout
{
    /// <summary>Chooses a real lyric, falling back to track metadata while lyrics are unavailable.</summary>
    /// <param name="state">Current playback and lyric state.</param>
    internal static string Text(MenuBarState state) => !state.IsRunning || !state.ShowsLyrics
        ? string.Empty : (string.IsNullOrWhiteSpace(state.LyricText) ? state.ToolTip : state.LyricText).Trim();

    /// <summary>Fits the strip into the free horizontal space before the notification area.</summary>
    /// <param name="frame">Current shell rectangles in physical pixels.</param>
    /// <param name="characters">User-requested approximate character width.</param>
    /// <param name="showsArtwork">Whether to reserve a cover and its trailing gap.</param>
    /// <param name="textWidth">Natural shaped text width in device-independent pixels.</param>
    /// <param name="reservesWidth">Whether short lines retain the full configured text budget.</param>
    /// <returns>A physical rectangle, or null when the taskbar is hidden or has no safe space.</returns>
    internal static TaskbarRect? Place(TaskbarFrame frame, int characters, bool showsArtwork = false,
        double? textWidth = null, bool reservesWidth = true)
    {
        var bar = frame.Bar;
        var tray = frame.Tray;
        var scale = (frame.Dpi == 0 ? 96 : frame.Dpi) / 96d;
        var margin = (int)Math.Round(8 * scale);
        var minimum = (int)Math.Round(64 * scale);
        var visibleHeight = Math.Min(bar.Bottom, frame.Monitor.Bottom) - Math.Max(bar.Top, frame.Monitor.Top);
        if (bar.Height <= 0 || bar.Width <= bar.Height || visibleHeight < bar.Height / 2 ||
            tray.Width <= 0 || tray.Left <= bar.Left || tray.Right > bar.Right + 2) return null;
        var right = tray.Left - margin;
        var budget = Math.Clamp(characters, 10, 60) * 12d;
        var text = !reservesWidth && textWidth is { } natural && double.IsFinite(natural)
            ? Math.Min(budget, Math.Ceiling(Math.Max(0, natural)) + 12) : budget;
        var desired = (int)Math.Ceiling((text + (showsArtwork ? 28 : 0)) * scale);
        var left = Math.Max(bar.Left + margin, right - desired);
        if (frame.TaskList is { } list && list.Width > 0)
        {
            // Never cover the task-list surface when its native bounds describe occupied space.
            left = Math.Max(left, list.Right + margin);
        }
        if (right - left < Math.Min(minimum, desired)) return null;
        // A short top-level HWND is clamped to Windows' minimum native height (39 px at 96 DPI).
        // Use the full taskbar height so native and Avalonia geometry agree and content stays centered.
        return new(left, bar.Top, right, bar.Bottom);
    }
}
