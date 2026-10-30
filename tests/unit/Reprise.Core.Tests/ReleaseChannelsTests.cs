using Xunit;

namespace Reprise.Core.Tests;

/// <summary>
/// Covers semantic and Debian release-stage classification without implicit promotion.
/// </summary>
public sealed class ReleaseChannelsTests
{
    /// <summary>
    /// Version labels select a single isolated channel, including historical package formats.
    /// </summary>
    [Theory]
    [InlineData("2.0.0", "release")]
    [InlineData("2.0.0+20261029010000", "release")]
    [InlineData("2.0.0.20261029010000+20261029010000", "release")]
    [InlineData("1:2.0.0-1", "release")]
    [InlineData("2.0.0~alpha.1+20261029010000", "alpha")]
    [InlineData("2.0.0-alpha.1.20261029010000+abcdef", "alpha")]
    [InlineData("2.0.0~beta.2+20261029010000", "beta")]
    [InlineData("2.0.0-beta.2.20261029010000", "beta")]
    public void ClassifiesVersions(string version, string channel) =>
        Assert.Equal(channel, ReleaseChannels.ForVersion(version));

    /// <summary>
    /// Unsupported prereleases cannot silently become release packages.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("2.0.0-rc.1")]
    [InlineData("2.0.0~dev.1")]
    [InlineData("2.0.0~alphabeta.1")]
    public void RejectsUnknownPrereleases(string version) =>
        Assert.Throws<FormatException>(() => ReleaseChannels.ForVersion(version));

    /// <summary>
    /// A legacy directory is an alias for one specific stage, never for all prereleases.
    /// </summary>
    [Theory]
    [InlineData("stable", "release")]
    [InlineData("develop", "alpha")]
    [InlineData("release", "release")]
    [InlineData("alpha", "alpha")]
    [InlineData("beta", "beta")]
    public void ResolvesLegacyAliases(string channel, string expected) =>
        Assert.Equal(expected, ReleaseChannels.Canonicalize(channel));
}
