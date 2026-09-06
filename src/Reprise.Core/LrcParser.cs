using System.Globalization;
using System.Text.RegularExpressions;

namespace Reprise.Core;

/// <summary>
/// Reads LRC-format lyrics into timed lines.
/// </summary>
/// <remarks>
/// LRC is loosely specified: a line may carry several timestamps, metadata
/// tags sit beside lyric lines, and an <c>[offset:]</c> tag shifts every
/// time. This parser accepts all of that and produces one line per
/// timestamp, sorted, with each line ending where the next begins.
/// </remarks>
public static partial class LrcParser
{
    /// <summary>
    /// Parses LRC text.
    /// </summary>
    /// <param name="lrc">Lyrics in LRC format.</param>
    /// <param name="duration">
    /// Track length, used as the end of the last line. Zero leaves the last
    /// line open-ended.
    /// </param>
    /// <returns>Timed lines in ascending order; empty when nothing parsed.</returns>
    /// <example>
    /// <code>
    /// var lines = LrcParser.Parse("[00:12.00]Hello\n[00:15.50]World", TimeSpan.FromSeconds(20));
    /// // 12.0s "Hello" until 15.5s, 15.5s "World" until 20s
    /// </code>
    /// </example>
    public static IReadOnlyList<LyricLine> Parse(string lrc, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(lrc);

        var offset = TimeSpan.Zero;
        var timed = new List<(TimeSpan Start, string Text)>();
        foreach (var rawLine in lrc.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("[offset:", StringComparison.OrdinalIgnoreCase))
            {
                var closing = line.IndexOf(']', StringComparison.Ordinal);
                if (closing > 8
                    && double.TryParse(line[8..closing], NumberStyles.Float, CultureInfo.InvariantCulture, out var milliseconds))
                {
                    offset = TimeSpan.FromMilliseconds(milliseconds);
                }

                continue;
            }

            var matches = Timestamp().Matches(line);
            if (matches.Count == 0)
            {
                continue;
            }

            var text = Timestamp().Replace(line, string.Empty).Trim();
            if (text.Length == 0)
            {
                continue;
            }

            foreach (Match match in matches)
            {
                var minutes = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                var seconds = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                var start = TimeSpan.FromSeconds(minutes * 60 + seconds) + offset;
                timed.Add((start < TimeSpan.Zero ? TimeSpan.Zero : start, text));
            }
        }

        timed.Sort((left, right) =>
        {
            var byTime = left.Start.CompareTo(right.Start);
            return byTime != 0 ? byTime : string.CompareOrdinal(left.Text, right.Text);
        });

        var lines = new List<LyricLine>(timed.Count);
        for (var index = 0; index < timed.Count; index++)
        {
            TimeSpan? end = index + 1 < timed.Count
                ? timed[index + 1].Start
                : duration > timed[index].Start ? duration : null;
            lines.Add(new LyricLine(timed[index].Start, end, timed[index].Text));
        }

        return lines;
    }

    /// <summary>
    /// Matches one <c>[mm:ss.xx]</c> timestamp.
    /// </summary>
    /// <returns>The compiled pattern.</returns>
    [GeneratedRegex(@"\[(\d{1,3}):(\d{2}(?:\.\d{1,3})?)\]")]
    private static partial Regex Timestamp();
}
