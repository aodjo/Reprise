using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Themes.Fluent;
using Reprise.Core;

namespace Reprise.Desktop;

/// <summary>
/// Avalonia application root shared by every desktop build of Reprise.
/// </summary>
/// <remarks>
/// Owns the theme, the panel window, the settings window, the preferences,
/// and the tray entry, but deliberately not the media backend or the tray
/// transport: the platform entry point supplies those, which is what lets
/// this assembly stay free of any platform-specific reference.
/// </remarks>
public sealed class RepriseApplication : Application
{
    private StatusItemController? _statusItem;
    private SettingsWindow? _settings;

    /// <summary>
    /// Runs platform setup and returns any instructions the user needs to see.
    /// </summary>
    /// <remarks>
    /// Invoked after the panel exists, so a background setup result cannot
    /// arrive before the UI is ready or delay the first window.
    /// </remarks>
    public static Func<Task<StartupNotice?>>? StartupNoticeFactory { get; set; }

    /// <summary>
    /// Supplies the platform service for manual update checks and installation.
    /// </summary>
    public static Func<IApplicationUpdater>? ApplicationUpdaterFactory { get; set; }

    /// <summary>Supplies optional official player account authorization for remote-device controls.</summary>
    public static Func<IPlayerAccountConnection>? PlayerAccountConnectionFactory { get; set; }

    /// <summary>
    /// Supplies the platform's per-user launch-at-login registration.
    /// </summary>
    public static Func<IAutostartEntry>? AutostartEntryFactory { get; set; }

    /// <summary>Attaches a platform-owned live optical background to the player panel.</summary>
    public static Func<NowPlayingWindow, PreferencesStore, IDisposable>? PanelBackdropFactory { get; set; }

    /// <summary>Whether launch should open the panel rather than remain in the tray.</summary>
    public static bool ShowPanelOnLaunch { get; set; } = true;

    /// <summary>
    /// Supplies the platform's media session backend.
    /// </summary>
    /// <remarks>
    /// Static because Avalonia constructs the Application itself, leaving no
    /// constructor for the entry point to inject through. The platform
    /// <c>Main</c> assigns this before starting the lifetime, and it is read
    /// exactly once during startup.
    /// </remarks>
    /// <example>
    /// <code>
    /// RepriseApplication.MediaSessionServiceFactory =
    ///     static () => new MprisMediaSessionService();
    /// </code>
    /// </example>
    public static Func<IMediaSessionService>? MediaSessionServiceFactory
    {
        get;
        set;
    }

    /// <summary>
    /// Supplies the platform's tray entry.
    /// </summary>
    /// <remarks>
    /// Optional: when unset, Avalonia's own tray icon is used, which shows
    /// an icon and tooltip but no label. Linux sets this to its D-Bus
    /// implementation so the track title can appear in the top bar.
    /// </remarks>
    /// <example>
    /// <code>
    /// RepriseApplication.StatusItemFactory =
    ///     static () => new StatusNotifierItem();
    /// </code>
    /// </example>
    public static Func<IStatusItem>? StatusItemFactory
    {
        get;
        set;
    }

    /// <summary>
    /// Supplies the service a shell extension reads to draw the top bar.
    /// </summary>
    /// <remarks>
    /// Optional. Without one, Reprise appears only as a tray entry, whose
    /// label the desktop draws and which therefore cannot scroll smoothly.
    /// </remarks>
    /// <example>
    /// <code>
    /// RepriseApplication.MenuBarPublisherFactory =
    ///     static () => new RepriseMenuBarService();
    /// </code>
    /// </example>
    public static Func<IMenuBarPublisher>? MenuBarPublisherFactory
    {
        get;
        set;
    }

