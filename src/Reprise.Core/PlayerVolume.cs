namespace Reprise.Core;

/// <summary>
/// Rules for the volume control shared by every panel.
/// </summary>
/// <remarks>
/// The panel works in whole percent because that is what users read off a
/// slider, while <see cref="IMediaSessionService"/> takes a fraction; the
/// conversions and the mute behaviour live here so both directions agree.
/// </remarks>
public static class PlayerVolume
{
    /// <summary>
    /// Lowest level the slider can express.
    /// </summary>
    public const int Minimum = 0;

    /// <summary>
    /// Highest level the slider can express.
    /// </summary>
    public const int Maximum = 100;

    /// <summary>
    /// Level restored by unmuting when nothing better is known.
    /// </summary>
    /// <remarks>
    /// Half volume is loud enough to hear that unmuting worked and quiet
    /// enough not to startle, which matters when the last audible level was
    /// never observed - a player that was already muted when Reprise started.
    /// </remarks>
    public const int DefaultAudibleLevel = 50;

    /// <summary>
    /// Confines a level to the slider's range.
    /// </summary>
    /// <param name="value">Level in percent.</param>
    /// <returns>The level limited to <see cref="Minimum"/>..<see cref="Maximum"/>.</returns>
    /// <example>
    /// <code>
    /// PlayerVolume.Clamp(140); // 100
    /// </code>
    /// </example>
    public static int Clamp(int value) => Math.Clamp(value, Minimum, Maximum);

    /// <summary>
    /// Converts a service-scale fraction into a slider percent.
    /// </summary>
    /// <param name="fraction">Level from 0 to 1, as the service reports it.</param>
    /// <returns>The level in whole percent, clamped to the slider range.</returns>
    /// <example>
    /// <code>
    /// PlayerVolume.FromFraction(0.64); // 64
    /// </code>
    /// </example>
    public static int FromFraction(double fraction)
    {
        if (double.IsNaN(fraction))
        {
            return Minimum;
        }

        return Clamp((int)Math.Round(fraction * Maximum, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// Converts a slider percent into the fraction the service expects.
    /// </summary>
    /// <param name="percent">Level in percent.</param>
    /// <returns>The level from 0 to 1.</returns>
    /// <example>
    /// <code>
    /// PlayerVolume.ToFraction(64); // 0.64
    /// </code>
    /// </example>
    public static double ToFraction(int percent) => Clamp(percent) / (double)Maximum;

    /// <summary>
    /// Chooses the level a mute toggle should move to.
    /// </summary>
    /// <remarks>
    /// Anything audible mutes. From silence, the last audible level is
    /// restored so a toggle round-trips, falling back to
    /// <see cref="DefaultAudibleLevel"/> when that level is unknown or was
    /// itself zero - restoring silence would make the button appear broken.
    /// </remarks>
    /// <param name="current">Level in percent right now.</param>
    /// <param name="lastAudible">
    /// Most recent non-zero level seen, or null when none was observed.
    /// </param>
    /// <returns>The level to apply.</returns>
    /// <example>
    /// <code>
    /// PlayerVolume.MuteToggleTarget(current: 0, lastAudible: 64); // 64
    /// PlayerVolume.MuteToggleTarget(current: 64, lastAudible: 64); // 0
    /// </code>
    /// </example>
    public static int MuteToggleTarget(int current, int? lastAudible)
    {
        if (Clamp(current) != Minimum)
        {
            return Minimum;
        }

        var restored = lastAudible is { } level ? Clamp(level) : DefaultAudibleLevel;
        return restored > Minimum ? restored : DefaultAudibleLevel;
    }
}
