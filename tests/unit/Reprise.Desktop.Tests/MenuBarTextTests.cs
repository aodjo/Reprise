using Reprise.Core;
using Reprise.Desktop;
using Xunit;

namespace Reprise.Desktop.Tests;

/// <summary>
/// Pins how the tray label is composed and scrolled.
/// </summary>
public sealed class MenuBarTextTests
{
    private static readonly MediaSessionSnapshot Session = new(
        "spotify", "Spotify", PlaybackStatus.Playing, "Bridge Song", "Bridge Artist", "Bridge Album",
        TimeSpan.FromSeconds(180), TimeSpan.Zero, 1, null, DateTimeOffset.UtcNow);

    /// <summary>
    /// Each format arranges title and artist the macOS way.
    /// </summary>
    /// <param name="format">Format under test.</param>
    /// <param name="expected">Expected text.</param>
    [Theory]
    [InlineData(MenuBarTitleFormat.TitleOnly, "Bridge Song")]
    [InlineData(MenuBarTitleFormat.TitleArtist, "Bridge Song - Bridge Artist")]
    [InlineData(MenuBarTitleFormat.ArtistTitle, "Bridge Artist - Bridge Song")]
    [InlineData(MenuBarTitleFormat.Hidden, "")]
    public void FormatsArrangeTitleAndArtist(MenuBarTitleFormat format, string expected)
    {
        var preferences = new DesktopPreferences(MenuBarTitleFormat: format);

        Assert.Equal(expected, MenuBarText.Compose(Session, null, preferences));
    }

    /// <summary>
    /// An empty part drops out along with its dash.
    /// </summary>
    [Fact]
    public void EmptyPartsAreDropped()
    {
        Assert.Equal("Solo", MenuBarText.FormatTitle(MenuBarTitleFormat.TitleArtist, "Solo", " "));
        Assert.Equal("Band", MenuBarText.FormatTitle(MenuBarTitleFormat.TitleOnly, "", "Band"));
    }

    /// <summary>
    /// Lyrics replace the title only when enabled and present.
    /// </summary>
    [Fact]
    public void LyricsReplaceTitleWhenEnabled()
    {
        Assert.Equal("la la", MenuBarText.Compose(Session, " la la ", new DesktopPreferences(MenuBarShowsLyrics: true)));
        Assert.Equal("Bridge Song", MenuBarText.Compose(Session, "la la", new DesktopPreferences(MenuBarShowsLyrics: false)));
        Assert.Equal("Bridge Song", MenuBarText.Compose(Session, null, new DesktopPreferences(MenuBarShowsLyrics: true)));
        Assert.Equal("", MenuBarText.Compose(null, "la la", new DesktopPreferences(MenuBarShowsLyrics: true)));
    }

    /// <summary>
    /// A short text is returned whole; a long one rests, then steps through a ring.
    /// </summary>
    [Fact]
    public void WindowRestsThenScrolls()
    {
        Assert.Equal("abc", MenuBarText.Window("abc", 4, TimeSpan.FromSeconds(10)));
        Assert.Equal("abcd", MenuBarText.Window("abcdef", 4, TimeSpan.Zero));
        Assert.Equal("abcd", MenuBarText.Window("abcdef", 4, MenuBarText.InitialPause));

        var oneStep = MenuBarText.InitialPause + MenuBarText.StepInterval;
        Assert.Equal("bcde", MenuBarText.Window("abcdef", 4, oneStep));

        var ring = "abcdef" + MenuBarText.ScrollGap;
        var fullCycle = MenuBarText.InitialPause + MenuBarText.StepInterval * ring.Length;
        Assert.Equal("abcd", MenuBarText.Window("abcdef", 4, fullCycle));
        Assert.True(MenuBarText.Scrolls("abcdef", 4));
        Assert.False(MenuBarText.Scrolls("abc", 4));
    }

    /// <summary>
    /// The marquee speed scales the step interval around the normal setting.
    /// </summary>
    [Fact]
    public void StepIntervalScalesWithSpeed()
    {
        Assert.Equal(MenuBarText.StepInterval, MenuBarText.StepIntervalFor(30));
        Assert.True(MenuBarText.StepIntervalFor(45) < MenuBarText.StepInterval);
        Assert.True(MenuBarText.StepIntervalFor(20) > MenuBarText.StepInterval);
        Assert.Equal("bcde", MenuBarText.Window("abcdef", 4, MenuBarText.InitialPause + MenuBarText.StepIntervalFor(45), MenuBarText.StepIntervalFor(45)));
    }

    /// <summary>
    /// Surrogate pairs and combining sequences move as one character.
    /// </summary>
    [Fact]
    public void WindowKeepsTextElementsIntact()
    {
        const string text = "🎵ábcd";

        Assert.Equal("🎵áb", MenuBarText.Window(text, 3, TimeSpan.Zero));
        Assert.Equal("ábc", MenuBarText.Window(text, 3, MenuBarText.InitialPause + MenuBarText.StepInterval));
    }
}
