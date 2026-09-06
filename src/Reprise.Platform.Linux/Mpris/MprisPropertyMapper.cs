using System.Globalization;
using Reprise.Core;
using Tmds.DBus.Protocol;

namespace Reprise.Platform.Linux.Mpris;

/// <summary>
/// Converts raw MPRIS property values into the shape Reprise.Core expects.
/// </summary>
/// <remarks>
/// D-Bus is loosely typed at the edges: the same field can arrive as a plain
/// value or wrapped in one or more variants, track length can be signed or
/// unsigned, and artist can be a lone string or an array of them. Players
/// also disagree about which properties they publish at all. Everything here
/// is therefore written to degrade rather than throw - a field that is
/// missing, nested unexpectedly, or of the wrong type yields the neutral
/// value for its slot, so one eccentric player cannot take down the whole
/// snapshot.
/// <para>
/// Being pure and free of any bus dependency, this is also where the parsing
/// rules are covered by tests.
/// </para>
/// </remarks>
internal static class MprisPropertyMapper
{
    /// <summary>
    /// Player property naming the transport state, published as a string.
    /// </summary>
    internal const string PlaybackStatusKey = "PlaybackStatus";

    /// <summary>
    /// Player property holding the nested per-track metadata dictionary.
    /// </summary>
    internal const string MetadataKey = "Metadata";

    /// <summary>
    /// Player property holding the elapsed time, in microseconds.
    /// </summary>
    internal const string PositionKey = "Position";

    /// <summary>
    /// Player property holding the volume, nominally from 0 to 1.
    /// </summary>
    internal const string VolumeKey = "Volume";

    /// <summary>
    /// Metadata key for the player's opaque track identifier, an object path.
    /// </summary>
    internal const string TrackIdKey = "mpris:trackid";

    /// <summary>
    /// Track id a player publishes when no track is loaded at all.
    /// </summary>
    /// <remarks>
    /// Reserved by the MPRIS specification. Seeking against it is
    /// meaningless, so it is reported as no track id rather than passed on.
    /// </remarks>
    internal const string NoTrackId = "/org/mpris/MediaPlayer2/TrackList/NoTrack";

    /// <summary>
    /// Metadata key for the track title, from the Xesam ontology.
    /// </summary>
    internal const string TitleKey = "xesam:title";

    /// <summary>
    /// Metadata key for the artist credits, published as a string or an array.
    /// </summary>
    internal const string ArtistKey = "xesam:artist";

    /// <summary>
    /// Metadata key for the album name, from the Xesam ontology.
    /// </summary>
    internal const string AlbumKey = "xesam:album";

    /// <summary>
    /// Metadata key for the track length, in microseconds.
    /// </summary>
    internal const string LengthKey = "mpris:length";

    /// <summary>
    /// Metadata key for the absolute cover art URI.
    /// </summary>
    internal const string ArtUrlKey = "mpris:artUrl";

    /// <summary>
    /// Builds a <see cref="MediaSessionSnapshot"/> from a player's properties.
    /// </summary>
    /// <remarks>
    /// Reads the flat player properties and the nested Metadata dictionary in
    /// one pass, since the two live at different depths but describe the same
    /// moment. The observation timestamp is supplied by the caller rather
    /// than read from the clock here, so every player polled in the same
    /// sweep shares one timestamp and the recency tie-break in
    /// <see cref="ActiveSessionSelector"/> compares like with like.
    /// </remarks>
    /// <param name="playerId">
    /// Identifier for the player, already stripped of its bus-name prefix by
    /// <see cref="ToPlayerId"/>.
    /// </param>
    /// <param name="properties">
    /// Player properties as returned by
    /// <see cref="IMprisBus.GetPlayerPropertiesAsync"/>.
    /// </param>
    /// <param name="observedAt">
    /// Timestamp shared by every player in this polling sweep.
    /// </param>
    /// <returns>
    /// A fully populated snapshot; fields the player did not publish fall
    /// back to empty strings or null.
    /// </returns>
    /// <example>
    /// <code>
    /// var snapshot = MprisPropertyMapper.ToSnapshot(
    ///     "spotify", properties, DateTimeOffset.UtcNow);
    /// Console.WriteLine(snapshot.PlayerName); // "Spotify"
    /// </code>
    /// </example>
    public static MediaSessionSnapshot ToSnapshot(
        string playerId,
        IReadOnlyDictionary<string, VariantValue> properties,
        DateTimeOffset observedAt)
    {
        var metadata = ReadMetadata(properties);

        return new MediaSessionSnapshot(
            PlayerId: playerId,
            PlayerName: FriendlyPlayerName(playerId),
            Status: ParseStatus(ReadString(properties, PlaybackStatusKey)),
            Title: ReadString(metadata, TitleKey),
            Artist: ReadTextList(metadata, ArtistKey),
            Album: ReadString(metadata, AlbumKey),
            Duration: ReadMicroseconds(metadata, LengthKey),
            Position: ReadMicroseconds(properties, PositionKey),
            Volume: ReadVolume(properties, VolumeKey),
            ArtworkUri: ReadArtworkUri(metadata, ArtUrlKey),
            ObservedAt: observedAt);
    }

