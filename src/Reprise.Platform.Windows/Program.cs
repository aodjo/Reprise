using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using Reprise.Desktop;
using Reprise.Platform.Windows.Media;

namespace Reprise.Platform.Windows;

/// <summary>
/// Windows entry point wiring native media sessions into the shared desktop UI.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Handles display-free diagnostics before starting the Windows desktop lifetime.
    /// </summary>
    /// <param name="args">Diagnostic flags or Avalonia arguments.</param>
    /// <returns>Zero on success, otherwise an unsupported-platform or startup error.</returns>
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--version", StringComparer.Ordinal))
        {
            Console.WriteLine(AppVersion.Current);
            return 0;
        }

        if (args.Contains("--health-check", StringComparer.Ordinal))
        {
            Console.WriteLine($"Reprise Windows {AppVersion.Current} ({RuntimeInformation.RuntimeIdentifier})");
            return OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763) ? 0 : 1;
        }

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            Console.Error.WriteLine("Reprise requires Windows 10 version 1809 or later.");
            return 1;
        }

        RepriseApplication.MediaSessionServiceFactory = static () => new WindowsMediaSessionService();
        RepriseApplication.AutostartEntryFactory = static () => new WindowsAutostartEntry();
        RepriseApplication.ApplicationUpdaterFactory = static () => new WindowsApplicationUpdater();
        RepriseApplication.MenuBarPublisherFactory = static () => new Taskbar.WindowsTaskbarLyricsPublisher();
        RepriseApplication.PanelBackdropFactory = static (window, preferences) => new Glass.WindowsGlassBackdrop(window, preferences);
        RepriseApplication.ShowPanelOnLaunch = !args.Contains("--startup", StringComparer.Ordinal);
        using var instance = new SingleInstanceController();
        if (!instance.IsPrimary)
        {
            instance.ActivateExistingAsync().GetAwaiter().GetResult();
            return 0;
        }

        instance.Listen(() => Dispatcher.UIThread.Post(ActivatePanel));
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Brings the existing tray panel forward on the application's UI thread.
    /// </summary>
    private static void ActivatePanel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
            { MainWindow: NowPlayingWindow panel })
        {
            panel.ShowPanel();
        }
    }

    /// <summary>
    /// Builds the Windows desktop using the shared typeface and Fluent controls.
    /// </summary>
    /// <returns>The configured application builder.</returns>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<RepriseApplication>()
        .With(new FontManagerOptions { DefaultFamilyName = PanelTypography.FamilyName })
        .With(new Win32PlatformOptions { WinUICompositionBackdropCornerRadius = (float)NowPlayingWindow.PanelCornerRadius })
        .UsePlatformDetect()
        .LogToTrace();
}
