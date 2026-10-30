using Reprise.Desktop;
using Xunit;

namespace Reprise.Platform.Linux.Tests;

/// <summary>
/// Covers validation and privileged command boundaries for local Debian updates.
/// </summary>
public sealed class DebianPackageInstallerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"reprise-package-{Guid.NewGuid():N}");
    private readonly List<(string Executable, string[] Arguments)> _commands = [];
    private string _packageName = "reprise";
    private string _architecture = "amd64";
    private string _packageVersion = "2.0.0~alpha.1.20261029010000+20261029010000";
    private string _installedVersion = "2.0.0~alpha.1+20261028132951";
    private string _status = "install ok installed";
    private bool _newer = true;
    private int _installExitCode;
    private bool _verifyInstall = true;

    /// <summary>
    /// A path containing spaces and shell syntax remains one literal argument.
    /// </summary>
    [Fact]
    public async Task InstallsLiteralPathAndVerifiesInstalledVersion()
    {
        var installer = new DebianPackageInstaller(RunAsync);
        var path = Package("Reprise 업데이트 $(touch unwanted).deb");
        var update = await installer.InspectAsync(path);

        Assert.Equal(_installedVersion, update.InstalledVersion);
        Assert.Equal(_packageVersion, update.Version);
        await installer.InstallAsync(update);

        var privileged = Assert.Single(_commands, command => command.Executable.EndsWith("/pkexec"));
        Assert.Equal(new[] { "--disable-internal-agent", "/usr/bin/apt-get", "--yes", "--no-remove",
            "-o", "Dpkg::Use-Pty=0", "install", "--", path }, privileged.Arguments);
        Assert.Equal("/usr/bin/dpkg-query", _commands[^1].Executable);
    }

    /// <summary>
    /// Unrelated packages cannot reach authentication or apt.
    /// </summary>
    [Fact]
    public async Task RejectsAnotherPackage()
    {
        _packageName = "another-app";
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DebianPackageInstaller(RunAsync).InspectAsync(Package()));
        Assert.DoesNotContain(_commands, command => command.Executable.EndsWith("/pkexec"));
    }

    /// <summary>
    /// ARM files are rejected on an AMD64 system before requesting privileges.
    /// </summary>
    [Fact]
    public async Task RejectsWrongArchitecture()
    {
        _architecture = "arm64";
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DebianPackageInstaller(RunAsync).InspectAsync(Package()));
        Assert.Equal(2, _commands.Count);
    }

    /// <summary>
    /// Removed or unpacked installations must not be presented as ready to update.
    /// </summary>
    [Theory]
    [InlineData("deinstall ok config-files")]
    [InlineData("install ok unpacked")]
    public async Task RejectsUnconfiguredInstallation(string status)
    {
        _status = status;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DebianPackageInstaller(RunAsync).InspectAsync(Package()));
    }

    /// <summary>
    /// Debian version ordering decides eligibility instead of string comparison.
    /// </summary>
    [Fact]
    public async Task RejectsSameOrOlderVersion()
    {
        _newer = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DebianPackageInstaller(RunAsync).InspectAsync(Package()));
        Assert.Equal(new[] { "--compare-versions", _packageVersion, "gt", _installedVersion }, _commands[^1].Arguments);
    }

    /// <summary>
    /// A replaced package must be confirmed again before requesting privileges.
    /// </summary>
    [Fact]
    public async Task RevalidatesAfterConfirmation()
    {
        var installer = new DebianPackageInstaller(RunAsync);
        var update = await installer.InspectAsync(Package());
        _packageVersion = "9.0.0";
        await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync(update));
        Assert.DoesNotContain(_commands, command => command.Executable.EndsWith("/pkexec"));
    }

    /// <summary>
    /// Authentication cancellation does not claim installation succeeded.
    /// </summary>
    [Fact]
    public async Task ReportsAuthenticationCancellation()
    {
        var installer = new DebianPackageInstaller(RunAsync);
        var update = await installer.InspectAsync(Package());
        _installExitCode = 126;
        await Assert.ThrowsAsync<OperationCanceledException>(() => installer.InstallAsync(update));
        Assert.Equal("/usr/bin/pkexec", _commands[^1].Executable);
    }

    /// <summary>
    /// Authorization and apt failures stay visible instead of reporting success.
    /// </summary>
    [Theory]
    [InlineData(127)]
    [InlineData(100)]
    public async Task ReportsInstallationFailure(int exitCode)
    {
        var installer = new DebianPackageInstaller(RunAsync);
        var update = await installer.InspectAsync(Package());
        _installExitCode = exitCode;
        await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync(update));
    }

    /// <summary>
    /// A successful process alone is insufficient when the expected version is absent.
    /// </summary>
    [Fact]
    public async Task RequiresExpectedVersionAfterInstall()
    {
        var installer = new DebianPackageInstaller(RunAsync);
        var update = await installer.InspectAsync(Package());
        _verifyInstall = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync(update));
    }

    /// <summary>
    /// Missing files are rejected before any external command is invoked.
    /// </summary>
    [Fact]
    public async Task RejectsMissingPackage()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DebianPackageInstaller(RunAsync).InspectAsync(Path.Combine(_directory, "missing.deb")));
        Assert.Empty(_commands);
    }

    /// <summary>
    /// Creates a selected file whose package metadata is supplied by the command fake.
    /// </summary>
    /// <param name="name">Filename, including any special characters under test.</param>
    /// <returns>The absolute package path.</returns>
    private string Package(string name = "reprise.deb")
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, "package fixture");
        return path;
    }

    /// <summary>
    /// Models package-manager responses while recording privileged invocations.
    /// </summary>
    /// <param name="executable">Command being run.</param>
    /// <param name="arguments">Literal command arguments.</param>
    /// <returns>The configured command result.</returns>
    private Task<PackageCommandResult> RunAsync(string executable, IReadOnlyList<string> arguments)
    {
        _commands.Add((executable, arguments.ToArray()));
        var result = Path.GetFileName(executable) switch
        {
            "dpkg-deb" => new PackageCommandResult(0, $"{_packageName}\n{_packageVersion}\n{_architecture}\n"),
            "dpkg-query" => new PackageCommandResult(0, $"{_status}\n{_installedVersion}"),
            "dpkg" when arguments[0] == "--print-architecture" => new PackageCommandResult(0, "amd64\n"),
            "dpkg" => new PackageCommandResult(_newer ? 0 : 1),
            "pkexec" => new PackageCommandResult(_installExitCode, Error: "package manager diagnostic"),
            _ => throw new InvalidOperationException($"Unexpected executable: {executable}"),
        };
        if (executable.EndsWith("/pkexec") && _installExitCode == 0 && _verifyInstall)
        {
            _installedVersion = _packageVersion;
        }

        return Task.FromResult(result);
    }

    /// <summary>
    /// Removes only this test's selected package files.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }
}
