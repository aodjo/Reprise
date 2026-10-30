using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Reprise.Desktop;
using Xunit;

namespace Reprise.Platform.Windows.Tests;

/// <summary>
/// Covers Windows feed isolation, verified installer downloads, and safe exit handoff.
/// </summary>
public sealed class WindowsUpdaterTests : IDisposable
{
    /// <summary>Installed alpha used by the update fixture.</summary>
    private const string InstalledVersion = "2.0.0-alpha.1.20261028000000";
    /// <summary>Newer alpha whose installer is offered by the fixture.</summary>
    private const string IncomingVersion = "2.0.0-alpha.1.20261029000000";

    private readonly byte[] _bytes = Encoding.UTF8.GetBytes("installer fixture bytes");
    private readonly string _cache = Path.Combine(Path.GetTempPath(), $"reprise-windows-update-{Guid.NewGuid():N}");
    private readonly HttpClient _client;
    private readonly WindowsApplicationUpdater _updater;
    private WindowsInstallation? _installed = new(InstalledVersion, "20261028000000", @"C:\Users\Test\Apps\Reprise");
    private WindowsUpdateManifest _manifest;
    private byte[]? _download;
    private string? _productVersion = IncomingVersion;
    private bool _launched;
    private int _requests;

    /// <summary>
    /// Connects the updater to controlled HTTP responses and a recorded installer boundary.
    /// </summary>
    public WindowsUpdaterTests()
    {
        _manifest = new WindowsUpdateManifest(1, "windows", "alpha", "x64", IncomingVersion, "20261029000000",
            "https://updates.example/Reprise/windows/alpha/x64/packages/Reprise-new-Setup.exe",
            Convert.ToHexString(SHA256.HashData(_bytes)), _bytes.Length);
        _client = new HttpClient(new Handler(Respond));
        _updater = new WindowsApplicationUpdater(_client, new Uri("https://updates.example/Reprise/windows/"),
            "alpha", "x64", () => _installed, Launch, _ => _productVersion, _cache);
    }

    /// <summary>
    /// Merely checking discovers the correct installer without downloading or launching it.
    /// </summary>
    [Fact]
    public async Task CheckDoesNotInstall()
    {
        var update = await _updater.CheckAsync();
        Assert.NotNull(update);
        Assert.Equal(IncomingVersion, update.Version);
        Assert.Equal("20261029000000", update.BuildNumber);
        Assert.False(_launched);
        Assert.Equal(1, _requests);
    }

    /// <summary>Automatic download verifies and caches the installer without launching it; explicit installation reuses the verified bytes.</summary>
    [Fact]
    public async Task PreparedUpdateDoesNotInstallUntilRequested()
    {
        var update = (await _updater.CheckAsync())!;
        await _updater.PrepareAsync(update, new Progress<UpdateProgress>());
        Assert.False(_launched);
        Assert.Equal(2, _requests);
        await _updater.PrepareAsync(update, new Progress<UpdateProgress>());
        Assert.Equal(2, _requests);
        await _updater.InstallAsync(update, new Progress<UpdateProgress>());
        Assert.True(_launched);
        Assert.Equal(2, _requests);
    }

    /// <summary>A modified automatic download is reverified before installation and replaced through the validated download route.</summary>
    [Fact]
    public async Task CorruptPreparedUpdateIsNotExecuted()
    {
        var update = (await _updater.CheckAsync())!;
        await _updater.PrepareAsync(update, new Progress<UpdateProgress>());
        File.WriteAllText(Assert.Single(Directory.GetFiles(_cache, "package.exe", SearchOption.AllDirectories)), "tampered");
        await _updater.InstallAsync(update, new Progress<UpdateProgress>());
        Assert.True(_launched);
        Assert.Equal(3, _requests);
    }

    /// <summary>
    /// Handoff occurs only after digest and executable metadata both match.
    /// </summary>
    [Fact]
    public async Task VerifiedInstallerRequestsCleanApplicationExit()
    {
        var update = await _updater.CheckAsync();
        var result = await _updater.InstallAsync(update!, new Progress<UpdateProgress>());
        Assert.True(_launched);
        Assert.True(result.RequiresApplicationExit);
        Assert.Equal(2, _requests);
        Assert.Single(Directory.GetDirectories(_cache));
    }

    /// <summary>
    /// A server response for another release stage or architecture cannot cross installation boundaries.
    /// </summary>
    [Theory]
    [InlineData("beta", "x64")]
    [InlineData("release", "x64")]
    [InlineData("alpha", "arm64")]
    public async Task RejectsAnotherStageOrArchitecture(string channel, string architecture)
    {
        _manifest = _manifest with { Channel = channel, Architecture = architecture };
        await Assert.ThrowsAsync<InvalidDataException>(() => _updater.CheckAsync());
        Assert.False(_launched);
    }

