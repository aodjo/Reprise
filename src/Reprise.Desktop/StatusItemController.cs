using System.ComponentModel;
using Avalonia.Threading;
using Reprise.Core;

namespace Reprise.Desktop;

/// <summary>
/// Keeps the tray entry in step with playback, the way the macOS status
/// item follows the menu bar store.
/// </summary>
/// <remarks>
/// Owns the only timer that touches the tray. It recomputes the label and
/// icon four times a second - the step rate of the label marquee - and
/// pushes an update only when something visible changed, so an idle tray
/// costs no bus traffic. Events from the tray arrive on the platform's
/// thread and are re-raised on the UI thread.
/// </remarks>
public sealed class StatusItemController : IDisposable
{
    private readonly IStatusItem _item;
    private readonly NowPlayingViewModel _viewModel;
    private readonly PreferencesStore _preferences;
    private readonly DispatcherTimer _timer;
    private string _fullText = string.Empty;
    private DateTimeOffset _textShownAt;
    private byte[]? _iconSource;
    private IReadOnlyList<StatusItemIcon> _icons = [];
    private StatusItemState? _lastState;
    private bool _disposed;

    /// <summary>
    /// Creates the controller and subscribes to its inputs.
    /// </summary>
    /// <param name="item">Tray entry to drive.</param>
    /// <param name="viewModel">Playback state to reflect.</param>
    /// <param name="preferences">Label and icon settings.</param>
    /// <example>
    /// <code>
    /// var controller = new StatusItemController(statusItem, viewModel, preferences);
    /// await controller.StartAsync();
    /// </code>
    /// </example>
    public StatusItemController(
        IStatusItem item,
        NowPlayingViewModel viewModel,
        PreferencesStore preferences)
    {
        _item = item;
        _viewModel = viewModel;
        _preferences = preferences;
        _timer = new DispatcherTimer(
            MenuBarText.StepInterval,
            DispatcherPriority.Background,
            (_, _) => Refresh());

        _item.Activated += (_, _) => Dispatcher.UIThread.Post(() => Activated?.Invoke(this, EventArgs.Empty));
        _viewModel.PropertyChanged += HandleViewModelChanged;
        _preferences.Changed += HandlePreferencesChanged;
    }

    /// <summary>
    /// Raised on the UI thread when the tray entry is clicked.
    /// </summary>
    public event EventHandler? Activated;

    /// <summary>
    /// Registers the tray entry and starts following playback.
    /// </summary>
    /// <param name="cancellationToken">Cancels the registration.</param>
    /// <returns>True when the desktop accepted the entry.</returns>
    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        var registered = await _item.StartAsync(cancellationToken);
        Refresh();
        _timer.Start();
        return registered;
    }

    /// <summary>
    /// Restarts a scrolling label from its beginning.
    /// </summary>
    /// <remarks>
    /// Called when the panel opens, if the user asked for it, so the title
    /// in the tray and the title in the panel line up.
    /// </remarks>
    public void RestartScroll()
    {
        _textShownAt = DateTimeOffset.UtcNow;
        Refresh();
    }

    /// <summary>
    /// Stops updates and releases the tray entry.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _viewModel.PropertyChanged -= HandleViewModelChanged;
        _preferences.Changed -= HandlePreferencesChanged;
        _item.Dispose();
    }

    /// <summary>
    /// Recomputes the tray content and pushes it if it changed.
    /// </summary>
    /// <remarks>
    /// Must run on the UI thread, both because the icon renderer needs it
    /// and because the view model is read here.
    /// </remarks>
    private void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        var preferences = _preferences.Current;
        var session = _viewModel.ActiveSession;
        var now = DateTimeOffset.UtcNow;
        var lyric = preferences.MenuBarShowsLyrics ? _viewModel.CurrentLyricLine(now)?.Text : null;
        var text = MenuBarText.Compose(session, lyric, preferences);
        if (text != _fullText)
        {
            _fullText = text;
            _textShownAt = now;
        }

        var label = preferences.AutomaticallyScrollsTitles
            ? MenuBarText.Window(text, preferences.MenuBarLabelLength, now - _textShownAt, MenuBarText.StepIntervalFor(preferences.MarqueePointsPerSecond))
            : MenuBarText.Window(text, preferences.MenuBarLabelLength, TimeSpan.Zero);
        var guide = preferences.MenuBarShowsLyrics && preferences.MenuBarReservesLabelWidth && label.Length > 0
            ? new string('M', Math.Max(preferences.MenuBarLabelLength, 1))
            : label;
        var tooltip = session is null
            ? "Reprise"
            : string.IsNullOrEmpty(session.Artist) ? session.Title : $"{session.Title} — {session.Artist}";

        var state = new StatusItemState(label, guide, tooltip, ResolveIcons(session, preferences));
        if (state == _lastState)
        {
            return;
        }

        _lastState = state;
        _item.Update(state);
    }

    /// <summary>
    /// Picks the album cover or the application icon, rendering on change.
    /// </summary>
    /// <param name="session">Session on display, or null.</param>
    /// <param name="preferences">Icon setting.</param>
    /// <returns>Icons to show, reused while the source is unchanged.</returns>
    private IReadOnlyList<StatusItemIcon> ResolveIcons(MediaSessionSnapshot? session, DesktopPreferences preferences)
    {
        var artwork = session is not null && preferences.MenuBarArtworkStyle == MenuBarArtworkStyle.AlbumArtwork
            ? _viewModel.ArtworkData
            : null;
        if (ReferenceEquals(artwork, _iconSource) && _icons.Count > 0)
        {
            return _icons;
        }

        _iconSource = artwork;
        _icons = artwork is null
            ? TrayIconRenderer.ApplicationIcons()
            : TrayIconRenderer.RenderArtwork(artwork);
        return _icons;
    }

    /// <summary>
    /// Refreshes promptly when playback state changes.
    /// </summary>
    /// <param name="sender">The view model.</param>
    /// <param name="e">Unused.</param>
    private void HandleViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(Refresh);
    }

    /// <summary>
    /// Refreshes when a tray setting changes.
    /// </summary>
    /// <param name="sender">The preferences store.</param>
    /// <param name="e">Unused.</param>
    private void HandlePreferencesChanged(object? sender, EventArgs e)
    {
        Refresh();
    }
}
