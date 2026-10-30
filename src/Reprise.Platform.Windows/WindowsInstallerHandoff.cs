using System.Diagnostics;
using System.Text;

namespace Reprise.Platform.Windows;

/// <summary>
/// Runs installation after the parent releases its executable, then verifies and reopens the app.
/// </summary>
internal static class WindowsInstallerHandoff
{
    /// <summary>
    /// Starts a hidden Windows PowerShell helper without a terminal or execution-policy change.
    /// </summary>
    /// <param name="installer">Verified downloaded setup executable.</param>
    /// <param name="installation">Existing per-user installation to replace.</param>
    /// <param name="version">Expected installed version.</param>
    /// <param name="buildNumber">Expected installed build.</param>
    internal static void Start(string installer, WindowsInstallation installation, string version, string buildNumber)
    {
        var script = BuildScript(installer, installation, version, buildNumber, Environment.ProcessId,
            WindowsInstallation.RegistryPath);
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            @"WindowsPowerShell\v1.0\powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("업데이트 설치 프로그램을 실행하지 못했습니다.");
    }

    /// <summary>
    /// Generates a literal-path helper that never interpolates package paths as PowerShell code.
    /// </summary>
    /// <param name="installer">Setup executable path.</param>
    /// <param name="installation">Installation location and original identity.</param>
    /// <param name="version">Expected incoming version.</param>
    /// <param name="buildNumber">Expected incoming build.</param>
    /// <param name="parentId">Process that must exit before replacement.</param>
    /// <param name="registryPath">Registration key to verify after setup exits.</param>
    /// <returns>Script passed directly through EncodedCommand, never through a shell.</returns>
    internal static string BuildScript(string installer, WindowsInstallation installation, string version,
        string buildNumber, int parentId, string registryPath)
    {
        return $$"""
            $ErrorActionPreference = 'Stop'
            $installer = {{Literal(installer)}}
            $directory = {{Literal(installation.Directory)}}
            try {
                if (Get-Process -Id {{parentId}} -ErrorAction SilentlyContinue) {
                    Wait-Process -Id {{parentId}} -Timeout 120 -ErrorAction Stop
                }
                $setup = Start-Process -FilePath $installer -ArgumentList ('/S /D=' + $directory) -Wait -PassThru
                if ($setup.ExitCode -ne 0) { throw ('Installer exit code: ' + $setup.ExitCode) }
                $installed = Get-ItemProperty -LiteralPath {{Literal("HKCU:\\" + registryPath)}}
                if ($installed.Version -ne {{Literal(version)}} -or $installed.BuildNumber -ne {{Literal(buildNumber)}}) {
                    throw 'The installed version could not be verified.'
                }
                Start-Process -FilePath (Join-Path $directory 'Reprise.exe')
                Remove-Item -LiteralPath (Split-Path -Parent $installer) -Recurse -Force
            } catch {
                Add-Type -AssemblyName System.Windows.Forms
                [System.Windows.Forms.MessageBox]::Show(('업데이트 설치에 실패했습니다. Reprise를 다시 실행한 뒤 다시 시도해 주세요.' + [Environment]::NewLine + $_.Exception.Message), 'Reprise 업데이트') | Out-Null
            }
            """;
    }

    /// <summary>
    /// Quotes a value as a PowerShell single-quoted literal, including embedded apostrophes.
    /// </summary>
    /// <param name="value">Literal path or metadata value.</param>
    /// <returns>A safely quoted string expression.</returns>
    private static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
