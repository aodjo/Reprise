using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Win32;
using Reprise.Desktop;

namespace Reprise.Platform.Windows.Taskbar;

/// <summary>Draws synchronized lyrics in a nonactivating strip immediately before the system tray.</summary>
/// <remarks>Owns only Reprise windows; it does not inject into, resize, or reparent Explorer.</remarks>
internal sealed class WindowsTaskbarLyricsPublisher : IMenuBarPublisher
{
    private readonly TaskbarLyricViewport _text = new() { Height = 26, VerticalAlignment = VerticalAlignment.Center };
    private readonly Image _cover = new() { Stretch = Stretch.UniformToFill };
    private readonly PanelGlyph _fallback = new() { Icon = PanelIcons.MusicNote, IconSize = 16 };
    private readonly Border _coverFrame;
    private Bitmap? _coverBitmap;
    private byte[] _coverPng = [];
    private Window _window;
    private readonly Border _content;
    private bool _windowClosed;
    private readonly DispatcherTimer _positionTimer;
    private readonly DispatcherTimer _widthTimer;
    private TaskbarRect? _targetRectangle;
    private TaskbarRect? _displayedRectangle;
    private TaskbarRect _fromRectangle;
    private TaskbarRect _safeRectangle;
    private long _resizeStarted;
    private double _scale = 1;
    private bool _animateNextWidth;
    private MenuBarState? _state;
    private bool _disposed;

