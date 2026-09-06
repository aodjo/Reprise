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
    /// A long line is published whole, never cut to fit.
    /// </summary>
    /// <remarks>
    /// The tray label is the one place a title cannot scroll, so shortening
    /// it would mean the user simply never sees the end of the line.
    /// </remarks>
    [Fact]
    public void LongTextIsNeverShortened()
    {
        var title = new string('가', 80);
        var session = Session with { Title = title };

        Assert.Equal(title, MenuBarText.Compose(session, null, new DesktopPreferences(MenuBarLabelLength: 10)));
    }

    /// <summary>
    /// Reserving a width pads the label out and never trims it.
    /// </summary>
    [Fact]
    public void ReserveHoldsAWidthWithoutOverstatingIt()
    {
        Assert.Equal("ab   ", MenuBarText.Reserve("ab", 5));
        Assert.Equal("abcdef", MenuBarText.Reserve("abcdef", 4));
        Assert.Equal("🎵 ", MenuBarText.Reserve("🎵", 2));
    }
}
