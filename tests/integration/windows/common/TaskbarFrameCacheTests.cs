using Reprise.Platform.Windows.Taskbar;
using Xunit;

namespace Reprise.Platform.Windows.Tests;

/// <summary>Verifies safe shell-only recovery when Windows temporarily stops reporting taskbar accessibility geometry.</summary>
public sealed class TaskbarFrameCacheTests
{
    /// <summary>Representative measured Windows taskbar geometry.</summary>
    private static TaskbarFrame Frame() => new(new(0, 936, 1728, 984), new(1450, 936, 1728, 984),
        new(500, 936, 1000, 984), new(0, 0, 1728, 984), 96);

    /// <summary>Start can retain the measured tray, while newly occupied task-list space still limits lyric placement.</summary>
    [Fact]
    public void KeepsMeasuredTrayDuringShellOutageAndRespectsCurrentButtons()
    {
        var cache = new TaskbarFrameCache();
        var frame = Frame();
        cache.Remember(123, frame);
        var expanded = new TaskbarRect(500, 936, 1420, 984);
        var recovered = cache.DuringShell(123, frame.Bar, frame.Monitor, frame.Dpi, expanded, true)!;
        Assert.Equal(frame.Tray, recovered.Tray);
        Assert.Equal(expanded, recovered.TaskList);
        Assert.Null(TaskbarLyricsLayout.Place(recovered, 20));
        Assert.Null(cache.DuringShell(123, frame.Bar, frame.Monitor, frame.Dpi, null, false));
        Assert.Equal(frame, cache.DuringShell(123, frame.Bar, frame.Monitor, frame.Dpi, null, true));
    }

    /// <summary>Explorer restarts, taskbar movement, monitor changes, and DPI changes invalidate old coordinates.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void NeverReusesGeometryAfterNativeChanges(int change)
    {
        var cache = new TaskbarFrameCache();
        var frame = Frame();
        cache.Remember(123, frame);
        var handle = change == 0 ? 456 : 123;
        var bar = change == 1 ? new TaskbarRect(0, 0, 1728, 48) : frame.Bar;
        var monitor = change == 2 ? new TaskbarRect(0, 0, 1920, 1080) : frame.Monitor;
        var dpi = change == 3 ? 144u : frame.Dpi;
        Assert.Null(cache.DuringShell(handle, bar, monitor, dpi, null, true));
        Assert.Null(cache.DuringShell(123, frame.Bar, frame.Monitor, frame.Dpi, null, true));
    }

    /// <summary>A hidden or unavailable taskbar cannot resurrect a stale lyric placement.</summary>
    [Fact]
    public void CannotInventGeometryOrReuseClearedFrames()
    {
        var cache = new TaskbarFrameCache();
        var frame = Frame();
        Assert.Null(cache.DuringShell(123, frame.Bar, frame.Monitor, frame.Dpi, null, true));
        cache.Remember(123, frame);
        cache.Clear();
        Assert.Null(cache.DuringShell(123, frame.Bar, frame.Monitor, frame.Dpi, null, true));
    }

    /// <summary>Legacy task-list HWNDs may omit XAML buttons; a shell fallback must not let wide lyrics cover previously measured app icons.</summary>
    [Fact]
    public void NarrowLegacyBoundsCannotExposeOccupiedButtonSpace()
    {
        var cache = new TaskbarFrameCache();
        var frame = Frame();
        cache.Remember(123, frame);
        var recovered = cache.DuringShell(123, frame.Bar, frame.Monitor, frame.Dpi,
            new(500, 936, 800, 984), true)!;
        var wide = TaskbarLyricsLayout.Place(recovered, 60)!.Value;
        Assert.True(wide.Left > frame.TaskList!.Value.Right,
            "Wide lyrics overlap XAML buttons that were visible before the shell provider stopped reporting them.");
    }

    /// <summary>A usable tray alone must not replace measured XAML buttons with missing or smaller partial geometry.</summary>
    [Fact]
    public void PartialShellUpdatesCannotShrinkKnownOccupiedSpace()
    {
        var cache = new TaskbarFrameCache();
        var frame = Frame();
        cache.Resolve(123, frame.Bar, frame.Monitor, frame.Dpi, frame.Tray, frame.TaskList, false);
        Assert.Equal(frame.TaskList, cache.Resolve(123, frame.Bar, frame.Monitor, frame.Dpi, frame.Tray, null, true)!.TaskList);
        Assert.Equal(frame.TaskList, cache.Resolve(123, frame.Bar, frame.Monitor, frame.Dpi, frame.Tray, new(500, 936, 800, 984), true)!.TaskList);
        var expanded = new TaskbarRect(500, 936, 1200, 984);
        cache.Resolve(123, frame.Bar, frame.Monitor, frame.Dpi, frame.Tray, expanded, true);
        Assert.Equal(expanded, cache.Resolve(123, frame.Bar, frame.Monitor, frame.Dpi, null, null, true)!.TaskList);
        Assert.Equal(frame.TaskList, cache.Resolve(123, frame.Bar, frame.Monitor, frame.Dpi, frame.Tray, frame.TaskList, false)!.TaskList);
    }

    /// <summary>Expanding notification icons remain protected until normal complete measurements resume.</summary>
    [Fact]
    public void ShellTrayGrowthRemainsReservedAcrossLaterPartialUpdates()
    {
        var cache = new TaskbarFrameCache();
        var frame = Frame();
        cache.Remember(123, frame);
        var expanded = frame.Tray with { Left = 1350 };
        Assert.Equal(expanded, cache.Resolve(123, frame.Bar, frame.Monitor, frame.Dpi, expanded, null, true)!.Tray);
        Assert.Equal(expanded, cache.Resolve(123, frame.Bar, frame.Monitor, frame.Dpi, null, null, true)!.Tray);
    }

    /// <summary>Partial cold-start data cannot invent safe positions before any complete desktop frame has been measured.</summary>
    [Fact]
    public void WaitsForCompleteGeometryBeforeFirstPlacement()
    {
        var cache = new TaskbarFrameCache();
        var frame = Frame();
        Assert.Null(cache.Resolve(123, frame.Bar, frame.Monitor, frame.Dpi, frame.Tray, null, false));
        Assert.Null(cache.Resolve(123, frame.Bar, frame.Monitor, frame.Dpi, frame.Tray, null, true));
        Assert.Equal(frame, cache.Resolve(123, frame.Bar, frame.Monitor, frame.Dpi, frame.Tray, frame.TaskList, false));
    }
}
