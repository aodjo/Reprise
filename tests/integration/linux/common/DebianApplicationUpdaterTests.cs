using System.Net;
using System.Security.Cryptography;
using System.Text;
using Reprise.Desktop;
using Xunit;

namespace Reprise.Platform.Linux.Tests;

/// <summary>
/// Exercises discovery, download verification, and installation using controlled HTTP responses.
/// </summary>
public sealed class DebianApplicationUpdaterTests : IDisposable
{
    /// <summary>Current package in the test installation.</summary>
    private const string Current = "2.0.0~alpha.1+20261028000000";

    /// <summary>First eligible update in the package index.</summary>
    private const string Next = "2.0.0~alpha.1.20261029000000+20261029000000";

    /// <summary>A still newer package, regardless of stanza ordering.</summary>
    private const string Latest = "2.0.0~alpha.1.20261030000000+20261030000000";

    private readonly string _cache = Path.Combine(Path.GetTempPath(), $"reprise-updater-{Guid.NewGuid():N}");
    private readonly byte[] _bytes = Encoding.UTF8.GetBytes("downloaded package bytes");
    private readonly List<Uri> _requests = [];
    private readonly List<(string Executable, string[] Arguments)> _commands = [];
    private readonly HttpClient _client;
    private readonly DebianApplicationUpdater _updater;
    private string _index = string.Empty;
    private string _architecture = "amd64";
    private string _installed = Current;
    private string _downloadedVersion = Next;
    private byte[]? _responseBytes;
    private HttpStatusCode _status = HttpStatusCode.OK;
    private int _installExitCode;

    /// <summary>
    /// Connects real updater logic to deterministic HTTP and package command boundaries.
    /// </summary>
    public DebianApplicationUpdaterTests()
    {
        _client = new HttpClient(new Handler(Respond));
        _updater = new DebianApplicationUpdater(new DebianPackageInstaller(RunAsync), _client,
            new Uri("https://updates.example/Reprise/"), "develop", _cache);
        _index = Stanza(Next);
    }

    /// <summary>Preparing an update never invokes the authenticated installer and allows explicit installation to reuse verified bytes.</summary>
    [Fact]
    public async Task PreparationRequiresSeparateExplicitInstallation()
    {
        var update = (await _updater.CheckAsync())!;
        await _updater.PrepareAsync(update, new Progress<UpdateProgress>());
        Assert.Equal(2, _requests.Count);
        Assert.DoesNotContain(_commands, command => command.Executable.EndsWith("/pkexec"));
        await _updater.InstallAsync(update, new Progress<UpdateProgress>());
        Assert.Equal(2, _requests.Count);
        Assert.Contains(_commands, command => command.Executable.EndsWith("/pkexec"));
    }

    /// <summary>Changed cached bytes cannot reach dpkg; the installer obtains and verifies the original package again.</summary>
    [Fact]
    public async Task CorruptPreparedPackageIsDownloadedAgain()
    {
        var update = (await _updater.CheckAsync())!;
        await _updater.PrepareAsync(update, new Progress<UpdateProgress>());
        File.WriteAllText(Assert.Single(Directory.GetFiles(_cache, "package.deb", SearchOption.AllDirectories)), "tampered");
        await _updater.InstallAsync(update, new Progress<UpdateProgress>());
        Assert.Equal(3, _requests.Count);
    }

    /// <summary>
    /// Discovery requests the native architecture index and chooses the newest eligible stanza.
    /// </summary>
    [Theory]
    [InlineData("amd64")]
    [InlineData("arm64")]
    public async Task FindsNewestNativePackageWithoutDownloading(string architecture)
    {
        _architecture = architecture;
        _index = Stanza(Latest, architecture) + Stanza(Next, architecture) + Stanza(Current, architecture);
        var result = await _updater.CheckAsync();

        Assert.NotNull(result);
        Assert.Equal(Latest, result.Version);
        Assert.Equal(Current, result.InstalledVersion);
        Assert.EndsWith($"/binary-{architecture}/Packages", Assert.Single(_requests).AbsolutePath);
        Assert.DoesNotContain(_commands, command => command.Executable.EndsWith("/pkexec"));
    }

    /// <summary>
    /// An installed or older published build does not produce an Install action.
    /// </summary>
    [Fact]
    public async Task ReturnsNoUpdateWhenInstalledVersionIsCurrent()
    {
        _installed = Latest;
        _index = Stanza(Next) + Stanza(Latest);
        Assert.Null(await _updater.CheckAsync());
        Assert.Single(_requests);
    }

    /// <summary>
    /// Wrong-architecture entries cannot be installed even when their version is higher.
    /// </summary>
    [Fact]
    public async Task IgnoresWrongArchitecture()
    {
        _index = Stanza(Latest, "arm64") + Stanza(Current);
        Assert.Null(await _updater.CheckAsync());
    }

