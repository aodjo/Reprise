using Reprise.Platform.Windows.Taskbar;
using Xunit;

namespace Reprise.Platform.Windows.Tests;

/// <summary>Protects Start-menu visibility without placing taskbar lyrics over genuine fullscreen applications.</summary>
public sealed class TaskbarFullscreenTests
{
    /// <summary>A full-monitor Windows shell host is not a fullscreen game or video.</summary>
    [Theory]
    [InlineData(@"C:\Windows\SystemApps\Microsoft.Windows.StartMenuExperienceHost_cw5n1h2txyewy\StartMenuExperienceHost.exe")]
    [InlineData(@"C:\WINDOWS\SystemApps\MicrosoftWindows.Client.CBS_cw5n1h2txyewy\SearchHost.EXE")]
    [InlineData(@"C:\Windows\SystemApps\Microsoft.Windows.Search_cw5n1h2txyewy\SearchApp.exe")]
    [InlineData(@"C:\Windows\SystemApps\Microsoft.Windows.Cortana_cw5n1h2txyewy\SearchUI.exe")]
    [InlineData(@"C:\Windows\SystemApps\ShellExperienceHost_cw5n1h2txyewy\ShellExperienceHost.exe")]
    [InlineData(@"C:\Windows\System32\ShellHost.exe")]
    public void KeepsLyricsWhileShellHasFocus(string path)
    {
        var monitor = new TaskbarRect(0, 0, 1728, 984);
        Assert.False(TaskbarFullscreenPolicy.ShouldHide(monitor, monitor, "Windows.UI.Core.CoreWindow", path, @"C:\Windows"));
    }

    /// <summary>CoreWindow video players, games, and similarly named non-system programs still suppress the strip.</summary>
    [Theory]
    [InlineData(@"C:\Program Files\WindowsApps\Microsoft.ZuneVideo\Video.UI.exe")]
    [InlineData(@"C:\Games\Player.exe")]
    [InlineData(@"C:\Apps\StartMenuExperienceHost.exe")]
    [InlineData(@"C:\WindowsBackup\SystemApps\ShellHost.exe")]
    [InlineData(@"C:\Windows\SystemAppsOther\ShellHost.exe")]
    [InlineData(null)]
    public void HidesForGenuineFullscreenApps(string? path)
    {
        var monitor = new TaskbarRect(-1728, 0, 0, 984);
        Assert.True(TaskbarFullscreenPolicy.ShouldHide(monitor, monitor, "Windows.UI.Core.CoreWindow", path, @"C:\Windows"));
    }

    /// <summary>Ordinary maximized windows and the desktop are not mistaken for fullscreen media.</summary>
    [Fact]
    public void LeavesNormalDesktopAndMaximizedAppsVisible()
    {
        var monitor = new TaskbarRect(0, 0, 1728, 984);
        Assert.False(TaskbarFullscreenPolicy.ShouldHide(monitor, new(0, 0, 1728, 936), "Chrome_WidgetWin_1", @"C:\Apps\chrome.exe", @"C:\Windows"));
        Assert.False(TaskbarFullscreenPolicy.ShouldHide(monitor, monitor, "WorkerW", null, @"C:\Windows"));
        Assert.False(TaskbarFullscreenPolicy.ShouldHide(monitor, monitor, "Shell_SecondaryTrayWnd", null, @"C:\Windows"));
    }

    /// <summary>The actual Windows installation is used instead of a hard-coded drive or directory.</summary>
    [Fact]
    public void HonorsTheWindowsInstallationDirectory()
    {
        var monitor = new TaskbarRect(0, 0, 1728, 984);
        Assert.False(TaskbarFullscreenPolicy.ShouldHide(monitor, monitor, "Windows.UI.Core.CoreWindow", @"D:\WINNT\SystemApps\Start\StartMenuExperienceHost.exe", @"D:\WINNT"));
        Assert.True(TaskbarFullscreenPolicy.ShouldHide(monitor, monitor, "Windows.UI.Core.CoreWindow", @"C:\Windows\SystemApps\Start\StartMenuExperienceHost.exe", @"D:\WINNT"));
    }

