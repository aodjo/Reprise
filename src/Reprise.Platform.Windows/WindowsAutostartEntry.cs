using Microsoft.Win32;
using Reprise.Desktop;

namespace Reprise.Platform.Windows;

/// <summary>
/// Registers Reprise for the current user's Windows login without administrator privileges.
/// </summary>
internal sealed class WindowsAutostartEntry : IAutostartEntry
{
    /// <summary>Per-user login startup key.</summary>
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Startup value isolated from other release stages.</summary>
    private static string ValueName => $"Reprise-{Reprise.Core.ReleaseChannels.ForVersion(AppVersion.Current)}";

    /// <inheritdoc />
    public bool IsAvailable => OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(Environment.ProcessPath);

    /// <inheritdoc />
    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string value &&
                    value.Equals($"\"{Environment.ProcessPath}\" --startup", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                return false;
            }
        }
    }

    /// <inheritdoc />
    public string? SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --startup", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }

            return null;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return $"자동 실행을 설정하지 못했습니다: {exception.Message}";
        }
    }
}
