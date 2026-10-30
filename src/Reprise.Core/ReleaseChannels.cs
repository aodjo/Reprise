namespace Reprise.Core;

/// <summary>
/// Identifies isolated update channels from application and Debian package versions.
/// </summary>
public static class ReleaseChannels
{
    /// <summary>
    /// Reads the release stage without treating an unknown prerelease as a stable release.
    /// </summary>
    /// <param name="version">An informational or Debian package version.</param>
    /// <returns>Release, alpha, or beta as a lowercase repository channel.</returns>
    /// <exception cref="FormatException">The prerelease stage is unsupported or the version is empty.</exception>
    public static string ForVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new FormatException("An update version is required.");
        }

        var upstream = version.Split('+', 2)[0];
        var marker = upstream.IndexOf('~');
        if (marker < 0)
        {
            marker = upstream.IndexOf('-');
            if (marker >= 0 && marker + 1 < upstream.Length && char.IsAsciiDigit(upstream[marker + 1]))
            {
                return "release";
            }
        }

        if (marker < 0)
        {
            return "release";
        }

        var suffix = upstream[(marker + 1)..];
        foreach (var channel in new[] { "alpha", "beta" })
        {
            if (suffix == channel || suffix.StartsWith(channel + ".", StringComparison.Ordinal))
            {
                return channel;
            }
        }

        throw new FormatException($"Unsupported prerelease stage: {version}");
    }

    /// <summary>
    /// Maps historical repository names to their single release stage.
    /// </summary>
    /// <param name="channel">Canonical name or a historical stable/develop alias.</param>
    /// <returns>The canonical release, alpha, or beta channel.</returns>
    /// <exception cref="ArgumentException">The channel is unknown.</exception>
    public static string Canonicalize(string channel) => channel switch
    {
        "release" or "stable" => "release",
        "alpha" or "develop" => "alpha",
        "beta" => "beta",
        _ => throw new ArgumentException("Unknown update channel.", nameof(channel)),
    };
}
