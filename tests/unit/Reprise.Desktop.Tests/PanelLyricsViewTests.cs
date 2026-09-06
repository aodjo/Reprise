using Reprise.Desktop;
using Xunit;

namespace Reprise.Desktop.Tests;

/// <summary>
/// Pins the lyric sheet's animation curves to the macOS originals.
/// </summary>
/// <remarks>
/// The scrolling lyrics are the one part of the panel judged purely by how
/// it moves, and a wrong constant here does not fail anything else: the
/// rows still arrive in the right places, just far too quickly and tearing
/// apart on the way. So the curve itself is the thing worth asserting.
/// </remarks>
public sealed class PanelLyricsViewTests
{
    /// <summary>
    /// Tolerance on a curve value, loose enough for the reference figures
    /// below to be quoted to three places.
    /// </summary>
    private const double Tolerance = 0.005;

    /// <summary>
    /// The slide follows SwiftUI's spring of duration 0.56 and bounce 0.24.
    /// </summary>
    /// <remarks>
    /// Values are the analytic response of a spring with a damping ratio of
    /// 0.76 whose period is the full duration, sampled at tenths of the way
    /// through. An earlier curve reached 0.96 by the quarter mark, which is
    /// what made the sheet jump rather than glide.
    /// </remarks>
    [Fact]
    public void SlideMatchesTheMacOsSpring()
    {
        Assert.Equal(0, PanelLyricsView.Spring(0), Tolerance);
        Assert.Equal(0.143, PanelLyricsView.Spring(0.1), Tolerance);
        Assert.Equal(0.539, PanelLyricsView.Spring(0.25), Tolerance);
        Assert.Equal(0.946, PanelLyricsView.Spring(0.5), Tolerance);
        Assert.Equal(1, PanelLyricsView.Spring(1), Tolerance);
    }

    /// <summary>
    /// The slide overshoots slightly and stays settled afterwards.
    /// </summary>
    /// <remarks>
    /// The bounce is what gives the sheet the weight of a physical scroll,
    /// so a curve that never passes 1 has lost it; one that passes it by
    /// much would visibly rebound.
    /// </remarks>
    [Fact]
    public void SlideOvershootsOnceAndSettles()
    {
        var peak = 0.0;
        for (var step = 0; step <= 100; step++)
        {
            peak = Math.Max(peak, PanelLyricsView.Spring(step / 100.0));
        }

        Assert.InRange(peak, 1.01, 1.05);
        Assert.Equal(1, PanelLyricsView.Spring(1.5), Tolerance);
    }

    /// <summary>
    /// The highlight crossfade eases in and out around a straight middle.
    /// </summary>
    [Fact]
    public void CrossfadeEasesAtBothEnds()
    {
        Assert.Equal(0, PanelLyricsView.EaseInOut(0), Tolerance);
        Assert.Equal(0.5, PanelLyricsView.EaseInOut(0.5), Tolerance);
        Assert.Equal(1, PanelLyricsView.EaseInOut(1), Tolerance);
        Assert.True(PanelLyricsView.EaseInOut(0.1) < 0.1);
        Assert.True(PanelLyricsView.EaseInOut(0.9) > 0.9);
    }
}
