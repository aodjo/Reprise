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
/// Owns the theme, the main window, and the tray icon, but deliberately not
/// the media backend: the platform entry point supplies that, which is what
/// lets this assembly stay free of any platform-specific reference.
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
    /// Applies the application theme before any window is created.
    /// </summary>
    /// <remarks>
    /// The dark variant is requested outright rather than following the
    /// desktop setting, because the window paints its own dark palette and a
    /// light system theme would leave the built-in controls mismatched.
    /// </remarks>
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    /// <summary>
    /// Builds the main window and tray icon once Avalonia is ready.
    /// </summary>
    /// <remarks>
    /// Shutdown is switched to explicit so closing the window leaves Reprise
    /// running in the tray, which is the behaviour a media controller wants:
    /// the window is a panel to summon, not the app itself. Quitting then has
    /// to go through the tray menu.
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
            var window = new NowPlayingWindow(
                new NowPlayingViewModel(service));
            desktop.MainWindow = window;
            ConfigureTrayIcon(desktop, window);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Installs the tray icon that keeps Reprise reachable once hidden.
    /// </summary>
    /// <remarks>
    /// The tray is the only way back to a closed window and the only way to
    /// quit, so it carries both actions. Quitting sets
    /// <see cref="NowPlayingWindow.AllowClose"/> first, since the window
    /// would otherwise cancel its own close and block shutdown.
    /// <para>
    /// The icon is disposed on exit because a tray icon left registered can
    /// outlive the process as a dead entry on some desktop environments.
    /// </para>
    /// </remarks>
    /// <param name="desktop">
    /// Lifetime used to shut down and to hook the exit event.
    /// </param>
    /// <param name="window">Window the menu shows and hides.</param>
    private void ConfigureTrayIcon(
        IClassicDesktopStyleApplicationLifetime desktop,
        NowPlayingWindow window)
    {
        var showItem = new NativeMenuItem { Header = "Reprise 열기" };
        showItem.Click += (_, _) => ShowWindow(window);

        var quitItem = new NativeMenuItem { Header = "종료" };
        quitItem.Click += (_, _) =>
        {
            window.AllowClose = true;
            desktop.Shutdown();
        };

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
        _trayIcon.Clicked += (_, _) => ShowWindow(window);
        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
        desktop.Exit += (_, _) => _trayIcon?.Dispose();
    }

    /// <summary>
    /// Brings the window back into view from the tray.
    /// </summary>
    /// <remarks>
    /// All three steps are needed: the window may have been hidden,
    /// minimised, or merely sent behind another window, and each state needs
    /// a different call to recover from.
    /// </remarks>
    /// <param name="window">Window to reveal and focus.</param>
    private static void ShowWindow(Window window)
    {
        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
    }
}
