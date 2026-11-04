namespace Reprise.Platform.Windows.Taskbar;

/// <summary>Public shell notification states used to recognize exclusive games without inspecting their processes.</summary>
internal enum TaskbarNotificationState
{
    /// <summary>The native query failed or has not been performed.</summary>
    Unknown = 0,
    /// <summary>The user is away or the session is locked.</summary>
    NotPresent = 1,
    /// <summary>A fullscreen application or presentation prevents interruptions.</summary>
    Busy = 2,
    /// <summary>An exclusive Direct3D fullscreen application is running.</summary>
    Direct3DFullscreen = 3,
    /// <summary>The user explicitly enabled presentation settings.</summary>
    Presentation = 4,
    /// <summary>The shell currently accepts notifications.</summary>
    AcceptsNotifications = 5,
    /// <summary>The user is in the initial quiet period after setup.</summary>
    QuietTime = 6,
    /// <summary>A Windows Store application is running, without implying fullscreen.</summary>
    App = 7,
}

/// <summary>Distinguishes genuine fullscreen apps from Windows shell surfaces that also span the monitor.</summary>
internal static class TaskbarFullscreenPolicy
{
    /// <summary>Allows only small physical-pixel rounding gaps at fullscreen monitor edges.</summary>
    private const int EdgeTolerance = 2;

    /// <summary>Hides lyrics for a covering app, while keeping them available when Start, Search, or another trusted shell flyout has focus.</summary>
    internal static bool ShouldHide(TaskbarRect monitor, TaskbarRect? bounds, string windowClass,
        string? processPath, string windowsDirectory, TaskbarNotificationState notificationState = TaskbarNotificationState.Unknown,
        bool foregroundOnMonitor = false, bool borderless = false)
    {
        if (windowClass is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        if (IsShellSurface(processPath, windowsDirectory)) return false;
        if (foregroundOnMonitor && (notificationState == TaskbarNotificationState.Direct3DFullscreen ||
            notificationState == TaskbarNotificationState.Busy && borderless)) return true;
        return bounds is { Width: > 0, Height: > 0 } rectangle &&
            (long)rectangle.Left <= (long)monitor.Left + EdgeTolerance &&
            (long)rectangle.Top <= (long)monitor.Top + EdgeTolerance &&
            (long)rectangle.Right >= (long)monitor.Right - EdgeTolerance &&
            (long)rectangle.Bottom >= (long)monitor.Bottom - EdgeTolerance;
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
