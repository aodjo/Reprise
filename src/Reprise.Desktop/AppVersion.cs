using System.Reflection;

namespace Reprise.Desktop;

/// <summary>
/// Version shared by the panel, settings, and platform command line.
/// </summary>
public static class AppVersion
{
    /// <summary>
    /// Informational version including the published build number.
    /// </summary>
    public static string Current
    {
        get
        {
            var assembly = typeof(AppVersion).Assembly;
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            return string.IsNullOrWhiteSpace(informational)
                ? assembly.GetName().Version?.ToString(3) ?? "-"
                : informational.Split('+', 2)[0];
        }
    }
}
