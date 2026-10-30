using System.Diagnostics;
using Reprise.Desktop;

namespace Reprise.Platform.Linux;

/// <summary>
/// Installs newer Reprise Debian packages through the desktop's polkit agent.
/// </summary>
internal sealed class DebianPackageInstaller : ILocalPackageInstaller
{
    private readonly Func<string, IReadOnlyList<string>, Task<PackageCommandResult>> _run;

    /// <summary>
    /// Uses system package tools without opening a terminal or launching a shell.
    /// </summary>
    public DebianPackageInstaller() : this(RunAsync)
    {
    }

    /// <summary>
    /// Supplies command execution for package validation and installation tests.
    /// </summary>
    /// <param name="run">Executes an executable with separate, literal arguments.</param>
    internal DebianPackageInstaller(Func<string, IReadOnlyList<string>, Task<PackageCommandResult>> run)
    {
        _run = run;
    }

    /// <inheritdoc />
    public bool IsAvailable => OperatingSystem.IsLinux() &&
        new[] { "pkexec", "apt-get", "dpkg", "dpkg-query", "dpkg-deb" }
            .All(name => File.Exists($"/usr/bin/{name}"));

    /// <inheritdoc />
    public async Task<LocalPackageUpdate> InspectAsync(string path)
    {
        path = Path.GetFullPath(path);
        if (!path.EndsWith(".deb", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            throw new InvalidOperationException("업데이트 파일을 찾지 못했습니다. 업데이트를 다시 확인해 주세요.");
        }

        var details = await _run("/usr/bin/dpkg-deb",
            ["--show", "--showformat=${Package}\n${Version}\n${Architecture}\n", path]);
        var fields = details.Output.TrimEnd().Split('\n');
        if (details.ExitCode != 0 || fields.Length != 3 || fields[0] != "reprise" ||
            string.IsNullOrWhiteSpace(fields[1]))
        {
            throw new InvalidOperationException("다운로드한 파일이 Reprise 업데이트 패키지가 아닙니다. 업데이트를 다시 확인해 주세요.");
        }

        var architecture = await GetArchitectureAsync();
        if (fields[2] != architecture && fields[2] != "all")
        {
            throw new InvalidOperationException("다운로드한 업데이트가 이 컴퓨터에 맞지 않습니다. 업데이트를 다시 확인해 주세요.");
        }

        var installedVersion = await GetInstalledVersionAsync();
        if (!await IsNewerVersionAsync(fields[1], installedVersion))
        {
            throw new InvalidOperationException("이미 같거나 더 최신 버전이 설치되어 있습니다.");
        }

        return new LocalPackageUpdate(path, installedVersion, fields[1]);
    }

    /// <summary>
    /// Reads the native architecture reported by Debian's package manager.
    /// </summary>
    /// <returns>The Debian architecture name.</returns>
    internal async Task<string> GetArchitectureAsync()
    {
        var result = await _run("/usr/bin/dpkg", ["--print-architecture"]);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
        {
            throw new InvalidOperationException("이 컴퓨터의 패키지 종류를 확인하지 못했습니다.");
        }

        return result.Output.Trim();
    }

    /// <summary>
    /// Reads the fully configured Reprise package version.
    /// </summary>
    /// <returns>The installed Debian version, including its build number.</returns>
    internal async Task<string> GetInstalledVersionAsync()
    {
        var result = await _run("/usr/bin/dpkg-query", ["--show", "--showformat=${Status}\n${Version}", "reprise"]);
        var fields = result.Output.TrimEnd().Split('\n');
        if (result.ExitCode != 0 || fields.Length != 2 || fields[0] != "install ok installed" ||
            string.IsNullOrWhiteSpace(fields[1]))
        {
            throw new InvalidOperationException("이 기능은 .deb로 설치한 Reprise를 업데이트할 때 사용할 수 있습니다.");
        }

        return fields[1];
    }

    /// <summary>
    /// Compares package versions using Debian ordering, including epochs and prereleases.
    /// </summary>
    /// <param name="candidate">Potential newer version.</param>
    /// <param name="installed">Version it must exceed.</param>
    /// <returns>True when the candidate is strictly newer.</returns>
    internal async Task<bool> IsNewerVersionAsync(string candidate, string installed)
    {
        var result = await _run("/usr/bin/dpkg", ["--compare-versions", candidate, "gt", installed]);
        if (result.ExitCode is not (0 or 1))
        {
            throw new InvalidOperationException("업데이트 버전 정보를 확인하지 못했습니다. 잠시 후 다시 확인해 주세요.");
        }

        return result.ExitCode == 0;
    }

    /// <inheritdoc />
    public async Task InstallAsync(LocalPackageUpdate update)
    {
        var current = await InspectAsync(update.Path);
        if (current.Version != update.Version || current.InstalledVersion != update.InstalledVersion)
        {
            throw new InvalidOperationException("업데이트 파일 또는 설치된 버전이 변경되었습니다. 업데이트를 다시 확인해 주세요.");
        }

        var result = await _run("/usr/bin/pkexec",
            ["--disable-internal-agent", "/usr/bin/apt-get", "--yes", "--no-remove",
                "-o", "Dpkg::Use-Pty=0", "install", "--", current.Path]);
        if (result.ExitCode == 126)
        {
            throw new OperationCanceledException("설치 인증을 취소했습니다.");
        }

        if (result.ExitCode == 127)
        {
            throw new InvalidOperationException("설치 권한을 얻지 못했습니다. Ubuntu 인증창에서 관리자 인증을 완료해 주세요.");
        }

        if (result.ExitCode != 0)
        {
            var details = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
            throw new InvalidOperationException($"패키지를 설치하지 못했습니다. 다른 업데이트가 진행 중이면 완료 후 다시 시도해 주세요.\n{details.Trim()[..Math.Min(details.Trim().Length, 1500)]}");
        }

        var installed = await _run("/usr/bin/dpkg-query", ["--show", "--showformat=${Status}\n${Version}", "reprise"]);
        if (installed.ExitCode != 0 || installed.Output.TrimEnd() != $"install ok installed\n{current.Version}")
        {
            throw new InvalidOperationException("설치된 버전을 확인하지 못했습니다. 업데이트를 다시 확인해 설치 상태를 확인해 주세요.");
        }
    }

    /// <summary>
    /// Runs a package tool asynchronously, draining both output pipes before returning.
    /// </summary>
    /// <param name="executable">Absolute system executable path.</param>
    /// <param name="arguments">Literal arguments, never interpreted by a shell.</param>
    /// <returns>Exit status, standard output, and diagnostic output.</returns>
    private static async Task<PackageCommandResult> RunAsync(string executable, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Ubuntu 패키지 설치 도구를 실행하지 못했습니다.");
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new PackageCommandResult(process.ExitCode, await output, await error);
    }
}

/// <summary>
/// Captured result of a system package command.
/// </summary>
/// <param name="ExitCode">Process exit status.</param>
/// <param name="Output">Standard output.</param>
/// <param name="Error">Standard error.</param>
internal sealed record PackageCommandResult(int ExitCode, string Output = "", string Error = "");
