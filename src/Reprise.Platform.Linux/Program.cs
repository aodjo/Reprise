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
        if (args.Contains("--health-check", StringComparer.Ordinal))
        {
            Console.WriteLine(
                $"Reprise Linux 2.0.0-alpha.1 ({RuntimeInformation.RuntimeIdentifier})");
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

        _ = EnableShellExtensionAsync();

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Turns the GNOME Shell extension on, in the background.
    /// </summary>
    /// <remarks>
    /// Runs unawaited: the panel must not wait on the shell, and every
    /// outcome is one line on stderr rather than anything the user has to
    /// answer. On any other desktop the shell is simply absent and this
    /// says nothing at all.
    /// </remarks>
    /// <returns>A task that completes once the shell has answered.</returns>
    private static async Task EnableShellExtensionAsync()
    {
        var result = await GnomeShellExtension.EnsureEnabledAsync();
        switch (result)
        {
            case GnomeShellExtensionResult.Enabled:
                Console.Error.WriteLine("Reprise: enabled the GNOME top bar extension.");
                break;
            case GnomeShellExtensionResult.NeedsReload:
                Console.Error.WriteLine(
                    "Reprise: installed the GNOME top bar extension. Log out and back in, or press Alt+F2 and type r on Xorg, to load it.");
                break;
            case GnomeShellExtensionResult.Failed:
                Console.Error.WriteLine(
                    $"Reprise: could not enable the GNOME top bar extension. Try: gnome-extensions enable {GnomeShellExtension.Uuid}");
                break;
        }
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
