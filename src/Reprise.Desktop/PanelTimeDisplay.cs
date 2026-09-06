namespace Reprise.Desktop;

/// <summary>
/// Formats the two time labels under the scrubber.
/// </summary>
/// <remarks>
/// Kept identical to the macOS <c>PanelTimeDisplay</c> so a track reads the
/// same on both platforms: minutes are not zero-padded, hours are folded
/// into minutes, and anything non-positive shows as <c>0:00</c>.
/// </remarks>
public static class PanelTimeDisplay
{
    /// <summary>
    /// Text for the left-hand label.
    /// </summary>
    /// <param name="style">Which value the user chose to see.</param>
    /// <param name="position">Current position in the track.</param>
    /// <returns>The elapsed time, or a fixed <c>00:00</c>.</returns>
    /// <example>
    /// <code>
    /// PanelTimeDisplay.LeadingText(PanelLeadingTimeStyle.Elapsed, TimeSpan.FromSeconds(67));
    /// // "1:07"
    /// </code>
    /// </example>
    public static string LeadingText(PanelLeadingTimeStyle style, TimeSpan position) =>
        style switch
        {
            PanelLeadingTimeStyle.Zero => "00:00",
            _ => TimeString(position),
        };

    /// <summary>
    /// Text for the right-hand label.
    /// </summary>
    /// <param name="style">Which value the user chose to see.</param>
    /// <param name="duration">Length of the track.</param>
    /// <param name="remaining">Time left in the track.</param>
    /// <returns>
    /// The remaining time with a leading minus sign, or the total length.
    /// </returns>
    /// <example>
    /// <code>
    /// PanelTimeDisplay.TrailingText(
    ///     PanelTrailingTimeStyle.Remaining,
    ///     TimeSpan.FromSeconds(180),
    ///     TimeSpan.FromSeconds(113));
    /// // "-1:53"
    /// </code>
    /// </example>
    public static string TrailingText(
        PanelTrailingTimeStyle style,
        TimeSpan duration,
        TimeSpan remaining) =>
        style switch
        {
            PanelTrailingTimeStyle.Duration => TimeString(duration),
            _ => "-" + TimeString(remaining),
        };

    /// <summary>
    /// Formats a duration as <c>m:ss</c>.
    /// </summary>
    /// <param name="time">Duration to format.</param>
    /// <returns>
    /// Whole minutes and zero-padded seconds, or <c>0:00</c> for anything
    /// that is not positive.
    /// </returns>
    /// <example>
    /// <code>
    /// PanelTimeDisplay.TimeString(TimeSpan.FromSeconds(3725)); // "62:05"
    /// </code>
    /// </example>
    public static string TimeString(TimeSpan time)
    {
        if (time <= TimeSpan.Zero)
        {
            return "0:00";
        }

        var totalSeconds = (long)Math.Floor(time.TotalSeconds);
        return $"{totalSeconds / 60}:{totalSeconds % 60:00}";
    }
}