    /// <summary>
    /// Missing or empty publication is reported as unavailable, never as already up to date.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.OK)]
    public async Task ReportsMissingPublication(HttpStatusCode status)
    {
        _status = status;
        _index = string.Empty;
        await Assert.ThrowsAsync<InvalidOperationException>(() => _updater.CheckAsync());
    }

    /// <summary>
    /// Network failures stay distinguishable from a valid no-update response.
    /// </summary>
    [Fact]
    public async Task ReportsServerFailure()
    {
        _status = HttpStatusCode.ServiceUnavailable;
        await Assert.ThrowsAsync<HttpRequestException>(() => _updater.CheckAsync());
    }

    /// <summary>
    /// The index cannot redirect installation to an external or unrelated package path.
    /// </summary>
    [Theory]
    [InlineData("https://elsewhere.example/reprise.deb")]
    [InlineData("../../reprise.deb")]
    [InlineData("pool/stable/main/r/reprise/reprise.deb")]
    public async Task RejectsUnexpectedDownloadLocation(string filename)
    {
        _index = Stanza(Next, filename: filename);
        await Assert.ThrowsAsync<InvalidDataException>(() => _updater.CheckAsync());
    }

    /// <summary>
    /// No package is eligible without the digest required for download verification.
    /// </summary>
    [Fact]
    public async Task RequiresChecksumInIndex()
    {
        _index = Stanza(Next).Replace($"SHA256: {Convert.ToHexString(SHA256.HashData(_bytes))}\n", "");
        await Assert.ThrowsAsync<InvalidDataException>(() => _updater.CheckAsync());
    }

    /// <summary>
    /// A mislabeled feed cannot offer a package belonging to another release stage.
    /// </summary>
    [Theory]
    [InlineData("release", "2.1.0~alpha.1")]
    [InlineData("release", "2.1.0~beta.1")]
    [InlineData("beta", "2.1.0~alpha.1")]
    [InlineData("beta", "2.1.0")]
    [InlineData("alpha", "2.1.0~beta.1")]
    [InlineData("alpha", "2.1.0")]
    [InlineData("develop", "2.1.0~beta.1")]
    public async Task RejectsPackagesFromAnotherChannel(string channel, string version)
    {
        _index = Stanza(version, filename: $"pool/{channel}/main/r/reprise/reprise.deb");
        var updater = new DebianApplicationUpdater(new DebianPackageInstaller(RunAsync), _client,
            new Uri("https://updates.example/Reprise/"), channel, _cache);
        await Assert.ThrowsAsync<InvalidDataException>(() => updater.CheckAsync());
        Assert.DoesNotContain(_commands, command => command.Executable.EndsWith("/pkexec"));
    }

    /// <summary>
    /// Successful installation downloads the expected bytes, authenticates, and cleans its cache.
    /// </summary>
    [Fact]
    public async Task DownloadsVerifiesAndInstallsSelectedUpdate()
    {
        var update = await _updater.CheckAsync();
        var progress = new ProgressRecorder();
        await _updater.InstallAsync(update!, progress);

        Assert.Equal(Next, _installed);
        Assert.Equal(2, _requests.Count);
        Assert.Single(_commands, command => command.Executable.EndsWith("/pkexec"));
        Assert.Contains(progress.Values, value => value.Fraction == 1);
        Assert.Empty(Directory.GetFileSystemEntries(_cache));
    }

    /// <summary>
    /// Corrupt or incomplete bytes never reach the privileged installer.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectsCorruptDownloadBeforeAuthentication(bool wrongSize)
    {
        var update = await _updater.CheckAsync();
        _responseBytes = wrongSize ? [1] : Enumerable.Repeat((byte)'x', _bytes.Length).ToArray();
        await Assert.ThrowsAsync<InvalidDataException>(() => _updater.InstallAsync(update!, new ProgressRecorder()));
        Assert.DoesNotContain(_commands, command => command.Executable.EndsWith("/pkexec"));
        Assert.Empty(Directory.GetFileSystemEntries(_cache));
    }

    /// <summary>
    /// A package with different internal metadata cannot substitute for the checked version.
    /// </summary>
    [Fact]
    public async Task RequiresDownloadedMetadataToMatchIndex()
    {
        var update = await _updater.CheckAsync();
        _downloadedVersion = Latest;
        await Assert.ThrowsAsync<InvalidOperationException>(() => _updater.InstallAsync(update!, new ProgressRecorder()));
        Assert.DoesNotContain(_commands, command => command.Executable.EndsWith("/pkexec"));
    }

