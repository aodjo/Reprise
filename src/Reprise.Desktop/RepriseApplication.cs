using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Reprise.Core;

namespace Reprise.Desktop;

public sealed class RepriseApplication : Application
{
    private TrayIcon? _trayIcon;

    public static Func<IMediaSessionService>? MediaSessionServiceFactory
    {
        get;
        set;
    }

    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

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

    private static void ShowWindow(Window window)
    {
        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
    }
}
