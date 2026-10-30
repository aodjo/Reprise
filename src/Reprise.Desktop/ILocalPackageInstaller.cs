namespace Reprise.Desktop;

/// <summary>
/// Inspects and installs a local platform update after user confirmation.
/// </summary>
public interface ILocalPackageInstaller
{
    /// <summary>
    /// Whether the required platform package tools are installed.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Validates a selected file and reads its installed and incoming versions.
    /// </summary>
    /// <param name="path">Local update package selected by the user.</param>
    /// <returns>A validated update to display before requesting authentication.</returns>
    Task<LocalPackageUpdate> InspectAsync(string path);

    /// <summary>
    /// Requests platform authentication and installs a previously confirmed update.
    /// </summary>
    /// <param name="update">The update the user confirmed.</param>
    /// <returns>A task completing after the installed version has been verified.</returns>
    Task InstallAsync(LocalPackageUpdate update);
}

/// <summary>
/// Versions shown before a local package update is installed.
/// </summary>
/// <param name="Path">Absolute path of the selected package.</param>
/// <param name="InstalledVersion">Version currently registered with the package manager.</param>
/// <param name="Version">Version in the selected package.</param>
public sealed record LocalPackageUpdate(string Path, string InstalledVersion, string Version);
