using Reprise.Desktop;
using Xunit;

namespace Reprise.Desktop.Tests;

/// <summary>
/// Pins the time label formats so they match the macOS panel.
/// </summary>
public sealed class PanelTimeDisplayTests
{
    /// <summary>
    /// Minutes are unpadded, seconds padded, hours folded into minutes.
    /// </summary>
    /// <param name="seconds">Duration in seconds.</param>
    /// <param name="expected">Expected label.</param>
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(-5, "0:00")]
    [InlineData(7, "0:07")]
    [InlineData(67.9, "1:07")]
    [InlineData(3725, "62:05")]
    public void TimeStringMatchesTheMacPanel(double seconds, string expected)
    {
        Assert.Equal(expected, PanelTimeDisplay.TimeString(TimeSpan.FromSeconds(seconds)));
    }

    /// <summary>
    /// The leading label shows elapsed time or a fixed zero.
    /// </summary>
    [Fact]
    public void LeadingLabelHonoursStyle()
    {
        var position = TimeSpan.FromSeconds(67);

        Assert.Equal("1:07", PanelTimeDisplay.LeadingText(PanelLeadingTimeStyle.Elapsed, position));
        Assert.Equal("00:00", PanelTimeDisplay.LeadingText(PanelLeadingTimeStyle.Zero, position));
    }

    /// <summary>
    /// The trailing label shows remaining time with a minus, or the length.
    /// </summary>
    [Fact]
    public void TrailingLabelHonoursStyle()
    {
        var duration = TimeSpan.FromSeconds(180);
        var remaining = TimeSpan.FromSeconds(113);

        Assert.Equal("-1:53", PanelTimeDisplay.TrailingText(PanelTrailingTimeStyle.Remaining, duration, remaining));
        Assert.Equal("3:00", PanelTimeDisplay.TrailingText(PanelTrailingTimeStyle.Duration, duration, remaining));
    }
}
