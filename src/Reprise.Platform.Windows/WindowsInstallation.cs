using System.Runtime.InteropServices;
using Microsoft.Win32;
using NuGet.Versioning;
using Reprise.Core;
using Reprise.Desktop;

namespace Reprise.Platform.Windows;

/// <summary>
/// Per-user installed version and location recorded by the Windows installer.
/// </summary>
/// <param name="Version">Installed informational version.</param>
/// <param name="BuildNumber">Number identifying the installed build.</param>
/// <param name="Directory">Installation directory containing Reprise.exe.</param>
internal sealed record WindowsInstallation(string Version, string BuildNumber, string Directory)
{
    /// <summary>Release stage of the running application.</summary>
    internal static string Channel => ReleaseChannels.ForVersion(AppVersion.Current);

    /// <summary>Process architecture used to select compatible updates.</summary>
    internal static string Architecture => RuntimeInformation.ProcessArchitecture switch
    {
        System.Runtime.InteropServices.Architecture.X64 => "x64",
        System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
        System.Runtime.InteropServices.Architecture.X86 => "x86",
        _ => throw new PlatformNotSupportedException("Unsupported Windows architecture."),
    };

    /// <summary>Registration key isolated by release stage and process architecture.</summary>
    internal static string RegistryPath => $@"Software\Reprise\{Channel}\{Architecture}";

    /// <summary>
    /// Reads installation metadata only when it belongs to this running executable.
    /// </summary>
    /// <returns>The matching installed application, or null for a portable or unknown installation.</returns>
    internal static WindowsInstallation? ReadCurrent()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,
                Architecture == "x86" ? RegistryView.Registry32 : RegistryView.Registry64);
            using var key = hive.OpenSubKey(RegistryPath);
            if (key?.GetValue("Version") is not string version || !NuGetVersion.TryParse(version, out _) ||
                key.GetValue("BuildNumber") is not string build || !long.TryParse(build, out _) ||
                key.GetValue("InstallLocation") is not string directory ||
                !Path.GetFullPath(Path.Combine(directory, "Reprise.exe"))
                    .Equals(Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return new WindowsInstallation(version, build, Path.GetFullPath(directory));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException
            or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
