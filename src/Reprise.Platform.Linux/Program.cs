using Avalonia;
using System.Runtime.InteropServices;
using Reprise.Desktop;
using Reprise.Platform.Linux.Mpris;

namespace Reprise.Platform.Linux;

internal static class Program
{
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
            new PlayerctlMediaSessionService(new ProcessRunner());

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder
            .Configure<RepriseApplication>()
            .UsePlatformDetect()
            .LogToTrace();
}