    /// <summary>
    /// Reduces a bus name to the identifier Reprise uses for a player.
    /// </summary>
    /// <remarks>
    /// Dropping the shared prefix leaves a value that is both short enough to
    /// show and still unique per player instance, since browsers append an
    /// instance suffix of their own.
    /// </remarks>
    /// <param name="serviceName">Fully qualified bus name.</param>
    /// <returns>
    /// The name with the MPRIS prefix removed, or the input unchanged when it
    /// does not carry the prefix.
    /// </returns>
    /// <example>
    /// <code>
    /// MprisPropertyMapper.ToPlayerId("org.mpris.MediaPlayer2.firefox.instance42");
    /// // "firefox.instance42"
    /// </code>
    /// </example>
    internal static string ToPlayerId(string serviceName) =>
        serviceName.StartsWith(DBusMprisBus.ServicePrefix, StringComparison.Ordinal)
            ? serviceName[DBusMprisBus.ServicePrefix.Length..]
            : serviceName;

    /// <summary>
    /// Restores the bus name for a player id, so a command can be addressed.
    /// </summary>
    /// <remarks>
    /// Tolerates an input that already carries the prefix, which keeps
    /// callers from having to know whether a given id has been through
    /// <see cref="ToPlayerId"/>.
    /// </remarks>
    /// <param name="playerId">
    /// Player identifier, with or without the prefix.
    /// </param>
    /// <returns>A fully qualified MPRIS bus name.</returns>
    /// <example>
    /// <code>
    /// MprisPropertyMapper.ToServiceName("spotify");
    /// // "org.mpris.MediaPlayer2.spotify"
    /// </code>
    /// </example>
    internal static string ToServiceName(string playerId) =>
        playerId.StartsWith(DBusMprisBus.ServicePrefix, StringComparison.Ordinal)
            ? playerId
            : DBusMprisBus.ServicePrefix + playerId;

    /// <summary>
    /// Reads the id of the track a player currently has loaded.
    /// </summary>
    /// <remarks>
    /// The specification types this as an object path, but a string is
    /// accepted too because some players publish it that way. The reserved
    /// no-track path counts as absent.
    /// </remarks>
    /// <param name="properties">
    /// Player properties as returned by
    /// <see cref="IMprisBus.GetPlayerPropertiesAsync"/>.
    /// </param>
    /// <returns>
    /// The track id, or null when the player publishes none or has no track
    /// loaded.
    /// </returns>
    /// <example>
    /// <code>
    /// var trackId = MprisPropertyMapper.ReadTrackId(properties);
    /// // "/com/spotify/track/4uLU6hMCjMI75M1A2tKUQC"
    /// </code>
    /// </example>
    internal static string? ReadTrackId(
        IReadOnlyDictionary<string, VariantValue> properties)
    {
        if (!TryUnwrap(ReadMetadata(properties), TrackIdKey, out var value))
        {
            return null;
        }

        var trackId = value.Type switch
        {
            VariantValueType.ObjectPath => value.GetObjectPathAsString(),
            VariantValueType.String => value.GetString(),
            _ => null,
        };

        return string.IsNullOrEmpty(trackId) || trackId == NoTrackId
            ? null
            : trackId;
    }

