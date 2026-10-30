using Reprise.Desktop;
using Reprise.Platform.Windows.Taskbar;
using Xunit;

namespace Reprise.Platform.Windows.Tests;

/// <summary>Protects taskbar icons, DPI placement, hidden-bar behavior, and lyric state transitions.</summary>
public sealed class TaskbarLyricsTests
{
    /// <summary>Simulates a primary taskbar with a centered task list and right-hand tray.</summary>
    private static TaskbarFrame Frame(uint dpi = 96) => new(new(0, 936, 1728, 984),
        new(1450, 936, 1728, 984), new(500, 936, 1000, 984), new(0, 0, 1728, 984), dpi);

    /// <summary>The strip stays between real app buttons and notification icons.</summary>
    [Fact]
    public void FitsFreeSpaceBeforeTray()
    {
        var frame = Frame();
        var slot = TaskbarLyricsLayout.Place(frame, 20)!.Value;
        Assert.True(slot.Left > frame.TaskList!.Value.Right);
        Assert.True(slot.Right < frame.Tray.Left);
        Assert.InRange(slot.Top, frame.Bar.Top, frame.Bar.Bottom - slot.Height);
        Assert.Equal(frame.Bar.Height, slot.Height);
        Assert.Equal(frame.Bar.Top, slot.Top);
    }

    /// <summary>The left preference uses only a measured gap, falling back to the right when widgets or buttons leave no room.</summary>
    [Fact]
    public void LeftPlacementProtectsWidgetsAndCenteredButtons()
    {
        var frame = Frame() with { LeadingSpace = new(256, 936, 500, 984) };
        var left = TaskbarLyricsLayout.Place(frame, 20, true, 100, false, true)!.Value;
        Assert.Equal(264, left.Left);
        Assert.True(left.Right <= 492);
        var blocked = frame with { LeadingSpace = new(480, 936, 500, 984) };
        Assert.True(TaskbarLyricsLayout.Place(blocked, 20, true, 100, false, true)!.Value.Left > frame.TaskList!.Value.Right);
        var element = new TaskbarElement("DynamicContent1", "TextBlock", false, new(0, 936, 41, 957));
        var geometry = TaskbarAccessibility.Resolve(frame.Bar, [element,
            new("StartButton", "Button", true, new(500, 936, 550, 984)),
            new("SystemTrayFrame", "Grid", false, frame.Tray)]);
        Assert.Equal(256, geometry.LeadingSpace!.Value.Left);
        Assert.Equal(500, geometry.LeadingSpace.Value.Right);
        var complete = TaskbarAccessibility.Resolve(frame.Bar, [
            new("WidgetsButton", "ToggleButton", true, new(6, 936, 158, 984)),
            new("StartButton", "ToggleButton", true, new(500, 936, 550, 984)),
            new("SystemTrayFrame", "Grid", false, frame.Tray)]);
        Assert.Equal(500, complete.TaskList!.Value.Left);
        Assert.Equal(158, complete.LeadingSpace!.Value.Left);
    }

    /// <summary>Horizontal edges pass through intermediate widths and finish without overshoot in either direction.</summary>
    [Fact]
    public void WidthChangesInterpolateInsteadOfJumping()
    {
        var narrow = new TaskbarRect(1300, 936, 1442, 984);
        var wide = new TaskbarRect(1100, 936, 1442, 984);
        foreach (var (from, target) in new[] { (narrow, wide), (wide, narrow) })
        {
            Assert.Equal(from, TaskbarLyricMotion.Rectangle(from, target, TimeSpan.Zero));
            var middle = TaskbarLyricMotion.Rectangle(from, target, TimeSpan.FromMilliseconds(140));
            Assert.InRange(middle.Width, narrow.Width + 20, wide.Width - 20);
            Assert.Equal(1442, middle.Right);
            Assert.Equal(target, TaskbarLyricMotion.Rectangle(from, target, TimeSpan.FromSeconds(1)));
        }
    }

    /// <summary>Insufficient free space and an autohidden bar never cover other shell UI.</summary>
    [Fact]
    public void HidesWhenOccupiedOrAutohidden()
    {
        var frame = Frame();
        Assert.Null(TaskbarLyricsLayout.Place(frame with { TaskList = new(500, 936, 1430, 984) }, 20));
        Assert.Null(TaskbarLyricsLayout.Place(frame with { Bar = new(0, 982, 1728, 1030) }, 20));
        Assert.Null(TaskbarLyricsLayout.Place(frame with { Bar = new(0, 0, 48, 984) }, 20));
    }

    /// <summary>High-DPI and negative desktop coordinates retain correct physical placement.</summary>
    [Fact]
    public void AppliesDpiWithoutAssumingPositiveScreenCoordinates()
    {
        var normal = TaskbarLyricsLayout.Place(Frame(), 10)!.Value;
        var scaled = TaskbarLyricsLayout.Place(Frame(192), 10)!.Value;
        Assert.Equal(normal.Width * 2, scaled.Width);
        var leftMonitor = new TaskbarFrame(new(-1728, 936, 0, 984), new(-280, 936, 0, 984),
            new(-1200, 936, -800, 984), new(-1728, 0, 0, 984), 96);
        var slot = TaskbarLyricsLayout.Place(leftMonitor, 20)!.Value;
        Assert.True(slot.Left >= -800 && slot.Right < -280);
    }

