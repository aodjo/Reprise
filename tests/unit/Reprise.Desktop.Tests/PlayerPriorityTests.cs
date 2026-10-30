using Reprise.Core;
using Reprise.Desktop;
using Xunit;

namespace Reprise.Desktop.Tests;

/// <summary>
/// Pins the stored form and behaviour of the display priority.
/// </summary>
public sealed class PlayerPriorityTests
{
    /// <summary>
    /// A partial or stale list is completed in default order.
    /// </summary>
    [Fact]
    public void ParseCompletesMissingKinds()
    {
        Assert.Equal(
            [PanelPlayerLogo.YouTubeMusic, PanelPlayerLogo.Spotify, PanelPlayerLogo.Melon, PanelPlayerLogo.Generic],
            PlayerPriority.Parse("youTubeMusic"));
        Assert.Equal(PlayerPriority.DefaultOrder, PlayerPriority.Parse(""));
        Assert.Equal(PlayerPriority.DefaultOrder, PlayerPriority.Parse("bogus,spotify"));
    }

    /// <summary>
    /// Serialisation round-trips and uses the macOS-style camel case.
    /// </summary>
    [Fact]
    public void SerializeRoundTrips()
    {
        var order = new[] { PanelPlayerLogo.Generic, PanelPlayerLogo.Spotify };

        var stored = PlayerPriority.Serialize(order);

        Assert.Equal("generic,spotify,youTubeMusic,melon", stored);
        Assert.Equal([PanelPlayerLogo.Generic, PanelPlayerLogo.Spotify, PanelPlayerLogo.YouTubeMusic, PanelPlayerLogo.Melon], PlayerPriority.Parse(stored));
    }

    /// <summary>
    /// Moving a kind reorders the list and clamps the target.
    /// </summary>
    [Fact]
    public void MoveReorders()
    {
        var moved = PlayerPriority.Move(PlayerPriority.DefaultOrder, PanelPlayerLogo.Generic, 0);

        Assert.Equal([PanelPlayerLogo.Generic, PanelPlayerLogo.Spotify, PanelPlayerLogo.YouTubeMusic, PanelPlayerLogo.Melon], moved);
        Assert.Equal(PlayerPriority.DefaultOrder, PlayerPriority.Move(PlayerPriority.DefaultOrder, PanelPlayerLogo.Generic, 9));
    }

    /// <summary>
    /// Sessions are ranked by their kind's position, then by id.
    /// </summary>
    [Fact]
    public void PreferredIdsFollowKindOrder()
    {
        var sessions = new[] { Session("vlc"), Session("spotify"), Session("chromium.instance2"), Session("windows:com.kakaoent.melon.player:1") };
        var order = PlayerPriority.Parse("generic,spotify");

        Assert.Equal(["chromium.instance2", "vlc", "spotify", "windows:com.kakaoent.melon.player:1"], PlayerPriority.PreferredIds(sessions, order));
    }

    /// <summary>Old three-player preferences preserve their order while a separately movable Melon entry is appended.</summary>
    [Fact]
    public void OldPreferencesGainIndependentMelonPriority()
    {
        var old = PlayerPriority.Parse("youTubeMusic,generic,spotify");
        Assert.Equal([PanelPlayerLogo.YouTubeMusic, PanelPlayerLogo.Generic, PanelPlayerLogo.Spotify, PanelPlayerLogo.Melon], old);
        var changed = PlayerPriority.Move(old, PanelPlayerLogo.Melon, 0);
        var sessions = new[] { Session("spotify"), Session("windows:com.kakaoent.melon.player:1"), Session("vlc") };
        Assert.Equal("windows:com.kakaoent.melon.player:1", PlayerPriority.PreferredIds(sessions, changed)[0]);
        Assert.Equal("멜론", PlayerPriority.DisplayName(PanelPlayerLogo.Melon));
        Assert.Equal(changed, PlayerPriority.Parse(PlayerPriority.Serialize(changed)));
    }

    /// <summary>
    /// Builds a paused session for an id.
    /// </summary>
    /// <param name="id">Player id.</param>
    /// <returns>The session.</returns>
    private static MediaSessionSnapshot Session(string id) => new(
        id, id, PlaybackStatus.Paused, "T", "A", "B", TimeSpan.FromSeconds(100), TimeSpan.Zero, 1, null, DateTimeOffset.UtcNow);
}

/// <summary>
/// Covers the XDG autostart entry.
/// </summary>
public sealed class AutostartEntryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "reprise-autostart-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Enabling writes a desktop entry pointing at the executable; disabling removes it.
    /// </summary>
    [Fact]
    public void EnableAndDisableManageTheDesktopFile()
    {
        var path = Path.Combine(_directory, "autostart", "dev.junx.Reprise.desktop");
        var entry = new AutostartEntry(path, "/opt/reprise/app/reprise");

        Assert.True(entry.IsAvailable);
        Assert.False(entry.IsEnabled);
        Assert.Null(entry.SetEnabled(true));
        Assert.True(entry.IsEnabled);
        Assert.Contains("Exec=\"/opt/reprise/app/reprise\"", File.ReadAllText(path));
        Assert.Null(entry.SetEnabled(false));
        Assert.False(File.Exists(path));
    }

    /// <summary>
    /// Without a known executable the entry reports itself unavailable.
    /// </summary>
    [Fact]
    public void UnknownExecutableIsUnavailable()
    {
        var entry = new AutostartEntry(Path.Combine(_directory, "x.desktop"), null);

        Assert.False(entry.IsAvailable);
        Assert.NotNull(entry.SetEnabled(true));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
