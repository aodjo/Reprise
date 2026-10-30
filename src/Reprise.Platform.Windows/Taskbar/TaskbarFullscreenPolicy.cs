namespace Reprise.Platform.Windows.Taskbar;

/// <summary>Distinguishes genuine fullscreen apps from Windows shell surfaces that also span the monitor.</summary>
internal static class TaskbarFullscreenPolicy
{
    /// <summary>Hides lyrics for a covering app, while keeping them available when Start, Search, or another trusted shell flyout has focus.</summary>
    internal static bool ShouldHide(TaskbarRect monitor, TaskbarRect bounds, string windowClass,
        string? processPath, string windowsDirectory)
    {
        if (windowClass is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        if (bounds.Left > monitor.Left || bounds.Top > monitor.Top ||
            bounds.Right < monitor.Right || bounds.Bottom < monitor.Bottom) return false;
        return !IsShellSurface(processPath, windowsDirectory);
    }

    /// <summary>Recognizes specific shell hosts only inside the Windows installation, without excluding ordinary fullscreen CoreWindow apps.</summary>
    internal static bool IsShellSurface(string? processPath, string windowsDirectory)
    {
        if (string.IsNullOrWhiteSpace(processPath) || string.IsNullOrWhiteSpace(windowsDirectory)) return false;
        var path = processPath.Replace('\\', '/');
        var root = windowsDirectory.Replace('\\', '/').TrimEnd('/');
        if (!path.StartsWith(root + "/SystemApps/", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith(root + "/System32/", StringComparison.OrdinalIgnoreCase)) return false;
        var name = path[(path.LastIndexOf('/') + 1)..];
        return name.Equals("StartMenuExperienceHost.exe", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("SearchHost.exe", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("SearchApp.exe", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("SearchUI.exe", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("ShellExperienceHost.exe", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("ShellHost.exe", StringComparison.OrdinalIgnoreCase);
    }
}