    /// <summary>Exclusive games hide lyrics even when their reported bounds use a different resolution or cannot be queried.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HidesExclusiveGameWithIncompleteWindowGeometry(bool readableBounds)
    {
        var monitor = new TaskbarRect(0, 0, 2560, 1440);
        TaskbarRect? bounds = readableBounds ? new(0, 0, 1920, 1080) : null;
        Assert.True(TaskbarFullscreenPolicy.ShouldHide(monitor, bounds, "UnrealWindow", null, @"C:\Windows",
            TaskbarNotificationState.Direct3DFullscreen, foregroundOnMonitor: true));
        Assert.False(TaskbarFullscreenPolicy.ShouldHide(monitor, bounds, "UnrealWindow", null, @"C:\Windows",
            TaskbarNotificationState.Direct3DFullscreen, foregroundOnMonitor: false));
    }

    /// <summary>The shell's generic busy state supports borderless games but cannot hide ordinary framed applications.</summary>
    [Fact]
    public void RequiresBorderlessForegroundForGenericBusyState()
    {
        var monitor = new TaskbarRect(-2560, -1440, 0, 0);
        var bounds = new TaskbarRect(-2560, -1440, -640, -360);
        Assert.True(TaskbarFullscreenPolicy.ShouldHide(monitor, bounds, "UnrealWindow", null, @"C:\Windows",
            TaskbarNotificationState.Busy, foregroundOnMonitor: true, borderless: true));
        Assert.False(TaskbarFullscreenPolicy.ShouldHide(monitor, bounds, "UnrealWindow", null, @"C:\Windows",
            TaskbarNotificationState.Busy, foregroundOnMonitor: true, borderless: false));
        Assert.False(TaskbarFullscreenPolicy.ShouldHide(monitor, bounds, "UnrealWindow", null, @"C:\Windows",
            TaskbarNotificationState.Busy, foregroundOnMonitor: false, borderless: true));
    }

    /// <summary>Unrelated notification modes never turn a normal window into fullscreen content.</summary>
    [Theory]
    [InlineData((int)TaskbarNotificationState.Unknown)]
    [InlineData((int)TaskbarNotificationState.NotPresent)]
    [InlineData((int)TaskbarNotificationState.AcceptsNotifications)]
    [InlineData((int)TaskbarNotificationState.QuietTime)]
    [InlineData((int)TaskbarNotificationState.App)]
    [InlineData((int)TaskbarNotificationState.Presentation)]
    public void LeavesOrdinaryWindowsVisibleForUnrelatedNotificationStates(int state)
    {
        Assert.False(TaskbarFullscreenPolicy.ShouldHide(new(0, 0, 2560, 1440), new TaskbarRect(0, 0, 1280, 720),
            "Chrome_WidgetWin_1", null, @"C:\Windows", (TaskbarNotificationState)state, foregroundOnMonitor: true, borderless: true));
    }

    /// <summary>Opening Start over a game and returning to the desktop preserve existing shell visibility behavior.</summary>
    [Theory]
    [InlineData((int)TaskbarNotificationState.Busy)]
    [InlineData((int)TaskbarNotificationState.Direct3DFullscreen)]
    public void PreservesShellExceptionsWhenFullscreenStateIsReported(int state)
    {
        var monitor = new TaskbarRect(0, 0, 2560, 1440);
        Assert.False(TaskbarFullscreenPolicy.ShouldHide(monitor, monitor, "Windows.UI.Core.CoreWindow",
            @"C:\Windows\SystemApps\Start\StartMenuExperienceHost.exe", @"C:\Windows", (TaskbarNotificationState)state, true, true));
        Assert.False(TaskbarFullscreenPolicy.ShouldHide(monitor, monitor, "WorkerW", null, @"C:\Windows", (TaskbarNotificationState)state, true, true));
    }

    /// <summary>Physical rounding gaps are accepted on negative-coordinate monitors without treating maximized work areas as fullscreen.</summary>
    [Fact]
    public void AcceptsOnlySmallMonitorEdgeGaps()
    {
        var monitor = new TaskbarRect(-2560, -1440, 0, 0);
        Assert.True(TaskbarFullscreenPolicy.ShouldHide(monitor, new TaskbarRect(-2559, -1439, -1, -1), "UnrealWindow", null, @"C:\Windows"));
        Assert.False(TaskbarFullscreenPolicy.ShouldHide(monitor, new TaskbarRect(-2560, -1440, 0, -48), "UnrealWindow", null, @"C:\Windows"));
        Assert.False(TaskbarFullscreenPolicy.ShouldHide(monitor, new TaskbarRect(-2557, -1440, 0, 0), "UnrealWindow", null, @"C:\Windows"));
        Assert.False(TaskbarFullscreenPolicy.ShouldHide(monitor, null, "UnrealWindow", null, @"C:\Windows"));
    }
}