    /// <summary>
    /// Cancelling the authentication dialog cleans downloaded files and allows retry.
    /// </summary>
    [Fact]
    public async Task CleansDownloadAfterAuthenticationCancellation()
    {
        var update = await _updater.CheckAsync();
        _installExitCode = 126;
        await Assert.ThrowsAsync<OperationCanceledException>(() => _updater.InstallAsync(update!, new ProgressRecorder()));
        Assert.Equal(Current, _installed);
        Assert.Empty(Directory.GetFileSystemEntries(_cache));
    }

    /// <summary>
    /// A concurrent package update invalidates the previous check before authentication.
    /// </summary>
    [Fact]
    public async Task RejectsStaleCheck()
    {
        var update = await _updater.CheckAsync();
        _installed = Latest;
        await Assert.ThrowsAsync<InvalidOperationException>(() => _updater.InstallAsync(update!, new ProgressRecorder()));
        Assert.DoesNotContain(_commands, command => command.Executable.EndsWith("/pkexec"));
    }

    /// <summary>
    /// Builds a Debian stanza with a real digest of the download fixture.
    /// </summary>
    /// <param name="version">Package version.</param>
    /// <param name="architecture">Package architecture.</param>
    /// <param name="filename">Optional alternate download location.</param>
    /// <returns>One complete Packages stanza.</returns>
    private string Stanza(string version, string architecture = "amd64", string? filename = null) =>
        $"Package: reprise\nVersion: {version}\nArchitecture: {architecture}\n" +
        $"Filename: {filename ?? $"pool/develop/main/r/reprise/reprise_{version}_{architecture}.deb"}\n" +
        $"Size: {_bytes.Length}\nSHA256: {Convert.ToHexString(SHA256.HashData(_bytes))}\n" +
        "Description: Reprise\n Description continuation: ignored\n\n";

    /// <summary>
    /// Serves the index or package bytes without any external network traffic.
    /// </summary>
    /// <param name="request">The outgoing request to record.</param>
    /// <returns>The configured response.</returns>
    private HttpResponseMessage Respond(HttpRequestMessage request)
    {
        _requests.Add(request.RequestUri!);
        return new HttpResponseMessage(_status)
        {
            Content = request.RequestUri!.AbsolutePath.EndsWith("/Packages", StringComparison.Ordinal)
                ? new StringContent(_index)
                : new ByteArrayContent(_responseBytes ?? _bytes),
        };
    }

    /// <summary>
    /// Supplies package metadata and records the privileged installation boundary.
    /// </summary>
    /// <param name="executable">Absolute executable name.</param>
    /// <param name="arguments">Literal arguments supplied by production code.</param>
    /// <returns>The package command result.</returns>
    private Task<PackageCommandResult> RunAsync(string executable, IReadOnlyList<string> arguments)
    {
        _commands.Add((executable, arguments.ToArray()));
        var versions = new[] { Current, Next, Latest };
        var result = Path.GetFileName(executable) switch
        {
            "dpkg-query" => new PackageCommandResult(0, $"install ok installed\n{_installed}"),
            "dpkg" when arguments[0] == "--print-architecture" => new PackageCommandResult(0, _architecture),
            "dpkg" => new PackageCommandResult(Array.IndexOf(versions, arguments[1]) > Array.IndexOf(versions, arguments[3]) ? 0 : 1),
            "dpkg-deb" => new PackageCommandResult(0, $"reprise\n{_downloadedVersion}\n{_architecture}\n"),
            "pkexec" => new PackageCommandResult(_installExitCode),
            _ => throw new InvalidOperationException($"Unexpected executable: {executable}"),
        };
        if (executable.EndsWith("/pkexec") && _installExitCode == 0)
        {
            Assert.True(File.Exists(arguments[^1]));
            Assert.Equal(_bytes, File.ReadAllBytes(arguments[^1]));
            _installed = _downloadedVersion;
        }

        return Task.FromResult(result);
    }

    /// <summary>
    /// Disposes HTTP transport and removes only this test's download directory.
    /// </summary>
    public void Dispose()
    {
        _client.Dispose();
        if (Directory.Exists(_cache))
        {
            Directory.Delete(_cache, true);
        }
    }

    /// <summary>
    /// HTTP handler that delegates responses to the owning test.
    /// </summary>
    /// <param name="respond">Produces a response for each request.</param>
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        /// <summary>
        /// Returns the configured response while respecting request cancellation.
        /// </summary>
        /// <param name="request">Outgoing request.</param>
        /// <param name="cancellationToken">Request cancellation.</param>
        /// <returns>The response.</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }

    /// <summary>
    /// Records progress synchronously so download milestones can be asserted.
    /// </summary>
    private sealed class ProgressRecorder : IProgress<UpdateProgress>
    {
        public List<UpdateProgress> Values { get; } = [];

        /// <summary>
        /// Records one update status.
        /// </summary>
        /// <param name="value">Status emitted by the updater.</param>
        public void Report(UpdateProgress value) => Values.Add(value);
    }
}
