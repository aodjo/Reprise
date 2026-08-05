using System.Globalization;
using Reprise.Core;

namespace Reprise.Platform.Linux.Mpris;

internal static class PlayerctlOutputParser
{
    internal const char FieldSeparator = '\u001f';
    internal const char RecordSeparator = '\u001e';

    public static IReadOnlyList<MediaSessionSnapshot> Parse(
        string output,
        DateTimeOffset observedAt)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var sessions = new List<MediaSessionSnapshot>();
        foreach (var rawRecord in output.Split(
                     RecordSeparator,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = rawRecord
                .Trim('\r', '\n')
                .Split(FieldSeparator);
            if (fields.Length != 9 || string.IsNullOrWhiteSpace(fields[0]))
            {
                continue;
            }

            sessions.Add(new MediaSessionSnapshot(
                PlayerId: fields[0],
                PlayerName: FriendlyPlayerName(fields[0]),
                Status: ParseStatus(fields[1]),
                Title: fields[2],
                Artist: fields[3],
                Album: fields[4],
                Duration: ParseMicroseconds(fields[5]),
                Position: ParseMicroseconds(fields[6]),
                Volume: ParseVolume(fields[7]),
                ArtworkUri: ParseArtworkUri(fields[8]),
                ObservedAt: observedAt));
        }

        return sessions;
    }

    private static PlaybackStatus ParseStatus(string value) => value switch
    {
        "Playing" => PlaybackStatus.Playing,
        "Paused" => PlaybackStatus.Paused,
        "Stopped" => PlaybackStatus.Stopped,
        _ => PlaybackStatus.Unknown,
    };

    private static TimeSpan? ParseMicroseconds(string value)
    {
        return long.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var microseconds)
            && microseconds >= 0
                ? TimeSpan.FromMicroseconds(microseconds)
                : null;
    }

    private static double? ParseVolume(string value)
    {
        return double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var volume)
            ? Math.Clamp(volume, 0, 1)
            : null;
    }

    private static Uri? ParseArtworkUri(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var artworkUri)
            ? artworkUri
            : null;
    }

    private static string FriendlyPlayerName(string playerId)
    {
        var name = playerId.Split('.', 2)[0];
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name);
    }
}
