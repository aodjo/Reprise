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
/// icon on a fine tick and pushes an update only when something visible
/// changed, so a still label costs no bus traffic while a scrolling one
/// steps as smoothly as a text label can. Events from the tray arrive on
/// the platform's thread and are re-raised on the UI thread.
/// </remarks>
public sealed class StatusItemController : IDisposable
{
    /// <summary>
    /// How close together two tray clicks have to be to count as one.
    /// </summary>
    private static readonly TimeSpan ActivationDebounce = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// How often the tray content is recomputed.
    /// </summary>
    /// <remarks>
    /// Fine enough to drive the label marquee at its fastest step and to
    /// put a lyric line up about when it is sung. Nothing is pushed unless
    /// the content actually changed, so a still label costs no bus traffic.
    /// </remarks>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(80);

    private readonly IStatusItem _item;
    private readonly IMenuBarPublisher? _publisher;
    private readonly NowPlayingViewModel _viewModel;
    private readonly PreferencesStore _preferences;
    private readonly DispatcherTimer _timer;
    private byte[] _iconPng = [];
    private MenuBarState? _lastPublished;
    private string _fullText = string.Empty;
    private DateTimeOffset _textShownAt;
    private byte[]? _iconSource;
    private IReadOnlyList<StatusItemIcon> _icons = [];
    private StatusItemState? _lastState;
    private DateTimeOffset _lastActivated;
    private bool _disposed;

    /// <summary>
    /// Creates the controller and subscribes to its inputs.
    /// </summary>
    /// <param name="item">Tray entry to drive.</param>
    /// <param name="viewModel">Playback state to reflect.</param>
    /// <param name="preferences">Label and icon settings.</param>
    /// <param name="publisher">
    /// Feeds a shell extension that draws the top bar itself, when the
    /// platform offers one. Null leaves the tray entry as the only surface.
    /// </param>
    /// <example>
    /// <code>
    /// var controller = new StatusItemController(statusItem, viewModel, preferences);
    /// await controller.StartAsync();
    /// </code>
    /// </example>
    public StatusItemController(
        IStatusItem item,
        NowPlayingViewModel viewModel,
        PreferencesStore preferences,
        IMenuBarPublisher? publisher = null)
    {
        _item = item;
        _publisher = publisher;
        _viewModel = viewModel;
        _preferences = preferences;
        _timer = new DispatcherTimer(
            RefreshInterval,
            DispatcherPriority.Background,
            (_, _) => Refresh());

        _item.Activated += (_, _) => Dispatcher.UIThread.Post(RaiseActivated);
        if (_publisher is not null)
        {
            _publisher.Activated += (_, _) => Dispatcher.UIThread.Post(RaiseActivated);
        }
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
        if (_publisher is not null)
        {
            await _publisher.StartAsync(cancellationToken);
        }

        Refresh();
        _timer.Start();
        return registered;
    }

    /// <summary>
    /// Passes a tray click on, ignoring one that repeats too quickly.
    /// </summary>
    /// <remarks>
    /// Some hosts answer a single click with more than one call - an
    /// activation and a context-menu request, say - and the panel toggles,
    /// so the two would cancel out and nothing would appear.
    /// </remarks>
    private void RaiseActivated()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastActivated < ActivationDebounce)
        {
            return;
        }

        _lastActivated = now;
        Activated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Sends a scrolling label back to the start of the text.
    /// </summary>
    /// <remarks>
    /// Called when the panel opens, if the user asked for it, so the title
    /// in the tray and the title in the panel read from the same place.
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
        _publisher?.Dispose();
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
            ? MenuBarText.Window(
                text,
                preferences.MenuBarLabelLength,
                now - _textShownAt,
                MenuBarText.StepIntervalFor(preferences.MarqueePointsPerSecond))
            : text;
        if (preferences.MenuBarShowsLyrics && preferences.MenuBarReservesLabelWidth && label.Length > 0)
        {
            label = MenuBarText.Reserve(label, preferences.MenuBarLabelLength);
        }

        var tooltip = session is null
            ? "Reprise"
            : string.IsNullOrEmpty(session.Artist) ? session.Title : $"{session.Title} — {session.Artist}";

        var state = new StatusItemState(label, tooltip, ResolveIcons(session, preferences));
        PublishToShell(session, text, tooltip, preferences);
        if (state == _lastState)
        {
            return;
        }

        _lastState = state;
        _item.Update(state);
    }

    /// <summary>
    /// Hands the shell extension the material to draw its own item.
    /// </summary>
    /// <remarks>
    /// The extension gets the whole line rather than the stepped label: it
    /// scrolls the text itself, pixel by pixel, so cutting it here would
    /// only take away what it needs.
    /// </remarks>
    /// <param name="session">Session on display, or null.</param>
    /// <param name="text">The full line for this moment.</param>
    /// <param name="tooltip">Hover text.</param>
    /// <param name="preferences">Current settings.</param>
    private void PublishToShell(
        MediaSessionSnapshot? session,
        string text,
        string tooltip,
        DesktopPreferences preferences)
    {
        if (_publisher is null)
        {
            return;
        }

        var state = new MenuBarState(
            text,
            tooltip,
            _iconPng,
            session is not null,
            _viewModel.IsPlaying,
            preferences.AutomaticallyScrollsTitles,
            preferences.MarqueePointsPerSecond,
            preferences.MenuBarLabelLength);
        if (state == _lastPublished)
        {
            return;
        }

        _lastPublished = state;
        _publisher.Publish(state);
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
        _iconPng = _publisher is null ? [] : TrayIconRenderer.RenderPng(artwork, 32);
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
