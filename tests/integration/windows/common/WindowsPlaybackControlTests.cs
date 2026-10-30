using Reprise.Core;
using Reprise.Platform.Windows.Media;
using Xunit;

namespace Reprise.Platform.Windows.Tests;

/// <summary>Protects explicit pause requests and bounded fallback for players that reject the generic toggle.</summary>
public sealed class WindowsPlaybackControlTests
{
    /// <summary>A playing session receives pause even if it advertises toggle support, and a successful request is never doubled.</summary>
    [Fact]
    public async Task PrefersExplicitPauseWithoutDoubleToggling()
    {
        var calls = new List<string>();
        Assert.True(await WindowsPlaybackControl.SendAsync(PlaybackCommand.PlayPause, true, true, true, true,
            () => { calls.Add("play"); return Task.FromResult(true); },
            () => { calls.Add("pause"); return Task.FromResult(true); },
            () => { calls.Add("toggle"); return Task.FromResult(false); }));
        Assert.Equal(new[] { "pause" }, calls);
    }

    /// <summary>Rejected explicit requests may use the advertised toggle, but a background pause never starts an idle player.</summary>
    [Fact]
    public async Task UsesToggleOnlyForARejectedTransportRequest()
    {
        var toggles = 0;
        Task<bool> Rejected() => Task.FromResult(false);
        Task<bool> Toggle() { toggles++; return Task.FromResult(true); }
        Assert.True(await WindowsPlaybackControl.SendAsync(PlaybackCommand.PlayPause, true, true, true, true, Rejected, Rejected, Toggle));
        Assert.Equal(1, toggles);
        Assert.True(await WindowsPlaybackControl.SendAsync(PlaybackCommand.Pause, false, true, true, true, Rejected, Rejected, Toggle));
        Assert.Equal(1, toggles);
        Assert.False(await WindowsPlaybackControl.SendAsync(PlaybackCommand.PlayPause, true, false, false, false, Rejected, Rejected, Toggle));
        Assert.Equal(1, toggles);
    }
}