    /// <summary>
    /// Extracts the nested Metadata dictionary from a player's properties.
    /// </summary>
    /// <param name="properties">The player's properties.</param>
    /// <returns>
    /// Track metadata, or an empty dictionary when the player publishes none.
    /// Returning empty rather than null lets every field reader take the same
    /// code path.
    /// </returns>
    private static IReadOnlyDictionary<string, VariantValue> ReadMetadata(
        IReadOnlyDictionary<string, VariantValue> properties)
    {
        if (!TryUnwrap(properties, MetadataKey, out var value)
            || value.Type != VariantValueType.Dictionary)
        {
            return new Dictionary<string, VariantValue>();
        }

        return value.GetDictionary<string, VariantValue>();
    }

    /// <summary>
    /// Maps the MPRIS PlaybackStatus string onto the Reprise enum.
    /// </summary>
    /// <param name="value">Raw status string, or empty when absent.</param>
    /// <returns>
    /// The matching status, or <see cref="PlaybackStatus.Unknown"/> for
    /// anything outside the three values the specification defines.
    /// </returns>
    private static PlaybackStatus ParseStatus(string value) => value switch
    {
        "Playing" => PlaybackStatus.Playing,
        "Paused" => PlaybackStatus.Paused,
        "Stopped" => PlaybackStatus.Stopped,
        _ => PlaybackStatus.Unknown,
    };

    /// <summary>
    /// Reads a string field, tolerating absence and type mismatches.
    /// </summary>
    /// <param name="source">Dictionary to read from.</param>
    /// <param name="key">Property or metadata key.</param>
    /// <returns>
    /// The string value, or an empty string when the key is missing or holds
    /// something other than a string.
    /// </returns>
    private static string ReadString(
        IReadOnlyDictionary<string, VariantValue> source,
        string key)
    {
        return TryUnwrap(source, key, out var value)
            && value.Type == VariantValueType.String
                ? value.GetString()
                : string.Empty;
    }

    /// <summary>
    /// Reads a field that may hold either one string or an array of them.
    /// </summary>
    /// <remarks>
    /// <c>xesam:artist</c> is specified as a string array, but enough players
    /// publish a bare string that both forms have to be accepted. Arrays are
    /// flattened into one display string, and non-string array entries are
    /// skipped rather than rendered as placeholders.
    /// </remarks>
    /// <param name="source">Dictionary to read from.</param>
    /// <param name="key">Metadata key holding the value.</param>
    /// <returns>
    /// A single value as-is, array entries joined by <c>", "</c>, or an empty
    /// string when the key is missing or of an unusable type.
    /// </returns>
    /// <example>
    /// <code>
    /// // ["Daft Punk", "Julian Casablancas"] becomes:
    /// // "Daft Punk, Julian Casablancas"
    /// </code>
    /// </example>
    private static string ReadTextList(
        IReadOnlyDictionary<string, VariantValue> source,
        string key)
    {
        if (!TryUnwrap(source, key, out var value))
        {
            return string.Empty;
        }

        if (value.Type == VariantValueType.String)
        {
            return value.GetString();
        }

        if (value.Type != VariantValueType.Array)
        {
            return string.Empty;
        }

        var items = new List<string>(value.Count);
        for (var index = 0; index < value.Count; index++)
        {
            var item = Unwrap(value.GetItem(index));
            if (item.Type == VariantValueType.String)
            {
                items.Add(item.GetString());
            }
        }

        return string.Join(", ", items);
    }

    /// <summary>
    /// Reads a microsecond duration into a <see cref="TimeSpan"/>.
    /// </summary>
    /// <remarks>
    /// MPRIS specifies these as signed 64-bit, but players in practice
    /// publish unsigned, 32-bit, and even floating-point values, so all of
    /// them are accepted. A negative result is treated as absent rather than
    /// clamped to zero: some players use -1 to mean "unknown", and reporting
    /// that as a real zero would make a live stream look like a track at its
    /// start.
    /// </remarks>
    /// <param name="source">Dictionary to read from.</param>
    /// <param name="key">Key holding the microsecond count.</param>
    /// <returns>
    /// The duration, or null when the key is missing, holds a non-numeric
    /// type, or carries a negative value.
    /// </returns>
    private static TimeSpan? ReadMicroseconds(
        IReadOnlyDictionary<string, VariantValue> source,
        string key)
    {
        if (!TryUnwrap(source, key, out var value))
        {
            return null;
        }

        var microseconds = value.Type switch
        {
            VariantValueType.Int64 => value.GetInt64(),
            VariantValueType.UInt64 => (long)value.GetUInt64(),
            VariantValueType.Int32 => value.GetInt32(),
            VariantValueType.UInt32 => value.GetUInt32(),
            VariantValueType.Double => (long)value.GetDouble(),
            _ => -1,
        };

        return microseconds >= 0 ? TimeSpan.FromMicroseconds(microseconds) : null;
    }