    /// <summary>Windows 11's XAML geometry replaces zero-width legacy shell handles.</summary>
    [Fact]
    public void ResolvesXamlTrayAndRightmostButton()
    {
        TaskbarElement[] elements =
        [
            new("SystemTrayFrame", "Grid", false, new(1450, 936, 1728, 984)),
            new("StartButton", "Button", true, new(500, 936, 550, 984)),
            new("Appid:Player", "TaskListButton", true, new(950, 936, 1000, 984)),
            new("SystemTray.Volume", "Button", true, new(1550, 936, 1600, 984)),
        ];
        var result = TaskbarAccessibility.Resolve(Frame().Bar, elements);
        Assert.Equal(1450, result.Tray!.Value.Left);
        Assert.Equal(1000, result.TaskList!.Value.Right);
    }

    /// <summary>Lyrics replace metadata when available; switching off or losing playback removes the strip.</summary>
    [Fact]
    public void UsesCurrentLyricAndHonorsVisibility()
    {
        var state = new MenuBarState("Song", "Song — Artist", [], true, true, true, 30, 20, "첫 번째 가사", true);
        Assert.Equal("첫 번째 가사", TaskbarLyricsLayout.Text(state));
        Assert.Equal("다음 가사", TaskbarLyricsLayout.Text(state with { LyricText = "다음 가사" }));
        Assert.Equal("Song", TaskbarLyricsLayout.Text(state with { LyricText = null }));
        Assert.Equal("Artist - Song", TaskbarLyricsLayout.Text(state with { LyricText = null, Text = "Artist - Song" }));
        Assert.Empty(TaskbarLyricsLayout.Text(state with { HidesText = true }));
        Assert.Empty(TaskbarLyricsLayout.Text(state with { ShowsLyrics = false }));
        Assert.Empty(TaskbarLyricsLayout.Text(state with { IsRunning = false }));
    }

    /// <summary>A visible cover reserves space without reducing the requested lyric width.</summary>
    [Fact]
    public void ReservesCoverAndGapBeforeTheText()
    {
        var text = TaskbarLyricsLayout.Place(Frame(), 20)!.Value;
        var cover = TaskbarLyricsLayout.Place(Frame(), 20, true)!.Value;
        Assert.Equal(text.Width + 28, cover.Width);
        Assert.Equal(text.Right, cover.Right);
    }

    /// <summary>Unreserved text hugs the tray while reserved and overflowing text keep the configured budget.</summary>
    [Fact]
    public void CompactWidthUsesMeasuredGlyphsAndKeepsTheRightEdge()
    {
        var full = TaskbarLyricsLayout.Place(Frame(), 20, true, 20, true)!.Value;
        var compact = TaskbarLyricsLayout.Place(Frame(), 20, true, 20, false)!.Value;
        var longLine = TaskbarLyricsLayout.Place(Frame(), 20, true, 1000, false)!.Value;
        Assert.Equal(60, compact.Width); // 20 text + 12 padding + 28 cover/gap.
        Assert.Equal(full.Right, compact.Right);
        Assert.Equal(full.Width, longLine.Width);
        var singleGlyph = TaskbarLyricsLayout.Place(Frame(), 20, false, 5, false)!.Value;
        Assert.Equal(17, singleGlyph.Width);
        var scaled = TaskbarLyricsLayout.Place(Frame(192), 20, true, 20, false)!.Value;
        Assert.Equal(compact.Width * 2, scaled.Width);
    }

    /// <summary>The macOS push applies only to a changed lyric within the same track.</summary>
    [Fact]
    public void PushesLyricsWithoutAnimatingTrackChangesOrHiding()
    {
        var first = new MenuBarState("Song", "Song — Artist", [], true, true, true, 30, 20,
            "첫 줄", true, TrackKey: "track-a");
        var next = first with { LyricText = "다음 줄" };
        Assert.True(TaskbarLyricMotion.ShouldPush(first, next));
        Assert.False(TaskbarLyricMotion.ShouldPush(null, first));
        Assert.False(TaskbarLyricMotion.ShouldPush(first, first));
        Assert.False(TaskbarLyricMotion.ShouldPush(first, next with { TrackKey = "track-b" }));
        Assert.False(TaskbarLyricMotion.ShouldPush(first, next with { LyricText = null }));
        Assert.False(TaskbarLyricMotion.ShouldPush(first, next with { ShowsLyrics = false }));
        Assert.False(TaskbarLyricMotion.ShouldPush(first with { IsRunning = false }, next));
    }

    /// <summary>Both layers share a continuous 280 ms ease curve and meet at exactly one viewport height.</summary>
    [Fact]
    public void PushMotionIsContinuousAndEndsAtRest()
    {
        Assert.Equal((0d, 26d), TaskbarLyricMotion.Offsets(TimeSpan.Zero, 26));
        Assert.Equal((-26d, 0d), TaskbarLyricMotion.Offsets(TimeSpan.FromSeconds(1), 26));
        var middle = TaskbarLyricMotion.Offsets(TimeSpan.FromMilliseconds(140), 26);
        Assert.Equal(-13, middle.Outgoing, 3);
        Assert.Equal(13, middle.Incoming, 3);
        var previous = 26d;
        for (var milliseconds = 0; milliseconds <= 280; milliseconds += 10)
        {
            var value = TaskbarLyricMotion.Offsets(TimeSpan.FromMilliseconds(milliseconds), 26);
            Assert.True(value.Incoming <= previous);
            Assert.Equal(26, value.Incoming - value.Outgoing, 6);
            previous = value.Incoming;
        }
    }
}
