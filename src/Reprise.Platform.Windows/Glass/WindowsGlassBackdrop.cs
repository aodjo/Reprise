using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Reprise.Desktop;

namespace Reprise.Platform.Windows.Glass;

/// <summary>Synchronizes a live refractive backdrop behind the player without affecting other windows.</summary>
internal sealed class WindowsGlassBackdrop : IDisposable
{
    private readonly NowPlayingWindow _window;
    private readonly PreferencesStore _preferences;
    private readonly DispatcherTimer _refresh;
    private GlassScene? _scene;
    private nint _background;
    private Vector2 _size;
    private float _scale;
    private bool _busy;
    private bool _disposed;
    private bool _failed;
    /// <summary>Native initialization failure, reported by the platform diagnostic.</summary>
    internal string? Error { get; private set; }
    /// <summary>Number of live independently refracted optical regions.</summary>
    internal int RegionCount => _scene?.RegionCount ?? 0;
    /// <summary>Number of live source windows behind the panel.</summary>
    internal int SourceCount => _scene?.SourceCount ?? 0;
    /// <summary>Owned background target handle, used only by the native renderer diagnostic.</summary>
    internal nint BackgroundHandle => _background;

    /// <summary>Attaches to the real panel and retains resources until its lifetime ends.</summary>
    internal WindowsGlassBackdrop(NowPlayingWindow window, PreferencesStore preferences)
    {
        _window = window;
        _preferences = preferences;
        _refresh = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Background, RefreshBackdrop);
        _refresh.Stop();
        _window.TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        _window.Opened += HandleOpened;
        _window.Activated += HandleOpened;
        _window.PositionChanged += HandlePosition;
        _window.PropertyChanged += HandleGeometry;
        _window.Closed += HandleClosed;
        _preferences.Changed += HandlePreference;
    }

    /// <summary>Initializes after the real HWND and its client dimensions exist.</summary>
    private void HandleOpened(object? sender, EventArgs args) => Synchronize();
    /// <summary>Follows movement across monitors in physical pixels.</summary>
    private void HandlePosition(object? sender, PixelPointEventArgs args) => Synchronize();
    /// <summary>Follows resizing and visibility without rebuilding effects on ordinary paint events.</summary>
    private void HandleGeometry(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == Visual.BoundsProperty || args.Property == Visual.IsVisibleProperty || args.Property == Window.TopmostProperty)
            Synchronize();
    }
    /// <summary>Switches the material with the same persisted theme as the foreground controls.</summary>
    private void HandlePreference(object? sender, EventArgs args)
    {
        if (Dispatcher.UIThread.CheckAccess()) Synchronize();
        else Dispatcher.UIThread.Post(Synchronize);
    }
    /// <summary>Releases the GPU and the owned HWND when the foreground closes.</summary>
    private void HandleClosed(object? sender, EventArgs args) => Dispose();

    /// <summary>Updates only the owned backdrop's scene and z-order, never Explorer or another process.</summary>
    private void Synchronize()
    {
        if (_disposed || _busy || _failed) return;
        _busy = true;
        try
        {
            var visibleLiquid = _window.IsVisible && _preferences.Current.PanelTheme == PanelTheme.Liquid;
            if (!visibleLiquid)
            {
                _refresh.Stop();
                if (_background != 0) GlassNativeWindow.Hide(_background);
                _scene?.Dispose();
                _scene = null;
                _window.SetRefractiveBackdropActive(false);
                return;
            }
            var foreground = _window.TryGetPlatformHandle()?.Handle ?? 0;
            if (foreground == 0) return;
            var bounds = GlassNativeWindow.Bounds(foreground);
            var size = new Vector2(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
            var scale = Math.Max(GlassNativeWindow.Dpi(foreground), 96) / 96f;
            if (size.X < 1 || size.Y < 1) return;
            if (_background == 0)
            {
                _background = GlassNativeWindow.Create();
            }
            if (_scene is null || size != _size || scale != _scale)
            {
                _scene?.Dispose();
                _scene = new GlassScene(_background, foreground, size, scale);
                GlassNativeWindow.SetShape(_background, (int)size.X, (int)size.Y, scale);
                _size = size;
                _scale = scale;
            }
            GlassNativeWindow.Place(_background, foreground, bounds);
            _scene.Refresh(bounds);
            _refresh.Start();
            ApplyAvailability(foreground, bounds);
        }
        catch (Exception error)
        {
            Fail(error);
        }
        finally { _busy = false; }
    }

    /// <summary>Refreshes the small native lens only while visible, with no desktop pixels read into application memory.</summary>
    private void RefreshBackdrop(object? sender, EventArgs args)
    {
        if (_disposed || _failed || _busy || !_window.IsVisible) return;
        try
        {
            var foreground = _window.TryGetPlatformHandle()?.Handle ?? 0;
            if (foreground != 0)
            {
                var bounds = GlassNativeWindow.Bounds(foreground);
                _scene?.Refresh(bounds);
                ApplyAvailability(foreground, bounds);
            }
        }
        catch (Exception error) { Fail(error); }
    }

    /// <summary>Keeps the foreground opaque when no valid native source exists, and restores the lens when sources return.</summary>
    private void ApplyAvailability(nint foreground, GlassNativeWindow.Rectangle bounds)
    {
        var available = (_scene?.SourceCount ?? 0) > 0;
        if (available && (!_window.RefractiveBackdropActive || !GlassNativeWindow.IsDirectlyBehind(_background, foreground)))
            GlassNativeWindow.Place(_background, foreground, bounds);
        if (!available && _background != 0) GlassNativeWindow.Hide(_background);
        _window.SetRefractiveBackdropActive(available);
    }

    /// <summary>Stops the native material and displays an opaque usable panel after a graphics failure.</summary>
    private void Fail(Exception error)
    {
        Error = error.ToString();
        _failed = true;
        _refresh.Stop();
        if (_background != 0) GlassNativeWindow.Hide(_background);
        _scene?.Dispose();
        _scene = null;
        _window.SetRefractiveBackdropActive(false);
        _window.ShowStartupNotice(new StartupNotice("유리 효과를 사용할 수 없습니다",
            "현재 그래픽 환경에서 배경 굴절 효과를 시작하지 못해 일반 배경으로 표시합니다."));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _refresh.Stop();
        _refresh.Tick -= RefreshBackdrop;
        _window.Opened -= HandleOpened;
        _window.Activated -= HandleOpened;
        _window.PositionChanged -= HandlePosition;
        _window.PropertyChanged -= HandleGeometry;
        _window.Closed -= HandleClosed;
        _preferences.Changed -= HandlePreference;
        _window.SetRefractiveBackdropActive(false);
        _scene?.Dispose();
        if (_background != 0) GlassNativeWindow.Destroy(_background);
        _background = 0;
    }
}