    /// <summary>
    /// Reads the player volume as a normalised fraction.
    /// </summary>
    /// <remarks>
    /// MPRIS allows values above 1 to mean amplification, which Reprise does
    /// not display, so the result is clamped into the range the UI can
    /// render.
    /// </remarks>
    /// <param name="source">Dictionary to read from.</param>
    /// <param name="key">Key holding the volume.</param>
    /// <returns>
    /// Volume from 0 to 1, or null when the key is missing or holds a
    /// non-numeric type.
    /// </returns>
    private static double? ReadVolume(
        IReadOnlyDictionary<string, VariantValue> source,
        string key)
    {
        if (!TryUnwrap(source, key, out var value))
        {
            return null;
        }

        return value.Type switch
        {
            VariantValueType.Double => Math.Clamp(value.GetDouble(), 0, 1),
            VariantValueType.Int64 => Math.Clamp((double)value.GetInt64(), 0, 1),
            _ => null,
        };
    }

    /// <summary>
    /// Reads the cover art location as an absolute URI.
    /// </summary>
    /// <remarks>
    /// Relative or malformed values are dropped rather than surfaced, since a
    /// URI that cannot be resolved is of no use to the caller. The scheme is
    /// left alone: players publish <c>file:</c>, <c>http:</c>, and
    /// <c>data:</c> alike.
    /// </remarks>
    /// <param name="source">Dictionary to read from.</param>
    /// <param name="key">Key holding the artwork location.</param>
    /// <returns>
    /// The parsed absolute URI, or null when absent or unparsable.
    /// </returns>
    private static Uri? ReadArtworkUri(
        IReadOnlyDictionary<string, VariantValue> source,
        string key)
    {
        return Uri.TryCreate(ReadString(source, key), UriKind.Absolute, out var artworkUri)
            ? artworkUri
            : null;
    }

    /// <summary>
    /// Looks a key up and unwraps the variant nesting around its value.
    /// </summary>
    /// <remarks>
    /// The single entry point every field reader shares, so lookup failure
    /// and variant unwrapping are handled identically everywhere.
    /// </remarks>
    /// <param name="source">Dictionary to read from.</param>
    /// <param name="key">Key to look up.</param>
    /// <param name="value">
    /// Receives the unwrapped value, or <c>default</c> when the lookup fails.
    /// </param>
    /// <returns>True when the key exists and yielded a usable value.</returns>
    private static bool TryUnwrap(
        IReadOnlyDictionary<string, VariantValue> source,
        string key,
        out VariantValue value)
    {
        if (!source.TryGetValue(key, out var raw))
        {
            value = default;
            return false;
        }

        value = Unwrap(raw);
        return value.Type != VariantValueType.Invalid;
    }

    /// <summary>
    /// Strips any layers of variant wrapping from a value.
    /// </summary>
    /// <remarks>
    /// A <c>GetAll</c> reply wraps each property in a variant, and a player
    /// that builds its Metadata dictionary generically may wrap the inner
    /// values a second time. Looping instead of unwrapping once handles both
    /// depths.
    /// </remarks>
    /// <param name="value">Possibly wrapped value.</param>
    /// <returns>The innermost non-variant value.</returns>
    private static VariantValue Unwrap(VariantValue value)
    {
        while (value.Type == VariantValueType.Variant)
        {
            value = value.GetVariantValue();
        }

        return value;
    }

    /// <summary>
    /// Derives a display name from a player id.
    /// </summary>
    /// <remarks>
    /// Bus names are lowercase and may carry an instance suffix, neither of
    /// which belongs in the UI. Dropping everything after the first dot also
    /// collapses a browser's per-tab instances into one recognisable name.
    /// </remarks>
    /// <param name="playerId">
    /// Player id with the bus prefix already removed.
    /// </param>
    /// <returns>A title-cased name suitable for display.</returns>
    /// <example>
    /// <code>
    /// // "firefox.instance42" becomes "Firefox"
    /// </code>
    /// </example>
    private static string FriendlyPlayerName(string playerId)
    {
        var name = playerId.Split('.', 2)[0];
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name);
    }
}
