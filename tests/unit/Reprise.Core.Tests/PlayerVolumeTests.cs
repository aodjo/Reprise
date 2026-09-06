using Reprise.Core;
using Xunit;

namespace Reprise.Core.Tests;

/// <summary>
/// Pins the percent conversions and mute behaviour of the volume control.
/// </summary>
public sealed class PlayerVolumeTests
{
    /// <summary>
    /// Fractions round to the nearest whole percent and stay in range.
    /// </summary>
    [Theory]
    [InlineData(0.64, 64)]
    [InlineData(0.645, 65)]
    [InlineData(1.5, 100)]
    [InlineData(-0.2, 0)]
    [InlineData(double.NaN, 0)]
    public void FractionsConvertToPercent(double fraction, int expected)
    {
        Assert.Equal(expected, PlayerVolume.FromFraction(fraction));
    }

    /// <summary>
    /// Percent converts back to a clamped fraction.
    /// </summary>
    [Theory]
    [InlineData(64, 0.64)]
    [InlineData(250, 1.0)]
    [InlineData(-3, 0.0)]
    public void PercentConvertsToFraction(int percent, double expected)
    {
        Assert.Equal(expected, PlayerVolume.ToFraction(percent), precision: 9);
    }

    /// <summary>
    /// Toggling from an audible level mutes; toggling from silence restores.
    /// </summary>
    [Theory]
    [InlineData(64, 64, 0)]
    [InlineData(0, 64, 64)]
    [InlineData(0, null, PlayerVolume.DefaultAudibleLevel)]
    [InlineData(0, 0, PlayerVolume.DefaultAudibleLevel)]
    public void MuteToggleRoundTrips(int current, int? lastAudible, int expected)
    {
        Assert.Equal(expected, PlayerVolume.MuteToggleTarget(current, lastAudible));
    }
}
