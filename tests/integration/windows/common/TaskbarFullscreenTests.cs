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
}
