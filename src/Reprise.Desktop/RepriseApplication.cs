using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Reprise.Core;

namespace Reprise.Desktop;

/// <summary>
/// Avalonia application root shared by every desktop build of Reprise.
/// </summary>
/// <remarks>
/// Owns the theme, the panel window, the preferences, and the tray icon,
/// but deliberately not the media backend: the platform entry point supplies
/// that, which is what lets this assembly stay free of any platform-specific
/// reference.
/// </remarks>
public sealed class RepriseApplication : Application
{
    private TrayIcon? _trayIcon;

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
    /// Builds the panel and tray icon once Avalonia is ready.
    /// </summary>
    /// <remarks>
    /// Shutdown is switched to explicit so dismissing the panel leaves
    /// Reprise running in the tray, which is the behaviour a media controller
    /// wants: the panel is something to summon, not the app itself. Quitting
    /// goes through the panel's exit button or the tray menu.
    /// <para>
    /// The panel is shown once at launch so a desktop without a working tray
    /// still gets to see it; from then on the tray icon toggles it.
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
            var window = new NowPlayingWindow(
                new NowPlayingViewModel(service),
                preferences);
            window.ExitRequested += (_, _) => Quit(desktop, window);
            desktop.MainWindow = window;
            ConfigureTrayIcon(desktop, window);
            window.ShowPanel();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Installs the tray icon that keeps Reprise reachable once hidden.
    /// </summary>
    /// <remarks>
    /// Clicking the icon toggles the panel, as clicking the macOS status
    /// item does. The menu duplicates that and adds quit, since a tray icon
    /// is the only surface left once the panel is dismissed.
    /// <para>
    /// The icon is disposed on exit because a tray icon left registered can
    /// outlive the process as a dead entry on some desktop environments.
    /// </para>
    /// </remarks>
    /// <param name="desktop">
    /// Lifetime used to shut down and to hook the exit event.
    /// </param>
    /// <param name="window">Panel the menu shows and hides.</param>
    private void ConfigureTrayIcon(
        IClassicDesktopStyleApplicationLifetime desktop,
        NowPlayingWindow window)
    {
        var showItem = new NativeMenuItem { Header = "Reprise 열기" };
        showItem.Click += (_, _) => window.ShowPanel();

        var quitItem = new NativeMenuItem { Header = "Reprise 종료" };
        quitItem.Click += (_, _) => Quit(desktop, window);

        var menu = new NativeMenu();
        menu.Add(showItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(quitItem);

        using var iconStream = AssetLoader.Open(
            new Uri("avares://Reprise.Desktop/Assets/reprise.png"));
        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            IsVisible = true,
            ToolTipText = "Reprise",
            Menu = menu,
        };
        _trayIcon.Clicked += (_, _) => window.TogglePanel();
        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
        desktop.Exit += (_, _) => _trayIcon?.Dispose();
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
    private static void Quit(
        IClassicDesktopStyleApplicationLifetime desktop,
        NowPlayingWindow window)
    {
        window.AllowClose = true;
        desktop.Shutdown();
    }
}
