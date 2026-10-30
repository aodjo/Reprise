using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Reprise.Desktop;

namespace Reprise.Platform.Windows.Taskbar;

/// <summary>Pushes lyric lines upward while retaining smooth horizontal scrolling inside each line.</summary>
internal sealed class TaskbarLyricViewport : Grid, IDisposable
{
    private PanelTitleMarquee _current = CreateLine();
    private PanelTitleMarquee _outgoing = CreateLine();
    private readonly DispatcherTimer _timer;
    private long _started;
    private bool _moving;
    private string? _trackKey;

    /// <summary>Creates two clipped text layers; the album cover is outside this viewport.</summary>
    internal TaskbarLyricViewport()
    {
        ClipToBounds = true;
        _outgoing.IsVisible = false;
        Children.Add(_outgoing);
        Children.Add(_current);
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Tick());
        _timer.Stop();
        DetachedFromVisualTree += (_, _) => Stop();
    }

    /// <summary>Text on the incoming/current layer, used by the native diagnostic.</summary>
    internal string Text => _current.Title;
    /// <summary>Natural shaped width of the current lyric, independent of the viewport's allocated width.</summary>
    internal double TextWidth => _current.MeasureTitleWidth();
    /// <summary>Whether a lyric push is currently running.</summary>
    internal bool IsTransitioning => _moving;

    /// <summary>Updates line content and starts at most one push, replacing rapid obsolete advances.</summary>
    internal void Present(MenuBarState state, bool push)
    {
        if (_trackKey != state.TrackKey)
        {
            Stop();
            _current.RestartScroll();
            _trackKey = state.TrackKey;
        }
        var text = TaskbarLyricsLayout.Text(state);
        if (_current.Title != text)
        {
            Stop();
            if (push && Bounds.Height > 0)
            {
                (_current, _outgoing) = (_outgoing, _current);
                _outgoing.IsVisible = true;
                _moving = true;
                _started = Stopwatch.GetTimestamp();
            }
            _current.Title = text;
            _current.IsVisible = true;
            Tick();
            if (_moving) _timer.Start();
        }
        else if (!state.IsRunning || !state.ShowsLyrics) Stop();
        foreach (var line in new[] { _current, _outgoing })
        {
            line.AutomaticallyScrolls = state.ScrollsText;
            line.PointsPerSecond = state.PointsPerSecond;
        }
    }

    /// <summary>Updates both layers when the system taskbar theme changes.</summary>
    internal void SetForeground(IBrush foreground)
    {
        _current.Foreground = foreground;
        _outgoing.Foreground = foreground;
    }

    /// <summary>Advances both layers on one clock, including short lines that do not horizontally scroll.</summary>
    private void Tick()
    {
        if (!_moving) return;
        var elapsed = Stopwatch.GetElapsedTime(_started);
        if (elapsed >= TaskbarLyricMotion.Duration) { Stop(); return; }
        var offsets = TaskbarLyricMotion.Offsets(elapsed, Bounds.Height);
        ((TranslateTransform)_outgoing.RenderTransform!).Y = offsets.Outgoing;
        ((TranslateTransform)_current.RenderTransform!).Y = offsets.Incoming;
    }

    /// <summary>Removes the obsolete layer and returns the current lyric to its resting position.</summary>
    private void Stop()
    {
        _moving = false;
        _timer.Stop();
        _outgoing.IsVisible = false;
        _outgoing.Title = string.Empty;
        ((TranslateTransform)_outgoing.RenderTransform!).Y = 0;
        ((TranslateTransform)_current.RenderTransform!).Y = 0;
    }

    /// <summary>Creates an independent scrolling layer at taskbar text size.</summary>
    private static PanelTitleMarquee CreateLine()
    {
        var line = new PanelTitleMarquee
        {
            FontSize = 13, FadesLeadingEdge = true, RenderTransform = new TranslateTransform(),
        };
        // Grayscale antialiasing preserves glyph alpha on the transparent taskbar surface.
        TextOptions.SetTextRenderingMode(line, TextRenderingMode.Antialias);
        return line;
    }

    /// <inheritdoc />
    public void Dispose() => Stop();
}
