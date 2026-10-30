using Reprise.Desktop;
using Xunit;

namespace Reprise.Desktop.Tests;

/// <summary>Protects complete lyric starts while preserving fades for genuinely clipped text.</summary>
public sealed class PanelTitleMarqueeTests
{
    /// <summary>Initial rest, stopped scrolling, partial exit, and the next-copy handoff use the correct mask.</summary>
    [Theory]
    [InlineData(0, 200, 1)]
    [InlineData(5, 200, 1)]
    [InlineData(-7, 200, 0.5)]
    [InlineData(-14, 200, 0)]
    [InlineData(-100, 200, 0)]
    [InlineData(-199, 200, 0)]
    [InlineData(-200, 200, 1)]
    [InlineData(-210, 200, 1)]
    public void LeadingFadeDoesNotDimAnUnclippedSentence(double offset, double width, double opacity) =>
        Assert.Equal(opacity, PanelTitleMarquee.LeadingEdgeOpacity(offset, width), 6);
}
