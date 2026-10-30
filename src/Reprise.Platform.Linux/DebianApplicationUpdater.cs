using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Reprise.Core;
using Reprise.Desktop;

namespace Reprise.Platform.Linux;

/// <summary>
/// Finds Linux updates in the published apt index and verifies downloads before installation.
/// </summary>
internal sealed class DebianApplicationUpdater : IApplicationUpdater
{
    /// <inheritdoc />
    public bool SupportsPreparation => true;

    /// <inheritdoc />
    public async Task PrepareAsync(ApplicationUpdate update, IProgress<UpdateProgress> progress, CancellationToken cancellationToken = default)
    {
        ValidateDownload(update);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        var path = await UpdateDownload.PrepareAsync(_client, update, _cacheRoot, progress, timeout.Token);
        var package = await _installer.InspectAsync(path);
        if (package.Version != update.Version || package.InstalledVersion != update.InstalledVersion)
        {
            File.Delete(path);
            throw new InvalidOperationException("업데이트 정보 또는 설치된 버전이 변경되었습니다. 업데이트를 다시 확인해 주세요.");
        }
    }
    /// <summary>HTTP transport shared by manual update checks.</summary>
    private static readonly HttpClient Client = CreateClient();

    private readonly DebianPackageInstaller _installer;
    private readonly HttpClient _client;
    private readonly Uri _repository;
    private readonly string _channel;
    private readonly string _releaseChannel;
    private readonly string _cacheRoot;

    /// <summary>
    /// Uses the official repository and the channel appropriate to this build.
    /// </summary>
    public DebianApplicationUpdater() : this(new DebianPackageInstaller(), Client,
        new Uri("https://aodjo.github.io/Reprise/"),
        ReleaseChannels.ForVersion(AppVersion.Current))
    {
    }

    /// <summary>
    /// Supplies transport and package tools for deterministic update integration tests.
    /// </summary>
    /// <param name="installer">Debian version checks and privileged installer.</param>
    /// <param name="client">HTTP transport for the index and package download.</param>
    /// <param name="repository">Repository root including its trailing slash.</param>
    /// <param name="channel">Published release stage or a historical compatibility alias.</param>
    /// <param name="cacheRoot">Optional private download directory for tests.</param>
    internal DebianApplicationUpdater(DebianPackageInstaller installer, HttpClient client,
        Uri repository, string channel, string? cacheRoot = null)
    {
        _releaseChannel = ReleaseChannels.Canonicalize(channel);
        _installer = installer;
        _client = client;
        _repository = repository;
        _channel = channel;
        _cacheRoot = cacheRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Reprise", "updates");
    }

    /// <inheritdoc />
    public bool IsAvailable => _installer.IsAvailable;

    /// <inheritdoc />
    public string UnavailableMessage => "앱 내 업데이트는 Ubuntu/Debian에 .deb로 설치한 Reprise에서 사용할 수 있습니다.";

    /// <inheritdoc />
    public async Task<ApplicationUpdate?> CheckAsync(CancellationToken cancellationToken = default)
    {
        var installed = await _installer.GetInstalledVersionAsync();
        var architecture = await _installer.GetArchitectureAsync();
        if (architecture is not ("amd64" or "arm64"))
        {
            throw new InvalidOperationException("이 컴퓨터용 업데이트는 아직 제공되지 않습니다.");
        }

        var index = new Uri(_repository, $"dists/{_channel}/main/binary-{architecture}/Packages");
        using var request = new HttpRequestMessage(HttpMethod.Get, index);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        using var response = await _client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException("업데이트 정보가 아직 게시되지 않았습니다. 잠시 후 다시 확인해 주세요.");
        }

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        ApplicationUpdate? newest = null;
        var found = false;
        foreach (var fields in ReadPackages(content))
        {
            if (fields.GetValueOrDefault("Package") != "reprise" ||
                fields.GetValueOrDefault("Architecture") is not { } packageArchitecture ||
                (packageArchitecture != architecture && packageArchitecture != "all"))
            {
                continue;
            }

            found = true;
            if (fields.GetValueOrDefault("Version") is not { Length: > 0 } version ||
                fields.GetValueOrDefault("Filename") is not { Length: > 0 } filename ||
                fields.GetValueOrDefault("SHA256") is not { } sha256 ||
                !long.TryParse(fields.GetValueOrDefault("Size"), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var size) || size <= 0)
            {
                throw new InvalidDataException("업데이트 정보가 올바르지 않습니다. 잠시 후 다시 확인해 주세요.");
            }

            var update = new ApplicationUpdate(installed, version, new Uri(_repository, filename), sha256, size);
            ValidateDownload(update);
            if (await _installer.IsNewerVersionAsync(version, newest?.Version ?? installed))
            {
                newest = update;
            }
        }

