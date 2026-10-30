namespace Reprise.Platform.Windows.Media;

/// <summary>
/// Normalizes native timeline origins and advances playing tracks between Windows updates.
/// </summary>
internal static class WindowsPlaybackTimeline
{
    /// <summary>
    /// Computes track-relative duration and position while ignoring missing native timestamps.
    /// </summary>
    /// <param name="start">Native timeline origin.</param>
    /// <param name="end">Native end timestamp.</param>
    /// <param name="position">Native position timestamp.</param>
    /// <param name="updated">Time the native timeline was sampled.</param>
    /// <param name="now">Time this snapshot is observed.</param>
    /// <param name="playing">Whether playback is currently advancing.</param>
    /// <param name="rate">Reported playback speed.</param>
    /// <returns>The track duration and clamped elapsed position.</returns>
    internal static (TimeSpan? Duration, TimeSpan Position) Observe(TimeSpan start, TimeSpan end,
        TimeSpan position, DateTimeOffset updated, DateTimeOffset now, bool playing, double rate)
    {
        var duration = end > start ? end - start : (TimeSpan?)null;
        var elapsed = position - start;
        if (playing && updated.Year >= 2000 && updated <= now && double.IsFinite(rate) && rate > 0)
        {
            var advanced = Math.Min((now - updated).Ticks * rate, long.MaxValue - Math.Max(elapsed.Ticks, 0));
            elapsed += TimeSpan.FromTicks((long)advanced);
        }

        return (duration, TimeSpan.FromTicks(Math.Clamp(elapsed.Ticks, 0, duration?.Ticks ?? long.MaxValue)));
    }
}
