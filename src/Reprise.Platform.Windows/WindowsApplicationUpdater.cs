using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Diagnostics;
using NuGet.Versioning;
using Reprise.Core;
using Reprise.Desktop;

namespace Reprise.Platform.Windows;

/// <summary>
/// Downloads Windows installers from an architecture- and release-stage-specific update feed.
/// </summary>
internal sealed class WindowsApplicationUpdater : IApplicationUpdater
{
    /// <summary>Shared transport used only when the user requests an update check.</summary>
    private static readonly HttpClient Client = CreateClient();

    private readonly HttpClient _client;
    private readonly Uri _feed;
    private readonly string _channel;
    private readonly string _architecture;
    private readonly Func<WindowsInstallation?> _installation;
    private readonly Action<string, WindowsInstallation, string, string> _launch;
    private readonly string _cache;
    private readonly Func<string, string?> _readPackageVersion;

    /// <summary>
    /// Uses the running process's architecture and release stage without crossing either boundary.
    /// </summary>
    public WindowsApplicationUpdater() : this(Client, new Uri("https://aodjo.github.io/Reprise/windows/"),
        WindowsInstallation.Channel, WindowsInstallation.Architecture, WindowsInstallation.ReadCurrent,
        WindowsInstallerHandoff.Start, path => FileVersionInfo.GetVersionInfo(path).ProductVersion)
    {
    }

    /// <summary>
    /// Supplies network, installation state, and handoff boundaries for deterministic tests.
    /// </summary>
    /// <param name="client">HTTP transport.</param>
    /// <param name="root">Windows update repository root.</param>
    /// <param name="channel">Canonical release stage.</param>
    /// <param name="architecture">Process architecture.</param>
    /// <param name="installation">Reads the current per-user registration.</param>
    /// <param name="launch">Starts the installer helper after validation.</param>
    /// <param name="readPackageVersion">Reads the downloaded executable's product version.</param>
    /// <param name="cache">Optional test cache directory.</param>
    internal WindowsApplicationUpdater(HttpClient client, Uri root, string channel, string architecture,
        Func<WindowsInstallation?> installation, Action<string, WindowsInstallation, string, string> launch,
        Func<string, string?> readPackageVersion, string? cache = null)
    {
        if (ReleaseChannels.Canonicalize(channel) != channel || architecture is not ("x64" or "arm64" or "x86"))
        {
            throw new ArgumentException("Unsupported Windows update channel or architecture.");
        }

        _client = client;
        _channel = channel;
        _architecture = architecture;
        _feed = new Uri(root, $"{channel}/{architecture}/");
        _installation = installation;
        _launch = launch;
        _readPackageVersion = readPackageVersion;
        _cache = cache ?? Path.Combine(Path.GetTempPath(), "Reprise", "updates");
    }

    /// <inheritdoc />
    public bool IsAvailable => _installation() is not null;

    /// <inheritdoc />
    public string UnavailableMessage => "설치 프로그램으로 설치한 Reprise에서 앱 내 업데이트를 사용할 수 있습니다.";

