using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Reprise.Desktop;
using Reprise.Platform.Linux.Mpris;
using Reprise.Platform.Linux.Tray;

namespace Reprise.Platform.Linux;

/// <summary>
/// Entry point for the Linux build of Reprise.
/// </summary>
/// <remarks>
/// Keeps everything platform-specific in one place: the shared Reprise.Desktop
/// UI has no notion of MPRIS, and learns which backend to use only through
/// the factory this class installs before Avalonia starts.
/// </remarks>
internal static class Program
{
    /// <summary>
    /// Wires up the MPRIS backend and tray entry, then runs the Avalonia
    /// desktop lifetime.
    /// </summary>
    /// <remarks>
    /// Handles <c>--health-check</c> before anything else. Packaging and CI
    /// run the published binary on machines with no display and no session
    /// bus, so the check has to answer without touching Avalonia or D-Bus; it
    /// exists to prove the self-contained archive actually executes on the
    /// target.
    /// <para>
    /// The OS guard matters because the published output is self-contained
    /// and can be launched anywhere. Failing with an explanation beats
    /// letting the Avalonia platform detection crash further in.
    /// </para>
    /// </remarks>
    /// <param name="args">
    /// Command line arguments, forwarded to Avalonia once the
    /// Reprise-specific flags have been handled.
    /// </param>
    /// <returns>0 on success, 1 when run on a non-Linux platform.</returns>
    /// <example>
    /// <code>
    /// $ ./reprise --health-check
    /// Reprise Linux 2.0.0-alpha.1 (linux-arm64)
    /// </code>
    /// </example>
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
            Console.WriteLine(
                $"Reprise Linux {AppVersion.Current} ({RuntimeInformation.RuntimeIdentifier})");
            return OperatingSystem.IsLinux() ? 0 : 1;
        }

        if (!OperatingSystem.IsLinux())
        {
            Console.Error.WriteLine("Reprise.Platform.Linux can only run on Linux.");
            return 1;
        }

        RepriseApplication.MediaSessionServiceFactory = static () =>
            new MprisMediaSessionService();
        RepriseApplication.StatusItemFactory = static () =>
            new StatusNotifierItem();
        RepriseApplication.MenuBarPublisherFactory = static () =>
            new RepriseMenuBarService();

        RepriseApplication.StartupNoticeFactory = EnableShellExtensionAsync;
        RepriseApplication.ApplicationUpdaterFactory = static () => new DebianApplicationUpdater();

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Turns the GNOME Shell extension on, in the background.
    /// </summary>
    /// <remarks>
    /// Runs after the panel exists without blocking its first appearance.
    /// A required session reload is explained in the panel as well as stderr.
    /// On other desktops the shell is absent and no message is shown.
    /// </remarks>
    /// <returns>Setup guidance when a session reload is needed, otherwise null.</returns>
    private static async Task<StartupNotice?> EnableShellExtensionAsync()
    {
        var result = await GnomeShellExtension.EnsureEnabledAsync();
        switch (result)
        {
            case GnomeShellExtensionResult.Enabled:
                Console.Error.WriteLine("Reprise: enabled the GNOME top bar extension.");
                break;
            case GnomeShellExtensionResult.NeedsReload:
                Console.Error.WriteLine(
                    "Reprise: installed or updated the GNOME top bar extension. Log out and back in to load the new code.");
                return new StartupNotice(
                    "로그아웃 후 다시 로그인해 주세요",
                    "상단바 업데이트를 적용하려면 작업을 저장한 뒤 Ubuntu에서 로그아웃하고 다시 로그인해 주세요. 로그인 후 Reprise를 실행하면 부드러운 제목 스크롤이 적용됩니다.");
            case GnomeShellExtensionResult.Failed:
                Console.Error.WriteLine(
                    $"Reprise: could not enable the GNOME top bar extension. Try: gnome-extensions enable {GnomeShellExtension.Uuid}");
                break;
        }

        return null;
    }

    /// <summary>
    /// Builds the Avalonia application configuration.
    /// </summary>
    /// <remarks>
    /// Public and parameterless by convention: Avalonia's design-time tooling
    /// and XAML previewer locate this method by name.
    /// </remarks>
    /// <returns>
    /// A configured builder with the windowing backend detected from the
    /// environment, X11 or Wayland, and Pretendard as the default typeface.
    /// </returns>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder
            .Configure<RepriseApplication>()
            .With(new FontManagerOptions
            {
                DefaultFamilyName = PanelTypography.FamilyName,
            })
            .UsePlatformDetect()
            .LogToTrace();
}