    /// <summary>Creates a transparent tool window that never receives keyboard focus.</summary>
    internal WindowsTaskbarLyricsPublisher()
    {
        _coverFrame = new Border
        {
            Width = 20, Height = 20, Margin = new Thickness(0, 0, 8, 0), CornerRadius = new CornerRadius(4),
            VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true,
            Child = new Grid { Children = { _cover, _fallback } },
        };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { _coverFrame, _text } };
        Grid.SetColumn(_text, 1);
        _content = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(6, 0),
            Child = row,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        _window = CreateWindow();
        _positionTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background,
            (_, _) => Reposition());
        _widthTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => ResizeFrame());
        _widthTimer.Stop();
    }

    /// <summary>Creates only Reprise's strip; a destroyed shell owner can be followed by a new native window without losing lyric state.</summary>
    private Window CreateWindow()
    {
        var window = new Window
        {
            Title = "Reprise 작업 표시줄 가사",
            WindowDecorations = WindowDecorations.None,
            CanResize = false,
            MinWidth = 1,
            MinHeight = 1,
            ShowInTaskbar = false,
            ShowActivated = false,
            Focusable = false,
            Topmost = true,
            Background = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            WindowStartupLocation = WindowStartupLocation.Manual,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Content = _content,
        };
        window.Opened += (_, _) =>
        {
            if (window.TryGetPlatformHandle() is { } handle) WindowsTaskbarGeometry.Configure(handle.Handle);
        };
        window.PointerPressed += (_, args) =>
        {
            if (args.GetCurrentPoint(window).Properties.IsLeftButtonPressed)
                Activated?.Invoke(this, EventArgs.Empty);
        };
        window.Closed += (_, _) => _windowClosed = true;
        return window;
    }

    /// <inheritdoc />
    public event EventHandler? Activated;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _positionTimer.Start();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Publish(MenuBarState state)
    {
        if (_disposed) return;
        var previousText = _text.Text;
        _text.Present(state, _window.IsVisible && TaskbarLyricMotion.ShouldPush(_state, state));
        _animateNextWidth = _window.IsVisible && previousText != _text.Text && !state.ReservesTextWidth &&
            _state?.TrackKey == state.TrackKey && _state?.PrefersLeft == state.PrefersLeft;
        _state = state;
        UpdateCover(state);
        ToolTip.SetTip(_text, state.ToolTip);
        Reposition();
    }

    /// <summary>Follows the taskbar's DPI, theme, visibility, and notification-area position.</summary>
    private void Reposition()
    {
        if (_disposed) return;
        if (_windowClosed)
        {
            _widthTimer.Stop();
            _targetRectangle = null;
            _displayedRectangle = null;
            _window.Content = null;
            _window = CreateWindow();
            _windowClosed = false;
        }
        if (_state is null || !_state.IsRunning || !_state.ShowsLyrics || _text.Text.Length == 0 && !_state.ShowsArtwork ||
            WindowsTaskbarGeometry.Read() is not { } frame ||
            TaskbarLyricsLayout.Place(frame, _state.MaxWidthChars, _state.ShowsArtwork,
                _text.TextWidth, _state.ReservesTextWidth && !_state.HidesText, _state.PrefersLeft) is not { } rectangle ||
            WindowsTaskbarGeometry.IsFullscreen(frame.Monitor))
        {
            _widthTimer.Stop();
            _targetRectangle = null;
            _displayedRectangle = null;
            if (_window.IsVisible) _window.Hide();
            return;
        }
        var scale = (frame.Dpi == 0 ? 96 : frame.Dpi) / 96d;
        var margin = (int)Math.Round(8 * scale);
        _safeRectangle = _state.PrefersLeft && frame.LeadingSpace is { } gap && rectangle.Right <= gap.Right
            ? new(gap.Left + margin, frame.Bar.Top, gap.Right - margin, frame.Bar.Bottom)
            : TaskbarLyricsLayout.Place(frame, 60, _state.ShowsArtwork, null, true) ?? rectangle;
        if (_targetRectangle != rectangle || _scale != scale)
        {
            _widthTimer.Stop();
            var previous = _displayedRectangle;
            var sameScale = _scale == scale;
            _scale = scale;
            _targetRectangle = rectangle;
            if (_animateNextWidth && sameScale && previous is { } from && from.Top == rectangle.Top && from.Bottom == rectangle.Bottom &&
                (from.Left == rectangle.Left || from.Right == rectangle.Right))
            {
                _fromRectangle = from;
                _resizeStarted = Stopwatch.GetTimestamp();
                ResizeFrame();
                _widthTimer.Start();
            }
            else ApplyRectangle(rectangle);
        }
        _animateNextWidth = false;
        var foreground = TaskbarForeground();
        _text.SetForeground(foreground);
        _fallback.Foreground = foreground;
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        if (!_window.IsVisible) _window.Show();
        if (_window.TryGetPlatformHandle() is { } handle) WindowsTaskbarGeometry.Raise(handle.Handle);
    }

    /// <summary>Animates only Reprise's native strip and clips each frame to the latest safe shell slot.</summary>
    private void ResizeFrame()
    {
        if (_targetRectangle is not { } target) { _widthTimer.Stop(); return; }
        var elapsed = Stopwatch.GetElapsedTime(_resizeStarted);
        var rectangle = TaskbarLyricMotion.Rectangle(_fromRectangle, target, elapsed);
        ApplyRectangle(rectangle with { Left = Math.Max(rectangle.Left, _safeRectangle.Left),
            Right = Math.Min(rectangle.Right, _safeRectangle.Right) });
        if (elapsed >= TaskbarLyricMotion.Duration) _widthTimer.Stop();
    }

    /// <summary>Applies physical bounds while keeping text layout in device-independent pixels.</summary>
    private void ApplyRectangle(TaskbarRect rectangle)
    {
        _displayedRectangle = rectangle;
        _window.Width = Math.Max(1, rectangle.Width / _scale);
        _window.Height = rectangle.Height / _scale;
        _window.Position = new PixelPoint(rectangle.Left, rectangle.Top);
    }

    /// <summary>Decodes each new cover once and honors the tray artwork visibility preference.</summary>
    private void UpdateCover(MenuBarState state)
    {
        _coverFrame.IsVisible = state.ShowsArtwork;
        if (!_coverPng.AsSpan().SequenceEqual(state.IconPng))
        {
            Bitmap? bitmap = null;
            try
            {
                if (state.IconPng.Length > 0)
                {
                    using var source = new MemoryStream(state.IconPng, writable: false);
                    bitmap = new Bitmap(source);
                }
            }
            catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException or InvalidOperationException)
            {
                bitmap = null;
            }
            _cover.Source = bitmap;
            _coverBitmap?.Dispose();
            _coverBitmap = bitmap;
            _coverPng = state.IconPng;
        }
        _cover.IsVisible = _coverBitmap is not null;
        _fallback.IsVisible = _coverBitmap is null;
    }

    /// <summary>Uses the system taskbar theme independently of the application's selected theme.</summary>
    private static IBrush TaskbarForeground()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int value && value == 0 ? Brushes.WhiteSmoke : Brushes.Black;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Brushes.Black;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _positionTimer.Stop();
        _widthTimer.Stop();
        _text.Dispose();
        _window.Close();
        _cover.Source = null;
        _coverBitmap?.Dispose();
    }
}