    /// <inheritdoc />
    public async Task<ApplicationUpdate?> CheckAsync(CancellationToken cancellationToken = default)
    {
        var installed = _installation() ?? throw new InvalidOperationException(UnavailableMessage);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_feed, "update.json"));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        using var response = await _client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException("이 채널의 Windows 업데이트가 아직 게시되지 않았습니다.");
        }

        response.EnsureSuccessStatusCode();
        var manifest = await response.Content.ReadFromJsonAsync<WindowsUpdateManifest>(cancellationToken)
            ?? throw new InvalidDataException("업데이트 정보를 읽지 못했습니다.");
        if (manifest.SchemaVersion != 1 || manifest.Platform != "windows" || manifest.Channel != _channel ||
            manifest.Architecture != _architecture || string.IsNullOrWhiteSpace(manifest.Version) ||
            string.IsNullOrWhiteSpace(manifest.BuildNumber) || string.IsNullOrWhiteSpace(manifest.Url) ||
            string.IsNullOrWhiteSpace(manifest.Sha256))
        {
            throw new InvalidDataException("업데이트 채널 또는 컴퓨터 종류가 일치하지 않습니다.");
        }

        var update = new ApplicationUpdate(installed.Version, manifest.Version, new Uri(manifest.Url, UriKind.Absolute),
            manifest.Sha256, manifest.Size, manifest.BuildNumber, installed.BuildNumber);
        Validate(update);
        var comparison = VersionComparer.VersionRelease.Compare(NuGetVersion.Parse(update.Version), NuGetVersion.Parse(installed.Version));
        return comparison > 0 || comparison == 0 && long.Parse(manifest.BuildNumber) > long.Parse(installed.BuildNumber)
            ? update : null;
    }

    /// <inheritdoc />
    public async Task<ApplicationUpdateResult> InstallAsync(ApplicationUpdate update, IProgress<UpdateProgress> progress,
        CancellationToken cancellationToken = default)
    {
        Validate(update);
        var installed = _installation();
        if (installed is null || installed.Version != update.InstalledVersion || installed.BuildNumber != update.InstalledBuildNumber)
        {
            throw new InvalidOperationException("설치된 버전이 변경되었습니다. 업데이트를 다시 확인해 주세요.");
        }

        var directory = Path.Combine(_cache, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var package = Path.Combine(directory, "Reprise-Setup.exe");
        var handedOff = false;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(10));
            await UpdateDownload.DownloadAsync(_client, update, package, progress, timeout.Token);
            if (_readPackageVersion(package) != update.Version)
            {
                throw new InvalidDataException("설치 파일의 버전이 업데이트 정보와 다릅니다.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            _launch(package, installed, update.Version, update.BuildNumber!);
            handedOff = true;
            return new ApplicationUpdateResult("업데이트 설치를 시작합니다. 새 버전으로 자동으로 다시 실행됩니다.", true);
        }
        finally
        {
            if (!handedOff)
            {
                Directory.Delete(directory, true);
            }
        }
    }

    /// <summary>
    /// Enforces the platform, channel, architecture, digest, and build identity before downloading.
    /// </summary>
    /// <param name="update">Candidate returned by the update check.</param>
    private void Validate(ApplicationUpdate update)
    {
        var pool = new Uri(_feed, "packages/");
        if (ReleaseChannels.ForVersion(update.Version) != _channel || !NuGetVersion.TryParse(update.Version, out _) ||
            !long.TryParse(update.BuildNumber, out var build) || build <= 0 || update.Size <= 0 ||
            update.Sha256.Length != 64 || !update.Sha256.All(Uri.IsHexDigit) ||
            update.DownloadUri.Scheme != pool.Scheme || update.DownloadUri.Authority != pool.Authority ||
            !update.DownloadUri.AbsolutePath.StartsWith(pool.AbsolutePath, StringComparison.Ordinal) ||
            !update.DownloadUri.AbsolutePath.EndsWith("-Setup.exe", StringComparison.Ordinal) ||
            update.DownloadUri.Query.Length != 0 || update.DownloadUri.Fragment.Length != 0)
        {
            throw new InvalidDataException("업데이트 다운로드 정보가 올바르지 않습니다.");
        }
    }

    /// <summary>
    /// Creates bounded network transport for explicit user update requests.
    /// </summary>
    /// <returns>The shared update client.</returns>
    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Reprise-Windows/2.0");
        return client;
    }
}

/// <summary>
/// Published Windows installer metadata; separate files exist for every stage and architecture.
/// </summary>
/// <param name="SchemaVersion">Supported manifest schema.</param>
/// <param name="Platform">Must be Windows.</param>
/// <param name="Channel">Canonical release stage.</param>
/// <param name="Architecture">Target process architecture.</param>
/// <param name="Version">Installer product version.</param>
/// <param name="BuildNumber">Monotonic build identity for stable rebuilds.</param>
/// <param name="Url">Immutable installer URL.</param>
/// <param name="Sha256">Expected SHA-256.</param>
/// <param name="Size">Expected byte count.</param>
internal sealed record WindowsUpdateManifest(int SchemaVersion, string Platform, string Channel,
    string Architecture, string Version, string BuildNumber, string Url, string Sha256, long Size);