    /// <summary>
    /// Even a larger beta version is rejected from an alpha-labeled manifest.
    /// </summary>
    [Fact]
    public async Task RejectsMislabeledVersion()
    {
        _manifest = _manifest with { Version = "9.0.0-beta.1" };
        await Assert.ThrowsAsync<InvalidDataException>(() => _updater.CheckAsync());
    }

    /// <summary>
    /// Download URLs cannot escape the official stage and architecture package directory.
    /// </summary>
    [Fact]
    public async Task RejectsExternalInstaller()
    {
        _manifest = _manifest with { Url = "https://external.example/Reprise-Setup.exe" };
        await Assert.ThrowsAsync<InvalidDataException>(() => _updater.CheckAsync());
    }

    /// <summary>
    /// Replaced installer bytes are rejected before launching anything.
    /// </summary>
    [Fact]
    public async Task RejectsCorruptInstaller()
    {
        var update = await _updater.CheckAsync();
        _download = Enumerable.Repeat((byte)'x', _bytes.Length).ToArray();
        await Assert.ThrowsAsync<InvalidDataException>(() => _updater.InstallAsync(update!, new Progress<UpdateProgress>()));
        Assert.False(_launched);
        Assert.Empty(Directory.GetDirectories(_cache));
    }

    /// <summary>
    /// A valid digest alone is insufficient when the executable declares a different version.
    /// </summary>
    [Fact]
    public async Task RequiresMatchingExecutableVersion()
    {
        var update = await _updater.CheckAsync();
        _productVersion = "9.0.0";
        await Assert.ThrowsAsync<InvalidDataException>(() => _updater.InstallAsync(update!, new Progress<UpdateProgress>()));
        Assert.False(_launched);
    }

    /// <summary>
    /// A concurrent installation invalidates the update selected earlier.
    /// </summary>
    [Fact]
    public async Task RechecksInstallationBeforeDownloading()
    {
        var update = await _updater.CheckAsync();
        _installed = _installed! with { BuildNumber = "20261030000000" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => _updater.InstallAsync(update!, new Progress<UpdateProgress>()));
        Assert.Equal(1, _requests);
    }

    /// <summary>
    /// A portable application does not claim that it can replace a registered installation.
    /// </summary>
    [Fact]
    public void PortableBuildHasNoInstallerHandoff()
    {
        _installed = null;
        Assert.False(_updater.IsAvailable);
    }

    /// <summary>
    /// Package paths containing apostrophes remain data in the generated helper.
    /// </summary>
    [Fact]
    public void HandoffQuotesPathsAndWaitsForParent()
    {
        var script = WindowsInstallerHandoff.BuildScript(@"C:\Users\O'Brien\setup.exe",
            _installed! with { Directory = @"C:\Users\O'Brien\Reprise" }, IncomingVersion, "20261029000000",
            1234, @"Software\Reprise\alpha\x64");
        Assert.Contains("'C:\\Users\\O''Brien\\setup.exe'", script);
        Assert.Contains("Wait-Process -Id 1234", script);
        Assert.DoesNotContain("ExecutionPolicy", script);
        Assert.Contains("BuildNumber", script);
    }

    /// <summary>
    /// Supplies fixture metadata or installer bytes through the real HTTP client interface.
    /// </summary>
    /// <param name="request">Outgoing update request.</param>
    /// <returns>The fixture response.</returns>
    private HttpResponseMessage Respond(HttpRequestMessage request)
    {
        _requests++;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = request.RequestUri!.AbsolutePath.EndsWith("update.json", StringComparison.Ordinal)
                ? JsonContent.Create(_manifest) : new ByteArrayContent(_download ?? _bytes),
        };
    }

    /// <summary>
    /// Records an installer handoff only when its payload and expected identity are intact.
    /// </summary>
    /// <param name="package">Verified cached installer.</param>
    /// <param name="installation">Existing registered application.</param>
    /// <param name="version">Expected incoming version.</param>
    /// <param name="build">Expected incoming build number.</param>
    private void Launch(string package, WindowsInstallation installation, string version, string build)
    {
        Assert.Equal(_bytes, File.ReadAllBytes(package));
        Assert.Equal(IncomingVersion, version);
        Assert.Equal("20261029000000", build);
        _launched = true;
    }

    /// <summary>
    /// Removes test cache files and disposes transport.
    /// </summary>
    public void Dispose()
    {
        _client.Dispose();
        if (Directory.Exists(_cache)) Directory.Delete(_cache, true);
    }

    /// <summary>HTTP handler with no external network traffic.</summary>
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        /// <summary>
        /// Returns the configured response for the update request.
        /// </summary>
        /// <param name="request">Outgoing request.</param>
        /// <param name="cancellationToken">Request cancellation.</param>
        /// <returns>The fixture response.</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