    /// <summary>
    /// Installs the control theme before any window is created.
    /// </summary>
    /// <remarks>
    /// The variant is left at the platform default here; the panel window
    /// sets its own variant from the user's theme choice so its menus and
    /// pop-ups match the panel rather than the desktop.
    /// </remarks>
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Default;
        Styles.Add(new FluentTheme());
    }

    /// <summary>
    /// Builds the panel and tray entry once Avalonia is ready.
    /// </summary>
    /// <remarks>
    /// Shutdown is switched to explicit so dismissing the panel leaves
    /// Reprise running in the tray, which is the behaviour a media controller
    /// wants: the panel is something to summon, not the app itself. Clicking
    /// the tray entry toggles the panel, and quitting goes through the
    /// panel's exit button.
    /// <para>
    /// The panel is shown once at launch so a desktop without a working tray
    /// still gets to see it; from then on the tray entry toggles it.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no <see cref="MediaSessionServiceFactory"/> was installed,
    /// which means a platform entry point was skipped.
    /// </exception>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var service = MediaSessionServiceFactory?.Invoke()
                ?? throw new InvalidOperationException(
                    "A platform media-session service was not configured.");

            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var preferences = new PreferencesStore(PreferencesStore.DefaultPath);
            var updater = ApplicationUpdaterFactory?.Invoke() is { } platformUpdater
                ? new AutomaticApplicationUpdater(platformUpdater, preferences) : null;
            updater?.Start();
            desktop.Exit += (_, _) => updater?.Dispose();
            var viewModel = new NowPlayingViewModel(
                service,
                lyricsService: new LyricsService(),
                preferences: () => preferences.Current);
            viewModel.LastPlayedPlayerChanged += (_, kind) => Dispatcher.UIThread.Post(() =>
                preferences.Update(p => p with { LastPlayedPlayer = kind.ToString() }));
            var window = new NowPlayingWindow(viewModel, preferences);
            var backdrop = PanelBackdropFactory?.Invoke(window, preferences);
            desktop.Exit += (_, _) => backdrop?.Dispose();
            window.ExitRequested += (_, _) => Quit(desktop, window);
            window.SettingsRequested += (_, _) =>
            {
                if (_settings is null)
                {
                    _settings = new SettingsWindow(viewModel, preferences, updater, AutostartEntryFactory?.Invoke());
                    _settings.ExitRequested += (_, _) => Quit(desktop, window);
                }
                _settings.Present();
            };
            desktop.MainWindow = window;

            var statusItem = StatusItemFactory?.Invoke() ?? new AvaloniaTrayStatusItem(this);
            _statusItem = new StatusItemController(
                statusItem,
                viewModel,
                preferences,
                MenuBarPublisherFactory?.Invoke());
            _statusItem.Activated += (_, _) => window.TogglePanel();
            desktop.Exit += (_, _) => _statusItem?.Dispose();
            _ = StartStatusItemAsync(_statusItem);

            if (ShowPanelOnLaunch)
            {
                window.ShowPanel();
            }
            _ = ShowStartupNoticeAsync(window);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Presents platform setup guidance on the UI thread when setup finishes.
    /// </summary>
    /// <param name="window">Player panel receiving the notice.</param>
    /// <returns>A task that completes after setup and any notice presentation.</returns>
    private static async Task ShowStartupNoticeAsync(NowPlayingWindow window)
    {
        if (StartupNoticeFactory is not { } factory)
        {
            return;
        }

        try
        {
            if (await factory() is { } notice)
            {
                await Dispatcher.UIThread.InvokeAsync(() => window.ShowStartupNotice(notice));
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Reprise: platform setup unavailable: {exception.Message}");
        }
    }

    /// <summary>
    /// Registers the tray entry, reporting rather than failing when the
    /// desktop has no status area.
    /// </summary>
    /// <remarks>
    /// A missing tray is not fatal - the panel is already on screen - but it
    /// is worth a line on stderr, since the user will otherwise wonder why
    /// closing the panel appears to quit the app.
    /// </remarks>
    /// <param name="controller">Controller to start.</param>
    /// <returns>A task that completes once registration has been attempted.</returns>
    private static async Task StartStatusItemAsync(StatusItemController controller)
    {
        try
        {
            if (!await controller.StartAsync())
            {
                Console.Error.WriteLine(
                    "Reprise: no status notifier host found; the tray entry will appear when one starts.");
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Reprise: tray entry unavailable: {exception.Message}");
        }
    }

    /// <summary>
    /// Ends the process cleanly from either quit entry point.
    /// </summary>
    /// <remarks>
    /// The panel cancels its own close to hide instead, so it is told to
    /// allow the close first; otherwise shutdown would stall on the window.
    /// </remarks>
    /// <param name="desktop">Lifetime to shut down.</param>
    /// <param name="window">Panel that must be allowed to close.</param>
    private void Quit(
        IClassicDesktopStyleApplicationLifetime desktop,
        NowPlayingWindow window)
    {
        window.AllowClose = true;
        if (_settings is { } settings)
        {
            settings.AllowClose = true;
        }

        desktop.Shutdown();
    }
}