        if (!found)
        {
            throw new InvalidOperationException("업데이트 서버에 이 컴퓨터용 패키지가 아직 게시되지 않았습니다.");
        }

        return newest;
    }

    /// <inheritdoc />
    public async Task<ApplicationUpdateResult> InstallAsync(ApplicationUpdate update, IProgress<UpdateProgress> progress,
        CancellationToken cancellationToken = default)
    {
        ValidateDownload(update);
        var directory = Path.Combine(_cacheRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "reprise-update.deb");
        try
        {
            progress.Report(new UpdateProgress("업데이트를 다운로드하고 있습니다…", 0));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(10));
            try
            {
                await UpdateDownload.DownloadOrUsePreparedAsync(_client, update, _cacheRoot, path, progress, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("다운로드 시간이 초과되었습니다. 인터넷 연결을 확인한 뒤 다시 시도해 주세요.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(new UpdateProgress("다운로드한 업데이트를 확인하고 있습니다…"));
            var package = await _installer.InspectAsync(path);
            if (package.Version != update.Version || package.InstalledVersion != update.InstalledVersion)
            {
                throw new InvalidOperationException("업데이트 정보 또는 설치된 버전이 변경되었습니다. 업데이트를 다시 확인해 주세요.");
            }

            progress.Report(new UpdateProgress("Ubuntu 인증창에서 설치를 승인해 주세요. 설치가 끝날 때까지 기다려 주세요…"));
            await _installer.InstallAsync(package);
            return new ApplicationUpdateResult("업데이트를 설치했습니다. Reprise를 종료한 뒤 다시 실행해 주세요. 상단바 확장도 변경된 경우 로그아웃 후 다시 로그인해 주세요.");
        }
        finally
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Reprise: update cache cleanup failed: {exception.Message}");
            }
        }
    }

    /// <summary>
    /// Rejects downloads outside this channel's official package directory.
    /// </summary>
    /// <param name="update">Candidate package information.</param>
    private void ValidateDownload(ApplicationUpdate update)
    {
        try
        {
            if (ReleaseChannels.ForVersion(update.Version) != _releaseChannel)
            {
                throw new InvalidDataException("업데이트 채널이 일치하지 않습니다. 업데이트를 다시 확인해 주세요.");
            }
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("업데이트 버전 정보가 올바르지 않습니다.", exception);
        }

        var expected = new Uri(_repository, $"pool/{_channel}/main/r/reprise/");
        var uri = update.DownloadUri;
        if (uri.Scheme != expected.Scheme || uri.Authority != expected.Authority ||
            !uri.AbsolutePath.StartsWith(expected.AbsolutePath, StringComparison.Ordinal) ||
            !uri.AbsolutePath.EndsWith(".deb", StringComparison.Ordinal) ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0 || update.Size <= 0 ||
            update.Sha256.Length != 64 || !update.Sha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("업데이트 다운로드 정보가 올바르지 않습니다.");
        }
    }

    /// <summary>
    /// Reads Debian control stanzas while ignoring description continuation lines.
    /// </summary>
    /// <param name="content">Uncompressed Packages index.</param>
    /// <returns>The fields of each package stanza.</returns>
    private static IEnumerable<Dictionary<string, string>> ReadPackages(string content)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        using var reader = new StringReader(content);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
            {
                if (fields.Count != 0)
                {
                    yield return fields;
                    fields = new Dictionary<string, string>(StringComparer.Ordinal);
                }

                continue;
            }

            if (char.IsWhiteSpace(line[0]))
            {
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator > 0)
            {
                fields.Add(line[..separator], line[(separator + 1)..].Trim());
            }
        }

        if (fields.Count != 0)
        {
            yield return fields;
        }
    }

    /// <summary>
    /// Creates transport for explicit update checks, with a bounded request timeout.
    /// </summary>
    /// <returns>The shared HTTP client.</returns>
    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Reprise-Linux/2.0");
        return client;
    }
}
