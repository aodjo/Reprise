namespace Reprise.Desktop;

/// <summary>
/// Finds and installs a newer application package from the platform update source.
/// </summary>
public interface IApplicationUpdater
{
    /// <summary>
    /// Whether the platform supports installing updates in the current environment.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>Explains why this installation cannot use the platform updater.</summary>
    string UnavailableMessage => "이 설치 환경에서는 앱 내 업데이트를 사용할 수 없습니다.";

    /// <summary>
    /// Checks the published package index against the installed version.
    /// </summary>
    /// <param name="cancellationToken">Cancels the network request.</param>
    /// <returns>The newest eligible update, or null when none is newer.</returns>
    Task<ApplicationUpdate?> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads, verifies, and installs the update selected by the user.
    /// </summary>
    /// <param name="update">Update returned by the latest check.</param>
    /// <param name="progress">Receives download and installation status.</param>
    /// <param name="cancellationToken">Cancels download before installation begins.</param>
    /// <returns>Installation completion or a verified handoff requiring the application to exit.</returns>
    Task<ApplicationUpdateResult> InstallAsync(ApplicationUpdate update, IProgress<UpdateProgress> progress,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A published package that is newer than the installed application.
/// </summary>
/// <param name="InstalledVersion">Installed package version at check time.</param>
/// <param name="Version">Incoming package version.</param>
/// <param name="DownloadUri">Package address in the official update repository.</param>
/// <param name="Sha256">Expected SHA-256 from the package index.</param>
/// <param name="Size">Expected download size in bytes.</param>
/// <param name="BuildNumber">Optional build identity when stable versions share a semantic version.</param>
/// <param name="InstalledBuildNumber">Installed build identity captured during the update check.</param>
public sealed record ApplicationUpdate(
    string InstalledVersion, string Version, Uri DownloadUri, string Sha256, long Size,
    string? BuildNumber = null, string? InstalledBuildNumber = null);

/// <summary>
/// User-facing progress during a package download or installation.
/// </summary>
/// <param name="Message">Short description of the current step.</param>
/// <param name="Fraction">Download completion between zero and one, when known.</param>
public sealed record UpdateProgress(string Message, double? Fraction = null);

/// <summary>
/// Outcome of installation or handoff to an out-of-process platform installer.
/// </summary>
/// <param name="Message">Result shown to the user.</param>
/// <param name="RequiresApplicationExit">Whether the app must exit so installation can proceed.</param>
public sealed record ApplicationUpdateResult(string Message, bool RequiresApplicationExit = false);
